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
namespace AmigaEmulator.Core.Audio
{
    /// <summary>
    /// Buffers and resamples Paula audio samples for host playback.
    /// </summary>
    public sealed class AudioResampler
    {
        public int SourceRate { get; set; }

        public int TargetRate { get; set; }

        /// <summary>
        /// Initializes a new instance of the AudioResampler class.
        /// </summary>
        public AudioResampler()
        {
            SourceRate = 3546895;
            TargetRate = 48000;
        }
    }
}
