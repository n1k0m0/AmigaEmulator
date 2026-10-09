using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace AmigaEmulator.Core.Media
{
    /// <summary>Single-target WD33C93A initiator and A3000 Super DMAC.
    /// Independent implementation of register/phase behavior; synchronous bus timing,
    /// intermediate disconnect/reselection and target mode are not implemented.</summary>
    public sealed class A3000ScsiController
    {
        private readonly Func<HardDiskImage> _image;
        private readonly Action<bool> _sectorCompleted;
        private readonly byte[] _registers = new byte[32], _buffer = new byte[512], _cdb = new byte[12];
        private byte _selected, _scsiStatus, _targetStatus, _senseKey, _senseAsc, _planningSelected, _pendingService;
        private ushort _control, _dawr = 3;
        private uint _address, _wordCount;
        private bool _irq, _dma, _connected, _combined, _transfer, _writing, _disk, _planning, _commandIgnored;
        private int _phase, _position, _length, _bytesLeft, _cdbLength, _previewPosition;
        private long _sector;
        public bool InterruptActive => _irq && (_control & 4) != 0;
        public uint DmaAddress => _address;
        public bool DmaActive => _dma;
        private int Count => (_registers[0x12] << 16) | (_registers[0x13] << 8) | _registers[0x14];
        private void SetCount(int value)
        { _registers[0x12] = (byte)(value >> 16); _registers[0x13] = (byte)(value >> 8); _registers[0x14] = (byte)value; }
        public A3000ScsiController(Func<HardDiskImage> image, Action<bool> sectorCompleted)
        { _image = image; _sectorCompleted = sectorCompleted; Reset(); }
        public void Reset()
        {
            Array.Clear(_registers); Array.Clear(_buffer); Array.Clear(_cdb);
            _selected = _scsiStatus = _targetStatus = _senseKey = _senseAsc = _pendingService = 0;
            _control = 0; _dawr = 3; _address = _wordCount = 0;
            _irq = _dma = _connected = _combined = _transfer = _writing = _disk = _planning = _commandIgnored = false;
            _phase = 2; _position = _length = _bytesLeft = _cdbLength = 0; _sector = 0;
        }
        public void BeginPlanning() { _planning = true; _previewPosition = 0; _planningSelected = _selected; }
        public void EndPlanning() { if (_planning) _selected = _planningSelected; _planning = false; }
        public bool Maps(uint address) => address >= 0xDD0000 && address <= 0xDD00FF;
        private ushort InterruptStatus => (ushort)(1 | (_irq ? 0xC0 : 0) | (InterruptActive ? 0x10 : 0));
        private void Interrupt(byte status) { _scsiStatus = status; _irq = true; }
        private byte AuxiliaryStatus => (byte)((_irq ? 0x80 : 0) | (_commandIgnored ? 0x40 : 0) |
            (_combined && _transfer ? 0x20 : 0) | (_transfer ? 1 : 0));
        // The WD byte ports repeat across the four longwords at $DD0040.
        // Kickstart uses SASR at $DD0049; old software also uses $DD0041.
        private static bool IsWdAddressPort(int offset) => (offset & 0xF3) == 0x41;
        private static bool IsWdDataPort(int offset) => (offset & 0xF3) == 0x43;
        public byte ReadByte(uint address, bool peek = false)
        {
            int offset = (int)(address & 255);
            if (IsWdAddressPort(offset)) return AuxiliaryStatus;
            if (IsWdDataPort(offset)) return ReadRegister(peek);
            uint value = (offset & ~3) switch
            {
                0 => _dawr, 4 => _wordCount, 8 => (uint)(_control | (_dma ? 0x100 : 0)),
                12 => _address, 28 => InterruptStatus, _ => 0
            };
            if (!peek && !_planning) Strobe(offset & ~3);
            return (byte)(value >> (24 - (offset & 3) * 8));
        }
        public void WriteByte(uint address, byte value)
        {
            if (_planning) return;
            int offset = (int)(address & 255), shift = 24 - (offset & 3) * 8;
            if (IsWdAddressPort(offset)) { _selected = value; return; }
            if (IsWdDataPort(offset)) { WriteRegister(value); return; }
            uint Replace(uint old) => (old & ~(255u << shift)) | ((uint)value << shift);
            switch (offset & ~3)
            {
                case 0: _dawr = (ushort)Replace(_dawr); break;
                case 4: _wordCount = Replace(_wordCount); break;
                case 8:
                    _control = (ushort)(Replace(_control) & 0x3F);
                    if ((_control & 16) != 0) ResetWd();
                    break;
                case 12: _address = Replace(_address) & ~3u; break;
                default: Strobe(offset & ~3); break;
            }
        }
        private void Strobe(int offset)
        {
            if (offset == 16) _dma = true;
            if (offset == 24) _irq = false;
            if (offset == 60) _dma = false;
            // FIFO is drained per transfer; FLUSH therefore completes immediately.
        }
        private byte ReadRegister(bool peek)
        {
            int reg = _selected;
            byte value;
            if (reg > 0x19 && reg != 0x1F) value = 0xFF;
            else if (reg == 0x1F) value = AuxiliaryStatus;
            else if (reg == 0x17) { value = _scsiStatus; if (!peek) _irq = false; }
            else if (reg == 0x19) value = ReadData(peek);
            else value = _registers[reg];
            // Only command, data and ASR remain stationary (WD33C93A 6.2.2).
            if (reg != 0x18 && reg != 0x19 && reg != 0x1F)
            {
                if (!peek || _planning) _selected = unchecked((byte)(reg + 1));
            }
            return value;
        }
        private void WriteRegister(byte value)
        {
            int reg = _selected;
            if (reg > 0x19) return;
            if (reg == 0x18) Command(value);
            else if (reg == 0x19) WriteData(value);
            else if (reg != 0x17 && reg != 0x1F)
            { _registers[reg] = value; if (reg < 0x18) _selected++; }
        }
        private void ResetWd()
        {
            byte ownId = _registers[0];
            Array.Clear(_registers); _registers[0] = ownId;
            _connected = _transfer = _combined = false;
            _pendingService = 0;
            _position = _length = _bytesLeft = 0;
            Interrupt((ownId & 8) != 0 ? (byte)1 : (byte)0);
        }
        private void Command(byte command)
        {
            if (_irq || (_combined && _transfer && (command & 0x7F) > 1))
            { _commandIgnored = true; return; }
            _commandIgnored = false;
            _irq = false;
            _pendingService = 0;
            switch (command & 0x7F)
            {
                case 0: ResetWd(); return;
                case 1: _transfer = false; Interrupt(0x28); return;
                case 2: return; // ATN has no asynchronous target negotiation in this implementation.
                case 3: // Negate ACK accepts the last message-in byte.
                    if (_phase == 7) { _connected = _transfer = false; Interrupt(0x85); } return;
                case 4: _connected = _transfer = false; Interrupt(0x85); return;
                case 6: case 7: case 8: case 9:
                    if (_image() == null || (_registers[0x15] & 7) != 0)
                    { _connected = _transfer = false; Interrupt(0x42); return; }
                    _connected = true; _combined = (command & 0x7F) >= 8;
                    if (_combined)
                    {
                        if (_registers[0x10] < 0x40)
                        {
                            Array.Copy(_registers, 3, _cdb, 0, 12);
                            _cdbLength = CommandLength(_cdb[0]); ExecuteCdb();
                        }
                        else if (_bytesLeft > 0) _transfer = Count > 0;
                        else CompleteCommand();
                    }
                    else
                    {
                        _phase = (command & 0x7F) == 6 ? 6 : 2; _position = 0;
                        Interrupt(0x11);
                        // Selection completion and the target's first REQ are distinct
                        // interrupts. Status must remain stable until acknowledged.
                        _pendingService = (byte)(0x88 | _phase);
                    }
                    return;
                case 0x20:
                    if (!_connected) { Interrupt(0x40); return; }
                    if ((command & 0x80) != 0) SetCount(1);
                    _transfer = Count > 0;
                    return;
                default: Interrupt(0x40); return;
            }
        }
        private static int CommandLength(byte opcode) => (opcode >> 5) switch { 0 => 6, 1 or 2 => 10, 5 => 12, _ => 6 };
        private void CheckCondition(byte key, byte asc)
        { _senseKey = key; _senseAsc = asc; _targetStatus = 2; _bytesLeft = _length = 0; FinishData(); }
        private void ExecuteCdb()
        {
            _position = 0; _targetStatus = 0; _disk = _writing = false; _length = _bytesLeft = 0;
            if ((_cdb[1] & 0xE0) != 0 || (_registers[0x0F] & 7) != 0) { CheckCondition(5, 0x25); return; }
            byte command = _cdb[0];
            Array.Clear(_buffer);
            switch (command)
            {
                case 0: case 0x1B: case 0x1E: case 0x0B: case 0x2B: case 0x2F: case 0x35:
                    if (command == 0x35) _image().Flush(); FinishData(); return;
                case 3:
                    _buffer[0] = 0x70; _buffer[2] = _senseKey; _buffer[7] = 10; _buffer[12] = _senseAsc;
                    _length = Math.Min(18, (int)_cdb[4]); _senseKey = _senseAsc = 0; break;
                case 0x12:
                    if ((_cdb[1] & 1) != 0) { CheckCondition(5, 0x24); return; }
                    _buffer[2] = 2; _buffer[3] = 2; _buffer[4] = 31;
                    Encoding.ASCII.GetBytes("CODEX   AMIGA HDF       0001").CopyTo(_buffer, 8);
                    _length = Math.Min(36, (int)_cdb[4]); break;
                case 0x25:
                    BinaryPrimitives.WriteUInt32BigEndian(_buffer, (uint)(_image().SectorCount - 1));
                    BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(4), 512); _length = 8; break;
                case 0x1A:
                    _buffer[0] = 11; _buffer[2] = _image().ReadOnly ? (byte)0x80 : (byte)0;
                    if ((_cdb[1] & 8) == 0)
                    {
                        _buffer[3] = 8; uint sectors = (uint)_image().SectorCount;
                        _buffer[5] = (byte)(sectors >> 16); _buffer[6] = (byte)(sectors >> 8); _buffer[7] = (byte)sectors;
                        _buffer[10] = 2;
                    }
                    else _buffer[0] = 3;
                    _length = Math.Min(_buffer[0] + 1, _cdb[4]); break;
                case 8: case 10: case 0x28: case 0x2A:
                    int blocks;
                    if (command < 0x20)
                    { _sector = ((_cdb[1] & 31) << 16) | (_cdb[2] << 8) | _cdb[3]; blocks = _cdb[4] == 0 ? 256 : _cdb[4]; }
                    else
                    { _sector = BinaryPrimitives.ReadUInt32BigEndian(_cdb.AsSpan(2)); blocks = (_cdb[7] << 8) | _cdb[8]; }
                    if (_sector + blocks > _image().SectorCount) { CheckCondition(5, 0x21); return; }
                    _writing = command == 10 || command == 0x2A;
                    if (_writing && _image().ReadOnly) { CheckCondition(7, 0x27); return; }
                    _disk = true; _bytesLeft = blocks * 512; _length = Math.Min(512, _bytesLeft);
                    if (_bytesLeft == 0) { FinishData(); return; }
                    if (!_writing) ReadSector(); break;
                default: CheckCondition(5, 0x20); return;
            }
            if (!_disk) _bytesLeft = _length;
            _phase = _writing ? 0 : 1;
            if (_combined) { _registers[0x10] = 0x45; _transfer = Count > 0; if (!_transfer && _bytesLeft != 0) Interrupt((byte)(0x88 | _phase)); }
            else Interrupt((byte)(0x18 | _phase));
            if (_bytesLeft == 0) FinishData();
        }
        private void ReadSector()
        {
            try { _image().ReadSector(_sector, _buffer); _sectorCompleted(false); }
            catch (IOException) { CheckCondition(3, 0x11); }
        }
        private byte ReadData(bool peek)
        {
            if (!_transfer || (_phase != 1 && _phase != 3 && _phase != 7)) return 0xFF;
            if (peek)
            {
                int offset = _planning ? _previewPosition++ : 0;
                if (_phase == 3) return _targetStatus;
                if (_phase == 7) return 0;
                int position = _position + offset;
                if (position < _length) return _buffer[position];
                if (_disk && position < _position + _bytesLeft)
                {
                    Span<byte> sector = stackalloc byte[512];
                    try { _image().ReadSector(_sector + position / 512, sector); return sector[position % 512]; }
                    catch (IOException) { return 0; }
                }
                return 0;
            }
            byte value = _phase == 3 ? _targetStatus : _phase == 7 ? (byte)0 : _buffer[_position];
            if (_phase == 1) AdvanceData();
            else if (_phase == 3) { _phase = 7; _transfer = false; Interrupt(0x1F); }
            else { _transfer = false; Interrupt(0x20); } // Message-in pauses with ACK asserted.
            DecrementCount(); return value;
        }
        private void WriteData(byte value)
        {
            if (!_transfer) return;
            if (_phase == 2)
            {
                if (_position < 12) _cdb[_position++] = value;
                if (_position == 1) _cdbLength = CommandLength(value);
                DecrementCount();
                if (_position == _cdbLength) { _transfer = false; ExecuteCdb(); }
                return;
            }
            if (_phase == 6) { DecrementCount(); if (Count == 0) { _phase = 2; _position = 0; Interrupt(0x1A); } return; }
            if (_phase != 0 || !_writing) return;
            _buffer[_position] = value; AdvanceData(); DecrementCount();
        }
        private void AdvanceData()
        {
            _position++; _bytesLeft--;
            if (_disk && _position == 512)
            {
                if (_writing)
                {
                    try { _image().WriteSector(_sector, _buffer); _sectorCompleted(true); }
                    catch (IOException) { CheckCondition(3, 0x0C); return; }
                }
                _sector++; _position = 0;
                if (_bytesLeft > 0 && !_writing) ReadSector();
            }
            if (_bytesLeft == 0) FinishData();
        }
        private void DecrementCount()
        {
            if (Count > 0) SetCount(Count - 1);
            if (Count == 0 && _transfer) { _transfer = false; Interrupt((byte)(0x18 | _phase)); }
        }
        private void FinishData()
        {
            _transfer = false; _phase = 3;
            if (_combined) CompleteCommand(); else Interrupt(0x1B);
        }
        private void CompleteCommand()
        {
            _registers[0x0F] = _targetStatus; _registers[0x10] = 0x60;
            _connected = _transfer = _combined = false;
            Interrupt(_targetStatus == 0 ? (byte)0x16 : (byte)0x45);
            // With EDI clear, completion precedes the target releasing BSY.
            // Drivers wait for this second interrupt before starting another I/O.
            if ((_registers[1] & 8) == 0) _pendingService = 0x85;
        }

        /// <returns>True when this tick consumed a shared Chip RAM slot.</returns>
        public bool TickDma(bool chipBusAvailable, Func<uint, bool> isChipRam,
            Func<uint, bool> validMemory, Func<uint, byte> read, Action<uint, byte> write)
        {
            if (!_irq && _pendingService != 0)
            { byte status = _pendingService; _pendingService = 0; Interrupt(status); }
            if (!_dma || !_transfer || (_phase != 0 && _phase != 1)) return false;
            bool chip = isChipRam(_address);
            if (chip && !chipBusAvailable) return false;
            if (((_control & 2) != 0) != _writing) { _transfer = false; Interrupt(0x40); return false; }
            for (int i = 0; i < 2 && _transfer; i++)
            {
                if (!validMemory(_address)) { _transfer = false; Interrupt(0x40); break; }
                if (_writing) WriteData(read(_address)); else write(_address, ReadData(false));
                _address++; if (_wordCount > 0 && (i & 1) != 0) _wordCount--;
            }
            return chip;
        }
        public void SaveState(BinaryWriter writer)
        {
            writer.Write(3); writer.Write(_registers); writer.Write(_buffer); writer.Write(_cdb);
            writer.Write(_selected); writer.Write(_scsiStatus); writer.Write(_targetStatus); writer.Write(_senseKey); writer.Write(_senseAsc);
            writer.Write(_control); writer.Write(_dawr); writer.Write(_address); writer.Write(_wordCount);
            writer.Write(_irq); writer.Write(_dma); writer.Write(_connected); writer.Write(_combined); writer.Write(_transfer); writer.Write(_writing); writer.Write(_disk);
            writer.Write(_phase); writer.Write(_position); writer.Write(_length); writer.Write(_bytesLeft); writer.Write(_cdbLength); writer.Write(_sector);
            writer.Write(_commandIgnored);
            writer.Write(_pendingService);
        }
        public void LoadState(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version < 1 || version > 3) throw new InvalidDataException("Unsupported A3000 SCSI state.");
            reader.BaseStream.ReadExactly(_registers); reader.BaseStream.ReadExactly(_buffer); reader.BaseStream.ReadExactly(_cdb);
            _selected = reader.ReadByte(); _scsiStatus = reader.ReadByte(); _targetStatus = reader.ReadByte(); _senseKey = reader.ReadByte(); _senseAsc = reader.ReadByte();
            _control = reader.ReadUInt16(); _dawr = reader.ReadUInt16(); _address = reader.ReadUInt32(); _wordCount = reader.ReadUInt32();
            _irq = reader.ReadBoolean(); _dma = reader.ReadBoolean(); _connected = reader.ReadBoolean(); _combined = reader.ReadBoolean(); _transfer = reader.ReadBoolean(); _writing = reader.ReadBoolean(); _disk = reader.ReadBoolean();
            _phase = reader.ReadInt32(); _position = reader.ReadInt32(); _length = reader.ReadInt32(); _bytesLeft = reader.ReadInt32(); _cdbLength = reader.ReadInt32(); _sector = reader.ReadInt64(); _planning = false;
            _commandIgnored = version >= 2 && reader.ReadBoolean();
            _pendingService = version >= 3 ? reader.ReadByte() : (byte)0;
            if (_phase < 0 || _phase > 7 || _position < 0 || _position > 512 ||
                _length < 0 || _length > 512 || _bytesLeft < 0 || _bytesLeft > 65535 * 512 || _cdbLength < 0 || _cdbLength > 12 ||
                _pendingService is not (0 or 0x8A or 0x8E or 0x85))
                throw new InvalidDataException("Invalid SCSI transfer state.");
        }
    }
}
