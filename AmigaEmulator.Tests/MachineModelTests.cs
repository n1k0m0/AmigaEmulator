// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.CustomChips;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.SaveStates;
using AmigaEmulator.Core.Video;
using AmigaEmulator.Core.DevTools;

namespace AmigaEmulator.Tests
{
    internal static class MachineModelTests
    {
        private static readonly AmigaMachineModel[] LegacyMachines = { AmigaMachineModel.A500, AmigaMachineModel.A500Plus, AmigaMachineModel.A600, AmigaMachineModel.A2000Ocs, AmigaMachineModel.A2000Ecs };
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase("Models/AGA", "A1200 and A4000/030 profiles preserve CPU, buses, RAM and public save identity", "Motherboard integration", TestAgaModels);
            yield return new ChipTestCase("Models/Ricoh", "calendar banks, byte lanes, divider and battery RAM survive stop/reset/restore", "Ricoh EK-086-9804 pages 5/6", TestRicohClock);
            yield return new ChipTestCase("Models/Memory", "word reads preserve overlay, region boundaries, physical addressing and observers", "Memory decode optimization", TestMemoryWords);
            yield return new ChipTestCase("Models/Memory", "Chip RAM aliases prevent false extra-memory detection and preserve bus wiring", "CPU Chip RAM aperture", TestChipRamAliases);
            yield return new ChipTestCase("Models/Integration", "explicit startup models override stale frontend ROM and RAM settings", "GUI model selection", TestStartupModel);
            yield return new ChipTestCase("Models/Integration", "wrong RAM layouts are rejected before touching the machine", "Inner state preflight", TestRamStateMismatch);
            yield return new ChipTestCase("Models/DesktopBringup", "Ramsey RAM and Gary control latches remain independent and survive reset/restore", "Commodore A4000 service specification 6-32/33", TestDesktopResources);
            yield return new ChipTestCase("Models/A2000", "A2000 unmapped memory returns zero without shadowing installed devices", "B2000 open bus", TestA2000Unmapped);
            yield return new ChipTestCase("Models/RTC", "battery-backed date and held programming survive restart and reject corrupt storage", "Persistent clock and elapsed downtime", TestClockPersistence);
            yield return new ChipTestCase("Models/RTC", "weekday programming advances independently of the calendar", "MSM6242 weekday counter", TestClockWeekday);
            yield return new ChipTestCase("Models/RTC", "savestates preserve the subsecond divider without advancing through downtime", "Clock phase and guest pause", TestClockDivider);
            yield return new ChipTestCase("Models/PCMCIA", "A600 SRAM cards expose the correct apertures, CIS sizes and bus lanes", "Gayle common/attribute memory and reset", TestPcmciaMemory);
            yield return new ChipTestCase("Models/PCMCIA", "PCMCIA detect masks, acknowledgements and speculative accesses preserve hardware state", "Gayle control and level-2/6 interrupt routing", TestPcmciaControl);
            yield return new ChipTestCase("Models/PCMCIA", "card memory, controller state and pending resets survive savestates", "SRAM state, empty-slot migration and Fast RAM overlap", TestPcmciaState);
            yield return new ChipTestCase("Models/PCMCIA", "IDE and card interrupts acknowledge independently and ATA reset preserves the socket", "Shared Gayle registers", TestPcmciaIde);
            yield return new ChipTestCase("Models", "ECS models retain independent RAM and built-in IDE capabilities", "A500+, A600 model configuration", TestModels);
            yield return new ChipTestCase("Models", "ECS identification is read-only and survives reset", "DENISEID and VPOSR chipset revision", TestIdentification);
            yield return new ChipTestCase("Models/Video", "LOF toggles only with LACE and VPOSW survives save and restore", "Commodore HRM BPLCON0 and VPOSR/VPOSW", TestLongFieldFlop);
            yield return new ChipTestCase("Models/Video", "PAL fields follow 312/313 scanlines and restore the extra line before presentation", "Commodore HRM Copper vertical beam position and VPOSR/VPOSW", TestPalFieldLengths);
            yield return new ChipTestCase("Models/Video", "normal and profiled execution present only after the entire long PAL field", "Field completion through the machine compositor", TestLongFieldPresentation);
            yield return new ChipTestCase("Models/Video", "completed LACE fields weave independently and reset invalidates the pair", "PAL field completion and transient presentation", TestInterlacePresentation);
            yield return new ChipTestCase("Models", "ECS blitter size registers execute wider blits and BLTCON0L preserves channels", "BLTSIZV/H and BLTCON0L", TestExtendedBlitter);
            yield return new ChipTestCase("Models", "ECS DIWHIGH zero overrides legacy vertical wrap and survives restore", "DIWHIGH explicit-zero latch and model-aware savestates", TestDisplayWindowAndState);
            yield return new ChipTestCase("Models", "wrong-model savestates are rejected before changing machine state", "Model identity in file and machine state", TestStateMismatch);
            yield return new ChipTestCase("Models", "empty built-in IDE bus cannot be mistaken for an installed drive", "Gayle/ATA empty-bus status and device registers", TestEmptyIde);
            yield return new ChipTestCase("Models", "ECS horizontal DIWHIGH clips both window edges", "Nine-bit horizontal display window", TestHorizontalWindow);
            yield return new ChipTestCase("Models", "battery clock follows motherboard and retains guest programming across reset and restore", "MSM6242 bus lanes, planning and savestate", TestClockBus);
            yield return new ChipTestCase("Models", "battery clock rolls over dates and supports HOLD, STOP and 12-hour mode", "MSM6242 calendar and control registers", TestClockCalendar);
            yield return new ChipTestCase("Models", "version 4 machine states still load without battery-clock data", "Pre-RTC state migration", TestPreClockState);
            yield return new ChipTestCase("Models/ECS", "legacy display writes release DIWHIGH even when values are unchanged", "DIWHIGH interlock and byte writes", TestWindowInterlock);
            yield return new ChipTestCase("Models/ECS", "vertical display comparators retain an open window after a passed stop is programmed", "Raster splits, next-field stop/start and mid-window restore", TestVerticalWindowComparators);
            yield return new ChipTestCase("Models/Bitplanes", "DMA pointers retain their progress across scanlines without bitplane DMA", "Commodore HRM chapter 3; alternating-row dot-matrix display", TestDmaPointersAcrossBlankLines);
            yield return new ChipTestCase("Models/ECS", "border blank gates on ECSENA and respects both display-window edges", "ECS border rendering and empty-playfield fast path", TestBorderBlank);
            yield return new ChipTestCase("Models/ECS", "raster border changes apply immediately and preserve sprite palette colors", "Active playfield border and sprite composition", TestActiveBorderBlank);
            yield return new ChipTestCase("Models/ECS", "KILLEHB changes the palette immediately and remains ECS-only", "EHB disable, dual playfield and restore", TestKillEhb);
            yield return new ChipTestCase("Models/ECS", "HAM entering a blanked border retains COLOR00 as its initial held color", "HAM base at aligned and split-span DIW edges", TestHamBorderSeed);
            yield return new ChipTestCase("Models/A2000", "A2000 revision RAM rules preserve Zorro expansions and reject invalid layouts", "Revision capabilities, RAM mapping and model state", TestA2000Ram);
            yield return new ChipTestCase("Models/A2000", "A2000 CIA-A follows an independent 50 Hz clock and restores phase", "Mains TOD source and state migration", TestA2000Tod);
            yield return new ChipTestCase("Models/A2000", "A2000 savestate file records model and RAM and rejects a different revision", "Public savestate metadata and revision validation", TestA2000StateFile);
        }

        private static void TestLongFieldFlop()
        {
            foreach (var machine in new[] { AmigaMachineModel.A500, AmigaMachineModel.A600, AmigaMachineModel.A1200 })
            {
                var model = AmigaModel.Create(machine, AmigaModel.GetDefaultRamProfile(machine));
                var agnus = new Agnus(model);
                var registers = agnus.CustomRegisters;
                void Field()
                {
                    long frame = agnus.Beam.Frame;
                    do { agnus.Tick(); } while (agnus.Beam.Frame == frame);
                }
                ushort Lof() => (ushort)(registers.ReadWord(0x004) & 0x8000);
                Field(); agnus.Tick();
                Check.Equal((ushort)0, Lof(), "Non-laced output must not alternate LOF merely because a frame passed.");
                registers.WriteWord(0x02A, 0xFFFF); agnus.Tick();
                Check.Equal((ushort)0x8000, Lof(), "VPOSW sets the field flop.");
                Check.Equal(model.AgnusId, (ushort)(registers.ReadWord(0x004) & 0x7F00), "VPOSW cannot change chip identification.");
                registers.WriteWord(0x004, 0); registers.WriteWord(0x006, 0xFFFF);
                Check.Equal((ushort)0x8000, Lof(), "Writing the read port cannot change LOF.");
                Field(); Check.Equal((ushort)0x8000, Lof(), "Cleared LACE retains a programmed long field.");
                registers.WriteWord(0x100, 4);
                Field(); Check.Equal((ushort)0, Lof(), "LACE toggles at the next field boundary.");
                using var stream = new MemoryStream(); agnus.SaveState(new BinaryWriter(stream));
                Field(); Check.Equal((ushort)0x8000, Lof(), "Interlaced fields alternate.");
                stream.Position = 0; agnus.LoadState(new BinaryReader(stream));
                Check.Equal((ushort)0, Lof(), "Restore preserves the actual flop independently of host frame parity.");
                registers.WriteWord(0x100, 0);
                Field(); Check.Equal((ushort)0, Lof(), "Disabling LACE freezes the current field selection.");
                registers.WriteWord(0x02A, 0x8000);
                agnus.Reset(); agnus.Tick(); Check.Equal((ushort)0, Lof(), "Reset clears the saved field selection.");
            }
        }

