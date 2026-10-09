using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.CustomChips;
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Tests
{
    internal static class SpriteShifterTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("early POS/DATA writes preserve the running sprite burst and its restored tail", TestRunningBurst);
            yield return Test("zero VSTOP cannot become a line-256 DMA fetch", TestZeroStop);
            yield return Test("a missed VSTOP comparator does not continuously consume control words", TestMissedStop);
            yield return Test("early Copper DATA arms a parked DMA sprite before the first slot of a new line", TestEarlyData);
            yield return new ChipTestCase("Denise/Bitplane", "packed DMA output preserves every plane bit, scroll delay and reset boundary",
                "Independent planar bitmap reference over OCS/ECS/AGA register fixtures", TestPackedOutput);
        }

        private static ChipTestCase Test(string name, Action run) => new("Denise/Sprite", name,
            "Commodore Hardware Reference Manual, Sprite Hardware Details; independent register fixtures", run);

        private static void TestRunningBurst()
        {
            foreach (AmigaChipset chipset in Enum.GetValues<AmigaChipset>())
            {
                var regs = new CustomRegisterMap(chipset);
                regs.Reset();
                regs.WriteWord(0x140, 0x2C40); regs.WriteWord(0x142, 0x2D00);
                regs.WriteWord(0x144, 0xFFFF); regs.WriteWord(0x146, 0);
                regs.WriteWord(0x1A2, 0xF00);
                var sprites = new SpriteUnit(regs, null);
                sprites.RefreshFromRegisters();
                Check.True(sprites.TryRenderPixel(0, 44, out uint first), "The first comparison starts a red sprite burst.");
                Check.Equal(0xFFFF0000u, first, chipset + ": initial DATA selects COLOR17.");
                regs.WriteWord(0x140, 0x2C60);
                sprites.NotifyCustomRegisterWordWrite(0x140, 0x2C60, 44);
                regs.WriteWord(0x144, 0);
                sprites.NotifyCustomRegisterWordWrite(0x144, 0, 44);
                Check.True(sprites.HasRenderablePixelsOnLine(44), "The renderer must still sample a running burst after DATA becomes transparent.");
                Check.True(sprites.TryRenderPixel(30, 44, out uint tail), "Moving POS and changing DATA cannot truncate the old serial output.");
                Check.Equal(first, tail, "The running burst retains the original bitmap.");
                using var saved = new MemoryStream();
                sprites.SaveState(new BinaryWriter(saved)); saved.Position = 0;
                var restored = new SpriteUnit(regs, null);
                restored.LoadState(new BinaryReader(saved));
                Check.True(restored.TryRenderPixel(31, 44, out uint restoredTail), "Restore retains the final hires sample of the old burst.");
                Check.Equal(first, restoredTail, "Restore retains the latched pixel data independently of new DATA.");
                Check.True(!restored.TryRenderPixel(128, 44, out _), "The next comparison uses the newly transparent DATA.");
                Check.True(!restored.TryRenderPixel(31, 45, out _), "A new scanline cannot reuse the preceding output latch.");
            }
        }

        private static void TestZeroStop()
        {
            foreach (AmigaChipset chipset in Enum.GetValues<AmigaChipset>())
            {
                var regs = new CustomRegisterMap(chipset);
                int reads = 0;
                ushort Read(uint address) { reads++; return address == 0x102 ? (ushort)0x80 : (ushort)0; }
                var sprites = new SpriteUnit(regs, Read);
                regs.WriteWord(0x096, 0x8220); regs.WriteWord(0x122, 0x100);
                sprites.RefreshFromRegisters();
                sprites.TickDmaSlot(0, 0, 25); sprites.TickDmaSlot(0, 1, 25);
                Check.Equal(2, reads, "Only the parked sprite's control pair is fetched.");
                regs.WriteWord(0x140, 0x0060); sprites.NotifyCustomRegisterWordWrite(0x140, 0x0060, 100);
                regs.WriteWord(0x144, 0xFFFF); sprites.NotifyCustomRegisterWordWrite(0x144, 0xFFFF, 100);
                regs.WriteWord(0x142, 0); sprites.NotifyCustomRegisterWordWrite(0x142, 0, 205);
                for (int y = 206; y < 300; y++)
                { sprites.TickDmaSlot(0, 0, y); sprites.TickDmaSlot(0, 1, y); }
                Check.Equal(2, reads, "VSTOP=0 has already passed; line 256 must not fetch unrelated RAM.");
            }
        }

        private static void TestEarlyData()
        {
            foreach (AmigaChipset chipset in Enum.GetValues<AmigaChipset>())
            {
                var regs = new CustomRegisterMap(chipset); regs.Reset();
                var sprites = new SpriteUnit(regs, address => address == 0x102 ? (ushort)0x80 : (ushort)0);
                regs.WriteWord(0x096, 0x8220); regs.WriteWord(0x122, 0x100);
                regs.WriteWord(0x1A2, 0xF00); sprites.RefreshFromRegisters();
                sprites.TickDmaSlot(0, 0, 25); sprites.TickDmaSlot(0, 1, 25);
                // On line 100 Copper writes DATA ahead of the sprite slot.
                regs.WriteWord(0x140, 0x0040); sprites.NotifyCustomRegisterWordWrite(0x140, 0x0040, 100);
                regs.WriteWord(0x144, 0xFFFF); sprites.NotifyCustomRegisterWordWrite(0x144, 0xFFFF, 100);
                sprites.TickDmaSlot(0, 0, 100); sprites.TickDmaSlot(0, 1, 100);
                Check.True(sprites.HasRenderablePixelsOnLine(100), "Starting DMA slot bookkeeping cannot erase a preceding DATA arm.");
                Check.True(sprites.TryRenderPixel(0, 100, out uint color), "Parked DMA channel still emits Copper-supplied bitmap data.");
                Check.Equal(0xFFFF0000u, color, "The early Copper word reaches the sprite output.");
            }
        }

        private static void TestMissedStop()
        {
            var regs = new CustomRegisterMap(); int reads = 0;
            ushort Read(uint address) { reads++; return address == 0x100 ? (ushort)0x5A40 : (ushort)0x5000; }
            var sprites = new SpriteUnit(regs, Read);
            regs.WriteWord(0x096, 0x8220); regs.WriteWord(0x122, 0x100);
            sprites.RefreshFromRegisters();
            sprites.TickDmaSlot(0, 0, 100); sprites.TickDmaSlot(0, 1, 100);
            for (int y = 101; y < 130; y++)
            { sprites.TickDmaSlot(0, 0, y); sprites.TickDmaSlot(0, 1, y); }
            Check.Equal(2, reads, "A loaded stop behind the beam does not match subsequent lines.");
        }

        private static void TestPackedOutput()
        {
            foreach (AmigaChipset chipset in Enum.GetValues<AmigaChipset>())
            for (int count = 1; count <= (chipset == AmigaChipset.Aga ? 8 : 6); count++)
            for (int scroll = 0; scroll < 16; scroll++)
            {
                var regs = new CustomRegisterMap(chipset); regs.Reset();
                var memory = new Dictionary<uint, ushort>();
                for (int plane = 0; plane < count; plane++)
                {
                    uint address = 0x100u + (uint)plane * 0x40;
                    memory[address] = (ushort)(0xA639 ^ (0x1249 << plane));
                    regs.WriteWord(0xE0u + (uint)plane * 4, 0);
                    regs.WriteWord(0xE2u + (uint)plane * 4, (ushort)address);
                }
                regs.WriteWord(0x100, (ushort)(count == 8 ? 0x10 : count << 12));
                regs.WriteWord(0x102, (ushort)(scroll * 17));
                regs.WriteWord(0x092, 0x38); regs.WriteWord(0x094, 0x38);
                regs.WriteWord(0x096, 0x8300);
                var planes = new BitplaneUnit(regs, address => memory.TryGetValue(address, out ushort value) ? value : (ushort)0);
                planes.FetchDmaWords(0x38, 44);
                int origin = Math.Max(0, 4 - scroll * 2) + scroll * 2;
                for (int bit = 0; bit < 16; bit++)
                {
                    int expected = 0;
                    for (int plane = 0; plane < count; plane++)
                        expected |= ((memory[0x100u + (uint)plane * 0x40] >> (15 - bit)) & 1) << plane;
                    Check.Equal(expected, planes.RenderPlaneMask(origin + bit * 2, 44), "Packed pixels retain independent planar bits and scroll delay.");
                }
                planes.Reset();
                Check.Equal(0, planes.RenderPlaneMask(origin, 44), "Reset cannot expose an earlier packed DMA word.");
            }
        }
    }
}
