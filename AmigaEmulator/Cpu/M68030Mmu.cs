// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;

namespace AmigaEmulator.Core.Cpu
{
    /// <summary>Independent 68030 translation-table engine for A3000 bring-up.
    /// CPU integration uses approximate short fault frames; full pipeline restart is pending.
    /// ATC replacement uses fixed slots and one history bit per entry (MC68030UM 9.4).
    /// Descriptor formats: Motorola MC68030UM, sections 9.5 and 9.7.</summary>
    public sealed class M68030Mmu
    {
        public const ushort BusError = 0x8000, Limit = 0x4000, SupervisorOnly = 0x2000,
            WriteProtected = 0x0800, Invalid = 0x0400, Modified = 0x0200, Transparent = 0x0040;
        // Internal translation attribute, absent from the architected MMUSR.
        public const ushort CacheInhibit = 0x1000;
        private readonly Func<uint, uint> _read;
        private readonly Action<uint, uint> _write;
        private readonly Dictionary<(uint page, int fc), Translation> _atc = new();
        private const int AtcCapacity = 22;
        private const uint AllHistoryBits = (1u << AtcCapacity) - 1;
        private readonly (uint page, int fc)?[] _slots = new (uint, int)?[AtcCapacity];
        private readonly Dictionary<(uint page, int fc), int> _slotByKey = new();
        private uint _history;
        public uint TranslationControl { get; private set; }
        public ulong CpuRoot { get; private set; }
        public ulong SupervisorRoot { get; private set; }
        public uint Transparent0 { get; private set; }
        public uint Transparent1 { get; private set; }
        public ushort Status { get; set; }
        private int PageBits => (int)((TranslationControl >> 20) & 15);
        public readonly record struct Translation(uint Physical, ushort Flags, uint Descriptor)
        {
            public bool Accessible(bool write) => (Flags & (BusError | Limit | SupervisorOnly | Invalid)) == 0 && (!write || (Flags & WriteProtected) == 0);
        }

        public M68030Mmu(Func<uint, uint> readPhysical, Action<uint, uint> writePhysical)
        { _read = readPhysical ?? throw new ArgumentNullException(nameof(readPhysical)); _write = writePhysical ?? throw new ArgumentNullException(nameof(writePhysical)); }

        /// <returns>False requests the CPU's MMU configuration exception (vector 56).</returns>
        public bool SetTranslationControl(uint value, bool flush = true)
        {
            TranslationControl = value & 0x83FFFFFFu;
            bool valid = IsValidControl(value);
            if (!valid) TranslationControl &= 0x7FFFFFFF;
            if (flush) Flush(); return valid;
        }
        private static bool IsValidControl(uint value)
        {
            if ((value & 0x80000000) == 0) return true;
            int pageBits = (int)((value >> 20) & 15), bits = pageBits + (int)((value >> 16) & 15);
            for (int shift = 12; shift >= 0; shift -= 4)
            { int width = (int)((value >> shift) & 15); if (width == 0) break; bits += width; }
            return pageBits >= 8 && bits == 32;
        }
        public bool SetRoot(bool supervisor, ulong value, bool flush = true)
        {
            value &= 0xFFFF0003FFFFFFF0UL;
            if (supervisor) SupervisorRoot = value; else CpuRoot = value;
            if (flush) Flush(); return ((value >> 32) & 3) != 0;
        }
        public void SetTransparent(int index, uint value, bool flush = true)
        {
            if (index != 0 && index != 1) throw new ArgumentOutOfRangeException(nameof(index));
            if (index == 0) Transparent0 = value & 0xFFFF8777; else Transparent1 = value & 0xFFFF8777;
            if (flush) Flush();
        }
        public void Reset()
        {
            TranslationControl &= 0x7FFFFFFF; Transparent0 &= 0xFFFF7FFF; Transparent1 &= 0xFFFF7FFF;
            // External RESET disables translation without flushing the ATC.
        }
        public void Flush(int functionCode = 0, int functionMask = 0, uint? address = null)
        {
            uint page = address.GetValueOrDefault() >> PageBits;
            var remove = new List<(uint page, int fc)>();
            foreach (var key in _atc.Keys)
                if (((key.fc ^ functionCode) & functionMask & 7) == 0 && (!address.HasValue || key.page == page)) remove.Add(key);
            foreach (var key in remove)
            {
                _slots[_slotByKey[key]] = null;
                _slotByKey.Remove(key); _atc.Remove(key);
            }
            // Flush invalidates tags, not the internal slot order/history.
        }

