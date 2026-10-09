using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AmigaEmulator.Core.Media
{
    /// <summary>A600 memory-card socket. SRAM common memory and read-only CIS
    /// attribute memory use separate apertures; no ATA or network card is implied.</summary>
    public sealed class GaylePcmciaController
    {
        private byte[] _memory = Array.Empty<byte>();
        private byte[] _cis = Array.Empty<byte>();
        private byte _statusControl, _statusMask, _changes, _interruptEnable, _configuration;
        private bool _reset, _planning;
        private readonly bool _commonMemoryAvailable;
        public bool Enabled { get; }
        public bool ReadOnly { get; private set; }
        public int Size => _memory.Length;
        public bool Inserted => Size != 0;
        private bool Accessible => Enabled && Inserted && (_statusControl & 1) == 0;
        public byte Status => (byte)(_statusMask | _statusControl |
            (Accessible ? 0x40 | (ReadOnly ? 0 : 8) : 0));
        public byte Changes => _changes;
        public byte InterruptEnable => _interruptEnable;
        public byte Configuration => _configuration;
        public bool Level2Interrupt => (_changes & _interruptEnable & 8) != 0 ||
            (_interruptEnable & 2) == 0 && (_changes & _interruptEnable & 0x30) != 0;
        public bool Level6Interrupt => (_changes & _interruptEnable & 0x40) != 0 ||
            (_interruptEnable & 2) != 0 && (_changes & _interruptEnable & 0x30) != 0;

        public GaylePcmciaController(bool enabled, bool commonMemoryAvailable = true)
        { Enabled = enabled; _commonMemoryAvailable = commonMemoryAvailable; }

        /// <summary>Configuration changes are applied by the frontend with a cold
        /// restart: removing memory already added to Exec is not a safe live action.</summary>
        public void InsertSram(int bytes, bool readOnly = false)
        {
            if (!Enabled) throw new InvalidOperationException("This machine has no PCMCIA socket.");
            if (!_commonMemoryAvailable) throw new InvalidOperationException("PCMCIA SRAM overlaps the configured Fast RAM. Select at most 4 MiB Fast RAM.");
            if (bytes != 1024 * 1024 && bytes != 2 * 1024 * 1024 && bytes != 4 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(bytes), "SRAM cards support 1, 2 or 4 MiB.");
            var memory = new byte[bytes];
            var cis = CreateCis(bytes, readOnly);
            _memory = memory; _cis = cis; ReadOnly = readOnly;
            _configuration = 8; _changes |= 0x48;
        }

        public void Eject()
        {
            if (Inserted) _changes |= 0x48;
            _memory = Array.Empty<byte>(); _cis = Array.Empty<byte>();
            ReadOnly = false; _reset = false;
        }

        public void ResetMotherboard()
        {
            _statusControl = _statusMask = _changes = _interruptEnable = 0;
            _configuration = Inserted ? (byte)8 : (byte)0;
            _reset = _planning = false;
        }

        internal void BeginPlanning() => _planning = true;
        internal void EndPlanning() => _planning = false;

        public bool Maps(uint address) => Enabled &&
            (address >= 0x600000 && address <= 0x9FFFFF || address >= 0xA00000 && address <= 0xA7FFFF);

        public byte ReadByte(uint address, bool peek = false)
        {
            if (!Maps(address) || !Accessible) return 0xFF;
            if (address >= 0xA40000 && address <= 0xA5FFFF)
            {
                if (!peek && !_planning) _reset = false;
                return 0;
            }
            if (_reset) return 0xFF;
            if (address < 0xA00000)
            {
                uint offset = address - 0x600000;
                return offset < Size ? _memory[offset] : (byte)0xFF;
            }
            if (address < 0xA20000 && (address & 1) == 0)
            {
                int index = (int)((address - 0xA00000) >> 1);
                return index < _cis.Length ? _cis[index] : (byte)0xFF;
            }
            return 0xFF;
        }

        public void WriteByte(uint address, byte value)
        {
            if (_planning || !Maps(address) || !Accessible) return;
            if (address >= 0xA40000 && address <= 0xA5FFFF) { _reset = true; return; }
            if (!_reset && !ReadOnly && address >= 0x600000 && address < 0x600000 + Size)
                _memory[address - 0x600000] = value;
        }

        internal void WriteRegister(uint offset, byte value)
        {
            if (_planning) return;
            switch (offset)
            {
                case 0x8000:
                    bool wasDisabled = (_statusControl & 1) != 0;
                    _statusControl = (byte)(value & 3); _statusMask = (byte)(value & 0x7C);
                    if (Inserted && wasDisabled != ((_statusControl & 1) != 0)) _changes |= 0x48;
                    break;
                case 0x9000: _changes = (byte)((_changes & value) | (value & 3)); break;
                case 0xA000: _interruptEnable = value; break;
                case 0xB000: _configuration = (byte)(value & 15); break;
            }
        }

        // PC Card DEVICE size = (count + 1) * 512 * 4^scale.
        private static byte[] CreateCis(int bytes, bool readOnly)
        {
            int unit = 512, scale = 0;
            while (bytes / unit > 32) { unit *= 4; scale++; }
            var cis = new List<byte>();
            void Tuple(byte tag, params byte[] data) { cis.Add(tag); cis.Add((byte)data.Length); cis.AddRange(data); }
            Tuple(1, (byte)(0x64 | (readOnly ? 8 : 0)), (byte)(((bytes / unit - 1) << 3) | scale), 0xFF);
            Tuple(0x1E, 2, 0, 1, 1, 1, 1, 0xFF); // 16-bit SRAM device geometry.
            var version = new List<byte> { 4, 1 };
            version.AddRange(Encoding.ASCII.GetBytes("Amiga Emulator\0SRAM\0")); version.Add(0xFF);
            Tuple(0x15, version.ToArray());
            Tuple(0x21, 1, 0); // Memory card function.
            Tuple(0x14); // No common-memory CIS link.
            cis.Add(0xFF);
            return cis.ToArray();
        }

        internal void SaveState(BinaryWriter writer)
        {
            writer.Write(Size); writer.Write(ReadOnly); writer.Write(_memory);
            writer.Write(_statusControl); writer.Write(_statusMask); writer.Write(_changes);
            writer.Write(_interruptEnable); writer.Write(_configuration); writer.Write(_reset);
        }

        internal void LoadState(BinaryReader reader)
        {
            int size = reader.ReadInt32(); bool readOnly = reader.ReadBoolean();
            if (size != 0 && (!Enabled || !_commonMemoryAvailable || size != 1024 * 1024 && size != 2 * 1024 * 1024 && size != 4 * 1024 * 1024))
                throw new InvalidDataException("Invalid PCMCIA SRAM card size or machine.");
            var memory = new byte[size]; reader.BaseStream.ReadExactly(memory);
            byte control = reader.ReadByte(), mask = reader.ReadByte(), changes = reader.ReadByte();
            byte interrupts = reader.ReadByte(), configuration = reader.ReadByte(); bool reset = reader.ReadBoolean();
            if (control > 3 || (mask & ~0x7C) != 0 || (changes & 0x84) != 0 || configuration > 15)
                throw new InvalidDataException("Invalid PCMCIA controller state.");
            _memory = memory; _cis = size == 0 ? Array.Empty<byte>() : CreateCis(size, readOnly);
            ReadOnly = readOnly; _statusControl = control; _statusMask = mask; _changes = changes;
            _interruptEnable = interrupts; _configuration = configuration; _reset = reset; _planning = false;
        }
    }
}
