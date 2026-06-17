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
        /// Represents the S ha rp option.
        /// </summary>
        Sharp,
        /// <summary>
        /// Represents the C rt option.
        /// </summary>
        Crt,
        /// <summary>
        /// Represents the T v option.
        /// </summary>
        Tv
    }

    /// <summary>
    /// Lists optional source-image upscalers applied before filtering.
    /// </summary>
    public enum AmigaUpscaleMode
    {
        /// <summary>
        /// Represents the N on e option.
        /// </summary>
        None,
        /// <summary>
        /// Represents the S ca le2 x option.
        /// </summary>
        Scale2x,
        /// <summary>
        /// Represents the S ca le3 x option.
        /// </summary>
        Scale3x,
        /// <summary>
        /// Represents the H q2 x option.
        /// </summary>
        Hq2x,
        /// <summary>
        /// Represents the H q3 x option.
        /// </summary>
        Hq3x,
        /// <summary>
        /// Represents the H q4 x option.
        /// </summary>
        Hq4x,
        /// <summary>
        /// Represents the X br z option.
        /// </summary>
        Xbrz,
        /// <summary>
        /// Represents the C le an4 option.
        /// </summary>
        Clean4
    }

    /// <summary>
    /// Lists frontend render-loop frame caps.
    /// </summary>
    public enum AmigaRenderFps
    {
        /// <summary>
        /// Represents the N at iv e option.
        /// </summary>
        Native,
        /// <summary>
        /// Represents the H z50 option.
        /// </summary>
        Hz50,
        /// <summary>
        /// Represents the H z60 option.
        /// </summary>
        Hz60,
        /// <summary>
        /// Represents the H z100 option.
        /// </summary>
        Hz100,
        /// <summary>
        /// Represents the H z120 option.
        /// </summary>
        Hz120,
        /// <summary>
        /// Represents the U nl im it ed option.
        /// </summary>
        Unlimited
    }

    /// <summary>
    /// Lists memory layouts commonly selected from the overlay.
    /// </summary>
    public enum AmigaRamProfile
    {
        /// <summary>
        /// Represents the C hi p512 K option.
        /// </summary>
        Chip512K,
        /// <summary>
        /// Represents the C hi p512 KS lo w512 K option.
        /// </summary>
        Chip512KSlow512K,
        /// <summary>
        /// Represents the C hi p1 Mb option.
        /// </summary>
        Chip1Mb,
        /// <summary>
        /// Represents the C hi p2 Mb option.
        /// </summary>
        Chip2Mb,
        /// <summary>
        /// Represents the C hi p2 Mb Fa st8 Mb option.
        /// </summary>
        Chip2MbFast8Mb
    }

    /// <summary>
    /// Lists Kickstart ROM preferences shown by the overlay.
    /// </summary>
    public enum AmigaKickstart
    {
        /// <summary>
        /// Represents the A ut o option.
        /// </summary>
        Auto,
        /// <summary>
        /// Represents the K ic ks ta rt11 option.
        /// </summary>
        Kickstart11,
        /// <summary>
        /// Represents the K ic ks ta rt12 option.
        /// </summary>
        Kickstart12,
        /// <summary>
        /// Represents the K ic ks ta rt13 option.
        /// </summary>
        Kickstart13,
        /// <summary>
        /// Represents the K ic ks ta rt20 option.
        /// </summary>
        Kickstart20,
        /// <summary>
        /// Represents the K ic ks ta rt30 option.
        /// </summary>
        Kickstart30,
        /// <summary>
        /// Represents the K ic ks ta rt31 option.
        /// </summary>
        Kickstart31,
        /// <summary>
        /// Represents the C us to m option.
        /// </summary>
        Custom
    }

    /// <summary>
    /// Lists floppy execution speeds exposed by the overlay.
    /// </summary>
    public enum AmigaFloppySpeed
    {
        /// <summary>
        /// Represents the A cc ur at e option.
        /// </summary>
        Accurate,
        /// <summary>
        /// Represents the F as t option.
        /// </summary>
        Fast,
        /// <summary>
        /// Represents the T ur bo option.
        /// </summary>
        Turbo,
        /// <summary>
        /// Represents the I ns ta nt option.
        /// </summary>
        Instant
    }

    /// <summary>
    /// Lists Paula output filter profiles.
    /// </summary>
    public enum AmigaPaulaFilter
    {
        /// <summary>
        /// Represents the O ff option.
        /// </summary>
        Off,
        /// <summary>
        /// Represents the A500 option.
        /// </summary>
        A500,
        /// <summary>
        /// Represents the A1200 option.
        /// </summary>
        A1200,
        /// <summary>
        /// Represents the L ed option.
        /// </summary>
        Led
    }

    /// <summary>
    /// Lists host input mappings for Amiga DB9 ports.
    /// </summary>
    public enum AmigaInputPortMode
    {
        /// <summary>
        /// Represents the N on e option.
        /// </summary>
        None,
        /// <summary>
        /// Represents the M ou se option.
        /// </summary>
        Mouse,
        /// <summary>
        /// Represents the J oy st ic k option.
        /// </summary>
        Joystick,
        /// <summary>
        /// Represents the G am ep ad option.
        /// </summary>
        Gamepad,
        /// <summary>
        /// Represents the K ey bo ar dJ oy st ic k option.
        /// </summary>
        KeyboardJoystick
    }

    /// <summary>
    /// Lists reset behaviors exposed by the overlay.
    /// </summary>
    public enum AmigaResetMode
    {
        /// <summary>
        /// Represents the W ar m option.
        /// </summary>
        Warm,
        /// <summary>
        /// Represents the R el oa d option.
        /// </summary>
        Reload,
        /// <summary>
        /// Represents the P ow er option.
        /// </summary>
        Power
    }

    /// <summary>
    /// Lists named core accuracy profiles.
    /// </summary>
    public enum AmigaAccuracyProfile
    {
        /// <summary>
        /// Represents the F as t option.
        /// </summary>
        Fast,
        /// <summary>
        /// Represents the C om pa ti bl e option.
        /// </summary>
        Compatible,
        /// <summary>
        /// Represents the S tr ic t option.
        /// </summary>
        Strict
    }
}
