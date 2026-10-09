using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.CustomChips;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.DevTools;
using AmigaEmulator.Core.Video;

namespace AmigaEmulator.Tests
{
    internal static class AgaTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("eight independent palette banks combine MSB/LSB writes and RDRAM protects the table", TestPalette);
            yield return Test("eight planes retain all 256 indices, XOR palette offsets and four-plane playfields", TestPlanes);
            yield return Test("HAM8 uses low control bits and preserves two held component bits", TestHam8);
            yield return Test("16/32/64-bit DMA groups align addresses and commit every enabled plane together", TestDma);
            yield return Test("development chipset identity and complete color table survive restore", TestState);
            yield return Test("wide sprites consume complete header/data groups, select palette banks and restore pending DMA", TestSprites);
            yield return Test("extended collision masks include planes seven/eight and legacy writes reset them", TestCollisions);
            yield return Test("playfield XOR and border sprites retain independent border color and blanking", TestBorderComposition);
        }
        private static ChipTestCase Test(string name, Action run) => new("AGA/Bringup", name,
            "Commodore Advanced Amiga Chip Set Functional Specification, 1991; independent fixtures", run);

        private static void TestBorderComposition()
        {
            foreach (var chipset in new[] { AmigaChipset.Aga, AmigaChipset.Ecs })
            {
                var regs = new CustomRegisterMap(chipset);
                regs.Reset();
                var planes = new BitplaneUnit(regs, null);
                var sprites = new SpriteUnit(regs, null);
                var frame = new AmigaFrameBuffer(64, 256);
                var denise = new Denise(frame, planes, sprites);
                regs.WriteWord(0x08E, 0x2C89); regs.WriteWord(0x090, 0x2D91); regs.WriteWord(0x1E4, 0);
                regs.WriteWord(0x100, 0x1001); regs.WriteWord(0x104, 0x0038);
                if (chipset == AmigaChipset.Aga)
                {
                    Color(regs, 0, 0x0000FF); Color(regs, 2, 0xFF0000);
                    Color(regs, 3, 0xFFFF00); Color(regs, 17, 0x00FF00);
                    regs.WriteWord(0x10C, 0x0311);
                }
                else
                {
                    regs.WriteWord(0x180, 0x000F); regs.WriteWord(0x182, 0x0F00);
                    regs.WriteWord(0x1A2, 0x00F0);
                }
                regs.WriteWord(0x106, 0x0C00);
                planes.NotifyCustomRegisterWordWrite(0x110, 0xFFFF);
                denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = 44 });
                denise.Tick(new BeamTraceEntry { BeamX = 0x42, BeamY = 44 });
                Check.Equal(0xFF0000FFu, frame.Pixels[0], "Outside DIW uses COLOR00, independent of playfield XOR.");
                Check.Equal(0xFFFF0000u, frame.Pixels[16], "Inside DIW uses the playfield palette address.");

                regs.WriteWord(0x106, 0x0C22); // BRDRBLNK and BRDRSPRT.
                regs.WriteWord(0x140, 0x2C40); regs.WriteWord(0x142, 0x2D00);
                regs.WriteWord(0x144, 0x8000);
                sprites.NotifyCustomRegisterWordWrite(0x140, 0x2C40, 44);
                sprites.NotifyCustomRegisterWordWrite(0x142, 0x2D00, 44);
                sprites.NotifyCustomRegisterWordWrite(0x144, 0x8000, 44);
                denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = 44 });
                Check.Equal(chipset == AmigaChipset.Aga ? 0xFF00FF00u : 0xFF000000u, frame.Pixels[0], "AGA permits border sprites; ECS continues to clip them.");
                Check.Equal(0xFF000000u, frame.Pixels[2], "Border blank still applies beside an enabled sprite.");
                Check.Equal(0xFF000000u, frame.ReadPlayfieldPixel(0), "Presentation retains black behind the sprite.");
                regs.WriteWord(0x106, 0x0C20);
                denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = 44 });
                Check.Equal(0xFF000000u, frame.Pixels[0], "Clearing BRDRSPRT clips the sprite into the blanked border.");
                regs.WriteWord(0x100, 1); // No playfield, border sprites enabled.
                regs.WriteWord(0x106, 0x0C22);
                denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = 45 });
                Check.Equal(0xFF000000u, frame.Pixels[64], "The empty-playfield fast path blanks vertical borders.");
            }
        }

        private static void Color(CustomRegisterMap registers, int index, uint rgb)
        {
            ushort bank = (ushort)((index / 32) << 13);
            registers.WriteWord(0x106, bank);
            registers.WriteWord(0x180u + (uint)(index % 32) * 2, (ushort)(((rgb >> 12) & 0xF00) | ((rgb >> 8) & 0xF0) | ((rgb >> 4) & 15)));
            registers.WriteWord(0x106, (ushort)(bank | 0x200));
            registers.WriteWord(0x180u + (uint)(index % 32) * 2, (ushort)(((rgb >> 8) & 0xF00) | ((rgb >> 4) & 0xF0) | (rgb & 15)));
        }

        private static void TestPalette()
        {
            var regs = new CustomRegisterMap(AmigaChipset.Aga);
            for (int i = 0; i < 256; i++) Color(regs, i, (uint)(i << 16 | (255 - i) << 8 | i ^ 0x55));
            for (int i = 0; i < 256; i++)
            {
                uint expected = (uint)(i << 16 | (255 - i) << 8 | i ^ 0x55);
                Check.Equal(0xFF000000u | expected, regs.AgaColors.ReadArgb(i), "Each color bank remains independent.");
            }
            regs.WriteWord(0x106, 0xE000); regs.WriteWord(0x180, 0x0ABC);
            Check.Equal(0xFFAABBCCu, regs.AgaColors.ReadArgb(224), "MSB writes mirror low nibbles for legacy lists.");
            regs.WriteWord(0x104, 0x100); regs.WriteWord(0x180, 0);
            Check.Equal((ushort)0xABC, regs.ReadWord(0x180), "RDRAM selected high word and blocks writes.");
            regs.WriteWord(0x106, 0xE200); Check.Equal((ushort)0xABC, regs.ReadWord(0x180), "RDRAM selected low word.");
            regs.Reset(); Check.Equal(0xFF000000u, regs.AgaColors.ReadArgb(224), "Reset clears palette.");
            Check.Equal((ushort)0xFFF8, regs.ReadWord(0x7C), "Lisa ID.");
            Check.Equal((ushort)0x0C00, regs.ReadWord(0x106), "Default second playfield offset is eight.");
            Check.Equal((ushort)0x0011, regs.ReadWord(0x10C), "Default sprite banks preserve COLOR16..31.");
            regs.WriteByte(0x180, 0x0A); regs.WriteByte(0x181, 0xBC);
            Check.Equal(0xFFAABBCCu, regs.AgaColors.ReadArgb(0), "Byte writes retain the other register lane.");
        }

        private static void TestSprites()
        {
            foreach (int mode in new[] { 0, 1, 2, 3 })
            foreach (int resolution in new[] { 1, 2, 3 })
            {
                var regs = new CustomRegisterMap(AmigaChipset.Aga);
                int words = mode == 3 ? 4 : mode == 0 ? 1 : 2;
                int width = words * 16;
                var memory = new Dictionary<uint, ushort>(); var reads = new List<uint>();
                ushort Read(uint address) { reads.Add(address); return memory.TryGetValue(address, out ushort value) ? value : (ushort)0; }
                uint pointer = 0x1000;
                memory[pointer] = 0x0A40; memory[pointer + (uint)words * 2] = 0x0C00;
                uint data = pointer + (uint)words * 4;
                for (int i = 0; i < words; i++)
                {
                    memory[data + (uint)i * 2] = i == words - 1 ? (ushort)1 : (ushort)0;
                    memory[data + (uint)(words + i) * 2] = 0xFFFF;
                }
                regs.WriteWord(0x120, 0); regs.WriteWord(0x122, (ushort)pointer);
                regs.WriteWord(0x1FC, (ushort)(mode << 2));
                Color(regs, 82, 0x102030); Color(regs, 83, 0xAABBCC);
                regs.WriteWord(0x106, (ushort)(resolution << 6)); regs.WriteWord(0x10C, 0x0051);
                regs.WriteWord(0x096, 0x8220);
                var sprites = new SpriteUnit(regs, Read, 2 * 1024 * 1024); sprites.RefreshFromRegisters();
                sprites.TickDmaSlot(0, 0, 10);
                Check.Equal(words, reads.Count, "First header DMA group consumes the selected width.");
                using (var state = new MemoryStream())
                {
                    sprites.SaveState(new BinaryWriter(state)); state.Position = 0;
                    var restored = new SpriteUnit(regs, Read, 2 * 1024 * 1024); restored.LoadState(new BinaryReader(state));
                    sprites = restored;
                }
                reads.Clear(); // Restore independently inspects the sprite list without DMA ownership.
                sprites.TickDmaSlot(0, 1, 10);
                sprites.TickDmaSlot(0, 0, 11); sprites.TickDmaSlot(0, 1, 11);
                Check.Equal(words * 3, reads.Count, "Remaining header and two data groups retain the fixed sprite slot pair.");
                Check.True(sprites.TryRenderPixel(0, 11, out uint first), "Wide sprite first pixel.");
                Check.Equal(0xFF102030u, first, "Even sprite bank and second data latch.");
                int hostWidth = resolution == 1 ? width * 2 : resolution == 2 ? width : width / 2;
                int lastX = hostWidth - 1;
                Check.True(sprites.TryRenderPixel(lastX, 11, out uint last), "Final host sample remains inside the sprite.");
                // Superhires sampling sees the penultimate bit in a hires framebuffer.
                Check.Equal(resolution == 3 ? 0xFF102030u : 0xFFAABBCCu, last, "The complete wide latch is addressable.");
                Check.True(!sprites.TryRenderPixel(hostWidth, 11, out _), "Wide sprite stops exactly at its selected width.");
                sprites.TickDmaSlot(0, 0, 12); sprites.TickDmaSlot(0, 1, 12);
                Check.True(!sprites.TryRenderPixel(0, 12, out _), "Wide zero terminator disarms output.");
            }
        }

        private static void TestCollisions()
        {
            var regs = new CustomRegisterMap(AmigaChipset.Aga); var collisions = new CollisionUnit(); regs.AttachCollisionUnit(collisions);
            regs.WriteWord(0x098, 0); regs.WriteWord(0x10E, 0xC3);
            collisions.ObservePixel(0, 0); Check.Equal((ushort)0, collisions.ReadAndClear(), "Both extended match bits require high planes.");
            collisions.ObservePixel(0xC0, 0); Check.Equal((ushort)1, collisions.ReadAndClear(), "Planes seven/eight match independently.");
            regs.WriteWord(0x098, 0); Check.Equal((ushort)0, regs.ReadWord(0x10E), "CLXCON resets CLXCON2 for old Copper lists.");
            collisions.ObservePixel(0, 0); Check.Equal((ushort)1, collisions.ReadAndClear(), "Disabled extensions impose no constraint.");
        }

        private static (CustomRegisterMap regs, BitplaneUnit planes) Setup()
        {
            var regs = new CustomRegisterMap(AmigaChipset.Aga); var planes = new BitplaneUnit(regs, null);
            regs.WriteWord(0x08E, 0x2C81); regs.WriteWord(0x090, 0x2D91); regs.WriteWord(0x1E4, 0);
            regs.WriteWord(0x100, 0x0011);
            return (regs, planes);
        }
        private static void Latches(BitplaneUnit planes, int index)
        {
            for (int i = 0; i < 8; i++) planes.NotifyCustomRegisterWordWrite(0x110u + (uint)i * 2,
                (index & (1 << i)) != 0 ? (ushort)0xFFFF : (ushort)0);
        }

        private static void TestPlanes()
        {
            var (regs, planes) = Setup();
            for (int index = 0; index < 256; index++)
            {
                Latches(planes, index); planes.RefreshFromRegisters();
                Check.Equal(8, planes.EnabledPlanes, "BPU3 selects eight planes.");
                Check.Equal(index, planes.RenderPixelInfo(8, 44, out int raw), "All color indices survive.");
                Check.Equal(index, raw, "Collision raw plane mask includes planes seven/eight.");
            }
            Color(regs, 0xAC, 0x123456); regs.WriteWord(0x10C, 0xF000);
            Check.Equal(0xFF123456u, planes.ResolveColorArgb(0x5C, 0), "BPLAM XOR palette address.");
            Color(regs, 0, 0xAA1122); Color(regs, 0xF0, 0x33BB44);
            Check.Equal(0xFF33BB44u, planes.ResolveColorArgb(0, 0), "Playfield zero index is XORed.");
            Check.Equal(0xFFAA1122u, planes.BorderColorArgb, "Border keeps COLOR00 despite playfield XOR.");
            Check.Equal(0xFFAA1122u, planes.ReadColorArgb(0), "HAM initial color keeps COLOR00.");
            regs.WriteWord(0x10C, 0); regs.WriteWord(0x100, 0x0411); regs.WriteWord(0x106, 0x0C00);
            Latches(planes, 0xAA); planes.RefreshFromRegisters();
            Check.Equal(8 + 15, planes.RenderColorIndex(8, 44), "Four-plane PF2 and configured offset.");
            regs.WriteWord(0x106, 0x1800);
            Check.Equal(64 + 15, planes.RenderColorIndex(8, 44), "PF2 palette bank changes without losing depth.");
        }

        private static void TestHam8()
        {
            var (regs, planes) = Setup(); regs.WriteWord(0x100, 0x0811); planes.RefreshFromRegisters();
            Color(regs, 63, 0x123456);
            Check.Equal(0xFF123456u, planes.ResolveColorArgb(63 << 2, 0), "HAM8 selects sixty-four base entries.");
            uint prior = 0xFF123456;
            Check.Equal(0xFF1234FEu, planes.ResolveColorArgb(0xFD, prior), "Blue changes six MSBs, preserving blue low bits.");
            Check.Equal(0xFFFE3456u, planes.ResolveColorArgb(0xFE, prior), "Red component.");
            Check.Equal(0xFF12FC56u, planes.ResolveColorArgb(0xFF, prior), "Green component.");
        }

        private static void TestDma()
        {
            foreach (ushort mode in new ushort[] { 0, 1, 2, 3 })
            {
                var regs = new CustomRegisterMap(AmigaChipset.Aga); var reads = new List<uint>();
                var planes = new BitplaneUnit(regs, a => { reads.Add(a); return 0xFFFF; }, 2 * 1024 * 1024);
                regs.WriteWord(0x08E, 0x2C81); regs.WriteWord(0x090, 0x2D91); regs.WriteWord(0x1E4, 0);
                regs.WriteWord(0x092, 0x38); regs.WriteWord(0x094, 0x38);
                regs.WriteWord(0x100, 0x0011); regs.WriteWord(0x1FC, mode); regs.WriteWord(0x096, 0x8300);
                for (int i = 0; i < 8; i++) { regs.WriteWord(0xE0u + (uint)i * 4, 0); regs.WriteWord(0xE2u + (uint)i * 4, (ushort)(0x3000 + i * 0x100 + 2)); }
                planes.BeginFrame();
                for (int slot = 0; slot < 7; slot++) planes.FetchDmaPlaneWord(0x38 + slot, 44);
                Check.Equal(0, planes.RenderColorIndex(8, 44), "Partially fetched groups remain hidden.");
                planes.FetchDmaPlaneWord(0x3F, 44);
                Check.Equal(255, planes.RenderColorIndex(8, 44), "Completed eight-plane group is visible.");
                int words = mode == 3 ? 4 : mode == 0 ? 1 : 2;
                Check.Equal(8 * words, reads.Count, "Selected DMA width loads one group per plane.");
                for (int i = 0; i < reads.Count; i += words)
                    Check.Equal(0u, reads[i] & (uint)(words * 2 - 1), "Fetch base uses selected alignment.");
            }
        }

        private static void TestState()
        {
            var model = AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip2MbFast4Mb)
                .WithDevelopmentProcessor(AmigaCpuModel.M68EC020, 14187580).WithDevelopmentChipset(AmigaChipset.Aga);
            using var source = new AmigaSystem(model); Color(source.Agnus.CustomRegisters, 255, 0xAABBCC);
            using var stream = new MemoryStream(); source.SaveState(new BinaryWriter(stream));
            using var restored = new AmigaSystem(model); stream.Position = 0; restored.LoadState(new BinaryReader(stream));
            Check.Equal(0xFFAABBCCu, restored.Agnus.CustomRegisters.AgaColors.ReadArgb(255), "Full palette state restore.");
            using var wrong = new AmigaSystem(model.WithDevelopmentChipset(AmigaChipset.Ecs)); stream.Position = 0;
            bool rejected = false; try { wrong.LoadState(new BinaryReader(stream)); } catch (InvalidDataException) { rejected = true; }
            Check.True(rejected, "Different chipset is rejected before loading arrays.");
        }
    }
}
