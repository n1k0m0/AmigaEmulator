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
namespace AmigaEmulator.Core
{
    /// <summary>
    /// Lists the presentation filters exposed by the Amiga overlay.
    /// </summary>
    public enum AmigaVideoFilter
    {
        /// <summary>
        /// Uses crisp nearest-neighbor style presentation.
        /// </summary>
        Sharp,
        /// <summary>
        /// Applies a CRT-style presentation filter.
        /// </summary>
        Crt,
        /// <summary>
        /// Applies a softer television-style presentation filter.
        /// </summary>
        Tv
    }

    /// <summary>
    /// Lists optional source-image upscalers applied before filtering.
    /// </summary>
    public enum AmigaUpscaleMode
    {
        /// <summary>
        /// Leaves the source framebuffer at its native size.
        /// </summary>
        None,
        /// <summary>
        /// Reconstructs pixel edges with the Scale2x neighbor rules.
        /// </summary>
        Scale2x,
        /// <summary>
        /// Reconstructs pixel edges with the Scale3x neighbor rules.
        /// </summary>
        Scale3x,
        /// <summary>
        /// Uses the HQ2x pattern and color difference interpolation rules.
        /// </summary>
        Hq2x,
        /// <summary>
        /// Uses the HQ3x pattern and color difference interpolation rules.
        /// </summary>
        Hq3x,
        /// <summary>
        /// Uses the HQ4x pattern and color difference interpolation rules.
        /// </summary>
        Hq4x
    }

    /// <summary>
    /// Lists frontend render-loop frame caps.
    /// </summary>
    public enum AmigaRenderFps
    {
        /// <summary>
        /// Uses the emulated video standard's native frame cadence.
        /// </summary>
        Native,
        /// <summary>
        /// Caps frontend presentation at 50 Hz.
        /// </summary>
        Hz50,
        /// <summary>
        /// Caps frontend presentation at 60 Hz.
        /// </summary>
        Hz60,
        /// <summary>
        /// Caps frontend presentation at 100 Hz.
        /// </summary>
        Hz100,
        /// <summary>
        /// Caps frontend presentation at 120 Hz.
        /// </summary>
        Hz120,
        /// <summary>
        /// Lets the frontend render loop run uncapped.
        /// </summary>
        Unlimited
    }

    /// <summary>
    /// Lists host monitor aperture models for mapping the raw PAL signal to a window.
    /// </summary>
    public enum AmigaMonitorAperture
    {
        /// <summary>
        /// Shows a CRT-like monitor aperture that hides common PAL overscan border areas.
        /// </summary>
        Crt,
        /// <summary>
        /// Shows the complete emulator PAL overscan presentation for diagnostics.
        /// </summary>
        Overscan
    }

    /// <summary>
    /// Lists memory layouts commonly selected from the overlay.
    /// </summary>
    public enum AmigaRamProfile
    {
        /// <summary>
        /// Maps 512 KB of Chip RAM with no Slow or Fast RAM.
        /// </summary>
        Chip512K,
        /// <summary>
        /// Maps 512 KB Chip RAM plus 512 KB trapdoor Slow RAM.
        /// </summary>
        Chip512KSlow512K,
        /// <summary>
        /// Maps 1 MB of Chip RAM.
        /// </summary>
        Chip1Mb,
        /// <summary>
        /// Maps 2 MB of Chip RAM.
        /// </summary>
        Chip2Mb,
        /// <summary>
        /// Maps 2 MB Chip RAM plus 8 MB Zorro II Fast RAM.
        /// </summary>
        Chip2MbFast8Mb,
        Chip1MbFast2Mb,
        Chip1MbFast4Mb,
        Chip2MbFast2Mb,
        Chip2MbFast4Mb,
        Chip512KSlow512KFast2Mb,
        Chip512KSlow512KFast4Mb,
        Chip512KSlow512KFast8Mb
    }

    /// <summary>
    /// Lists Kickstart ROM preferences shown by the overlay.
    /// </summary>
    public enum AmigaKickstart
    {
        /// <summary>
        /// Selects the best available Kickstart ROM automatically.
        /// </summary>
        Auto,
        /// <summary>
        /// Requests Kickstart 1.1 when available.
        /// </summary>
        Kickstart11,
        /// <summary>
        /// Requests Kickstart 1.2 when available.
        /// </summary>
        Kickstart12,
        /// <summary>
        /// Requests Kickstart 1.3 when available.
        /// </summary>
        Kickstart13,
        /// <summary>
        /// Requests Kickstart 2.0 when available.
        /// </summary>
        Kickstart20,
        /// <summary>
        /// Requests Kickstart 3.0 when available.
        /// </summary>
        Kickstart30,
        /// <summary>
        /// Requests Kickstart 3.1 when available.
        /// </summary>
        Kickstart31,
        /// <summary>
        /// Uses the configured custom Kickstart path.
        /// </summary>
        Custom
    }

    /// <summary>
    /// Lists floppy execution speeds exposed by the overlay.
    /// </summary>
    public enum AmigaFloppySpeed
    {
        /// <summary>
        /// Uses hardware-like disk word timing.
        /// </summary>
        Accurate,
        /// <summary>
        /// Uses reduced disk wait timing for faster loading.
        /// </summary>
        Fast,
        /// <summary>
        /// Uses aggressive disk timing for turbo loading.
        /// </summary>
        Turbo,
        /// <summary>
        /// Reserves the instant-load setting for compatible fast paths.
        /// </summary>
        Instant
    }

    /// <summary>
    /// Lists Paula output filter profiles.
    /// </summary>
    public enum AmigaPaulaFilter
    {
        /// <summary>
        /// Disables the Paula output filter.
        /// </summary>
        Off,
        /// <summary>
        /// Uses A500 fixed stages and the software-controlled LED filter.
        /// </summary>
        A500,
        /// <summary>
        /// Bypasses fixed stages, with the software-controlled A1200 LED filter.
        /// </summary>
        A1200,
        /// <summary>
        /// Forces the A500 fixed stages and LED filter on.
        /// </summary>
        Led
    }

    /// <summary>
    /// Lists host input mappings for Amiga DB9 ports.
    /// </summary>
    public enum AmigaInputPortMode
    {
        /// <summary>
        /// Leaves the DB9 port unmapped.
        /// </summary>
        None,
        /// <summary>
        /// Maps host mouse input to the DB9 port.
        /// </summary>
        Mouse,
        /// <summary>
        /// Maps host joystick input to the DB9 port.
        /// </summary>
        Joystick,
        /// <summary>
        /// Maps host gamepad input to the DB9 port.
        /// </summary>
        Gamepad,
        /// <summary>
        /// Maps keyboard controls to joystick input.
        /// </summary>
        KeyboardJoystick
    }

    /// <summary>
    /// Lists reset behaviors exposed by the overlay.
    /// </summary>
    public enum AmigaResetMode
    {
        /// <summary>
        /// Performs a warm reset without remounting media.
        /// </summary>
        Warm,
        /// <summary>
        /// Resets and remounts the configured media.
        /// </summary>
        Reload,
        /// <summary>
        /// Performs a power-cycle style reset.
        /// </summary>
        Power
    }

    /// <summary>
    /// Lists named core accuracy profiles.
    /// </summary>
    public enum AmigaAccuracyProfile
    {
        /// <summary>
        /// Uses lower-overhead core timing.
        /// </summary>
        Fast,
        /// <summary>
        /// Balances compatibility with runtime cost.
        /// </summary>
        Compatible,
        /// <summary>
        /// Enables the strictest implemented timing behavior.
        /// </summary>
        Strict
    }
}
