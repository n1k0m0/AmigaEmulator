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
using System.Diagnostics;
using System.IO;
using AmigaEmulator.Core.CustomChips;
using AmigaEmulator.Core.DevTools;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.Cpu;
using AmigaEmulator.Core.Cia;
using AmigaEmulator.Core.Input;
using AmigaEmulator.Core.SaveStates;
using AmigaEmulator.Core.Video;

namespace AmigaEmulator.Core
{
    /// <summary>
    /// Coordinates the first deterministic A500 core slice.
    /// </summary>
    public sealed class AmigaSystem : IDisposable
    {
        /// <summary>
        /// Crops the stable PAL presentation window out of the wider internal Denise raster.
        /// </summary>
        private const int PresentationCropLeft = 128;

        /// <summary>
        /// Gives Denise enough horizontal room to render wide OCS overscan fetches before presentation.
        /// </summary>
        private const int HorizontalOverscanPixels = 128;

        /// <summary>
        /// Scans the top overscan rows for isolated transition artifacts before presentation.
        /// </summary>
        private const int TopOverscanArtifactRows = 24;

        /// <summary>
        /// Keeps the cleanup narrow enough that real top-edge artwork is preserved.
        /// </summary>
        private const int MaxTopOverscanArtifactPixels = 6000;

        /// <summary>
        /// Keeps transient black frames from collapsing the presentation source to a line or two.
        /// </summary>
        private const int MinimumTrimmedPresentationHeight = 64;

        /// <summary>
        /// Crops tiny PAL overscan height differences instead of vertically resampling HUD lines.
        /// </summary>
        private const int SmallVerticalOverscanCropTolerance = 8;

        /// <summary>
        /// Runs object.
        /// </summary>
        private readonly object _syncRoot = new object();
        /// <summary>
        /// Stores model.
        /// </summary>
        private readonly AmigaModel _model;
        /// <summary>
        /// Stores accuracy Options.
        /// </summary>
        private readonly AmigaAccuracyOptions _accuracyOptions;
        /// <summary>
        /// Stores frame Buffer.
        /// </summary>
        private readonly AmigaFrameBuffer _frameBuffer;
        /// <summary>
        /// Stores presented Frame Buffer.
        /// </summary>
        private readonly AmigaFrameBuffer _presentedFrameBuffer;
        /// <summary>
        /// Stores bus.
        /// </summary>
        private readonly AmigaBus _bus;
        /// <summary>
        /// Stores agnus.
        /// </summary>
        private readonly Agnus _agnus;
        /// <summary>
        /// Stores denise.
        /// </summary>
        private readonly Denise _denise;
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
        /// Stores input.
        /// </summary>
        private readonly AmigaInputDevice _input;
        /// <summary>
        /// Stores CPU.
        /// </summary>
        private readonly M68kCpu _cpu;
        /// <summary>
        /// Stores last Trace Sample.
        /// </summary>
        private MachineTraceSample _lastTraceSample;
        /// <summary>
        /// Stores CIA Clock Divider.
        /// </summary>
        private int _ciaClockDivider;
        /// <summary>
        /// Stores CIA A Irq Last.
        /// </summary>
        private bool _ciaAIrqLast;
        /// <summary>
        /// Stores CIA B Irq Last.
        /// </summary>
        private bool _ciaBIrqLast;
        /// <summary>
        /// Stores the interrupt level currently visible on the CPU IPL pins.
        /// </summary>
        private int _cpuVisibleInterruptLevel;
        /// <summary>
        /// Stores the most recent custom/CIA interrupt level waiting to reach the CPU pins.
        /// </summary>
        private int _cpuPendingInterruptLevel;
        /// <summary>
        /// Counts down the chip-cycle delay before the sampled interrupt level reaches IPL pins.
        /// </summary>
        private int _cpuInterruptDelayCycles;
        /// <summary>
        /// Keeps the saved presented image visible until one partial post-load frame has been discarded.
        /// </summary>
        private bool _suppressNextPresentationAfterLoad;

