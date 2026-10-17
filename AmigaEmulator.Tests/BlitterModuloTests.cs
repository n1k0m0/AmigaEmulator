// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AmigaEmulator.Core;
using AmigaEmulator.Core.CustomChips;
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Tests
{
    internal static class BlitterModuloTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase("Blitter/Modulo", "ascending and descending DMA ignore the unwired modulo low bit on all four channels",
                "Commodore BLTxMOD word addressing; hardware register cross-check", () => Block(false));
            yield return new ChipTestCase("Blitter/Modulo", "legacy in-flight blits restore even signed modulos before continuing DMA",
                "Old serialized odd modulo latches and partially fetched source words", () => Block(true));
            yield return new ChipTestCase("Blitter/Modulo", "line slope and upward stride ignore modulo bit zero and retain exact restore",
                "BLTAMOD/BLTBMOD slope accumulators and BLTCMOD upward rows", Line);
        }

        private static void Block(bool legacyRestore)
        {
            // Absolute row strides for a one-word blit, including negative
            // modulo and the zero-stride overlap case. Masking a pointer after
            // subtracting an odd modulo incorrectly rounds down by another word.
            var cases = new (ushort raw, int ascendingStride, int descendingStride)[]
                { (1, 2, -2), (3, 4, -4), (0xFFFD, -2, 2), (0xFFFF, 0, 0) };
            foreach (var chipset in new[] { AmigaChipset.Ocs, AmigaChipset.Ecs, AmigaChipset.Aga })
            foreach (bool descending in new[] { false, true })
            foreach (var sample in cases)
            {
                var regs = new CustomRegisterMap(chipset);
                var memory = new Dictionary<uint, ushort>();
                var writes = new List<(uint address, ushort value)>();
                int stride = descending ? sample.descendingStride : sample.ascendingStride;
                var bases = new uint[] { 0x1000, 0x2000, 0x3000, 0x4000 };
                ushort[] values = { 0xA55A, 0x1234, 0x00FF };
                for (int channel = 0; channel < 3; channel++)
                for (int row = 0; row < 3; row++) memory[At(bases[channel], row * stride)] = values[channel];
                var blitter = Create(regs, memory, writes);
                uint[] pointers = { 0x050, 0x04C, 0x048, 0x054 };
                uint[] modulos = { 0x064, 0x062, 0x060, 0x066 };
                for (int channel = 0; channel < 4; channel++)
                {
                    Pointer(regs, pointers[channel], bases[channel]);
                    // Byte-lane programming must reach the same DMA behavior.
                    regs.WriteByte(modulos[channel], (byte)(sample.raw >> 8));
                    regs.WriteByte(modulos[channel] + 1, (byte)sample.raw);
                }
                regs.WriteWord(0x044, 0xFFFF); regs.WriteWord(0x046, 0xFFFF);
                regs.WriteWord(0x040, 0x0F96); // A XOR B XOR C, with all channels enabled.
                regs.WriteWord(0x042, descending ? (ushort)2 : (ushort)0);
                regs.WriteWord(0x058, (3 << 6) | 1); blitter.NotifyCustomRegisterWrite(0x058, 2);
                if (legacyRestore)
                {
                    // Recreate the old version's genuinely odd decoded latches,
                    // not merely a new state labeled as an older format.
                    foreach (string name in new[] { "_amod", "_bmod", "_cmod", "_dmod" })
                        typeof(Blitter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(blitter, (int)(short)sample.raw);
                    for (int i = 0; i < 3; i++) blitter.Tick(true); // Source words are fetched; the first row has not advanced yet.
                    using var state = new MemoryStream(); blitter.SaveState(new BinaryWriter(state));
                    blitter = Create(regs, memory, writes); state.Position = 0; blitter.LoadState(new BinaryReader(state));
                }
                var trace = new List<BlitterWordTraceEntry>(); blitter.WordExecuted += trace.Add;
                Finish(blitter);
                int firstRow = 0;
                Check.Equal(3 - firstRow, trace.Count, "Exactly the remaining row snapshots are emitted.");
                for (int row = firstRow; row < 3; row++)
                {
                    var entry = trace[row - firstRow];
                    Check.Equal(At(bases[0], row * stride), entry.AAddress, "A row address.");
                    Check.Equal(At(bases[1], row * stride), entry.BAddress, "B row address.");
                    Check.Equal(At(bases[2], row * stride), entry.CAddress, "C row address.");
                    Check.Equal(At(bases[3], row * stride), entry.DAddress, "D row address.");
                }
                Check.Equal(3, writes.Count, "Partially fetched and new destination words both complete.");
                for (int row = 0; row < 3; row++)
                {
                    Check.Equal(At(bases[3], row * stride), writes[row].address, "Actual DMA destination.");
                    Check.Equal((ushort)0xB791, writes[row].value, "The independent three-source XOR image.");
                }
                for (int channel = 0; channel < 4; channel++)
                    Check.Equal(At(bases[channel], 3 * stride), ReadPointer(regs, pointers[channel]), "Final pointer includes all row modulos.");
            }
        }

        private static void Line()
        {
            var regs = new CustomRegisterMap(); var memory = new Dictionary<uint, ushort>();
            var writes = new List<(uint address, ushort value)>(); var blitter = Create(regs, memory, writes);
            Pointer(regs, 0x050, 0xFFFC); Pointer(regs, 0x048, 0x2000); Pointer(regs, 0x054, 0x2000);
            regs.WriteWord(0x074, 0x8000); regs.WriteWord(0x072, 0xFFFF);
            regs.WriteWord(0x044, 0xFFFF); regs.WriteWord(0x046, 0xFFFF);
            regs.WriteWord(0x064, 0xFFFF); regs.WriteWord(0x062, 3);
            regs.WriteWord(0x060, 0x29); regs.WriteWord(0x066, 0x29);
            regs.WriteWord(0x040, 0x0BEA); regs.WriteWord(0x042, 0x0045); // Negative initial slope, vertical major axis upwards.
            regs.WriteWord(0x058, (5 << 6) | 1); blitter.NotifyCustomRegisterWrite(0x058, 2);
            var trace = new List<BlitterWordTraceEntry>(); blitter.WordExecuted += trace.Add;
            for (int i = 0; i < 6; i++) blitter.Tick(true); // Save in the middle of the second pixel.
            using var saved = new MemoryStream(); blitter.SaveState(new BinaryWriter(saved));
            var restoredMemory = new Dictionary<uint, ushort>(memory); var restoredWrites = new List<(uint address, ushort value)>();
            var restored = Create(regs, restoredMemory, restoredWrites); saved.Position = 0; restored.LoadState(new BinaryReader(saved));
            var restoredTrace = new List<BlitterWordTraceEntry>(); restored.WordExecuted += restoredTrace.Add;
            Finish(blitter); Finish(restored);
            int[] errors = { -4, -2, 0, -2, 0 }; int[] shifts = { 0, 0, 0, 1, 1 };
            Check.Equal(5, trace.Count, "Five real line pixels.");
            for (int i = 0; i < 5; i++)
            {
                Check.Equal(errors[i], trace[i].LineError, "Unwired bit must not enter the slope accumulator.");
                Check.Equal(At(0x2000, -40 * i), trace[i].CAddress, "Upward line uses the even forty-byte stride.");
                Check.Equal((ushort)(0x8000 >> shifts[i]), writes[i].value, "The rasterized line follows the independent slope.");
                if (i > 0)
                {
                    Check.Equal(trace[i].LineError, restoredTrace[i - 1].LineError, "Restored slope.");
                    Check.Equal(writes[i], restoredWrites[i - 1], "Restored DMA pixel.");
                }
            }
        }

        private static uint At(uint start, int offset) => (uint)((int)start + offset);
        private static Blitter Create(CustomRegisterMap regs, Dictionary<uint, ushort> memory, List<(uint address, ushort value)> writes)
        {
            var blitter = new Blitter(regs);
            blitter.AttachChipMemory(address => memory.TryGetValue(address, out ushort value) ? value : (ushort)0,
                (address, value) => { memory[address] = value; writes.Add((address, value)); });
            return blitter;
        }
        private static void Pointer(CustomRegisterMap regs, uint offset, uint address)
        { regs.WriteWord(offset, (ushort)(address >> 16)); regs.WriteWord(offset + 2, (ushort)address); }
        private static uint ReadPointer(CustomRegisterMap regs, uint offset) => ((uint)regs.ReadWord(offset) << 16) | regs.ReadWord(offset + 2);
        private static void Finish(Blitter blitter)
        {
            int ticks = 0; while (blitter.DmaPending && ticks++ < 1000) blitter.Tick(true);
            Check.True(!blitter.DmaPending, "The fixture must finish DMA.");
        }
    }
}