        private static void TestPalFieldLengths()
        {
            foreach (var machine in new[] { AmigaMachineModel.A500, AmigaMachineModel.A600, AmigaMachineModel.A1200 })
            {
                var model = AmigaModel.Create(machine, AmigaModel.GetDefaultRamProfile(machine));
                var agnus = new Agnus(model); var registers = agnus.CustomRegisters;
                int NextField(Agnus target)
                {
                    long frame = target.Beam.Frame; int cycles = 0;
                    do
                    {
                        target.Tick();
                        if (++cycles > 314 * model.ChipCyclesPerLine) throw new InvalidOperationException("Field did not finish.");
                    } while (target.Beam.Frame == frame);
                    return cycles;
                }
                registers.WriteWord(0x100, 4); agnus.Tick();
                Check.Equal(312 * model.ChipCyclesPerLine, NextField(agnus), "The short PAL field has 312 scanlines.");
                Check.Equal(313 * model.ChipCyclesPerLine, NextField(agnus), "The following long field includes line 312.");
                Check.Equal(312 * model.ChipCyclesPerLine, NextField(agnus), "The third field is short again.");
                // We are now at the first clock of a long field. Its extra
                // scanline must remain in this field, including after restore.
                for (int i = 0; i < 312 * model.ChipCyclesPerLine; i++) agnus.Tick();
                Check.Equal(312, agnus.Beam.BeamY, "The extra line is observable on the beam counter.");
                Check.Equal((ushort)1, (ushort)(registers.ReadWord(0x004) & 1), "VPOSR retains the high vertical bit.");
                using var state = new MemoryStream(); agnus.SaveState(new BinaryWriter(state));
                var restored = new Agnus(model); state.Position = 0; restored.LoadState(new BinaryReader(state));
                Check.Equal(model.ChipCyclesPerLine, NextField(agnus), "The final long-field line completes before the next frame.");
                Check.Equal(model.ChipCyclesPerLine, NextField(restored), "Restore preserves the remaining clocks in that line.");
                using var expected = new MemoryStream(); using var actual = new MemoryStream();
                agnus.SaveState(new BinaryWriter(expected)); restored.SaveState(new BinaryWriter(actual));
                Check.True(expected.ToArray().AsSpan().SequenceEqual(actual.ToArray()), "Restored beam, DMA and field state match exactly.");
                registers.WriteWord(0x100, 0);
                registers.WriteWord(0x02A, 0x8000);
                Check.Equal(313 * model.ChipCyclesPerLine, NextField(agnus), "A programmed long field remains long with LACE cleared.");
                Check.Equal(313 * model.ChipCyclesPerLine, NextField(agnus), "Only automatic alternation depends on LACE.");
                registers.WriteWord(0x02A, 0);
                Check.Equal(312 * model.ChipCyclesPerLine, NextField(agnus), "A programmed short field is 312 lines.");
            }
        }

        private static void TestLongFieldPresentation()
        {
            foreach (bool profiled in new[] { false, true })
            {
                using var system = new AmigaSystem(AmigaModel.A500Pal512Slow512);
                system.Agnus.CustomRegisters.WriteWord(0x02A, 0x8000);
                // The old machine-level boundary presented after line 311,
                // even when Agnus had not yet completed its long field.
                const uint sentinel = 0xFF123456;
                system.PresentedFrameBuffer.Clear(sentinel);
                Advance(312 * system.Model.ChipCyclesPerLine);
                Check.True(Array.TrueForAll(system.PresentedFrameBuffer.Pixels, p => p == sentinel),
                    "The presentation must remain untouched while the extra scanline is pending.");
                Advance(system.Model.ChipCyclesPerLine - 1);
                Check.True(Array.TrueForAll(system.PresentedFrameBuffer.Pixels, p => p == sentinel),
                    "No partial field may be presented on the penultimate clock.");
                Advance(1);
                Check.True(Array.Exists(system.PresentedFrameBuffer.Pixels, p => p != sentinel),
                    "The completed field must be presented on its final clock.");
                void Advance(int clocks)
                {
                    if (profiled) system.ProfileChipCycles(clocks);
                    else system.RunChipCycles(clocks);
                }
            }
        }

        private static void TestInterlacePresentation()
        {
            using var system = new AmigaSystem(AmigaModel.A500Pal512Slow512);
            var registers = system.Agnus.CustomRegisters;
            registers.WriteWord(0x100, 4);
            registers.WriteWord(0x180, 0x00F);
            system.RunChipCycles(312 * system.Model.ChipCyclesPerLine);
            Check.True(!system.InterlacedFrameBuffer.HasBothFields,
                "A short field alone must not be reported as a woven image.");
            uint shortPixel = system.PresentedFrameBuffer.Pixels[0];
            registers.WriteWord(0x180, 0xF00);
            system.RunChipCycles(313 * system.Model.ChipCyclesPerLine);
            Check.True(system.InterlacedFrameBuffer.HasBothFields,
                "The following long field completes the PAL pair.");
            uint longPixel = system.PresentedFrameBuffer.Pixels[0];
            Check.Equal(longPixel, system.InterlacedFrameBuffer.Pixels[0],
                "The completed long field occupies even output scanlines.");
            Check.Equal(shortPixel, system.InterlacedFrameBuffer.Pixels[system.InterlacedFrameBuffer.Width],
                "The completed short field occupies odd output scanlines.");
            registers.WriteWord(0x100, 0);
            system.RunChipCycles(312 * system.Model.ChipCyclesPerLine);
            Check.True(system.CompletedInterlacedFrameBuffer == null,
                "Leaving LACE invalidates the complete pair at the next field boundary.");
            registers.WriteWord(0x100, 4);
            system.RunChipCycles(312 * system.Model.ChipCyclesPerLine);
            Check.True(system.CompletedInterlacedFrameBuffer == null,
                "Re-entering LACE cannot pair a new short field with an old long field.");
            system.RunChipCycles(313 * system.Model.ChipCyclesPerLine);
            Check.True(system.CompletedInterlacedFrameBuffer != null,
                "Only a fresh long field completes the re-entered LACE pair.");
            system.RunChipCycles(312 * system.Model.ChipCyclesPerLine);
            Check.True(system.CompletedInterlacedFrameBuffer != null,
                "An alternating short field may use the preceding long field.");
            registers.WriteWord(0x02A, 0);
            system.RunChipCycles(312 * system.Model.ChipCyclesPerLine);
            Check.True(system.CompletedInterlacedFrameBuffer == null,
                "A forced second short field cannot reuse a stale long field.");
            system.RunChipCycles(313 * system.Model.ChipCyclesPerLine);
            Check.True(system.CompletedInterlacedFrameBuffer != null,
                "A fresh long field completes the pair after the VPOSW override.");
            system.Reset(false);
            Check.True(!system.InterlacedFrameBuffer.HasBothFields,
                "A reset must not expose the previous field pair.");
        }

        private static void TestDmaPointersAcrossBlankLines()
        {
            foreach (var chipset in new[] { AmigaChipset.Ocs, AmigaChipset.Ecs, AmigaChipset.Aga })
            foreach (ushort fmode in chipset == AmigaChipset.Aga ? new ushort[] { 0, 1, 3 } : new ushort[] { 0 })
            {
                var regs = new CustomRegisterMap(chipset);
                var reads = new List<uint>();
                var planes = new BitplaneUnit(regs, address => { reads.Add(address); return 0xFFFF; });
                regs.WriteWord(0x08E, 0x2C81); regs.WriteWord(0x090, 0x3CC1);
                regs.WriteWord(0x092, 0x0038); regs.WriteWord(0x094, 0x0038);
                regs.WriteWord(0x0E2, 0x1000); regs.WriteWord(0x108, 8);
                regs.WriteWord(0x100, 0x1200); regs.WriteWord(0x096, 0x8300);
                regs.WriteWord(0x1FC, fmode);
                planes.BeginFrame();
                Fetch(44);
                int words = reads.Count;
                Check.True(words > 0, "The active scanline must fetch bitplane data.");
                Check.Equal(0x1000u, reads[0], "The first fetch starts at the programmed pointer.");
                regs.WriteWord(0x100, 0x0200);
                Fetch(45);
                Check.Equal(words, reads.Count, "A zero-plane scanline must not consume data or modulo.");
                regs.WriteWord(0x100, 0x1200);
                Fetch(46);
                Check.Equal(0x1000u + (uint)(words * 2 + 8), reads[words],
                    $"{chipset}/FMODE={fmode}: DMA resumes after the fetched words and one modulo, not at row zero.");
                regs.WriteWord(0x100, 0x0200);
                Fetch(47); Fetch(48); Fetch(49);
                Check.Equal(words * 2, reads.Count, "Several blank scanlines must leave the pointer untouched.");
                regs.WriteWord(0x100, 0x1200);
                Fetch(50);
                Check.Equal(0x1000u + (uint)(2 * (words * 2 + 8)), reads[words * 2],
                    "The next enabled line continues after exactly two completed fetches.");
                regs.WriteWord(0x0E2, 0x2000);
                Fetch(52);
                Check.Equal(0x2000u, reads[words * 3], "An explicit pointer rewrite still takes effect after a gap.");

                void Fetch(int line)
                {
                    for (int x = 0; x < 227; x++)
                        if (planes.IsPlaneFetchSlot(x, line)) planes.FetchDmaPlaneWord(x, line);
                }
            }
        }

