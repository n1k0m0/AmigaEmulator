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
    /// Provides Amiga Input Self Test behavior used by the emulator.
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
            log.AppendLine(failures == 0 ? "Input self-test OK" : "Input self-test FAILED: " + failures);
            return failures;
        }

        /// <summary>
        /// Runs the Case routine.
        /// </summary>
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
        /// Executes the Test Keyboard Serial logic.
        /// </summary>
        private static bool TestKeyboardSerial()
        {
            var cia = new Cia8520("CIA-A");
            var input = new AmigaInputDevice();
            input.AttachCiaA(cia);
            input.KeyboardKeyDown(0x20);
            input.TickEClock();
            TickCiaSerial(cia, 8);

            byte expectedDown = AmigaInputDevice.EncodeKeyboardSerialByte(0x20, false);
            bool downOk = cia.SerialData == expectedDown && cia.IsSerialInputPending;
            cia.ReadByte(0x00BFED00);

            input.KeyboardKeyUp(0x20);
            for (int index = 0; index < 24; index++)
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
        /// Executes the Test Joystick Directions logic.
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
        /// Executes the Test Mouse And Buttons logic.
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
    }
}
