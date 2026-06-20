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
namespace AmigaEmulator.Core.Media
{
    /// <summary>
    /// Models a selected Amiga floppy drive, including motor state, track position, side, and MFM stream position.
    /// </summary>
    public sealed class FloppyDrive
    {
        /// <summary>
        /// Constant for Motor Off Ready Grace Chip Cycles.
        /// </summary>
        private const int MotorOffReadyGraceChipCycles = 2_000_000;
        /// <summary>
        /// Constant for Pal Index Pulse Period Chip Cycles.
        /// </summary>
        private const int PalIndexPulsePeriodChipCycles = 709_379;

        /// <summary>
        /// Stores step Line High.
        /// </summary>
        private bool _stepLineHigh = true;
        /// <summary>
        /// Stores selected.
        /// </summary>
        private bool _selected;
        /// <summary>
        /// Stores write Protected.
        /// </summary>
        private bool _writeProtected = true;
        /// <summary>
        /// Stores motor On.
        /// </summary>
        private bool _motorOn;
        /// <summary>
        /// Stores motor Off Ready Grace Cycles.
        /// </summary>
        private int _motorOffReadyGraceCycles;
        /// <summary>
        /// Stores index Pulse Countdown.
        /// </summary>
        private int _indexPulseCountdown = PalIndexPulsePeriodChipCycles;
        /// <summary>
        /// Stores track Stream.
        /// </summary>
        private AdfTrackStream _trackStream;
        /// <summary>
        /// Stores stream Track.
        /// </summary>
        private int _streamTrack = -1;
        /// <summary>
        /// Stores stream Side.
        /// </summary>
        private int _streamSide = -1;
        /// <summary>
        /// Stores stream Word Position.
        /// </summary>
        private int _streamWordPosition;
        /// <summary>
        /// Tracks captured MFM words written by Paula disk-write DMA.
        /// </summary>
        private readonly System.Collections.Generic.List<ushort> _capturedWriteWords = new System.Collections.Generic.List<ushort>();
        /// <summary>
        /// Stores decoded Write Sectors.
        /// </summary>
        private int _decodedWriteSectors;

        /// <summary>
        /// Gets or sets Image.
        /// </summary>
        public AdfImage Image { get; private set; }

        /// <summary>
        /// Gets or sets Track.
        /// </summary>
        public int Track { get; private set; }

        /// <summary>
        /// Gets or sets Side.
        /// </summary>
        public int Side { get; private set; }

        /// <summary>
        /// Gets Selected.
        /// </summary>
        public bool Selected
        {
            get { return _selected; }
        }

        /// <summary>
        /// Gets Motor On.
        /// </summary>
        public bool MotorOn
        {
            get { return _motorOn; }
            set
            {
                if (_motorOn == value)
                {
                    return;
                }

                _motorOn = value;
                _motorOffReadyGraceCycles = value ? 0 : MotorOffReadyGraceChipCycles;
            }
        }

        /// <summary>
        /// Gets or sets Disk Changed.
        /// </summary>
        public bool DiskChanged { get; private set; }

        /// <summary>
        /// Returns whether Disk is present.
        /// </summary>
        public bool HasDisk
        {
            get { return Image != null; }
        }

        /// <summary>
        /// Gets Track0.
        /// </summary>
        public bool Track0
        {
            get { return Track == 0; }
        }

        /// <summary>
        /// Gets whether the drive is selected, contains media, and is ready for disk data.
        /// </summary>
        public bool Ready
        {
            get { return Selected && HasDisk && (MotorOn || _motorOffReadyGraceCycles > 0); }
        }

        /// <summary>
        /// Gets whether the front-panel drive LED should be lit for this drive.
        /// </summary>
        public bool ActivityLedOn
        {
            get { return Selected && MotorOn; }
        }

        /// <summary>
        /// Gets whether the inserted disk is write-protected.
        /// </summary>
        public bool WriteProtected
        {
            get { return HasDisk && _writeProtected; }
        }

        /// <summary>
        /// Gets or sets Index Pulse Pending.
        /// </summary>
        public bool IndexPulsePending { get; private set; }

        /// <summary>
        /// Inserts media without changing the physical drive mechanics.
        /// </summary>
        public void Insert(AdfImage image)
        {
            Image = image;
            DiskChanged = true;
            _writeProtected = true;
            _trackStream = null;
            _streamTrack = -1;
            _streamSide = -1;
            _capturedWriteWords.Clear();
            _decodedWriteSectors = 0;
            IndexPulsePending = false;
        }

        /// <summary>
        /// Runs Insert.
        /// </summary>
        public void Insert(AdfImage image, bool writeProtected)
        {
            Insert(image);
            _writeProtected = writeProtected;
        }

        /// <summary>
        /// Ejects media while leaving the drive head and control lines unchanged.
        /// </summary>
        public void Eject()
        {
            FlushImage();
            Image = null;
            DiskChanged = true;
            _writeProtected = true;
            _trackStream = null;
            _streamTrack = -1;
            _streamSide = -1;
            _capturedWriteWords.Clear();
            _decodedWriteSectors = 0;
            IndexPulsePending = false;
        }

        /// <summary>
        /// Updates the Side.
        /// </summary>
        public void SetSide(int side)
        {
            Side = side == 0 ? 0 : 1;
        }

