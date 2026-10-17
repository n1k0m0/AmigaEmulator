// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
            yield return Test("all 256 scroll delays retain independent odd/even phases in lores and hires", TestExtendedScroll);
            yield return Test("ECS/AGA hires preload stays outside DIW and DMA spans retain every leading bit", TestHiresDmaPhase);
            yield return Test("ECS/AGA SHRES request survives BPLCON0 decoding while OCS ignores it", TestSuperHiresDecode);
            yield return Test("ECS SHRES DMA yields eighty words per plane and 1280 distinct bit samples", TestEcsSuperHiresDmaBits);
            yield return Test("AGA FMODE0 SHRES fetches both 1280-pixel bitplane rows", TestAgaSuperHiresDmaBits);
            yield return Test("SHRES native border follows COLOR00 raster writes without transparent gaps", TestSuperHiresNativeBorder);
            yield return Test("mid-field SHRES enable retains the already rendered lowres aperture", TestSuperHiresMidFieldEnable);
            yield return Test("SHRES native presentation preserves both aperture samples and drops stale frames", TestSuperHiresNativePresentation);
            yield return Test("ECS/AGA SHRES reaches a completed system presentation field", TestSuperHiresSystemField);
        }
        private static ChipTestCase Test(string name, Action run) => new("AGA/Bringup", name,
            "Commodore Advanced Amiga Chip Set Functional Specification, 1991; independent fixtures", run);

        private static void TestSuperHiresNativePresentation()
        {
            var model = AmigaModel.Create(AmigaMachineModel.A1200, AmigaRamProfile.Chip2MbFast4Mb);
            using var system = new AmigaSystem(model);
            var raw = system.FrameBuffer;
            int width = AmigaSystem.PresentationViewportWidth;
            int height = AmigaSystem.PresentationViewportHeight;
            raw.RecordSuperHiresPlayfieldPair(0, 0xFFFF0000u, 0xFF0000FFu);
            raw.RecordSuperHiresSpritePair(0, 0, 0xFF00FF00u);
            raw.RecordSuperHiresPlayfieldPair(width - 1, 0xFFFFFF00u, 0xFF00FFFFu);
            MethodInfo copy = typeof(AmigaSystem).GetMethod("CopyPresentationWindow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Check.True(copy != null, "Presentation aperture copier is available.");
            copy.Invoke(system, new object[] { 0, 0, width, height });
            uint[] native = new uint[width * height * 2];
            Check.True(system.PresentedFrameBuffer.CopyCompositedSuperHiresTo(native),
                "Selected aperture retains the native SHRES output.");
            Check.Equal(0xFFFF0000u, native[0], "First aperture sample is red.");
            Check.Equal(0xFF00FF00u, native[1], "Second aperture sample retains its sprite.");
            Check.Equal(0xFFFFFF00u, native[(width - 1) * 2], "Right edge first sample is retained.");
            Check.Equal(0xFF00FFFFu, native[width * 2 - 1], "Right edge second sample is retained.");
            raw.DiscardSuperHiresPlayfield();
            int croppedFirst = raw.Width + 10;
            int croppedLast = raw.Width + 709;
            raw.RecordSuperHiresPlayfieldPair(croppedFirst, 0xFF112233u, 0xFF445566u);
            raw.RecordSuperHiresPlayfieldPair(croppedLast, 0xFF778899u, 0xFFAABBCCu);
            copy.Invoke(system, new object[] { 10, 1, 700, 280 });
            Check.True(system.PresentedFrameBuffer.CopyCompositedSuperHiresTo(native),
                "Scaled source aperture retains native samples.");
            Check.Equal(0xFF112233u, native[0], "Scaled crop selects its first source column.");
            Check.Equal(0xFF445566u, native[1], "Scaled crop retains its second sample.");
            Check.Equal(0xFF778899u, native[width * 2 - 2], "Scaled crop selects its last source column.");
            Check.Equal(0xFFAABBCCu, native[width * 2 - 1], "Scaled crop retains the last native sample.");
            raw.DiscardSuperHiresPlayfield();
            copy.Invoke(system, new object[] { 0, 0, width, height });
            Check.True(!system.PresentedFrameBuffer.CopyCompositedSuperHiresTo(native),
                "A following non-SHRES frame has no stale native output.");
        }

        private static void TestSuperHiresSystemField()
        {
            foreach (var machine in new[] { AmigaMachineModel.A500Plus, AmigaMachineModel.A1200 })
            {
                var model = AmigaModel.Create(machine, AmigaRamProfile.Chip2MbFast4Mb);
                using var system = new AmigaSystem(model);
                var regs = system.Agnus.CustomRegisters;
                regs.WriteWord(0x08E, 0x2C81);
                regs.WriteWord(0x090, 0x2DC1);
                regs.WriteWord(0x092, 0x003C);
                regs.WriteWord(0x094, 0x00D4);
                regs.WriteWord(0x0E2, 0x2000);
                regs.WriteWord(0x0E6, 0x3000);
                system.Bus.WriteWord(0x2000, 0x8000);
                system.Bus.WriteWord(0x3000, 0x4000);
                regs.WriteWord(0x096, 0x8300);
                regs.WriteWord(0x100, 0x2041);
                regs.WriteWord(0x180, 0x0F00);
                if (machine == AmigaMachineModel.A1200)
                {
                    regs.WriteWord(0x182, 0x0F00);
                    regs.WriteWord(0x184, 0x000F);
                }
                else regs.WriteWord(0x192, 0x0C36);
                system.Agnus.Bitplanes.RefreshFromRegisters();
                system.RunChipCycles(model.ChipCyclesPerLine * (model.LinesPerFrame + 1));
                var presented = system.PresentedFrameBuffer;
                Check.True(presented.SuperHiresPlayfieldPixels != null,
                    machine + " keeps native samples through a completed field.");
                uint[] native = new uint[presented.Pixels.Length * 2];
                Check.True(presented.CopyCompositedSuperHiresTo(native),
                    machine + " provides a native frame for the frontend and capture runner.");
                Check.Equal(presented.Width * 2 * presented.Height, native.Length,
                    machine + " has exactly two samples per logical pixel.");
                Check.Equal(machine == AmigaMachineModel.A1200 ? 0xFFFF0000u : 0xFFFF0055u, native[0],
                    machine + " carries the programmed red COLOR00 border through the native aperture.");
                bool foundAdjacentColors = false;
                uint expectedFirst = machine == AmigaMachineModel.A1200 ? 0xFFFF0000u : 0xFFFF0055u;
                uint expectedSecond = machine == AmigaMachineModel.A1200 ? 0xFF0000FFu : 0xFF00FFAAu;
                for (int index = 0; index < native.Length - 1; index += 2)
                {
                    if (native[index] == expectedFirst && native[index + 1] == expectedSecond)
                    {
                        foundAdjacentColors = true;
                        break;
                    }
                }
                Check.True(foundAdjacentColors,
                    machine + " presents adjacent independently fetched 35-ns color samples.");
                using var state = new MemoryStream();
                using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true))
                    system.SaveState(writer);
                int continuationCycles = model.ChipCyclesPerLine * (model.LinesPerFrame * 2 + 2);
                system.RunChipCycles(continuationCycles);
                uint[] continuedNative = new uint[native.Length];
                Check.True(system.PresentedFrameBuffer.CopyCompositedSuperHiresTo(continuedNative),
                    machine + " continues native output before restore.");
                state.Position = 0;
                using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true))
                    system.LoadState(reader);
                system.RunChipCycles(continuationCycles);
                uint[] restoredNative = new uint[native.Length];
                Check.True(system.PresentedFrameBuffer.CopyCompositedSuperHiresTo(restoredNative),
                    machine + " regenerates native output after restore.");
                Check.True(continuedNative.AsSpan().SequenceEqual(restoredNative),
                    machine + " restores the exact complete native presented frame.");
            }
        }

        private static void TestSuperHiresNativeBorder()
        {
            foreach (var chipset in new[] { AmigaChipset.Ecs, AmigaChipset.Aga })
            {
                var regs = new CustomRegisterMap(chipset);
                var planes = new BitplaneUnit(regs, null);
                var frame = new AmigaFrameBuffer(16, 1);
                var denise = new Denise(frame, planes, new SpriteUnit(regs, null));
                regs.WriteWord(0x08E, 0x2D81); // DIW starts one line below this border fixture.
                regs.WriteWord(0x090, 0x2EC1);
                regs.WriteWord(0x100, 0x2041); // SHRES with ECS enable; DMA remains off.
                regs.WriteWord(0x180, 0x00F0); // Green COLOR00.
                denise.BeginFrame();
                uint green = planes.BorderColorArgb;
                Check.Equal(green, frame.SuperHiresPlayfieldPixels[0], "The left native border starts with COLOR00.");
                Check.Equal(green, frame.SuperHiresPlayfieldPixels[31], "The right native border is initialized too.");

                regs.WriteWord(0x180, 0x0F00); // Red COLOR00 mid-line.
                planes.NotifyCustomRegisterWordWrite(0x180, 0x0F00);
                uint red = planes.ReadColorArgb(0);
                denise.Tick(new BeamTraceEntry { BeamX = 63, BeamY = 44 });
                Check.Equal(red, frame.Pixels[4], "The host border observes the raster COLOR00 change.");
                Check.Equal(red, frame.SuperHiresPlayfieldPixels[8], "The first native sample observes the raster COLOR00 change.");
                Check.Equal(red, frame.SuperHiresPlayfieldPixels[15], "The full chip-cycle border span is updated.");
                Check.Equal(green, frame.SuperHiresPlayfieldPixels[16], "Unvisited native border keeps its previous color.");
            }
        }

        private static void TestSuperHiresMidFieldEnable()
        {
            foreach (var chipset in new[] { AmigaChipset.Ecs, AmigaChipset.Aga })
            {
                var regs = new CustomRegisterMap(chipset);
                var planes = new BitplaneUnit(regs, null);
                var frame = new AmigaFrameBuffer(16, 1);
                var denise = new Denise(frame, planes, new SpriteUnit(regs, null));
                regs.WriteWord(0x180, 0x000F);
                denise.BeginFrame();
                frame.Pixels[0] = 0xFFFF0000u;
                frame.Pixels[1] = 0xFF00FF00u;
                regs.WriteWord(0x100, 0x0041); // Copper enables SHRES in the current field.
                planes.RefreshFromRegisters();
                denise.Tick(new BeamTraceEntry { BeamX = 63, BeamY = 44 });
                Check.True(frame.SuperHiresPlayfieldPixels != null,
                    chipset + " allocates native samples on the raster mode switch.");
                Check.Equal(0xFFFF0000u, frame.SuperHiresPlayfieldPixels[0],
                    chipset + " keeps the earlier first lowres pixel.");
                Check.Equal(0xFFFF0000u, frame.SuperHiresPlayfieldPixels[1],
                    chipset + " keeps the earlier first lowres pixel's second sample.");
                Check.Equal(0xFF00FF00u, frame.SuperHiresPlayfieldPixels[2],
                    chipset + " keeps the earlier adjacent pixel distinct.");
                Check.Equal(frame.Pixels[15], frame.SuperHiresPlayfieldPixels[31],
                    chipset + " keeps the unvisited right border opaque.");
                frame.RecordSuperHiresSpritePair(8, 0xFFFFFFFFu, 0xFFFFFFFFu);
                regs.WriteWord(0x100, 0); // Copper returns to lores in the same field.
                planes.RefreshFromRegisters();
                denise.Tick(new BeamTraceEntry { BeamX = 64, BeamY = 44 });
                uint[] native = new uint[frame.Pixels.Length * 2];
                Check.True(frame.CopyCompositedSuperHiresTo(native),
                    chipset + " retains the mixed-resolution field.");
                Check.Equal(frame.Pixels[8], native[16],
                    chipset + " expands the subsequent lores pixel instead of stale SHRES output.");
                Check.Equal(frame.Pixels[8], native[17],
                    chipset + " replaces both samples and clears old sprite overlay.");
            }
        }

        private static void TestSuperHiresDecode()
        {
            foreach (var chipset in new[] { AmigaChipset.Ocs, AmigaChipset.Ecs, AmigaChipset.Aga })
            {
                var regs = new CustomRegisterMap(chipset);
                var planes = new BitplaneUnit(regs, null);
                regs.WriteWord(0x100, 0x2041); // Two planes, SHRES, ECS enable.
                planes.RefreshFromRegisters();
                Check.Equal(chipset != AmigaChipset.Ocs, planes.IsSuperHiresMode,
                    "Only ECS Denise/Lisa decodes the 35 ns pixel request.");
                Check.Equal(2, planes.EnabledPlanes, "SHRES bit must not change BPU depth.");
                using (var state = new MemoryStream())
                {
                    planes.SaveState(new BinaryWriter(state));
                    state.Position = 0;
                    planes.LoadState(new BinaryReader(state));
                    planes.RefreshFromRegisters();
                    Check.Equal(chipset != AmigaChipset.Ocs, planes.IsSuperHiresMode,
                        "Restored register decoding retains the chipset-specific SHRES request.");
                }
                regs.WriteWord(0x100, 0x2001);
                planes.RefreshFromRegisters();
                Check.True(!planes.IsSuperHiresMode, "Clearing SHRES returns to the legacy mode.");
            }
        }

        private static void TestEcsSuperHiresDmaBits()
        {
            var regs = new CustomRegisterMap(AmigaChipset.Ecs);
            int firstReads = 0, secondReads = 0;
            ushort Read(uint address)
            {
                if (address >= 0x1000)
                {
                    secondReads++;
                    int word = (int)((address - 0x1000) / 2);
                    return word == 0 ? (ushort)0x4040 : word == 79 ? (ushort)0x0002 : (ushort)0;
                }
                firstReads++;
                int firstWord = (int)(address / 2);
                return firstWord == 0 ? (ushort)0x8080 : firstWord == 79 ? (ushort)0x0001 : (ushort)0;
            }
            var planes = new BitplaneUnit(regs, Read);
            regs.WriteWord(0x08E, 0x2C81); regs.WriteWord(0x090, 0x2DC1);
            regs.WriteWord(0x1E4, 0x2000);
            regs.WriteWord(0x092, 0x3C); regs.WriteWord(0x094, 0xD4);
            regs.WriteWord(0x100, 0x2041); regs.WriteWord(0x096, 0x8300);
            regs.WriteWord(0x0E6, 0x1000);
            planes.BeginFrame();
            for (int beamX = 0; beamX < 227; beamX++)
                if (planes.IsPlaneFetchSlot(beamX, 44)) planes.FetchDmaPlaneWord(beamX, 44);
            Check.Equal(80, firstReads, "A 1280-sample SHRES row fetches eighty first-plane words.");
            Check.Equal(80, secondReads, "The second plane has the same native fetch width.");
            Check.Equal(1, planes.RenderSuperHiresColorIndex(0, 44), "First native bit comes from plane one.");
            Check.Equal(2, planes.RenderSuperHiresColorIndex(1, 44), "Next 35 ns bit is independently sampled from plane two.");
            Check.Equal(0, planes.RenderSuperHiresColorIndex(2, 44), "Bits do not stretch to a 70 ns sample.");
            Check.Equal(2, planes.RenderSuperHiresColorIndex(1278, 44), "Penultimate native bit comes from plane two.");
            Check.Equal(1, planes.RenderSuperHiresColorIndex(1279, 44), "Last native bit of word eighty remains visible.");
            Check.Equal(1, planes.RenderColorIndex(0, 44), "The 640-pixel preview includes the left SHRES edge.");
            Check.Equal(2, planes.RenderColorIndex(639, 44), "The 640-pixel preview reaches the right SHRES edge.");
            using var state = new MemoryStream();
            planes.SaveState(new BinaryWriter(state));
            state.Position = 0;
            planes.LoadState(new BinaryReader(state));
            planes.RefreshFromRegisters();
            Check.Equal(1, planes.RenderSuperHiresColorIndex(0, 44), "Restore retains the first native bit.");
            Check.Equal(2, planes.RenderSuperHiresColorIndex(1278, 44), "Restore retains the final word's second plane.");
            Check.Equal(1, planes.RenderSuperHiresColorIndex(1279, 44), "Restore retains the final native bit.");
            regs.WriteWord(0x192, 0x0C36); // COLOR09 packs native colors 1 then 2.
            planes.NotifyCustomRegisterWordWrite(0x192, 0x0C36);
            var frame = new AmigaFrameBuffer(16, 1);
            var denise = new Denise(frame, planes, new SpriteUnit(regs, null));
            denise.Tick(new BeamTraceEntry { BeamX = 62, BeamY = 44 });
            Check.Equal(0xFFFF0055u, frame.Pixels[0], "First SHRES sample takes COLOR09's high two RGB bits.");
            Check.Equal(frame.Width * frame.Height * 2, frame.SuperHiresPlayfieldPixels.Length,
                "The native SHRES playfield target retains two samples per host pixel.");
            Check.Equal(0xFFFF0055u, frame.SuperHiresPlayfieldPixels[0], "Native ECS pixel zero is the first packed color.");
            Check.Equal(0xFF00FFAAu, frame.SuperHiresPlayfieldPixels[1], "Native ECS pixel one is the second packed color.");
            Check.Equal(0xFF00FFAAu, planes.ResolveEcsSuperHiresColorArgb(1, 2, true),
                "Second SHRES sample takes the same register's low two RGB bits.");
            planes.ResolveSuperHiresPixelPair(1, 2, out uint ecsFirst, out uint ecsSecond);
            Check.Equal(0xFFFF0055u, ecsFirst, "The native ECS pair retains its first 35 ns color.");
            Check.Equal(0xFF00FFAAu, ecsSecond, "The native ECS pair retains its second 35 ns color.");
            regs.WriteWord(0x192, 0x003C);
            planes.NotifyCustomRegisterWordWrite(0x192, 0x003C);
            denise.Tick(new BeamTraceEntry { BeamX = 63, BeamY = 44 });
            Check.Equal(0xFF0000FFu, frame.Pixels[4], "Raster COLOR09 rewrite changes the next SHRES pair immediately.");
            using var colorState = new MemoryStream();
            planes.SaveState(new BinaryWriter(colorState));
            colorState.Position = 0;
            planes.LoadState(new BinaryReader(colorState));
            planes.RefreshFromRegisters();
            Check.Equal(0xFF0000FFu, planes.ResolveEcsSuperHiresColorArgb(1, 2),
                "Restoring a legacy bitplane state rebuilds the SHRES palette from COLOR09.");
        }

        private static void TestAgaSuperHiresDmaBits()
        {
            var regs = new CustomRegisterMap(AmigaChipset.Aga);
            int firstReads = 0, secondReads = 0;
            ushort Read(uint address)
            {
                if (address >= 0x1000)
                {
                    secondReads++;
                    int word = (int)((address - 0x1000) / 2);
                    return word == 0 ? (ushort)0x4000 : word == 79 ? (ushort)0x0002 : (ushort)0;
                }
                firstReads++;
                int firstWord = (int)(address / 2);
                return firstWord == 0 ? (ushort)0x8000 : firstWord == 79 ? (ushort)0x0001 : (ushort)0;
            }
            var planes = new BitplaneUnit(regs, Read);
            regs.WriteWord(0x08E, 0x2C81); regs.WriteWord(0x090, 0x2DC1);
            regs.WriteWord(0x1E4, 0x2000);
            regs.WriteWord(0x092, 0x3C); regs.WriteWord(0x094, 0xD4);
            regs.WriteWord(0x100, 0x2041); regs.WriteWord(0x096, 0x8300);
            regs.WriteWord(0x0E6, 0x1000); regs.WriteWord(0x1FC, 0);
            planes.BeginFrame();
            for (int beamX = 0; beamX < 227; beamX++)
                if (planes.IsPlaneFetchSlot(beamX, 44)) planes.FetchDmaPlaneWord(beamX, 44);
            Check.Equal(80, firstReads, "AGA FMODE0 SHRES reads eighty words from plane one.");
            Check.Equal(80, secondReads, "AGA FMODE0 SHRES reads eighty words from plane two.");
            Check.Equal(1, planes.RenderSuperHiresColorIndex(0, 44), "AGA first native sample uses plane one.");
            Check.Equal(2, planes.RenderSuperHiresColorIndex(1, 44), "AGA second native sample uses plane two.");
            Check.Equal(2, planes.RenderSuperHiresColorIndex(1278, 44), "AGA penultimate sample reaches word eighty.");
            Check.Equal(1, planes.RenderSuperHiresColorIndex(1279, 44), "AGA last sample reaches word eighty.");
            Check.Equal(1, planes.RenderColorIndex(0, 44), "AGA preview reaches the left SHRES edge.");
            Check.Equal(2, planes.RenderColorIndex(639, 44), "AGA preview reaches the right SHRES edge.");
            regs.WriteWord(0x182, 0x0F00); // AGA COLOR01 is direct red, not ECS pair encoding.
            regs.WriteWord(0x184, 0x000F); // COLOR02 is independently blue.
            var frame = new AmigaFrameBuffer(16, 1);
            var denise = new Denise(frame, planes, new SpriteUnit(regs, null));
            denise.Tick(new BeamTraceEntry { BeamX = 62, BeamY = 44 });
            Check.Equal(0xFFFF0000u, frame.Pixels[0], "AGA preview uses Lisa's direct palette for the first native pixel.");
            Check.Equal(0xFFFF0000u, frame.SuperHiresPlayfieldPixels[0], "Native AGA pixel zero uses COLOR01.");
            Check.Equal(0xFF0000FFu, frame.SuperHiresPlayfieldPixels[1], "Native AGA pixel one uses COLOR02.");
            Color(regs, 17, 0x00FF00); // Sprite color one, independent of playfield red/blue.
            regs.WriteWord(0x106, 0x00C0); // One 35 ns sample per AGA sprite bit.
            regs.WriteWord(0x104, 0x0020); // Sprite group zero over the playfield.
            regs.WriteWord(0x10C, 0x0011); // Both sprite banks use COLOR16..31.
            regs.WriteWord(0x140, 0x2C40); regs.WriteWord(0x142, 0x2D00);
            regs.WriteWord(0x144, 0x4000); regs.WriteWord(0x146, 0);
            var compositeSprites = new SpriteUnit(regs, null);
            compositeSprites.NotifyCustomRegisterWordWrite(0x140, 0x2C40, 44);
            compositeSprites.NotifyCustomRegisterWordWrite(0x142, 0x2D00, 44);
            compositeSprites.NotifyCustomRegisterWordWrite(0x144, 0x4000, 44);
            compositeSprites.NotifyCustomRegisterWordWrite(0x146, 0, 44);
            Check.True(compositeSprites.TryRenderSuperHiresPixel(1, 44,
                out uint nativeSpriteProbe, out _, out _), "The second native sprite bit is present before Denise composition.");
            Check.Equal(0xFF00FF00u, nativeSpriteProbe, "The native sprite uses its independent palette entry.");
            var compositeFrame = new AmigaFrameBuffer(16, 1);
            var compositeDenise = new Denise(compositeFrame, planes, compositeSprites);
            compositeDenise.Tick(new BeamTraceEntry { BeamX = 62, BeamY = 44 });
            var nativeComposite = new uint[compositeFrame.Width * 2];
            Check.True(compositeFrame.CopyCompositedSuperHiresTo(nativeComposite),
                "A native SHRES target can combine the playfield and sprite overlay.");
            Check.Equal(0xFFFF0000u, nativeComposite[0], "The first native sample stays playfield red.");
            Check.Equal(0xFF00FF00u, nativeComposite[1], "The second native sample shows its independent sprite bit.");
            Check.Equal(0xFF0000FFu, compositeFrame.SuperHiresPlayfieldPixels[1],
                "Native sprite composition leaves raw playfield data available.");
            Check.Equal(0xFFFF0000u, compositeFrame.Pixels[0],
                "The 70 ns preview still selects only the first native sample.");
            compositeDenise.BeginFrame();
            Check.True(compositeFrame.CopyCompositedSuperHiresTo(nativeComposite),
                "The following SHRES field retains its native output target.");
            Check.Equal(planes.BorderColorArgb, nativeComposite[1],
                "A new field clears the native sprite overlay along with the playfield.");
            var wideFrame = new AmigaFrameBuffer(640, 1);
            var wideDenise = new Denise(wideFrame, planes, new SpriteUnit(regs, null));
            for (int beamX = 62; beamX < 222; beamX++)
                wideDenise.Tick(new BeamTraceEntry { BeamX = beamX, BeamY = 44 });
            Check.Equal(0xFF0000FFu, wideFrame.SuperHiresPlayfieldPixels[1278],
                "The penultimate native pixel reaches the far edge of the 1280-pixel target.");
            Check.Equal(0xFFFF0000u, wideFrame.SuperHiresPlayfieldPixels[1279],
                "The final native pixel remains independently colored at the far edge.");
            denise.BeginFrame();
            Check.Equal(planes.BorderColorArgb, frame.SuperHiresPlayfieldPixels[1],
                "A fresh field clears the transient second native sample.");
            using var frameState = new MemoryStream();
            frame.SaveState(new BinaryWriter(frameState));
            frameState.Position = 0;
            frame.LoadState(new BinaryReader(frameState));
            Check.True(frame.SuperHiresPlayfieldPixels == null,
                "Legacy framebuffer restores discard transient native SHRES samples.");
            planes.ResolveSuperHiresPixelPair(1, 2, out uint agaFirst, out uint agaSecond);
            Check.Equal(0xFFFF0000u, agaFirst, "AGA's first native sample uses COLOR01.");
            Check.Equal(0xFF0000FFu, agaSecond, "AGA's second native sample uses COLOR02 independently.");
            using var state = new MemoryStream();
            planes.SaveState(new BinaryWriter(state));
            state.Position = 0;
            planes.LoadState(new BinaryReader(state));
            planes.RefreshFromRegisters();
            Check.Equal(1, planes.RenderSuperHiresColorIndex(0, 44), "AGA restored row retains its first native bit.");
            Check.Equal(1, planes.RenderSuperHiresColorIndex(1279, 44), "AGA restored row retains its final native bit.");
            regs.WriteWord(0x102, 0x0100); // One 35 ns odd-plane delay; even plane stays unscrolled.
            planes.RefreshFromRegisters();
            Check.Equal(0, planes.RenderSuperHiresColorIndex(0, 44),
                "AGA SHRES delays the odd plane by one native pixel at the left edge.");
            Check.Equal(3, planes.RenderSuperHiresColorIndex(1, 44),
                "AGA SHRES combines delayed odd and undelayed even bits independently.");
            Check.Equal(0, planes.RenderColorIndex(0, 44),
                "The host preview follows the native odd-plane delay.");
            regs.WriteWord(0x102, 0x1000); // One 35 ns even-plane delay; odd plane stays unscrolled.
            planes.RefreshFromRegisters();
            Check.Equal(1, planes.RenderSuperHiresColorIndex(0, 44),
                "The odd plane stays at the left edge when only the even plane is delayed.");
            Check.Equal(0, planes.RenderSuperHiresColorIndex(1, 44),
                "The even plane does not leak its first bit one pixel early.");
            Check.Equal(2, planes.RenderSuperHiresColorIndex(2, 44),
                "The even plane's first bit appears at its independently delayed position.");
            frame.RecordSuperHiresPlayfieldPair(0, 0xFFFF0000u, 0xFF0000FFu);
            regs.WriteWord(0x100, 0x2001); // Back to lores.
            denise.BeginFrame();
            Check.True(frame.SuperHiresPlayfieldPixels == null,
                "Leaving SHRES releases the extra native target before ordinary frames.");
        }

        private static void TestHiresDmaPhase()
        {
            foreach (var chipset in new[] { AmigaChipset.Ecs, AmigaChipset.Aga })
            foreach (bool preload in new[] { false, true })
            {
                var regs = new CustomRegisterMap(chipset);
                ushort Read(uint address)
                {
                    if (address >= 0x1000) return 0; // Second plane.
                    int word = (int)(address / 2);
                    if (!preload) return 0xFFFF;
                    // A previous row's final word and the next row's first
                    // word must stay outside a 640-pixel display window.
                    return word switch { 0 or 41 => 0xFFFF, 1 => 0x8000, 40 => 1, _ => 0 };
                }
                var planes = new BitplaneUnit(regs, Read);
                var frame = new AmigaFrameBuffer(704, 1);
                var denise = new Denise(frame, planes, new SpriteUnit(regs, null));
                regs.WriteWord(0x08E, 0x2C81); regs.WriteWord(0x090, 0x2DC1);
                regs.WriteWord(0x1E4, 0x2000);
                regs.WriteWord(0x092, preload ? (ushort)0x38 : (ushort)0x3C);
                regs.WriteWord(0x094, preload ? (ushort)0xD8 : (ushort)0xD4);
                regs.WriteWord(0x100, 0xA001); regs.WriteWord(0x096, 0x8300);
                regs.WriteWord(0x0E6, 0x1000); regs.WriteWord(0x182, 0x0FFF);
                planes.BeginFrame();
                for (int x = 0; x < 227; x++)
                {
                    if (planes.IsPlaneFetchSlot(x, 44)) planes.FetchDmaPlaneWord(x, 44);
                    denise.Tick(new BeamTraceEntry { BeamX = x, BeamY = 44 });
                }
                denise.FinishLineTail(new BeamTraceEntry { BeamX = 226, BeamY = 44 });
                for (int x = 0; x < 704; x++)
                {
                    bool white = x < 640 && (!preload || x == 0 || x == 639);
                    Check.Equal(white ? 0xFFFFFFFFu : 0xFF000000u, frame.Pixels[x],
                        $"{chipset}, preload={preload}, pixel={x}: completed DMA and DIW must agree.");
                }
            }
        }

        private static void TestExtendedScroll()
        {
            foreach (bool hires in new[] { false, true })
            {
                var regs = new CustomRegisterMap(AmigaChipset.Aga);
                var planes = new BitplaneUnit(regs, null, 2 * 1024 * 1024);
                regs.WriteWord(0x08E, 0x2C81); regs.WriteWord(0x090, 0x2DC1);
                regs.WriteWord(0x092, hires ? (ushort)0x3C : (ushort)0x38);
                regs.WriteWord(0x094, 0xD4);
                regs.WriteWord(0x100, hires ? (ushort)0xA001 : (ushort)0x2001);
                planes.NotifyCustomRegisterWordWrite(0x110, 0xA55A);
                planes.NotifyCustomRegisterWordWrite(0x112, 0x3CC3);
                for (int odd = 0; odd < 256; odd++)
                {
                    int even = 255 - odd;
                    // Encode the published PFxH7..0 register layout independently.
                    ushort value = (ushort)(((odd & 0x3C) >> 2) | ((odd & 3) << 8) | ((odd & 0xC0) << 4) |
                        ((even & 0x3C) << 2) | ((even & 3) << 12) | ((even & 0xC0) << 8));
                    regs.WriteWord(0x102, value); planes.RefreshFromRegisters();
                    for (int x = 0; x < 256; x++)
                    {
                        int first = (x * 2 - odd) >> (hires ? 1 : 2);
                        int second = (x * 2 - even) >> (hires ? 1 : 2);
                        int expected = first < 0 ? 0 : (0xA55A >> (15 - (first & 15))) & 1;
                        if (second >= 0) expected |= ((0x3CC3 >> (15 - (second & 15))) & 1) << 1;
                        Check.Equal(expected, planes.RenderColorIndex(x, 44), "35 ns delay and independent plane phase.");
                    }
                    if (odd == 129)
                    {
                        int beforeRestore = planes.RenderColorIndex(66, 44);
                        using var stream = new MemoryStream(); planes.SaveState(new BinaryWriter(stream));
                        stream.Position = 0; planes.LoadState(new BinaryReader(stream));
                        planes.RefreshFromRegisters();
                        Check.Equal(beforeRestore, planes.RenderColorIndex(66, 44), "Restore regenerates the fractional delay from the register.");
                    }
                }
            }
            // The compositor's lores pixel-pair cache must retain an odd
            // host-pixel transition caused by the sub-lores delay bits.
            var registers = new CustomRegisterMap(AmigaChipset.Aga);
            var bitplanes = new BitplaneUnit(registers, null);
            var sprites = new SpriteUnit(registers, null);
            var frame = new AmigaFrameBuffer(64, 256);
            var denise = new Denise(frame, bitplanes, sprites);
            registers.WriteWord(0x08E, 0x2C81); registers.WriteWord(0x090, 0x2DC1);
            registers.WriteWord(0x092, 0x38); registers.WriteWord(0x094, 0xD0);
            registers.WriteWord(0x100, 0x1001); registers.WriteWord(0x102, 0x0100);
            Color(registers, 0, 0); Color(registers, 1, 0xFF0000);
            bitplanes.NotifyCustomRegisterWordWrite(0x110, 0x8000); bitplanes.RefreshFromRegisters();
            denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = 44 });
            Check.Equal(0xFF000000u, frame.Pixels[0], "Fractional delay leaves the first sample blank.");
            Check.Equal(0xFFFF0000u, frame.Pixels[1], "Odd host sample begins the lores bit.");
            Check.Equal(0xFFFF0000u, frame.Pixels[2], "The delayed bit still spans two host samples.");
            Check.Equal(0xFF000000u, frame.Pixels[3], "The next bit begins on the correct phase.");
        }

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
                Check.True(sprites.TryRenderSuperHiresPixel(hostWidth * 2 - 1, 11,
                    out uint lastNative, out _, out _), "The final 35 ns sprite sample remains inside the wide latch.");
                Check.Equal(0xFFAABBCCu, lastNative,
                    "Native SHRES output reaches the final bit independently of the 70 ns preview.");
                Check.True(sprites.TryRenderSuperHiresPixel(hostWidth * 2 - 2, 11,
                    out uint penultimateNative, out _, out _), "The penultimate native sample remains visible.");
                Check.Equal(resolution == 3 ? 0xFF102030u : 0xFFAABBCCu, penultimateNative,
                    "SHRES sprites distinguish adjacent 35 ns samples only at native resolution.");
                Check.True(!sprites.TryRenderSuperHiresPixel(hostWidth * 2, 11,
                    out _, out _, out _), "The native sprite ends after its exact 35 ns width.");
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
