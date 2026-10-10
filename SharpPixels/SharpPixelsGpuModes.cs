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
namespace SharpPixels
{
    /// <summary>
    /// Lists presentation filter modes understood by SharpPixels-compatible callers.
    /// </summary>
    public enum SharpPixelsGpuFilterMode
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
    /// Lists source upscaler modes understood by SharpPixels-compatible callers.
    /// </summary>
    public enum SharpPixelsGpuUpscaleMode
    {
        /// <summary>
        /// Leaves the source image at native size.
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
}
