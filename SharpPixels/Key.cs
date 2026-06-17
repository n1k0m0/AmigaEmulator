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
using System.Windows.Forms;

namespace SharpPixels
{
    /// <summary>
    /// Lists the host keys exposed by the minimal SharpPixels frontend.
    /// </summary>
    public enum Key
    {
        /// <summary>
        /// Represents the U nk no wn option.
        /// </summary>
        Unknown,
        /// <summary>
        /// Represents the E sc ap e option.
        /// </summary>
        Escape,
        /// <summary>
        /// Represents the E nt er option.
        /// </summary>
        Enter,
        /// <summary>
        /// Represents the S pa ce option.
        /// </summary>
        Space,
        /// <summary>
        /// Represents the T ab option.
        /// </summary>
        Tab,
        /// <summary>
        /// Represents the B ac ks pa ce option.
        /// </summary>
        Backspace,
        /// <summary>
        /// Represents the D el et e option.
        /// </summary>
        Delete,
        /// <summary>
        /// Represents the H om e option.
        /// </summary>
        Home,
        /// <summary>
        /// Represents the E nd option.
        /// </summary>
        End,
        /// <summary>
        /// Represents the P ag eU p option.
        /// </summary>
        PageUp,
        /// <summary>
        /// Represents the P ag eD ow n option.
        /// </summary>
        PageDown,
        /// <summary>
        /// Represents the U p option.
        /// </summary>
        Up,
        /// <summary>
        /// Represents the D ow n option.
        /// </summary>
        Down,
        /// <summary>
        /// Represents the L ef t option.
        /// </summary>
        Left,
        /// <summary>
        /// Represents the R ig ht option.
        /// </summary>
        Right,
        /// <summary>
        /// Represents the F1 option.
        /// </summary>
        F1,
        /// <summary>
        /// Represents the F2 option.
        /// </summary>
        F2,
        /// <summary>
        /// Represents the F3 option.
        /// </summary>
        F3,
        /// <summary>
        /// Represents the F4 option.
        /// </summary>
        F4,
        /// <summary>
        /// Represents the F5 option.
        /// </summary>
        F5,
        /// <summary>
        /// Represents the F6 option.
        /// </summary>
        F6,
        /// <summary>
        /// Represents the F7 option.
        /// </summary>
        F7,
        /// <summary>
        /// Represents the F8 option.
        /// </summary>
        F8,
        /// <summary>
        /// Represents the F9 option.
        /// </summary>
        F9,
        /// <summary>
        /// Represents the F10 option.
        /// </summary>
        F10,
        /// <summary>
        /// Represents the F11 option.
        /// </summary>
        F11,
        /// <summary>
        /// Represents the F12 option.
        /// </summary>
        F12,
        /// <summary>
        /// Represents the A option.
        /// </summary>
        A,
        /// <summary>
        /// Represents the B option.
        /// </summary>
        B,
        /// <summary>
        /// Represents the C option.
        /// </summary>
        C,
        /// <summary>
        /// Represents the D option.
        /// </summary>
        D,
        /// <summary>
        /// Represents the E option.
        /// </summary>
        E,
        /// <summary>
        /// Represents the F option.
        /// </summary>
        F,
        /// <summary>
        /// Represents the G option.
        /// </summary>
        G,
        /// <summary>
        /// Represents the H option.
        /// </summary>
        H,
        /// <summary>
        /// Represents the I option.
        /// </summary>
        I,
        /// <summary>
        /// Represents the J option.
        /// </summary>
        J,
        /// <summary>
        /// Represents the K option.
        /// </summary>
        K,
        /// <summary>
        /// Represents the L option.
        /// </summary>
        L,
        /// <summary>
        /// Represents the M option.
        /// </summary>
        M,
        /// <summary>
        /// Represents the N option.
        /// </summary>
        N,
        /// <summary>
        /// Represents the O option.
        /// </summary>
        O,
        /// <summary>
        /// Represents the P option.
        /// </summary>
        P,
        /// <summary>
        /// Represents the Q option.
        /// </summary>
        Q,
        /// <summary>
        /// Represents the R option.
        /// </summary>
        R,
        /// <summary>
        /// Represents the S option.
        /// </summary>
        S,
        /// <summary>
        /// Represents the T option.
        /// </summary>
        T,
        /// <summary>
        /// Represents the U option.
        /// </summary>
        U,
        /// <summary>
        /// Represents the V option.
        /// </summary>
        V,
        /// <summary>
        /// Represents the W option.
        /// </summary>
        W,
        /// <summary>
        /// Represents the X option.
        /// </summary>
        X,
        /// <summary>
        /// Represents the Y option.
        /// </summary>
        Y,
        /// <summary>
        /// Represents the Z option.
        /// </summary>
        Z,
        /// <summary>
        /// Represents the D0 option.
        /// </summary>
        D0,
        /// <summary>
        /// Represents the D1 option.
        /// </summary>
        D1,
        /// <summary>
        /// Represents the D2 option.
        /// </summary>
        D2,
        /// <summary>
        /// Represents the D3 option.
        /// </summary>
        D3,
        /// <summary>
        /// Represents the D4 option.
        /// </summary>
        D4,
        /// <summary>
        /// Represents the D5 option.
        /// </summary>
        D5,
        /// <summary>
        /// Represents the D6 option.
        /// </summary>
        D6,
        /// <summary>
        /// Represents the D7 option.
        /// </summary>
        D7,
        /// <summary>
        /// Represents the D8 option.
        /// </summary>
        D8,
        /// <summary>
        /// Represents the D9 option.
        /// </summary>
        D9,
        /// <summary>
        /// Represents the M in us option.
        /// </summary>
        Minus,
        /// <summary>
        /// Represents the E qu al s option.
        /// </summary>
        Equals,
        /// <summary>
        /// Represents the O pe nB ra ck et option.
        /// </summary>
        OpenBracket,
        /// <summary>
        /// Represents the C lo se Br ac ke t option.
        /// </summary>
        CloseBracket,
        /// <summary>
        /// Represents the S em ic ol on option.
        /// </summary>
        Semicolon,
        /// <summary>
        /// Represents the A po st ro ph e option.
        /// </summary>
        Apostrophe,
        /// <summary>
        /// Represents the B ac ks la sh option.
        /// </summary>
        Backslash,
        /// <summary>
        /// Represents the C om ma option.
        /// </summary>
        Comma,
        /// <summary>
        /// Represents the P er io d option.
        /// </summary>
        Period,
        /// <summary>
        /// Represents the S la sh option.
        /// </summary>
        Slash,
        /// <summary>
        /// Represents the G ra ve option.
        /// </summary>
        Grave,
        /// <summary>
        /// Represents the S hi ft Le ft option.
        /// </summary>
        ShiftLeft,
        /// <summary>
        /// Represents the S hi ft Ri gh t option.
        /// </summary>
        ShiftRight,
        /// <summary>
        /// Represents the C on tr ol Le ft option.
        /// </summary>
        ControlLeft,
        /// <summary>
        /// Represents the C on tr ol Ri gh t option.
        /// </summary>
        ControlRight,
        /// <summary>
        /// Represents the A lt Le ft option.
        /// </summary>
        AltLeft,
        /// <summary>
        /// Represents the A lt Ri gh t option.
        /// </summary>
        AltRight,
        /// <summary>
        /// Represents the C ap sL oc k option.
        /// </summary>
        CapsLock,
        /// <summary>
        /// Represents the N um Pa d0 option.
        /// </summary>
        NumPad0,
        /// <summary>
        /// Represents the N um Pa d1 option.
        /// </summary>
        NumPad1,
        /// <summary>
        /// Represents the N um Pa d2 option.
        /// </summary>
        NumPad2,
        /// <summary>
        /// Represents the N um Pa d3 option.
        /// </summary>
        NumPad3,
        /// <summary>
        /// Represents the N um Pa d4 option.
        /// </summary>
        NumPad4,
        /// <summary>
        /// Represents the N um Pa d5 option.
        /// </summary>
        NumPad5,
        /// <summary>
        /// Represents the N um Pa d6 option.
        /// </summary>
        NumPad6,
        /// <summary>
        /// Represents the N um Pa d7 option.
        /// </summary>
        NumPad7,
        /// <summary>
        /// Represents the N um Pa d8 option.
        /// </summary>
        NumPad8,
        /// <summary>
        /// Represents the N um Pa d9 option.
        /// </summary>
        NumPad9
    }

