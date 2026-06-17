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
    /// Selects how aggressively the core spends work on timing detail.
    /// </summary>
    public sealed class AmigaAccuracyOptions
    {
        /// <summary>
        /// Executes the Fast logic.
        /// </summary>
        public static readonly AmigaAccuracyOptions Fast = new AmigaAccuracyOptions
        {
            Name = "Fast",
            EnableDmaSlotTrace = false,
            EnableCpuTrace = false,
            StrictChipBusArbitration = false
        };

        /// <summary>
        /// Executes the Compatible logic.
        /// </summary>
        public static readonly AmigaAccuracyOptions Compatible = new AmigaAccuracyOptions
        {
            Name = "Compatible",
            EnableDmaSlotTrace = true,
            EnableCpuTrace = true,
            StrictChipBusArbitration = true
        };

        /// <summary>
        /// Executes the Strict logic.
        /// </summary>
        public static readonly AmigaAccuracyOptions Strict = new AmigaAccuracyOptions
        {
            Name = "Strict",
            EnableDmaSlotTrace = true,
            EnableCpuTrace = true,
            StrictChipBusArbitration = true
        };

        /// <summary>
        /// Gets or updates the Name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or updates the Enable DMA Slot Trace.
        /// </summary>
        public bool EnableDmaSlotTrace { get; set; }

        /// <summary>
        /// Gets or updates the Enable CPU Trace.
        /// </summary>
        public bool EnableCpuTrace { get; set; }

        /// <summary>
        /// Gets or updates the Strict Chip Bus Arbitration.
        /// </summary>
        public bool StrictChipBusArbitration { get; set; }

        /// <summary>
        /// Executes the Clone logic.
        /// </summary>
        public AmigaAccuracyOptions Clone()
        {
            return new AmigaAccuracyOptions
            {
                Name = Name,
                EnableDmaSlotTrace = EnableDmaSlotTrace,
                EnableCpuTrace = EnableCpuTrace,
                StrictChipBusArbitration = StrictChipBusArbitration
            };
        }
    }
}
