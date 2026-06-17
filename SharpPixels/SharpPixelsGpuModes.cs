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
    /// Lists source upscaler modes understood by SharpPixels-compatible callers.
    /// </summary>
    public enum SharpPixelsGpuUpscaleMode
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
}
