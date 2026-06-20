/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using System;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Cia;
using AmigaEmulator.Core.Cpu;
using AmigaEmulator.Core.CustomChips;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Core.Memory
{
    /// <summary>
    /// Routes 68000 memory accesses between Chip RAM, Slow RAM, CIA chips, custom registers, and Kickstart ROM.
    /// </summary>
    public sealed class AmigaBus : IM68kPeekableBus, IM68kBusArbitration, IM68kResetVectorProvider
    {
        /// <summary>
        /// Stores chip RAM.
        /// </summary>
        private readonly ChipRam _chipRam;
        /// <summary>
        /// Stores slow RAM.
        /// </summary>
        private readonly SlowRam _slowRam;
        /// <summary>
        /// Stores fast RAM.
        /// </summary>
        private readonly FastRam _fastRam;
        /// <summary>
        /// Stores chip RAM End.
        /// </summary>
        private readonly uint _chipRamEnd;
        /// <summary>
        /// Stores custom Registers.
        /// </summary>
        private readonly CustomRegisterMap _customRegisters;
        /// <summary>
        /// Stores paula.
        /// </summary>
        private readonly Paula _paula;
        /// <summary>
        /// Stores CIA A.
        /// </summary>
        private readonly Cia8520 _ciaA;
        /// <summary>
        /// Stores CIA B.
        /// </summary>
        private readonly Cia8520 _ciaB;
        /// <summary>
        /// Stores floppy Controller.
        /// </summary>
        private readonly FloppyController _floppyController;
        /// <summary>
        /// Stores blitter.
        /// </summary>
        private Blitter _blitter;
        /// <summary>
        /// Stores kickstart.
        /// </summary>
        private KickstartRom _kickstart;
        /// <summary>
        /// Stores last Bus Value.
        /// </summary>
        private byte _lastBusValue;
        /// <summary>
        /// Stores CIA A Port A Input Mask Provider.
        /// </summary>
        private Func<byte> _ciaAPortAInputMaskProvider;
        /// <summary>
        /// Stores CIA A Port A Input Value Provider.
        /// </summary>
        private Func<byte> _ciaAPortAInputValueProvider;

        /// <summary>
        /// Raised when the emulated bus observes a memory access.
        /// </summary>
        public event Action<uint, int, uint, AmigaAddressRegion> ReadObserved;

        /// <summary>
        /// Raised when the emulated bus observes a memory access.
        /// </summary>
        public event Action<uint, int, uint, AmigaAddressRegion> WriteObserved;

        /// <summary>
        /// Initializes a new instance of the AmigaBus class.
        /// </summary>
        public AmigaBus(AmigaModel model, CustomRegisterMap customRegisters, Paula paula, Cia8520 ciaA, Cia8520 ciaB)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            _chipRam = new ChipRam(model.ChipRamBytes);
            _slowRam = new SlowRam(model.SlowRamBytes);
            _fastRam = new FastRam(model.FastRamBytes);
            _chipRamEnd = model.ChipRamBytes > 0
                ? (uint)model.ChipRamBytes - 1u
                : MemoryMap.ChipRamEnd;
            _customRegisters = customRegisters ?? new CustomRegisterMap();
            _paula = paula;
            _ciaA = ciaA;
            _ciaB = ciaB;
            _floppyController = new FloppyController();
            _kickstart = new KickstartRom();
            RomOverlayAtZero = true;
            _lastBusValue = 0xFF;
        }

        /// <summary>
        /// Gets or sets ROM Overlay At Zero.
        /// </summary>
        public bool RomOverlayAtZero { get; set; }

        /// <summary>
        /// Gets Kickstart.
        /// </summary>
        public KickstartRom Kickstart
        {
            get { return _kickstart; }
        }

        /// <summary>
        /// Gets Chip RAM.
        /// </summary>
        public ChipRam ChipRam
        {
            get { return _chipRam; }
        }

        /// <summary>
        /// Gets Slow RAM.
        /// </summary>
        public SlowRam SlowRam
        {
            get { return _slowRam; }
        }

        /// <summary>
        /// Gets Last Bus Value.
        /// </summary>
        public byte LastBusValue
        {
            get { return _lastBusValue; }
        }

        /// <summary>
        /// Gets Floppy Controller.
        /// </summary>
        public FloppyController FloppyController
        {
            get { return _floppyController; }
        }

        /// <summary>
        /// Attaches Blitter to this component.
        /// </summary>
        public void AttachBlitter(Blitter blitter)
        {
            _blitter = blitter;
        }

        /// <summary>
        /// Attaches CIA A Port A Input Provider to this component.
        /// </summary>
        public void AttachCiaAPortAInputProvider(Func<byte> maskProvider, Func<byte> valueProvider)
        {
            _ciaAPortAInputMaskProvider = maskProvider;
            _ciaAPortAInputValueProvider = valueProvider;
        }

        /// <summary>
        /// Loads Kickstart into emulator state.
        /// </summary>
        public void LoadKickstart(byte[] romBytes)
        {
            _kickstart = new KickstartRom(romBytes);
        }

        /// <summary>
        /// Runs Clear Writable Memory.
        /// </summary>
        public void ClearWritableMemory()
        {
            _chipRam.Clear();
            _slowRam.Clear();
            _fastRam.Clear();
            _customRegisters.Reset();
        }

        /// <summary>
        /// Writes bus-owned memory, floppy, ROM overlay, and open-bus state to a savestate.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            writer.Write(RomOverlayAtZero);
            writer.Write(_lastBusValue);
            StateSerializer.WriteObjectFields(writer, _chipRam);
            StateSerializer.WriteObjectFields(writer, _slowRam);
            StateSerializer.WriteObjectFields(writer, _fastRam);
            StateSerializer.WriteObjectFields(writer, _kickstart);
            StateSerializer.WriteObjectFields(writer, _floppyController);
        }

        /// <summary>
        /// Restores bus-owned memory, floppy, ROM overlay, and open-bus state from a savestate.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            RomOverlayAtZero = reader.ReadBoolean();
            _lastBusValue = reader.ReadByte();
            StateSerializer.ReadObjectFields(reader, _chipRam);
            StateSerializer.ReadObjectFields(reader, _slowRam);
            StateSerializer.ReadObjectFields(reader, _fastRam);
            StateSerializer.ReadObjectFields(reader, _kickstart);
            StateSerializer.ReadObjectFields(reader, _floppyController);
            UpdateCiaAFloppyInputs();
        }

        /// <summary>
        /// Returns the Region.
        /// </summary>
        public AmigaAddressRegion GetRegion(uint address)
        {
            address = MemoryMap.Normalize(address);

            if (address <= _chipRamEnd)
            {
                return AmigaAddressRegion.ChipRam;
            }

            if (address >= MemoryMap.CiaBStart && address <= MemoryMap.CiaBEnd)
            {
                return AmigaAddressRegion.CiaB;
            }

            if (address >= MemoryMap.CiaAStart && address <= MemoryMap.CiaAEnd)
            {
                return AmigaAddressRegion.CiaA;
            }

            if (_slowRam.Enabled &&
                address >= MemoryMap.SlowRamStart &&
                address < MemoryMap.SlowRamStart + (uint)_slowRam.Size)
            {
                return AmigaAddressRegion.SlowRam;
            }

            if (_fastRam.Enabled &&
                address >= MemoryMap.FastRamStart &&
                address < MemoryMap.FastRamStart + (uint)_fastRam.Size &&
                address <= MemoryMap.FastRamEnd)
            {
                return AmigaAddressRegion.FastRam;
            }

            if (address >= MemoryMap.CustomStart && address <= MemoryMap.CustomEnd)
            {
                return AmigaAddressRegion.CustomRegisters;
            }

            if (address >= MemoryMap.KickstartStart && address <= MemoryMap.KickstartEnd)
            {
                return AmigaAddressRegion.KickstartRom;
            }

            return AmigaAddressRegion.Unmapped;
        }

        /// <summary>
        /// Returns whether the CPU access must wait for a free Agnus-controlled bus slot.
        /// </summary>
        public bool RequiresChipBus(uint address, int size)
        {
            uint normalized = MemoryMap.Normalize(address);
            if (IsKickstartOverlayRead(normalized))
            {
                return false;
            }

            AmigaAddressRegion first = GetRegion(normalized);
            if (RegionRequiresChipBus(first))
            {
                return true;
            }

            if (size <= 1)
            {
                return false;
            }

            uint last = MemoryMap.Normalize(normalized + (uint)size - 1u);
            return RegionRequiresChipBus(GetRegion(last));
        }

        /// <summary>
        /// Reads CPU reset vectors through the current overlay and applies A500 ROM-stub vector handling.
        /// </summary>
        public bool TryReadResetVectors(out uint initialStackPointer, out uint initialProgramCounter)
        {
            initialStackPointer = ReadLong(0);
            initialProgramCounter = ReadLong(4);
            ushort wordAtTwo = ReadWord(2);
            if (RomOverlayAtZero &&
                wordAtTwo == 0x4EF9 &&
                initialProgramCounter >= MemoryMap.KickstartStart &&
                initialProgramCounter <= MemoryMap.KickstartEnd)
            {
                initialStackPointer = _chipRamEnd + 1u;
            }

            return true;
        }

        /// <summary>
        /// Reads one byte from the normalized Amiga address map, including overlay and CIA lanes.
        /// </summary>
        public byte ReadByte(uint address)
        {
            address = MemoryMap.Normalize(address);
            byte value;

            if (IsKickstartOverlayRead(address))
            {
                value = _kickstart.Read(address);
                _lastBusValue = value;
                NotifyReadObserved(address, 1, value, AmigaAddressRegion.KickstartRom);
                return value;
            }

            AmigaAddressRegion region = GetRegion(address);
            switch (region)
            {
                case AmigaAddressRegion.ChipRam:
                    value = _chipRam.Read(address);
                    break;
                case AmigaAddressRegion.SlowRam:
                    value = _slowRam.Read(address - MemoryMap.SlowRamStart);
                    break;
                case AmigaAddressRegion.FastRam:
                    value = _fastRam.Read(address - MemoryMap.FastRamStart);
                    break;
                case AmigaAddressRegion.CustomRegisters:
                    if (IsBplconWriteOnlyRead(address))
                    {
                        value = _lastBusValue;
                        break;
                    }

                    if (_paula != null && _paula.TryReadCustomRegisterWord(address & 0xFFFFFEu, out ushort paulaWord))
                    {
                        value = (address & 1) == 0 ? (byte)(paulaWord >> 8) : (byte)(paulaWord & 0xFF);
                        break;
                    }

                    value = _customRegisters.ReadByte(address);
                    break;
                case AmigaAddressRegion.CiaA:
                    if (IsCiaAByteLane(address))
                    {
                        UpdateCiaAFloppyInputs();
                        value = _ciaA != null ? _ciaA.ReadByte(address) : (byte)0xFF;
                    }
                    else
                    {
                        value = 0xFF;
                    }
                    break;
                case AmigaAddressRegion.CiaB:
                    value = IsCiaBByteLane(address) && _ciaB != null
                        ? _ciaB.ReadByte(address)
                        : (byte)0xFF;
                    break;
                case AmigaAddressRegion.KickstartRom:
                    value = _kickstart.Read(address - MemoryMap.KickstartStart);
                    break;
                default:
                    value = 0xFF;
                    break;
            }

            _lastBusValue = value;
            NotifyReadObserved(address, 1, value, region);
            return value;
        }

        /// <summary>
        /// Reads one byte without applying bus read side effects, used by the suspendable CPU planner.
        /// </summary>
        public byte PeekByte(uint address)
        {
            address = MemoryMap.Normalize(address);
            if (IsKickstartOverlayRead(address))
            {
                return _kickstart.Read(address);
            }

            AmigaAddressRegion region = GetRegion(address);
            switch (region)
            {
                case AmigaAddressRegion.ChipRam:
                    return _chipRam.Read(address);
                case AmigaAddressRegion.SlowRam:
                    return _slowRam.Read(address - MemoryMap.SlowRamStart);
                case AmigaAddressRegion.FastRam:
                    return _fastRam.Read(address - MemoryMap.FastRamStart);
                case AmigaAddressRegion.CustomRegisters:
                    if (_paula != null && _paula.TryPeekCustomRegisterWord(address & 0xFFFFFEu, out ushort paulaWord))
                    {
                        return (address & 1) == 0 ? (byte)(paulaWord >> 8) : (byte)(paulaWord & 0xFF);
                    }

                    return _customRegisters.PeekByte(address);
                case AmigaAddressRegion.CiaA:
                    if (IsCiaAByteLane(address))
                    {
                        UpdateCiaAFloppyInputs();
                        return _ciaA != null ? _ciaA.PeekByte(address) : (byte)0xFF;
                    }

                    return 0xFF;
                case AmigaAddressRegion.CiaB:
                    return IsCiaBByteLane(address) && _ciaB != null
                        ? _ciaB.PeekByte(address)
                        : (byte)0xFF;
                case AmigaAddressRegion.KickstartRom:
                    return _kickstart.Read(address - MemoryMap.KickstartStart);
                default:
                    return 0xFF;
            }
        }

        /// <summary>
        /// Reads a big-endian word from the Amiga bus, honoring custom-register word reads.
        /// </summary>
        public ushort ReadWord(uint address)
        {
            uint normalized = MemoryMap.Normalize(address);
            if (GetRegion(normalized) == AmigaAddressRegion.CustomRegisters &&
                _paula != null &&
                _paula.TryReadCustomRegisterWord(normalized, out ushort paulaWord))
            {
                _lastBusValue = (byte)(paulaWord & 0xFF);
                NotifyReadObserved(normalized, 2, paulaWord, AmigaAddressRegion.CustomRegisters);
                return paulaWord;
            }

            return (ushort)((ReadByte(normalized) << 8) | ReadByte(normalized + 1));
        }

        /// <summary>
        /// Reads a big-endian word without applying bus read side effects.
        /// </summary>
        public ushort PeekWord(uint address)
        {
            uint normalized = MemoryMap.Normalize(address);
            if (GetRegion(normalized) == AmigaAddressRegion.CustomRegisters &&
                _paula != null &&
                _paula.TryPeekCustomRegisterWord(normalized, out ushort paulaWord))
            {
                return paulaWord;
            }

            if (GetRegion(normalized) == AmigaAddressRegion.CustomRegisters &&
                GetRegion(normalized + 1) == AmigaAddressRegion.CustomRegisters)
            {
                return _customRegisters.PeekWord(normalized);
            }

            return (ushort)((PeekByte(normalized) << 8) | PeekByte(normalized + 1));
        }

        /// <summary>
        /// Reads a big-endian longword from the Amiga bus as two consecutive words.
        /// </summary>
        public uint ReadLong(uint address)
        {
            uint normalized = MemoryMap.Normalize(address);
            return (uint)((ReadWord(normalized) << 16) | ReadWord(normalized + 2));
        }

        /// <summary>
        /// Reads a big-endian longword without applying bus read side effects.
        /// </summary>
        public uint PeekLong(uint address)
        {
            uint normalized = MemoryMap.Normalize(address);
            return (uint)((PeekWord(normalized) << 16) | PeekWord(normalized + 2));
        }

        /// <summary>
        /// Writes one byte to the normalized Amiga address map and dispatches device side effects.
        /// </summary>
        public void WriteByte(uint address, byte value)
        {
            address = MemoryMap.Normalize(address);
            _lastBusValue = value;
            if (IsKickstartOverlayRead(address))
            {
                NotifyWriteObserved(address, 1, value, AmigaAddressRegion.KickstartRom);
                return;
            }

            AmigaAddressRegion region = GetRegion(address);

            switch (region)
            {
                case AmigaAddressRegion.ChipRam:
                    _chipRam.Write(address, value);
                    break;
                case AmigaAddressRegion.SlowRam:
                    _slowRam.Write(address - MemoryMap.SlowRamStart, value);
                    break;
                case AmigaAddressRegion.FastRam:
                    _fastRam.Write(address - MemoryMap.FastRamStart, value);
                    break;
                case AmigaAddressRegion.CustomRegisters:
                    _customRegisters.WriteByte(address, value);
                    if (_blitter != null)
                    {
                        _blitter.NotifyCustomRegisterWrite(address, 1);
                    }

                    if (_paula != null)
                    {
                        _paula.NotifyCustomRegisterWordWrite(address & 0x00FFFFFEu, _customRegisters.ReadWord(address & 0x00FFFFFEu));
                        _paula.NotifyCustomRegisterWrite(address, value);
                    }
                    break;
                case AmigaAddressRegion.CiaA:
                    if (_ciaA != null && IsCiaAByteLane(address))
                    {
                        _ciaA.WriteByte(address, value);
                    }
                    break;
                case AmigaAddressRegion.CiaB:
                    if (_ciaB != null && IsCiaBByteLane(address))
                    {
                        _ciaB.WriteByte(address, value);
                        _floppyController.ApplyCiaBPort(_ciaB.Prb, _ciaB.Ddrb);
                    }
                    break;
            }

            NotifyWriteObserved(address, 1, value, region);
        }

        /// <summary>
        /// Writes a big-endian word to the Amiga bus, using direct custom-register writes when possible.
        /// </summary>
        public void WriteWord(uint address, ushort value)
        {
            uint normalized = MemoryMap.Normalize(address);
            if (GetRegion(normalized) == AmigaAddressRegion.CustomRegisters &&
                GetRegion(normalized + 1) == AmigaAddressRegion.CustomRegisters)
            {
                _customRegisters.WriteWord(normalized, value);
                if (_blitter != null)
                {
                    _blitter.NotifyCustomRegisterWrite(normalized, 2);
                }

                _lastBusValue = (byte)(value & 0xFF);
                NotifyWriteObserved(normalized, 2, value, AmigaAddressRegion.CustomRegisters);
                if (_paula != null)
                {
                    _paula.NotifyCustomRegisterWordWrite(normalized, value);
                    _paula.NotifyCustomRegisterWrite(normalized, (byte)(value >> 8));
                    _paula.NotifyCustomRegisterWrite(normalized + 1, (byte)(value & 0xFF));
                }

                return;
            }

            WriteByte(address, (byte)(value >> 8));
            WriteByte(address + 1, (byte)(value & 0xFF));
        }

        /// <summary>
        /// Writes a big-endian longword to the Amiga bus as two consecutive words.
        /// </summary>
        public void WriteLong(uint address, uint value)
        {
            WriteWord(address, (ushort)(value >> 16));
            WriteWord(address + 2, (ushort)(value & 0xFFFF));
        }

        /// <summary>
        /// Notifies this component about Write Observed.
        /// </summary>
        private void NotifyWriteObserved(uint address, int size, uint value, AmigaAddressRegion region)
        {
            Action<uint, int, uint, AmigaAddressRegion> handler = WriteObserved;
            if (handler != null)
            {
                handler(address, size, value, region);
            }
        }

        /// <summary>
        /// Notifies this component about Read Observed.
        /// </summary>
        private void NotifyReadObserved(uint address, int size, uint value, AmigaAddressRegion region)
        {
            Action<uint, int, uint, AmigaAddressRegion> handler = ReadObserved;
            if (handler != null)
            {
                handler(address, size, value, region);
            }
        }

        /// <summary>
        /// Updates CIA A Floppy Inputs from current emulator state.
        /// </summary>
        private void UpdateCiaAFloppyInputs()
        {
            if (_ciaA == null)
            {
                return;
            }

            if (_ciaB != null)
            {
                _floppyController.ApplyCiaBPort(_ciaB.Prb, _ciaB.Ddrb);
            }

            byte mask = FloppyController.CiaAStatusMask;
            byte value = _floppyController.BuildCiaAStatusBits();
            if (_ciaAPortAInputMaskProvider != null && _ciaAPortAInputValueProvider != null)
            {
                byte extraMask = _ciaAPortAInputMaskProvider();
                byte extraValue = (byte)(_ciaAPortAInputValueProvider() & extraMask);
                value = (byte)((value & (byte)~extraMask) | extraValue);
                mask = (byte)(mask | extraMask);
            }

            _ciaA.SetPortAInputBits(mask, value);
        }

        /// <summary>
        /// Returns whether Kickstart Overlay Read is true.
        /// </summary>
        private bool IsKickstartOverlayRead(uint address)
        {
            return RomOverlayAtZero && _kickstart.IsPresent && address < (uint)_kickstart.OverlaySize && address <= MemoryMap.ChipRamEnd;
        }

        /// <summary>
        /// Returns whether a decoded address region is part of the Agnus/custom-chip bus.
        /// </summary>
        private static bool RegionRequiresChipBus(AmigaAddressRegion region)
        {
            return region == AmigaAddressRegion.ChipRam ||
                region == AmigaAddressRegion.SlowRam ||
                region == AmigaAddressRegion.CustomRegisters;
        }

        /// <summary>
        /// Returns whether a CPU bus read targets write-only BPLCON display-control registers.
        /// </summary>
        private static bool IsBplconWriteOnlyRead(uint address)
        {
            uint offset = address & 0x01FEu;
            return offset == 0x0100u || offset == 0x0102u || offset == 0x0104u;
        }

        /// <summary>
        /// Returns whether CIA A Byte Lane is true.
        /// </summary>
        private static bool IsCiaAByteLane(uint address)
        {
            return (address & 1u) != 0;
        }

        /// <summary>
        /// Returns whether CIA B Byte Lane is true.
        /// </summary>
        private static bool IsCiaBByteLane(uint address)
        {
            return (address & 1u) == 0;
        }
    }
}
