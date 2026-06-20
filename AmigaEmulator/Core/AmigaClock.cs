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
        /// Constant for Pal CPU Clock Hz.
        /// </summary>
        public const double PalCpuClockHz = 7093790.0;

        /// <summary>
        /// Constant for Pal Chip Clock Hz.
        /// </summary>
        public const double PalChipClockHz = PalCpuClockHz / 2.0;

        /// <summary>
        /// Constant for Pal Chip Cycles Per Line.
        /// </summary>
        public const int PalChipCyclesPerLine = 227;

        /// <summary>
        /// Constant for Pal Lines Per Frame.
        /// </summary>
        public const int PalLinesPerFrame = 312;

        /// <summary>
        /// Constant for Pal Frame Rate Hz.
        /// </summary>
        public const double PalFrameRateHz = PalChipClockHz / (PalChipCyclesPerLine * PalLinesPerFrame);

        /// <summary>
        /// Stores Chip Cycle.
        /// </summary>
        public long ChipCycle;

        /// <summary>
        /// Stores Frame.
        /// </summary>
        public long Frame;

        /// <summary>
        /// Stores Beam X.
        /// </summary>
        public int BeamX;

        /// <summary>
        /// Stores Beam Y.
        /// </summary>
        public int BeamY;

        /// <summary>
        /// Runs Advance.
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
