// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;

namespace AmigaEmulator.Core.Cpu
{
    public sealed partial class M68kCpu
    {
        // 020: 64 independently tagged longwords. 030: 16 shared tags,
        // with four independently valid longwords per line. Both are logical
        // instruction caches; FC2 distinguishes supervisor and user code.
        [NonSerialized] private readonly uint[] _instructionCacheTags = new uint[64];
        [NonSerialized] private readonly byte[] _instructionCacheValid = new byte[64];
        [NonSerialized] private readonly uint[] _instructionCacheData = new uint[64];
        private bool Has030Cache => _cpuModel == AmigaCpuModel.M68030 || _cpuModel == AmigaCpuModel.M68EC030;

        private void ClearInstructionCache() => Array.Clear(_instructionCacheValid);
        private void ClearInstructionCacheEntry()
        {
            int entry = (int)(_cacheAddress >> 2) & 63;
            if (Has030Cache) _instructionCacheValid[entry >> 2] &= (byte)~(1 << (entry & 3));
            else _instructionCacheValid[entry] = 0;
        }

        private ushort FetchCachedInstructionWord()
        {
            uint address = _state.PC & AddressMask;
            if ((address & 1) != 0)
            { Enter020AddressError(address); throw new AddressErrorException(); }
            int fc = IsSupervisor() ? 6 : 2;
            int entry = (int)(address >> 2) & 63;
            int line = Has030Cache ? entry >> 2 : entry;
            byte valid = (byte)(Has030Cache ? 1 << (entry & 3) : 1);
            uint tag = (address & 0xFFFFFF00u) | (uint)(fc & 4);
            uint data;
            if (_instructionCacheTags[line] == tag && (_instructionCacheValid[line] & valid) != 0)
            {
                // A resident ATC fault still aborts a cache hit. An ATC miss
                // does not walk tables or consult a newly changed CI mapping.
                if (_mmu != null && _mmu.TryGetCachedTranslation(address, fc, out var cached) && !cached.Accessible(false))
                    throw new MmuAccessException(address, fc, false, 2);
                data = _instructionCacheData[entry];
            }
            else
            {
                uint aligned = address & ~3u;
                int previousFc = _functionCodeOverride;
                _functionCodeOverride = fc;
                try
                {
                    // The modeled bus does not acknowledge 030 burst requests.
                    // A miss fills one aligned longword, never speculative I/O.
                    data = ((uint)ReadWord(aligned) << 16) | ReadWord(unchecked(aligned + 2));
                    uint physical = aligned;
                    bool cacheable = true;
                    if (_mmu != null)
                    {
                        var translation = _mmu.Translate(aligned, fc, false);
                        physical = translation.Physical;
                        cacheable = (translation.Flags & M68030Mmu.CacheInhibit) == 0;
                    }
                    if (_cpuModel == AmigaCpuModel.M68EC030)
                        cacheable &= !(((_accessControl0 & 0x400) != 0 && MatchesAccessControl(_accessControl0, aligned, fc, true)) ||
                            ((_accessControl1 & 0x400) != 0 && MatchesAccessControl(_accessControl1, aligned, fc, true)));
                    if (_bus is IM68kCacheBus cacheBus) cacheable &= cacheBus.IsInstructionCacheable(physical);
                    if ((_cacheControl & 2) == 0 && cacheable)
                    {
                        if (_instructionCacheTags[line] != tag) _instructionCacheValid[line] = 0;
                        _instructionCacheTags[line] = tag;
                        _instructionCacheData[entry] = data;
                        _instructionCacheValid[line] |= valid;
                    }
                }
                catch (MmuAccessException)
                {
                    _instructionCacheValid[line] = 0;
                    throw;
                }
                finally { _functionCodeOverride = previousFc; }
            }
            _state.PC = unchecked(_state.PC + 2) & AddressMask;
            // Cache hits do not generate external bus micro-operations.
            return (ushort)((address & 2) == 0 ? data >> 16 : data);
        }

        private void SaveInstructionCache(BinaryWriter writer)
        {
            for (int i = 0; i < 64; i++)
            { writer.Write(_instructionCacheTags[i]); writer.Write(_instructionCacheValid[i]); writer.Write(_instructionCacheData[i]); }
        }
        private void LoadInstructionCache(BinaryReader reader)
        {
            var tags = new uint[64]; var valid = new byte[64]; var data = new uint[64];
            for (int i = 0; i < 64; i++)
            {
                tags[i] = reader.ReadUInt32(); valid[i] = reader.ReadByte(); data[i] = reader.ReadUInt32();
                if ((tags[i] & 0xFBu) != 0 || valid[i] > (Has030Cache && i < 16 ? 15 : Has030Cache ? 0 : 1))
                    throw new InvalidDataException("Invalid instruction cache state.");
            }
            tags.CopyTo(_instructionCacheTags, 0); valid.CopyTo(_instructionCacheValid, 0); data.CopyTo(_instructionCacheData, 0);
        }
    }
}
