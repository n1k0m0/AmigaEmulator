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
using System.Text;
using AmigaEmulator.Core.Cia;
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Core.Input
{
    /// <summary>
    /// Provides Amiga Input Self Test functionality.
    /// </summary>
    public static class AmigaInputSelfTest
    {
        /// <summary>
        /// Runs the value routine.
        /// </summary>
        public static int Run(StringBuilder log)
        {
            int failures = 0;
            failures += RunCase(log, "keyboard serial encoding and CIA interrupt", TestKeyboardSerial);
            failures += RunCase(log, "joystick directions encode into JOYDAT bits", TestJoystickDirections);
            failures += RunCase(log, "mouse counters and buttons feed custom/CIA registers", TestMouseAndButtons);
            failures += RunCase(log, "POTGO start resets and runs POTDAT counters", TestPotCounters);
            failures += RunCase(log, "Windows gamepad axes, deadzone, POV and fire mapping", TestGamepadMapping);
            failures += RunCase(log, "gamepad and keyboard sources coexist and release independently", TestGamepadSources);
            failures += RunCase(log, "savestate restore clears live gamepad buttons", TestGamepadRestore);
            log.AppendLine(failures == 0 ? "Input self-test OK" : "Input self-test FAILED: " + failures);
            return failures;
        }

        /// <summary>
        /// Runs the Case routine.
        /// </summary>
        private static bool TestGamepadMapping()
        {
            var none = AmigaJoystickButtons.None;
            if (WindowsGamepadInput.MapButtons(32767, 32767, 0, 65535, 0, 65535, 0, 65535) != none) return false;
            if (WindowsGamepadInput.MapButtons(40000, 25000, 0, 65535, 0, 65535, 0, 65535) != none) return false;
            var diagonal = AmigaJoystickButtons.Right | AmigaJoystickButtons.Up;
            if (WindowsGamepadInput.MapButtons(65535, 0, 0, 65535, 0, 65535, 1, 65535) != (diagonal | AmigaJoystickButtons.Fire)) return false;
            var sectors = new[] { AmigaJoystickButtons.Up, diagonal, AmigaJoystickButtons.Right,
                AmigaJoystickButtons.Right | AmigaJoystickButtons.Down, AmigaJoystickButtons.Down,
                AmigaJoystickButtons.Down | AmigaJoystickButtons.Left, AmigaJoystickButtons.Left,
                AmigaJoystickButtons.Left | AmigaJoystickButtons.Up };
            for (int i = 0; i < sectors.Length; i++)
                if (WindowsGamepadInput.MapButtons(50, 50, 0, 100, 0, 100, 0, (uint)(i * 4500)) != sectors[i]) return false;
            if (WindowsGamepadInput.MapButtons(50, 50, 0, 100, 0, 100, 0, 35999) != AmigaJoystickButtons.Up) return false;
            for (int i = 0; i < 4; i++)
                if (WindowsGamepadInput.MapButtons(50, 50, 0, 100, 0, 100, 1u << i, 65535) != AmigaJoystickButtons.Fire) return false;
            return WindowsGamepadInput.MapButtons(0, 0, 0, 0, 0, 0, 0x30, 65535) ==
                (AmigaJoystickButtons.Second | AmigaJoystickButtons.Third);
        }

        private static bool TestGamepadSources()
        {
            var input = new AmigaInputDevice();
            var port = AmigaControlPort.Port1;
            input.SetJoystickButtons(port, AmigaJoystickButtons.Right | AmigaJoystickButtons.Fire);
            input.SetGamepadButtons(port, AmigaJoystickButtons.Up | AmigaJoystickButtons.Fire | AmigaJoystickButtons.Second | AmigaJoystickButtons.Third);
            if (input.ReadJoyDat(port) != AmigaInputDevice.EncodeJoystickDirections(AmigaJoystickButtons.Up | AmigaJoystickButtons.Right)) return false;
            if ((input.GetCiaAPortAInputValue() & 0x80) != 0 || (input.ReadPotInp(0) & 0x5000) != 0) return false;
            input.SetGamepadButtons(port, AmigaJoystickButtons.None); // Unplug must retain keyboard.
            if (input.ReadJoyDat(port) != AmigaInputDevice.EncodeJoystickDirections(AmigaJoystickButtons.Right)) return false;
            if ((input.GetCiaAPortAInputValue() & 0x80) != 0) return false;
            input.SetGamepadButtons(port, AmigaJoystickButtons.Up | AmigaJoystickButtons.Fire);
            input.SetJoystickButtons(port, AmigaJoystickButtons.None); // Key-up must retain pad.
            if (input.ReadJoyDat(port) != AmigaInputDevice.EncodeJoystickDirections(AmigaJoystickButtons.Up)) return false;
            if ((input.GetCiaAPortAInputValue() & 0x80) != 0) return false;
            input.ReleaseAll();
            return input.ReadJoyDat(port) == 0 && (input.GetCiaAPortAInputValue() & 0x80) != 0;
        }

        private static bool TestGamepadRestore()
        {
            var input = new AmigaInputDevice();
            var port = AmigaControlPort.Port1;
            input.SetJoystickButtons(port, AmigaJoystickButtons.Right);
            input.SetGamepadButtons(port, AmigaJoystickButtons.Up | AmigaJoystickButtons.Fire);
            using var buffer = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) input.SaveState(writer);
            buffer.Position = 0;
            using (var reader = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, true)) input.LoadState(reader);
            return input.ReadJoyDat(port) == AmigaInputDevice.EncodeJoystickDirections(AmigaJoystickButtons.Right)
                && (input.GetCiaAPortAInputValue() & 0x80) != 0;
        }

        private static int RunCase(StringBuilder log, string name, System.Func<bool> test)
        {
            bool passed = false;
            try
            {
                passed = test();
            }
            catch (System.Exception ex)
            {
                log.AppendLine("FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message);
                return 1;
            }

            log.AppendLine((passed ? "PASS " : "FAIL ") + name);
            return passed ? 0 : 1;
        }

        /// <summary>
        /// Runs Test Keyboard Serial.
        /// </summary>
        private static bool TestKeyboardSerial()
        {
            var cia = new Cia8520("CIA-A");
            var input = new AmigaInputDevice();
            input.AttachCiaA(cia);
            input.KeyboardKeyDown(0x20);
            input.TickEClock();
            TickCiaSerial(cia, 344);

            byte expectedDown = AmigaInputDevice.EncodeKeyboardSerialByte(0x20, false);
            bool downOk = cia.SerialData == expectedDown && cia.IsSerialInputPending;
            cia.ReadByte(0x00BFED00);
            // The keyboard waits for KDAT acknowledgement after the CIA ICR read.
            cia.WriteByte(0x00BFEE00, 0x40);
            cia.WriteByte(0x00BFEE00, 0);

            input.KeyboardKeyUp(0x20);
            for (int index = 0; index < 400; index++)
            {
                input.TickEClock();
                cia.Tick();
            }

            byte expectedUp = AmigaInputDevice.EncodeKeyboardSerialByte(0x20, true);
            return downOk && cia.SerialData == expectedUp && cia.IsSerialInputPending;
        }

        /// <summary>
        /// Advances CIA serial shifting for tests that do not run the full machine E-clock loop.
        /// </summary>
        private static void TickCiaSerial(Cia8520 cia, int ticks)
        {
            for (int index = 0; index < ticks; index++)
            {
                cia.Tick();
            }
        }

        /// <summary>
        /// Runs Test Joystick Directions.
        /// </summary>
        private static bool TestJoystickDirections()
        {
            ushort right = AmigaInputDevice.EncodeJoystickDirections(AmigaJoystickButtons.Right);
            ushort left = AmigaInputDevice.EncodeJoystickDirections(AmigaJoystickButtons.Left);
            ushort down = AmigaInputDevice.EncodeJoystickDirections(AmigaJoystickButtons.Down);
            ushort up = AmigaInputDevice.EncodeJoystickDirections(AmigaJoystickButtons.Up);
            return (right & 0x0002) != 0 &&
                (left & 0x0200) != 0 &&
                ((down & 0x0003) == 0x0001) &&
                ((up & 0x0300) == 0x0100);
        }

        /// <summary>
        /// Runs Test Mouse And Buttons.
        /// </summary>
        private static bool TestMouseAndButtons()
        {
            var system = new AmigaSystem(AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone());
            system.Input.SetPortModes(AmigaInputPortMode.Mouse, AmigaInputPortMode.Joystick);
            system.Input.MoveMouse(AmigaControlPort.Port0, 3, -2);
            system.Input.SetMouseButton(AmigaControlPort.Port0, AmigaJoystickButtons.Fire, true);
            system.Input.SetMouseButton(AmigaControlPort.Port0, AmigaJoystickButtons.Second, true);
            system.Input.SetJoystickButtons(AmigaControlPort.Port1, AmigaJoystickButtons.Right | AmigaJoystickButtons.Fire);

            ushort joy0 = system.Bus.ReadWord(0x00DFF00A);
            ushort joy1 = system.Bus.ReadWord(0x00DFF00C);
            byte ciaA = system.Bus.ReadByte(0x00BFE001);
            ushort potinp = system.Bus.ReadWord(0x00DFF016);
            return joy0 == 0xFE03 &&
                (joy1 & 0x0002) != 0 &&
                (ciaA & 0xC0) == 0x00 &&
                (potinp & 0x0400) == 0;
        }

        /// <summary>
        /// Verifies the live potentiometer counters driven by POTGO start.
        /// </summary>
        private static bool TestPotCounters()
        {
            var system = new AmigaSystem(AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone());
            system.Bus.WriteWord(0x00DFF034, 0x0001);
            ushort initial = system.Bus.ReadWord(0x00DFF012);
            system.RunCycles(AmigaModel.PalA500.ChipCyclesPerLine * 2);
            ushort afterTwoLines = system.Bus.ReadWord(0x00DFF012);
            system.Bus.WriteWord(0x00DFF034, 0x0001);
            ushort reset = system.Bus.ReadWord(0x00DFF012);
            return initial == 0x0000 &&
                afterTwoLines == 0x0202 &&
                reset == 0x0000;
        }
    }
}
