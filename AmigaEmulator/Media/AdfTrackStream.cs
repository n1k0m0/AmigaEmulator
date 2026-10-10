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
namespace AmigaEmulator.Core.Media
{
    /// <summary>
    /// Provides a looping MFM word stream for one ADF track and side.
    /// </summary>
    public sealed class AdfTrackStream
    {
        /// <summary>
        /// Constant for sync Word.
        /// </summary>
        public const ushort SyncWord = AdfMfmEncoder.SyncWord;

        /// <summary>
        /// Stores words.
        /// </summary>
        private readonly ushort[] _words;

        /// <summary>
        /// Initializes a new instance of the AdfTrackStream class.
        /// </summary>
        private AdfTrackStream(ushort[] words)
        {
            _words = words ?? Array.Empty<ushort>();
        }

        /// <summary>
        /// Gets Word Count.
        /// </summary>
        public int WordCount
        {
            get { return _words.Length; }
        }

        /// <summary>
        /// Reads one encoded word from the generated ADF track stream.
        /// </summary>
        public ushort ReadWord(int index)
        {
            if (_words.Length == 0)
            {
                return 0xAAAA;
            }

            int normalized = index % _words.Length;
            if (normalized < 0)
            {
                normalized += _words.Length;
            }

            return _words[normalized];
        }

        public static AdfTrackStream Build(AdfImage image, int track, int side)
        {
            if (image == null || !image.IsStandardDoubleDensity)
            {
                return new AdfTrackStream(Array.Empty<ushort>());
            }

            if (track < 0 || track >= AdfImage.Tracks || side < 0 || side >= AdfImage.Sides)
            {
                return new AdfTrackStream(Array.Empty<ushort>());
            }

            return new AdfTrackStream(AdfMfmEncoder.EncodeTrackWords(image, track, side));
        }
    }
}
