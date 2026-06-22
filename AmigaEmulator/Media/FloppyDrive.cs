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
    /// Models a selected Amiga floppy drive, including motor state, track position, side, and MFM stream position.
    /// </summary>
    public sealed class FloppyDrive
    {
        /// <summary>
        /// Constant for Motor Off Ready Grace Chip Cycles.
        /// </summary>
        private const int MotorReadySpinUpChipCycles = 18 * 312 * 227;
        /// <summary>
        /// Constant for Motor Off Ready Grace Chip Cycles.
        /// </summary>
        private const int MotorOffReadyGraceChipCycles = 250 * 312 * 227;
        /// <summary>
        /// Constant for Pal Index Pulse Period Chip Cycles.
        /// </summary>
        private const int PalIndexPulsePeriodChipCycles = 709_379;
        /// <summary>
        /// Constant for the emulated MFM stream word period in chip cycles.
        /// </summary>
        private const int MfmWordPeriodChipCycles = 108;

        /// <summary>
        /// Stores step Line High.
        /// </summary>
        private bool _stepLineHigh = true;
        /// <summary>
        /// Stores selected.
        /// </summary>
        private bool _selected;
        /// <summary>
        /// Stores the previous active-low motor control line as seen during the last CIA-B update.
        /// </summary>
        private bool _motorLineLow;
        /// <summary>
        /// Stores write Protected.
        /// </summary>
        private bool _writeProtected = true;
        /// <summary>
        /// Stores motor On.
        /// </summary>
        private bool _motorOn;
        /// <summary>
        /// Stores motor Ready.
        /// </summary>
        private bool _motorReady;
        /// <summary>
        /// Stores motor Ready Spin Up Cycles.
        /// </summary>
        private int _motorReadySpinUpCycles;
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
        /// Accumulates fractional chip cycles for physical MFM stream rotation.
        /// </summary>
        [NonSerialized]
        private int _streamRotationCycleAccumulator;
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
                if (value)
                {
                    bool stillSpinning = _motorOffReadyGraceCycles > 0;
                    _motorReady = HasDisk && stillSpinning;
                    _motorReadySpinUpCycles = HasDisk && !stillSpinning ? MotorReadySpinUpChipCycles : 0;
                    _motorOffReadyGraceCycles = 0;
                }
                else
                {
                    _motorReadySpinUpCycles = 0;
                    _motorOffReadyGraceCycles = _motorReady ? MotorOffReadyGraceChipCycles : 0;
                    _motorReady = false;
                }
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
        /// Gets whether the drive is selected, contains media, and can still deliver disk data.
        /// </summary>
        public bool Ready
        {
            get { return Selected && HasDisk && (_motorReady || _motorOffReadyGraceCycles > 0); }
        }

        /// <summary>
        /// Gets whether the selected drive can present flux words to Paula.
        /// </summary>
        public bool CanReadFlux
        {
            get { return Selected && HasDisk && (MotorOn || _motorOffReadyGraceCycles > 0); }
        }

        /// <summary>
        /// Gets whether the physical drive ready signal should be asserted on CIA-A.
        /// </summary>
        public bool ReadySignal
        {
            get { return Selected && HasDisk && (_motorReady || _motorOffReadyGraceCycles > 0); }
        }

        /// <summary>
        /// Gets whether the front-panel drive LED should be lit for this drive.
        /// </summary>
        public bool ActivityLedOn
        {
            get { return Selected && MotorOn; }
        }

        /// <summary>
        /// Gets the current cached track stream word position for diagnostics.
        /// </summary>
        public int StreamWordPosition
        {
            get { return _streamWordPosition; }
        }

        /// <summary>
        /// Gets the current cached track stream length for diagnostics.
        /// </summary>
        public int StreamWordCount
        {
            get { return _trackStream != null ? _trackStream.WordCount : 0; }
        }

        /// <summary>
        /// Gets the remaining chip cycles until the next index pulse for diagnostics.
        /// </summary>
        public int IndexPulseCountdown
        {
            get { return _indexPulseCountdown; }
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
            if (MotorOn)
            {
                _motorReady = false;
                _motorReadySpinUpCycles = MotorReadySpinUpChipCycles;
                _motorOffReadyGraceCycles = 0;
            }
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
        /// Restores host media after loading a savestate without changing the saved drive mechanics.
        /// </summary>
        public void RestoreMedia(AdfImage image, bool writeProtected)
        {
            Image = image;
            _writeProtected = writeProtected;
            _trackStream = null;
            _streamTrack = -1;
            _streamSide = -1;
            _capturedWriteWords.Clear();
            _decodedWriteSectors = 0;
            IndexPulsePending = false;
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
            _motorReady = false;
            _motorReadySpinUpCycles = 0;
            _motorOffReadyGraceCycles = 0;
        }

        /// <summary>
        /// Updates the Side.
        /// </summary>
        public void SetSide(int side)
        {
            Side = side == 0 ? 0 : 1;
        }

        /// <summary>
        /// Applies the CIA-B select, motor, step, and direction lines to this drive.
        /// </summary>
        public void ApplyControlLines(bool selected, bool motorLineLow, bool stepLineHigh, bool directionIn)
        {
            bool wasSelected = _selected;
            bool wasStepLineHigh = _stepLineHigh;
            bool wasMotorLineLow = _motorLineLow;

            if (selected && (!wasSelected || wasMotorLineLow != motorLineLow))
            {
                MotorOn = motorLineLow;
            }

            _selected = selected;
            _motorLineLow = motorLineLow;

            // DSKSTEP is active-low; real drives advance when the low pulse is released.
            if (!wasStepLineHigh && stepLineHigh && wasSelected)
            {
                Step(directionIn ? 1 : -1);
                if (HasDisk)
                {
                    DiskChanged = false;
                }
            }

            _stepLineHigh = stepLineHigh;
        }

        /// <summary>
        /// Applies Selection to the current state.
        /// </summary>
        public void ApplySelection(bool selected, bool motorLineLow)
        {
            ApplyControlLines(selected, motorLineLow, _stepLineHigh, false);
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
            if (_motorReadySpinUpCycles > 0)
            {
                _motorReadySpinUpCycles--;
                if (_motorReadySpinUpCycles == 0 && MotorOn && HasDisk)
                {
                    _motorReady = true;
                }
            }

            bool wasInMotorOffGrace = _motorOffReadyGraceCycles > 0;
            if (_motorOffReadyGraceCycles > 0)
            {
                _motorOffReadyGraceCycles--;
            }

            bool rotating = MotorOn || wasInMotorOffGrace;
            if (!HasDisk || !rotating)
            {
                _indexPulseCountdown = PalIndexPulsePeriodChipCycles;
                return;
            }

            AdvanceStreamRotation();

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
            if (!CanReadFlux || Image == null)
            {
                return 0xAAAA;
            }

            EnsureTrackStream();
            ushort word = _trackStream != null ? _trackStream.ReadWord(_streamWordPosition) : (ushort)0xAAAA;
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

        /// <summary>
        /// Advances physical disk rotation independently of whether Paula is currently sampling it.
        /// </summary>
        private void AdvanceStreamRotation()
        {
            EnsureTrackStream();
            if (_trackStream == null || _trackStream.WordCount <= 0)
            {
                return;
            }

            _streamRotationCycleAccumulator++;
            while (_streamRotationCycleAccumulator >= MfmWordPeriodChipCycles)
            {
                _streamRotationCycleAccumulator -= MfmWordPeriodChipCycles;
                _streamWordPosition++;
                if (_streamWordPosition >= _trackStream.WordCount)
                {
                    _streamWordPosition = 0;
                }
            }
        }
    }
}
