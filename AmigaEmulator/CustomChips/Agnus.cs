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
using System.IO;
using AmigaEmulator.Core.DevTools;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>
    /// Emulates the Agnus timing, DMA arbitration, Copper, Blitter, and bitplane coordination logic.
    /// </summary>
    public sealed class Agnus
    {
        /// <summary>
        /// Defines the Vposr constant.
        /// </summary>
        private const uint Vposr = 0x004;
        /// <summary>
        /// Defines the Vhposr constant.
        /// </summary>
        private const uint Vhposr = 0x006;
        /// <summary>
        /// Defines the Pal V Blank End constant.
        /// </summary>
        private const int PalVBlankEnd = 25;

        /// <summary>
        /// Tracks the model.
        /// </summary>
        private readonly AmigaModel _model;
        /// <summary>
        /// Tracks the DMA Scheduler.
        /// </summary>
        private readonly DmaScheduler _dmaScheduler;
        /// <summary>
        /// Tracks the global Cycle.
        /// </summary>
        private long _globalCycle;
        /// <summary>
        /// Tracks the frame.
        /// </summary>
        private long _frame;
        /// <summary>
        /// Tracks the beam X.
        /// </summary>
        private int _beamX;
        /// <summary>
        /// Tracks the beam Y.
        /// </summary>
        private int _beamY;
        /// <summary>
        /// Tracks the last Is V Blank.
        /// </summary>
        private bool _lastIsVBlank;

        /// <summary>
        /// Initializes a new instance of the Agnus class.
        /// </summary>
        public Agnus(AmigaModel model)
        {
            _model = model;
            CustomRegisters = new CustomRegisterMap();
            Bitplanes = new BitplaneUnit(CustomRegisters, null, _model.ChipRamBytes);
            Sprites = new SpriteUnit(CustomRegisters, null, _model.ChipRamBytes);
            _dmaScheduler = new DmaScheduler(CustomRegisters, Bitplanes);
            Copper = new Copper(CustomRegisters, _model.ChipRamBytes);
            Blitter = new Blitter(CustomRegisters, _model.ChipRamBytes);
            _dmaScheduler.AttachBlitterStatus(() => Blitter.DmaPending);
            Copper.AttachBlitterStatus(() => Blitter.DmaPending);
            Copper.CustomRegisterWritten += Blitter.NotifyCustomRegisterWrite;
            CustomRegisters.CopperJumpRequested += Copper.StrobeJump;
            CustomRegisters.AttachBlitterStatus(() => Blitter.Busy, () => Blitter.ZeroFlag);
            Denise.AttachCustomRegisters(CustomRegisters);
            Reset();
        }

        /// <summary>
        /// Gets or updates the Custom Registers.
        /// </summary>
        public CustomRegisterMap CustomRegisters { get; private set; }

        /// <summary>
        /// Gets or updates the Bitplanes.
        /// </summary>
        public BitplaneUnit Bitplanes { get; private set; }

        /// <summary>
        /// Gets the sprite DMA and rendering state shared with Denise.
        /// </summary>
        public SpriteUnit Sprites { get; private set; }

        /// <summary>
        /// Gets or updates the Copper.
        /// </summary>
        public Copper Copper { get; private set; }

        /// <summary>
        /// Gets or updates the Blitter.
        /// </summary>
        public Blitter Blitter { get; private set; }

        /// <summary>
        /// Gets or updates the Beam.
        /// </summary>
        public BeamTraceEntry Beam { get; private set; }

        /// <summary>
        /// Gets or updates the Last DMA Slot.
        /// </summary>
        public DmaSlotTraceEntry LastDmaSlot { get; private set; }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            _globalCycle = 0;
            _frame = 0;
            _beamX = 0;
            _beamY = 0;
            _lastIsVBlank = true;
            CustomRegisters.Reset();
            Bitplanes.Reset();
            Sprites.Reset();
            Copper.Reset();
            Blitter.Reset();
            Beam = new BeamTraceEntry();
            LastDmaSlot = new DmaSlotTraceEntry { Owner = DmaOwner.Cpu, BaseOwner = DmaOwner.Cpu, SpriteIndex = -1, SpriteSlotPhase = -1, CpuCanUseChipBus = true };
        }

        /// <summary>
        /// Attaches Chip Word Reader to this component.
        /// </summary>
        public void AttachChipWordReader(System.Func<uint, ushort> readChipWord)
        {
            Bitplanes.AttachChipWordReader(readChipWord);
            Sprites.AttachChipWordReader(readChipWord);
            Copper.AttachChipWordReader(readChipWord);
        }

        /// <summary>
        /// Attaches Disk DMA Status to this component.
        /// </summary>
        public void AttachDiskDmaStatus(System.Func<bool> isDiskDmaActive)
        {
            _dmaScheduler.AttachDiskDmaStatus(isDiskDmaActive);
        }

        /// <summary>
        /// Writes Agnus timing, DMA, Copper, Blitter, bitplane, sprite, and custom-register state.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_globalCycle);
            writer.Write(_frame);
            writer.Write(_beamX);
            writer.Write(_beamY);
            writer.Write(_lastIsVBlank);
            StateSerializer.WriteObjectFields(writer, CustomRegisters);
            Bitplanes.SaveState(writer);
            Sprites.SaveState(writer);
            Copper.SaveState(writer);
            Blitter.SaveState(writer);
            WriteBeamTrace(writer, Beam);
            WriteDmaSlotTrace(writer, LastDmaSlot);
        }

        /// <summary>
        /// Restores Agnus timing, DMA, Copper, Blitter, bitplane, sprite, and custom-register state.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            _globalCycle = reader.ReadInt64();
            _frame = reader.ReadInt64();
            _beamX = reader.ReadInt32();
            _beamY = reader.ReadInt32();
            _lastIsVBlank = reader.ReadBoolean();
            StateSerializer.ReadObjectFields(reader, CustomRegisters);
            Bitplanes.LoadState(reader);
            Sprites.LoadState(reader);
            Copper.LoadState(reader);
            Blitter.LoadState(reader);
            Beam = ReadBeamTrace(reader);
            LastDmaSlot = ReadDmaSlotTrace(reader);
        }

        /// <summary>
        /// Writes a beam trace value without boxing the struct.
        /// </summary>
        private static void WriteBeamTrace(BinaryWriter writer, BeamTraceEntry value)
        {
            writer.Write(value.GlobalChipCycle);
            writer.Write(value.Frame);
            writer.Write(value.BeamX);
            writer.Write(value.BeamY);
            writer.Write(value.IsHBlank);
            writer.Write(value.IsVBlank);
        }

        /// <summary>
        /// Reads a beam trace value.
        /// </summary>
        private static BeamTraceEntry ReadBeamTrace(BinaryReader reader)
        {
            return new BeamTraceEntry
            {
                GlobalChipCycle = reader.ReadInt64(),
                Frame = reader.ReadInt64(),
                BeamX = reader.ReadInt32(),
                BeamY = reader.ReadInt32(),
                IsHBlank = reader.ReadBoolean(),
                IsVBlank = reader.ReadBoolean()
            };
        }

        /// <summary>
        /// Writes a DMA slot trace value without boxing the struct.
        /// </summary>
        private static void WriteDmaSlotTrace(BinaryWriter writer, DmaSlotTraceEntry value)
        {
            writer.Write(value.GlobalChipCycle);
            writer.Write(value.SlotIndex);
            writer.Write(value.BeamY);
            writer.Write((int)value.Owner);
            writer.Write((int)value.BaseOwner);
            writer.Write(value.SpriteIndex);
            writer.Write(value.SpriteSlotPhase);
            writer.Write(value.CpuCanUseChipBus);
        }

        /// <summary>
        /// Reads a DMA slot trace value.
        /// </summary>
        private static DmaSlotTraceEntry ReadDmaSlotTrace(BinaryReader reader)
        {
            return new DmaSlotTraceEntry
            {
                GlobalChipCycle = reader.ReadInt64(),
                SlotIndex = reader.ReadInt32(),
                BeamY = reader.ReadInt32(),
                Owner = (DmaOwner)reader.ReadInt32(),
                BaseOwner = (DmaOwner)reader.ReadInt32(),
                SpriteIndex = reader.ReadInt32(),
                SpriteSlotPhase = reader.ReadInt32(),
                CpuCanUseChipBus = reader.ReadBoolean()
            };
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public DmaSlotTraceEntry Tick()
        {
            int beamX = _beamX;
            int beamY = _beamY;
            long frame = _frame;
            if (beamX == 0 && beamY == 0)
            {
                Bitplanes.BeginFrame();
                Copper.RestartAtCopperList1();
            }

            bool dmaMasterEnabled = (CustomRegisters.DmaControl & 0x0200) != 0;
            DmaSlotTraceEntry slot = _dmaScheduler.GetSlot(_globalCycle, beamX, beamY);
            slot.CpuCanUseChipBus = !dmaMasterEnabled || slot.Owner == DmaOwner.Cpu;
            LastDmaSlot = slot;
            UpdateBeamRegisters(beamX, beamY, frame);
            if (LastDmaSlot.Owner == DmaOwner.Bitplane)
            {
                Bitplanes.FetchDmaPlaneWord(beamX, beamY);
            }
            else if (LastDmaSlot.Owner == DmaOwner.Sprite)
            {
                Sprites.TickDmaSlot(LastDmaSlot.SpriteIndex, LastDmaSlot.SpriteSlotPhase, beamY);
            }

            Copper.Tick(LastDmaSlot.Owner == DmaOwner.Copper, beamX, beamY, _globalCycle);
            Blitter.Tick(LastDmaSlot.Owner == DmaOwner.Blitter);

            Beam = new BeamTraceEntry
            {
                GlobalChipCycle = _globalCycle,
                Frame = frame,
                BeamX = beamX,
                BeamY = beamY,
                IsHBlank = beamX >= 180,
                IsVBlank = beamY < PalVBlankEnd
            };

            if (!_lastIsVBlank && Beam.IsVBlank)
            {
                CustomRegisters.RequestInterrupt(CustomInterruptSource.VerticalBlank);
            }

            _lastIsVBlank = Beam.IsVBlank;
            _globalCycle++;
            AdvanceBeam();
            return LastDmaSlot;
        }

        /// <summary>
        /// Executes the Advance Beam logic.
        /// </summary>
        private void AdvanceBeam()
        {
            _beamX++;
            if (_beamX < _model.ChipCyclesPerLine)
            {
                return;
            }

            _beamX = 0;
            _beamY++;
            if (_beamY < _model.LinesPerFrame)
            {
                return;
            }

            _beamY = 0;
            _frame++;
        }

        /// <summary>
        /// Updates Beam Registers from current emulator state.
        /// </summary>
        private void UpdateBeamRegisters(int beamX, int beamY, long frame)
        {
            ushort vpos = (ushort)((beamY >> 8) & 0x0001);
            if ((frame & 1) != 0)
            {
                vpos |= 0x8000;
            }

            ushort vhpos = (ushort)(((beamY & 0xFF) << 8) | (beamX & 0xFF));
            CustomRegisters.WriteRawWord(Vposr, vpos);
            CustomRegisters.WriteRawWord(Vhposr, vhpos);
        }
    }
}
