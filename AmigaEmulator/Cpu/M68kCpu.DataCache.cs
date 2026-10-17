// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;

namespace AmigaEmulator.Core.Cpu
{
    public sealed partial class M68kCpu
    {
        // The 030 data cache uses all three FC bits, unlike the instruction
        // cache. DMA and MMU table updates never snoop these logical entries.
        [NonSerialized] private readonly uint[] _dataCacheTags = new uint[16];
        [NonSerialized] private readonly byte[] _dataCacheValid = new byte[16];
        [NonSerialized] private readonly uint[] _dataCacheData = new uint[64];
        [NonSerialized] private bool _dataCacheEnabled, _cacheAccessInProgress;

        private void ClearDataCache() => Array.Clear(_dataCacheValid);
        private void ClearDataCacheEntry()
        {
            int entry = (int)(_cacheAddress >> 2) & 63;
            _dataCacheValid[entry >> 2] &= (byte)~(1 << (entry & 3));
        }

        private int DataFunctionCode => _functionCodeOverride >= 0 ? _functionCodeOverride : IsSupervisor() ? 5 : 1;

        private bool DataAccessCacheable(uint logical, int fc, bool write, int size)
        {
            uint physical = logical;
            bool cacheable = fc != 7;
            if (_mmu != null)
            {
                bool needsWrite = write || _locked030Access;
                var translation = _mmu.Translate(logical, fc, needsWrite);
                if (!translation.Accessible(needsWrite))
                    throw new MmuAccessException(logical, fc, write, size, _locked030Access);
                physical = translation.Physical;
                cacheable &= (translation.Flags & M68030Mmu.CacheInhibit) == 0;
            }
            if (_cpuModel == AmigaCpuModel.M68EC030)
                cacheable &= !(((_accessControl0 & 0x400) != 0 && MatchesAccessControl(_accessControl0, logical, fc, !write)) ||
                    ((_accessControl1 & 0x400) != 0 && MatchesAccessControl(_accessControl1, logical, fc, !write)));
            // Board device apertures are inhibited on both directions. They
            // cannot supply a complete memory longword or accept cache hits.
            if (_bus is IM68kCacheBus cacheBus) cacheable &= cacheBus.IsDataCacheable(physical & ~3u);
            return cacheable;
        }

        private uint ReadCachedData(uint address, int size)
        {
            int fc = DataFunctionCode;
            uint result = 0;
            Precheck030CachedSplitOperand(address, size, false);
            _cacheAccessInProgress = true;
            try
            {
                // A misaligned operand can hit one longword and miss the next.
                // Cross-page translations were checked before either transfer.
                for (int remaining = size; remaining > 0;)
                {
                    int offset = (int)(address & 3), count = Math.Min(remaining, 4 - offset);
                    int entry = (int)(address >> 2) & 63, line = entry >> 2;
                    byte valid = (byte)(1 << (entry & 3));
                    uint tag = (address & 0xFFFFFF00u) | (uint)fc;
                    bool hit = fc != 7 && _dataCacheTags[line] == tag && (_dataCacheValid[line] & valid) != 0;
                    uint part;
                    if (hit && !_locked030Access)
                    {
                        if (_mmu != null && _mmu.TryGetCachedTranslation(address, fc, out var cached) && !cached.Accessible(false))
                            throw new MmuAccessException(address, fc, false, size);
                        part = (_dataCacheData[entry] >> ((4 - offset - count) * 8)) & DataMask(count);
                    }
                    else if (DataAccessCacheable(address, fc, false, size) && (_cacheControl & 0x200) == 0)
                    {
                        // No CBACK is modeled: request exactly one longword,
                        // through normal deferred/contended bus operations.
                        uint data = ReadLong(address & ~3u);
                        if (_dataCacheTags[line] != tag) _dataCacheValid[line] = 0;
                        _dataCacheTags[line] = tag; _dataCacheData[entry] = data; _dataCacheValid[line] |= valid;
                        part = (data >> ((4 - offset - count) * 8)) & DataMask(count);
                    }
                    else part = ReadDataChunk(address, count);
                    result = count == 4 ? part : (result << (count * 8)) | part;
                    address = unchecked(address + (uint)count); remaining -= count;
                }
                return result;
            }
            finally { _cacheAccessInProgress = false; }
        }