        /// <summary>
        /// Applies Selection to the current state.
        /// </summary>
        public void ApplySelection(bool selected, bool motorLineLow)
        {
            if (selected)
            {
                MotorOn = motorLineLow;
            }

            _selected = selected;
        }

        /// <summary>
        /// Updates the Step Line.
        /// </summary>
        public void SetStepLine(bool high, bool directionIn)
        {
            if (_stepLineHigh && !high && Selected)
            {
                Step(directionIn ? 1 : -1);
                if (HasDisk)
                {
                    DiskChanged = false;
                }
            }

            _stepLineHigh = high;
        }

        /// <summary>
        /// Runs Step.
        /// </summary>
        public void Step(int delta)
        {
            Track += delta;
            if (Track < 0)
            {
                Track = 0;
            }
            else if (Track > 79)
            {
                Track = 79;
            }

            _trackStream = null;
            _streamTrack = -1;
            _streamSide = -1;
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick()
        {
            IndexPulsePending = false;
            if (_motorOffReadyGraceCycles > 0)
            {
                _motorOffReadyGraceCycles--;
            }

            if (!HasDisk || !MotorOn)
            {
                _indexPulseCountdown = PalIndexPulsePeriodChipCycles;
                return;
            }

            _indexPulseCountdown--;
            if (_indexPulseCountdown <= 0)
            {
                _indexPulseCountdown = PalIndexPulsePeriodChipCycles;
                IndexPulsePending = Selected;
            }
        }

        /// <summary>
        /// Returns the next rotating MFM word from the currently selected floppy track.
        /// </summary>
        public ushort ReadNextMfmWord()
        {
            if (!Ready || Image == null)
            {
                return 0xAAAA;
            }

            EnsureTrackStream();
            ushort word = _trackStream != null ? _trackStream.ReadWord(_streamWordPosition) : (ushort)0xAAAA;
            _streamWordPosition++;
            if (_trackStream != null && _trackStream.WordCount > 0 && _streamWordPosition >= _trackStream.WordCount)
            {
                _streamWordPosition %= _trackStream.WordCount;
            }

            return word;
        }

        /// <summary>
        /// Captures one MFM word written by disk DMA; persistence back into ADF sectors is intentionally separate.
        /// </summary>
        public bool TryCaptureWrittenMfmWord(ushort word)
        {
            if (!Ready || WriteProtected)
            {
                return false;
            }

            _capturedWriteWords.Add(word);
            TryDecodeLatestCapturedWriteSector();
            return true;
        }

        /// <summary>
        /// Flushes dirty inserted media to its host file.
        /// </summary>
        public void FlushImage()
        {
            if (Image != null && Image.IsDirty)
            {
                Image.Flush();
            }
        }

        /// <summary>
        /// Gets the number of MFM words captured from disk-write DMA.
        /// </summary>
        public int CapturedWriteWordCount
        {
            get { return _capturedWriteWords.Count; }
        }

        /// <summary>
        /// Gets the number of captured write sectors decoded back into the inserted ADF.
        /// </summary>
        public int DecodedWriteSectorCount
        {
            get { return _decodedWriteSectors; }
        }

        /// <summary>
        /// Tries to decode the newest captured MFM sector and persist it into the current ADF image.
        /// </summary>
        private void TryDecodeLatestCapturedWriteSector()
        {
            if (Image == null || _capturedWriteWords.Count < AdfMfmEncoder.SectorWords)
            {
                return;
            }

            int startIndex = _capturedWriteWords.Count - AdfMfmEncoder.SectorWords;
            if (!AdfMfmEncoder.TryDecodeSectorWords(_capturedWriteWords, startIndex, out AdfDecodedSector decoded))
            {
                TrimCapturedWriteWords();
                return;
            }

            int logicalTrack = (Track * AdfImage.Sides) + Side;
            if (decoded.LogicalTrack == logicalTrack &&
                Image.WriteSector(Track, Side, decoded.Sector, decoded.Data, 0))
            {
                _decodedWriteSectors++;
                _trackStream = null;
                _streamTrack = -1;
                _streamSide = -1;
            }

            if (startIndex > 0)
            {
                _capturedWriteWords.RemoveRange(0, startIndex);
            }
        }

        /// <summary>
        /// Keeps disk-write capture memory bounded while preserving enough history for one sector decode.
        /// </summary>
        private void TrimCapturedWriteWords()
        {
            int maximumWords = AdfMfmEncoder.SectorWords * 2;
            if (_capturedWriteWords.Count > maximumWords)
            {
                _capturedWriteWords.RemoveRange(0, _capturedWriteWords.Count - maximumWords);
            }
        }

        /// <summary>
        /// Runs Ensure Track Stream.
        /// </summary>
        private void EnsureTrackStream()
        {
            if (_trackStream != null && _streamTrack == Track && _streamSide == Side)
            {
                return;
            }

            _trackStream = Image.BuildTrackStream(Track, Side);
            _streamTrack = Track;
            _streamSide = Side;
            if (_trackStream != null && _trackStream.WordCount > 0)
            {
                _streamWordPosition %= _trackStream.WordCount;
                if (_streamWordPosition < 0)
                {
                    _streamWordPosition += _trackStream.WordCount;
                }
            }
        }
    }
}
