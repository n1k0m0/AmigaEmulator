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

namespace SharpPixels
{
    /// <summary>
    /// Provides keyboard input data for the SharpPixels window callbacks.
    /// </summary>
    public sealed class KeyEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the KeyEventArgs class.
        /// </summary>
        public KeyEventArgs(Key key, char keyChar, bool alt, bool control, bool shift)
        {
            Key = key;
            KeyChar = keyChar;
            Alt = alt;
            Control = control;
            Shift = shift;
        }

        public Key Key { get; private set; }

        public char KeyChar { get; private set; }

        public bool Alt { get; private set; }

        public bool Control { get; private set; }

        public bool Shift { get; private set; }

        public bool Handled { get; set; }
    }
}
