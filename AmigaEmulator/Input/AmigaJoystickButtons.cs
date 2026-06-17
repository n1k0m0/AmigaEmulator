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

namespace AmigaEmulator.Core.Input
{
    /// <summary>
    /// Lists the supported Amiga Joystick Buttons values.
    /// </summary>
    [Flags]
    public enum AmigaJoystickButtons
    {
        /// <summary>
        /// Represents the N on e option.
        /// </summary>
        None = 0,
        /// <summary>
        /// Represents the U p option.
        /// </summary>
        Up = 1,
        /// <summary>
        /// Represents the D ow n option.
        /// </summary>
        Down = 2,
        /// <summary>
        /// Represents the L ef t option.
        /// </summary>
        Left = 4,
        /// <summary>
        /// Represents the R ig ht option.
        /// </summary>
        Right = 8,
        /// <summary>
        /// Represents the F ir e option.
        /// </summary>
        Fire = 16,
        /// <summary>
        /// Represents the S ec on d option.
        /// </summary>
        Second = 32,
        /// <summary>
        /// Represents the T hi rd option.
        /// </summary>
        Third = 64
    }
}