        /// <summary>
        /// Initializes a new instance of the AmigaSystem class.
        /// </summary>
        public AmigaSystem()
            : this(AmigaModel.A500Pal, AmigaAccuracyOptions.Compatible)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AmigaSystem class.
        /// </summary>
        public AmigaSystem(AmigaModel model)
            : this(model, AmigaAccuracyOptions.Compatible)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AmigaSystem class.
        /// </summary>
        public AmigaSystem(AmigaModel model, AmigaAccuracyOptions accuracyOptions)
        {
            _model = model ?? AmigaModel.A500Pal;
            _accuracyOptions = (accuracyOptions ?? AmigaAccuracyOptions.Compatible).Clone();
            _frameBuffer = new AmigaFrameBuffer(_model.VisibleWidth + PresentationCropLeft + HorizontalOverscanPixels, _model.LinesPerFrame);
            _presentedFrameBuffer = new AmigaFrameBuffer(_model.VisibleWidth, _model.VisibleHeight);
            _paula = new Paula(_model.ChipRamBytes);
            _ciaA = new Cia8520("CIA-A");
            _ciaB = new Cia8520("CIA-B");
            _input = new AmigaInputDevice();
            _agnus = new Agnus(_model);
            _bus = new AmigaBus(_model, _agnus.CustomRegisters, _paula, _ciaA, _ciaB);
            _bus.WriteObserved += NotifyBusWriteSideEffects;
            _input.AttachCiaA(_ciaA);
            _agnus.CustomRegisters.AttachInputReaders(
                () => _input.ReadJoyDat(AmigaControlPort.Port0),
                () => _input.ReadJoyDat(AmigaControlPort.Port1),
                potgo => _input.ReadPotInp(potgo));
            _bus.AttachCiaAPortAInputProvider(_input.GetCiaAPortAInputMask, _input.GetCiaAPortAInputValue);
            _agnus.AttachChipWordReader(ReadChipRamWord);
            _denise = new Denise(_frameBuffer, _agnus.Bitplanes, _agnus.Sprites, _agnus.Collisions, PresentationCropLeft, BitplaneUnit.PalVisibleTop);
            _paula.AttachDisk(
                _bus.FloppyController,
                WriteChipRamWord,
                ReadChipRamWord,
                _agnus.CustomRegisters.RequestInterrupt,
                () => _agnus.CustomRegisters.DmaControl,
                () => _agnus.CustomRegisters.AdkControl);
            _agnus.Copper.CustomRegisterWritten += NotifyCopperRegisterWriteSideEffects;
            _agnus.AttachDiskDmaStatus(() => _paula.DiskDmaActive);
            _agnus.Blitter.AttachChipMemory(ReadChipRamWord, WriteChipRamWord);
            _agnus.Blitter.AttachInterruptRequester(_agnus.CustomRegisters.RequestInterrupt);
            _bus.AttachBlitter(_agnus.Blitter);
            _cpu = new M68kCpu(_bus);
            _cpu.TraceEnabled = _accuracyOptions.EnableCpuTrace;
            _ciaA.OverlayChanged += delegate(bool enabled)
            {
                _bus.RomOverlayAtZero = enabled;
            };
            Reset(true);
        }

        /// <summary>
        /// Gets Model.
        /// </summary>
        public AmigaModel Model
        {
            get { return _model; }
        }

        /// <summary>
        /// Gets Accuracy Options.
        /// </summary>
        public AmigaAccuracyOptions AccuracyOptions
        {
            get { return _accuracyOptions.Clone(); }
        }

        /// <summary>
        /// Updates the Accuracy Options.
        /// </summary>
        public void SetAccuracyOptions(AmigaAccuracyOptions options)
        {
            lock (_syncRoot)
            {
                options = options ?? AmigaAccuracyOptions.Fast;
                _accuracyOptions.Name = options.Name;
                _accuracyOptions.EnableDmaSlotTrace = options.EnableDmaSlotTrace;
                _accuracyOptions.EnableCpuTrace = options.EnableCpuTrace;
                _accuracyOptions.StrictChipBusArbitration = options.StrictChipBusArbitration;
                _cpu.TraceEnabled = _accuracyOptions.EnableCpuTrace;
            }
        }

