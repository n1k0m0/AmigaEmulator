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
using AmigaEmulator.Core.DevTools;
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>
    /// Lists the supported DMA Owner values.
    /// </summary>
    public enum DmaOwner
    {
        /// <summary>
        /// Represents the C pu option.
        /// </summary>
        Cpu,
        /// <summary>
        /// Represents the R ef re sh option.
        /// </summary>
        Refresh,
        /// <summary>
        /// Represents the A ud io option.
        /// </summary>
        Audio,
        /// <summary>
        /// Represents the D is k option.
        /// </summary>
        Disk,
        /// <summary>
        /// Represents the S pr it e option.
        /// </summary>
        Sprite,
        /// <summary>
        /// Represents the B it pl an e option.
        /// </summary>
        Bitplane,
        /// <summary>
        /// Represents the C op pe r option.
        /// </summary>
        Copper,
        /// <summary>
        /// Represents the B li tt er option.
        /// </summary>
        Blitter
    }

    /// <summary>
    /// Describes one fixed OCS DMA slot before register enables and display-window overrides are applied.
    /// </summary>
    public struct OcsDmaSlot
    {
        /// <summary>
        /// Stores the default owner assigned by the PAL OCS DMA slot table.
        /// </summary>
        public DmaOwner Owner;
        /// <summary>
        /// Stores the sprite index for sprite slots.
        /// </summary>
        public int SpriteIndex;
        /// <summary>
        /// Stores the first or second word phase inside a sprite slot pair.
        /// </summary>
        public int SpriteSlotPhase;
    }

    /// <summary>
    /// Selects the DMA owner for each PAL chip cycle.
    /// </summary>
    public sealed class DmaScheduler
    {
        /// <summary>
        /// Defines the number of low-resolution PAL chip slots in one scanline.
        /// </summary>
        public const int PalSlotsPerLine = 227;
        /// <summary>
        /// Defines the first fixed audio DMA slot.
        /// </summary>
        private const int AudioSlotStart = 8;
        /// <summary>
        /// Defines the first fixed disk DMA slot.
        /// </summary>
        private const int DiskSlotStart = 16;
        /// <summary>
        /// Defines the first fixed sprite DMA slot.
        /// </summary>
        private const int SpriteSlotStart = 20;
        /// <summary>
        /// Defines the last fixed sprite DMA slot plus one.
        /// </summary>
        private const int SpriteSlotEndExclusive = 36;
        /// <summary>
        /// Stores the base PAL OCS DMA slot table.
        /// </summary>
        private static readonly OcsDmaSlot[] s_ocsPalSlotTable = BuildOcsPalSlotTable();

        /// <summary>
        /// Tracks the registers.
        /// </summary>
        private readonly CustomRegisterMap _registers;
        /// <summary>
        /// Tracks the bitplanes.
        /// </summary>
        private readonly BitplaneUnit _bitplanes;
        /// <summary>
        /// Tracks the is Blitter Busy.
        /// </summary>
        private System.Func<bool> _isBlitterBusy;
        /// <summary>
        /// Tracks the is Disk DMA Active.
        /// </summary>
        private System.Func<bool> _isDiskDmaActive;

        /// <summary>
        /// Initializes a new instance of the DmaScheduler class.
        /// </summary>
        public DmaScheduler()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the DmaScheduler class.
        /// </summary>
        public DmaScheduler(CustomRegisterMap registers)
            : this(registers, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the DmaScheduler class.
        /// </summary>
        public DmaScheduler(CustomRegisterMap registers, BitplaneUnit bitplanes)
        {
            _registers = registers;
            _bitplanes = bitplanes ?? new BitplaneUnit(registers);
        }

        /// <summary>
        /// Attaches Blitter Status to this component.
        /// </summary>
        public void AttachBlitterStatus(System.Func<bool> isBlitterBusy)
        {
            _isBlitterBusy = isBlitterBusy;
        }

        /// <summary>
        /// Attaches Disk DMA Status to this component.
        /// </summary>
        public void AttachDiskDmaStatus(System.Func<bool> isDiskDmaActive)
        {
            _isDiskDmaActive = isDiskDmaActive;
        }

        /// <summary>
        /// Returns the Slot.
        /// </summary>
        public DmaSlotTraceEntry GetSlot(long globalChipCycle, int beamX, int beamY)
        {
            OcsDmaSlot baseSlot = GetBaseSlot(beamX);
            DmaOwner owner = SelectOwner(beamX, beamY, baseSlot);
            return new DmaSlotTraceEntry
            {
                GlobalChipCycle = globalChipCycle,
                SlotIndex = beamX,
                BeamY = beamY,
                Owner = owner,
                BaseOwner = baseSlot.Owner,
                SpriteIndex = baseSlot.SpriteIndex,
                SpriteSlotPhase = baseSlot.SpriteSlotPhase,
                CpuCanUseChipBus = owner == DmaOwner.Cpu
            };
        }

        /// <summary>
        /// Executes the Select Owner logic.
        /// </summary>
        private DmaOwner SelectOwner(int beamX, int beamY, OcsDmaSlot baseSlot)
        {
            ushort dmacon = _bitplanes != null ? GetDmaControl() : (ushort)0;
            bool dmaMaster = (dmacon & 0x0200) != 0;
            bool audioDma = dmaMaster && (dmacon & 0x000F) != 0;
            bool diskDma = dmaMaster && (dmacon & 0x0010) != 0 && _isDiskDmaActive != null && _isDiskDmaActive();
            bool spriteDma = dmaMaster && (dmacon & 0x0020) != 0;
            bool blitterDma = dmaMaster && (dmacon & 0x0040) != 0;
            bool copperDma = dmaMaster && (dmacon & 0x0080) != 0;
            bool bitplaneDma = dmaMaster && (dmacon & 0x0100) != 0;
            bool blitterPriority = blitterDma && (dmacon & 0x0400) != 0;
            bool blitterBusy = blitterDma && _isBlitterBusy != null && _isBlitterBusy();

            if (baseSlot.Owner == DmaOwner.Refresh)
            {
                return DmaOwner.Refresh;
            }

            if (bitplaneDma && _bitplanes.IsPlaneFetchSlot(beamX, beamY))
            {
                return DmaOwner.Bitplane;
            }

            if (baseSlot.Owner == DmaOwner.Audio)
            {
                return audioDma ? DmaOwner.Audio : DmaOwner.Cpu;
            }

            if (baseSlot.Owner == DmaOwner.Disk)
            {
                return diskDma ? DmaOwner.Disk : DmaOwner.Cpu;
            }

            if (baseSlot.Owner == DmaOwner.Sprite)
            {
                return spriteDma ? DmaOwner.Sprite : DmaOwner.Cpu;
            }

            if (copperDma && IsCopperSlot(beamX))
            {
                return DmaOwner.Copper;
            }

            if (blitterBusy && (blitterPriority || IsBlitterSlot(beamX)))
            {
                return DmaOwner.Blitter;
            }

            return DmaOwner.Cpu;
        }

        /// <summary>
        /// Returns whether Copper Slot is true.
        /// </summary>
        private static bool IsCopperSlot(int beamX)
        {
            return beamX >= 20 && beamX < 224 && (beamX & 3) == 0;
        }

        /// <summary>
        /// Returns whether Blitter Slot is true.
        /// </summary>
        private static bool IsBlitterSlot(int beamX)
        {
            return beamX >= 20 && beamX < 224;
        }

        /// <summary>
        /// Returns the DMA Control.
        /// </summary>
        private ushort GetDmaControl()
        {
            return _registers != null ? _registers.DmaControl : (ushort)0;
        }

        /// <summary>
        /// Returns the fixed OCS slot-table entry for one PAL chip slot.
        /// </summary>
        private static OcsDmaSlot GetBaseSlot(int beamX)
        {
            int slot = beamX % PalSlotsPerLine;
            if (slot < 0)
            {
                slot += PalSlotsPerLine;
            }

            return s_ocsPalSlotTable[slot];
        }

        /// <summary>
        /// Builds the PAL OCS fixed DMA table for refresh, audio, disk, and sprite slots.
        /// </summary>
        private static OcsDmaSlot[] BuildOcsPalSlotTable()
        {
            var table = new OcsDmaSlot[PalSlotsPerLine];
            for (int index = 0; index < table.Length; index++)
            {
                table[index] = new OcsDmaSlot
                {
                    Owner = DmaOwner.Cpu,
                    SpriteIndex = -1,
                    SpriteSlotPhase = -1
                };
            }

            for (int slot = 0; slot < 4; slot++)
            {
                table[slot].Owner = DmaOwner.Refresh;
            }

            for (int slot = AudioSlotStart; slot < AudioSlotStart + 8; slot++)
            {
                table[slot].Owner = DmaOwner.Audio;
            }

            for (int slot = DiskSlotStart; slot < DiskSlotStart + 4; slot++)
            {
                table[slot].Owner = DmaOwner.Disk;
            }

            for (int slot = SpriteSlotStart; slot < SpriteSlotEndExclusive; slot++)
            {
                int offset = slot - SpriteSlotStart;
                table[slot].Owner = DmaOwner.Sprite;
                table[slot].SpriteIndex = offset >> 1;
                table[slot].SpriteSlotPhase = offset & 1;
            }

            return table;
        }
    }
}
