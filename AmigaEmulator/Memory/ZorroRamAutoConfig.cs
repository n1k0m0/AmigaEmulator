using System;
using System.IO;

namespace AmigaEmulator.Core.Memory
{
    /// <summary>One Zorro II memory board, registered with expansion.library via nibble-wide Autoconfig.</summary>
    public sealed class ZorroRamAutoConfig
    {
        private readonly int _size;
        private readonly byte[] _rom = new byte[16];
        private bool _configured;
        private byte _lowBase;
        public uint BaseAddress { get; private set; } = MemoryMap.FastRamStart;
        public bool Configured => _configured;
        public ZorroRamAutoConfig(int size)
        {
            _size = size;
            int code = size switch { 1024 * 1024 => 5, 2 * 1024 * 1024 => 6, 4 * 1024 * 1024 => 7, _ => 0 };
            _rom[0] = (byte)(0xE0 | code); _rom[1] = 1; _rom[2] = 0x80;
            _rom[4] = 0x07; _rom[5] = 0xDB; _rom[9] = 1; // emulator memory board ID
        }
        public bool Maps(uint address) => _size != 0 && !_configured && address >= 0xE80000 && address < 0xE90000;
        public void Reset() { _configured = false; _lowBase = 0; BaseAddress = MemoryMap.FastRamStart; }
        public void MarkLegacyConfigured() { Reset(); _configured = true; }
        public byte ReadByte(uint address)
        {
            int offset = (int)(address & 0xFFFF);
            if (offset == 0x40 || offset == 0x42) return 0;
            if (offset >= 64 || (offset & 1) != 0) return 0xFF;
            byte value = _rom[offset / 4];
            byte nibble = (offset & 2) == 0 ? (byte)(value & 0xF0) : (byte)(value << 4);
            return offset < 4 ? nibble : (byte)~nibble;
        }
        public void WriteByte(uint address, byte value)
        {
            int offset = (int)(address & 0xFFFF);
            if (offset == 0x4A) _lowBase = (byte)(value >> 4);
            if (offset == 0x48)
            {
                uint candidate = (uint)((value & 0xF0) | _lowBase) << 16;
                if (candidate >= MemoryMap.FastRamStart && candidate + _size <= MemoryMap.FastRamEnd + 1u)
                { BaseAddress = candidate; _configured = true; }
            }
            if (offset == 0x4C) _configured = true;
        }
        public void SaveState(BinaryWriter writer) { writer.Write(_configured); writer.Write(_lowBase); writer.Write(BaseAddress); }
        public void LoadState(BinaryReader reader)
        {
            _configured = reader.ReadBoolean(); _lowBase = reader.ReadByte(); BaseAddress = reader.ReadUInt32();
            if (BaseAddress < MemoryMap.FastRamStart || BaseAddress + _size > MemoryMap.FastRamEnd + 1u)
                throw new InvalidDataException("Invalid Zorro memory base.");
        }
    }
}