    /// <summary>
    /// Converts WinForms key values to SharpPixels key values.
    /// </summary>
    public static class KeyMapper
    {
        /// <summary>
        /// Executes the From Forms Key logic.
        /// </summary>
        public static Key FromFormsKey(Keys key)
        {
            switch (key & Keys.KeyCode)
            {
                case Keys.Escape: return Key.Escape;
                case Keys.Enter: return Key.Enter;
                case Keys.Space: return Key.Space;
                case Keys.Tab: return Key.Tab;
                case Keys.Back: return Key.Backspace;
                case Keys.Delete: return Key.Delete;
                case Keys.Home: return Key.Home;
                case Keys.End: return Key.End;
                case Keys.PageUp: return Key.PageUp;
                case Keys.PageDown: return Key.PageDown;
                case Keys.Up: return Key.Up;
                case Keys.Down: return Key.Down;
                case Keys.Left: return Key.Left;
                case Keys.Right: return Key.Right;
                case Keys.F1: return Key.F1;
                case Keys.F2: return Key.F2;
                case Keys.F3: return Key.F3;
                case Keys.F4: return Key.F4;
                case Keys.F5: return Key.F5;
                case Keys.F6: return Key.F6;
                case Keys.F7: return Key.F7;
                case Keys.F8: return Key.F8;
                case Keys.F9: return Key.F9;
                case Keys.F10: return Key.F10;
                case Keys.F11: return Key.F11;
                case Keys.F12: return Key.F12;
                case Keys.A: return Key.A;
                case Keys.B: return Key.B;
                case Keys.C: return Key.C;
                case Keys.D: return Key.D;
                case Keys.E: return Key.E;
                case Keys.F: return Key.F;
                case Keys.G: return Key.G;
                case Keys.H: return Key.H;
                case Keys.I: return Key.I;
                case Keys.J: return Key.J;
                case Keys.K: return Key.K;
                case Keys.L: return Key.L;
                case Keys.M: return Key.M;
                case Keys.N: return Key.N;
                case Keys.O: return Key.O;
                case Keys.P: return Key.P;
                case Keys.Q: return Key.Q;
                case Keys.R: return Key.R;
                case Keys.S: return Key.S;
                case Keys.T: return Key.T;
                case Keys.U: return Key.U;
                case Keys.V: return Key.V;
                case Keys.W: return Key.W;
                case Keys.X: return Key.X;
                case Keys.Y: return Key.Y;
                case Keys.Z: return Key.Z;
                case Keys.D0: return Key.D0;
                case Keys.D1: return Key.D1;
                case Keys.D2: return Key.D2;
                case Keys.D3: return Key.D3;
                case Keys.D4: return Key.D4;
                case Keys.D5: return Key.D5;
                case Keys.D6: return Key.D6;
                case Keys.D7: return Key.D7;
                case Keys.D8: return Key.D8;
                case Keys.D9: return Key.D9;
                case Keys.OemMinus: return Key.Minus;
                case Keys.Oemplus: return Key.Equals;
                case Keys.OemOpenBrackets: return Key.OpenBracket;
                case Keys.OemCloseBrackets: return Key.CloseBracket;
                case Keys.OemSemicolon: return Key.Semicolon;
                case Keys.OemQuotes: return Key.Apostrophe;
                case Keys.OemBackslash: return Key.Backslash;
                case Keys.Oemcomma: return Key.Comma;
                case Keys.OemPeriod: return Key.Period;
                case Keys.OemQuestion: return Key.Slash;
                case Keys.Oemtilde: return Key.Grave;
                case Keys.ShiftKey:
                case Keys.LShiftKey:
                    return Key.ShiftLeft;
                case Keys.RShiftKey: return Key.ShiftRight;
                case Keys.ControlKey:
                case Keys.LControlKey:
                    return Key.ControlLeft;
                case Keys.RControlKey: return Key.ControlRight;
                case Keys.Menu:
                case Keys.LMenu:
                    return Key.AltLeft;
                case Keys.RMenu: return Key.AltRight;
                case Keys.CapsLock: return Key.CapsLock;
                case Keys.NumPad0: return Key.NumPad0;
                case Keys.NumPad1: return Key.NumPad1;
                case Keys.NumPad2: return Key.NumPad2;
                case Keys.NumPad3: return Key.NumPad3;
                case Keys.NumPad4: return Key.NumPad4;
                case Keys.NumPad5: return Key.NumPad5;
                case Keys.NumPad6: return Key.NumPad6;
                case Keys.NumPad7: return Key.NumPad7;
                case Keys.NumPad8: return Key.NumPad8;
                case Keys.NumPad9: return Key.NumPad9;
                default: return Key.Unknown;
            }
        }
    }
}