        /// <summary>
        /// Gets Bus.
        /// </summary>
        public AmigaBus Bus
        {
            get { return _bus; }
        }

        /// <summary>
        /// Gets CPU.
        /// </summary>
        public M68kCpu Cpu
        {
            get { return _cpu; }
        }

        /// <summary>
        /// Gets Agnus.
        /// </summary>
        public Agnus Agnus
        {
            get { return _agnus; }
        }

        /// <summary>
        /// Gets Denise.
        /// </summary>
        public Denise Denise
        {
            get { return _denise; }
        }

        /// <summary>
        /// Gets Paula.
        /// </summary>
        public Paula Paula
        {
            get { return _paula; }
        }

        /// <summary>
        /// Gets CIA A.
        /// </summary>
        public Cia8520 CiaA
        {
            get { return _ciaA; }
        }

        /// <summary>
        /// Gets CIA B.
        /// </summary>
        public Cia8520 CiaB
        {
            get { return _ciaB; }
        }

        /// <summary>
        /// Gets Input.
        /// </summary>
        public AmigaInputDevice Input
        {
            get { return _input; }
        }

        /// <summary>
        /// Gets Frame Buffer.
        /// </summary>
        public AmigaFrameBuffer FrameBuffer
        {
            get { return _frameBuffer; }
        }

        /// <summary>
        /// Gets Presented Frame Buffer.
        /// </summary>
        public AmigaFrameBuffer PresentedFrameBuffer
        {
            get { return _presentedFrameBuffer; }
        }

        /// <summary>
        /// Returns the current horizontal source window used for PAL presentation.
        /// </summary>
        public string GetPresentationDebugInfo()
        {
            lock (_syncRoot)
            {
                GetPresentationSourceWindow(out int sourceLeft, out int sourceTop, out int sourceWidth, out int sourceHeight);
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "framebuffer:{0}x{1} presented:{2}x{3} source:{4},{5}+{6}x{7}",
                    _frameBuffer.Width,
                    _frameBuffer.Height,
                    _presentedFrameBuffer.Width,
                    _presentedFrameBuffer.Height,
                    sourceLeft,
                    sourceTop,
                    sourceWidth,
                    sourceHeight);
            }
        }

