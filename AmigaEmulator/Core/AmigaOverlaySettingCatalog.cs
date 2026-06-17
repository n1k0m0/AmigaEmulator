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
    /// Describes a selectable overlay option.
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
    /// Describes an Amiga RAM layout selected by the overlay.
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
        /// Defines the K512 constant.
        /// </summary>
        private const int K512 = 512 * 1024;
        /// <summary>
        /// Defines the M1 constant.
        /// </summary>
        private const int M1 = 1024 * 1024;

        /// <summary>
        /// Executes the Video Filter Options logic.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaVideoFilter>[] VideoFilterOptions =
        {
            /// <summary>
            /// Executes the pixels logic.
            /// </summary>
            new AmigaSettingOption<AmigaVideoFilter>(AmigaVideoFilter.Sharp, "SHARP", "Unfiltered square pixels."),
            /// <summary>
            /// Executes the presentation logic.
            /// </summary>
            new AmigaSettingOption<AmigaVideoFilter>(AmigaVideoFilter.Crt, "CRT", "Scanline CRT presentation."),
            /// <summary>
            /// Executes the presentation logic.
            /// </summary>
            new AmigaSettingOption<AmigaVideoFilter>(AmigaVideoFilter.Tv, "TV", "Softer composite-style presentation.")
        };

        /// <summary>
        /// Executes the Upscale Options logic.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaUpscaleMode>[] UpscaleOptions =
        {
            /// <summary>
            /// Executes the resolution logic.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.None, "NONE", "Use the source resolution."),
            /// <summary>
            /// Executes the upscale logic.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Scale2x, "SCALE2X", "Scale2x source upscale."),
            /// <summary>
            /// Executes the upscale logic.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Scale3x, "SCALE3X", "Scale3x source upscale."),
            /// <summary>
            /// Executes the upscale logic.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Hq2x, "HQ2X", "HQ2x source upscale."),
            /// <summary>
            /// Executes the upscale logic.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Hq3x, "HQ3X", "HQ3x source upscale."),
            /// <summary>
            /// Executes the upscale logic.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Hq4x, "HQ4X", "HQ4x source upscale."),
            /// <summary>
            /// Executes the upscale logic.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Xbrz, "XBRZ", "xBRZ source upscale."),
            /// <summary>
            /// Executes the upscale logic.
            /// </summary>
            new AmigaSettingOption<AmigaUpscaleMode>(AmigaUpscaleMode.Clean4, "CLEAN4", "Clean 4x source upscale.")
        };

        /// <summary>
        /// Renders Fps Options into the current frame buffer.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaRenderFps>[] RenderFpsOptions =
        {
            /// <summary>
            /// Executes the standard logic.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Native, "NATIVE", "Use the current Amiga video standard."),
            /// <summary>
            /// Executes the second logic.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Hz50, "50 HZ", "Cap rendering at 50 frames per second."),
            /// <summary>
            /// Executes the second logic.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Hz60, "60 HZ", "Cap rendering at 60 frames per second."),
            /// <summary>
            /// Executes the second logic.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Hz100, "100 HZ", "Cap rendering at 100 frames per second."),
            /// <summary>
            /// Executes the second logic.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Hz120, "120 HZ", "Cap rendering at 120 frames per second."),
            /// <summary>
            /// Executes the loop logic.
            /// </summary>
            new AmigaSettingOption<AmigaRenderFps>(AmigaRenderFps.Unlimited, "UNLIMIT", "Do not cap the frontend render loop.")
        };

        /// <summary>
        /// Executes the RAM Profiles logic.
        /// </summary>
        private static readonly AmigaRamProfileInfo[] RamProfiles =
        {
            /// <summary>
            /// Executes the Amiga RAM Profile Info logic.
            /// </summary>
            new AmigaRamProfileInfo(AmigaRamProfile.Chip512K, "512K CHIP", K512, 0, 0),
            /// <summary>
            /// Executes the Amiga RAM Profile Info logic.
            /// </summary>
            new AmigaRamProfileInfo(AmigaRamProfile.Chip512KSlow512K, "512K+512K", K512, K512, 0),
            /// <summary>
            /// Executes the Amiga RAM Profile Info logic.
            /// </summary>
            new AmigaRamProfileInfo(AmigaRamProfile.Chip1Mb, "1MB CHIP", M1, 0, 0),
            /// <summary>
            /// Executes the Amiga RAM Profile Info logic.
            /// </summary>
            new AmigaRamProfileInfo(AmigaRamProfile.Chip2Mb, "2MB CHIP", 2 * M1, 0, 0),
            /// <summary>
            /// Executes the Amiga RAM Profile Info logic.
            /// </summary>
            new AmigaRamProfileInfo(AmigaRamProfile.Chip2MbFast8Mb, "2MB+8MB", 2 * M1, 0, 8 * M1)
        };

        /// <summary>
        /// Executes the Kickstart Options logic.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaKickstart>[] KickstartOptions =
        {
            /// <summary>
            /// Executes the model logic.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Auto, "AUTO", "Use the best available ROM for the selected model."),
            /// <summary>
            /// Executes the Kickstart logic.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart11, "KS 1.1", "Kickstart 1.1."),
            /// <summary>
            /// Executes the Kickstart logic.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart12, "KS 1.2", "Kickstart 1.2."),
            /// <summary>
            /// Executes the Kickstart logic.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart13, "KS 1.3", "Kickstart 1.3."),
            /// <summary>
            /// Executes the Kickstart logic.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart20, "KS 2.0", "Kickstart 2.0."),
            /// <summary>
            /// Executes the Kickstart logic.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart30, "KS 3.0", "Kickstart 3.0."),
            /// <summary>
            /// Executes the Kickstart logic.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Kickstart31, "KS 3.1", "Kickstart 3.1."),
            /// <summary>
            /// Executes the path logic.
            /// </summary>
            new AmigaSettingOption<AmigaKickstart>(AmigaKickstart.Custom, "CUSTOM", "Use the configured custom ROM path.")
        };

        /// <summary>
        /// Executes the Floppy Speed Options logic.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaFloppySpeed>[] FloppySpeedOptions =
        {
            /// <summary>
            /// Executes the timing logic.
            /// </summary>
            new AmigaSettingOption<AmigaFloppySpeed>(AmigaFloppySpeed.Accurate, "ACCURATE", "Use hardware-like disk timing."),
            /// <summary>
            /// Executes the time logic.
            /// </summary>
            new AmigaSettingOption<AmigaFloppySpeed>(AmigaFloppySpeed.Fast, "FAST", "Reduce normal disk wait time."),
            /// <summary>
            /// Executes the timing logic.
            /// </summary>
            new AmigaSettingOption<AmigaFloppySpeed>(AmigaFloppySpeed.Turbo, "TURBO", "Use aggressive fast-loading disk timing."),
            /// <summary>
            /// Executes the immediately logic.
            /// </summary>
            new AmigaSettingOption<AmigaFloppySpeed>(AmigaFloppySpeed.Instant, "INSTANT", "Complete supported disk waits immediately.")
        };

        /// <summary>
        /// Executes the Paula Filter Options logic.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaPaulaFilter>[] PaulaFilterOptions =
        {
            /// <summary>
            /// Executes the filtering logic.
            /// </summary>
            new AmigaSettingOption<AmigaPaulaFilter>(AmigaPaulaFilter.Off, "OFF", "Disable host-side Paula filtering."),
            /// <summary>
            /// Executes the filtering logic.
            /// </summary>
            new AmigaSettingOption<AmigaPaulaFilter>(AmigaPaulaFilter.A500, "A500", "A500-style low-pass filtering."),
            /// <summary>
            /// Executes the filtering logic.
            /// </summary>
            new AmigaSettingOption<AmigaPaulaFilter>(AmigaPaulaFilter.A1200, "A1200", "A1200-style brighter filtering."),
            /// <summary>
            /// Executes the filter logic.
            /// </summary>
            new AmigaSettingOption<AmigaPaulaFilter>(AmigaPaulaFilter.Led, "LED", "Apply the stronger LED filter.")
        };

        /// <summary>
        /// Executes the Input Port Options logic.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaInputPortMode>[] InputPortOptions =
        {
            /// <summary>
            /// Executes the port logic.
            /// </summary>
            new AmigaSettingOption<AmigaInputPortMode>(AmigaInputPortMode.None, "NONE", "No host input on this port."),
            /// <summary>
            /// Executes the mouse logic.
            /// </summary>
            new AmigaSettingOption<AmigaInputPortMode>(AmigaInputPortMode.Mouse, "MOUSE", "Use the host mouse."),
            /// <summary>
            /// Executes the controls logic.
            /// </summary>
            new AmigaSettingOption<AmigaInputPortMode>(AmigaInputPortMode.Joystick, "JOYSTICK", "Use keyboard joystick controls."),
            /// <summary>
            /// Executes the gamepad logic.
            /// </summary>
            new AmigaSettingOption<AmigaInputPortMode>(AmigaInputPortMode.Gamepad, "GAMEPAD", "Use a host gamepad."),
            /// <summary>
            /// Executes the controls logic.
            /// </summary>
            new AmigaSettingOption<AmigaInputPortMode>(AmigaInputPortMode.KeyboardJoystick, "KEY JOY", "Use keyboard joystick controls.")
        };

        /// <summary>
        /// Resets the Mode Options state.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaResetMode>[] ResetModeOptions =
        {
            /// <summary>
            /// Executes the state logic.
            /// </summary>
            new AmigaSettingOption<AmigaResetMode>(AmigaResetMode.Warm, "WARM", "Reset without clearing all host-side state."),
            /// <summary>
            /// Executes the media logic.
            /// </summary>
            new AmigaSettingOption<AmigaResetMode>(AmigaResetMode.Reload, "RELOAD", "Reset and reload mounted media."),
            /// <summary>
            /// Executes the media logic.
            /// </summary>
            new AmigaSettingOption<AmigaResetMode>(AmigaResetMode.Power, "POWER", "Power-cycle and remount configured media.")
        };

        /// <summary>
        /// Executes the Accuracy Profile Options logic.
        /// </summary>
        private static readonly AmigaSettingOption<AmigaAccuracyProfile>[] AccuracyProfileOptions =
        {
            /// <summary>
            /// Executes the timing logic.
            /// </summary>
            new AmigaSettingOption<AmigaAccuracyProfile>(AmigaAccuracyProfile.Fast, "FAST", "Prefer responsiveness over detailed timing."),
            /// <summary>
            /// Executes the use logic.
            /// </summary>
            new AmigaSettingOption<AmigaAccuracyProfile>(AmigaAccuracyProfile.Compatible, "COMPAT", "Balanced timing profile for normal use."),
            /// <summary>
            /// Executes the detail logic.
            /// </summary>
            new AmigaSettingOption<AmigaAccuracyProfile>(AmigaAccuracyProfile.Strict, "STRICT", "Prefer strict timing detail.")
        };

        /// <summary>
        /// Executes the Video Filters logic.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaVideoFilter>> VideoFilters
        {
            get { return VideoFilterOptions; }
        }

        /// <summary>
        /// Executes the Upscale Modes logic.
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
        /// Executes the RAM Profile Infos logic.
        /// </summary>
        public static IReadOnlyList<AmigaRamProfileInfo> RamProfileInfos
        {
            get { return RamProfiles; }
        }

        /// <summary>
        /// Executes the Kickstarts logic.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaKickstart>> Kickstarts
        {
            get { return KickstartOptions; }
        }

        /// <summary>
        /// Executes the Floppy Speeds logic.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaFloppySpeed>> FloppySpeeds
        {
            get { return FloppySpeedOptions; }
        }

        /// <summary>
        /// Executes the Paula Filters logic.
        /// </summary>
        public static IReadOnlyList<AmigaSettingOption<AmigaPaulaFilter>> PaulaFilters
        {
            get { return PaulaFilterOptions; }
        }

        /// <summary>
        /// Executes the Input Port Modes logic.
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
        /// Executes the Accuracy Profiles logic.
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
