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
using AmigaEmulator.Core.CustomChips;

namespace AmigaEmulator.Core.DevTools
{
    /// <summary>
    /// Stores DMA Slot Trace Entry data used by the emulator.
    /// </summary>
    public struct DmaSlotTraceEntry
    {
        /// <summary>
        /// Stores the Global Chip Cycle.
        /// </summary>
        public long GlobalChipCycle;
        /// <summary>
        /// Stores the Slot Index.
        /// </summary>
        public int SlotIndex;
        /// <summary>
        /// Stores the Beam Y.
        /// </summary>
        public int BeamY;
        /// <summary>
        /// Stores the Owner.
        /// </summary>
        public DmaOwner Owner;
        /// <summary>
        /// Stores the owner assigned by the fixed OCS slot table before dynamic display and Blitter arbitration.
        /// </summary>
        public DmaOwner BaseOwner;
        /// <summary>
        /// Stores the sprite number for sprite DMA slots, or -1 when the slot is not a sprite slot.
        /// </summary>
        public int SpriteIndex;
        /// <summary>
        /// Stores the sprite DMA sub-slot inside a sprite pair.
        /// </summary>
        public int SpriteSlotPhase;
        /// <summary>
        /// Stores the audio channel assigned to this DMA slot, or -1 when it is not an audio slot.
        /// </summary>
        public int AudioChannelIndex;
        /// <summary>
        /// Stores the sub-slot phase inside an audio channel slot pair.
        /// </summary>
        public int AudioSlotPhase;
        /// <summary>
        /// Stores the CPU Can Use Chip Bus.
        /// </summary>
        public bool CpuCanUseChipBus;
    }
}
