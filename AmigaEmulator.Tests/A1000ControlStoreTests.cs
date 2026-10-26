// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Cpu;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.Input;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Tests
{
    internal static class A1000ControlStoreTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("bootstrap vectors, WCS load and write-latch handoff", TestHandoff);
            yield return Test("RESET retains WCS while power loss requires a new load", TestReset);
            yield return Test("restore retains the latch and rejects mismatched or truncated state atomically", TestState);
            yield return Test("68000 bootstrap loads WCS, switches CIA overlay and hands off through direct and deferred execution", TestGuestBoot);
            yield return Test("CPU RESET is privileged, ordered after its fetch and survives suspended restore", TestCpuReset);
            yield return Test("machine restore guards decoder presence and bootstrap identity before changing live state", TestMachineState);
            yield return Test("A1000 has a distinct gated identity and cannot bypass bootstrap or overwrite a frontend save", TestDevelopmentBoard);
            yield return Test("original PAL selects mirrored CIA lanes independently and mirrors custom registers", TestPalDecode);
            yield return Test("deferred reads at CIA aliases preserve interrupts raised after sampling", TestAliasedInterruptRead);
            yield return Test("KCLK reset detector rejects serial pulses and stretches a held keyboard reset", TestKeyboardCircuit);
            yield return Test("KCLK holds the CPU, retains WCS and resumes bootstrap after normal/profiled ticks and restore", TestKeyboardBoard);
            yield return Test("KCLK state rejects corruption before RAM mutation and imports legacy v19 neutrally", TestKeyboardState);
            yield return Test("warm reset cannot refetch bootstrap vectors while an external KCLK reset is held", TestHeldWarmReset);
            yield return Test("external keyboard chord emits a minimum half-second KCLK pulse and survives motherboard reset", TestKeyboardTransmitter);
            yield return Test("keyboard restarts synchronize and resend held physical keys without optional warning codes", TestKeyboardPowerStream);
            yield return Test("v22 transmitter state validates before RAM and v21 detector state imports without a synthetic reset", TestKeyboardTransmitterState);
        }

        private static ChipTestCase Test(string name, Action run) => new ChipTestCase("Models/A1000 WCS", name,
            "A1000 WCS hardware: F80000 bootstrap/latch, FC0000 256 KiB RAM; independent MAME/WinUAE decode references", run);

        private static byte[] Bootstrap()
        {
            var bytes = new byte[8192];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 17 + (i >> 8));
            return bytes;
        }

        private static AmigaSystem Board(byte[] boot, int chipBytes = 512 * 1024)
        {
            var model = AmigaModel.CreateDevelopmentA1000Pal(chipBytes);
            var board = new AmigaSystem(model);
            board.LoadDevelopmentA1000Bootstrap(boot);
            return board;
        }

        private static byte[] GuestBootstrap()
        {
            byte[] boot = new byte[8192];
            void Long(int offset, uint value)
            {
                boot[offset] = (byte)(value >> 24); boot[offset + 1] = (byte)(value >> 16);
                boot[offset + 2] = (byte)(value >> 8); boot[offset + 3] = (byte)value;
            }
            Long(0, 0x20000); Long(4, 0xF80040);
            int pc = 0x40;
            void Word(ushort value) { boot[pc++] = (byte)(value >> 8); boot[pc++] = (byte)value; }
            void EmitLong(uint value) { Word((ushort)(value >> 16)); Word((ushort)value); }
            void MoveLong(uint value, uint address) { Word(0x23FC); EmitLong(value); EmitLong(address); }
            void MoveByte(byte value, uint address) { Word(0x13FC); Word(value); EmitLong(address); }
            // Guest instructions load reset vectors and a tiny Kickstart stand-in.
            MoveLong(0x20000, 0xFC0000); MoveLong(0xFC0100, 0xFC0004);
            MoveLong(0x702A23C0, 0xFC0100); // MOVEQ #42,D0; MOVE.L D0,$2000
            MoveLong(0x00002000, 0xFC0104); MoveLong(0x4E722700, 0xFC0108); // STOP #$2700
            // Execute the latch-closing write from Chip RAM, outside the changing ROM aperture.
            MoveByte(3, 0xBFE201); MoveByte(0, 0xBFE001);
            MoveLong(0x13FC0000, 0x1000); MoveLong(0x00F80000, 0x1004);
            MoveLong(0x4EF900FC, 0x1008); Word(0x33FC); Word(0x0100); EmitLong(0x100C);
            Word(0x4EF9); EmitLong(0x1000);
            return boot;
        }

        private static void TestGuestBoot()
        {
            foreach (bool deferred in new[] { false, true })
            foreach (int chipBytes in new[] { 256 * 1024, 512 * 1024 })
            {
                var board = Board(GuestBootstrap(), chipBytes);
                Check.Equal(0xF80040u, board.Cpu.PC, "CPU fetches actual bootstrap reset vectors through the bus.");
                Check.Equal(0x20000u, board.Cpu.GetA(7), "Bootstrap supplies the initial stack.");
                if (deferred) board.RunChipCycles(5000);
                else for (int i = 0; i < 32 && board.Cpu.Snapshot.ExecutionState != M68kExecutionState.Stopped; i++) board.Cpu.Step();
                Check.Equal(42u, board.Bus.ReadLong(0x2000), "Guest reached WCS code and stored its result in physical Chip RAM.");
                Check.True(!board.Bus.RomOverlayAtZero && board.Bus.DevelopmentA1000ControlStore.WriteProtected,
                    "Guest CIA writes release low RAM, and its high-window write protects WCS.");
                Check.Equal(0x702A23C0u, board.Bus.PeekLong(0xF80100), "Fast peek reads the loaded lower WCS mirror.");
                Check.Equal(board.Bus.ReadLong(0xFC0100), board.Bus.ReadLong(0xF80100), "Both read paths agree after handoff.");
                board.Bus.WriteLong(0xFC0100, 0);
                Check.Equal(0x702A23C0u, board.Bus.PeekLong(0xFC0100), "CPU bus writes cannot alter protected code.");
                board.Reset(false);
                Check.True(board.Bus.RomOverlayAtZero && board.Bus.DevelopmentA1000ControlStore.BootstrapVisible, "Warm reset restores the boot decoder and CIA overlay.");
                Check.Equal(0xF80040u, board.Cpu.PC, "Warm reset fetches bootstrap vectors again.");
                Check.Equal(0x702A23C0u, board.Bus.ReadLong(0xFC0100), "Warm reset retains WCS.");
                board.Reset(true);
                Check.Equal(0u, board.Bus.ReadLong(0xFC0100), "Power cycle loses the WCS payload.");
            }
        }

        private static byte[] SaveBoard(AmigaSystem board)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            board.SaveState(writer); return stream.ToArray();
        }

        private static void LoadBoard(AmigaSystem board, byte[] state)
        {
            using var reader = new BinaryReader(new MemoryStream(state)); board.LoadState(reader);
        }

        private static string ResetDomain(AmigaSystem board)
        {
            // The generic core payload also contains a host-running RTC object
            // even on this board without a wired RTC. Compare the complete reset
            // domains explicitly, not that unrelated wall-clock divider.
            using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
            writer.Write(SaveBoard(board).AsSpan(0, 87)); // v22 header + analogue detector, transmitter and raw pin
            board.Cpu.SaveState(writer); board.Bus.DevelopmentA1000ControlStore.SaveState(writer);
            board.CiaA.SaveState(writer); board.CiaB.SaveState(writer); board.Input.SaveState(writer);
            writer.Write(board.Bus.RomOverlayAtZero); writer.Write(board.GetBeamDebugInfo());
            for (int i = 0; i < board.Model.ChipRamBytes; i++) writer.Write(board.Bus.ChipRam.Read((uint)i));
            foreach (uint pixel in board.PresentedFrameBuffer.Pixels) writer.Write(pixel);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray()));
        }

        private static byte[] SaveCircuit(A1000KeyboardReset circuit)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            circuit.SaveState(writer); return stream.ToArray();
        }

        private static void TestKeyboardCircuit()
        {
            var circuit = new A1000KeyboardReset(709379);
            void Ticks(double seconds)
            {
                for (int i = 0; i < (int)Math.Ceiling(seconds * 709379); i++) circuit.TickEClock();
            }
            // Ordinary serial clocks are tens of microseconds, not reset pulses.
            for (int j = 0; j < 1000; j++)
            {
                circuit.DriveClock(false);
                for (int i = 0; i < 43; i++) circuit.TickEClock();
                circuit.DriveClock(true); circuit.TickEClock();
            }
            Check.True(!circuit.Asserted, "Normal serial clocks cannot accumulate a reset.");
            circuit.DriveClock(false); Ticks(.151);
            Check.True(!circuit.Asserted, "151 ms is shorter than nominal 22k/10uF detection.");
            Ticks(.003); Check.True(circuit.Asserted, "A sustained KCLK low is detected by 154 ms.");
            Ticks(1.0); Check.True(circuit.Asserted, "A long-held clock keeps reset asserted indefinitely.");
            byte[] held = SaveCircuit(circuit);
            var restored = new A1000KeyboardReset(709379);
            using (var reader = new BinaryReader(new MemoryStream(held))) restored.LoadState(reader);
            circuit.DriveClock(true); restored.DriveClock(true);
            for (int i = 0; i < (int)Math.Ceiling(.7 * 709379); i++) { circuit.TickEClock(); restored.TickEClock(); }
            Check.True(circuit.Asserted && restored.Asserted, "700 ms does not complete the nominal 100k/10uF reset stretch.");
            for (int i = 0; i < (int)Math.Ceiling(.01 * 709379); i++) { circuit.TickEClock(); restored.TickEClock(); }
            Check.True(!circuit.Asserted && !restored.Asserted, "The stretched reset is released by 710 ms.");
            Check.Equal(Convert.ToHexString(SaveCircuit(circuit)), Convert.ToHexString(SaveCircuit(restored)), "Restored analogue phases evolve identically.");
            circuit.PowerCycle(); circuit.DriveClock(false); Ticks(.1); circuit.DriveClock(true); Ticks(.2);
            Check.True(!circuit.Asserted, "A 100 ms low pulse never asserts reset after release.");
        }

        private static void TestKeyboardBoard()
        {
            foreach (bool profiled in new[] { false, true })
            {
                using var board = Board(GuestBootstrap());
                board.Bus.WriteLong(0xFC1000, 0x2468ACE0); // Loader never touches this WCS marker.
                board.RunChipCycles(5000);
                board.Bus.WriteLong(0x2000, 0); board.Bus.WriteLong(0x3000, 0x13579BDF);
                int Clocks(double seconds) => (int)Math.Ceiling(board.Model.ChipMasterClockHz * seconds / 2);
                board.DriveDevelopmentA1000KeyboardClock(false);
                board.RunChipCycles(Clocks(.151));
                Check.True(!board.DevelopmentA1000KeyboardResetAsserted && !board.Bus.DevelopmentA1000ControlStore.BootstrapVisible,
                    "A short clock low does not reset the running WCS.");
                board.RunChipCycles(Clocks(.003));
                Check.True(board.DevelopmentA1000KeyboardResetAsserted && board.Bus.RomOverlayAtZero && board.Bus.DevelopmentA1000ControlStore.BootstrapVisible,
                    "Reset assertion reopens bootstrap/WCS and CIA overlay without a power cycle.");
                byte[] Cpu() { using var m = new MemoryStream(); using var w = new BinaryWriter(m); board.Cpu.SaveState(w); return m.ToArray(); }
                byte[] before = Cpu();
                if (profiled) board.ProfileChipCycles(Clocks(.346)); else board.RunChipCycles(Clocks(.346));
                Check.Equal(Convert.ToHexString(before), Convert.ToHexString(Cpu()), "No instruction, bus retry or CPU cycle retires while external reset is held.");
                Check.Equal(0u, board.Bus.ReadLong(0x82000), "Read physical Chip RAM above the reset ROM overlay: bootstrap has not rerun while held.");
                Check.Equal(0x2468ACE0u, board.Bus.ReadLong(0xFC1000), "Held reset retains a WCS marker that the synthetic loader cannot recreate.");
                byte[] state = SaveBoard(board);
                using var replay = Board(GuestBootstrap()); LoadBoard(replay, state);
                Check.True(replay.DevelopmentA1000KeyboardResetAsserted, "A v21 restore preserves the held reset pin.");
                board.DriveDevelopmentA1000KeyboardClock(true); replay.DriveDevelopmentA1000KeyboardClock(true);
                board.RunChipCycles(Clocks(.7)); replay.RunChipCycles(Clocks(.7));
                Check.True(board.DevelopmentA1000KeyboardResetAsserted && replay.DevelopmentA1000KeyboardResetAsserted, "The CPU remains held through the stretcher delay.");
                board.RunChipCycles(Clocks(.011)); replay.RunChipCycles(Clocks(.011));
                Check.True(!board.DevelopmentA1000KeyboardResetAsserted && !replay.DevelopmentA1000KeyboardResetAsserted, "Release allows both CPUs to restart.");
                Check.Equal(42u, board.Bus.ReadLong(0x2000), "The real CPU refetches bootstrap vectors and executes the synthetic WCS handoff again.");
                Check.Equal(0x13579BDFu, board.Bus.ReadLong(0x3000), "Keyboard reset retains physical Chip RAM.");
                Check.Equal(0x702A23C0u, board.Bus.ReadLong(0xFC0100), "Keyboard reset retains the WCS payload.");
                Check.Equal(0x2468ACE0u, board.Bus.ReadLong(0xFC1000), "The independent WCS marker survives release and resumed guest execution.");
                Check.Equal(ResetDomain(board), ResetDomain(replay), "CPU, physical RAM, WCS/decoder, detector, CIAs, input, beam and framebuffer follow the same restored trajectory.");
            }
            using var ordinary = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A500, AmigaRamProfile.Chip512K));
            bool rejected = false;
            try { ordinary.DriveDevelopmentA1000KeyboardClock(false); } catch (InvalidOperationException) { rejected = true; }
            Check.True(rejected, "The diagnostic pin cannot alter an ordinary selectable machine.");
        }

        private static void TestHeldWarmReset()
        {
            using var board = Board(GuestBootstrap()); board.RunChipCycles(5000);
            board.DriveDevelopmentA1000KeyboardClock(false); board.RunChipCycles(600000);
            Check.True(board.DevelopmentA1000KeyboardResetAsserted, "Fixture enters held external reset.");
            byte[] Cpu() { using var m = new MemoryStream(); using var w = new BinaryWriter(m); board.Cpu.SaveState(w); return m.ToArray(); }
            string before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Cpu()));
            board.Reset(false);
            Check.Equal(before, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Cpu())),
                "Warm reset must not fetch vectors or replace suspended CPU state while physical reset is held.");
            board.RunChipCycles(10000);
            Check.Equal(before, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Cpu())),
                "The held clock still blocks CPU work after the host reset request.");
            board.DriveDevelopmentA1000KeyboardClock(true); board.RunChipCycles(2600000);
            Check.Equal(42u, board.Bus.ReadLong(0x2000), "Only the eventual physical release refetches bootstrap and resumes the WCS guest.");
        }

        private static void TestKeyboardTransmitter()
        {
            var tx = new A1000KeyboardHardReset(1000);
            void Tick(int n, bool pending = false) { for (int i = 0; i < n; i++) tx.TickEClock(pending); }
            tx.KeyChanged(0x63, true); tx.KeyChanged(0x66, true); Tick(600);
            Check.True(!tx.ClockLow, "Two modifiers cannot generate reset.");
            tx.KeyChanged(0x67, true); Tick(3, true);
            Check.True(!tx.ClockLow, "An existing serial byte finishes before KCLK is held.");
            tx.KeyChanged(0x67, false);
            Tick(1); Check.True(tx.ClockLow, "A recognized chord remains latched across its last serial edges even after a quick release.");
            Tick(499);
            Check.True(tx.ClockLow, "Releasing a key cannot shorten the minimum pulse.");
            Check.True(tx.TickEClock(false) && !tx.ClockLow, "The 500-ms boundary releases and restarts the keyboard exactly once.");
            Check.True(!tx.TickEClock(false), "Idle clocks do not repeat keyboard startup.");
            tx.KeyChanged(0x67, true); Tick(1000);
            Check.True(tx.ClockLow, "Holding the chord extends the pulse beyond its minimum.");
            tx.KeyChanged(0x63, false); Check.True(tx.TickEClock(false), "One physical release ends the extended pulse.");

            foreach (bool profiled in new[] { false, true })
            {
                using var board = Board(GuestBootstrap());
                board.Bus.WriteLong(0xFC1000, 0x13579BDF); // Before the guest protects WCS.
                board.RunChipCycles(5000);
                void Run(int cycles) { if (profiled) board.ProfileChipCycles(cycles); else board.RunChipCycles(cycles); }
                board.Input.KeyboardKeyDown(0x63); board.Input.KeyboardKeyDown(0x66); board.Input.KeyboardKeyDown(0x67);
                Run(600000);
                Check.True(board.DevelopmentA1000KeyboardResetAsserted, "Raw key input reaches the analogue detector without a host Reset call.");
                byte[] held = SaveBoard(board); using var replay = Board(GuestBootstrap()); LoadBoard(replay, held);
                board.Reset(false); Run(1600000); replay.RunChipCycles(1600000);
                Check.True(board.DevelopmentA1000KeyboardResetAsserted && replay.DevelopmentA1000KeyboardResetAsserted,
                    "Motherboard reset and restored held keys cannot clear the external chord.");
                board.Input.ReleaseAll(); replay.Input.ReleaseAll(); Run(1000000); replay.RunChipCycles(1000000);
                Check.True(board.DevelopmentA1000KeyboardResetAsserted, "Pin release still honors the analogue stretcher.");
                Run(1600000); replay.RunChipCycles(1600000);
                Check.True(!board.DevelopmentA1000KeyboardResetAsserted && !replay.DevelopmentA1000KeyboardResetAsserted,
                    "The pin and stretcher release permits guest bootstrap execution.");
                Check.Equal(42u, board.Bus.ReadLong(0x2000), "The actual 68000 completes its synthetic WCS handoff.");
                Check.Equal(0x13579BDFu, board.Bus.ReadLong(0xFC1000), "Keyboard chord never power-clears WCS.");
                Check.Equal(ResetDomain(board), ResetDomain(replay), "Restored transmitter and normal/profiled pin paths agree after release.");
            }
        }

        private static void TestKeyboardPowerStream()
        {
            // Use the real board integration to exercise preserved physical keys;
            // standalone input's public reset remains a keyboard power cycle.
            using var board = Board(GuestBootstrap()); board.RunChipCycles(5000);
            board.Input.KeyboardKeyDown(0x20); board.Input.KeyboardKeyDown(0x63);
            board.Input.KeyboardKeyDown(0x66); board.Input.KeyboardKeyDown(0x67);
            board.RunChipCycles(600000); board.Input.KeyboardKeyUp(0x67);
            board.RunChipCycles(3900000);
            Check.True(!board.DevelopmentA1000KeyboardResetAsserted, "Quick key release still waits through 500 ms plus motherboard stretch.");
            void Ack() { board.CiaA.ReadByte(0x0D00); board.CiaA.WriteByte(0x0E00, 0x40); board.CiaA.WriteByte(0x0E00, 0); }
            void Send() { board.RunChipCycles(2200); }
            // The synthetic bootstrap does not acknowledge keys. Complete its
            // recovery handshake manually, then inspect actual CIA serial bytes.
            Ack(); Send();
            foreach (byte code in new byte[] { 0xFD, 0x20, 0x63, 0x66, 0xFE })
            {
                Check.Equal(AmigaInputDevice.EncodeKeyboardDecodedByte(code), board.CiaA.SerialData,
                    "Restart sends FD, all still-held keys and FE, without optional 78 warning or F9 lost-sync.");
                Ack(); Send();
            }
            Check.Equal(0, board.Input.QueuedKeyboardEvents, "The restarted external keyboard stream drains.");
            using var ordinary = new AmigaSystem(); ordinary.Input.KeyboardKeyDown(0x63);
            ordinary.Input.KeyboardKeyDown(0x66); ordinary.Input.KeyboardKeyDown(0x67);
            ordinary.RunChipCycles(600000);
            Check.True(!ordinary.DevelopmentA1000KeyboardResetAsserted, "Normal models have no development keyboard personality.");
        }

        private static void TestKeyboardTransmitterState()
        {
            using var board = Board(GuestBootstrap()); board.RunChipCycles(5000);
            byte[] source = SaveBoard(board); string before = ResetDomain(board);
            const int offset = 71;
            foreach (int i in new[] { 0, 4, 8, 9, 10, 11, 15 })
            {
                byte[] bad = (byte[])source.Clone(); bad[offset + i] = 0xFF;
                bool rejected = false; try { LoadBoard(board, bad); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "Invalid transmitter version/clock/mask/flags/timer/raw-pin rejects before bus restore.");
                Check.Equal(before, ResetDomain(board), "Bad transmitter state leaves the live board untouched.");
            }
            bool truncated = false;
            try { LoadBoard(board, source.AsSpan(0, 86).ToArray()); } catch (EndOfStreamException) { truncated = true; }
            Check.True(truncated && before == ResetDomain(board), "Truncation cannot partially restore a keyboard or RAM.");
            byte[] old = new byte[source.Length - 16]; Array.Copy(source, old, 71);
            BitConverter.GetBytes(21).CopyTo(old, 0); Array.Copy(source, 87, old, 71, source.Length - 87);
            board.Input.KeyboardKeyDown(0x63); board.Input.KeyboardKeyDown(0x66); board.Input.KeyboardKeyDown(0x67);
            board.RunChipCycles(600000); LoadBoard(board, old); board.RunChipCycles(5000);
            Check.True(!board.DevelopmentA1000KeyboardResetAsserted, "v21 snapshots import idle external transmitter without inventing a chord edge.");
            Check.Equal(42u, board.Bus.ReadLong(0x2000), "Legacy CPU/RAM handoff remains intact.");
            board.Input.KeyboardKeyDown(0x63); board.Input.KeyboardKeyDown(0x66); board.Input.KeyboardKeyDown(0x67);
            byte[] held = SaveBoard(board), heldV21 = new byte[held.Length - 16];
            Array.Copy(held, heldV21, 71); BitConverter.GetBytes(21).CopyTo(heldV21, 0);
            Array.Copy(held, 87, heldV21, 71, held.Length - 87); LoadBoard(board, heldV21);
            board.Input.KeyboardKeyDown(0x20); board.RunChipCycles(600000);
            Check.True(!board.DevelopmentA1000KeyboardResetAsserted,
                "An unrelated key cannot invent a reset chord edge from modifiers held in a v21 snapshot.");
            board.Input.KeyboardKeyUp(0x67); board.Input.KeyboardKeyDown(0x67); board.RunChipCycles(600000);
            Check.True(board.DevelopmentA1000KeyboardResetAsserted, "A new physical chord edge after legacy import still resets normally.");
        }

        private static void TestKeyboardState()
        {
            using var board = Board(GuestBootstrap()); board.RunChipCycles(5000);
            byte[] source = SaveBoard(board); string before = ResetDomain(board);
            // The v22 prefix contains a 26-byte detector, 15-byte transmitter
            // and one raw external pin byte before the unchanged v19 bus payload.
            const int header = 45, circuitBytes = 42;
            Check.Equal(22, BitConverter.ToInt32(source), "Only the development A1000 uses core v22.");
            foreach (int offset in new[] { 0, 4, 8, 9, 10, 18 })
            {
                byte[] bad = (byte[])source.Clone();
                if (offset == 10 || offset == 18) BitConverter.GetBytes(double.NaN).CopyTo(bad, header + offset);
                else bad[header + offset] = 0xFF;
                bool rejected = false; try { LoadBoard(board, bad); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "A corrupt timing version/frequency/flag/voltage is rejected.");
                Check.Equal(before, ResetDomain(board), "Circuit rejection precedes any live RAM, CPU, decoder, beam or pin mutation.");
            }
            bool truncated = false;
            try { LoadBoard(board, source.AsSpan(0, header + 26 - 1).ToArray()); }
            catch (EndOfStreamException) { truncated = true; }
            Check.True(truncated, "A truncated analogue payload is rejected before touching the bus.");
            Check.Equal(before, ResetDomain(board), "A truncated pin state cannot partially alter the live machine.");
            byte[] legacy = new byte[source.Length - circuitBytes];
            Array.Copy(source, 0, legacy, 0, header); BitConverter.GetBytes(19).CopyTo(legacy, 0);
            Array.Copy(source, header + circuitBytes, legacy, header, source.Length - header - circuitBytes);
            board.DriveDevelopmentA1000KeyboardClock(false); board.RunChipCycles(600000);
            Check.True(board.DevelopmentA1000KeyboardResetAsserted, "Fixture enters held reset before importing the older snapshot.");
            LoadBoard(board, legacy);
            Check.True(!board.DevelopmentA1000KeyboardResetAsserted, "Legacy v19 snapshots have a neutral, released external pin.");
            Check.Equal(42u, board.Bus.ReadLong(0x2000), "Legacy memory is restored without inventing a reset or rewriting guest data.");
        }

        private static void TestCpuReset()
        {
            var board = Board(GuestBootstrap()); var bus = board.Bus;
            bus.RomOverlayAtZero = false; bus.WriteByte(0xF80000, 0);
            bus.WriteWord(0x1000, 0x4E70); bus.WriteWord(0x1002, 0x7005);
            bus.WriteWord(0x1004, 0x4E72); bus.WriteWord(0x1006, 0x2700);
            // Run above the overlay window through the physical Chip RAM mirror.
            board.Cpu.SetProgramCounter(0x81000); board.Cpu.SetD(1, 0x1234);
            board.Cpu.Tick(false);
            Check.True(bus.DevelopmentA1000ControlStore.WriteProtected, "Planning RESET cannot change the decoder before a stalled opcode fetch.");
            byte[] pending = SaveBoard(board);
            var restored = Board(GuestBootstrap()); LoadBoard(restored, pending);
            foreach (var target in new[] { board, restored })
            {
                target.Cpu.Tick(true); // Retire the contended opcode fetch.
                Check.True(target.Bus.DevelopmentA1000ControlStore.WriteProtected, "RESET signal remains ordered after its fetch.");
                target.Cpu.Tick(false); // RESET itself is not an Agnus memory transfer.
                Check.True(target.Bus.DevelopmentA1000ControlStore.BootstrapVisible && target.Bus.RomOverlayAtZero, "CPU RESET reopens WCS and resets CIA overlay.");
                Check.Equal(0x81002u, target.Cpu.PC, "RESET does not fetch vectors or restart the CPU.");
                Check.Equal(0x1234u, target.Cpu.GetD(1), "RESET retains CPU registers.");
                for (int i = 0; i < 100; i++) target.Cpu.Tick(true);
                Check.Equal(5u, target.Cpu.D0, "Execution continues after the RESET opcode.");
            }
            var user = Board(GuestBootstrap()); user.Bus.RomOverlayAtZero = false;
            user.Bus.WriteByte(0xF80000, 0); user.Bus.WriteLong(8 * 4, 0x3000);
            user.Bus.WriteWord(0x1000, 0x46FC); user.Bus.WriteWord(0x1002, 0);
            user.Bus.WriteWord(0x1004, 0x4E70); user.Cpu.SetProgramCounter(0x1000);
            user.Cpu.Step(); user.Cpu.Step();
            Check.Equal(0x3000u, user.Cpu.PC, "User RESET enters privilege-violation vector 8.");
            Check.True(user.Bus.DevelopmentA1000ControlStore.WriteProtected && !user.Bus.RomOverlayAtZero, "Rejected user RESET does not touch external state.");
        }

        private static void TestMachineState()
        {
            byte[] boot = GuestBootstrap(); var board = Board(boot);
            board.RunChipCycles(5000); byte[] state = SaveBoard(board);
            Check.Equal(22, BitConverter.ToInt32(state), "Development decoder has an explicit machine-state format.");
            var restored = Board(boot); LoadBoard(restored, state);
            Check.Equal(42u, restored.Bus.ReadLong(0x2000), "Machine state retains the guest handoff result.");
            Check.Equal(0x702A23C0u, restored.Bus.PeekLong(0xF80100), "Machine state restores protected WCS code.");
            Check.True(restored.Bus.DevelopmentA1000ControlStore.WriteProtected && !restored.Bus.RomOverlayAtZero, "State retains both independent latches.");
            var plain = new AmigaSystem(); byte[] plainState = SaveBoard(plain);
            Check.Equal(18, BitConverter.ToInt32(plainState), "Ordinary machine states retain their released format.");
            boot[0x700] ^= 1; var otherRom = Board(boot);
            foreach (var pair in new[] { (plain, state), (otherRom, state), (restored, plainState) })
            {
                uint pc = pair.Item1.Cpu.PC; pair.Item1.Bus.ChipRam.Write(0x2345, 0xA7);
                bool rejected = false; try { LoadBoard(pair.Item1, pair.Item2); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "Decoder presence or bootstrap identity mismatch is rejected.");
                Check.Equal(pc, pair.Item1.Cpu.PC, "Rejected restore leaves CPU untouched.");
                Check.Equal((byte)0xA7, pair.Item1.Bus.ChipRam.Read(0x2345), "Rejected restore leaves RAM untouched.");
            }
            var invalid = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A1200, AmigaRamProfile.Chip2Mb));
            bool wrongBoard = false;
            try { invalid.LoadDevelopmentA1000Bootstrap(boot); } catch (InvalidOperationException) { wrongBoard = true; }
            Check.True(wrongBoard && invalid.Bus.DevelopmentA1000ControlStore == null, "Decoder cannot silently attach to an AGA/020 motherboard.");
        }

        private static void TestDevelopmentBoard()
        {
            foreach (int chipBytes in new[] { 256 * 1024, 512 * 1024 })
            {
                using var board = Board(GuestBootstrap(), chipBytes);
                Check.Equal(AmigaMachineModel.A1000, board.Model.Machine, "Core states identify the A1000 independently of A500.");
                Check.Equal(AmigaChipset.Ocs, board.Model.Chipset, "A1000 retains the original custom chips.");
                Check.Equal(AmigaCpuModel.M68000, board.Model.Cpu, "A1000 has the original CPU.");
                Check.Equal(chipBytes, board.Model.ChipRamBytes, "Installed base/expanded Chip RAM is explicit.");
                Check.Equal(0, board.Model.SlowRamBytes + board.Model.FastRamBytes, "No borrowed A500 trapdoor or Zorro RAM.");
                Check.True(!board.Model.HasBuiltInHardDiskController && !board.Model.HasBatteryClock && !board.Model.HasPcmcia,
                    "The development profile does not invent later motherboard devices.");
                byte[] state = SaveBoard(board);
                using var a500 = new AmigaSystem(new AmigaModel("Old WCS test board", 7093790, chipBytes, 0, 227, 312, 640, 256));
                a500.LoadDevelopmentA1000Bootstrap(GuestBootstrap());
                bool mismatch = false; try { LoadBoard(a500, state); } catch (InvalidDataException) { mismatch = true; }
                Check.True(mismatch, "Even a WCS-equipped A500 cannot accept the distinct A1000 board state.");
                LoadBoard(a500, SaveBoard(a500)); // Earlier diagnostic A500/WCS states remain self-compatible.
                bool bypass = false;
                try { board.LoadKickstart(new byte[256 * 1024]); } catch (InvalidOperationException) { bypass = true; }
                Check.True(bypass && board.Bus.DevelopmentA1000ControlStore != null, "Direct ROM loading cannot bypass the A1000 bootstrap/WCS path.");
            }
            Check.True(!AmigaModel.IsSelectableMachine(AmigaMachineModel.A1000), "A1000 has not passed the frontend gate.");
            var seen = new HashSet<AmigaMachineModel>(); var current = AmigaMachineModel.A500;
            for (int i = 0; i < 8; i++) { seen.Add(current); current = AmigaModel.CycleSelectableMachine(current, 1); }
            Check.Equal(8, seen.Count, "All eight released boards remain in the model menu.");
            Check.Equal(AmigaMachineModel.A500, current, "Model cycling skips the development board and wraps.");
            Check.Equal(AmigaMachineModel.A3000, AmigaModel.CycleSelectableMachine(current, -1), "Reverse cycling skips the development board too.");
            bool menu = false; try { AmigaModel.Create(AmigaMachineModel.A1000, AmigaRamProfile.Chip512K); } catch (InvalidOperationException) { menu = true; }
            Check.True(menu, "The normal RAM/profile factory cannot create the unfinished model.");
            using var dev = Board(GuestBootstrap()); var settings = new AmigaEmulatorSettings();
            bool startup = false; try { settings.ApplyStartupModel(dev.Model); } catch (InvalidOperationException) { startup = true; }
            Check.True(startup && settings.MachineModel == AmigaMachineModel.A500, "Startup settings reject the model without changing current preferences.");
            string path = Path.Combine(Path.GetTempPath(), "a1000-save-guard-" + Guid.NewGuid().ToString("N") + ".amigasav");
            byte[] sentinel = { 0x63, 0x45, 0x27, 0x18 };
            try
            {
                File.WriteAllBytes(path, sentinel); bool rejected = false;
                try { SaveStateFile.Write(path, dev, null, AmigaKickstart.Auto, AmigaRamProfile.Chip512K,
                    AmigaAccuracyProfile.Compatible, Array.Empty<uint>(), 0, 0); }
                catch (InvalidOperationException) { rejected = true; }
                Check.True(rejected && Convert.ToHexString(File.ReadAllBytes(path)) == Convert.ToHexString(sentinel),
                    "Frontend save rejection leaves an existing slot byte-for-byte untouched.");
            }
            finally { File.Delete(path); }
        }

        private static void TestPalDecode()
        {
            using var board = Board(GuestBootstrap()); var bus = board.Bus;
            bus.WriteWord(0xA00200, 0xFFFF); // Both DDR-A registers via independent byte lanes.
            bus.WriteWord(0xA00000, 0xA55A);
            Check.Equal((byte)0x5A, bus.PeekByte(0xBFE001), "Low byte writes CIA-A across the PAL mirror.");
            Check.Equal((byte)0xA5, bus.PeekByte(0xBFD000), "High byte independently writes CIA-B.");
            Check.Equal((ushort)0xA55A, bus.PeekWord(0xA00000), "A word with both selects active reaches both chips.");
            Check.True(!bus.RomOverlayAtZero, "Mirrored CIA-A writes control the boot overlay.");
            Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(0xA03000), "Both CIA selects high leave the bus unconnected.");
            bus.WriteByte(0xA03001, 0xFF);
            Check.Equal((byte)0x5A, bus.PeekByte(0xBFE001), "Unselected mirror writes do not reach a CIA.");
            bus.WriteWord(0xC00180, 0x0A5D);
            Check.Equal((ushort)0x0A5D, bus.PeekWord(0xDFF180), "A1000 custom-register writes mirror over C00000-DFFFFF.");
            bus.WriteWord(0xD00180, 0x0456);
            Check.Equal((ushort)0x0456, bus.PeekWord(0xC00180), "Another PAL mirror addresses the same register.");
            Check.True(bus.RequiresChipBus(0xC00180, 2), "Mirrored custom-register accesses still contend for the chip bus.");
            Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(0xE80000), "No absent expansion board responds at Autoconfig.");
            using var a500 = new AmigaSystem(AmigaModel.A500Pal);
            Check.Equal(AmigaAddressRegion.Unmapped, a500.Bus.GetRegion(0xC00180), "A1000 PAL decoding is confined to its own board identity.");
        }

        private static void TestAliasedInterruptRead()
        {
            using var board = Board(GuestBootstrap()); var bus = board.Bus; bus.RomOverlayAtZero = false;
            bus.WriteWord(0x1000, 0x1039); bus.WriteLong(0x1002, 0xA00D01); // MOVE.B mirrored CIA-A ICR,D0
            bus.WriteWord(0x1006, 0x4E72); bus.WriteWord(0x1008, 0x2700);
            board.CiaA.RequestInterrupt(1); board.Cpu.SetProgramCounter(0x1000);
            board.Cpu.Tick(false); // Sample ICR but stall the Chip RAM opcode fetch.
            board.CiaA.RequestInterrupt(2);
            for (int i = 0; i < 100; i++) board.Cpu.Tick(true);
            Check.Equal(1u, board.Cpu.D0 & 0xFF, "Guest receives only the interrupt request sampled during planning.");
            Check.Equal((byte)2, board.CiaA.InterruptRequest, "An IRQ arriving after the sample survives aliased read retirement.");
            board.CiaA.RequestInterrupt(1); board.CiaB.RequestInterrupt(4);
            ushort sample = bus.PeekWord(0xA00D00);
            board.CiaA.RequestInterrupt(8); board.CiaB.RequestInterrupt(16);
            Check.True(bus.IsCiaIcrRead(0xA00D00, 2) && bus.RetireCiaIcrRead(0xA00D00, 2, sample), "Both CIA ICR lanes retire through the board decoder.");
            Check.Equal((byte)8, board.CiaA.InterruptRequest, "CIA-A's later request survives the dual read.");
            Check.Equal((byte)16, board.CiaB.InterruptRequest, "CIA-B's later request survives independently.");
        }

        private static void TestHandoff()
        {
            byte[] boot = Bootstrap();
            var store = new A1000ControlStore(boot);
            boot[0] ^= 0xFF;
            Check.Equal((byte)0, store.ReadByte(0xF80000), "Bootstrap is privately copied and visible at the reset-vector aperture.");
            Check.Equal(store.ReadByte(0xF80007), store.ReadByte(0xF82007), "8 KiB bootstrap mirrors within the lower window.");
            Check.Equal((byte)0, store.ReadByte(0xFC0000), "WCS starts independently of the bootstrap.");
            for (int i = 0; i < A1000ControlStore.RamSize; i++)
                store.WriteByte(0xFC0000u + (uint)i, (byte)(i ^ (i >> 16) ^ 0xA7));
            Check.True(store.BootstrapVisible && !store.WriteProtected, "Loading WCS does not hide the bootstrap.");
            store.WriteByte(0xFBFFFF, 0x55);
            Check.True(store.WriteProtected, "A write at the far end of the bootstrap range latches protection.");
            for (int i = 0; i < A1000ControlStore.RamSize; i++)
            {
                byte expected = (byte)(i ^ (i >> 16) ^ 0xA7);
                Check.Equal(expected, store.ReadByte(0xFC0000u + (uint)i), "Entire WCS retains the loaded data.");
                Check.Equal(expected, store.ReadByte(0xF80000u + (uint)i), "Closed latch replaces bootstrap with a WCS mirror.");
            }
            store.WriteByte(0xF80000, 0x11); store.WriteByte(0xFC0000, 0x22); store.WriteByte(0xFFFFFF, 0x33);
            Check.Equal((byte)0xA7, store.ReadByte(0xFC0000), "Neither alias writes through the closed latch.");
            Check.Equal(unchecked((byte)(0x3FFFF ^ 3 ^ 0xA7)), store.ReadByte(0xFFFFFF), "Last WCS byte is protected too.");
            bool rejected = false;
            try { store.WriteByte(0xF7FFFF, 0); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check.True(rejected, "Decoder does not alias addresses below its aperture.");
            var physical = new byte[65536]; physical[0xD432] = 0x6D;
            Check.Equal((byte)0x6D, new A1000ControlStore(physical).ReadByte(0xF9D432), "A full physical ROM dump keeps its supplied decode contents.");
        }

        private static void TestReset()
        {
            var store = new A1000ControlStore(Bootstrap());
            store.WriteByte(0xFC0004, 0xA5); store.WriteByte(0xF80000, 0);
            store.Reset();
            Check.True(store.BootstrapVisible && !store.WriteProtected, "RESET restores the boot decoder and writable latch.");
            Check.Equal((byte)0xA5, store.ReadByte(0xFC0004), "Warm reset retains the loaded Kickstart bytes.");
            store.WriteByte(0xFC0004, 0x5A); store.WriteByte(0xF80000, 0);
            Check.Equal((byte)0x5A, store.ReadByte(0xF80004), "Warm bootstrap can relock retained or replaced WCS.");
            store.PowerCycle();
            Check.True(store.BootstrapVisible, "Power cycle exposes bootstrap again.");
            Check.Equal((byte)0, store.ReadByte(0xFC0004), "Power cycle loses the volatile Kickstart data.");
        }

        private static byte[] Save(A1000ControlStore store)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            store.SaveState(writer);
            return stream.ToArray();
        }

        private static void TestState()
        {
            byte[] boot = Bootstrap(); var store = new A1000ControlStore(boot);
            store.WriteByte(0xFC1234, 0xC9); store.WriteByte(0xF80000, 0);
            byte[] state = Save(store);
            store.PowerCycle();
            using (var reader = new BinaryReader(new MemoryStream(state))) store.LoadState(reader);
            Check.True(store.WriteProtected, "Restore preserves the locked decoder.");
            Check.Equal((byte)0xC9, store.ReadByte(0xF81234), "Restore preserves data through the lower mirror.");
            foreach (int cut in new[] { 3, 12, state.Length - 1 })
            {
                bool rejected = false;
                try { using var reader = new BinaryReader(new MemoryStream(state, 0, cut)); store.LoadState(reader); }
                catch (EndOfStreamException) { rejected = true; }
                Check.True(rejected, "Truncated state is rejected.");
                Check.True(store.WriteProtected, "Failed restore leaves the live latch unchanged.");
                Check.Equal((byte)0xC9, store.ReadByte(0xFC1234), "Failed restore leaves live WCS unchanged.");
            }
            byte[] malformed = (byte[])state.Clone(); malformed[36] = 2;
            bool badLatch = false;
            try { using var reader = new BinaryReader(new MemoryStream(malformed)); store.LoadState(reader); }
            catch (InvalidDataException) { badLatch = true; }
            Check.True(badLatch && store.WriteProtected, "Invalid latch encoding is rejected without changing memory.");
            boot[0] ^= 1; var different = new A1000ControlStore(boot);
            bool mismatch = false;
            try { using var reader = new BinaryReader(new MemoryStream(state)); different.LoadState(reader); }
            catch (InvalidDataException) { mismatch = true; }
            Check.True(mismatch && different.BootstrapVisible, "Different bootstrap cannot silently resume this state.");
            Check.Equal((byte)0, different.ReadByte(0xFC1234), "ROM mismatch leaves the target memory untouched.");
            store.Reset(); byte[] writable = Save(store); store.WriteByte(0xF80000, 0);
            using (var reader = new BinaryReader(new MemoryStream(writable))) store.LoadState(reader);
            Check.True(store.BootstrapVisible, "Restore also retains a still-writable bootstrap phase.");
        }
    }
}
