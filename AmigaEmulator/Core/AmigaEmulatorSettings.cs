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
        /// <summary>
        /// Gets or sets Version.
        /// </summary>
        public int Version { get; set; } = 4;

        /// <summary>
        /// Gets or sets Video Filter.
        /// </summary>
        public AmigaVideoFilter VideoFilter { get; set; } = AmigaVideoFilter.Sharp;

        /// <summary>
        /// Gets or sets Upscale Mode.
        /// </summary>
        public AmigaUpscaleMode UpscaleMode { get; set; } = AmigaUpscaleMode.None;

        /// <summary>
        /// Gets or sets Render Fps.
        /// </summary>
        public AmigaRenderFps RenderFps { get; set; } = AmigaRenderFps.Native;

        /// <summary>
        /// Gets or sets RAM Profile.
        /// </summary>
        public AmigaRamProfile RamProfile { get; set; } = AmigaRamProfile.Chip1Mb;

        /// <summary>
        /// Gets or sets Kickstart.
        /// </summary>
        public AmigaKickstart Kickstart { get; set; } = AmigaKickstart.Auto;

        /// <summary>
        /// Gets or sets Custom Kickstart Path.
        /// </summary>
        public string CustomKickstartPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets Floppy Speed.
        /// </summary>
        public AmigaFloppySpeed FloppySpeed { get; set; } = AmigaFloppySpeed.Accurate;

        /// <summary>
        /// Gets or sets Paula Filter.
        /// </summary>
        public AmigaPaulaFilter PaulaFilter { get; set; } = AmigaPaulaFilter.A500;

        /// <summary>
        /// Gets or sets Port 1 Mode.
        /// </summary>
        public AmigaInputPortMode Port1Mode { get; set; } = AmigaInputPortMode.Mouse;

        /// <summary>
        /// Gets or sets Port 2 Mode.
        /// </summary>
        public AmigaInputPortMode Port2Mode { get; set; } = AmigaInputPortMode.Joystick;

        /// <summary>
        /// Gets or sets Reset Mode.
        /// </summary>
        public AmigaResetMode ResetMode { get; set; } = AmigaResetMode.Warm;

        /// <summary>
        /// Gets or sets Accuracy Profile.
        /// </summary>
        public AmigaAccuracyProfile AccuracyProfile { get; set; } = AmigaAccuracyProfile.Fast;

        /// <summary>
        /// Gets or sets Fullscreen.
        /// </summary>
        public bool Fullscreen { get; set; }

        /// <summary>
        /// Gets or sets Turbo Mode.
        /// </summary>
        public bool TurboMode { get; set; }

        /// <summary>
        /// Gets or sets Drive Overlay Enabled.
        /// </summary>
        public bool DriveOverlayEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets Media Browser Directory.
        /// </summary>
        public string MediaBrowserDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets Df 0 Path.
        /// </summary>
        public string Df0Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets Df 1 Path.
        /// </summary>
        public string Df1Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets Df 2 Path.
        /// </summary>
        public string Df2Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets Df 3 Path.
        /// </summary>
        public string Df3Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets Selected Save State Slot.
        /// </summary>
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
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        /// <summary>
        /// Runs Migrate.
        /// </summary>
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
        }
    }
}
