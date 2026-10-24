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
    public sealed class AmigaBus : IM68kDeferredCiaBus, IM68kBusArbitration, IM68kResetVectorProvider, IM68kPlanningBus, IM68kCacheBus, IM68kResetSignalSink
    {
        public bool IsInstructionCacheable(uint physicalAddress)
        {
            AmigaAddressRegion region = GetRegion(physicalAddress);
            return (region == AmigaAddressRegion.ChipRam || region == AmigaAddressRegion.SlowRam ||
                region == AmigaAddressRegion.FastRam || region == AmigaAddressRegion.ZorroIIIRam ||
                region == AmigaAddressRegion.KickstartRom) &&
                GetRegion(unchecked(physicalAddress + 3)) == region;
        }
        public bool IsDataCacheable(uint physicalAddress)
        {
            // Chip/Slow RAM is DMA-visible and data-cache inhibited on the
            // Amiga boards. Instruction fetches remain independently cachable.
            AmigaAddressRegion region = GetRegion(physicalAddress);
            return (region == AmigaAddressRegion.FastRam || region == AmigaAddressRegion.ZorroIIIRam ||
                region == AmigaAddressRegion.KickstartRom) &&
                GetRegion(unchecked(physicalAddress + 3)) == region;
        }
        public GayleIdeController HardDisk { get; }
        public ZorroRamAutoConfig AutoConfig { get; }
        public ZorroIIIRamAutoConfig ZorroIII { get; }
        public Msm6242Clock BatteryClock { get; }
        public Rp5c01Clock RicohClock { get; }
        public GaylePcmciaController Pcmcia { get; }
        public DesktopMotherboardResources DesktopResources { get; }
        private readonly Func<uint, bool> _scsiChipRam, _scsiDmaMemory;
        private readonly Func<uint, byte> _scsiDmaRead;
        private readonly Action<uint, byte> _scsiDmaWrite;
        public bool TickStorageDma(bool chipBusAvailable) => HardDisk.Scsi != null && HardDisk.Enabled &&
            HardDisk.Scsi.TickDma(chipBusAvailable, _scsiChipRam, _scsiDmaMemory, _scsiDmaRead, _scsiDmaWrite);
        private readonly bool _hasMotherboardRam;
        private readonly uint _motherboardRamStart;
        private uint FastRamStart => _hasMotherboardRam ? _motherboardRamStart : AutoConfig.BaseAddress;
        private readonly bool _hasPcmcia;
        private readonly uint _addressMask;
        private readonly byte _unmappedReadValue;
        private readonly bool _hasBatteryClock;
        public void BeginInstructionPlanning() { HardDisk.BeginPlanning(); if (_hasBatteryClock) BatteryClock.BeginPlanning(); if (_hasMotherboardRam) RicohClock.BeginPlanning(); }
        public void EndInstructionPlanning() { HardDisk.EndPlanning(); if (_hasBatteryClock) BatteryClock.EndPlanning(); if (_hasMotherboardRam) RicohClock.EndPlanning(); }
        public void ResetExpansionDevices() { HardDisk.Reset(); AutoConfig.Reset(); ZorroIII.Reset(); if (_hasPcmcia) Pcmcia.ResetMotherboard(); }
        public void SaveExpansionState(BinaryWriter writer) { HardDisk.SaveState(writer); AutoConfig.SaveState(writer); }
        public void LoadExpansionState(BinaryReader reader) { HardDisk.LoadState(reader); AutoConfig.LoadState(reader); }
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
        private readonly uint _chipRamDecodeEnd;
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
        private readonly bool _canDevelopA1000;
        public A1000ControlStore DevelopmentA1000ControlStore { get; private set; }
        public bool CpuResetSignalConnected => DevelopmentA1000ControlStore != null;
        public event Action DevelopmentA1000CpuReset;
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

            HardDisk = new GayleIdeController(model.IdeModel) { Enabled = model.HasBuiltInHardDiskController };
            _canDevelopA1000 = model.Machine == AmigaMachineModel.A500 && model.Cpu == AmigaCpuModel.M68000 &&
                model.Chipset == AmigaChipset.Ocs && model.SlowRamBytes == 0 && model.FastRamBytes == 0 &&
                (model.ChipRamBytes == 256 * 1024 || model.ChipRamBytes == 512 * 1024);
            _addressMask = model.Cpu == AmigaCpuModel.M68000 || model.Cpu == AmigaCpuModel.M68EC020
                ? 0x00FFFFFFu : uint.MaxValue;
            _unmappedReadValue = model.UnmappedReadValue;
            _hasPcmcia = model.HasPcmcia;
            Pcmcia = new GaylePcmciaController(_hasPcmcia, model.FastRamBytes <= 4 * 1024 * 1024);
            if (_hasPcmcia) HardDisk.Pcmcia = Pcmcia;
            BatteryClock = new Msm6242Clock(model.HasBatteryClock && !model.HasRicohClock);
            RicohClock = new Rp5c01Clock(model.HasRicohClock);
            DesktopResources = new DesktopMotherboardResources(model.HasDesktopResources, model.FastRamBytes);
            _hasMotherboardRam = model.HasDesktopResources;
            _motherboardRamStart = model.MotherboardFastRamStart;
            _hasBatteryClock = BatteryClock.Enabled;
            _chipRam = new ChipRam(model.ChipRamBytes);
            _slowRam = new SlowRam(model.SlowRamBytes);
            _fastRam = new FastRam(model.FastRamBytes);
            if (model.HasBuiltInScsi)
            {
                _scsiChipRam = a => a < (uint)model.ChipRamBytes;
                _scsiDmaMemory = a => a < (uint)model.ChipRamBytes ||
                    (a >= _motherboardRamStart && a - _motherboardRamStart < (uint)model.FastRamBytes);
                // DMA sees physical RAM even while the CPU's ROM overlay is enabled.
                _scsiDmaRead = a => a < (uint)model.ChipRamBytes ? _chipRam.Read(a) : _fastRam.Read(a - _motherboardRamStart);
                _scsiDmaWrite = (a, v) => { if (a < (uint)model.ChipRamBytes) _chipRam.Write(a, v); else _fastRam.Write(a - _motherboardRamStart, v); };
            }
            AutoConfig = new ZorroRamAutoConfig(_hasMotherboardRam ? 0 : model.FastRamBytes);
            ZorroIII = new ZorroIIIRamAutoConfig(model.ZorroIIIRamBytes);
            _chipRamEnd = model.ChipRamBytes > 0
                ? (uint)model.ChipRamBytes - 1u
                : MemoryMap.ChipRamEnd;
            // The CPU's Chip RAM aperture is 2 MiB. Unconnected address
            // lines alias the fitted RAM; they do not create an empty bank.
            // Keep the physical size separate for reset vectors and storage DMA.
            // A 512 KiB ECS development board can have an unpopulated bank;
            // native ECS/AGA profiles all have at least 1 MiB installed.
            _chipRamDecodeEnd = model.Chipset == AmigaChipset.Ocs || model.ChipRamBytes >= 1024 * 1024
                ? 0x1FFFFFu : _chipRamEnd;
            _customRegisters = customRegisters ?? new CustomRegisterMap();
            _paula = paula;
            _ciaA = ciaA;
            _ciaB = ciaB;
            _floppyController = new FloppyController();
            _kickstart = new KickstartRom();
            RomOverlayAtZero = true;
            _lastBusValue = 0xFF;
        }

        public bool RomOverlayAtZero { get; set; }

        // The EC processors wrap their physical address lines; a full 32-bit
        // processor must leave high, unpopulated addresses unmapped for ROM probes.
        private uint Normalize(uint address) => address & _addressMask;

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
            DevelopmentA1000ControlStore = null;
        }

        /// <summary>Opt-in decoder bring-up on an unexpanded OCS/68000 test board; not a native A1000 profile.</summary>
        public void LoadDevelopmentA1000Bootstrap(byte[] bootstrap)
        {
            if (!_canDevelopA1000) throw new InvalidOperationException("A1000 decoder bring-up requires an unexpanded 256/512 KiB OCS/68000 board.");
            var store = new A1000ControlStore(bootstrap);
            _kickstart = new KickstartRom();
            DevelopmentA1000ControlStore = store;
        }

        public void AssertCpuReset()
        {
            // Other motherboard RESET wiring is outside this development path.
            if (DevelopmentA1000ControlStore == null) return;
            DevelopmentA1000ControlStore.Reset();
            DevelopmentA1000CpuReset?.Invoke();
        }

        public void ResetBootDecoder() => DevelopmentA1000ControlStore?.Reset();

        private byte ReadBootByte(uint offset) => DevelopmentA1000ControlStore != null
            ? DevelopmentA1000ControlStore.ReadByte(A1000ControlStore.WindowStart + offset)
            : _kickstart.Read(offset);

        /// <summary>
        /// Runs Clear Writable Memory.
        /// </summary>
        public void ClearWritableMemory()
        {
            _chipRam.Clear();
            _slowRam.Clear();
            _fastRam.Clear();
            ZorroIII.ClearRam();
            DevelopmentA1000ControlStore?.PowerCycle();
            _customRegisters.Reset();
        }

        /// <summary>
        /// Writes bus-owned memory, floppy, ROM overlay, and open-bus state to a savestate.
        /// </summary>
        public void SaveState(BinaryWriter writer) => SaveState(writer, false);

        internal void SaveState(BinaryWriter writer, bool includeA1000ControlStore)
        {
            if (includeA1000ControlStore != (DevelopmentA1000ControlStore != null))
                throw new InvalidDataException("A1000 decoder state requires the matching machine-state header.");
            if (includeA1000ControlStore) DevelopmentA1000ControlStore.SaveState(writer);
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
        public void LoadState(BinaryReader reader) => LoadState(reader, false);

        internal void LoadState(BinaryReader reader, bool includeA1000ControlStore)
        {
            if (includeA1000ControlStore != (DevelopmentA1000ControlStore != null))
                throw new InvalidDataException("Savestate A1000 decoder configuration does not match this machine.");
            var restoredStore = includeA1000ControlStore ? DevelopmentA1000ControlStore.ReadStateCopy(reader) : null;
            RomOverlayAtZero = reader.ReadBoolean();
            _lastBusValue = reader.ReadByte();
            StateSerializer.ReadObjectFields(reader, _chipRam);
            StateSerializer.ReadObjectFields(reader, _slowRam);
            StateSerializer.ReadObjectFields(reader, _fastRam);
            StateSerializer.ReadObjectFields(reader, _kickstart);
            StateSerializer.ReadObjectFields(reader, _floppyController);
            DevelopmentA1000ControlStore = restoredStore;
            // CIA control lines still belong to the previous machine state here.
            // AmigaSystem refreshes them after both CIAs have been restored;
            // applying them now would clock a spurious drive-select motor latch.
        }

        /// <summary>
        /// Reapplies CIA-controlled floppy output and input lines after restoring CIA state.
        /// </summary>
        public void RefreshFloppyControlLines()
        {
            UpdateCiaAFloppyInputs();
        }

        /// <summary>
        /// Returns the Region.
        /// </summary>
        public AmigaAddressRegion GetRegion(uint address)
        {
            address = Normalize(address);

            if (address <= _chipRamDecodeEnd)
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
                address >= FastRamStart &&
                address < FastRamStart + (uint)_fastRam.Size &&
                (_hasMotherboardRam || address <= MemoryMap.FastRamEnd))
            {
                return AmigaAddressRegion.FastRam;
            }

            if (ZorroIII.MapsRam(address)) return AmigaAddressRegion.ZorroIIIRam;
            if (ZorroIII.MapsConfig(address)) return AmigaAddressRegion.ZorroIIIConfig;

            if (address >= MemoryMap.CustomStart && address <= MemoryMap.CustomEnd)
            {
                return AmigaAddressRegion.CustomRegisters;
            }

            if (address >= MemoryMap.KickstartStart && address <= MemoryMap.KickstartEnd)
            {
                return AmigaAddressRegion.KickstartRom;
            }

            if (_hasMotherboardRam && DesktopResources.Maps(address)) return AmigaAddressRegion.MotherboardResources;
            if (HardDisk.Maps(address)) return AmigaAddressRegion.GayleIde;
            if (AutoConfig.Maps(address)) return AmigaAddressRegion.AutoConfig;
            if (_hasPcmcia && Pcmcia.Maps(address)) return AmigaAddressRegion.Pcmcia;
            if (BatteryClock.Maps(address) || (_hasMotherboardRam && RicohClock.Maps(address))) return AmigaAddressRegion.BatteryClock;
            return AmigaAddressRegion.Unmapped;
        }

        /// <summary>
        /// Returns whether the CPU access must wait for a free Agnus-controlled bus slot.
        /// </summary>
        public bool RequiresChipBus(uint address, int size)
        {
            uint normalized = Normalize(address);
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

            uint last = Normalize(normalized + (uint)size - 1u);
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
            if (DevelopmentA1000ControlStore == null && RomOverlayAtZero &&
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
            address = Normalize(address);
            byte value;

            if (IsKickstartOverlayRead(address))
            {
                value = ReadBootByte(address);
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
                    value = _fastRam.Read(address - FastRamStart);
                    break;
                case AmigaAddressRegion.ZorroIIIRam:
                    value = ZorroIII.ReadRam(address);
                    break;
                case AmigaAddressRegion.ZorroIIIConfig:
                    value = ZorroIII.ReadConfig(address);
                    break;
                case AmigaAddressRegion.MotherboardResources:
                    value = DesktopResources.ReadByte(address);
                    break;
                case AmigaAddressRegion.GayleIde:
                    value = HardDisk.ReadByte(address);
                    break;
                case AmigaAddressRegion.BatteryClock:
                    value = _hasMotherboardRam ? RicohClock.ReadByte(address) : BatteryClock.ReadByte(address);
                    break;
                case AmigaAddressRegion.Pcmcia:
                    value = Pcmcia.ReadByte(address);
                    break;
                case AmigaAddressRegion.AutoConfig:
                    value = AutoConfig.ReadByte(address);
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
                    value = ReadBootByte(address - MemoryMap.KickstartStart);
                    break;
                default:
                    value = _unmappedReadValue;
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
            address = Normalize(address);
            if (IsKickstartOverlayRead(address))
            {
                return ReadBootByte(address);
            }

            AmigaAddressRegion region = GetRegion(address);
            switch (region)
            {
                case AmigaAddressRegion.ChipRam:
                    return _chipRam.Read(address);
                case AmigaAddressRegion.SlowRam:
                    return _slowRam.Read(address - MemoryMap.SlowRamStart);
                case AmigaAddressRegion.FastRam:
                    return _fastRam.Read(address - FastRamStart);
                case AmigaAddressRegion.ZorroIIIRam:
                    return ZorroIII.PeekRam(address);
                case AmigaAddressRegion.ZorroIIIConfig:
                    return ZorroIII.PeekConfig(address);
                case AmigaAddressRegion.MotherboardResources:
                    return DesktopResources.ReadByte(address);
                case AmigaAddressRegion.GayleIde:
                    return HardDisk.ReadByte(address, true);
                case AmigaAddressRegion.BatteryClock:
                    return _hasMotherboardRam ? RicohClock.ReadByte(address) : BatteryClock.ReadByte(address);
                case AmigaAddressRegion.Pcmcia:
                    return Pcmcia.ReadByte(address, true);
                case AmigaAddressRegion.AutoConfig:
                    return AutoConfig.ReadByte(address);
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
                    return ReadBootByte(address - MemoryMap.KickstartStart);
                default:
                    return _unmappedReadValue;
            }
        }

        /// <summary>
        /// Reads a big-endian word from the Amiga bus, honoring custom-register word reads.
        /// </summary>
        public ushort ReadWord(uint address)
        {
            uint normalized = Normalize(address);
            if (ReadObserved == null && TryPeekMemoryWord(normalized, out ushort memoryWord))
            {
                _lastBusValue = (byte)memoryWord;
                return memoryWord;
            }
            if (HardDisk.Maps(normalized) && HardDisk.IsDataPort(normalized))
            {
                ushort value = HardDisk.ReadData();
                NotifyReadObserved(normalized, 2, value, AmigaAddressRegion.GayleIde);
                return value;
            }
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
            uint normalized = Normalize(address);
            if (TryPeekMemoryWord(normalized, out ushort memoryWord)) return memoryWord;
            if (HardDisk.Maps(normalized) && HardDisk.IsDataPort(normalized)) return HardDisk.ReadData(true);
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

        // A word wholly inside memory needs only one decode. Peripheral, overlay
        // boundary and address-wrap transfers retain individual byte semantics.
        private bool TryPeekMemoryWord(uint address, out ushort value)
        {
            value = 0;
            if (address < _chipRamDecodeEnd)
            {
                bool overlay = IsKickstartOverlayRead(address);
                if (overlay != IsKickstartOverlayRead(address + 1)) return false;
                value = overlay ? (ushort)((ReadBootByte(address) << 8) | ReadBootByte(address + 1))
                    : (ushort)((_chipRam.Read(address) << 8) | _chipRam.Read(address + 1));
                return true;
            }
            if (address >= MemoryMap.KickstartStart && address < MemoryMap.KickstartEnd)
            {
                uint offset = address - MemoryMap.KickstartStart;
                value = (ushort)((ReadBootByte(offset) << 8) | ReadBootByte(offset + 1)); return true;
            }
            if (_slowRam.Enabled && address >= MemoryMap.SlowRamStart && address < MemoryMap.SlowRamStart + (uint)_slowRam.Size - 1)
            {
                uint offset = address - MemoryMap.SlowRamStart;
                value = (ushort)((_slowRam.Read(offset) << 8) | _slowRam.Read(offset + 1)); return true;
            }
            if (_fastRam.Enabled && address >= FastRamStart && address < FastRamStart + (uint)_fastRam.Size - 1 &&
                (_hasMotherboardRam || address < MemoryMap.FastRamEnd))
            {
                uint offset = address - FastRamStart;
                value = (ushort)((_fastRam.Read(offset) << 8) | _fastRam.Read(offset + 1)); return true;
            }
            return false;
        }

        /// <summary>
        /// Reads a big-endian longword from the Amiga bus as two consecutive words.
        /// </summary>
        public uint ReadLong(uint address)
        {
            uint normalized = Normalize(address);
            return (uint)((ReadWord(normalized) << 16) | ReadWord(normalized + 2));
        }

        /// <summary>
        /// Reads a big-endian longword without applying bus read side effects.
        /// </summary>
        public uint PeekLong(uint address)
        {
            uint normalized = Normalize(address);
            return (uint)((PeekWord(normalized) << 16) | PeekWord(normalized + 2));
        }

        /// <summary>
        /// Clears only the CIA ICR bits that were visible to a previously sampled deferred CPU read.
        /// </summary>
        public bool RetireCiaIcrRead(uint address, int size, uint sampledValue)
        {
            uint normalized = Normalize(address);
            if (size == 1)
            {
                bool retired = RetireCiaIcrByteRead(normalized, (byte)sampledValue);
                if (retired)
                {
                    NotifyReadObserved(normalized, 1, sampledValue & 0xFFu, GetRegion(normalized));
                }

                return retired;
            }

            if (size == 2)
            {
                bool high = RetireCiaIcrByteRead(normalized, (byte)(sampledValue >> 8));
                bool low = RetireCiaIcrByteRead(normalized + 1, (byte)sampledValue);
                if (high || low)
                {
                    NotifyReadObserved(normalized, 2, sampledValue & 0xFFFFu, GetRegion(normalized));
                }

                return high || low;
            }

            return false;
        }

        /// <summary>
        /// Writes one byte to the normalized Amiga address map and dispatches device side effects.
        /// </summary>
        public void WriteByte(uint address, byte value)
        {
            address = Normalize(address);
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
                case AmigaAddressRegion.KickstartRom:
                    DevelopmentA1000ControlStore?.WriteByte(address, value);
                    break;
                case AmigaAddressRegion.SlowRam:
                    _slowRam.Write(address - MemoryMap.SlowRamStart, value);
                    break;
                case AmigaAddressRegion.FastRam:
                    _fastRam.Write(address - FastRamStart, value);
                    break;
                case AmigaAddressRegion.ZorroIIIRam:
                    ZorroIII.WriteRam(address, value);
                    break;
                case AmigaAddressRegion.ZorroIIIConfig:
                    ZorroIII.WriteConfigByte(address, value);
                    break;
                case AmigaAddressRegion.MotherboardResources:
                    DesktopResources.WriteByte(address, value);
                    break;
                case AmigaAddressRegion.GayleIde:
                    HardDisk.WriteByte(address, value);
                    break;
                case AmigaAddressRegion.BatteryClock:
                    if (_hasMotherboardRam) RicohClock.WriteByte(address, value);
                    else BatteryClock.WriteByte(address, value);
                    break;
                case AmigaAddressRegion.Pcmcia:
                    Pcmcia.WriteByte(address, value);
                    break;
                case AmigaAddressRegion.AutoConfig:
                    AutoConfig.WriteByte(address, value);
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
            if (ZorroIII.MapsConfig(Normalize(address)))
            {
                ZorroIII.WriteConfigWord(Normalize(address), value);
                NotifyWriteObserved(Normalize(address), 2, value, AmigaAddressRegion.ZorroIIIConfig);
                return;
            }
            if (HardDisk.Maps(Normalize(address)) && HardDisk.IsDataPort(Normalize(address)))
            {
                HardDisk.WriteData(value);
                NotifyWriteObserved(Normalize(address), 2, value, AmigaAddressRegion.GayleIde);
                return;
            }
            uint normalized = Normalize(address);
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
            return RomOverlayAtZero && (DevelopmentA1000ControlStore != null ? address < 512 * 1024u :
                _kickstart.IsPresent && address < (uint)_kickstart.OverlaySize) && address <= MemoryMap.ChipRamEnd;
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

        /// <summary>
        /// Clears sampled ICR bits for the CIA byte lane addressed by one CPU bus byte.
        /// </summary>
        private bool RetireCiaIcrByteRead(uint address, byte sampledValue)
        {
            uint normalized = Normalize(address);
            if (((normalized >> 8) & 0x0Fu) != 13u)
            {
                return false;
            }

            AmigaAddressRegion region = GetRegion(normalized);
            if (region == AmigaAddressRegion.CiaA && IsCiaAByteLane(normalized) && _ciaA != null)
            {
                _ciaA.ClearInterruptRequest(sampledValue);
                return true;
            }

            if (region == AmigaAddressRegion.CiaB && IsCiaBByteLane(normalized) && _ciaB != null)
            {
                _ciaB.ClearInterruptRequest(sampledValue);
                return true;
            }

            return false;
        }
    }
}
