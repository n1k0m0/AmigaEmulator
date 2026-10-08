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
using System.Collections.Generic;

namespace AmigaEmulator.Core
{
    /// <summary>
    /// Gets a selectable overlay option.
    /// </summary>
    public sealed class AmigaSettingOption<T>
        where T : struct
    {
        /// <summary>
        /// Initializes a new instance of the AmigaSettingOption class.
        /// </summary>
        public AmigaSettingOption(T value, string label, string description)
        {
            Value = value;
            Label = label ?? string.Empty;
            Description = description ?? string.Empty;
        }

        /// <summary>
        /// Gets the Value.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Gets the Label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Gets the Description.
        /// </summary>
        public string Description { get; }
    }

    /// <summary>
    /// Gets an Amiga RAM layout selected by the overlay.
    /// </summary>
    public sealed class AmigaRamProfileInfo
    {
        /// <summary>
        /// Initializes a new instance of the AmigaRamProfileInfo class.
        /// </summary>
        public AmigaRamProfileInfo(
            AmigaRamProfile profile,
            string label,
            int chipRamBytes,
            int slowRamBytes,
            int fastRamBytes)
        {
            Profile = profile;
            Label = label ?? string.Empty;
            ChipRamBytes = chipRamBytes;
            SlowRamBytes = slowRamBytes;
            FastRamBytes = fastRamBytes;
        }

        /// <summary>
        /// Gets the Profile.
        /// </summary>
        public AmigaRamProfile Profile { get; }

        /// <summary>
        /// Gets the Label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Gets the Chip RAM Bytes.
        /// </summary>
        public int ChipRamBytes { get; }

        /// <summary>
        /// Gets the Slow RAM Bytes.
        /// </summary>
        public int SlowRamBytes { get; }

        /// <summary>
        /// Gets the Fast RAM Bytes.
        /// </summary>
        public int FastRamBytes { get; }
    }

    /// <summary>
    /// Provides stable option lists and labels for Amiga overlay menus.
    /// </summary>
    public static class AmigaOverlaySettingCatalog
    {
        /// <summary>
        /// Constant for K 512.
        /// </summary>
        private const int K512 = 512 * 1024;
        /// <summary>
        /// Constant for M 1.
        /// </summary>
        private const int M1 = 1024 * 1024;

        /// <summary>
        /// Stores Video Filter Options.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaVideoFilter>[] VideoFilterOptions =
        {
            /// <summary>
            /// Gets pixels.
            /// </summary>
            new AmigaSettingOption<AmigaVideoFilter>(AmigaVideoFilter.Sharp, "SHARP", "Unfiltered square pixels."),
            /// <summary>
            /// Gets presentation.
            /// </summary>
            new AmigaSettingOption<AmigaVideoFilter>(AmigaVideoFilter.Crt, "CRT", "Scanline CRT presentation."),
            /// <summary>
            /// Gets presentation.
            /// </summary>
            new AmigaSettingOption<AmigaVideoFilter>(AmigaVideoFilter.Tv, "TV", "Softer composite-style presentation.")
        };

        /// <summary>
        /// Stores Upscale Options.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaUpscaleMode>[] UpscaleOptions =
        {
            /// <summary>
            /// Gets resolution.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.None, "NONE", "Use the source resolution."),
            /// <summary>
            /// Gets upscale.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Scale2x, "2X SHARP", "Nearest-neighbor 2x source upscale."),
            /// <summary>
            /// Gets upscale.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Scale3x, "3X SHARP", "Nearest-neighbor 3x source upscale."),
            /// <summary>
            /// Gets upscale.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Hq2x, "2X SMOOTH", "Bicubic 2x source upscale."),
            /// <summary>
            /// Gets upscale.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Hq3x, "3X SMOOTH", "Bicubic 3x source upscale."),
            /// <summary>
            /// Gets upscale.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Hq4x, "4X SMOOTH", "Bicubic 4x source upscale."),
            /// <summary>
            /// Gets upscale.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Xbrz, "4X SOFT", "Bicubic 4x source upscale for legacy xBRZ settings."),
            /// <summary>
            /// Gets upscale.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Clean4, "4X SHARP", "Nearest-neighbor 4x source upscale.")
        };

