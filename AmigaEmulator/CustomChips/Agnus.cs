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
        /// Constant for Vposr.
        /// </summary>
        private const uint Vposr = 0x004;
        /// <summary>
        /// Constant for Vhposr.
        /// </summary>
        private const uint Vhposr = 0x006;
        /// <summary>
        /// Constant for Pal V Blank End.
        /// </summary>
        private const int PalVBlankEnd = 25;

        /// <summary>
        /// Stores model.
        /// </summary>
        private readonly AmigaModel _model;
        [System.NonSerialized] private readonly ushort _agnusId;
        /// <summary>
        /// Stores DMA Scheduler.
        /// </summary>
        private readonly DmaScheduler _dmaScheduler;
        /// <summary>
        /// Stores global Cycle.
        /// </summary>
        private long _globalCycle;
        /// <summary>
        /// Stores frame.
        /// </summary>
        private long _frame;
        /// <summary>
        /// Stores beam X.
        /// </summary>
        private int _beamX;
        /// <summary>
        /// Stores beam Y.
        /// </summary>
        private int _beamY;
        /// <summary>
        /// Stores last Is V Blank.
        /// </summary>
        private bool _lastIsVBlank;

        /// <summary>
        /// Initializes a new instance of the Agnus class.
        /// </summary>
        public Agnus(AmigaModel model)
        {
            _model = model;
            _agnusId = model.AgnusId;
            CustomRegisters = new CustomRegisterMap(_model.Chipset);
            Collisions = new CollisionUnit();
            Bitplanes = new BitplaneUnit(CustomRegisters, null, _model.ChipRamBytes);
            Sprites = new SpriteUnit(CustomRegisters, null, _model.ChipRamBytes);
            _dmaScheduler = new DmaScheduler(CustomRegisters, Bitplanes);
            Copper = new Copper(CustomRegisters, _model.ChipRamBytes);
            Blitter = new Blitter(CustomRegisters, _model.ChipRamBytes);
            _dmaScheduler.AttachBlitterStatus(() => Blitter.DmaPending);
            _dmaScheduler.AttachCopperBusDemand(() => Copper.NeedsDmaBus);
            _dmaScheduler.AttachSpriteBusDemand(Sprites.NeedsDmaBus);
            Copper.AttachBlitterStatus(() => Blitter.DmaPending);
            Copper.CustomRegisterWritten += Blitter.NotifyCustomRegisterWrite;
            CustomRegisters.CopperJumpRequested += Copper.StrobeJump;
            CustomRegisters.AttachBlitterStatus(() => Blitter.Busy, () => Blitter.ZeroFlag);
            CustomRegisters.AttachCollisionUnit(Collisions);
            Denise.AttachCustomRegisters(CustomRegisters);
            Reset();
        }

        public CustomRegisterMap CustomRegisters { get; private set; }

        public BitplaneUnit Bitplanes { get; private set; }

        /// <summary>
        /// Gets the sprite DMA and rendering state shared with Denise.
        /// </summary>
        public SpriteUnit Sprites { get; private set; }

        /// <summary>
        /// Gets the Denise collision register state.
        /// </summary>
        public CollisionUnit Collisions { get; private set; }

        public Copper Copper { get; private set; }

        public Blitter Blitter { get; private set; }

        public BeamTraceEntry Beam { get; private set; }

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
            Collisions.Reset();
            Bitplanes.Reset();
            Sprites.Reset();
            Copper.Reset();
            Blitter.Reset();
            Beam = new BeamTraceEntry();
            LastDmaSlot = new DmaSlotTraceEntry { Owner = DmaOwner.Cpu, BaseOwner = DmaOwner.Cpu, SpriteIndex = -1, SpriteSlotPhase = -1, AudioChannelIndex = -1, AudioSlotPhase = -1, CpuCanUseChipBus = true };
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
            StateSerializer.WriteObjectFields(writer, CustomRegisters, "_collisionUnit");
            StateSerializer.WriteObjectFields(writer, Collisions);
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
            StateSerializer.ReadObjectFields(reader, CustomRegisters, "_collisionUnit");
            ReadOptionalCollisionState(reader);
            CustomRegisters.AttachCollisionUnit(Collisions);
            Bitplanes.LoadState(reader);
            Sprites.LoadState(reader);
            Copper.LoadState(reader);
            Blitter.LoadState(reader);
            Beam = ReadBeamTrace(reader);
            LastDmaSlot = ReadDmaSlotTraceCompatible(reader);
        }

        /// <summary>
        /// Reads collision state when present while accepting savestates written before CLXDAT support.
        /// </summary>
        private void ReadOptionalCollisionState(BinaryReader reader)
        {
            long position = reader.BaseStream.CanSeek ? reader.BaseStream.Position : -1;
            try
            {
                StateSerializer.ReadObjectFields(reader, Collisions);
            }
            catch (InvalidDataException)
            {
                if (position < 0)
                {
                    throw;
                }

                reader.BaseStream.Position = position;
                Collisions.Reset();
            }
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
            writer.Write(value.AudioChannelIndex);
            writer.Write(value.AudioSlotPhase);
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
                AudioChannelIndex = reader.ReadInt32(),
                AudioSlotPhase = reader.ReadInt32(),
                CpuCanUseChipBus = reader.ReadBoolean()
            };
        }

        /// <summary>
        /// Reads DMA slot traces written before or after audio channel trace fields were added.
        /// </summary>
        private static DmaSlotTraceEntry ReadDmaSlotTraceCompatible(BinaryReader reader)
        {
            long position = reader.BaseStream.CanSeek ? reader.BaseStream.Position : -1;
            long globalChipCycle = reader.ReadInt64();
            int slotIndex = reader.ReadInt32();
            int beamY = reader.ReadInt32();
            var owner = (DmaOwner)reader.ReadInt32();
            var baseOwner = (DmaOwner)reader.ReadInt32();
            int spriteIndex = reader.ReadInt32();
            int spriteSlotPhase = reader.ReadInt32();
            int audioChannelIndex = reader.ReadInt32();
            int audioSlotPhase = reader.ReadInt32();
            if (IsPlausibleAudioTrace(audioChannelIndex, audioSlotPhase))
            {
                return new DmaSlotTraceEntry
                {
                    GlobalChipCycle = globalChipCycle,
                    SlotIndex = slotIndex,
                    BeamY = beamY,
                    Owner = owner,
                    BaseOwner = baseOwner,
                    SpriteIndex = spriteIndex,
                    SpriteSlotPhase = spriteSlotPhase,
                    AudioChannelIndex = audioChannelIndex,
                    AudioSlotPhase = audioSlotPhase,
                    CpuCanUseChipBus = reader.ReadBoolean()
                };
            }

            if (position < 0)
            {
                throw new InvalidDataException("Invalid DMA slot trace in savestate.");
            }

            reader.BaseStream.Position = position;
            return ReadLegacyDmaSlotTrace(reader);
        }

        /// <summary>
        /// Reads DMA slot traces written before audio channel trace fields existed.
        /// </summary>
        private static DmaSlotTraceEntry ReadLegacyDmaSlotTrace(BinaryReader reader)
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
                AudioChannelIndex = -1,
                AudioSlotPhase = -1,
                CpuCanUseChipBus = reader.ReadBoolean()
            };
        }

        /// <summary>
        /// Returns whether decoded audio trace fields are in the valid OCS range.
        /// </summary>
        private static bool IsPlausibleAudioTrace(int audioChannelIndex, int audioSlotPhase)
        {
            return audioChannelIndex >= -1 && audioChannelIndex <= 3 && audioSlotPhase >= -1 && audioSlotPhase <= 1;
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

            if (beamX == 0) Bitplanes.SampleVerticalDisplayWindow(beamY);
            bool dmaMasterEnabled = (CustomRegisters.DmaControl & 0x0200) != 0;
            DmaSlotTraceEntry slot = _dmaScheduler.GetSlot(_globalCycle, beamX, beamY);
            slot.CpuCanUseChipBus = !dmaMasterEnabled || slot.Owner == DmaOwner.Cpu;
            LastDmaSlot = slot;
            UpdateBeamRegisters(beamX, beamY);
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
        /// Runs Advance Beam.
        /// </summary>
        private void AdvanceBeam()
        {
            _beamX++;
            if (_beamX < _model.ChipCyclesPerLine)
            {
                return;
            }

            _beamX = 0;
            CustomRegisters.TickPotCounters();
            _beamY++;
            if (_beamY < _model.LinesPerFrame)
            {
                return;
            }

            _beamY = 0;
            _frame++;
            // LOF is a hardware flop, not the host frame counter's parity.
            // BPLCON0 LACE enables automatic toggling; VPOSW can set it even
            // in a non-interlaced display. The register array saves the flop.
            if ((CustomRegisters.ReadStoredWord(0x100) & 4) != 0)
                CustomRegisters.WriteRawWord(Vposr, (ushort)(CustomRegisters.ReadWord(Vposr) ^ 0x8000));
        }

        /// <summary>
        /// Updates Beam Registers from current emulator state.
        /// </summary>
        private void UpdateBeamRegisters(int beamX, int beamY)
        {
            ushort vpos = (ushort)(_agnusId | ((beamY >> 8) & 0x0007));
            vpos |= (ushort)(CustomRegisters.ReadWord(Vposr) & 0x8000);

            ushort vhpos = (ushort)(((beamY & 0xFF) << 8) | (beamX & 0xFF));
            CustomRegisters.WriteRawWord(Vposr, vpos);
            CustomRegisters.WriteRawWord(Vhposr, vhpos);
        }
    }
}
