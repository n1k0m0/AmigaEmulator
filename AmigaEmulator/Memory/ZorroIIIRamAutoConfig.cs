// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;

namespace AmigaEmulator.Core.Memory
{
    /// <summary>An opt-in 32-bit Zorro III RAM card with native Autoconfig.</summary>
    public sealed class ZorroIIIRamAutoConfig
    {
        public const uint ConfigStart = 0xFF000000u;
        private readonly byte[] _ram;
        private readonly byte[] _rom = new byte[16];
        private bool _configured;
        private byte _highBase;
        public int Size => _ram.Length;
        public bool Configured => _configured;
        public uint BaseAddress { get; private set; }
        public long RamReads { get; private set; }
        public long RamWrites { get; private set; }
        public uint LastRamReadAddress { get; private set; }
        public uint LastRamWriteAddress { get; private set; }
        public long ConfigReads { get; private set; }
        public long ConfigWrites { get; private set; }
        public long Assignments { get; private set; }
        public long Shutups { get; private set; }
        public uint LastConfigWriteOffset { get; private set; }
        public byte LastConfigWriteValue { get; private set; }
        public byte HighWriteValue { get; private set; }
        public byte LowWriteValue { get; private set; }

        public ZorroIIIRamAutoConfig(int size)
        {
            if (size != 0 && size != 8 * 1024 * 1024 && size != 16 * 1024 * 1024 && size != 32 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(size));
            _ram = new byte[size];
            if (size == 0) return;
            // Zorro III, auto-linked memory, with size extension for 16/32 MiB.
            _rom[0] = (byte)(size == 32 * 1024 * 1024 ? 0xA1 : 0xA0);
            _rom[1] = 1; // Emulator development board product.
            // Explicit 8 MiB sub-size prevents the OS linking the minimum 16 MiB Z3 aperture.
            _rom[2] = (byte)(size == 8 * 1024 * 1024 ? 0x9A : 0xB0);
            _rom[4] = 0x07; _rom[5] = 0xDB; _rom[9] = 1;
        }

        public bool MapsConfig(uint address) => Size != 0 && !_configured &&
            address >= ConfigStart && address < ConfigStart + 0x10000u;
        public bool MapsRam(uint address) => Size != 0 && _configured && BaseAddress != 0 &&
            address >= BaseAddress && address - BaseAddress < (uint)Size;

        public byte ReadConfig(uint address)
        {
            ConfigReads++;
            return PeekConfig(address);
        }

        public byte PeekConfig(uint address)
        {
            uint offset = address - ConfigStart;
            if (offset == 0x40u || offset == 0x140u) return 0;
            if (offset >= 0x200u || (offset & 3u) != 0) return 0xFF;
            int register = (int)((offset & 0xFFu) / 4);
            if (register >= _rom.Length) return 0xFF;
            byte nibble = (offset & 0x100u) == 0 ? (byte)(_rom[register] & 0xF0) : (byte)(_rom[register] << 4);
            return register == 0 ? nibble : (byte)~nibble;
        }

        public void WriteConfigByte(uint address, byte value)
        {
            uint offset = address - ConfigStart;
            ConfigWrites++; LastConfigWriteOffset = offset; LastConfigWriteValue = value;
            if (offset == 0x48u) { _highBase = value; LowWriteValue = value; }
            else if (offset == 0x44u) { HighWriteValue = value; Assign(value, _highBase); }
            else if (offset == 0x4Cu) { Shutups++; _configured = true; BaseAddress = 0; }
        }

        public void WriteConfigWord(uint address, ushort value)
        {
            uint offset = address - ConfigStart;
            ConfigWrites++; LastConfigWriteOffset = offset; LastConfigWriteValue = (byte)(value >> 8);
            if (offset == 0x48u) { _highBase = (byte)(value >> 8); LowWriteValue = _highBase; }
            else if (offset == 0x44u) { HighWriteValue = (byte)(value >> 8); Assign(HighWriteValue, (byte)value); }
            else if (offset == 0x4Cu) { Shutups++; _configured = true; BaseAddress = 0; }
        }

        private void Assign(byte high, byte low)
        {
            uint candidate = ((uint)high << 24) | ((uint)low << 16);
            if (candidate < 0x10000000u || candidate > 0x80000000u - (uint)Size ||
                candidate % (uint)Size != 0) return;
            BaseAddress = candidate;
            _configured = true;
            Assignments++;
        }

        public byte ReadRam(uint address)
        {
            RamReads++; LastRamReadAddress = address;
            return _ram[address - BaseAddress];
        }
        public byte PeekRam(uint address) => _ram[address - BaseAddress];
        public void WriteRam(uint address, byte value)
        {
            RamWrites++; LastRamWriteAddress = address;
            _ram[address - BaseAddress] = value;
        }
        public void ClearRam() => Array.Clear(_ram, 0, _ram.Length);
        public void Reset() { _configured = false; _highBase = 0; BaseAddress = 0; }

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_configured); writer.Write(_highBase); writer.Write(BaseAddress);
            writer.Write(_ram);
        }
        public void LoadState(BinaryReader reader)
        {
            bool configured = reader.ReadBoolean(); byte low = reader.ReadByte(); uint address = reader.ReadUInt32();
            if (configured && address != 0 && (address < 0x10000000u || address > 0x80000000u - (uint)Size ||
                address % (uint)Size != 0)) throw new InvalidDataException("Invalid Zorro III base address.");
            if (!configured && address != 0) throw new InvalidDataException("Unconfigured Zorro III board has a base address.");
            byte[] data = reader.ReadBytes(_ram.Length);
            if (data.Length != _ram.Length) throw new EndOfStreamException();
            data.CopyTo(_ram, 0);
            _configured = configured; _highBase = low; BaseAddress = address;
        }
    }
}