        /// <summary>
        /// Renders Fps Options into the current frame buffer.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaRenderFps>[] RenderFpsOptions =
        {
            /// <summary>
            /// Gets standard.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Native, "NATIVE", "Use the current Amiga video standard."),
            /// <summary>
            /// Gets second.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Hz50, "50 HZ", "Cap rendering at 50 frames per second."),
            /// <summary>
            /// Gets second.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Hz60, "60 HZ", "Cap rendering at 60 frames per second."),
            /// <summary>
            /// Gets second.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Hz100, "100 HZ", "Cap rendering at 100 frames per second."),
            /// <summary>
            /// Gets second.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Hz120, "120 HZ", "Cap rendering at 120 frames per second."),
            /// <summary>
            /// Gets loop.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Unlimited, "UNLIMIT", "Do not cap the frontend render loop.")
        };

        /// <summary>
        /// Stores Ram Profiles.
        /// </summary>
        private static readonly AmigaRamProfileInfo[] RamProfiles =
        {
            /// <summary>
            /// Runs Amiga Ram Profile Info.
            /// </summary>
            new AmigaRamProfileInfo(AmigaRamProfile.Chip512K, "512K CHIP", K512, 0, 0),
            /// <summary>
            /// Runs Amiga Ram Profile Info.
            /// </summary>
            new AmigaRamProfileInfo(AmigaRamProfile.Chip512KSlow512K, "512K+512K", K512, K512, 0),
            /// <summary>
            /// Runs Amiga Ram Profile Info.
            /// </summary>
            new AmigaRamProfileInfo(AmigaRamProfile.Chip1Mb, "1MB CHIP", M1, 0, 0),
            /// <summary>
            /// Runs Amiga Ram Profile Info.
            /// </summary>
            new AmigaRamProfileInfo(AmigaRamProfile.Chip2Mb, "2MB CHIP", 2 * M1, 0, 0),
            /// <summary>
            /// Runs Amiga Ram Profile Info.
            /// </summary>
            new AmigaRamProfileInfo(AmigaRamProfile.Chip2MbFast8Mb, "2MB+8MB", 2 * M1, 0, 8 * M1),
            new AmigaRamProfileInfo(AmigaRamProfile.Chip1MbFast2Mb, "1MB CHIP + 2MB FAST", M1, 0, 2 * M1),
            new AmigaRamProfileInfo(AmigaRamProfile.Chip1MbFast4Mb, "1MB CHIP + 4MB FAST", M1, 0, 4 * M1),
            new AmigaRamProfileInfo(AmigaRamProfile.Chip2MbFast2Mb, "2MB CHIP + 2MB FAST", 2 * M1, 0, 2 * M1),
            new AmigaRamProfileInfo(AmigaRamProfile.Chip2MbFast4Mb, "2MB CHIP + 4MB FAST", 2 * M1, 0, 4 * M1)
        };

        /// <summary>
        /// Stores Kickstart Options.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaKickstart>[] KickstartOptions =
        {
            /// <summary>
            /// Gets model.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Auto, "AUTO", "Use the best available ROM for the selected model."),
            /// <summary>
            /// Gets Kickstart.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart11, "KS 1.1", "Kickstart 1.1."),
            /// <summary>
            /// Gets Kickstart.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart12, "KS 1.2", "Kickstart 1.2."),
            /// <summary>
            /// Gets Kickstart.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart13, "KS 1.3", "Kickstart 1.3."),
            /// <summary>
            /// Gets Kickstart.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart20, "KS 2.0", "Kickstart 2.0."),
            /// <summary>
            /// Gets Kickstart.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart30, "KS 3.0", "Kickstart 3.0."),
            /// <summary>
            /// Gets Kickstart.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart31, "KS 3.1", "Kickstart 3.1."),
            /// <summary>
            /// Gets path.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Custom, "CUSTOM", "Use the configured custom ROM path.")
        };

        /// <summary>
        /// Stores Floppy Speed Options.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaFloppySpeed>[] FloppySpeedOptions =
        {
            /// <summary>
            /// Gets timing.
            /// </summary>
            new AmigaSettingOption<AmigaFloppySpeed>(AmigaFloppySpeed.Accurate, "ACCURATE", "Use hardware-like disk timing."),
            /// <summary>
            /// Gets time.
            /// </summary>
            new AmigaSettingOption<AmigaFloppySpeed>(AmigaFloppySpeed.Fast, "FAST", "Reduce normal disk wait time."),
            /// <summary>
            /// Gets timing.
            /// </summary>
            new AmigaSettingOption<AmigaFloppySpeed>(AmigaFloppySpeed.Turbo, "TURBO", "Use aggressive fast-loading disk timing."),
            /// <summary>
            /// Gets immediately.
            /// </summary>
            new AmigaSettingOption<AmigaFloppySpeed>(AmigaFloppySpeed.Instant, "INSTANT", "Complete supported disk waits immediately.")
        };

