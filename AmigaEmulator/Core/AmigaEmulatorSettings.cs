/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmigaEmulator.Core
{
    /// <summary>
    /// Contains persistent user settings for the Amiga frontend overlay.
    /// </summary>
    public sealed class AmigaEmulatorSettings
    {
        public int Version { get; set; } = 7;

        public AmigaMachineModel MachineModel { get; set; } = AmigaMachineModel.A500;
        public int PcmciaSramMiB { get; set; }
        public string RomSearchDirectory { get; set; } = string.Empty;

        public bool AudioEnabled { get; set; } = true;
        public float MasterVolume { get; set; } = 0.80f;
        public string NetworkTransportMode { get; set; } = "Lan";
        public int NetworkServerPort { get; set; } = 6464;
        public string NetworkServerPassword { get; set; } = string.Empty;
        public string NetworkClientHost { get; set; } = "127.0.0.1";
        public int NetworkClientPort { get; set; } = 6464;
        public int NetworkRelayPort { get; set; } = 6465;
        public string NetworkConnectionId { get; set; } = "amiga";
        public string NetworkRelayPassword { get; set; } = string.Empty;
        public string NetworkClientPassword { get; set; } = string.Empty;
        public string NetworkPlayerName { get; set; } = "player";
        public string NetworkRequestedRole { get; set; } = "Player";

        public void ApplyStartupModel(AmigaModel model)
        {
            MachineModel = model.Machine;
            RamProfile = AmigaModel.NormalizeRamProfile(MachineModel, GetRamProfile(model));
            Kickstart = AmigaKickstart.Auto; CustomKickstartPath = string.Empty;
        }

        private static AmigaRamProfile GetRamProfile(AmigaModel model)
        {
            foreach (var profile in Enum.GetValues<AmigaRamProfile>())
            {
                var info = AmigaOverlaySettingCatalog.GetRamProfileInfo(profile);
                if (info.ChipRamBytes == model.ChipRamBytes && info.SlowRamBytes == model.SlowRamBytes && info.FastRamBytes == model.FastRamBytes)
                    return profile;
            }
            throw new ArgumentException("Unknown startup memory layout.", nameof(model));
        }

        public AmigaVideoFilter VideoFilter { get; set; } = AmigaVideoFilter.Sharp;

        public AmigaUpscaleMode UpscaleMode { get; set; } = AmigaUpscaleMode.None;

        /// <summary>Stretches the image to fill the window instead of preserving its aspect ratio.</summary>
        public bool StretchToFullWidth { get; set; }

        public AmigaRenderFps RenderFps { get; set; } = AmigaRenderFps.Native;

        public AmigaMonitorAperture MonitorAperture { get; set; } = AmigaMonitorAperture.Crt;

        public AmigaRamProfile RamProfile { get; set; } = AmigaRamProfile.Chip1Mb;

        public bool HardDiskEnabled { get; set; }
        public string HardDiskPath { get; set; } = string.Empty;
        public bool HardDiskReadOnly { get; set; } = true;
        public int NewHardDiskSizeMiB { get; set; } = 256;
        public bool BootFromHardDisk { get; set; }

        public AmigaKickstart Kickstart { get; set; } = AmigaKickstart.Auto;

        public string CustomKickstartPath { get; set; } = string.Empty;

        public AmigaFloppySpeed FloppySpeed { get; set; } = AmigaFloppySpeed.Accurate;

        public AmigaPaulaFilter PaulaFilter { get; set; } = AmigaPaulaFilter.A500;

        public AmigaInputPortMode Port1Mode { get; set; } = AmigaInputPortMode.Mouse;

        public AmigaInputPortMode Port2Mode { get; set; } = AmigaInputPortMode.Joystick;

        public AmigaResetMode ResetMode { get; set; } = AmigaResetMode.Warm;

        public AmigaAccuracyProfile AccuracyProfile { get; set; } = AmigaAccuracyProfile.Compatible;

        public bool Fullscreen { get; set; }

        public bool TurboMode { get; set; }

        public bool DriveOverlayEnabled { get; set; } = true;

        public string MediaBrowserDirectory { get; set; } = string.Empty;

        public string Df0Path { get; set; } = string.Empty;

        public string Df1Path { get; set; } = string.Empty;

        public string Df2Path { get; set; } = string.Empty;

        public string Df3Path { get; set; } = string.Empty;

        public int SelectedSaveStateSlot { get; set; }

        /// <summary>
        /// Creates detached accuracy options from the selected profile.
        /// </summary>
        public AmigaAccuracyOptions CreateAccuracyOptions()
        {
            switch (AccuracyProfile)
            {
                case AmigaAccuracyProfile.Fast:
                    return AmigaAccuracyOptions.Fast.Clone();
                case AmigaAccuracyProfile.Strict:
                    return AmigaAccuracyOptions.Strict.Clone();
                case AmigaAccuracyProfile.Compatible:
                default:
                    return AmigaAccuracyOptions.Compatible.Clone();
            }
        }
    }

    /// <summary>
    /// Loads and saves Amiga settings from the per-user AppData directory.
    /// </summary>
    public static class AmigaEmulatorSettingsStore
    {
        /// <summary>
        /// Creates the Json Options.
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        /// <summary>
        /// Loads data into emulator state.
        /// </summary>
        public static AmigaEmulatorSettings Load()
        {
            string path = UserDataPaths.GetSettingsPath();
            try
            {
                if (!File.Exists(path))
                {
                    return new AmigaEmulatorSettings();
                }

                string json = File.ReadAllText(path);
                AmigaEmulatorSettings settings = JsonSerializer.Deserialize<AmigaEmulatorSettings>(json, JsonOptions);
                settings = settings ?? new AmigaEmulatorSettings();
                Migrate(settings);
                return settings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return new AmigaEmulatorSettings();
            }
        }

        /// <summary>
        /// Saves value from emulator state.
        /// </summary>
        public static void Save(AmigaEmulatorSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            string path = UserDataPaths.GetSettingsPath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Creates the Json Options.
        /// </summary>
        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            options.Converters.Add(new AmigaUpscaleModeJsonConverter());
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private static void Migrate(AmigaEmulatorSettings settings)
        {
            if (settings.Version < 2)
            {
                settings.AccuracyProfile = AmigaAccuracyProfile.Fast;
                settings.Version = 2;
            }

            if (settings.Version < 3)
            {
                settings.RamProfile = AmigaRamProfile.Chip512K;
                settings.Version = 3;
            }

            if (settings.Version < 4)
            {
                if (settings.RamProfile == AmigaRamProfile.Chip512K)
                {
                    settings.RamProfile = AmigaRamProfile.Chip1Mb;
                }

                settings.Version = 4;
            }

            if (settings.Version < 6)
            {
                settings.MachineModel = AmigaMachineModel.A500;
            }
            if (!Enum.IsDefined(settings.MachineModel)) settings.MachineModel = AmigaMachineModel.A500;
            if (!Enum.IsDefined(settings.RamProfile)) settings.RamProfile = AmigaRamProfile.Chip1Mb;
            settings.RamProfile = AmigaModel.NormalizeRamProfile(settings.MachineModel, settings.RamProfile);
            if (settings.PcmciaSramMiB != 0 && settings.PcmciaSramMiB != 1 && settings.PcmciaSramMiB != 2 && settings.PcmciaSramMiB != 4)
                settings.PcmciaSramMiB = 0;

            if (settings.Version < 5)
            {
                settings.MonitorAperture = AmigaMonitorAperture.Crt;
                settings.Version = 5;
            }
            settings.NetworkTransportMode = string.Equals(settings.NetworkTransportMode, "Relay", StringComparison.OrdinalIgnoreCase) ? "Relay" : "Lan";
            settings.RomSearchDirectory = settings.RomSearchDirectory?.Trim() ?? string.Empty;
            settings.NetworkServerPort = Math.Clamp(settings.NetworkServerPort, 1, 65535);
            settings.NetworkClientPort = Math.Clamp(settings.NetworkClientPort, 1, 65535);
            settings.NetworkRelayPort = Math.Clamp(settings.NetworkRelayPort, 1, 65535);
            settings.NetworkClientHost = string.IsNullOrWhiteSpace(settings.NetworkClientHost) ? "127.0.0.1" : settings.NetworkClientHost.Trim();
            settings.NetworkConnectionId = string.IsNullOrWhiteSpace(settings.NetworkConnectionId) ? "amiga" : settings.NetworkConnectionId.Trim();
            settings.NetworkPlayerName = string.IsNullOrWhiteSpace(settings.NetworkPlayerName) ? "player" : settings.NetworkPlayerName.Trim();
            settings.NetworkRequestedRole = string.Equals(settings.NetworkRequestedRole, "Observer", StringComparison.OrdinalIgnoreCase) ? "Observer" : "Player";
            settings.NetworkServerPassword ??= string.Empty;
            settings.NetworkClientPassword ??= string.Empty;
            settings.NetworkRelayPassword ??= string.Empty;
            settings.MasterVolume = float.IsFinite(settings.MasterVolume) ? Math.Clamp(settings.MasterVolume, 0f, 1.5f) : 0.8f;
            settings.Version = Math.Max(settings.Version, 7);
        }
    }
}
