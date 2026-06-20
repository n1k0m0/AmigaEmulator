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
        /// Represents Unknown.
        /// </summary>
        Unknown,
        /// <summary>
        /// Represents Escape.
        /// </summary>
        Escape,
        /// <summary>
        /// Represents Enter.
        /// </summary>
        Enter,
        /// <summary>
        /// Represents Space.
        /// </summary>
        Space,
        /// <summary>
        /// Represents Tab.
        /// </summary>
        Tab,
        /// <summary>
        /// Represents Backspace.
        /// </summary>
        Backspace,
        /// <summary>
        /// Represents Delete.
        /// </summary>
        Delete,
        /// <summary>
        /// Represents Home.
        /// </summary>
        Home,
        /// <summary>
        /// Represents End.
        /// </summary>
        End,
        /// <summary>
        /// Represents Page Up.
        /// </summary>
        PageUp,
        /// <summary>
        /// Represents Page Down.
        /// </summary>
        PageDown,
        /// <summary>
        /// Represents Up.
        /// </summary>
        Up,
        /// <summary>
        /// Represents Down.
        /// </summary>
        Down,
        /// <summary>
        /// Represents Left.
        /// </summary>
        Left,
        /// <summary>
        /// Represents Right.
        /// </summary>
        Right,
        /// <summary>
        /// Represents F1.
        /// </summary>
        F1,
        /// <summary>
        /// Represents F2.
        /// </summary>
        F2,
        /// <summary>
        /// Represents F3.
        /// </summary>
        F3,
        /// <summary>
        /// Represents F4.
        /// </summary>
        F4,
        /// <summary>
        /// Represents F5.
        /// </summary>
        F5,
        /// <summary>
        /// Represents F6.
        /// </summary>
        F6,
        /// <summary>
        /// Represents F7.
        /// </summary>
        F7,
        /// <summary>
        /// Represents F8.
        /// </summary>
        F8,
        /// <summary>
        /// Represents F9.
        /// </summary>
        F9,
        /// <summary>
        /// Represents F10.
        /// </summary>
        F10,
        /// <summary>
        /// Represents F11.
        /// </summary>
        F11,
        /// <summary>
        /// Represents F12.
        /// </summary>
        F12,
        /// <summary>
        /// Represents A.
        /// </summary>
        A,
        /// <summary>
        /// Represents B.
        /// </summary>
        B,
        /// <summary>
        /// Represents C.
        /// </summary>
        C,
        /// <summary>
        /// Represents D.
        /// </summary>
        D,
        /// <summary>
        /// Represents E.
        /// </summary>
        E,
        /// <summary>
        /// Represents F.
        /// </summary>
        F,
        /// <summary>
        /// Represents G.
        /// </summary>
        G,
        /// <summary>
        /// Represents H.
        /// </summary>
        H,
        /// <summary>
        /// Represents I.
        /// </summary>
        I,
        /// <summary>
        /// Represents J.
        /// </summary>
        J,
        /// <summary>
        /// Represents K.
        /// </summary>
        K,
        /// <summary>
        /// Represents L.
        /// </summary>
        L,
        /// <summary>
        /// Represents M.
        /// </summary>
        M,
        /// <summary>
        /// Represents N.
        /// </summary>
        N,
        /// <summary>
        /// Represents O.
        /// </summary>
        O,
        /// <summary>
        /// Represents P.
        /// </summary>
        P,
        /// <summary>
        /// Represents Q.
        /// </summary>
        Q,
        /// <summary>
        /// Represents R.
        /// </summary>
        R,
        /// <summary>
        /// Represents S.
        /// </summary>
        S,
        /// <summary>
        /// Represents T.
        /// </summary>
        T,
        /// <summary>
        /// Represents U.
        /// </summary>
        U,
        /// <summary>
        /// Represents V.
        /// </summary>
        V,
        /// <summary>
        /// Represents W.
        /// </summary>
        W,
        /// <summary>
        /// Represents X.
        /// </summary>
        X,
        /// <summary>
        /// Represents Y.
        /// </summary>
        Y,
        /// <summary>
        /// Represents Z.
        /// </summary>
        Z,
        /// <summary>
        /// Represents D0.
        /// </summary>
        D0,
        /// <summary>
        /// Represents D1.
        /// </summary>
        D1,
        /// <summary>
        /// Represents D2.
        /// </summary>
        D2,
        /// <summary>
        /// Represents D3.
        /// </summary>
        D3,
        /// <summary>
        /// Represents D4.
        /// </summary>
        D4,
        /// <summary>
        /// Represents D5.
        /// </summary>
        D5,
        /// <summary>
        /// Represents D6.
        /// </summary>
        D6,
        /// <summary>
        /// Represents D7.
        /// </summary>
        D7,
        /// <summary>
        /// Represents D8.
        /// </summary>
        D8,
        /// <summary>
        /// Represents D9.
        /// </summary>
        D9,
        /// <summary>
        /// Represents Minus.
        /// </summary>
        Minus,
        /// <summary>
        /// Represents Equals.
        /// </summary>
        Equals,
        /// <summary>
        /// Represents Open Bracket.
        /// </summary>
        OpenBracket,
        /// <summary>
        /// Represents Close Bracket.
        /// </summary>
        CloseBracket,
        /// <summary>
        /// Represents Semicolon.
        /// </summary>
        Semicolon,
        /// <summary>
        /// Represents Apostrophe.
        /// </summary>
        Apostrophe,
        /// <summary>
        /// Represents Backslash.
        /// </summary>
        Backslash,
        /// <summary>
        /// Represents Comma.
        /// </summary>
        Comma,
        /// <summary>
        /// Represents Period.
        /// </summary>
        Period,
        /// <summary>
        /// Represents Slash.
        /// </summary>
        Slash,
        /// <summary>
        /// Represents Grave.
        /// </summary>
        Grave,
        /// <summary>
        /// Represents Shift Left.
        /// </summary>
        ShiftLeft,
        /// <summary>
        /// Represents Shift Right.
        /// </summary>
        ShiftRight,
        /// <summary>
        /// Represents Control Left.
        /// </summary>
        ControlLeft,
        /// <summary>
        /// Represents Control Right.
        /// </summary>
        ControlRight,
        /// <summary>
        /// Represents Alt Left.
        /// </summary>
        AltLeft,
        /// <summary>
        /// Represents Alt Right.
        /// </summary>
        AltRight,
        /// <summary>
        /// Represents Caps Lock.
        /// </summary>
        CapsLock,
        /// <summary>
        /// Represents Num Pad 0.
        /// </summary>
        NumPad0,
        /// <summary>
        /// Represents Num Pad 1.
        /// </summary>
        NumPad1,
        /// <summary>
        /// Represents Num Pad 2.
        /// </summary>
        NumPad2,
        /// <summary>
        /// Represents Num Pad 3.
        /// </summary>
        NumPad3,
        /// <summary>
        /// Represents Num Pad 4.
        /// </summary>
        NumPad4,
        /// <summary>
        /// Represents Num Pad 5.
        /// </summary>
        NumPad5,
        /// <summary>
        /// Represents Num Pad 6.
        /// </summary>
        NumPad6,
        /// <summary>
        /// Represents Num Pad 7.
        /// </summary>
        NumPad7,
        /// <summary>
        /// Represents Num Pad 8.
        /// </summary>
        NumPad8,
        /// <summary>
        /// Represents Num Pad 9.
        /// </summary>
        NumPad9
    }

    /// <summary>
    /// Converts WinForms key values to SharpPixels key values.
    /// </summary>
    public static class KeyMapper
    {
        /// <summary>
        /// Runs From Forms Key.
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
