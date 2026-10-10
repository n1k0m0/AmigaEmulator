// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Media;

namespace AmigaEmulator
{
    public sealed partial class AmigaWindow
    {
        private int _hardDiskSelection;
        private static readonly int[] HardDiskSizes = { 16, 32, 64, 128, 256, 512, 1024, 2048 };

        private void MountConfiguredHardDisk()
        {
            var controller = _system.Bus.HardDisk;
            controller.Enabled = _system.Model.HasBuiltInHardDiskController || _settings.HardDiskEnabled;
            if (!controller.Enabled || string.IsNullOrWhiteSpace(_settings.HardDiskPath)) return;
            try { controller.Mount(_settings.HardDiskPath, _settings.HardDiskReadOnly); }
            catch (Exception ex)
            {
                _statusText = "HDF MOUNT FAILED: " + ex.Message;
                controller.Enabled = _system.Model.HasBuiltInHardDiskController;
            }
        }

        private void DrawHardDiskOverlay(Graphics graphics)
        {
            var disk = _system.Bus.HardDisk.Image;
            DrawMenu(graphics, "SETTINGS / HARD DISK - " + _system.Model.HardDiskControllerName, "ENTER SELECT  LEFT/RIGHT SIZE  ESC BACK", new[]
            {
                MenuLine("CONTROLLER", _system.Model.HasBuiltInHardDiskController ? "BUILT-IN " + _system.Model.HardDiskControllerName : _settings.HardDiskEnabled ? "ON - A600 IDE" : "OFF"),
                MenuLine("NEW IMAGE SIZE", _settings.NewHardDiskSizeMiB + " MiB"),
                MenuLine("CREATE IMAGE", "NEW HDF WITH DH0 PARTITION"),
                MenuLine("MOUNT IMAGE", disk == null ? "SELECT .HDF" : Path.GetFileName(disk.Path)),
                MenuLine("EJECT IMAGE", disk == null ? "EMPTY" : "FLUSH AND DISCONNECT"),
                MenuLine("WRITE PROTECTION", _settings.HardDiskReadOnly ? "READ ONLY" : "READ / WRITE"),
                MenuLine("BOOT FROM HD", _settings.BootFromHardDisk ? "ON - ENTER RESTARTS" : "OFF - ENTER RESTARTS"),
                MenuLine("KICKSTART", "SELECT ROM FOR THIS MODEL")
            }, _hardDiskSelection);
        }

        private void ChangeHardDiskItem(int direction)
        {
            if (_hardDiskSelection != 1) return;
            int index = Array.IndexOf(HardDiskSizes, _settings.NewHardDiskSizeMiB);
            _settings.NewHardDiskSizeMiB = HardDiskSizes[(Math.Max(0, index) + direction + HardDiskSizes.Length) % HardDiskSizes.Length];
            AmigaEmulatorSettingsStore.Save(_settings);
        }

