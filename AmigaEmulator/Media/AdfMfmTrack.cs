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
using System.Collections.Generic;

namespace AmigaEmulator.Core.Media
{
    public sealed class AdfMfmTrack
    {
        /// <summary>
        /// Stores words.
        /// </summary>
        private readonly ushort[] _words;

        /// <summary>
        /// Initializes a new instance of the AdfMfmTrack class.
        /// </summary>
        internal AdfMfmTrack(int cylinder, int side, ushort[] words)
        {
            if (words == null)
            {
                throw new ArgumentNullException(nameof(words));
            }

            Cylinder = cylinder;
            Side = side;
            LogicalTrack = (cylinder * AdfImage.Sides) + side;
            _words = words;
        }

        public int Cylinder { get; private set; }

        public int Side { get; private set; }

        public int LogicalTrack { get; private set; }

        /// <summary>
        /// Gets Word Count.
        /// </summary>
        public int WordCount
        {
            get { return _words.Length; }
        }

        /// <summary>
        /// Gets Words.
        /// </summary>
        public IReadOnlyList<ushort> Words
        {
            get { return _words; }
        }

        /// <summary>
        /// Returns the Word.
        /// </summary>
        public ushort GetWord(int index)
        {
            if (index < 0 || index >= _words.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _words[index];
        }

        /// <summary>
        /// Returns the Word Wrapped.
        /// </summary>
        public ushort GetWordWrapped(int index)
        {
            if (_words.Length == 0)
            {
                return 0;
            }

            int wrapped = index % _words.Length;
            if (wrapped < 0)
            {
                wrapped += _words.Length;
            }

            return _words[wrapped];
        }

        /// <summary>
        /// Runs To Array.
        /// </summary>
        public ushort[] ToArray()
        {
            ushort[] copy = new ushort[_words.Length];
            Array.Copy(_words, copy, _words.Length);
            return copy;
        }
    }
}