        /// <summary>
        /// Stores Paula Filter Options.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaPaulaFilter>[] PaulaFilterOptions =
        {
            /// <summary>
            /// Gets filtering.
            /// </summary>
            new AmigaSettingOption<AmigaPaulaFilter>(AmigaPaulaFilter.Off, "OFF", "Disable host-side Paula filtering."),
            /// <summary>
            /// Gets filtering.
            /// </summary>
            new AmigaSettingOption<AmigaPaulaFilter>(AmigaPaulaFilter.A500, "A500", "A500-style low-pass filtering."),
            /// <summary>
            /// Gets filtering.
            /// </summary>
            new AmigaSettingOption<AmigaPaulaFilter>(AmigaPaulaFilter.A1200, "A1200", "A1200-style brighter filtering."),
            /// <summary>
            /// Gets filter.
            /// </summary>
            new AmigaSettingOption<AmigaPaulaFilter>(AmigaPaulaFilter.Led, "LED", "Apply the stronger LED filter.")
        };

        /// <summary>
        /// Stores Input Port Options.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaInputPortMode>[] InputPortOptions =
        {
            /// <summary>
            /// Gets port.
            /// </summary>
            new AmigaSettingOption<AmigaInputPortMode>(AmigaInputPortMode.None, "NONE", "No host input on this port."),
            /// <summary>
            /// Gets mouse.
            /// </summary>
            new AmigaSettingOption<AmigaInputPortMode>(AmigaInputPortMode.Mouse, "MOUSE", "Use the host mouse."),
            /// <summary>
            /// Gets controls.
            /// </summary>
            new AmigaSettingOption<AmigaInputPortMode>(AmigaInputPortMode.Joystick, "JOYSTICK", "Use keyboard joystick controls."),
            /// <summary>
            /// Gets gamepad.
            /// </summary>
            new AmigaSettingOption<AmigaInputPortMode>(AmigaInputPortMode.Gamepad, "GAMEPAD", "Use a host gamepad."),
            /// <summary>
            /// Gets controls.
            /// </summary>
            new AmigaSettingOption<AmigaInputPortMode>(AmigaInputPortMode.KeyboardJoystick, "KEY JOY", "Use keyboard joystick controls.")
        };

        /// <summary>
        /// Resets the Mode Options state.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaResetMode>[] ResetModeOptions =
        {
            /// <summary>
            /// Gets state.
            /// </summary>
            new AmigaSettingOption<AmigaResetMode>(AmigaResetMode.Warm, "WARM", "Reset without clearing all host-side state."),
            /// <summary>
            /// Gets media.
            /// </summary>
            new AmigaSettingOption<AmigaResetMode>(AmigaResetMode.Reload, "RELOAD", "Reset and reload mounted media."),
            /// <summary>
            /// Gets media.
            /// </summary>
            new AmigaSettingOption<AmigaResetMode>(AmigaResetMode.Power, "POWER", "Power-cycle and remount configured media.")
        };

        /// <summary>
        /// Stores Accuracy Profile Options.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaAccuracyProfile>[] AccuracyProfileOptions =
        {
            /// <summary>
            /// Gets timing.
            /// </summary>
            new AmigaSettingOption<AmigaAccuracyProfile>(AmigaAccuracyProfile.Fast, "FAST", "Prefer responsiveness over detailed timing."),
            /// <summary>
            /// Gets use.
            /// </summary>
            new AmigaSettingOption<AmigaAccuracyProfile>(AmigaAccuracyProfile.Compatible, "COMPAT", "Balanced timing profile for normal use."),
            /// <summary>
            /// Gets detail.
            /// </summary>
            new AmigaSettingOption<AmigaAccuracyProfile>(AmigaAccuracyProfile.Strict, "STRICT", "Prefer strict timing detail.")
        };

        /// <summary>
        /// Gets Video Filters.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaVideoFilter>> VideoFilters
        {
            get { return VideoFilterOptions; }
        }