        private void ActivateHardDiskItem()
        {
            try
            {
                switch (_hardDiskSelection)
                {
                    case 0:
                        if (_system.Model.HasBuiltInHardDiskController) { _statusText = _system.Model.HardDiskControllerName + " IS BUILT IN - EJECT TO REMOVE THE DRIVE"; return; }
                        _settings.HardDiskEnabled = !_settings.HardDiskEnabled;
                        ReconfigureHardDisk();
                        break;
                    case 1: ChangeHardDiskItem(1); break;
                    case 2:
                        using (var dialog = new SaveFileDialog { Title = "Neue Amiga-Festplatte anlegen", Filter = "Amiga-Festplattenimage (*.hdf)|*.hdf", DefaultExt = "hdf", AddExtension = true,
                            FileName = "AmigaHD.hdf", InitialDirectory = GetHardDiskDirectory(), OverwritePrompt = true })
                        {
                            if (dialog.ShowDialog(this) != DialogResult.OK) return;
                            HardDiskImage.Create(dialog.FileName, _settings.NewHardDiskSizeMiB);
                            _settings.HardDiskPath = dialog.FileName; _settings.HardDiskReadOnly = false; _settings.HardDiskEnabled = true;
                            ReconfigureHardDisk();
                            _statusText = "DH0 CREATED - FORMAT AND INSTALL WORKBENCH TO BOOT";
                        }
                        break;
                    case 3:
                        using (var dialog = new OpenFileDialog { Title = "Amiga-Festplattenimage einbinden", Filter = "Amiga-Festplattenimage (*.hdf)|*.hdf", InitialDirectory = GetHardDiskDirectory() })
                        {
                            if (dialog.ShowDialog(this) != DialogResult.OK) return;
                            if (!string.Equals(dialog.FileName, _system.Bus.HardDisk.Image?.Path, StringComparison.OrdinalIgnoreCase))
                                using (var probe = new HardDiskImage(dialog.FileName, true)) { }
                            _settings.HardDiskPath = dialog.FileName; _settings.HardDiskEnabled = true;
                            ReconfigureHardDisk();
                        }
                        break;
                    case 4:
                        _system.Bus.HardDisk.Eject(); _settings.HardDiskPath = string.Empty;
                        AmigaEmulatorSettingsStore.Save(_settings);
                        _system.Reset(false); ResetEmulationClock(); _statusText = "HDF EJECTED";
                        break;
                    case 5:
                        _settings.HardDiskReadOnly = !_settings.HardDiskReadOnly;
                        ReconfigureHardDisk();
                        break;
                    case 6:
                        _settings.BootFromHardDisk = !_settings.BootFromHardDisk;
                        if (_settings.BootFromHardDisk)
                        {
                            if (_system.Bus.HardDisk.Image == null) { _settings.BootFromHardDisk = false; _statusText = "MOUNT A HARD DISK FIRST"; return; }
                            ClearDeferredMediaMount();
                            for (int drive = 0; drive < 4; drive++) _system.Bus.FloppyController.EjectDrive(drive);
                            _mountedMedia = MountedMediaInfo.None;
                        }
                        AmigaEmulatorSettingsStore.Save(_settings);
                        _system.Reset(true); ResetAudioOutput(); ResetEmulationClock();
                        _statusText = _settings.BootFromHardDisk ? "BOOTING " + _system.Model.HardDiskControllerName + " - REQUIRES INSTALLED SYSTEM AND MATCHING ROM" : "HD BOOT PREFERENCE OFF";
                        break;
                    case 7: RefreshRomEntries(); ShowOverlay(OverlayMode.Roms); break;
                }
            }
            catch (Exception ex) { _statusText = "HARD DISK: " + ex.Message; }
        }

        private void ReconfigureHardDisk()
        {
            var controller = _system.Bus.HardDisk;
            string oldPath = controller.Image?.Path;
            bool oldReadOnly = controller.Image?.ReadOnly ?? true, oldEnabled = controller.Enabled;
            try
            {
                controller.Eject(); controller.Enabled = _system.Model.HasBuiltInHardDiskController || _settings.HardDiskEnabled;
                if (controller.Enabled && !string.IsNullOrWhiteSpace(_settings.HardDiskPath)) controller.Mount(_settings.HardDiskPath, _settings.HardDiskReadOnly);
            }
            catch
            {
                if (oldPath != null && controller.Image == null) controller.Mount(oldPath, oldReadOnly);
                controller.Enabled = oldEnabled; _settings.HardDiskEnabled = oldEnabled;
                _settings.HardDiskPath = oldPath ?? string.Empty; _settings.HardDiskReadOnly = oldReadOnly;
                throw;
            }
            AmigaEmulatorSettingsStore.Save(_settings);
            _system.Reset(true); ResetAudioOutput(); ResetEmulationClock();
            _statusText = "IDE CONFIGURATION APPLIED - MACHINE RESTARTED";
        }
        private static string GetHardDiskDirectory()
        {
            string directory = Path.Combine(UserDataPaths.GetBaseDirectory(), "harddisks");
            Directory.CreateDirectory(directory); return directory;
        }
    }
}
