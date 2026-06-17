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
namespace AmigaEmulator.Core.DevTools
{
    /// <summary>
    /// Stores Beam Trace Entry data used by the emulator.
    /// </summary>
    public struct BeamTraceEntry
    {
        /// <summary>
        /// Stores the Global Chip Cycle.
        /// </summary>
        public long GlobalChipCycle;
        /// <summary>
        /// Stores the Frame.
        /// </summary>
        public long Frame;
        /// <summary>
        /// Stores the Beam X.
        /// </summary>
        public int BeamX;
        /// <summary>
        /// Stores the Beam Y.
        /// </summary>
        public int BeamY;
        /// <summary>
        /// Stores the Is H Blank.
        /// </summary>
        public bool IsHBlank;
        /// <summary>
        /// Stores the Is V Blank.
        /// </summary>
        public bool IsVBlank;
    }
}