        private static void TestChipRamAliases()
        {
            foreach (var model in new[] { AmigaModel.A500Pal512Slow512,
                AmigaModel.Create(AmigaMachineModel.A2000Ocs, AmigaRamProfile.Chip512KSlow512K),
                AmigaModel.Create(AmigaMachineModel.A500Plus, AmigaRamProfile.Chip1Mb),
                AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip2Mb) })
            {
                using var system = new AmigaSystem(model); var bus = system.Bus;
                bus.RomOverlayAtZero = false;
                uint size = (uint)model.ChipRamBytes;
                bus.WriteLong(0x100, 0x12345678);
                for (uint bank = 0; bank < 0x200000; bank += size)
                {
                    uint address = bank + 0x100;
                    Check.Equal(AmigaAddressRegion.ChipRam, bus.GetRegion(address), "CPU decode includes every alias.");
                    Check.Equal(0x12345678u, bus.ReadLong(address), "Every alias addresses the same physical cells.");
                    Check.Equal((ushort)0x1234, bus.PeekWord(address), "The optimized debugger read uses the same wiring.");
                    Check.True(bus.RequiresChipBus(address, 4), "Aliases still contend with custom-chip DMA.");
                    bus.WriteLong(address, 0xABCDEF01);
                    Check.Equal(0xABCDEF01u, bus.ReadLong(0x100), "A probe write must expose aliasing, not fictitious extra RAM.");
                    bus.WriteLong(0x100, 0x12345678);
                }
                bus.WriteByte(size - 1, 0xA5); bus.WriteByte(0, 0x5A);
                ushort boundary = size < 0x200000 ? (ushort)0xA55A : (ushort)0xA5FF;
                Check.Equal(boundary, bus.ReadWord(size - 1), "Physical banks wrap inside the aperture; its final boundary enters Zorro space.");
                Check.Equal(boundary, bus.PeekWord(size - 1), "The fast word path preserves both boundaries.");
                var reads = new List<uint>();
                bus.ReadObserved += (a, n, v, r) => reads.Add(a);
                bus.ReadWord(size - 1);
                Check.Equal(size - 1, reads[0], "Observers retain CPU addresses, not rewritten physical offsets.");
                Check.Equal(size, reads[1], "The second byte retains the alias address.");
                Check.True(bus.GetRegion(0x200000) != AmigaAddressRegion.ChipRam, "No Chip RAM alias enters Zorro II space.");
                bus.WriteByte(0xC00000, 0x39);
                Check.Equal((byte)0x5A, bus.PeekByte(0), "Slow RAM remains independent of CPU Chip RAM aliases.");
            }
        }

        private static void TestMemoryWords()
        {
            foreach (var model in new[] { AmigaModel.A500Pal512Slow512, AmigaModel.Create(AmigaMachineModel.A4000Ec030, AmigaRamProfile.Chip2MbFast4Mb) })
            {
                using var system = new AmigaSystem(model); var bus = system.Bus;
                uint fast = model.MotherboardFastRamStart;
                if (model.FastRamBytes != 0) bus.WriteLong(fast, 0x11223344);
                foreach (bool overlay in new[] { false, true })
                {
                    bus.RomOverlayAtZero = overlay;
                    foreach (uint address in new uint[] { 0, 1, 0x7FFFE, 0x7FFFF, (uint)model.ChipRamBytes - 1, 0xBFFFFF, 0xC00000,
                        0xC7FFFE, 0xC7FFFF, 0xF80000, 0xFFFFFE, 0xFFFFFF, 0xFFFFFFFF, fast, fast + 1, fast + (uint)model.FastRamBytes - 1 })
                    {
                        ushort expected = (ushort)((bus.PeekByte(address) << 8) | bus.PeekByte(unchecked(address + 1)));
                        Check.Equal(expected, bus.PeekWord(address), "Word peek must agree with byte wiring at every boundary.");
                        Check.Equal(expected, bus.ReadWord(address), "Unobserved word read.");
                        var reads = new List<(uint address, int size, uint value)>();
                        Action<uint, int, uint, AmigaAddressRegion> observer = (a, s, v, r) => reads.Add((a, s, v));
                        bus.ReadObserved += observer;
                        Check.Equal(expected, bus.ReadWord(address), "Observed word read remains equal.");
                        bus.ReadObserved -= observer;
                        Check.Equal(2, reads.Count, "Byte read observer semantics retained for memory words.");
                        Check.Equal(1, reads[0].size, "First observed byte.");
                        Check.Equal((uint)(expected >> 8), reads[0].value, "Big-endian high byte.");
                        Check.Equal((uint)(expected & 255), reads[1].value, "Big-endian low byte.");
                    }
                }
            }
        }

        private static void TestAgaModels()
        {
            foreach (var machine in new[] { AmigaMachineModel.A1200, AmigaMachineModel.A4000Ec030 })
            foreach (var ram in new[] { AmigaRamProfile.Chip2Mb, AmigaRamProfile.Chip2MbFast2Mb, AmigaRamProfile.Chip2MbFast4Mb, AmigaRamProfile.Chip2MbFast8Mb })
            {
                bool desktop = machine == AmigaMachineModel.A4000Ec030;
                var model = AmigaModel.Create(machine, ram);
                Check.Equal(AmigaChipset.Aga, model.Chipset, "AGA motherboard.");
                Check.Equal(desktop ? AmigaCpuModel.M68EC030 : AmigaCpuModel.M68EC020, model.Cpu, "Physical processor variant.");
                Check.Equal(desktop ? 25000000 : 14187580, model.CpuClockHz, "Processor clock independent of PAL beam.");
                Check.Equal(7093790, model.ChipMasterClockHz, "PAL DMA clock remains unchanged.");
                using var system = new AmigaSystem(model);
                Check.True(system.Bus.HardDisk.Enabled, "Motherboard IDE present even with an empty disk socket.");
                Check.Equal(desktop ? AmigaIdeModel.A4000 : AmigaIdeModel.Gayle, system.Bus.HardDisk.Model, "IDE wiring.");
                Check.Equal(!desktop, model.HasPcmcia, "Only A1200 has a PCMCIA socket.");
                Check.Equal(desktop, system.Bus.RicohClock.Enabled, "Desktop clock selected.");
                Check.Equal((ushort)0xFFF8, system.Bus.ReadWord(0xDFF07C), "Lisa identification.");
                Check.Equal(AmigaRamProfile.Chip2Mb, AmigaModel.NormalizeRamProfile(machine, AmigaRamProfile.Chip1Mb), "AGA boards have two MiB Chip RAM.");
                if (model.FastRamBytes != 0 && desktop)
                {
                    system.Bus.WriteLong(model.MotherboardFastRamStart, 0x12345678);
                    Check.Equal(0x12345678u, system.Bus.ReadLong(model.MotherboardFastRamStart), "32-bit high motherboard RAM.");
                    Check.Equal((byte)0xFF, system.Bus.ReadByte(0xE80000), "No fake Zorro II expansion for motherboard RAM.");
                }
                string path = Path.Combine(Path.GetTempPath(), "aga-profile-" + Guid.NewGuid().ToString("N") + ".amigasav");
                try
                {
                    SaveStateFile.Write(path, system, MountedMediaInfo.None, AmigaKickstart.Custom, ram, AmigaAccuracyProfile.Compatible, Array.Empty<uint>(), 0, 0);
                    Check.Equal(machine, SaveStateFile.ReadMetadata(path).MachineModel, "Public metadata reconstructs the exact motherboard.");
                    using var restored = new AmigaSystem(model);
                    SaveStateFile.Load(path, restored);
                    Check.Equal(model.Cpu, restored.Model.Cpu, "New CPU restore.");
                    if (model.FastRamBytes != 0 && desktop) Check.Equal(0x12345678u, restored.Bus.ReadLong(model.MotherboardFastRamStart), "Board RAM restored.");
                }
                finally { File.Delete(path); }
            }
        }

        private static void TestRicohClock()
        {
            var now = new DateTime(2024, 2, 28, 23, 59, 59).AddMilliseconds(500);
            var clock = new Rp5c01Clock(true, () => now);
            byte R(int i) => clock.ReadByte(0xDC0003u + (uint)i * 4);
            void W(int i, byte b) => clock.WriteByte(0xDC0003u + (uint)i * 4, b);
            Check.Equal((byte)0xFF, clock.ReadByte(0xDC0001), "Only fourth byte lane connected.");
            now = now.AddSeconds(1); Check.Equal((byte)9, R(7), "Leap day.");
            W(13, 0); W(0, 5); W(1, 4); now = now.AddDays(1);
            Check.Equal((byte)5, R(0), "Disabled timer preserves incomplete programming.");
            W(13, 8); now = now.AddSeconds(1); Check.Equal((byte)6, R(0), "Timer resumes guest date.");
            W(13, 9); W(10, 0); W(13, 8);
            Check.Equal((byte)0, R(4), "Ricoh represents midnight as zero in 12-hour mode.");
            now = now.AddHours(13); Check.Equal((byte)1, R(4), "Afternoon hours."); Check.Equal((byte)2, R(5), "PM flag is bit one.");
            W(13, 10); for (int i = 0; i < 13; i++) W(i, (byte)i);
            W(13, 11); for (int i = 0; i < 13; i++) W(i, (byte)(15 - i));
            using var state = new MemoryStream();
            using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) clock.SaveState(writer);
            W(0, 0); now = now.AddMilliseconds(300); state.Position = 0;
            using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true)) clock.LoadState(reader);
            Check.Equal((byte)15, R(0), "Second battery RAM bank restored.");
            W(13, 10); for (int i = 0; i < 13; i++) Check.Equal((byte)i, R(i), "First battery RAM bank independent.");
            W(13, 9); W(2, 7); W(15, 1); Check.Equal((byte)0, R(2), "Alarm reset clears alarm digits.");
            Check.Equal((byte)0, R(15), "Write-only reset register reads zero.");
            W(13, 10);
            string path = Path.Combine(Path.GetTempPath(), "ricoh-" + Guid.NewGuid().ToString("N") + ".rtc");
            try
            {
                var utc = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
                W(13, 8); clock.SaveBattery(path, utc);
                var restored = new Rp5c01Clock(true, () => now); restored.LoadBattery(path, utc.AddSeconds(10));
                Check.Equal((byte)6, restored.ReadByte(0xDC0003), "Ten seconds downtime advances seconds without losing RAM.");
                restored.WriteByte(0xDC0037, 11); Check.Equal((byte)15, restored.ReadByte(0xDC0003), "Persistent second RAM bank.");
                using var board = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A4000Ec030, AmigaRamProfile.Chip2MbFast4Mb));
                board.Bus.WriteByte(0xDC0037, 10); board.Bus.WriteLong(0xDC0000, 0xFFFFFF09);
                Check.Equal(0xFFFFFF09u, board.Bus.ReadLong(0xDC0000), "32-bit motherboard byte lane routing.");
                board.Bus.BeginInstructionPlanning(); board.Bus.WriteByte(0xDC0003, 7); board.Bus.EndInstructionPlanning();
                Check.Equal((byte)9, board.Bus.ReadByte(0xDC0003), "Planning cannot mutate battery RAM.");
                board.Reset(true); Check.Equal((byte)9, board.Bus.ReadByte(0xDC0003), "Battery RAM survives cold reset.");
            }
            finally { File.Delete(path); }
        }

        private static void TestStartupModel()
        {
            foreach (var machine in LegacyMachines)
            foreach (var ram in Enum.GetValues<AmigaRamProfile>())
            {
                if (AmigaModel.NormalizeRamProfile(machine, ram) != ram) continue;
                var settings = new AmigaEmulatorSettings { MachineModel = AmigaMachineModel.A500,
                    RamProfile = AmigaRamProfile.Chip2MbFast8Mb, Kickstart = AmigaKickstart.Kickstart13, CustomKickstartPath = "old.rom" };
                settings.ApplyStartupModel(AmigaModel.Create(machine, ram));
                Check.Equal(machine, settings.MachineModel, "CLI model must reach the GUI session.");
                Check.Equal(ram, settings.RamProfile, "Explicit memory layout must reach the GUI session.");
                Check.Equal(AmigaKickstart.Auto, settings.Kickstart, "Old ROM preference must not silently select a different motherboard ROM.");
                Check.Equal(string.Empty, settings.CustomKickstartPath, "Old explicit ROM belongs to the old model.");
            }
        }

        private static void TestDesktopResources()
        {
            foreach (var profile in new[] { AmigaRamProfile.Chip2MbFast2Mb, AmigaRamProfile.Chip2MbFast4Mb, AmigaRamProfile.Chip2MbFast8Mb })
            {
                var board = AmigaModel.Create(AmigaMachineModel.A600, profile)
                    .WithDevelopmentProcessor(AmigaCpuModel.M68EC030, 25000000).WithDevelopmentDesktopResources();
                using var system = new AmigaSystem(board); var bus = system.Bus;
                bus.RomOverlayAtZero = false;
                uint first = board.MotherboardFastRamStart, last = 0x07FFFFFF;
                Check.Equal(AmigaAddressRegion.FastRam, bus.GetRegion(first), "Motherboard RAM starts at the advertised physical address.");
                Check.Equal(AmigaAddressRegion.FastRam, bus.GetRegion(last), "Highest Ramsey RAM byte.");
                Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(first - 1), "No alias before the installed RAM.");
                Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(last + 1), "No alias beyond the installed RAM.");
                Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(0x200000), "Motherboard memory is not a Zorro II board.");
                Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(0xE80000), "No fake Autoconfig header for motherboard RAM.");
                bus.WriteByte(first, 0x12); bus.WriteByte(last, 0x34);
                Check.Equal((byte)0x12, bus.PeekByte(first), "First and last RAM bytes do not alias.");
                Check.Equal((byte)0x34, bus.PeekByte(last), "Last byte.");
                Check.True(!bus.RequiresChipBus(first, 4), "Ramsey accesses do not consume an Agnus slot.");
                Check.Equal((byte)0x0F, bus.ReadByte(0xDE0043), "Ramsey -07 revision.");
                bus.WriteByte(0xDE0043, 0); Check.Equal((byte)0x0F, bus.ReadByte(0xDE0043), "Revision is read-only.");
                bus.WriteByte(0xDE0003, 0xA5); bus.WriteByte(0xDE0001, 0x80);
                Check.Equal((byte)0xA5, bus.PeekByte(0xDE0103), "Register mirrors decode the lower address lines.");
                using (var state = new MemoryStream())
                {
                    system.SaveState(new BinaryWriter(state));
                    bus.WriteByte(0xDE0003, 0); bus.WriteByte(0xDE0001, 0);
                    state.Position = 0; system.LoadState(new BinaryReader(state));
                    Check.Equal((byte)0xA5, bus.ReadByte(0xDE0003), "Restored Ramsey control.");
                    Check.Equal((byte)0xFF, bus.ReadByte(0xDE0001), "Restored Gary enable flag.");
                    using var wrong = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, profile)
                        .WithDevelopmentProcessor(AmigaCpuModel.M68EC030, 25000000));
                    state.Position = 0; bool rejected = false;
                    try { wrong.LoadState(new BinaryReader(state)); } catch (InvalidDataException) { rejected = true; }
                    Check.True(rejected, "Different motherboard decoder cannot consume this RAM state.");
                }
                system.Reset(false); Check.Equal((byte)0xA5, bus.ReadByte(0xDE0003), "Warm reset preserves Ramsey configuration.");
                system.Reset(true); Check.Equal((byte)(profile == AmigaRamProfile.Chip2MbFast8Mb ? 0x28 : 0x20),
                    bus.ReadByte(0xDE0003), "Power cycle restores RAM geometry and refresh defaults.");
            }
        }

        private static void TestClockPersistence()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-rtc-" + Guid.NewGuid().ToString("N") + ".rtc");
            var host = new DateTime(2024, 2, 28, 23, 59, 59);
            var utc = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);
            try
            {
                var clock = new Msm6242Clock(true, () => host);
                host = host.AddMilliseconds(600); clock.SaveBattery(path, utc);
                var restored = new Msm6242Clock(true, () => host);
                restored.LoadBattery(path, utc.AddMilliseconds(500));
                Check.Equal((byte)9, restored.ReadByte(0xDC0019), "Downtime and divider phase cross into leap day.");
                Check.Equal((byte)2, restored.ReadByte(0xDC001D), "Day is February 29.");
                Check.Equal((byte)0, restored.ReadByte(0xDC0001), "Restored seconds rolled over.");
                host = host.AddMilliseconds(900);
                Check.Equal((byte)1, restored.ReadByte(0xDC0001), "Remaining divider phase continues after restart.");
                restored.WriteByte(0xDC0035, 1); restored.WriteByte(0xDC0001, 7);
                restored.SaveBattery(path, utc);
                var held = new Msm6242Clock(true, () => host); held.LoadBattery(path, utc.AddDays(5));
                Check.Equal((byte)7, held.ReadByte(0xDC0001), "HOLD preserves pending calendar digits through downtime.");
                Check.Equal((byte)1, held.ReadByte(0xDC0035), "HOLD remains active.");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                bool rejected = false;
                try { held.LoadBattery(path, utc); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "Corrupt clock file is rejected.");
                Check.Equal((byte)7, held.ReadByte(0xDC0001), "Rejected file does not change the live clock.");
            }
            finally { File.Delete(path); }
        }

        private static void TestClockWeekday()
        {
            var host = new DateTime(2024, 2, 28, 23, 59, 59);
            var clock = new Msm6242Clock(true, () => host);
            clock.WriteByte(0xDC0031, 6); // Deliberately differs from calendar weekday.
            host = host.AddSeconds(1);
            Check.Equal((byte)0, clock.ReadByte(0xDC0031), "Independent weekday wraps at midnight.");
            host = host.AddSeconds(15);
            Check.Equal((byte)0, clock.ReadByte(0xDC0031), "Other calendar updates do not rewrite weekday from DateTime.");
        }

        private static void TestPcmciaMemory()
        {
            foreach (int size in new[] { 1, 2, 4 })
            {
                using var system = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip1Mb));
                var bus = system.Bus; var card = bus.Pcmcia; card.InsertSram(size * 1024 * 1024);
                Check.Equal(AmigaAddressRegion.Pcmcia, bus.GetRegion(0x600000), "SRAM has a dedicated non-chip-bus aperture.");
                Check.True(!bus.RequiresChipBus(0x600000, 2), "Card does not consume Agnus slots.");
                bus.WriteWord(0x600000, 0xCAFE); bus.WriteByte((uint)(0x600000 + card.Size - 1), 0xA5);
                Check.Equal((ushort)0xCAFE, bus.ReadWord(0x600000), "Both common-memory byte lanes are connected.");
                Check.Equal((byte)0xA5, bus.ReadByte((uint)(0x600000 + card.Size - 1)), "Last SRAM byte is addressable.");
                if (size < 4) Check.Equal((byte)0xFF, bus.ReadByte((uint)(0x600000 + card.Size)), "No mirroring beyond card capacity.");
                Check.Equal((byte)1, bus.ReadByte(0xA00000), "CIS starts with DEVICE tuple on the even lane.");
                Check.Equal((byte)0xFF, bus.ReadByte(0xA00001), "Attribute odd lane is unconnected.");
                byte encoded = bus.ReadByte(0xA00006);
                int decoded = ((encoded >> 3) + 1) * 512 * (1 << (2 * (encoded & 7)));
                Check.Equal(card.Size, decoded, "CIS size describes the exact inserted memory.");
                bus.WriteByte(0xA00000, 0); Check.Equal((byte)1, bus.ReadByte(0xA00000), "CIS is read-only.");
                bus.WriteByte(0xA40000, 0); Check.Equal((byte)0xFF, bus.ReadByte(0x600000), "Card reset holds memory disconnected.");
                bus.PeekByte(0xA40000); Check.Equal((byte)0xFF, bus.ReadByte(0x600000), "Debug peek cannot release reset.");
                bus.ReadByte(0xA40000); Check.Equal((ushort)0xCAFE, bus.ReadWord(0x600000), "Reset release reconnects existing SRAM.");
                system.Reset(true); Check.Equal((ushort)0xCAFE, bus.ReadWord(0x600000), "Cold motherboard reset preserves SRAM.");
                card.Eject(); Check.Equal((byte)0xFF, bus.ReadByte(0x600000), "Empty socket returns open bus.");
                card.InsertSram(1024 * 1024, true); bus.WriteByte(0x600000, 5);
                Check.Equal((byte)0, bus.ReadByte(0x600000), "Write-protected cards reject writes.");
                Check.Equal((byte)0, (byte)(bus.ReadByte(0xDA8000) & 8), "Write protection appears in Gayle status.");
            }
        }

        private static void TestPcmciaControl()
        {
            using var system = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip1Mb));
            var bus = system.Bus; bus.Pcmcia.InsertSram(1024 * 1024);
            Check.Equal((byte)0x48, (byte)(bus.ReadByte(0xDA8000) & 0x48), "Card detection and write status are visible.");
            Check.True(!bus.Pcmcia.Level6Interrupt, "Detection is initially masked.");
            bus.WriteByte(0xDAA000, 0x40); Check.True(bus.Pcmcia.Level6Interrupt, "Detect change uses level 6.");
            system.RunChipCycles(1);
            Check.True((bus.ReadWord(0xDFF01E) & 0x2000) != 0, "Card detect reaches Paula EXTER interrupt request.");
            bus.WriteByte(0xDA9000, 0xBF); Check.True(!bus.Pcmcia.Level6Interrupt, "Writing zero acknowledges only that change bit.");
            bus.WriteByte(0xDAA000, 8); Check.True(bus.Pcmcia.Level2Interrupt, "Write-status change uses level 2.");
            bus.WriteByte(0xDA9000, 0); Check.True(!bus.Pcmcia.Level2Interrupt, "Acknowledgement clears the remaining change.");
            bus.WriteByte(0x600000, 0x12);
            bus.BeginInstructionPlanning(); bus.WriteByte(0xDA8000, 1); bus.WriteByte(0x600000, 0x34); bus.WriteByte(0xA40000, 0); bus.EndInstructionPlanning();
            Check.Equal((byte)0x12, bus.ReadByte(0x600000), "Planning cannot disable, reset or modify the card.");
            bus.WriteByte(0xDA8000, 1); Check.Equal((byte)0xFF, bus.ReadByte(0x600000), "DIS disconnects memory.");
            bus.WriteByte(0xDA8000, 0); Check.Equal((byte)0x12, bus.ReadByte(0x600000), "Clearing DIS reconnects SRAM.");
        }

        private static void TestPcmciaState()
        {
            using var system = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip2MbFast4Mb));
            system.Bus.Pcmcia.InsertSram(4 * 1024 * 1024); system.Bus.WriteWord(0x900000, 0xBEEF);
            system.Bus.WriteByte(0xDAA000, 0x48); system.Bus.WriteByte(0xA40000, 0);
            using var state = new MemoryStream();
            using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
            using var restored = new AmigaSystem(system.Model); state.Position = 0;
            using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true)) restored.LoadState(reader);
            Check.Equal(4 * 1024 * 1024, restored.Bus.Pcmcia.Size, "Restored card size follows the state.");
            Check.True(restored.Bus.Pcmcia.Level6Interrupt, "Pending detect and masks survive restore.");
            Check.Equal((byte)0xFF, restored.Bus.ReadByte(0x900000), "Pending card reset survives restore.");
            restored.Bus.ReadByte(0xA40000); Check.Equal((ushort)0xBEEF, restored.Bus.ReadWord(0x900000), "Restored SRAM keeps high addresses.");
            using (var version7 = new BinaryReader(new MemoryStream(LegacyState(state.ToArray(), 7, 8)))) restored.LoadState(version7);
            restored.Bus.ReadByte(0xA40000);
            Check.Equal((ushort)0xBEEF, restored.Bus.ReadWord(0x900000), "Version 7 cards load without a RAM identity header or RTC divider.");
            using var conflict = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip2MbFast8Mb));
            bool rejected = false; try { conflict.Bus.Pcmcia.InsertSram(1024 * 1024); } catch (InvalidOperationException) { rejected = true; }
            Check.True(rejected, "Overlapping Fast RAM/card configurations must be rejected.");
            using var absent = new AmigaSystem(AmigaModel.A500Pal); rejected = false;
            try { absent.Bus.Pcmcia.InsertSram(1024 * 1024); } catch (InvalidOperationException) { rejected = true; }
            Check.True(rejected, "Other motherboards cannot insert a PCMCIA card.");
            using var oldSource = new AmigaSystem(system.Model); using var oldState = new MemoryStream();
            using (var writer = new BinaryWriter(oldState, System.Text.Encoding.UTF8, true)) oldSource.SaveState(writer);
            byte[] legacy = LegacyState(oldState.ToArray(), 6, 19);
            using var legacyReader = new BinaryReader(new MemoryStream(legacy)); restored.LoadState(legacyReader);
            Check.True(!restored.Bus.Pcmcia.Inserted, "Pre-PCMCIA states restore an empty socket.");
        }

        private static void TestPcmciaIde()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-pcmcia-ide-" + Guid.NewGuid().ToString("N") + ".hdf");
            try
            {
                HardDiskImage.Create(path, 16);
                using var system = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip1Mb));
                var bus = system.Bus; bus.Pcmcia.InsertSram(1024 * 1024); bus.HardDisk.Mount(path, true);
                bus.WriteWord(0x600000, 0xCAFE); bus.WriteByte(0xDAA000, 0xC8);
                bus.WriteByte(0xDA001C, 0xEC); // IDENTIFY DEVICE.
                Check.True(bus.HardDisk.InterruptActive && bus.Pcmcia.Level6Interrupt, "IDE and card detect can be pending together.");
                Check.Equal((byte)0xC0, (byte)(bus.ReadByte(0xDA9000) & 0xC0), "IRQ register combines independent sources.");
                bus.WriteByte(0xDA9000, 0xBF);
                Check.True(bus.HardDisk.InterruptActive && !bus.Pcmcia.Level6Interrupt, "Acknowledging card detect preserves IDE IRQ.");
                bus.ReadByte(0xDA001C); bus.WriteByte(0xDA9000, 0x7F);
                Check.True(!bus.HardDisk.InterruptActive && bus.Pcmcia.Level2Interrupt, "IDE acknowledgement preserves card write-status change.");
                bus.WriteByte(0xDA1018, 4); bus.WriteByte(0xDA1018, 0); // ATA SRST assertion/release.
                Check.Equal((ushort)0xCAFE, bus.ReadWord(0x600000), "ATA reset cannot clear SRAM.");
                Check.True(bus.Pcmcia.Level2Interrupt, "ATA reset cannot acknowledge PCMCIA IRQ or clear its mask.");
            }
            finally { File.Delete(path); }
        }

        private static byte[] LegacyState(byte[] current, int version, int tailBytes)
        {
            using var state = new MemoryStream(); using var writer = new BinaryWriter(state);
            writer.Write(version); writer.Write(current, 4, 4);
            int header = BitConverter.ToInt32(current, 0) >= 18 ? 45 :
                BitConverter.ToInt32(current, 0) >= 14 ? 41 : 20;
            writer.Write(current, header, current.Length - header - tailBytes); // Strip RAM/CPU/board identity headers.
            return state.ToArray();
        }

        private static void TestClockDivider()
        {
            var host = new DateTime(2024, 2, 28, 23, 59, 59);
            var clock = new Msm6242Clock(true, () => host);
            host = host.AddMilliseconds(750);
            using var state = new MemoryStream();
            using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) clock.SaveState(writer, true);
            host = host.AddDays(3); state.Position = 0;
            var restored = new Msm6242Clock(true, () => host);
            using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true)) restored.LoadState(reader, true);
            Check.Equal((byte)9, restored.ReadByte(0xDC0001), "Clock state does not advance through savestate downtime.");
            host = host.AddMilliseconds(249); Check.Equal((byte)9, restored.ReadByte(0xDC0001), "Clock keeps the remaining fraction of its second.");
            host = host.AddMilliseconds(1); Check.Equal((byte)0, restored.ReadByte(0xDC0001), "Divider edge occurs at its restored phase.");
        }

        private static void TestRamStateMismatch()
        {
            foreach (var machine in LegacyMachines)
            {
                using var source = new AmigaSystem(AmigaModel.Create(machine, AmigaModel.GetDefaultRamProfile(machine)));
                var targetRam = machine == AmigaMachineModel.A2000Ocs ? AmigaRamProfile.Chip512KSlow512KFast4Mb : AmigaRamProfile.Chip2MbFast4Mb;
                using var target = new AmigaSystem(AmigaModel.Create(machine, targetRam));
                target.Bus.RomOverlayAtZero = false; target.Bus.WriteWord(0x400, 0xCAFE);
                using var state = new MemoryStream();
                using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) source.SaveState(writer);
                state.Position = 0; bool rejected = false;
                try { using var reader = new BinaryReader(state); target.LoadState(reader); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "RAM identity must be checked before device restore.");
                Check.Equal((ushort)0xCAFE, target.Bus.ReadWord(0x400), "Failed RAM preflight cannot modify guest memory.");
                Check.True(!target.Bus.RomOverlayAtZero, "Failed RAM preflight cannot change ROM overlay.");
            }
        }

        private static void TestA2000Unmapped()
        {
            foreach (var machine in LegacyMachines)
            {
                using var system = new AmigaSystem(AmigaModel.Create(machine, AmigaModel.GetDefaultRamProfile(machine)));
                var bus = system.Bus; bool a2000 = machine == AmigaMachineModel.A2000Ocs || machine == AmigaMachineModel.A2000Ecs;
                Check.Equal(a2000 ? 0u : 0xFFFFFFFFu, bus.ReadLong(0x500000), "Unmapped data follows motherboard pull-down behavior.");
                Check.Equal(a2000 ? (ushort)0 : (ushort)0xFFFF, bus.PeekWord(0x500000), "Debug reads follow the same mapping.");
                bus.WriteLong(0x500000, 0xFFFFFFFF);
                Check.Equal(a2000 ? (byte)0 : (byte)0xFF, bus.ReadByte(0x500000), "Unmapped writes cannot create RAM.");
                if (a2000) Check.Equal((byte)0xFF, bus.ReadByte(0xDC0000), "Clock's disconnected byte lane retains its device behavior.");
            }
        }

        private static void TestModels()
        {
            foreach (var machine in LegacyMachines)
            {
                var model = AmigaModel.Create(machine, AmigaModel.NormalizeRamProfile(machine, AmigaRamProfile.Chip2MbFast4Mb));
                Check.Equal(machine == AmigaMachineModel.A2000Ocs ? 512 * 1024 : 2 * 1024 * 1024, model.ChipRamBytes, "Chip RAM follows the selected Agnus revision.");
                Check.Equal(4 * 1024 * 1024, model.FastRamBytes, "Fast RAM expansion must remain independent.");
                Check.Equal(AmigaModel.GetChipset(machine), model.Chipset, "Model must select its real chipset.");
                using var system = new AmigaSystem(model);
                Check.Equal(machine == AmigaMachineModel.A600, system.Bus.HardDisk.Enabled, "Only A600 has IDE enabled by default.");
            }
            Check.Equal(AmigaRamProfile.Chip1Mb, AmigaModel.NormalizeRamProfile(AmigaMachineModel.A600, AmigaRamProfile.Chip512KSlow512K), "A600 has no A500 trapdoor Slow RAM.");
            bool rejected = false;
            try { AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip512K); }
            catch (ArgumentException) { rejected = true; }
            Check.True(rejected, "Invalid hardware combinations must be rejected by the core.");
        }

        private static void TestClockBus()
        {
            const uint control = 0xDC0035;
            foreach (var machine in LegacyMachines)
            {
                using var system = new AmigaSystem(AmigaModel.Create(machine, AmigaModel.GetDefaultRamProfile(machine)));
                bool hasClock = machine == AmigaMachineModel.A500Plus || machine == AmigaMachineModel.A2000Ocs || machine == AmigaMachineModel.A2000Ecs;
                Check.Equal(hasClock, system.Bus.BatteryClock.Enabled, "Clock follows motherboard capabilities.");
                Check.Equal(hasClock ? AmigaAddressRegion.BatteryClock : AmigaAddressRegion.Unmapped,
                    system.Bus.GetRegion(control), "Clock must be mapped according to hardware capabilities.");
                if (!hasClock) continue;
                system.Bus.WriteByte(control, 1);
                system.Bus.WriteWord(0xDC0000, 0xFF07); // Only low byte is connected.
                Check.Equal((byte)7, system.Bus.PeekByte(0xDC0003), "Both odd byte lanes alias the same nibble.");
                Check.Equal((byte)0xFF, system.Bus.ReadByte(0xDC0000), "Even byte lane is disconnected.");
                Check.Equal((byte)7, system.Bus.ReadByte(0xDC0041), "Register bank is mirrored across the clock aperture.");
                system.Bus.BeginInstructionPlanning();
                system.Bus.WriteByte(0xDC0001, 2);
                system.Bus.EndInstructionPlanning();
                Check.Equal((byte)7, system.Bus.ReadByte(0xDC0001), "Speculative CPU planning must not program the clock.");
                using var state = new MemoryStream();
                using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
                system.Bus.WriteByte(0xDC0001, 2);
                state.Position = 0;
                using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true)) system.LoadState(reader);
                Check.Equal((byte)7, system.Bus.ReadByte(0xDC0001), "Savestate restores pending held calendar writes.");
                system.Reset(false);
                Check.Equal((byte)7, system.Bus.ReadByte(0xDC0001), "Battery clock survives machine reset.");
                system.Reset(true);
                Check.Equal((byte)7, system.Bus.ReadByte(0xDC0001), "Battery clock survives cold reset as well.");
            }
        }

        private static void TestClockCalendar()
        {
            var now = new DateTime(2024, 2, 28, 23, 59, 59);
            var clock = new Msm6242Clock(true, () => now);
            byte Read(int index) => clock.ReadByte(0xDC0001u + (uint)index * 4);
            void Write(int index, byte value) => clock.WriteByte(0xDC0001u + (uint)index * 4, value);
            now = now.AddSeconds(1);
            Check.Equal((byte)9, Read(6), "Leap day must be reached at midnight.");
            Check.Equal((byte)2, Read(7), "Day tens must be correct.");
            Check.Equal((byte)0, Read(0), "Second must roll over.");
            Write(13, 1);
            now = now.AddHours(2);
            Check.Equal((byte)0, Read(4), "HOLD inhibits calendar counting.");
            Write(0, 5); Write(1, 4);
            Write(13, 0);
            now = now.AddSeconds(1);
            Check.Equal((byte)6, Read(0), "Guest-programmed seconds advance after releasing HOLD.");
            Write(15, 6);
            now = now.AddDays(1);
            Check.Equal((byte)6, Read(0), "STOP inhibits counting.");
            Write(15, 4);
            now = now.AddSeconds(1);
            Check.Equal((byte)7, Read(0), "STOP release resumes without counting stopped time.");
            Write(13, 8);
            Check.Equal((byte)0, Read(0), "30-second adjustment clears seconds.");
            Check.Equal((byte)1, Read(2), "30-second adjustment rounds up the minute.");
            Write(15, 0);
            Check.Equal((byte)2, Read(4), "Midnight uses twelve in 12-hour mode.");
            Check.Equal((byte)1, Read(5), "12-hour midnight must be AM.");
            now = now.AddHours(13);
            Check.Equal((byte)1, Read(4), "13:00 uses one in 12-hour mode.");
            Check.Equal((byte)4, Read(5), "12-hour afternoon sets the PM bit.");
            now = new DateTime(2025, 12, 31, 23, 59, 59);
            var yearClock = new Msm6242Clock(true, () => now);
            now = now.AddSeconds(1);
            Check.Equal((byte)1, yearClock.ReadByte(0xDC0019), "New year rolls over the day.");
            Check.Equal((byte)1, yearClock.ReadByte(0xDC0021), "New year rolls over the month.");
            Check.Equal((byte)6, yearClock.ReadByte(0xDC0029), "New year increments BCD year.");
        }

        private static void TestPreClockState()
        {
            using var system = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A500Plus, AmigaRamProfile.Chip1Mb));
            using var state = new MemoryStream();
            using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
            // v5 adds a timestamp and sixteen nibbles; v6 appends the TOD phase.
            byte[] legacy = LegacyState(state.ToArray(), 4, 47); // RTC (24), mains (4), empty PCMCIA (11), divider (8).
            system.Bus.WriteByte(0xDC0035, 1);
            system.Bus.WriteByte(0xDC0001, 3);
            using var input = new MemoryStream(legacy);
            using var reader = new BinaryReader(input);
            system.LoadState(reader);
            Check.Equal(input.Length, input.Position, "All v4 device state must be consumed without reading nonexistent RTC data.");
            Check.Equal((byte)3, system.Bus.ReadByte(0xDC0001), "Older states retain the existing battery clock.");
        }

        private static void TestIdentification()
        {
            foreach (var machine in LegacyMachines)
            {
                var agnus = new Agnus(AmigaModel.Create(machine, AmigaModel.GetDefaultRamProfile(machine)));
                ushort expected = AmigaModel.GetChipset(machine) == AmigaChipset.Ocs ? (ushort)0xFFFF : (ushort)0xFFFC;
                Check.Equal(expected, agnus.CustomRegisters.ReadWord(0x07C), "Denise revision must report OCS/ECS.");
                agnus.CustomRegisters.WriteWord(0x07C, 0);
                Check.Equal(expected, agnus.CustomRegisters.ReadWord(0x07C), "Guest cannot change chipset identification.");
                agnus.Reset(); agnus.Tick();
                Check.Equal(AmigaModel.GetChipset(machine) == AmigaChipset.Ocs ? (ushort)0 : (ushort)0x2000,
                    (ushort)(agnus.CustomRegisters.ReadWord(0x004) & 0x7F00), "PAL ECS Agnus revision must be visible to graphics.library.");
            }
        }

        private static void TestExtendedBlitter()
        {
            var agnus = new Agnus(AmigaModel.Create(AmigaMachineModel.A500Plus, AmigaRamProfile.Chip1Mb));
            ushort[] memory = new ushort[1024];
            agnus.Blitter.AttachChipMemory(address => memory[address / 2], (address, value) => memory[address / 2] = value);
            var regs = agnus.CustomRegisters;
            regs.WriteWord(0x040, 0x0100); // D enabled, no source channels.
            regs.WriteWord(0x05A, 0xFFFF); // Constant-one minterm, preserve D enable.
            Check.Equal((ushort)0x01FF, regs.ReadWord(0x040), "BLTCON0L must leave shift/channel bits intact.");
            regs.WriteWord(0x054, 0); regs.WriteWord(0x056, 0);
            regs.WriteWord(0x05C, 2); // Height alone must not start the blitter.
            agnus.Blitter.NotifyCustomRegisterWrite(0x05C, 2);
            Check.True(!agnus.Blitter.Busy, "BLTSIZV must not trigger a blit.");
            regs.WriteWord(0x05E, 80);
            agnus.Blitter.NotifyCustomRegisterWrite(0x05E, 2);
            for (int i = 0; i < 2000 && agnus.Blitter.DmaPending; i++) agnus.Blitter.Tick(true);
            Check.True(!agnus.Blitter.DmaPending, "Extended blit must finish.");
            for (int i = 0; i < 160; i++) Check.Equal((ushort)0xFFFF, memory[i], "All 80 words of both rows must be written.");
            Check.Equal((ushort)0, memory[160], "Blit must stop at the extended size.");
            var ocs = new CustomRegisterMap();
            ocs.WriteWord(0x040, 0x0100); ocs.WriteWord(0x05A, 0xFF);
            Check.Equal((ushort)0x0100, ocs.ReadWord(0x040), "OCS must ignore ECS-only BLTCON0L.");
        }

        private static void TestDisplayWindowAndState()
        {
            var model = AmigaModel.Create(AmigaMachineModel.A500Plus, AmigaRamProfile.Chip1Mb);
            using var system = new AmigaSystem(model);
            void Set(uint register, ushort value) { system.Bus.WriteWord(0xDFF000 + register, value); }
            Set(0x100, 0x1000); Set(0x110, 0xFFFF); Set(0x08E, 0x2C81); Set(0x090, 0x10C1);
            Check.True(system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "Before DIWHIGH, legacy stop $10 wraps to $110.");
            Set(0x1E4, 0);
            Check.True(!system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "Written zero must use explicit stop $10 rather than legacy wrap.");
            Set(0x1E4, 0x2100); // VSTOP high bit and HSTOP bit 8.
            Check.True(system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "Extended stop $110 must re-enable the display.");
            using var state = new MemoryStream();
            using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
            using var restored = new AmigaSystem(model);
            state.Position = 0;
            using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true)) restored.LoadState(reader);
            Check.True(restored.Agnus.CustomRegisters.DiwHighWritten, "DIWHIGH write history must survive savestates.");
            Check.True(restored.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "Restored ECS display geometry must match.");
            restored.Reset(true);
            Check.True(!restored.Agnus.CustomRegisters.DiwHighWritten, "Reset must restore legacy window decoding.");
            Set(0x1E4, 0);
            state.SetLength(0); state.Position = 0;
            using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
            state.Position = 0;
            using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true)) restored.LoadState(reader);
            Check.True(restored.Agnus.CustomRegisters.DiwHighWritten, "An explicitly written zero must remain written after restore.");
            Check.True(!restored.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "Restoring zero must not accidentally enable legacy vertical wrap.");
        }

        private static void TestStateMismatch()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-model-" + Guid.NewGuid().ToString("N") + ".amigasav");
            try
            {
                using var source = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip1Mb));
                SaveStateFile.Write(path, source, MountedMediaInfo.None, AmigaKickstart.Auto, AmigaRamProfile.Chip1Mb,
                    AmigaAccuracyProfile.Compatible, Array.Empty<uint>(), 0, 0);
                Check.Equal(AmigaMachineModel.A600, SaveStateFile.ReadMetadata(path).MachineModel, "Metadata must identify the hardware needed to load.");
                using var target = new AmigaSystem(AmigaModel.A500Pal1MbChip);
                target.Bus.RomOverlayAtZero = false; target.Bus.WriteWord(0x400, 0x1234);
                bool rejected = false;
                try { SaveStateFile.Load(path, target); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "A600 state must not load into A500.");
                Check.Equal((ushort)0x1234, target.Bus.ReadWord(0x400), "Rejected state must not modify the running machine.");
                using var wrongRam = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip2Mb));
                wrongRam.Bus.RomOverlayAtZero = false; wrongRam.Bus.WriteWord(0x400, 0x5678);
                rejected = false;
                try { SaveStateFile.Load(path, wrongRam); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "Same-model states with incompatible RAM must also be rejected.");
                Check.Equal((ushort)0x5678, wrongRam.Bus.ReadWord(0x400), "RAM mismatch must be detected before loading the machine.");
                using var correct = new AmigaSystem(source.Model);
                SaveStateFile.Load(path, correct);
                Check.True(correct.Bus.HardDisk.Enabled, "Correct-model restore must retain built-in IDE.");
            }
            finally { File.Delete(path); }
        }

        private static void TestEmptyIde()
        {
            using var system = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip1Mb));
            Check.Equal((byte)0xFF, system.Bus.ReadByte(0xDA201C), "Empty IDE bus status must reject drive detection.");
            Check.Equal((byte)0xFF, system.Bus.ReadByte(0xDA3018), "Alternate status must agree with status.");
            Check.Equal((byte)0, system.Bus.ReadByte(0xDA2008), "An absent device must not echo a sector-count register.");
            system.Bus.WriteByte(0xDA201C, 0xEC);
            Check.True(!system.Bus.HardDisk.InterruptActive, "IDENTIFY on an empty bus must not create a phantom interrupt.");
            Check.Equal((byte)0xFF, system.Bus.ReadByte(0xDA201C), "An absent drive stays absent after commands.");
        }

        private static void TestHorizontalWindow()
        {
            var regs = new CustomRegisterMap(AmigaChipset.Ecs);
            var planes = new BitplaneUnit(regs, null);
            void Set(uint offset, ushort value) { regs.WriteWord(offset, value); planes.NotifyCustomRegisterWordWrite(offset, value); }
            Set(0x100, 0x1000); Set(0x110, 0xFFFF);
            Set(0x08E, 0x2C81); Set(0x090, 0x2D91); Set(0x1E4, 0);
            Check.Equal(1, planes.RenderColorIndex(0, 44), "Explicit horizontal start $81 must include X0.");
            Check.Equal(1, planes.RenderColorIndex(31, 44), "The last pixel of the window must remain visible.");
            Check.Equal(0, planes.RenderColorIndex(32, 44), "The right edge is exclusive.");
            Set(0x08E, 0x2C85);
            Set(0x1E4, 0); // DIWSTRT releases the high-bit interlock; re-enable the explicit window.
            Check.Equal(0, planes.RenderColorIndex(7, 44), "Moving DIWSTRT must immediately clip the left edge.");
            Check.Equal(1, planes.RenderColorIndex(8, 44), "The new left edge must be visible.");
            Set(0x1E4, 0x2000);
            Check.Equal(1, planes.RenderColorIndex(32, 44), "HSTOP bit 8 must extend the window across $100.");
        }

        private static void TestVerticalWindowComparators()
        {
            foreach (var machine in new[] { AmigaMachineModel.A600, AmigaMachineModel.A1200 })
            {
                var model = AmigaModel.Create(machine, AmigaRamProfile.Chip2Mb);
                using var system = new AmigaSystem(model);
                void Set(uint offset, ushort value) => system.Bus.WriteWord(0xDFF000 + offset, value);
                void Line(int y)
                {
                    while (system.Agnus.Beam.BeamY != y || system.Agnus.Beam.BeamX != 0)
                        system.Agnus.Tick();
                }
                Set(0x100, 0x1000); Set(0x110, 0xFFFF);
                Set(0x08E, 0x2C81); Set(0x090, 0xF4C1);
                Line(60);
                Check.True(system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(60), "The start comparator at line 44 opened the display.");
                Set(0x090, 0x2BC1); Set(0x1E4, 0x2000);
                Line(100);
                Check.True(system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "A stop moved behind the beam must not erase the open display.");
                using var state = new MemoryStream();
                using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
                using var restored = new AmigaSystem(model);
                state.Position = 0;
                using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true)) restored.LoadState(reader);
                Check.True(restored.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "The vertical comparator latch must survive a mid-window restore.");
                using var legacy = new MemoryStream();
                using (var writer = new BinaryWriter(legacy, System.Text.Encoding.UTF8, true))
                    StateSerializer.WriteObjectFields(writer, system.Agnus.Bitplanes,
                        "_registers", "_readChipWord", "_decodedRegistersValid", "_decodedControlKey",
                        "_decodedWindowKey", "_decodedDiwHighKey", "_fetchRowsMonotonic",
                        "_verticalComparatorLine", "_verticalWindowActive");
                legacy.Position = 0;
                using (var reader = new BinaryReader(legacy, System.Text.Encoding.UTF8, true)) restored.Agnus.Bitplanes.LoadState(reader);
                Check.True(!restored.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "An old snapshot must not inherit a different running machine's open latch.");
                Line(43);
                Check.True(!system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(43), "Vertical blank resets the latch before the next start.");
                Line(44);
                Check.True(system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(44), "The next start reopens the display.");
                Set(0x090, 0x2EC1); Set(0x1E4, 0x2000);
                Line(46);
                Check.True(!system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(46), "A future stop comparator still closes the window normally.");
                Set(0x08E, 0x2E81); Set(0x1E4, 0x2000);
                Line(47); Line(46);
                Check.True(!system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(46), "Coincident start/stop comparators give the stop priority.");
            }
        }

        private static void TestWindowInterlock()
        {
            using var system = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip1Mb));
            void Set(uint offset, ushort value) => system.Bus.WriteWord(0xDFF000 + offset, value);
            Set(0x100, 0x1000); Set(0x110, 0xFFFF);
            Set(0x08E, 0x2C81); Set(0x090, 0x10C1); Set(0x1E4, 0);
            Check.True(!system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "Explicit zero disables the wrapped display.");
            Set(0x090, 0x10C1);
            Check.True(!system.Agnus.CustomRegisters.DiwHighWritten, "Writing the same DIWSTOP value releases the interlock.");
            Check.True(system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "Legacy vertical wrap resumes immediately.");
            Set(0x1E4, 0);
            system.Bus.WriteByte(0xDFF08F, 0x81);
            Check.True(!system.Agnus.CustomRegisters.DiwHighWritten, "Byte writes to DIWSTRT also release the interlock.");
            Check.True(system.Agnus.Bitplanes.IsBitplaneDisplayActiveOnLine(100), "Byte-write geometry changes reach the renderer.");
        }

        private static void TestBorderBlank()
        {
            foreach (var chipset in new[] { AmigaChipset.Ocs, AmigaChipset.Ecs })
            {
                var regs = new CustomRegisterMap(chipset);
                var planes = new BitplaneUnit(regs, null);
                var frame = new AmigaFrameBuffer(640, 256);
                var denise = new Denise(frame, planes, new SpriteUnit(regs, null));
                regs.WriteWord(0x180, 0x0F00);
                regs.WriteWord(0x08E, 0x2C85); regs.WriteWord(0x090, 0x2D91); regs.WriteWord(0x1E4, 0);
                regs.WriteWord(0x106, 0xFFFF);
                Check.Equal(chipset == AmigaChipset.Ecs ? (ushort)0x0035 : (ushort)0, regs.ReadWord(0x106), "ECS must not expose AGA-only BPLCON3 controls.");
                regs.WriteWord(0x100, 0);
                denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = 44 });
                Check.Equal(0xFFFF0000u, frame.Pixels[0], "ECSENA=0 ignores border blank even if BPLCON3 is set.");
                regs.WriteWord(0x100, 1);
                planes.RefreshFromRegisters();
                for (int x = 0x3E; x < 0x49; x++) denise.Tick(new BeamTraceEntry { BeamX = x, BeamY = 44 });
                if (chipset == AmigaChipset.Ocs)
                {
                    Check.Equal(0xFFFF0000u, frame.Pixels[0], "OCS retains COLOR00 border.");
                    continue;
                }
                Check.Equal(0xFF000000u, frame.Pixels[7], "Left border is blanked.");
                Check.Equal(0xFFFF0000u, frame.Pixels[8], "Empty playfield inside DIW retains COLOR00.");
                Check.Equal(0xFFFF0000u, frame.Pixels[31], "Last interior pixel retains COLOR00.");
                Check.Equal(0xFF000000u, frame.Pixels[32], "Right border is blanked.");
                denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = 45 });
                Check.Equal(0xFF000000u, frame.Pixels[640], "Rows below the vertical window are blanked.");
                regs.WriteWord(0x106, 0);
                denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = 45 });
                Check.Equal(0xFFFF0000u, frame.Pixels[640], "Raster writes disabling blank immediately restore COLOR00.");
            }
        }

        private static void TestActiveBorderBlank()
        {
            var regs = new CustomRegisterMap(AmigaChipset.Ecs);
            var planes = new BitplaneUnit(regs, null);
            var sprites = new SpriteUnit(regs, null);
            var frame = new AmigaFrameBuffer(640, 256);
            var denise = new Denise(frame, planes, sprites);
            regs.WriteWord(0x08E, 0x2C85); regs.WriteWord(0x090, 0x2D91); regs.WriteWord(0x1E4, 0);
            regs.WriteWord(0x100, 0x1001); regs.WriteWord(0x104, 0x0038);
            regs.WriteWord(0x180, 0x0F00); regs.WriteWord(0x182, 0x000F); regs.WriteWord(0x1A2, 0x00F0);
            planes.NotifyCustomRegisterWordWrite(0x110, 0xFFFF);
            regs.WriteWord(0x140, 0x2C42); regs.WriteWord(0x142, 0x2D00); regs.WriteWord(0x144, 0xFFFF);
            denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = 44 });
            Check.Equal(0xFFFF0000u, frame.Pixels[0], "Unblanked border initially uses COLOR00.");
            regs.WriteWord(0x106, 0x20);
            denise.Tick(new BeamTraceEntry { BeamX = 0x3F, BeamY = 44 });
            denise.Tick(new BeamTraceEntry { BeamX = 0x40, BeamY = 44 });
            Check.Equal(0xFF000000u, frame.Pixels[4], "Raster blank applies to the next span.");
            Check.Equal(0xFF00FF00u, frame.Pixels[8], "Blank does not suppress an interior sprite or its palette.");
            denise.Tick(new BeamTraceEntry { BeamX = 0x46, BeamY = 44 });
            Check.Equal(0xFF000000u, frame.Pixels[32], "A sprite crossing the right DIW edge is clipped into the blank border.");
        }

        private static void TestKillEhb()
        {
            foreach (var machine in new[] { AmigaMachineModel.A500, AmigaMachineModel.A600 })
            {
                using var system = new AmigaSystem(AmigaModel.Create(machine, AmigaRamProfile.Chip1Mb));
                void Set(uint offset, ushort value) => system.Bus.WriteWord(0xDFF000 + offset, value);
                Set(0x100, 0x6000); Set(0x182, 0x0FFF);
                Check.Equal(0xFF7F7F7Fu, system.Agnus.Bitplanes.ReadColorArgb(33), "Six-plane display starts in EHB.");
                Set(0x104, 0x0200);
                uint expected = machine == AmigaMachineModel.A600 ? 0xFFFFFFFFu : 0xFF7F7F7Fu;
                Check.Equal(expected, system.Agnus.Bitplanes.ReadColorArgb(33), "ECS KILLEHB suppresses the intensity reduction; OCS ignores it.");
                using var state = new MemoryStream();
                using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
                Set(0x104, 0);
                Check.Equal(0xFF7F7F7Fu, system.Agnus.Bitplanes.ReadColorArgb(33), "Clearing KILLEHB refreshes EHB palette immediately.");
                state.Position = 0;
                using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true)) system.LoadState(reader);
                Check.Equal(expected, system.Agnus.Bitplanes.ReadColorArgb(33), "Restore rebuilds the correct EHB palette.");
                if (machine == AmigaMachineModel.A600)
                {
                    Set(0x104, 0); Set(0x100, 0x6400);
                    Check.Equal(0xFFFFFFFFu, system.Agnus.Bitplanes.ReadColorArgb(33), "Dual playfield does not activate EHB.");
                }
            }
        }

        private static void TestHamBorderSeed()
        {
            foreach (int left in new[] { 6, 8 })
            {
                var regs = new CustomRegisterMap(AmigaChipset.Ecs);
                var planes = new BitplaneUnit(regs, null);
                var frame = new AmigaFrameBuffer(640, 256);
                var denise = new Denise(frame, planes, new SpriteUnit(regs, null));
                regs.WriteWord(0x08E, (ushort)(0x2C81 + left / 2)); regs.WriteWord(0x090, 0x2D91); regs.WriteWord(0x1E4, 0);
                regs.WriteWord(0x100, 0x6801); regs.WriteWord(0x106, 0x20); regs.WriteWord(0x180, 0x0F00);
                for (int plane = 0; plane < 6; plane++)
                    planes.NotifyCustomRegisterWordWrite(0x110u + (uint)plane * 2, (0x1A & (1 << plane)) != 0 ? (ushort)0xFFFF : (ushort)0);
                for (int beamX = 0x3E; beamX <= 0x40; beamX++)
                    denise.Tick(new BeamTraceEntry { BeamX = beamX, BeamY = 44 });
                Check.Equal(0xFF000000u, frame.Pixels[left - 1], "Border before HAM playfield is black.");
                Check.Equal(0xFFFF00AAu, frame.Pixels[left], "First HAM blue modification retains COLOR00 red despite the black border.");
            }
        }

        private static void TestA2000Ram()
        {
            foreach (var machine in new[] { AmigaMachineModel.A2000Ocs, AmigaMachineModel.A2000Ecs })
            foreach (var profile in Enum.GetValues<AmigaRamProfile>())
            {
                var normalized = AmigaModel.NormalizeRamProfile(machine, profile);
                var input = AmigaOverlaySettingCatalog.GetRamProfileInfo(profile);
                var model = AmigaModel.Create(machine, normalized);
                Check.Equal(input.FastRamBytes, model.FastRamBytes, "Switching revisions retains the Zorro expansion capacity.");
                Check.Equal(normalized, AmigaModel.NormalizeRamProfile(machine, normalized), "Normalization must be stable.");
                if (normalized != profile)
                {
                    bool rejected = false;
                    try { AmigaModel.Create(machine, profile); } catch (ArgumentException) { rejected = true; }
                    Check.True(rejected, "Core must reject rather than silently normalize an invalid explicit configuration.");
                }
                using var system = new AmigaSystem(model);
                system.Bus.RomOverlayAtZero = false;
                system.Bus.WriteWord(0x400, 0x1234);
                Check.Equal((ushort)0x1234, system.Bus.ReadWord(0x400), "Chip RAM is writable.");
                if (model.FastRamBytes > 0)
                {
                    system.Bus.WriteByte(0xE8004A, 0); system.Bus.WriteByte(0xE80048, 0x20);
                    system.Bus.WriteWord(0x200000, 0x5678);
                    Check.Equal((ushort)0x5678, system.Bus.ReadWord(0x200000), "Zorro Fast RAM is independently writable after Autoconfig.");
                }
                using var state = new MemoryStream();
                using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
                using var restored = new AmigaSystem(model);
                state.Position = 0;
                using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true)) restored.LoadState(reader);
                Check.Equal((ushort)0x1234, restored.Bus.ReadWord(0x400), "Revision-specific state roundtrip retains RAM.");
                Check.True(restored.Bus.BatteryClock.Enabled && !restored.Bus.HardDisk.Enabled, "A2000 has a clock but no motherboard IDE.");
            }
        }

        private static void TestA2000Tod()
        {
            using var system = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A2000Ocs, AmigaRamProfile.Chip512KSlow512K));
            system.CiaA.WriteByte(0xA00, 0); system.CiaA.WriteByte(0x900, 0); system.CiaA.WriteByte(0x800, 0);
            // E-clock resolution: first 50 Hz edge is at chip tick 70940.
            system.RunChipCycles(70935);
            Check.Equal((byte)0, system.CiaA.ReadByte(0x800), "A2000 TOD must not increment at the earlier PAL VSYNC edge.");
            using var state = new MemoryStream();
            using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
            system.RunChipCycles(5);
            Check.Equal((byte)1, system.CiaA.ReadByte(0x800), "Mains TOD increments at 50 Hz independently of video.");
            state.Position = 0;
            using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true)) system.LoadState(reader);
            system.RunChipCycles(5);
            Check.Equal((byte)1, system.CiaA.ReadByte(0x800), "Restored phase reproduces the imminent TOD edge.");
        }

        private static void TestA2000StateFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-a2000-" + Guid.NewGuid().ToString("N") + ".amigasav");
            try
            {
                using var source = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A2000Ocs, AmigaRamProfile.Chip512KSlow512KFast8Mb));
                SaveStateFile.Write(path, source, MountedMediaInfo.None, AmigaKickstart.Kickstart13,
                    AmigaRamProfile.Chip512KSlow512KFast8Mb, AmigaAccuracyProfile.Compatible, Array.Empty<uint>(), 0, 0);
                var metadata = SaveStateFile.ReadMetadata(path);
                Check.True(metadata.HasState, "New revision metadata must be recognized.");
                Check.Equal(AmigaMachineModel.A2000Ocs, metadata.MachineModel, "File header records the exact motherboard revision.");
                Check.Equal(AmigaRamProfile.Chip512KSlow512KFast8Mb, metadata.RamProfile, "File header records the appended RAM-profile enum value.");
                using var restored = new AmigaSystem(AmigaModel.Create(metadata.MachineModel, metadata.RamProfile));
                SaveStateFile.Load(path, restored);
                using var different = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A2000Ecs, AmigaRamProfile.Chip2MbFast8Mb));
                different.Bus.RomOverlayAtZero = false; different.Bus.WriteWord(0x400, 0xCAFE);
                bool rejected = false;
                try { SaveStateFile.Load(path, different); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "An OCS state must not load into the ECS revision.");
                Check.Equal((ushort)0xCAFE, different.Bus.ReadWord(0x400), "Revision mismatch is rejected before changing RAM.");
            }
            finally { File.Delete(path); }
        }
    }
}
