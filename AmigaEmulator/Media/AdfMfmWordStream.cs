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
    /// Provides ADF MFM Word Stream behavior used by the emulator.
    /// </summary>
    public sealed class AdfMfmWordStream
    {
        /// <summary>
        /// Tracks the track.
        /// </summary>
        private AdfMfmTrack _track;
        /// <summary>
        /// Tracks the position.
        /// </summary>
        private int _position;

        /// <summary>
        /// Initializes a new instance of the AdfMfmWordStream class.
        /// </summary>
        public AdfMfmWordStream()
        {
        }

        /// <summary>
        /// Initializes a new instance of the AdfMfmWordStream class.
        /// </summary>
        public AdfMfmWordStream(AdfMfmTrack track)
        {
            LoadTrack(track);
        }

        /// <summary>
        /// Executes the Track logic.
        /// </summary>
        public AdfMfmTrack Track
        {
            get { return _track; }
        }

        /// <summary>
        /// Returns whether Track is present.
        /// </summary>
        public bool HasTrack
        {
            get { return _track != null && _track.WordCount > 0; }
        }

        /// <summary>
        /// Executes the Position logic.
        /// </summary>
        public int Position
        {
            get { return _position; }
        }

        /// <summary>
        /// Loads Track into emulator state.
        /// </summary>
        public void LoadTrack(AdfMfmTrack track)
        {
            if (track == null)
            {
                throw new ArgumentNullException(nameof(track));
            }

            _track = track;
            _position = 0;
        }

        /// <summary>
        /// Executes the Clear logic.
        /// </summary>
        public void Clear()
        {
            _track = null;
            _position = 0;
        }

        /// <summary>
        /// Executes the Seek Word logic.
        /// </summary>
        public void SeekWord(int position)
        {
            if (!HasTrack)
            {
                _position = 0;
                return;
            }

            _position = Wrap(position, _track.WordCount);
        }

        /// <summary>
        /// Executes the Peek Word logic.
        /// </summary>
        public ushort PeekWord()
        {
            if (!HasTrack)
            {
                return 0;
            }

            return _track.GetWord(_position);
        }

        /// <summary>
        /// Returns the next word from the circular MFM track stream.
        /// </summary>
        public ushort ReadNextWord()
        {
            if (!HasTrack)
            {
                return 0;
            }

            ushort value = _track.GetWord(_position);
            _position++;
            if (_position >= _track.WordCount)
            {
                _position = 0;
            }

            return value;
        }

        /// <summary>
        /// Executes the Wrap logic.
        /// </summary>
        private static int Wrap(int position, int length)
        {
            int wrapped = position % length;
            if (wrapped < 0)
            {
                wrapped += length;
            }

            return wrapped;
        }
    }
}
