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
    public sealed class AmigaInputDevice
    {
        /// <summary>
        /// Constant for Keyboard Send Delay E Clocks.
        /// </summary>
        private const int KeyboardSendDelayEClocks = 8;
        // HRM Appendix G specifies approximately 60 us per keyboard clock bit.
        // PAL/NTSC E clocks are about 0.71 MHz; 43 ticks approximate that period.
        private const int KeyboardBitPeriodEClocks = 43;
        // HRM Appendix G: a missing handshake starts recovery after 143 ms;
        // recovery clocks one logical 1 per timeout, rather than another byte.
        private const int KeyboardHandshakeTimeoutEClocks = 102_000;

        /// <summary>
        /// Stores keyboard Queue.
        /// </summary>
        private readonly Queue<byte> _keyboardQueue = new Queue<byte>();
        private bool _keyboardAwaitingHandshake;
        private uint _keyboardHandshakeSequence;
        private int _keyboardHandshakeTimeout = KeyboardHandshakeTimeoutEClocks;
        private byte _keyboardLastByte;
        private bool _keyboardLastByteValid;
        private bool _keyboardResynchronizing;
        private bool _keyboardNotifyLostSync;
        private bool _keyboardLostSyncPending;
        private bool _keyboardSendingLostSync;
        /// <summary>
        /// Stores pressed Raw Keys.
        /// </summary>
        private readonly HashSet<byte> _pressedRawKeys = new HashSet<byte>();
        /// <summary>
        /// Stores ports.
        /// </summary>
        private readonly PortState[] _ports =
        {
            /// <summary>
            /// Runs Port State.
            /// </summary>
            new PortState(),
            /// <summary>
            /// Runs Port State.
            /// </summary>
            new PortState()
        };

        /// <summary>
        /// Stores CIA A.
        /// </summary>
        private Cia8520 _ciaA;
        /// <summary>
        /// Stores keyboard Delay.
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

        public AmigaInputPortMode Port0Mode { get; private set; }

        public AmigaInputPortMode Port1Mode { get; private set; }

        /// <summary>
        /// Gets Queued Keyboard Events.
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
            // Older snapshots predate explicit keyboard acknowledgement.
            _keyboardAwaitingHandshake = false;
            _keyboardHandshakeSequence = 0;
            // Legacy snapshots did not retain the outstanding byte. Recover
            // synchronization and queued events, without inventing that byte.
            _keyboardHandshakeTimeout = KeyboardHandshakeTimeoutEClocks;
            _keyboardLastByte = 0;
            _keyboardLastByteValid = false;
            _keyboardResynchronizing = false;
            _keyboardNotifyLostSync = false;
            _keyboardLostSyncPending = false;
            _keyboardSendingLostSync = false;
            StateSerializer.ReadObjectFields(reader, this, "_ciaA");
            foreach (PortState port in _ports)
            {
                port.GamepadButtons = AmigaJoystickButtons.None;
                port.NetworkButtons = AmigaJoystickButtons.None;
            }
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            _keyboardQueue.Clear();
            _pressedRawKeys.Clear();
            _keyboardDelay = 0;
            // Power-up first synchronizes with the host. Its acknowledgement
            // is followed by FD/FE, without the timeout-only F9 notification.
            _keyboardAwaitingHandshake = true;
            _keyboardHandshakeSequence = _ciaA?.SerialInputHandshakeSequence ?? 0;
            _keyboardHandshakeTimeout = 0;
            _keyboardLastByte = 0;
            _keyboardLastByteValid = false;
            _keyboardResynchronizing = true;
            _keyboardNotifyLostSync = false;
            _keyboardLostSyncPending = false;
            _keyboardSendingLostSync = false;
            EnqueueKeyboardSpecialCode(0xFD);
            EnqueueKeyboardSpecialCode(0xFE);
            for (int index = 0; index < _ports.Length; index++)
            {
                _ports[index].Reset();
            }
        }

        /// <summary>
        /// Releases all held host-driven keys, joystick directions, and mouse buttons.
        /// </summary>
        public void ReleaseAll()
        {
            foreach (byte rawKey in _pressedRawKeys)
            {
                EnqueueKeyboardEvent(rawKey, true);
            }

            _pressedRawKeys.Clear();
            for (int index = 0; index < _ports.Length; index++)
            {
                _ports[index].JoystickButtons = AmigaJoystickButtons.None;
                _ports[index].GamepadButtons = AmigaJoystickButtons.None;
                _ports[index].NetworkButtons = AmigaJoystickButtons.None;
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
        /// Runs Keyboard Key Down.
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
        /// Runs Keyboard Key Up.
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

        /// <summary>Physical controller state is independent of keyboard/mouse input.</summary>
        public void SetNetworkButtons(AmigaControlPort port, AmigaJoystickButtons buttons) => GetPort(port).NetworkButtons = buttons;

        public void SetGamepadButtons(AmigaControlPort port, AmigaJoystickButtons buttons)
        {
            GetPort(port).GamepadButtons = buttons;
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
        /// Runs Move Mouse.
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
                return EncodeJoystickDirections(state.JoystickButtons | state.GamepadButtons | state.NetworkButtons);
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
                ((_ports[0].JoystickButtons | _ports[0].GamepadButtons | _ports[0].NetworkButtons) & AmigaJoystickButtons.Fire) != 0)
            {
                value &= unchecked((byte)~0x40);
            }

            if (UsesFireButton(AmigaControlPort.Port1) &&
                ((_ports[1].JoystickButtons | _ports[1].GamepadButtons | _ports[1].NetworkButtons) & AmigaJoystickButtons.Fire) != 0)
            {
                value &= unchecked((byte)~0x80);
            }

            return value;
        }

        public ushort ReadPotInp(ushort potgo)
        {
            ushort value = 0x5500;
            if (((_ports[0].JoystickButtons | _ports[0].GamepadButtons | _ports[0].NetworkButtons) & AmigaJoystickButtons.Third) != 0)
            {
                value &= unchecked((ushort)~0x0100);
            }

            if (((_ports[0].JoystickButtons | _ports[0].GamepadButtons | _ports[0].NetworkButtons) & AmigaJoystickButtons.Second) != 0)
            {
                value &= unchecked((ushort)~0x0400);
            }

            if (((_ports[1].JoystickButtons | _ports[1].GamepadButtons | _ports[1].NetworkButtons) & AmigaJoystickButtons.Third) != 0)
            {
                value &= unchecked((ushort)~0x1000);
            }

            if (((_ports[1].JoystickButtons | _ports[1].GamepadButtons | _ports[1].NetworkButtons) & AmigaJoystickButtons.Second) != 0)
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
            if (_ciaA == null || (_keyboardQueue.Count == 0 && !_keyboardAwaitingHandshake &&
                !_keyboardLostSyncPending && !_keyboardLastByteValid))
            {
                return;
            }

            // An ICR read only acknowledges the CIA interrupt. Sending another
            // byte at that point races the keyboard ISR's subsequent CRA output
            // handshake, which aborts the new transfer and can lose key releases.
            if (_keyboardAwaitingHandshake)
            {
                if (_ciaA.SerialInputHandshakeSequence == _keyboardHandshakeSequence)
                {
                    if (_keyboardHandshakeTimeout > 0 && --_keyboardHandshakeTimeout > 0) return;
                    if (!_keyboardResynchronizing)
                    {
                        _keyboardResynchronizing = true;
                        _keyboardNotifyLostSync = true;
                        _keyboardSendingLostSync = false;
                    }
                    // KDAT is active low: the recovery's logical 1 is a low
                    // physical bit, so the garbage character ends as a release.
                    _ciaA.ClockSerialInputBit(false);
                    _keyboardHandshakeTimeout = KeyboardHandshakeTimeoutEClocks;
                    return;
                }
                if (_keyboardResynchronizing)
                {
                    _keyboardLostSyncPending = _keyboardNotifyLostSync;
                    _keyboardResynchronizing = false;
                    _keyboardNotifyLostSync = false;
                }
                else if (_keyboardSendingLostSync) _keyboardSendingLostSync = false;
                else _keyboardLastByteValid = false;
                _keyboardAwaitingHandshake = false;
                _keyboardDelay = KeyboardSendDelayEClocks;
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

            byte value;
            if (_keyboardLostSyncPending)
            {
                value = EncodeKeyboardDecodedByte(0xF9);
                _keyboardLostSyncPending = false;
                _keyboardSendingLostSync = true;
            }
            else if (_keyboardLastByteValid) value = _keyboardLastByte;
            else
            {
                if (_keyboardQueue.Count == 0) return;
                value = _keyboardLastByte = _keyboardQueue.Dequeue();
                _keyboardLastByteValid = true;
            }
            _ciaA.QueueSerialInputByte(value, KeyboardBitPeriodEClocks);
            _keyboardHandshakeSequence = _ciaA.SerialInputHandshakeSequence;
            _keyboardAwaitingHandshake = true;
            _keyboardHandshakeTimeout = KeyboardHandshakeTimeoutEClocks + 8 * KeyboardBitPeriodEClocks;
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
        /// Runs Enqueue Keyboard Event.
        /// </summary>
        private void EnqueueKeyboardEvent(byte rawKeyCode, bool released)
        {
            _keyboardQueue.Enqueue(EncodeKeyboardSerialByte(rawKeyCode, released));
        }

        /// <summary>
        /// Runs Enqueue Keyboard Special Code.
        /// </summary>
        private void EnqueueKeyboardSpecialCode(byte code)
        {
            _keyboardQueue.Enqueue(EncodeKeyboardDecodedByte(code));
        }

        /// <summary>
        /// Runs Uses Fire Button.
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
        /// Runs Normalize Raw Key.
        /// </summary>
        private static byte NormalizeRawKey(byte rawKeyCode)
        {
            return (byte)(rawKeyCode & 0x7F);
        }

        private sealed class PortState
        {
            /// <summary>
            /// Stores Mouse X.
            /// </summary>
            public byte MouseX;
            /// <summary>
            /// Stores Mouse Y.
            /// </summary>
            public byte MouseY;
            /// <summary>
            /// Stores Joystick Buttons.
            /// </summary>
            public AmigaJoystickButtons JoystickButtons;
            public AmigaJoystickButtons GamepadButtons;
            [NonSerialized]
            public AmigaJoystickButtons NetworkButtons;

            /// <summary>
            /// Resets the value state.
            /// </summary>
            public void Reset()
            {
                MouseX = 0;
                MouseY = 0;
                JoystickButtons = AmigaJoystickButtons.None;
                GamepadButtons = AmigaJoystickButtons.None; NetworkButtons = AmigaJoystickButtons.None;
            }
        }
    }
}
