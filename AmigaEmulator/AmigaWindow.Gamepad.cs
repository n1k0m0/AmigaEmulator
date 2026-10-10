// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using AmigaEmulator.Core.Input;

namespace AmigaEmulator
{
    public sealed partial class AmigaWindow
    {
        private void PollGamepads()
        {
            _gamepads.Poll();
            if (IsNetworkClient) { SendNetworkJoystick(); return; }
            if (_system == null) return;
            _system.Input.SetGamepadButtons(AmigaControlPort.Port0, AmigaJoystickButtons.None);
            _system.Input.SetGamepadButtons(AmigaControlPort.Port1, AmigaJoystickButtons.None);
            // Menus and other applications must not send held buttons to the game.
            if (RemoteOwnsControl) return;
            if ((!Focused && !ContainsFocus) || _overlayMode != OverlayMode.None) return;
            if (_gamepads.States.Count > 0)
            {
                var port = _inputPortMode == InputPortMode.JoystickPort1MousePort2
                    ? AmigaControlPort.Port0 : AmigaControlPort.Port1;
                _system.Input.SetGamepadButtons(port, _gamepads.States[0].Buttons);
            }
            if (_inputPortMode == InputPortMode.DualJoystick && _gamepads.States.Count > 1)
                _system.Input.SetGamepadButtons(AmigaControlPort.Port0, _gamepads.States[1].Buttons);
        }

        private string FormatGamepadStatus()
        {
            if (_gamepads.States.Count == 0) return "NOT CONNECTED";
            string name = (_gamepads.States[0].Name ?? "USB GAMEPAD").ToUpperInvariant();
            if (name.Length > 18) name = name.Substring(0, 18);
            string port = _inputPortMode == InputPortMode.JoystickPort1MousePort2 ? "P1" : "P2";
            return name + " / " + port;
        }
    }
}
