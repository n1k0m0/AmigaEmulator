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

namespace AmigaEmulator.Core
{
    /// <summary>
    /// Describes a concrete Amiga hardware profile.
    /// </summary>
    public sealed class AmigaModel
    {
        /// <summary>
        /// Initializes a new instance of the AmigaModel class.
        /// </summary>
        public static readonly AmigaModel A500Pal = new AmigaModel(
            "A500 PAL OCS",
            7093790,
            512 * 1024,
            0,
            227,
            312,
            640,
            256);

        /// <summary>
        /// Initializes a new instance of the AmigaModel class.
        /// </summary>
        public static readonly AmigaModel A500Pal512Slow512 = new AmigaModel(
            "A500 PAL OCS + 512K SLOW",
            7093790,
            512 * 1024,
            512 * 1024,
            227,
            312,
            640,
            256);

        /// <summary>
        /// Initializes a new instance of the AmigaModel class.
        /// </summary>
        public static readonly AmigaModel A500Pal1MbChip = new AmigaModel(
            "A500 PAL OCS + 1MB CHIP",
            7093790,
            1024 * 1024,
            0,
            227,
            312,
            640,
            256);

        /// <summary>
        /// Gets the default PAL A500 model profile.
        /// </summary>
        public static AmigaModel PalA500
        {
            get { return A500Pal; }
        }

        /// <summary>
        /// Initializes a new instance of the AmigaModel class.
        /// </summary>
        public AmigaModel(
            string name,
            int cpuClockHz,
            int chipRamBytes,
            int slowRamBytes,
            int chipCyclesPerLine,
            int linesPerFrame,
            int visibleWidth,
            int visibleHeight)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Model name is required.", nameof(name));
            }

            Name = name;
            CpuClockHz = cpuClockHz;
            ChipRamBytes = chipRamBytes;
            SlowRamBytes = slowRamBytes;
            ChipCyclesPerLine = chipCyclesPerLine;
            LinesPerFrame = linesPerFrame;
            VisibleWidth = visibleWidth;
            VisibleHeight = visibleHeight;
        }

        /// <summary>
        /// Gets or updates the Name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets or updates the CPU Clock Hz.
        /// </summary>
        public int CpuClockHz { get; private set; }

        /// <summary>
        /// Gets the PAL chip clock frequency in hertz.
        /// </summary>
        public double ChipClockHz
        {
            get { return CpuClockHz / 2.0; }
        }

        /// <summary>
        /// Gets the PAL frame rate derived from the beam timing.
        /// </summary>
        public double FrameRateHz
        {
            get { return ChipClockHz / (ChipCyclesPerLine * LinesPerFrame); }
        }

        /// <summary>
        /// Gets or updates the Chip RAM Bytes.
        /// </summary>
        public int ChipRamBytes { get; private set; }

        /// <summary>
        /// Gets or updates the Slow RAM Bytes.
        /// </summary>
        public int SlowRamBytes { get; private set; }

        /// <summary>
        /// Gets or updates the Chip Cycles period Line.
        /// </summary>
        public int ChipCyclesPerLine { get; private set; }

        /// <summary>
        /// Gets or updates the Lines period Frame.
        /// </summary>
        public int LinesPerFrame { get; private set; }

        /// <summary>
        /// Gets or updates the Visible Width.
        /// </summary>
        public int VisibleWidth { get; private set; }

        /// <summary>
        /// Gets or updates the Visible Height.
        /// </summary>
        public int VisibleHeight { get; private set; }
    }
}
