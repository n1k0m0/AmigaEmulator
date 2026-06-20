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
    /// Gets a concrete Amiga hardware profile.
    /// </summary>
    public sealed class AmigaModel
    {
        /// <summary>
        /// Default PAL OCS Amiga 500 with 512 KB Chip RAM.
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
        /// PAL OCS Amiga 500 with the common 512 KB trapdoor Slow RAM expansion.
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
        /// PAL OCS Amiga 500 profile with 1 MB Chip RAM.
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
        /// PAL OCS Amiga 500 profile with 2 MB Chip RAM.
        /// </summary>
        public static readonly AmigaModel A500Pal2MbChip = new AmigaModel(
            "A500 PAL OCS + 2MB CHIP",
            7093790,
            2 * 1024 * 1024,
            0,
            227,
            312,
            640,
            256);

        /// <summary>
        /// PAL OCS Amiga 500 profile with 2 MB Chip RAM and 8 MB Zorro II Fast RAM.
        /// </summary>
        public static readonly AmigaModel A500Pal2MbChipFast8Mb = new AmigaModel(
            "A500 PAL OCS + 2MB CHIP + 8MB FAST",
            7093790,
            2 * 1024 * 1024,
            0,
            8 * 1024 * 1024,
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
            : this(name, cpuClockHz, chipRamBytes, slowRamBytes, 0, chipCyclesPerLine, linesPerFrame, visibleWidth, visibleHeight)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AmigaModel class.
        /// </summary>
        public AmigaModel(
            string name,
            int cpuClockHz,
            int chipRamBytes,
            int slowRamBytes,
            int fastRamBytes,
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
            FastRamBytes = fastRamBytes;
            ChipCyclesPerLine = chipCyclesPerLine;
            LinesPerFrame = linesPerFrame;
            VisibleWidth = visibleWidth;
            VisibleHeight = visibleHeight;
        }

        /// <summary>
        /// Gets the display name for this hardware profile.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the configured 68000 CPU clock in hertz.
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
        /// Gets the amount of Chip RAM mapped from address zero.
        /// </summary>
        public int ChipRamBytes { get; private set; }

        /// <summary>
        /// Gets the amount of trapdoor Slow RAM mapped at the A500 Slow RAM range.
        /// </summary>
        public int SlowRamBytes { get; private set; }

        /// <summary>
        /// Gets the configured Zorro II style Fast RAM bytes.
        /// </summary>
        public int FastRamBytes { get; private set; }

        /// <summary>
        /// Gets the PAL chip cycles per scanline used by the beam model.
        /// </summary>
        public int ChipCyclesPerLine { get; private set; }

        /// <summary>
        /// Gets the PAL scanline count per frame used by the beam model.
        /// </summary>
        public int LinesPerFrame { get; private set; }

        /// <summary>
        /// Gets the visible framebuffer width exposed to the frontend.
        /// </summary>
        public int VisibleWidth { get; private set; }

        /// <summary>
        /// Gets the visible framebuffer height exposed to the frontend.
        /// </summary>
        public int VisibleHeight { get; private set; }
    }
}
