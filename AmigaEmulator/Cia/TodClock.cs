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
namespace AmigaEmulator.Core.Cia
{
    /// <summary>
    /// Provides TOD Clock behavior used by the emulator.
    /// </summary>
    public sealed class TodClock
    {
        /// <summary>
        /// Gets or updates the Tenth Seconds.
        /// </summary>
        public int TenthSeconds { get; private set; }

        /// <summary>
        /// Gets or updates the Seconds.
        /// </summary>
        public int Seconds { get; private set; }

        /// <summary>
        /// Gets or updates the Minutes.
        /// </summary>
        public int Minutes { get; private set; }

        /// <summary>
        /// Gets or updates the Hours.
        /// </summary>
        public int Hours { get; private set; }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            TenthSeconds = 0;
            Seconds = 0;
            Minutes = 0;
            Hours = 0;
        }

        /// <summary>
        /// Advances the Tenth Second state by one emulator tick.
        /// </summary>
        public void TickTenthSecond()
        {
            TenthSeconds++;
            if (TenthSeconds < 10)
            {
                return;
            }

            TenthSeconds = 0;
            Seconds++;
            if (Seconds >= 60)
            {
                Seconds = 0;
                Minutes++;
            }

            if (Minutes >= 60)
            {
                Minutes = 0;
                Hours = (Hours + 1) % 24;
            }
        }
    }
}
