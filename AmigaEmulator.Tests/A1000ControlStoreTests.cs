// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Cpu;
using AmigaEmulator.Core.Memory;

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
            var model = new AmigaModel("A1000 decoder test board", 7093790, chipBytes, 0, 227, 312, 640, 256);
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
            Check.Equal(19, BitConverter.ToInt32(state), "Development decoder has an explicit machine-state format.");
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
