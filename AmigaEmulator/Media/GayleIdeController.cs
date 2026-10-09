using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace AmigaEmulator.Core.Media
{
    // Persisted controller IDs: retain the historical enum name for existing states.
    public enum AmigaIdeModel { Gayle, A4000, A3000Scsi }
    /// <summary>A600 Gayle ATA PIO master, with 512-byte sectors, CHS/LBA28 and level-2 interrupts.</summary>
    public sealed class GayleIdeController : IDisposable
    {
        private HardDiskImage _image;
        private readonly byte[] _registers = new byte[8];
        private readonly byte[] _buffer = new byte[512];
        private int _position, _remaining, _idPosition, _previewPosition, _previewId;
        private long _sector;
        private byte _status, _error, _control, _interruptEnable, _irqLatch, _configuration;
        private bool _writing, _identify, _irq, _planning;
        public bool Enabled { get; set; }
        public AmigaIdeModel Model { get; }
        public A3000ScsiController Scsi { get; }
        public GayleIdeController(AmigaIdeModel model = AmigaIdeModel.Gayle)
        {
            if (!Enum.IsDefined(model)) throw new ArgumentOutOfRangeException(nameof(model));
            Model = model;
            if (model == AmigaIdeModel.A3000Scsi)
                Scsi = new A3000ScsiController(() => _image, writing => { if (writing) WrittenSectors++; else ReadSectors++; });
        }
        internal GaylePcmciaController Pcmcia { get; set; }
        public HardDiskImage Image => _image;
        public long ReadSectors { get; private set; }
        public long WrittenSectors { get; private set; }
        public bool InterruptActive => Scsi != null ? Enabled && Scsi.InterruptActive : Enabled && _irq && (_control & 2) == 0 &&
            (Model == AmigaIdeModel.A4000 || (_interruptEnable & 0x80) != 0);
        private bool Master => (_registers[6] & 0x10) == 0;
        public void Mount(string path, bool readOnly)
        {
            string fullPath = Path.GetFullPath(path);
            if (_image != null && string.Equals(fullPath, _image.Path, StringComparison.OrdinalIgnoreCase))
            {
                _image.Flush();
                if (_image.ReadOnly != readOnly)
                {
                    // Writable HDFs are exclusively locked. A mode change on the
                    // same file must release our own handle before reopening it.
                    bool previousMode = _image.ReadOnly;
                    _image.Dispose(); _image = null;
                    try { _image = new HardDiskImage(fullPath, readOnly); }
                    catch (Exception replacementError)
                    {
                        try { _image = new HardDiskImage(fullPath, previousMode); }
                        catch (Exception recoveryError)
                        {
                            throw new IOException("Cannot reopen the current hard disk after a mode change failed.",
                                new AggregateException(replacementError, recoveryError));
                        }
                        throw;
                    }
                }
                Enabled = true; Reset(); return;
            }
            // Open first, so a failed replacement keeps the current disk available.
            var candidate = new HardDiskImage(fullPath, readOnly);
            try { _image?.Flush(); }
            catch { candidate.Dispose(); throw; }
            _image?.Dispose(); _image = candidate;
            Enabled = true; Reset();
        }
        public void Eject() { _image?.Flush(); _image?.Dispose(); _image = null; Reset(); }
        public void Flush() => _image?.Flush();
        public void Dispose() { _image?.Dispose(); _image = null; }
        public void Reset()
        {
            Scsi?.Reset();
            Array.Clear(_registers); Array.Clear(_buffer);
            _registers[2] = _registers[3] = 1; _registers[6] = 0xA0;
            _status = _image == null ? (byte)0 : (byte)0x50; _error = 1;
            _position = _remaining = _idPosition = 0; _control = _interruptEnable = _irqLatch = _configuration = 0;
            _writing = _identify = _irq = _planning = false; ReadSectors = WrittenSectors = 0;
        }
        public bool Maps(uint address) => Enabled &&
            (Scsi != null ? Scsi.Maps(address) : Model == AmigaIdeModel.A4000 ? address >= 0xDD2000 && address <= 0xDD3FFF :
            (address >= 0xDA0000 && address <= 0xDAFFFF) || (address >= 0xDE0000 && address <= 0xDEFFFF));
        public static bool IsData(uint address) => address >= 0xDA0000 && address < 0xDA4000 && Register(address) == 0;
        public bool IsDataPort(uint address) => Scsi == null && (Model == AmigaIdeModel.A4000
            ? address >= 0xDD2000 && address < 0xDD3000 && Register(address) == 0 : IsData(address));
        private static int Register(uint address) => (int)(((address & 0xFFFF) & ~0x2020u) >> 2);
        public void BeginPlanning() { _planning = true; _previewPosition = _position; _previewId = _idPosition; Pcmcia?.BeginPlanning(); Scsi?.BeginPlanning(); }
        public void EndPlanning() { _planning = false; Pcmcia?.EndPlanning(); Scsi?.EndPlanning(); }
        public byte ReadByte(uint address, bool peek = false)
        {
            if (Scsi != null) return Scsi.ReadByte(address, peek);
            if (Model == AmigaIdeModel.A4000 && !Maps(address)) return 0xFF;
            if (Model == AmigaIdeModel.A4000 && (address & 0xFFFF) == 0x3020)
                return (byte)(_irq && (_control & 2) == 0 ? 0x80 : 0);
            if (address >= 0xDE0000)
            {
                if ((address & 0xFFFF) != 0x1000) return 0;
                int bit = peek ? (_planning ? _previewId++ : _idPosition) : _idPosition++;
                return bit == 0 || bit == 1 || bit == 3 ? (byte)0x80 : (byte)0;
            }
            uint offset = address & 0xFFFF;
            if (offset == 0x8000) return (byte)((Pcmcia?.Status ?? 0) | (_irq && (_control & 2) == 0 ? 0x80 : 0));
            if (offset == 0x9000) return (byte)(_irqLatch | (Pcmcia?.Changes ?? 0) | (_irq && (_control & 2) == 0 ? 0x80 : 0));
            if (offset == 0xA000) return _interruptEnable;
            if (offset == 0xB000) return Pcmcia?.Configuration ?? _configuration;
            if (offset >= 0x4000) return 0;
            int reg = Register(address);
            if (reg == 0) return (byte)(ReadData(peek) >> 8);
            if (_image == null) return reg == 7 || reg == 0x406 ? (byte)0xFF : (byte)0;
            if (!Master) return reg == 7 || reg == 0x406 ? (byte)0 : (byte)0xFF;
            if (reg == 1) return _error;
            if (reg == 7 || reg == 0x406)
            {
                if (!peek && reg == 7) _irq = false;
                return _status;
            }
            return reg < 8 ? _registers[reg] : (byte)0;
        }
        public void WriteByte(uint address, byte value)
        {
            if (_planning) return;
            if (Scsi != null) { Scsi.WriteByte(address, value); return; }
            if (Model == AmigaIdeModel.A4000 && (!Maps(address) || (address & 0xFFFF) == 0x3020)) return;
            if (address >= 0xDE0000) { _idPosition = 0; return; }
            uint offset = address & 0xFFFF;
            Pcmcia?.WriteRegister(offset, value);
            if (offset == 0x9000) { _irqLatch &= value; return; }
            if (offset == 0xA000) { _interruptEnable = value; return; }
            if (offset == 0xB000) { _configuration = (byte)(value & 15); return; }
            if (offset >= 0x4000) return;
            int reg = Register(address);
            if (reg == 0x406)
            {
                bool reset = (_control & 4) != 0 && (value & 4) == 0;
                if (reset)
                {
                    byte interruptEnable = _interruptEnable, configuration = _configuration;
                    int idPosition = _idPosition;
                    long reads = ReadSectors, writes = WrittenSectors;
                    Reset(); _interruptEnable = interruptEnable; _configuration = configuration;
                    _idPosition = idPosition; ReadSectors = reads; WrittenSectors = writes;
                }
                _control = value;
                if ((value & 4) != 0) { _status = 0x80; _irq = false; _remaining = 0; }
                return;
            }
            if (reg == 7) { Command(value); return; }
            if (reg > 0 && reg < 7) _registers[reg] = value;
        }
        public ushort ReadData(bool peek = false)
        {
            if (!Master || (_status & 8) == 0 || _writing) return 0xFFFF;
            int position = peek && _planning ? _previewPosition : _position;
            ushort value;
            if (position < 512) value = BinaryPrimitives.ReadUInt16BigEndian(_buffer.AsSpan(position, 2));
            else if (!_identify && position / 512 < _remaining)
            {
                Span<byte> next = stackalloc byte[512];
                _image.ReadSector(_sector + position / 512, next);
                value = BinaryPrimitives.ReadUInt16BigEndian(next.Slice(position % 512, 2));
            }
            else value = 0xFFFF;
            if (peek) { if (_planning) _previewPosition += 2; }
            else
            {
                _position += 2;
                if (_position == 512) CompleteSector();
            }
            return value;
        }
        public void WriteData(ushort value)
        {
            if (!Master || !_writing || (_status & 8) == 0) return;
            BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(_position, 2), value);
            _position += 2;
            if (_position != 512) return;
            try { _image.WriteSector(_sector, _buffer); WrittenSectors++; CompleteSector(); }
            catch (IOException) { Fail(0x40); }
        }
        private void Command(byte command)
        {
            _irq = false; _irqLatch = 0; _error = 0; _position = _remaining = 0;
            _writing = _identify = false;
            if (!Master || _image == null) { _status = 0; return; }
            _status = 0x50;
            if (command == 0xEC)
            {
                Identify(); _remaining = 1; _identify = true; _status = 0x58; RaiseIrq(); return;
            }
            if (command == 0x20 || command == 0x21 || command == 0x30 || command == 0x31 || command == 0x40 || command == 0x41)
            {
                _remaining = _registers[2] == 0 ? 256 : _registers[2];
                _sector = (_registers[6] & 0x40) != 0
                    ? ((_registers[6] & 15L) << 24) | ((long)_registers[5] << 16) | ((long)_registers[4] << 8) | _registers[3]
                    : (((long)_registers[5] << 8 | _registers[4]) * HardDiskImage.Heads + (_registers[6] & 15)) * HardDiskImage.SectorsPerTrack + _registers[3] - 1;
                if (((_registers[6] & 0x40) == 0 && (_registers[3] == 0 || _registers[3] > HardDiskImage.SectorsPerTrack)) ||
                    _sector < 0 || _sector + _remaining > _image.SectorCount) { Fail(0x10); return; }
                if (command == 0x40 || command == 0x41) { _remaining = 0; RaiseIrq(); return; }
                _writing = command == 0x30 || command == 0x31;
                if (_writing && _image.ReadOnly) { Fail(4); return; }
                if (_writing) { Array.Clear(_buffer); _status = 0x58; }
                else LoadSector();
                return;
            }
            if (command == 0xE7) { _image.Flush(); RaiseIrq(); return; }
            if (command == 0x91 || command == 0xEF || command == 0xE0 || command == 0xE1 || (command & 0xF0) == 0x10 || (command & 0xF0) == 0x70)
            { RaiseIrq(); return; }
            Fail(4); // Unsupported DMA/multiple/packet commands are not advertised.
        }
        private void LoadSector()
        {
            try { _image.ReadSector(_sector, _buffer); ReadSectors++; _position = 0; _status = 0x58; RaiseIrq(); }
            catch (IOException) { Fail(0x40); }
        }
        private void CompleteSector()
        {
            _position = 0;
            if (--_remaining == 0) { _status = 0x50; if (_writing) RaiseIrq(); return; }
            _sector++;
            _registers[2] = (byte)_remaining;
            if ((_registers[6] & 0x40) != 0)
            {
                _registers[3] = (byte)_sector; _registers[4] = (byte)(_sector >> 8); _registers[5] = (byte)(_sector >> 16);
                _registers[6] = (byte)((_registers[6] & 0xF0L) | ((_sector >> 24) & 15));
            }
            else
            {
                long cylinder = _sector / (HardDiskImage.Heads * HardDiskImage.SectorsPerTrack);
                _registers[3] = (byte)(_sector % HardDiskImage.SectorsPerTrack + 1);
                _registers[4] = (byte)cylinder; _registers[5] = (byte)(cylinder >> 8);
                _registers[6] = (byte)((_registers[6] & 0xF0L) | ((_sector / HardDiskImage.SectorsPerTrack) % HardDiskImage.Heads));
            }
            if (_writing) { _status = 0x58; RaiseIrq(); } else LoadSector();
        }
        private void RaiseIrq() { _irq = true; _irqLatch |= 0x80; }
        private void Fail(byte error) { _error = error; _status = 0x51; _remaining = 0; RaiseIrq(); }
        private void Identify()
        {
            Array.Clear(_buffer);
            // ATA identify words are little-endian; raw Amiga filesystem sectors
            // retain their byte order through the Gayle data bus.
            void Word(int index, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(index * 2, 2), value);
            void Text(int index, int count, string value)
            {
                byte[] text = Encoding.ASCII.GetBytes(value.PadRight(count * 2)[..(count * 2)]);
                for (int i = 0; i < text.Length; i += 2) { _buffer[index * 2 + i] = text[i + 1]; _buffer[index * 2 + i + 1] = text[i]; }
            }
            Word(0, 0x0040); Word(1, (ushort)_image.Cylinders); Word(3, HardDiskImage.Heads); Word(6, HardDiskImage.SectorsPerTrack);
            Text(10, 10, "AMIGA-HDF-0000000001"); Text(23, 4, "1.0"); Text(27, 20, "Amiga Emulator Virtual IDE Disk");
            Word(49, 0x0200); Word(53, 1); Word(54, (ushort)_image.Cylinders); Word(55, HardDiskImage.Heads); Word(56, HardDiskImage.SectorsPerTrack);
            Word(57, (ushort)_image.SectorCount); Word(58, (ushort)(_image.SectorCount >> 16));
            Word(60, (ushort)_image.SectorCount); Word(61, (ushort)(_image.SectorCount >> 16));
        }
        public void SaveState(BinaryWriter writer)
        {
            Flush(); writer.Write(Enabled); writer.Write(_image?.Path ?? ""); writer.Write(_image?.ReadOnly ?? true); writer.Write(_image?.SectorCount ?? 0);
            writer.Write(_registers); writer.Write(_buffer); writer.Write(_position); writer.Write(_remaining); writer.Write(_idPosition); writer.Write(_sector);
            writer.Write(_status); writer.Write(_error); writer.Write(_control); writer.Write(_interruptEnable); writer.Write(_irqLatch); writer.Write(_configuration);
            writer.Write(_writing); writer.Write(_identify); writer.Write(_irq); writer.Write(ReadSectors); writer.Write(WrittenSectors);
            Scsi?.SaveState(writer);
        }
        public void LoadState(BinaryReader reader)
        {
            bool enabled = reader.ReadBoolean(); string path = reader.ReadString(); bool readOnly = reader.ReadBoolean(); long sectors = reader.ReadInt64();
            if (path.Length == 0) Eject();
            else if (_image == null || !string.Equals(path, _image.Path, StringComparison.OrdinalIgnoreCase) || _image.ReadOnly != readOnly) { Eject(); Mount(path, readOnly); }
            if ((_image?.SectorCount ?? 0) != sectors) throw new InvalidDataException("Savestate hard-disk size changed.");
            Enabled = enabled;
            reader.BaseStream.ReadExactly(_registers); reader.BaseStream.ReadExactly(_buffer); _position = reader.ReadInt32(); _remaining = reader.ReadInt32(); _idPosition = reader.ReadInt32(); _sector = reader.ReadInt64();
            _status = reader.ReadByte(); _error = reader.ReadByte(); _control = reader.ReadByte(); _interruptEnable = reader.ReadByte(); _irqLatch = reader.ReadByte(); _configuration = reader.ReadByte();
            _writing = reader.ReadBoolean(); _identify = reader.ReadBoolean(); _irq = reader.ReadBoolean(); ReadSectors = reader.ReadInt64(); WrittenSectors = reader.ReadInt64(); _planning = false;
            if (_position < 0 || _position >= 512 || (_position & 1) != 0 || _remaining < 0 || _remaining > 256) throw new InvalidDataException("Invalid ATA transfer state.");
            Scsi?.LoadState(reader);
        }
    }
}
