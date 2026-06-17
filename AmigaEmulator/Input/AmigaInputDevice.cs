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
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core.Cia;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Core.Input
{
    /// <summary>
    /// Provides Amiga Input Device behavior used by the emulator.
    /// </summary>
    public sealed class AmigaInputDevice
    {
        /// <summary>
        /// Defines the Keyboard Send Delay E Clocks constant.
        /// </summary>
        private const int KeyboardSendDelayEClocks = 8;

        /// <summary>
        /// Executes the keyboard Queue logic.
        /// </summary>
        private readonly Queue<byte> _keyboardQueue = new Queue<byte>();
        /// <summary>
        /// Executes the pressed Raw Keys logic.
        /// </summary>
        private readonly HashSet<byte> _pressedRawKeys = new HashSet<byte>();
        /// <summary>
        /// Executes the ports logic.
        /// </summary>
        private readonly PortState[] _ports =
        {
            /// <summary>
            /// Executes the Port State logic.
            /// </summary>
            new PortState(),
            /// <summary>
            /// Executes the Port State logic.
            /// </summary>
            new PortState()
        };

        /// <summary>
        /// Tracks the CIA A.
        /// </summary>
        private Cia8520 _ciaA;
        /// <summary>
        /// Tracks the keyboard Delay.
        /// </summary>
        private int _keyboardDelay;

        /// <summary>
        /// Initializes a new instance of the AmigaInputDevice class.
        /// </summary>
        public AmigaInputDevice()
        {
            Port0Mode = AmigaInputPortMode.Mouse;
            Port1Mode = AmigaInputPortMode.Joystick;
        }

        /// <summary>
        /// Gets or updates the Port0 Mode.
        /// </summary>
        public AmigaInputPortMode Port0Mode { get; private set; }

        /// <summary>
        /// Gets or updates the Port1 Mode.
        /// </summary>
        public AmigaInputPortMode Port1Mode { get; private set; }

        /// <summary>
        /// Executes the Queued Keyboard Events logic.
        /// </summary>
        public int QueuedKeyboardEvents
        {
            get { return _keyboardQueue.Count; }
        }

        /// <summary>
        /// Attaches CIA A to this component.
        /// </summary>
        public void AttachCiaA(Cia8520 ciaA)
        {
            _ciaA = ciaA;
        }

        /// <summary>
        /// Writes keyboard, joystick, mouse, and input-port state to a savestate.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            StateSerializer.WriteObjectFields(writer, this, "_ciaA");
        }

        /// <summary>
        /// Restores keyboard, joystick, mouse, and input-port state from a savestate.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            StateSerializer.ReadObjectFields(reader, this, "_ciaA");
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            _keyboardQueue.Clear();
            _pressedRawKeys.Clear();
            _keyboardDelay = 0;
            EnqueueKeyboardSpecialCode(0xFD);
            EnqueueKeyboardSpecialCode(0xFE);
            for (int index = 0; index < _ports.Length; index++)
            {
                _ports[index].Reset();
            }
        }

        /// <summary>
        /// Updates the Port Modes.
        /// </summary>
        public void SetPortModes(AmigaInputPortMode port0Mode, AmigaInputPortMode port1Mode)
        {
            Port0Mode = port0Mode;
            Port1Mode = port1Mode;
        }

        /// <summary>
        /// Executes the Keyboard Key Down logic.
        /// </summary>
        public void KeyboardKeyDown(byte rawKeyCode)
        {
            rawKeyCode = NormalizeRawKey(rawKeyCode);
            if (!_pressedRawKeys.Add(rawKeyCode))
            {
                return;
            }

            EnqueueKeyboardEvent(rawKeyCode, false);
        }

        /// <summary>
        /// Executes the Keyboard Key Up logic.
        /// </summary>
        public void KeyboardKeyUp(byte rawKeyCode)
        {
            rawKeyCode = NormalizeRawKey(rawKeyCode);
            if (!_pressedRawKeys.Remove(rawKeyCode))
            {
                return;
            }

            EnqueueKeyboardEvent(rawKeyCode, true);
        }

        /// <summary>
        /// Updates the Joystick Buttons.
        /// </summary>
        public void SetJoystickButtons(AmigaControlPort port, AmigaJoystickButtons buttons)
        {
            GetPort(port).JoystickButtons = buttons;
        }

        /// <summary>
        /// Updates the Joystick Button.
        /// </summary>
        public void SetJoystickButton(AmigaControlPort port, AmigaJoystickButtons button, bool pressed)
        {
            PortState state = GetPort(port);
            if (pressed)
            {
                state.JoystickButtons |= button;
            }
            else
            {
                state.JoystickButtons &= ~button;
            }
        }

        /// <summary>
        /// Executes the Move Mouse logic.
        /// </summary>
        public void MoveMouse(AmigaControlPort port, int deltaX, int deltaY)
        {
            if (deltaX == 0 && deltaY == 0)
            {
                return;
            }

            PortState state = GetPort(port);
            state.MouseX = (byte)(state.MouseX + deltaX);
            state.MouseY = (byte)(state.MouseY + deltaY);
        }

        /// <summary>
        /// Updates the Mouse Button.
        /// </summary>
        public void SetMouseButton(AmigaControlPort port, AmigaJoystickButtons button, bool pressed)
        {
            SetJoystickButton(port, button, pressed);
        }

        /// <summary>
        /// Builds the JOYDAT register value for the selected mouse or joystick port.
        /// </summary>
        public ushort ReadJoyDat(AmigaControlPort port)
        {
            PortState state = GetPort(port);
            AmigaInputPortMode mode = port == AmigaControlPort.Port0 ? Port0Mode : Port1Mode;
            if (mode == AmigaInputPortMode.Mouse)
            {
                return (ushort)((state.MouseY << 8) | state.MouseX);
            }

            if (mode == AmigaInputPortMode.Joystick ||
                mode == AmigaInputPortMode.KeyboardJoystick ||
                mode == AmigaInputPortMode.Gamepad)
            {
                return EncodeJoystickDirections(state.JoystickButtons);
            }

            return 0;
        }

        /// <summary>
        /// Returns the CIA A Port A Input Mask.
        /// </summary>
        public byte GetCiaAPortAInputMask()
        {
            byte mask = 0;
            if (UsesFireButton(AmigaControlPort.Port0))
            {
                mask |= 0x40;
            }

            if (UsesFireButton(AmigaControlPort.Port1))
            {
                mask |= 0x80;
            }

            return mask;
        }

        /// <summary>
        /// Returns the CIA A Port A Input Value.
        /// </summary>
        public byte GetCiaAPortAInputValue()
        {
            byte value = GetCiaAPortAInputMask();
            if (UsesFireButton(AmigaControlPort.Port0) &&
                (_ports[0].JoystickButtons & AmigaJoystickButtons.Fire) != 0)
            {
                value &= unchecked((byte)~0x40);
            }

            if (UsesFireButton(AmigaControlPort.Port1) &&
                (_ports[1].JoystickButtons & AmigaJoystickButtons.Fire) != 0)
            {
                value &= unchecked((byte)~0x80);
            }

            return value;
        }

        /// <summary>
        /// Builds the POTINP register value from current button and potentiometer inputs.
        /// </summary>
        public ushort ReadPotInp(ushort potgo)
        {
            ushort value = 0x5500;
            if ((_ports[0].JoystickButtons & AmigaJoystickButtons.Third) != 0)
            {
                value &= unchecked((ushort)~0x0100);
            }

            if ((_ports[0].JoystickButtons & AmigaJoystickButtons.Second) != 0)
            {
                value &= unchecked((ushort)~0x0400);
            }

            if ((_ports[1].JoystickButtons & AmigaJoystickButtons.Third) != 0)
            {
                value &= unchecked((ushort)~0x1000);
            }

            if ((_ports[1].JoystickButtons & AmigaJoystickButtons.Second) != 0)
            {
                value &= unchecked((ushort)~0x4000);
            }

            ushort outputBits = (ushort)(potgo & 0xAA00);
            return (ushort)(outputBits | (value & 0x5500));
        }

        /// <summary>
        /// Advances the E Clock state by one emulator tick.
        /// </summary>
        public void TickEClock()
        {
            if (_ciaA == null || _keyboardQueue.Count == 0)
            {
                return;
            }

            if (_keyboardDelay > 0)
            {
                _keyboardDelay--;
                return;
            }

            if (_ciaA.IsSerialInputPending)
            {
                return;
            }

            _ciaA.LoadSerialInputByte(_keyboardQueue.Dequeue());
            _keyboardDelay = KeyboardSendDelayEClocks;
        }

        /// <summary>
        /// Encodes Keyboard Serial Byte for Amiga-compatible use.
        /// </summary>
        public static byte EncodeKeyboardSerialByte(byte rawKeyCode, bool released)
        {
            byte decoded = (byte)((rawKeyCode & 0x7F) | (released ? 0x80 : 0x00));
            return EncodeKeyboardDecodedByte(decoded);
        }

        /// <summary>
        /// Encodes Keyboard Decoded Byte for Amiga-compatible use.
        /// </summary>
        public static byte EncodeKeyboardDecodedByte(byte decoded)
        {
            byte inverted = (byte)~decoded;
            return (byte)((inverted << 1) | (inverted >> 7));
        }

        /// <summary>
        /// Encodes Joystick Directions for Amiga-compatible use.
        /// </summary>
        public static ushort EncodeJoystickDirections(AmigaJoystickButtons buttons)
        {
            bool right = (buttons & AmigaJoystickButtons.Right) != 0;
            bool left = (buttons & AmigaJoystickButtons.Left) != 0;
            bool down = (buttons & AmigaJoystickButtons.Down) != 0;
            bool up = (buttons & AmigaJoystickButtons.Up) != 0;

            int x1 = right ? 1 : 0;
            int x0 = (down ? 1 : 0) ^ x1;
            int y1 = left ? 1 : 0;
            int y0 = (up ? 1 : 0) ^ y1;

            return (ushort)((y1 << 9) | (y0 << 8) | (x1 << 1) | x0);
        }

        /// <summary>
        /// Executes the Enqueue Keyboard Event logic.
        /// </summary>
        private void EnqueueKeyboardEvent(byte rawKeyCode, bool released)
        {
            _keyboardQueue.Enqueue(EncodeKeyboardSerialByte(rawKeyCode, released));
        }

        /// <summary>
        /// Executes the Enqueue Keyboard Special Code logic.
        /// </summary>
        private void EnqueueKeyboardSpecialCode(byte code)
        {
            _keyboardQueue.Enqueue(EncodeKeyboardDecodedByte(code));
        }

        /// <summary>
        /// Executes the Uses Fire Button logic.
        /// </summary>
        private bool UsesFireButton(AmigaControlPort port)
        {
            AmigaInputPortMode mode = port == AmigaControlPort.Port0 ? Port0Mode : Port1Mode;
            return mode == AmigaInputPortMode.Mouse ||
                mode == AmigaInputPortMode.Joystick ||
                mode == AmigaInputPortMode.KeyboardJoystick ||
                mode == AmigaInputPortMode.Gamepad;
        }

        /// <summary>
        /// Returns the Port.
        /// </summary>
        private PortState GetPort(AmigaControlPort port)
        {
            return _ports[(int)port & 1];
        }

        /// <summary>
        /// Executes the Normalize Raw Key logic.
        /// </summary>
        private static byte NormalizeRawKey(byte rawKeyCode)
        {
            return (byte)(rawKeyCode & 0x7F);
        }

        /// <summary>
        /// Provides Port State behavior used by the emulator.
        /// </summary>
        private sealed class PortState
        {
            /// <summary>
            /// Stores the Mouse X.
            /// </summary>
            public byte MouseX;
            /// <summary>
            /// Stores the Mouse Y.
            /// </summary>
            public byte MouseY;
            /// <summary>
            /// Stores the Joystick Buttons.
            /// </summary>
            public AmigaJoystickButtons JoystickButtons;

            /// <summary>
            /// Resets the value state.
            /// </summary>
            public void Reset()
            {
                MouseX = 0;
                MouseY = 0;
                JoystickButtons = AmigaJoystickButtons.None;
            }
        }
    }
}
