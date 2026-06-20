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
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Core.Cia
{
    /// <summary>
    /// Provides CIA Self Test functionality.
    /// </summary>
    public static class CiaSelfTest
    {
        /// <summary>
        /// Runs the value routine.
        /// </summary>
        public static int Run(StringBuilder log)
        {
            int failures = 0;
            failures += RunCase(log, "timer A interrupts when counting down to zero", TestTimerAUnderflow);
            failures += RunCase(log, "timer high write does not reload a running counter", TestTimerHighWriteDoesNotReloadRunningCounter);
            failures += RunCase(log, "timer B respects control register count source", TestTimerBCountSource);
            failures += RunCase(log, "8520 one-shot timer starts on latch high write", TestOneShotStartsOnLatchHighWrite);
            failures += RunCase(log, "system E-clock drives CIA timers at chip-cycle divider", TestSystemEClockDivider);
            failures += RunCase(log, "Amiga bus maps CIA byte lanes correctly", TestAmigaBusCiaByteLanes);
            failures += RunCase(log, "TOD reads latch on MSB until LSB read", TestTodReadLatch);
            failures += RunCase(log, "TOD alarm writes through CRB bit 7 and interrupts on match", TestTodAlarmInterrupt);
            failures += RunCase(log, "TOD clock writes do not trigger alarm on partial value", TestTodClockWriteDoesNotAlarmOnPartialValue);
            failures += RunCase(log, "CIA interrupt level is reasserted until ICR read", TestCiaInterruptLevel);
            failures += RunCase(log, "CIAB FLAG interrupt is raised by external pulse", TestFlagInterrupt);
            log.AppendLine(failures == 0 ? "CIA self-test OK" : "CIA self-test FAILED: " + failures);
            return failures;
        }

        /// <summary>
        /// Runs the Case routine.
        /// </summary>
        private static int RunCase(StringBuilder log, string name, System.Func<bool> test)
        {
            bool ok = false;
            try
            {
                ok = test();
            }
            catch (System.Exception ex)
            {
                log.AppendLine("FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message);
                return 1;
            }

            log.AppendLine((ok ? "OK   " : "FAIL ") + name);
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Runs Test Timer A Underflow.
        /// </summary>
        private static bool TestTimerAUnderflow()
        {
            var cia = new Cia8520("CIA-A");
            cia.WriteByte(0x0400, 0x01);
            cia.WriteByte(0x0500, 0x00);
            cia.WriteByte(0x0D00, 0x81);
            cia.WriteByte(0x0E00, 0x01);

            cia.Tick();
            return cia.InterruptPending && cia.TimerA == 1;
        }

        /// <summary>
        /// Runs Test One Shot Starts On Latch High Write.
        /// </summary>
        private static bool TestOneShotStartsOnLatchHighWrite()
        {
            var cia = new Cia8520("CIA-A");
            cia.WriteByte(0x0400, 0x01);
            cia.WriteByte(0x0E00, 0x08);
            cia.WriteByte(0x0500, 0x00);
            cia.WriteByte(0x0D00, 0x81);
            cia.Tick();
            return cia.InterruptPending && cia.TimerA == 1 && (cia.ReadByte(0x0E00) & 0x01) == 0;
        }

        /// <summary>
        /// Runs Test Timer High Write Does Not Reload Running Counter.
        /// </summary>
        private static bool TestTimerHighWriteDoesNotReloadRunningCounter()
        {
            var cia = new Cia8520("CIA-A");
            cia.WriteByte(0x0400, 0x04);
            cia.WriteByte(0x0500, 0x00);
            cia.WriteByte(0x0E00, 0x01);
            cia.Tick();
            bool counted = cia.TimerA == 3;

            cia.WriteByte(0x0500, 0x01);
            return counted && cia.TimerA == 3 && cia.TimerALatch == 0x0104;
        }

        /// <summary>
        /// Runs Test Timer B Count Source.
        /// </summary>
        private static bool TestTimerBCountSource()
        {
            var cia = new Cia8520("CIA-A");
            cia.WriteByte(0x0600, 0x01);
            cia.WriteByte(0x0700, 0x00);
            cia.WriteByte(0x0D00, 0x83);
            cia.WriteByte(0x0F00, 0x21);
            for (int index = 0; index < 4; index++)
            {
                cia.Tick();
            }

            bool cntModeDidNotUseEClock = !cia.InterruptPending && cia.TimerB == 1;

            cia = new Cia8520("CIA-A");
            cia.WriteByte(0x0400, 0x01);
            cia.WriteByte(0x0500, 0x00);
            cia.WriteByte(0x0600, 0x01);
            cia.WriteByte(0x0700, 0x00);
            cia.WriteByte(0x0D00, 0x83);
            cia.WriteByte(0x0F00, 0x41);
            cia.WriteByte(0x0E00, 0x01);
            cia.Tick();

            return cntModeDidNotUseEClock &&
                (cia.InterruptControl & 0x83) == 0x83 &&
                cia.TimerB == 1;
        }

        /// <summary>
        /// Runs Test System E Clock Divider.
        /// </summary>
        private static bool TestSystemEClockDivider()
        {
            var system = new AmigaSystem(AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone());
            system.CiaA.WriteByte(0x0400, 0x01);
            system.CiaA.WriteByte(0x0500, 0x00);
            system.CiaA.WriteByte(0x0D00, 0x81);
            system.CiaA.WriteByte(0x0E00, 0x01);
            system.RunChipCycles(10);
            return system.CiaA.InterruptPending &&
                (system.Agnus.CustomRegisters.InterruptRequest & (ushort)CustomInterruptSource.Ports) != 0;
        }

        /// <summary>
        /// Runs Test Amiga Bus Cia Byte Lanes.
        /// </summary>
        private static bool TestAmigaBusCiaByteLanes()
        {
            var system = new AmigaSystem(AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone());

            system.Bus.WriteByte(0x00BFE000, 0x00);
            bool ciaAWrongLaneIgnored = system.CiaA.Pra == 0x03 && system.Bus.ReadByte(0x00BFE000) == 0xFF;

            system.Bus.WriteByte(0x00BFE001, 0x02);
            bool ciaAOddLaneWorks = system.CiaA.Pra == 0x02;

            system.Bus.WriteByte(0x00BFD101, 0x44);
            bool ciaBWrongLaneIgnored = system.CiaB.Prb == 0x00 && system.Bus.ReadByte(0x00BFD101) == 0xFF;

            system.Bus.WriteByte(0x00BFD100, 0x55);
            bool ciaBEvenLaneWorks = system.CiaB.Prb == 0x55;

            system.Bus.WriteWord(0x00BFE000, 0xAA66);
            bool ciaAWordWritesOnlyLowLane = system.CiaA.Pra == 0x66;

            system.Bus.WriteWord(0x00BFD000, 0x7700);
            bool ciaBWordWritesOnlyHighLane = system.CiaB.Pra == 0x77;

            return ciaAWrongLaneIgnored &&
                ciaAOddLaneWorks &&
                ciaBWrongLaneIgnored &&
                ciaBEvenLaneWorks &&
                ciaAWordWritesOnlyLowLane &&
                ciaBWordWritesOnlyHighLane;
        }

        /// <summary>
        /// Runs Test Tod Alarm Interrupt.
        /// </summary>
        private static bool TestTodAlarmInterrupt()
        {
            var cia = new Cia8520("CIA-B");
            cia.WriteByte(0x0F00, 0x80);
            cia.WriteByte(0x0800, 0x02);
            cia.WriteByte(0x0900, 0x00);
            cia.WriteByte(0x0A00, 0x00);
            cia.WriteByte(0x0D00, 0x84);

            cia.WriteByte(0x0F00, 0x00);
            bool alarmWriteDidNotSetClock = cia.ReadByte(0x0A00) == 0 && cia.ReadByte(0x0900) == 0 && cia.ReadByte(0x0800) == 0;

            cia.TickTodEvent();
            bool beforeMatch = !cia.InterruptPending;
            cia.TickTodEvent();

            return alarmWriteDidNotSetClock && beforeMatch && cia.InterruptPending && (cia.InterruptControl & 0x84) == 0x84;
        }

        /// <summary>
        /// Runs Test Tod Read Latch.
        /// </summary>
        private static bool TestTodReadLatch()
        {
            var cia = new Cia8520("CIA-B");
            cia.WriteByte(0x0A00, 0x00);
            cia.WriteByte(0x0900, 0xFF);
            cia.WriteByte(0x0800, 0xFF);

            byte high = cia.ReadByte(0x0A00);
            cia.TickTodEvent();
            byte mid = cia.ReadByte(0x0900);
            byte low = cia.ReadByte(0x0800);
            byte highAfterRelease = cia.ReadByte(0x0A00);

            return high == 0x00 && mid == 0xFF && low == 0xFF && highAfterRelease == 0x01;
        }

        /// <summary>
        /// Runs Test Tod Clock Write Does Not Alarm On Partial Value.
        /// </summary>
        private static bool TestTodClockWriteDoesNotAlarmOnPartialValue()
        {
            var cia = new Cia8520("CIA-B");
            cia.WriteByte(0x0A00, 0x00);
            cia.WriteByte(0x0900, 0x6E);
            cia.WriteByte(0x0800, 0x00);
            cia.WriteByte(0x0D00, 0x84);

            cia.WriteByte(0x0A00, 0x00);
            cia.WriteByte(0x0900, 0x00);
            bool noAlarmAfterPartialZero = (cia.InterruptControl & 0x04) == 0 && !cia.InterruptPending;

            cia.WriteByte(0x0800, 0x00);
            bool noAlarmAfterFinalMatchWrite = (cia.InterruptControl & 0x04) == 0 && !cia.InterruptPending;

            return noAlarmAfterPartialZero &&
                noAlarmAfterFinalMatchWrite;
        }

        /// <summary>
        /// Runs Test Cia Interrupt Level.
        /// </summary>
        private static bool TestCiaInterruptLevel()
        {
            var system = new AmigaSystem(AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone());
            system.CiaB.WriteByte(0x0F00, 0x80);
            system.CiaB.WriteByte(0x0800, 0x01);
            system.CiaB.WriteByte(0x0900, 0x00);
            system.CiaB.WriteByte(0x0A00, 0x00);
            system.CiaB.WriteByte(0x0D00, 0x84);

            system.CiaB.WriteByte(0x0F00, 0x00);
            system.CiaB.TickTodEvent();
            system.RunChipCycles(1);
            bool firstAssert = (system.Agnus.CustomRegisters.InterruptRequest & (ushort)CustomInterruptSource.External) != 0;

            system.Agnus.CustomRegisters.WriteWord(0x09C, (ushort)CustomInterruptSource.External);
            bool cleared = (system.Agnus.CustomRegisters.InterruptRequest & (ushort)CustomInterruptSource.External) == 0;
            system.RunChipCycles(1);
            bool reasserted = (system.Agnus.CustomRegisters.InterruptRequest & (ushort)CustomInterruptSource.External) != 0;

            return firstAssert && cleared && reasserted;
        }

        /// <summary>
        /// Runs Test Flag Interrupt.
        /// </summary>
        private static bool TestFlagInterrupt()
        {
            var cia = new Cia8520("CIA-B");
            cia.WriteByte(0x0D00, 0x90);
            cia.PulseFlag();

            return cia.InterruptPending && (cia.InterruptControl & 0x90) == 0x90;
        }
    }
}
