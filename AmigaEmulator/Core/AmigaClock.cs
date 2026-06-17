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
namespace AmigaEmulator.Core
{
    /// <summary>
    /// Tracks deterministic chip-cycle time.
    /// </summary>
    public struct AmigaClock
    {
        /// <summary>
        /// Defines the Pal CPU Clock Hz constant.
        /// </summary>
        public const double PalCpuClockHz = 7093790.0;

        /// <summary>
        /// Defines the Pal Chip Clock Hz constant.
        /// </summary>
        public const double PalChipClockHz = PalCpuClockHz / 2.0;

        /// <summary>
        /// Defines the Pal Chip Cycles period Line constant.
        /// </summary>
        public const int PalChipCyclesPerLine = 227;

        /// <summary>
        /// Defines the Pal Lines period Frame constant.
        /// </summary>
        public const int PalLinesPerFrame = 312;

        /// <summary>
        /// Defines the Pal Frame Rate Hz constant.
        /// </summary>
        public const double PalFrameRateHz = PalChipClockHz / (PalChipCyclesPerLine * PalLinesPerFrame);

        /// <summary>
        /// Stores the Chip Cycle.
        /// </summary>
        public long ChipCycle;

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
        /// Executes the Advance logic.
        /// </summary>
        public void Advance(int chipCyclesPerLine, int linesPerFrame)
        {
            ChipCycle++;
            BeamX++;
            if (BeamX >= chipCyclesPerLine)
            {
                BeamX = 0;
                BeamY++;
                if (BeamY >= linesPerFrame)
                {
                    BeamY = 0;
                    Frame++;
                }
            }
        }
    }
}
