using System;
using System.Diagnostics;
using System.IO;
using AmigaEmulator.Core;

namespace AmigaEmulator
{
    public sealed partial class AmigaWindow
    {
        private void RestoreBatteryClock()
        {
            if (!_system.Model.HasBatteryClock) return;
            try
            {
                if (_system.Model.HasRicohClock) _system.Bus.RicohClock.LoadBattery(GetBatteryClockPath());
                else _system.Bus.BatteryClock.LoadBattery(GetBatteryClockPath());
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
            { _statusText = "BATTERY CLOCK: " + ex.Message; Trace.TraceWarning(_statusText); }
        }

        private void SaveBatteryClock()
        {
            if (_system == null || !_system.Model.HasBatteryClock) return;
            try
            {
                if (_system.Model.HasRicohClock) _system.Bus.RicohClock.SaveBattery(GetBatteryClockPath());
                else _system.Bus.BatteryClock.SaveBattery(GetBatteryClockPath());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { _statusText = "BATTERY CLOCK SAVE FAILED: " + ex.Message; Trace.TraceWarning(_statusText); }
        }

        private string GetBatteryClockPath() => Path.Combine(UserDataPaths.GetBaseDirectory(), "clocks", _system.Model.Machine + ".rtc");

        private void MountConfiguredPcmcia()
        {
            if (_system.Model.HasPcmcia && _settings.PcmciaSramMiB != 0 && _system.Model.FastRamBytes <= 4 * 1024 * 1024)
                _system.Bus.Pcmcia.InsertSram(_settings.PcmciaSramMiB * 1024 * 1024);
        }

        private string FormatPcmciaSram() => !_system.Model.HasPcmcia ? "NO SOCKET" :
            _system.Model.FastRamBytes > 4 * 1024 * 1024 ? "UNAVAILABLE WITH 8 MiB FAST" :
            _system.Bus.Pcmcia.Inserted ? _system.Bus.Pcmcia.Size / (1024 * 1024) + " MiB SRAM" : "EMPTY";

        private void CyclePcmciaSram(int direction)
        {
            if (!_system.Model.HasPcmcia) { _statusText = "THIS MODEL HAS NO PCMCIA SOCKET"; return; }
            if (_system.Model.FastRamBytes > 4 * 1024 * 1024) { _statusText = "PCMCIA SRAM REQUIRES AT MOST 4 MiB FAST RAM"; return; }
            int[] sizes = { 0, 1, 2, 4 };
            int index = Math.Max(0, Array.IndexOf(sizes, _settings.PcmciaSramMiB));
            _settings.PcmciaSramMiB = sizes[(index + direction + sizes.Length) % sizes.Length];
            AmigaEmulatorSettingsStore.Save(_settings);
            RecreateSystemForCurrentProfile();
            _statusText = "PCMCIA " + FormatPcmciaSram() + " - MACHINE RESTARTED";
        }

        private void SynchronizeRestoredMachineSettings(bool persist)
        {
            _settings.MachineModel = _machineModel;
            _settings.RamProfile = GetSettingsRamProfile(_ramProfile);
            _settings.AccuracyProfile = _accuracyProfile;
            _settings.PcmciaSramMiB = _system.Bus.Pcmcia.Size / (1024 * 1024);
            _settings.HardDiskEnabled = _system.Bus.HardDisk.Enabled;
            _settings.HardDiskPath = _system.Bus.HardDisk.Image?.Path ?? string.Empty;
            _settings.HardDiskReadOnly = _system.Bus.HardDisk.Image?.ReadOnly ?? true;
            _selectedRomPath = string.Empty;
            foreach (var info in KickstartRomResolver.FindAvailableKickstartRoms())
                if (info.IsUsable && string.Equals(info.Sha1, _system.Bus.Kickstart.Sha1, StringComparison.OrdinalIgnoreCase))
                { _selectedRomPath = info.Path; break; }
            _settings.CustomKickstartPath = _selectedRomPath;
            _romStatusText = _system.Bus.Kickstart.Kind + " - SAVESTATE";
            if (persist) AmigaEmulatorSettingsStore.Save(_settings);
        }
    }
}
