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
        /// Stores Fast.
        /// </summary>
        public static readonly AmigaAccuracyOptions Fast = new AmigaAccuracyOptions
        {
            Name = "Fast",
            EnableDmaSlotTrace = false,
            EnableCpuTrace = false,
            // The fast frontend profile keeps chip-bus contention relaxed until the
            // CPU/custom-chip timing model is fully cycle-exact for mid-line updates.
            StrictChipBusArbitration = false
        };

        /// <summary>
        /// Stores Compatible.
        /// </summary>
        public static readonly AmigaAccuracyOptions Compatible = new AmigaAccuracyOptions
        {
            Name = "Compatible",
            EnableDmaSlotTrace = false,
            EnableCpuTrace = false,
            StrictChipBusArbitration = true
        };

        /// <summary>
        /// Stores Strict.
        /// </summary>
        public static readonly AmigaAccuracyOptions Strict = new AmigaAccuracyOptions
        {
            Name = "Strict",
            EnableDmaSlotTrace = true,
            EnableCpuTrace = true,
            StrictChipBusArbitration = true
        };

        public string Name { get; set; }

        public bool EnableDmaSlotTrace { get; set; }

        public bool EnableCpuTrace { get; set; }

        public bool StrictChipBusArbitration { get; set; }

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