        /// <summary>
        /// Gets Last Trace Sample.
        /// </summary>
        public MachineTraceSample LastTraceSample
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lastTraceSample;
                }
            }
        }

        /// <summary>
        /// Loads Kickstart into emulator state.
        /// </summary>
        public void LoadKickstart(byte[] romBytes)
        {
            lock (_syncRoot)
            {
                _bus.LoadKickstart(romBytes);
                Reset(true);
            }
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset(bool powerCycle)
        {
            lock (_syncRoot)
            {
                if (powerCycle)
                {
                    _bus.ClearWritableMemory();
                }

                _bus.RomOverlayAtZero = true;
                _agnus.Reset();
                _denise.Reset();
                _presentedFrameBuffer.Clear(0xFF0C1020);
                _paula.Reset();
                _ciaA.Reset();
                _ciaB.Reset();
                _input.Reset();
                _ciaClockDivider = 0;
                _ciaAIrqLast = false;
                _ciaBIrqLast = false;
                _cpuVisibleInterruptLevel = 0;
                _cpuPendingInterruptLevel = 0;
                _cpuInterruptDelayCycles = 0;
                _suppressNextPresentationAfterLoad = false;
                _cpu.Reset();
                _lastTraceSample = CreateTraceSample(null);
            }
        }

        /// <summary>
        /// Writes the complete machine state in dependency order.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            lock (_syncRoot)
            {
                writer.Write(2);
                _bus.SaveState(writer);
                _cpu.SaveState(writer);
                _agnus.SaveState(writer);
                _paula.SaveState(writer);
                _ciaA.SaveState(writer);
                _ciaB.SaveState(writer);
                _input.SaveState(writer);
                _frameBuffer.SaveState(writer);
                _presentedFrameBuffer.SaveState(writer);
                writer.Write(_ciaClockDivider);
                writer.Write(_ciaAIrqLast);
                writer.Write(_ciaBIrqLast);
                writer.Write(_cpuVisibleInterruptLevel);
                writer.Write(_cpuPendingInterruptLevel);
                writer.Write(_cpuInterruptDelayCycles);
            }
        }

        /// <summary>
        /// Restores the complete machine state in the same dependency order used by <see cref="SaveState"/>.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            lock (_syncRoot)
            {
                int version = reader.ReadInt32();
                if (version != 1 && version != 2)
                {
                    throw new InvalidDataException("Unsupported Amiga system savestate version.");
                }

                _bus.LoadState(reader);
                _cpu.LoadState(reader);
                _agnus.LoadState(reader);
                _paula.LoadState(reader);
                _ciaA.LoadState(reader);
                _ciaB.LoadState(reader);
                _bus.RefreshFloppyControlLines();
                _input.LoadState(reader);
                _frameBuffer.LoadState(reader);
                _presentedFrameBuffer.LoadState(reader);
                _ciaClockDivider = reader.ReadInt32();
                _ciaAIrqLast = reader.ReadBoolean();
                _ciaBIrqLast = reader.ReadBoolean();
                if (version >= 2)
                {
                    _cpuVisibleInterruptLevel = reader.ReadInt32();
                    _cpuPendingInterruptLevel = reader.ReadInt32();
                    _cpuInterruptDelayCycles = reader.ReadInt32();
                }
                else
                {
                    _cpuVisibleInterruptLevel = _agnus.CustomRegisters.ActiveInterruptLevel;
                    _cpuPendingInterruptLevel = _cpuVisibleInterruptLevel;
                    _cpuInterruptDelayCycles = 0;
                }

                _cpu.SetInterruptLevel(_cpuVisibleInterruptLevel);
                _suppressNextPresentationAfterLoad = true;
                _lastTraceSample = CreateTraceSample(null);
            }
        }

        /// <summary>
        /// Advances the Chip Cycle state by one emulator tick.
        /// </summary>
        public void TickChipCycle()
        {
            lock (_syncRoot)
            {
                TickChipCycleCore();
            }
        }

        /// <summary>
        /// Runs the Chip Cycles routine.
        /// </summary>
        public void RunChipCycles(int cycles)
        {
            if (cycles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cycles));
            }

            lock (_syncRoot)
            {
                for (int index = 0; index < cycles; index++)
                {
                    TickChipCycleCore();
                }
            }
        }

        /// <summary>
        /// Runs the Cycles routine.
        /// </summary>
        public void RunCycles(int cycles)
        {
            RunChipCycles(cycles);
        }

        /// <summary>
        /// Runs Profile Chip Cycles.
        /// </summary>
        public AmigaSystemComponentProfile ProfileChipCycles(int cycles)
        {
            if (cycles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cycles));
            }

            var profile = new AmigaSystemComponentProfile { Cycles = cycles };
            lock (_syncRoot)
            {
                long started = Stopwatch.GetTimestamp();
                for (int index = 0; index < cycles; index++)
                {
                    long before = Stopwatch.GetTimestamp();
                    DmaSlotTraceEntry slot = _agnus.Tick();
                    long afterAgnus = Stopwatch.GetTimestamp();

                    _bus.FloppyController.Tick();
                    if (_bus.FloppyController.IndexPulsePending)
                    {
                        _ciaB.PulseFlag();
                    }

                    _paula.Tick(slot.Owner == DmaOwner.Disk, slot.Owner == DmaOwner.Audio ? slot.AudioChannelIndex : -1);
                    long afterPaula = Stopwatch.GetTimestamp();

                    TickCiaTodFromVideo();
                    TickCiasOnEClock();
                    long afterCia = Stopwatch.GetTimestamp();

                    bool cpuMayUseChipBus = !_accuracyOptions.StrictChipBusArbitration || slot.CpuCanUseChipBus;
                    M68kTraceEntry cpuTrace = _cpu.Tick(cpuMayUseChipBus);
                    ApplyPendingInterrupts();
                    long afterCpu = Stopwatch.GetTimestamp();

                    if (slot.SlotIndex == 0 && slot.BeamY == 0)
                    {
                        _denise.BeginFrame();
                    }

                    _denise.Tick(_agnus.Beam);
                    long afterDenise = Stopwatch.GetTimestamp();

                    if (_accuracyOptions.EnableCpuTrace || _accuracyOptions.EnableDmaSlotTrace)
                    {
                        _lastTraceSample = CreateTraceSample(cpuTrace);
                    }
                    long afterTrace = Stopwatch.GetTimestamp();

                    profile.AgnusTicks += afterAgnus - before;
                    profile.PaulaTicks += afterPaula - afterAgnus;
                    profile.CiaTicks += afterCia - afterPaula;
                    profile.CpuTicks += afterCpu - afterCia;
                    profile.DeniseTicks += afterDenise - afterCpu;
                    profile.TraceTicks += afterTrace - afterDenise;
                }

                profile.TotalTicks = Stopwatch.GetTimestamp() - started;
            }

            profile.StopwatchFrequency = Stopwatch.Frequency;
            return profile;
        }

        /// <summary>
        /// Gets Frame Counter.
        /// </summary>
        public long FrameCounter
        {
            get
            {
                lock (_syncRoot)
                {
                    return _agnus.Beam.Frame;
                }
            }
        }

        /// <summary>
        /// Runs Dispose.
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// Returns the CPU Debug Info.
        /// </summary>
        public string GetCpuDebugInfo()
        {
            lock (_syncRoot)
            {
                return _cpu.GetDebugInfo();
            }
        }

        /// <summary>
        /// Returns the Beam Debug Info.
        /// </summary>
        public string GetBeamDebugInfo()
        {
            lock (_syncRoot)
            {
                BeamTraceEntry beam = _agnus.Beam;
                return string.Format(
                    "frame:{0} cycle:{1} beam:{2},{3} hblank:{4} vblank:{5}",
                    beam.Frame,
                    beam.GlobalChipCycle,
                    beam.BeamX,
                    beam.BeamY,
                    beam.IsHBlank,
                    beam.IsVBlank);
            }
        }

        /// <summary>
        /// Returns the DMA Debug Info.
        /// </summary>
        public string GetDmaDebugInfo()
        {
            lock (_syncRoot)
            {
                DmaSlotTraceEntry slot = _agnus.LastDmaSlot;
                return string.Format(
                    "slot:{0} owner:{1} cpu:{2}",
                    slot.SlotIndex,
                    slot.Owner,
                    slot.CpuCanUseChipBus);
            }
        }

        /// <summary>
        /// Creates the Trace Sample.
        /// </summary>
        private MachineTraceSample CreateTraceSample(M68kTraceEntry cpuTrace)
        {
            BeamTraceEntry beam = _agnus.Beam;
            DmaSlotTraceEntry dma = _agnus.LastDmaSlot;
            return new MachineTraceSample
            {
                GlobalChipCycle = beam.GlobalChipCycle,
                Frame = beam.Frame,
                BeamX = beam.BeamX,
                BeamY = beam.BeamY,
                DmaOwner = dma.Owner.ToString(),
                DmaSlot = dma.SlotIndex,
                CpuCanUseChipBus = dma.CpuCanUseChipBus,
                CpuPc = _cpu.PC,
                CpuSr = _cpu.SR,
                CpuTrace = cpuTrace
            };
        }

        /// <summary>
        /// Advances the Chip Cycle Core state by one emulator tick.
        /// </summary>
        private void TickChipCycleCore()
        {
            DmaSlotTraceEntry slot = _agnus.Tick();
            _bus.FloppyController.Tick();
            if (_bus.FloppyController.IndexPulsePending)
            {
                _ciaB.PulseFlag();
            }

            _paula.Tick(slot.Owner == DmaOwner.Disk, slot.Owner == DmaOwner.Audio ? slot.AudioChannelIndex : -1);
            TickCiaTodFromVideo();
            TickCiasOnEClock();

            bool cpuMayUseChipBus = !_accuracyOptions.StrictChipBusArbitration || slot.CpuCanUseChipBus;
            M68kTraceEntry cpuTrace = _cpu.Tick(cpuMayUseChipBus);
            ApplyPendingInterrupts();
            if (slot.SlotIndex == 0 && slot.BeamY == 0)
            {
                _denise.BeginFrame();
            }

            _denise.Tick(_agnus.Beam);
            if (slot.SlotIndex == _model.ChipCyclesPerLine - 1 && slot.BeamY == _model.LinesPerFrame - 1)
            {
                if (_suppressNextPresentationAfterLoad)
                {
                    _suppressNextPresentationAfterLoad = false;
                }
                else
                {
                    GetPresentationSourceWindow(out int sourceLeft, out int sourceTop, out int sourceWidth, out int sourceHeight);
                    if (sourceWidth != _model.VisibleWidth || sourceHeight != _model.VisibleHeight)
                    {
                        _presentedFrameBuffer.CopyScaledWindowFrom(_frameBuffer, sourceLeft, sourceTop, sourceWidth, sourceHeight);
                    }
                    else
                    {
                        _presentedFrameBuffer.CopyWindowFrom(_frameBuffer, sourceLeft, sourceTop);
                    }

                    _presentedFrameBuffer.ClearSparseTopBorderArtifacts(TopOverscanArtifactRows, MaxTopOverscanArtifactPixels);
                }
            }

            if (_accuracyOptions.EnableCpuTrace || _accuracyOptions.EnableDmaSlotTrace)
            {
                _lastTraceSample = CreateTraceSample(cpuTrace);
            }
        }

        /// <summary>
        /// Resolves the source window selected by the emulated display registers for host presentation.
        /// </summary>
        private void GetPresentationSourceWindow(out int sourceLeft, out int sourceTop, out int sourceWidth, out int sourceHeight)
        {
            int requestedLeft = PresentationCropLeft + _agnus.Bitplanes.PresentationSourceLeft;
            int requestedWidth = Math.Max(_model.VisibleWidth, _agnus.Bitplanes.PresentationSourceWidth);
            int requestedTop = _agnus.Bitplanes.PresentationSourceTop;
            int requestedHeight = Math.Max(_model.VisibleHeight, _agnus.Bitplanes.PresentationSourceHeight);
            sourceLeft = Math.Max(0, Math.Min(requestedLeft, _frameBuffer.Width - 1));
            sourceWidth = Math.Max(1, Math.Min(requestedWidth, _frameBuffer.Width - sourceLeft));
            sourceTop = Math.Max(0, Math.Min(requestedTop, _frameBuffer.Height - 1));
            sourceHeight = Math.Max(1, Math.Min(requestedHeight, _frameBuffer.Height - sourceTop));
            TrimBlackLetterboxRows(sourceLeft, ref sourceTop, sourceWidth, ref sourceHeight);
            CropSmallVerticalOverscan(ref sourceTop, ref sourceHeight);
        }

        /// <summary>
        /// Removes solid black letterbox rows from the presentation source without cropping colored playfields.
        /// </summary>
        private void TrimBlackLetterboxRows(int sourceLeft, ref int sourceTop, int sourceWidth, ref int sourceHeight)
        {
            if (sourceHeight <= _model.VisibleHeight)
            {
                return;
            }

            int trimmedTop = sourceTop;
            int trimmedHeight = sourceHeight;
            while (trimmedHeight > 1 && IsSolidBlackSourceRow(sourceLeft, trimmedTop, sourceWidth))
            {
                trimmedTop++;
                trimmedHeight--;
            }

            while (trimmedHeight > 1 && IsSolidBlackSourceRow(sourceLeft, trimmedTop + trimmedHeight - 1, sourceWidth))
            {
                trimmedHeight--;
            }

            if (trimmedHeight >= MinimumTrimmedPresentationHeight)
            {
                sourceTop = trimmedTop;
                sourceHeight = trimmedHeight;
            }
        }

        /// <summary>
        /// Avoids row-drop artifacts when a game opens DIW only one or two PAL lines beyond the stable view.
        /// </summary>
        private void CropSmallVerticalOverscan(ref int sourceTop, ref int sourceHeight)
        {
            int extraRows = sourceHeight - _model.VisibleHeight;
            if (extraRows <= 0 || extraRows > SmallVerticalOverscanCropTolerance)
            {
                return;
            }

            int cropTop = extraRows / 2;
            sourceTop = Math.Min(sourceTop + cropTop, _frameBuffer.Height - _model.VisibleHeight);
            sourceHeight = _model.VisibleHeight;
        }

        /// <summary>
        /// Returns whether every pixel in a source row is black border/letterbox color.
        /// </summary>
        private bool IsSolidBlackSourceRow(int sourceLeft, int sourceY, int sourceWidth)
        {
            if (sourceY < 0 || sourceY >= _frameBuffer.Height)
            {
                return false;
            }

            int width = Math.Max(0, Math.Min(sourceWidth, _frameBuffer.Width - sourceLeft));
            int row = sourceY * _frameBuffer.Width;
            uint[] pixels = _frameBuffer.Pixels;
            for (int x = 0; x < width; x++)
            {
                if ((pixels[row + sourceLeft + x] & 0x00FFFFFFu) != 0)
                {
                    return false;
                }
            }

            return width > 0;
        }

        /// <summary>
        /// Reads a big-endian word from chip RAM for custom-chip DMA callbacks.
        /// </summary>
        private ushort ReadChipRamWord(uint address)
        {
            uint normalized = address & 0x00FFFFFEu;
            return (ushort)((_bus.ChipRam.Read(normalized) << 8) | _bus.ChipRam.Read(normalized + 1u));
        }

        /// <summary>
        /// Writes a big-endian word to chip RAM for custom-chip DMA callbacks.
        /// </summary>
        private void WriteChipRamWord(uint address, ushort value)
        {
            uint normalized = address & 0x00FFFFFEu;
            _bus.ChipRam.Write(normalized, (byte)(value >> 8));
            _bus.ChipRam.Write(normalized + 1u, (byte)(value & 0xFF));
        }

        /// <summary>
        /// Notifies this component about Copper Register Write Side Effects.
        /// </summary>
        private void NotifyCopperRegisterWriteSideEffects(uint registerOffset, int size)
        {
            ushort value = _agnus.CustomRegisters.ReadWord(registerOffset);
            _agnus.Bitplanes.NotifyCustomRegisterWordWrite(registerOffset, value);
            _agnus.Sprites.NotifyCustomRegisterWordWrite(registerOffset, value, _agnus.Beam.BeamY);
            _paula.NotifyCustomRegisterWordWrite(registerOffset, value);
            if (size >= 2)
            {
                _paula.NotifyCustomRegisterWrite(registerOffset, (byte)(value >> 8));
                _paula.NotifyCustomRegisterWrite(registerOffset + 1u, (byte)(value & 0xFF));
            }
            else
            {
                _paula.NotifyCustomRegisterWrite(registerOffset, (byte)(value & 0xFF));
            }
        }

        /// <summary>
        /// Notifies this component about Bus Write Side Effects.
        /// </summary>
        private void NotifyBusWriteSideEffects(uint address, int size, uint value, AmigaAddressRegion region)
        {
            if (region != AmigaAddressRegion.CustomRegisters)
            {
                return;
            }

            uint registerOffset = address & 0x01FEu;
            ushort wordValue = size >= 2
                ? (ushort)value
                : _agnus.CustomRegisters.ReadWord(registerOffset);
            _agnus.Bitplanes.NotifyCustomRegisterWordWrite(registerOffset, wordValue);
            _agnus.Sprites.NotifyCustomRegisterWordWrite(registerOffset, wordValue, _agnus.Beam.BeamY);
        }

        /// <summary>
        /// Applies Pending Interrupts to the current state.
        /// </summary>
        private void ApplyPendingInterrupts()
        {
            _cpu.SetInterruptLevel(_cpuVisibleInterruptLevel);

            bool ciaAIrq = _ciaA.InterruptPending;
            if (ciaAIrq)
            {
                _agnus.CustomRegisters.RequestInterrupt(CustomInterruptSource.Ports);
            }

            _ciaAIrqLast = ciaAIrq;

            bool ciaBIrq = _ciaB.InterruptPending;
            if (ciaBIrq)
            {
                _agnus.CustomRegisters.RequestInterrupt(CustomInterruptSource.External);
            }

            _ciaBIrqLast = ciaBIrq;

            int level = _agnus.CustomRegisters.ActiveInterruptLevel;
            SampleCpuInterruptPins(level);
        }

        /// <summary>
        /// Samples the current interrupt level and applies it to the CPU IPL pins after a short hardware delay.
        /// </summary>
        private void SampleCpuInterruptPins(int level)
        {
            if (level != _cpuPendingInterruptLevel)
            {
                _cpuPendingInterruptLevel = level;
                _cpuInterruptDelayCycles = 8;
            }

            if (_cpuInterruptDelayCycles > 0)
            {
                _cpuInterruptDelayCycles--;
                return;
            }

            _cpuVisibleInterruptLevel = _cpuPendingInterruptLevel;
        }

        /// <summary>
        /// Advances the CIA TOD From Video state by one emulator tick.
        /// </summary>
        private void TickCiaTodFromVideo()
        {
            BeamTraceEntry beam = _agnus.Beam;
            if (beam.Frame == 0)
            {
                return;
            }

            if (beam.BeamX != 0)
            {
                return;
            }

            _ciaB.TickTodEvent();
            if (beam.BeamY == 0)
            {
                _ciaA.TickTodEvent();
            }
        }

        /// <summary>
        /// Advances the Cias On E Clock state by one emulator tick.
        /// </summary>
        private void TickCiasOnEClock()
        {
            _ciaClockDivider++;
            if (_ciaClockDivider < 5)
            {
                return;
            }

            _ciaClockDivider = 0;
            _ciaA.Tick();
            _ciaB.Tick();
            _input.TickEClock();
        }
    }

    /// <summary>
    /// Provides Amiga System Component Profile functionality.
    /// </summary>
    public sealed class AmigaSystemComponentProfile
    {
        /// <summary>
        /// Gets or sets Cycles.
        /// </summary>
        public int Cycles { get; set; }

        /// <summary>
        /// Gets or sets Stopwatch Frequency.
        /// </summary>
        public long StopwatchFrequency { get; set; }

        /// <summary>
        /// Gets or sets Total Ticks.
        /// </summary>
        public long TotalTicks { get; set; }

        /// <summary>
        /// Gets or sets Agnus Ticks.
        /// </summary>
        public long AgnusTicks { get; set; }

        /// <summary>
        /// Gets or sets Paula Ticks.
        /// </summary>
        public long PaulaTicks { get; set; }

        /// <summary>
        /// Gets or sets CIA Ticks.
        /// </summary>
        public long CiaTicks { get; set; }

        /// <summary>
        /// Gets or sets CPU Ticks.
        /// </summary>
        public long CpuTicks { get; set; }

        /// <summary>
        /// Gets or sets Denise Ticks.
        /// </summary>
        public long DeniseTicks { get; set; }

        /// <summary>
        /// Gets or sets Trace Ticks.
        /// </summary>
        public long TraceTicks { get; set; }
    }
}
