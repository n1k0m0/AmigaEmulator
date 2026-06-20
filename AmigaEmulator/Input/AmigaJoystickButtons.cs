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
        /// Represents None.
        /// </summary>
        None = 0,
        /// <summary>
        /// Represents Up.
        /// </summary>
        Up = 1,
        /// <summary>
        /// Represents Down.
        /// </summary>
        Down = 2,
        /// <summary>
        /// Represents Left.
        /// </summary>
        Left = 4,
        /// <summary>
        /// Represents Right.
        /// </summary>
        Right = 8,
        /// <summary>
        /// Represents Fire.
        /// </summary>
        Fire = 16,
        /// <summary>
        /// Represents Second.
        /// </summary>
        Second = 32,
        /// <summary>
        /// Represents Third.
        /// </summary>
        Third = 64
    }
}