        private void WriteCachedData(uint address, int size, uint value)
        {
            int fc = DataFunctionCode;
            bool alignedLong = size == 4 && (address & 3) == 0;
            Precheck030CachedSplitOperand(address, size, true);
            _cacheAccessInProgress = true;
            try
            {
                for (int remaining = size; remaining > 0;)
                {
                    int offset = (int)(address & 3), count = Math.Min(remaining, 4 - offset);
                    int entry = (int)(address >> 2) & 63, line = entry >> 2;
                    byte valid = (byte)(1 << (entry & 3));
                    uint tag = (address & 0xFFFFFF00u) | (uint)fc;
                    uint part = (value >> ((remaining - count) * 8)) & DataMask(count);
                    bool cacheable;
                    try { cacheable = DataAccessCacheable(address, fc, true, size); }
                    catch (MmuAccessException)
                    {
                        if (_dataCacheTags[line] == tag) _dataCacheValid[line] &= (byte)~valid;
                        throw;
                    }
                    if (cacheable)
                    {
                        bool hit = _dataCacheTags[line] == tag && (_dataCacheValid[line] & valid) != 0;
                        if (hit)
                        {
                            int shift = (4 - offset - count) * 8;
                            uint mask = DataMask(count) << shift;
                            // Writethrough hits update even while frozen, before
                            // an external write is retired (Motorola 6.1.2).
                            _dataCacheData[entry] = (_dataCacheData[entry] & ~mask) | (part << shift);
                        }
                        else if ((_cacheControl & 0x2200) == 0x2000)
                        {
                            if (alignedLong)
                            {
                                if (_dataCacheTags[line] != tag) _dataCacheValid[line] = 0;
                                _dataCacheTags[line] = tag; _dataCacheData[entry] = part; _dataCacheValid[line] |= valid;
                            }
                            else
                            {
                                // Partial tag misses invalidate the indexed
                                // longword(s), preserving the old tag/siblings.
                                _dataCacheValid[line] &= (byte)~valid;
                            }
                        }
                    }
                    WriteDataChunk(address, count, part);
                    address = unchecked(address + (uint)count); remaining -= count;
                }
            }
            finally { _cacheAccessInProgress = false; }
        }

        private static uint DataMask(int count) => count == 4 ? uint.MaxValue : (1u << (count * 8)) - 1;

        private void Precheck030CachedSplitOperand(uint address, int size, bool write)
        {
            if (size == 1 || _mmu == null) return;
            uint tc = _mmu.TranslationControl;
            if ((tc & 0x80000000) == 0) return;
            int pageBits = (int)((tc >> 20) & 15);
            uint lastAddress = unchecked(address + (uint)(size - 1)) & AddressMask;
            if ((address >> pageBits) != (lastAddress >> pageBits))
            {
                // The cache may fill a line or update a writethrough hit before
                // the next part faults. Probe all logical bus pieces first.
                Probe030MovemOperand(address, size, write);
            }
        }

        private uint ReadDataChunk(uint address, int count) => count switch
        {
            1 => ReadByte(address), 2 => ReadWord(address), 4 => ReadLong(address),
            _ => ((uint)ReadWord(address) << 8) | ReadByte(unchecked(address + 2))
        };
        private void WriteDataChunk(uint address, int count, uint value)
        {
            if (count == 1) WriteByte(address, (byte)value);
            else if (count == 2) WriteWord(address, (ushort)value);
            else if (count == 4) WriteLong(address, value);
            else { WriteWord(address, (ushort)(value >> 8)); WriteByte(unchecked(address + 2), (byte)value); }
        }

        private void SaveDataCache(BinaryWriter writer)
        {
            for (int i = 0; i < 16; i++) { writer.Write(_dataCacheTags[i]); writer.Write(_dataCacheValid[i]); }
            foreach (uint data in _dataCacheData) writer.Write(data);
        }
        private void LoadDataCache(BinaryReader reader)
        {
            var tags = new uint[16]; var valid = new byte[16]; var data = new uint[64];
            for (int i = 0; i < 16; i++)
            {
                tags[i] = reader.ReadUInt32(); valid[i] = reader.ReadByte();
                if ((tags[i] & 0xF8) != 0 || valid[i] > 15) throw new InvalidDataException("Invalid data cache state.");
            }
            for (int i = 0; i < 64; i++) data[i] = reader.ReadUInt32();
            tags.CopyTo(_dataCacheTags, 0); valid.CopyTo(_dataCacheValid, 0); data.CopyTo(_dataCacheData, 0);
        }
    }
}