        /// <summary>
        /// Gets Upscale Modes.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaUpscaleMode>> UpscaleModes
        {
            get { return UpscaleOptions; }
        }

        /// <summary>
        /// Renders Fps Modes into the current frame buffer.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaRenderFps>> RenderFpsModes
        {
            get { return RenderFpsOptions; }
        }

        /// <summary>
        /// Gets RAM Profile Infos.
        /// </summary>
        public static IReadOnlyList<AmigaRamProfileInfo> RamProfileInfos
        {
            get { return RamProfiles; }
        }

        /// <summary>
        /// Gets Kickstarts.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaKickstart>> Kickstarts
        {
            get { return KickstartOptions; }
        }

        /// <summary>
        /// Gets Floppy Speeds.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaFloppySpeed>> FloppySpeeds
        {
            get { return FloppySpeedOptions; }
        }

        /// <summary>
        /// Gets Paula Filters.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaPaulaFilter>> PaulaFilters
        {
            get { return PaulaFilterOptions; }
        }

        /// <summary>
        /// Gets Input Port Modes.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaInputPortMode>> InputPortModes
        {
            get { return InputPortOptions; }
        }

        /// <summary>
        /// Resets the Modes state.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaResetMode>> ResetModes
        {
            get { return ResetModeOptions; }
        }

        /// <summary>
        /// Gets Accuracy Profiles.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaAccuracyProfile>> AccuracyProfiles
        {
            get { return AccuracyProfileOptions; }
        }

        /// <summary>
        /// Returns the RAM Profile Info.
        /// </summary>
        public static AmigaRamProfileInfo GetRamProfileInfo(AmigaRamProfile profile)
        {
            for (int index = 0; index < RamProfiles.Length; index++)
            {
                if (RamProfiles[index].Profile == profile)
                {
                    return RamProfiles[index];
                }
            }

            return RamProfiles[0];
        }

        /// <summary>
        /// Returns the Label.
        /// </summary>
        public static string GetLabel(AmigaVideoFilter value)
        {
            return GetOptionLabel(VideoFilterOptions, value);
        }

        /// <summary>
        /// Returns the Label.
        /// </summary>
        public static string GetLabel(AmigaUpscaleMode value)
        {
            return GetOptionLabel(UpscaleOptions, value);
        }

        /// <summary>
        /// Returns the Label.
        /// </summary>
        public static string GetLabel(AmigaRenderFps value)
        {
            return GetOptionLabel(RenderFpsOptions, value);
        }

        /// <summary>
        /// Returns the Label.
        /// </summary>
        public static string GetLabel(AmigaRamProfile value)
        {
            return GetRamProfileInfo(value).Label;
        }

        /// <summary>
        /// Returns the Label.
        /// </summary>
        public static string GetLabel(AmigaKickstart value)
        {
            return GetOptionLabel(KickstartOptions, value);
        }

        /// <summary>
        /// Returns the Label.
        /// </summary>
        public static string GetLabel(AmigaFloppySpeed value)
        {
            return GetOptionLabel(FloppySpeedOptions, value);
        }

        /// <summary>
        /// Returns the Label.
        /// </summary>
        public static string GetLabel(AmigaPaulaFilter value)
        {
            return GetOptionLabel(PaulaFilterOptions, value);
        }

        /// <summary>
        /// Returns the Label.
        /// </summary>
        public static string GetLabel(AmigaInputPortMode value)
        {
            return GetOptionLabel(InputPortOptions, value);
        }

        /// <summary>
        /// Returns the Label.
        /// </summary>
        public static string GetLabel(AmigaResetMode value)
        {
            return GetOptionLabel(ResetModeOptions, value);
        }

        /// <summary>
        /// Returns the Label.
        /// </summary>
        public static string GetLabel(AmigaAccuracyProfile value)
        {
            return GetOptionLabel(AccuracyProfileOptions, value);
        }

        /// <summary>
        /// Returns the configured value.
        /// </summary>
        private static string GetOptionLabel<T>(AmigaSettingOption<T>[] options, T value)
            where T : struct
        {
            var comparer = EqualityComparer<T>.Default;
            for (int index = 0; index < options.Length; index++)
            {
                if (comparer.Equals(options[index].Value, value))
                {
                    return options[index].Label;
                }
            }

            return options.Length == 0 ? string.Empty : options[0].Label;
        }
    }
}
