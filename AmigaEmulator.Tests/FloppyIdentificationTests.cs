// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Tests
{
    internal static class FloppyIdentificationTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase("Paula/Disk", "external DD identification works without motor spin-up or media", "Commodore HRM appendix H, device identification", TestIdentification);
            yield return new ChipTestCase("SaveState", "external drive identification survives restore", "Commodore external disk motor/ID interface", TestRestore);
            yield return new ChipTestCase("SaveState", "motor latch restore does not clock a synthetic select edge", "Commodore motor latch; system state dependency order", TestMotorRestore);
        }

        private static void Select(FloppyController c, int drive, bool motor, bool select)
        {
            byte lines = motor ? (byte)0x7F : (byte)0xFF;
            if (select) lines &= (byte)~(1 << (3 + drive));
            c.ApplyCiaBPort(lines, 0xFF);
        }

        // Appendix H's motor-on/off preamble, followed by 32 active-low RDY samples.
        // No emulated spin-up clocks are supplied: these are identification bits, not media-ready.
        private static uint ReadId(FloppyController c, int drive)
        {
            Select(c, drive, true, false);
            Select(c, drive, true, true);
            Select(c, drive, true, false);
            Select(c, drive, false, false);
            Select(c, drive, false, true);
            Select(c, drive, false, false);
            uint id = 0;
            for (int i = 0; i < 32; i++)
            {
                Select(c, drive, false, true);
                id = (id << 1) | ((c.BuildCiaAStatusBits() & FloppyController.CiaAReadyBit) == 0 ? 1u : 0u);
                Select(c, drive, false, false);
            }
            return id;
        }

        private static string NewDisk()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-drive-id-" + Guid.NewGuid().ToString("N") + ".adf");
            File.WriteAllBytes(path, new byte[AdfImage.StandardSize]);
            return path;
        }

        private static void TestIdentification()
        {
            string path = NewDisk();
            try
            {
                var controller = new FloppyController();
                for (int d = 1; d < 4; d++)
                {
                    Check.Equal(0u, ReadId(controller, d), "An unattached drive must not identify.");
                    controller.InsertDrive(d, AdfImage.Load(path), true);
                    Check.Equal(uint.MaxValue, ReadId(controller, d), "Attached standard DD drive must identify before spin-up.");
                    controller.EjectDrive(d);
                    Check.Equal(uint.MaxValue, ReadId(controller, d), "Removing media must not unplug the physical drive.");
                    Select(controller, d, false, true);
                    var drive = controller.GetDrive(d);
                    Check.True(!drive.HasDisk && !drive.Ready && !drive.CanReadFlux, "Drive ID must not manufacture readable media.");
                    Check.True((controller.BuildCiaAStatusBits() & FloppyController.CiaADiskChangeBit) == 0, "Empty attached drive must assert CHNG.");
                    Select(controller, d, true, false);
                    Select(controller, d, true, true);
                    Check.True((controller.BuildCiaAStatusBits() & FloppyController.CiaAReadyBit) != 0, "Motor-on empty drive must expose media-ready, not its ID bit.");
                    Select(controller, d, false, false);
                }
            }
            finally { File.Delete(path); }
        }

        private static void TestRestore()
        {
            string path = NewDisk();
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive(2, AdfImage.Load(path), true);
                controller.EjectDrive(2);
                Check.Equal(uint.MaxValue, ReadId(controller, 2), "Empty attached DF2 must identify.");
                using var saved = new MemoryStream();
                using (var writer = new BinaryWriter(saved, System.Text.Encoding.UTF8, true)) StateSerializer.WriteObjectFields(writer, controller);
                Select(controller, 2, true, true);
                saved.Position = 0;
                using (var reader = new BinaryReader(saved, System.Text.Encoding.UTF8, true)) StateSerializer.ReadObjectFields(reader, controller);
                Select(controller, 2, false, true);
                Check.True((controller.BuildCiaAStatusBits() & FloppyController.CiaAReadyBit) == 0, "Saved attachment/ID state must survive restoration.");
                Check.True(!controller.Drive2.MotorOn && !controller.Drive2.HasDisk, "Restored ID must retain the empty stopped drive.");
            }
            finally { File.Delete(path); }
        }

        private static void TestMotorRestore()
        {
            using var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Compatible.Clone());
            system.Bus.WriteByte(0xBFD300, 0xFF);
            system.Bus.WriteByte(0xBFD100, 0x77);
            system.Bus.WriteByte(0xBFD100, 0xF7);
            Check.True(system.Bus.FloppyController.Drive0.MotorOn, "Changing MOTOR while selected must retain the on latch.");
            using var saved = new MemoryStream();
            using (var writer = new BinaryWriter(saved, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
            system.Reset(true);
            saved.Position = 0;
            using (var reader = new BinaryReader(saved, System.Text.Encoding.UTF8, true)) system.LoadState(reader);
            Check.True(system.Bus.FloppyController.Drive0.Selected, "Saved CIA select must be restored.");
            Check.True(system.Bus.FloppyController.Drive0.MotorOn, "Loading bus state before CIA state must not manufacture a select edge and resample MOTOR high.");
            // The saved physical input level was high, independently of the latched motor.
            system.Bus.WriteByte(0xBFD100, 0xFF);
            system.Bus.WriteByte(0xBFD100, 0xF7);
            Check.True(!system.Bus.FloppyController.Drive0.MotorOn, "A subsequent real select edge must still clock the high MOTOR input.");
        }
    }
}