        public Translation Translate(uint logical, int functionCode, bool write, bool updateDescriptors = true)
        {
            functionCode &= 7;
            if (functionCode == 7) return new(logical, Transparent, 0);
            bool match0 = Matches(Transparent0, logical, functionCode, !write);
            bool match1 = Matches(Transparent1, logical, functionCode, !write);
            if (match0 || match1)
            {
                // Both windows participate in CIOUT. A broad cacheable TT0
                // must not suppress a narrower inhibited TT1 (or vice versa).
                bool inhibit = (match0 && (Transparent0 & 0x400) != 0) ||
                    (match1 && (Transparent1 & 0x400) != 0);
                return new(logical, (ushort)(Transparent | (inhibit ? CacheInhibit : 0)), 0);
            }
            if ((TranslationControl & 0x80000000) == 0) return new(logical, 0, 0);
            uint mask = (1u << PageBits) - 1;
            var key = (logical >> PageBits, functionCode);
            if (_atc.TryGetValue(key, out var cached) && (!write || (cached.Flags & Modified) != 0 || !cached.Accessible(true)))
            {
                if (updateDescriptors) Touch(_slotByKey[key]);
                return cached with { Physical = cached.Physical | (logical & mask) };
            }
            var result = Walk(logical, functionCode, write, updateDescriptors);
            if (updateDescriptors)
            {
                StoreTranslation(key, result with { Physical = result.Physical & ~mask });
            }
            return result;
        }
        private void Touch(int slot)
        {
            uint bit = 1u << slot;
            _history |= bit;
            // Starting a new epoch preserves the access that exhausted the
            // zero bits. This is history-based pseudo-LRU, not a FIFO/LRU queue.
            if (_history == AllHistoryBits) _history = bit;
        }
        private void StoreTranslation((uint page, int fc) key, Translation translation)
        {
            if (!_slotByKey.TryGetValue(key, out int slot))
            {
                slot = 0;
                while (slot < AtcCapacity && _slots[slot].HasValue) slot++;
                if (slot == AtcCapacity)
                {
                    slot = 0;
                    while ((_history & (1u << slot)) != 0) slot++;
                    var evicted = _slots[slot].Value;
                    _atc.Remove(evicted); _slotByKey.Remove(evicted);
                }
                _slots[slot] = key; _slotByKey.Add(key, slot);
            }
            _atc[key] = translation;
            Touch(slot);
        }
        public bool TryGetCachedTranslation(uint logical, int functionCode, out Translation translation)
        {
            translation = default;
            return (TranslationControl & 0x80000000) != 0 &&
                !Matches(Transparent0, logical, functionCode & 7, true) && !Matches(Transparent1, logical, functionCode & 7, true) &&
                _atc.TryGetValue((logical >> PageBits, functionCode & 7), out translation);
        }
        public Translation Test(uint logical, int functionCode, bool write, bool cachedOnly)
            => Test(logical, functionCode, write, cachedOnly ? 0 : 7);
        public Translation Test(uint logical, int functionCode, bool write, int level)
        {
            if (level < 0 || level > 7) throw new ArgumentOutOfRangeException(nameof(level));
            Translation result;
            if (level == 0)
            {
                if (Matches(Transparent0, logical, functionCode & 7, !write) || Matches(Transparent1, logical, functionCode & 7, !write)) result = new(logical, Transparent, 0);
                else if (_atc.TryGetValue((logical >> PageBits, functionCode & 7), out var entry)) result = entry with { Flags = AtcStatus(entry.Flags) };
                else result = new(0, Invalid, 0);
            }
            else result = Walk(logical, functionCode & 7, write, false, level);
            result = result with { Flags = (ushort)(result.Flags & ~CacheInhibit) };
            Status = result.Flags; return result;
        }
        private static ushort AtcStatus(ushort tableFlags)
        {
            // Keep table-search diagnostics internally (and in legacy saves),
            // but level-zero PTEST reports the ATC's B/WP/M fields. Every
            // invalid descriptor, limit/supervisor violation or physical table
            // fault latches B; table depth and L/S do not exist in the ATC.
            ushort flags = (ushort)(tableFlags & (WriteProtected | Modified));
            if ((tableFlags & (BusError | Limit | SupervisorOnly | Invalid)) != 0)
                flags |= BusError | Invalid;
            return flags;
        }
        public Translation Load(uint logical, int functionCode, bool write)
        {
            functionCode &= 7;
            Flush(functionCode, 7, logical);
            var result = Walk(logical, functionCode, write, true);
            var key = (logical >> PageBits, functionCode);
            StoreTranslation(key, result with { Physical = result.Physical & ~((1u << PageBits) - 1) });
            return result;
        }
        private Translation Walk(uint logical, int fc, bool write, bool update, int maxLevel = 7)
        {
            ulong root = (fc & 4) != 0 && (TranslationControl & 0x02000000) != 0 ? SupervisorRoot : CpuRoot;
            uint descriptor = (uint)(root >> 32), target = (uint)root, descriptorAddress = 0;
            bool longDescriptor = true, rootDescriptor = true, functionLevel = (TranslationControl & 0x01000000) != 0;
            ushort accrued = 0; int shift = 32 - (int)((TranslationControl >> 16) & 15), level = 0, field = 12;
            bool indirectFollowed = false;
            // Four address levels, optional FC level and a single indirect page descriptor.
            for (int step = 0; step < 7; step++)
            {
                int type = (int)(descriptor & 3);
                int width = functionLevel ? 3 : field >= 0 ? (int)((TranslationControl >> field) & 15) : 0;
                if (type == 0) return new(0, (ushort)(accrued | Invalid | level), descriptorAddress);
                bool indirect = type != 1 && !functionLevel && width == 0;
                if (indirect && indirectFollowed) return new(0, (ushort)(accrued | Invalid | level), descriptorAddress);
                if (!rootDescriptor && !indirect)
                {
                    if ((descriptor & 4) != 0) accrued |= WriteProtected;
                    if (longDescriptor && (descriptor & 0x100) != 0 && (fc & 4) == 0)
                        return new(0, (ushort)(accrued | SupervisorOnly | Invalid | level), descriptorAddress);
                    if (type == 1 && (descriptor & 16) != 0) accrued |= Modified;
                    if (update)
                    {
                        uint changed = descriptor | 8u;
                        if (type == 1 && write && (accrued & WriteProtected) == 0) { changed |= 16; accrued |= Modified; }
                        try { if (changed != descriptor) _write(descriptorAddress, changed); }
                        catch (Exception ex) when (ex is IOException || ex is ArgumentOutOfRangeException)
                        { return new(0, (ushort)(accrued | BusError | Invalid | level), descriptorAddress); }
                    }
                }
                uint index = functionLevel ? (uint)fc : width != 0 ? (logical >> (shift - width)) & ((1u << width) - 1) : 0;
                if (longDescriptor && !functionLevel && width != 0)
                {
                    uint limit = descriptor >> 16 & 0x7FFF;
                    if ((descriptor & 0x80000000) != 0 ? index < limit : index > limit)
                        return new(0, (ushort)(accrued | Limit | Invalid | level), descriptorAddress);
                }
                if (type == 1)
                {
                    if (!rootDescriptor && (descriptor & 0x40) != 0) accrued |= CacheInhibit;
                    uint physical = rootDescriptor ? unchecked(target + logical) : width != 0 ? unchecked(target + (logical & ((1u << shift) - 1)))
                        : (target & ~((1u << PageBits) - 1)) | (logical & ((1u << PageBits) - 1));
                    return new(physical, (ushort)(accrued | level), descriptorAddress);
                }
                if (level == maxLevel) return new(0, (ushort)(accrued | level), descriptorAddress);
                descriptorAddress = indirect ? target & 0xFFFFFFFCu : unchecked((target & 0xFFFFFFF0u) + index * (type == 3 ? 8u : 4u));
                try
                {
                    descriptor = _read(descriptorAddress); longDescriptor = type == 3;
                    int nextField = functionLevel ? field : field - 4;
                    bool nextIsPage = nextField < 0 || ((TranslationControl >> nextField) & 15) == 0;
                    target = longDescriptor ? _read(descriptorAddress + 4) : descriptor & ((descriptor & 3) == 1 ? 0xFFFFFF00u : nextIsPage ? 0xFFFFFFFCu : 0xFFFFFFF0u);
                }
                catch (Exception ex) when (ex is IOException || ex is ArgumentOutOfRangeException)
                { return new(0, (ushort)(accrued | BusError | Invalid | level), descriptorAddress); }
                rootDescriptor = false; level++;
                if (indirect) indirectFollowed = true;
                if (functionLevel) functionLevel = false;
                else if (!indirect) { shift -= width; field -= 4; }
            }
            return new(0, (ushort)(accrued | Invalid | level), descriptorAddress);
        }
        private static bool Matches(uint control, uint address, int fc, bool read) =>
            (control & 0x8000) != 0 && ((((address >> 24) ^ (control >> 24)) & (~(control >> 16) & 255)) == 0) &&
            (((fc ^ (int)(control >> 4)) & (int)(~control & 7)) == 0) && ((control & 0x100) != 0 || read == ((control & 0x200) != 0));

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(2); writer.Write(TranslationControl); writer.Write(CpuRoot); writer.Write(SupervisorRoot);
            writer.Write(Transparent0); writer.Write(Transparent1); writer.Write(Status); writer.Write(_atc.Count); writer.Write(_history);
            for (int slot = 0; slot < AtcCapacity; slot++)
            {
                if (!_slots[slot].HasValue) continue;
                var key = _slots[slot].Value; var entry = _atc[key];
                writer.Write((byte)slot); writer.Write(key.page); writer.Write(key.fc);
                writer.Write(entry.Physical); writer.Write(entry.Flags); writer.Write(entry.Descriptor);
            }
        }
        public void LoadState(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version != 1 && version != 2) throw new InvalidDataException("Unsupported MMU state.");
            uint tc = reader.ReadUInt32(); ulong crp = reader.ReadUInt64(), srp = reader.ReadUInt64();
            uint tt0 = reader.ReadUInt32(), tt1 = reader.ReadUInt32(); ushort status = reader.ReadUInt16(); int count = reader.ReadInt32();
            if (count < 0 || count > 22 || !IsValidControl(tc) || (tc & ~0x83FFFFFFu) != 0 ||
                (tt0 & ~0xFFFF8777u) != 0 || (tt1 & ~0xFFFF8777u) != 0 ||
                (crp & ~0xFFFF0003FFFFFFF0UL) != 0 || (srp & ~0xFFFF0003FFFFFFF0UL) != 0)
                throw new InvalidDataException("Invalid MMU configuration/ATC size.");
            uint history = version == 2 ? reader.ReadUInt32() : 0;
            if ((history & ~AllHistoryBits) != 0 || history == AllHistoryBits)
                throw new InvalidDataException("Invalid ATC history bits.");
            var entries = new Dictionary<(uint, int), Translation>();
            var slots = new (uint, int)?[AtcCapacity]; var slotByKey = new Dictionary<(uint, int), int>();
            for (int i = 0; i < count; i++)
            {
                int slot = version == 2 ? reader.ReadByte() : i;
                var key = (reader.ReadUInt32(), reader.ReadInt32()); var entry = new Translation(reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadUInt32());
                if (slot >= AtcCapacity || slots[slot].HasValue) throw new InvalidDataException("Invalid ATC slot.");
                if (key.Item2 < 0 || key.Item2 > 7 || !entries.TryAdd(key, entry)) throw new InvalidDataException("Invalid ATC entry.");
                slots[slot] = key; slotByKey.Add(key, slot);
            }
            TranslationControl = tc; CpuRoot = crp; SupervisorRoot = srp; Transparent0 = tt0; Transparent1 = tt1; Status = status;
            _atc.Clear(); _slotByKey.Clear(); Array.Copy(slots, _slots, AtcCapacity); _history = history;
            foreach (var entry in entries) { _atc.Add(entry.Key, entry.Value); _slotByKey.Add(entry.Key, slotByKey[entry.Key]); }
        }
    }
}
