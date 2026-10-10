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
        /// Represents CPU.
        /// </summary>
        Cpu,
        /// <summary>
        /// Represents Refresh.
        /// </summary>
        Refresh,
        /// <summary>
        /// Represents Audio.
        /// </summary>
        Audio,
        /// <summary>
        /// Represents Disk.
        /// </summary>
        Disk,
        /// <summary>
        /// Represents Sprite.
        /// </summary>
        Sprite,
        /// <summary>
        /// Represents Bitplane.
        /// </summary>
        Bitplane,
        /// <summary>
        /// Represents Copper.
        /// </summary>
        Copper,
        /// <summary>
        /// Represents Blitter.
        /// </summary>
        Blitter
    }

    /// <summary>
    /// Gets one fixed OCS DMA slot before register enables and display-window overrides are applied.
    /// </summary>
    public struct OcsDmaSlot
    {
        /// <summary>
        /// Stores default owner assigned by the PAL OCS DMA slot table.
        /// </summary>
        public DmaOwner Owner;
        /// <summary>
        /// Stores sprite index for sprite slots.
        /// </summary>
        public int SpriteIndex;
        /// <summary>
        /// Stores first or second word phase inside a sprite slot pair.
        /// </summary>
        public int SpriteSlotPhase;
        /// <summary>
        /// Stores audio channel assigned to an audio DMA slot.
        /// </summary>
        public int AudioChannelIndex;
        /// <summary>
        /// Stores first or second audio slot phase for one channel.
        /// </summary>
        public int AudioSlotPhase;
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
        private const int AudioSlotStart = 17;
        /// <summary>
        /// Defines the first fixed disk DMA slot.
        /// </summary>
        private const int DiskSlotStart = 11;
        /// <summary>
        /// Defines the first fixed sprite DMA slot.
        /// </summary>
        private const int SpriteSlotStart = 25;
        /// <summary>
        /// Defines the last fixed sprite DMA slot plus one.
        /// </summary>
        private const int SpriteSlotEndExclusive = 57;
        /// <summary>
        /// Stores base PAL OCS DMA slot table.
        /// </summary>
        private static readonly OcsDmaSlot[] s_ocsPalSlotTable = BuildOcsPalSlotTable();

        /// <summary>
        /// Stores registers.
        /// </summary>
        private readonly CustomRegisterMap _registers;
        /// <summary>
        /// Stores bitplanes.
        /// </summary>
        private readonly BitplaneUnit _bitplanes;
        /// <summary>
        /// Stores is Blitter Busy.
        /// </summary>
        private System.Func<bool> _isBlitterBusy;
        /// <summary>
        /// Stores whether the Copper needs a DMA slot for its next instruction fetch.
        /// </summary>
        private System.Func<bool> _doesCopperNeedBus;

        private System.Func<int, int, int, bool> _doesSpriteNeedBus;

        /// <summary>Releases idle sprite slots to lower-priority DMA clients.</summary>
        public void AttachSpriteBusDemand(System.Func<int, int, int, bool> doesSpriteNeedBus)
        {
            _doesSpriteNeedBus = doesSpriteNeedBus;
        }
        /// <summary>
        /// Stores is Disk DMA Active.
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
        /// Attaches Copper bus-demand status to this component.
        /// </summary>
        public void AttachCopperBusDemand(System.Func<bool> doesCopperNeedBus)
        {
            _doesCopperNeedBus = doesCopperNeedBus;
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
                AudioChannelIndex = baseSlot.AudioChannelIndex,
                AudioSlotPhase = baseSlot.AudioSlotPhase,
                CpuCanUseChipBus = owner == DmaOwner.Cpu
            };
        }

        /// <summary>
        /// Runs Select Owner.
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
            bool copperNeedsBus = _doesCopperNeedBus == null || _doesCopperNeedBus();

            if (baseSlot.Owner == DmaOwner.Refresh)
            {
                return DmaOwner.Refresh;
            }

            if (baseSlot.Owner == DmaOwner.Audio && audioDma && (dmacon & (1 << baseSlot.AudioChannelIndex)) != 0)
            {
                return DmaOwner.Audio;
            }

            if (baseSlot.Owner == DmaOwner.Disk && diskDma)
            {
                return DmaOwner.Disk;
            }

            if (baseSlot.Owner == DmaOwner.Sprite && spriteDma &&
                (_doesSpriteNeedBus == null || _doesSpriteNeedBus(baseSlot.SpriteIndex, baseSlot.SpriteSlotPhase, beamY)))
            {
                return DmaOwner.Sprite;
            }

            if (bitplaneDma && _bitplanes.IsPlaneFetchSlot(beamX, beamY))
            {
                return DmaOwner.Bitplane;
            }

            if (copperDma && copperNeedsBus && IsCopperSlot(beamX))
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
            // A Copper word takes one of every two memory cycles. Two
            // words therefore take four cycles, not eight.
            return beamX >= 4 && beamX < 224 && (beamX & 1) == 0;
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

        private static OcsDmaSlot[] BuildOcsPalSlotTable()
        {
            var table = new OcsDmaSlot[PalSlotsPerLine];
            for (int index = 0; index < table.Length; index++)
            {
                table[index] = new OcsDmaSlot
                {
                    Owner = DmaOwner.Cpu,
                    SpriteIndex = -1,
                    SpriteSlotPhase = -1,
                    AudioChannelIndex = -1,
                    AudioSlotPhase = -1
                };
            }

            // Fixed OCS DMA words are interleaved with free bus cycles.
            // Keep Copper cycles available between them, including in hblank.
            // RGA slot positions: WinUAE 4.9.1 custom.cpp, REFRESH_FIRST_HPOS,
            // DMAL_FIRST_HPOS and SPR_FIRST_HPOS (tag 4910).
            for (int slot = 3; slot <= 9; slot += 2)
            {
                table[slot].Owner = DmaOwner.Refresh;
            }

            for (int channel = 0; channel < 4; channel++)
            {
                int slot = AudioSlotStart + channel * 2;
                table[slot].Owner = DmaOwner.Audio;
                table[slot].AudioChannelIndex = channel;
                table[slot].AudioSlotPhase = 0;
            }

            for (int slot = DiskSlotStart; slot < DiskSlotStart + 6; slot += 2)
            {
                table[slot].Owner = DmaOwner.Disk;
            }

            for (int slot = SpriteSlotStart; slot < SpriteSlotEndExclusive; slot += 2)
            {
                int word = (slot - SpriteSlotStart) / 2;
                table[slot].Owner = DmaOwner.Sprite;
                table[slot].SpriteIndex = word >> 1;
                table[slot].SpriteSlotPhase = word & 1;
            }

            return table;
        }
    }
}
