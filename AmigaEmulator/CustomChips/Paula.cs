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
using System.IO;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>
    /// Emulates Paula disk DMA and four-channel audio behavior used by the A500.
    /// </summary>
    public sealed class Paula
    {
        /// <summary>
        /// Defines the DSKDATR custom register offset or bit mask.
        /// </summary>
        private const uint Dskdatr = 0x008;
        /// <summary>
        /// Defines the DSKBYTR custom register offset or bit mask.
        /// </summary>
        private const uint Dskbytr = 0x01A;
        /// <summary>
        /// Defines the DSKPTH custom register offset or bit mask.
        /// </summary>
        private const uint Dskpth = 0x020;
        /// <summary>
        /// Defines the DSKPTL custom register offset or bit mask.
        /// </summary>
        private const uint Dskptl = 0x022;
        /// <summary>
        /// Defines the DSKLEN custom register offset or bit mask.
        /// </summary>
        private const uint Dsklen = 0x024;
        /// <summary>
        /// Defines the DSKDAT custom register offset or bit mask.
        /// </summary>
        private const uint Dskdat = 0x026;
        /// <summary>
        /// Defines the DSKSYNC custom register offset or bit mask.
        /// </summary>
        private const uint Dsksync = 0x07E;
        /// <summary>
        /// Defines the AUD0 BASE custom register offset or bit mask.
        /// </summary>
        private const uint Aud0Base = 0x0A0;
        /// <summary>
        /// Defines the AUD3 END custom register offset or bit mask.
        /// </summary>
        private const uint Aud3End = 0x0DE;
        // PAL DD disks rotate once per ~709379 chip cycles. A standard ADF
        // track contains 0x1996 MFM words, so one word arrives every ~108 cycles.
        private const int DiskWordPeriodChipCycles = 108;
        /// <summary>
        /// Defines the AUDIO CHANNEL COUNT custom register offset or bit mask.
        /// </summary>
        private const int AudioChannelCount = 4;
        /// <summary>
        /// Defines the AUDIO OUTPUT RATE custom register offset or bit mask.
        /// </summary>
        private const int AudioOutputRate = 48000;
        /// <summary>
        /// Defines the Paula Clock Hz constant.
        /// </summary>
        private const double PaulaClockHz = 3546895.0;

        /// <summary>
        /// Tracks the floppy Controller.
        /// </summary>
        private FloppyController _floppyController;
        /// <summary>
        /// Tracks the write Chip Word.
        /// </summary>
        private Action<uint, ushort> _writeChipWord;
        /// <summary>
        /// Tracks the read Chip Word.
        /// </summary>
        private Func<uint, ushort> _readChipWord;
        /// <summary>
        /// Tracks the request Interrupt.
        /// </summary>
        private Action<CustomInterruptSource> _requestInterrupt;
        /// <summary>
        /// Tracks the read DMA Control.
        /// </summary>
        private Func<ushort> _readDmaControl;
        /// <summary>
        /// Tracks the read Adk Control.
        /// </summary>
        private Func<ushort> _readAdkControl;
        /// <summary>
        /// Tracks the DMA Address.
        /// </summary>
        private readonly ChipDmaAddress _dmaAddress;
        /// <summary>
        /// Tracks the audio Channels.
        /// </summary>
        private readonly AudioChannel[] _audioChannels;
        /// <summary>
        /// Tracks the disk Pointer.
        /// </summary>
        private uint _diskPointer;
        /// <summary>
        /// Tracks the disk Length Register.
        /// </summary>
        private ushort _diskLengthRegister;
        /// <summary>
        /// Tracks the armed Disk Length Register.
        /// </summary>
        private ushort _armedDiskLengthRegister;
        /// <summary>
        /// Tracks the disk sync.
        /// </summary>
        private ushort _diskSync = 0x4489;
        /// <summary>
        /// Tracks the latest Disk Word.
        /// </summary>
        private ushort _latestDiskWord = 0xAAAA;
        /// <summary>
        /// Tracks the pending Disk Word.
        /// </summary>
        private ushort _pendingDiskWord = 0xAAAA;
        /// <summary>
        /// Tracks the latest Disk Byte.
        /// </summary>
        private byte _latestDiskByte = 0xAA;
        /// <summary>
        /// Tracks the remaining Disk Words.
        /// </summary>
        private int _remainingDiskWords;
        /// <summary>
        /// Tracks the disk DMA Active.
        /// </summary>
        private bool _diskDmaActive;
        /// <summary>
        /// Tracks the disk DMA Armed.
        /// </summary>
        private bool _diskDmaArmed;
        /// <summary>
        /// Tracks the disk Write Mode.
        /// </summary>
        private bool _diskWriteMode;
        /// <summary>
        /// Tracks the waiting For Disk sync.
        /// </summary>
        private bool _waitingForDiskSync;
        /// <summary>
        /// Tracks the disk sync Matched For DMA.
        /// </summary>
        private bool _diskSyncMatchedForDma;
        /// <summary>
        /// Tracks the word Ready.
        /// </summary>
        private bool _wordReady;
        /// <summary>
        /// Tracks the word Equal.
        /// </summary>
        private bool _wordEqual;
        /// <summary>
        /// Tracks the disk Byte Ready.
        /// </summary>
        private bool _diskByteReady;
        /// <summary>
        /// Tracks the next Disk Byte Is Low.
        /// </summary>
        private bool _nextDiskByteIsLow;
        /// <summary>
        /// Tracks the disk Word Divider.
        /// </summary>
        private int _diskWordDivider;
        /// <summary>
        /// Tracks the disk DMA Starts.
        /// </summary>
        private long _diskDmaStarts;
        /// <summary>
        /// Tracks the disk DMA Completed Blocks.
        /// </summary>
        private long _diskDmaCompletedBlocks;
        /// <summary>
        /// Tracks the disk Words Transferred.
        /// </summary>
        private long _diskWordsTransferred;
        /// <summary>
        /// Tracks the disk words written through Paula write DMA.
        /// </summary>
        private long _diskWordsWritten;
        /// <summary>
        /// Tracks disk write attempts rejected by write-protect or missing media.
        /// </summary>
        private long _diskWriteRejectedWords;
        /// <summary>
        /// Tracks the disk sync Matches.
        /// </summary>
        private long _diskSyncMatches;
        /// <summary>
        /// Tracks the current Disk Block Start.
        /// </summary>
        private uint _currentDiskBlockStart;
        /// <summary>
        /// Tracks the last Disk Block Start.
        /// </summary>
        private uint _lastDiskBlockStart;
        /// <summary>
        /// Tracks the last Disk Block End.
        /// </summary>
        private uint _lastDiskBlockEnd;
        /// <summary>
        /// Tracks the last Disk Block Words.
        /// </summary>
        private int _lastDiskBlockWords;
        /// <summary>
        /// Tracks the audio Sample Accumulator.
        /// </summary>
        private double _audioSampleAccumulator;
        /// <summary>
        /// Tracks the audio Samples Generated.
        /// </summary>
        private long _audioSamplesGenerated;
        /// <summary>
        /// Tracks the audio DMA Words Fetched.
        /// </summary>
        private long _audioDmaWordsFetched;
        /// <summary>
        /// Tracks the audio Interrupts.
        /// </summary>
        private long _audioInterrupts;
        /// <summary>
        /// Tracks the audio Non Zero Samples.
        /// </summary>
        private long _audioNonZeroSamples;
        /// <summary>
        /// Tracks the audio Peak Left.
        /// </summary>
        private float _audioPeakLeft;
        /// <summary>
        /// Tracks the audio Peak Right.
        /// </summary>
        private float _audioPeakRight;

        /// <summary>
        /// Raised whenever Paula emits a mixed stereo audio sample.
        /// </summary>
        public event Action<float, float> AudioSampleGenerated;

        /// <summary>
        /// Gets or updates the Audio Ticks.
        /// </summary>
        public long AudioTicks { get; private set; }

        /// <summary>
        /// Initializes a new instance of the Paula class.
        /// </summary>
        public Paula()
            : this(512 * 1024)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Paula class.
        /// </summary>
        public Paula(int chipRamBytes)
        {
            _dmaAddress = new ChipDmaAddress(chipRamBytes);
            _audioChannels = new AudioChannel[AudioChannelCount];
            for (int index = 0; index < _audioChannels.Length; index++)
            {
                _audioChannels[index] = new AudioChannel(index);
            }
        }

        /// <summary>
        /// Gets or updates the Last Custom Write Address.
        /// </summary>
        public uint LastCustomWriteAddress { get; private set; }

        /// <summary>
        /// Gets or updates the Last Custom Write Value.
        /// </summary>
        public byte LastCustomWriteValue { get; private set; }

        /// <summary>
        /// Executes the Disk DMA Active logic.
        /// </summary>
        public bool DiskDmaActive
        {
            get { return _diskDmaActive; }
        }

        /// <summary>
        /// Executes the Remaining Disk Words logic.
        /// </summary>
        public int RemainingDiskWords
        {
            get { return _remainingDiskWords; }
        }

        /// <summary>
        /// Executes the Disk DMA Starts logic.
        /// </summary>
        public long DiskDmaStarts
        {
            get { return _diskDmaStarts; }
        }

        /// <summary>
        /// Executes the Disk DMA Completed Blocks logic.
        /// </summary>
        public long DiskDmaCompletedBlocks
        {
            get { return _diskDmaCompletedBlocks; }
        }

        /// <summary>
        /// Executes the Disk Words Transferred logic.
        /// </summary>
        public long DiskWordsTransferred
        {
            get { return _diskWordsTransferred; }
        }

        /// <summary>
        /// Executes the Disk Pointer logic.
        /// </summary>
        public uint DiskPointer
        {
            get { return _diskPointer; }
        }

        /// <summary>
        /// Executes the Current Disk Block Start logic.
        /// </summary>
        public uint CurrentDiskBlockStart
        {
            get { return _currentDiskBlockStart; }
        }

        /// <summary>
        /// Executes the Last Disk Block Start logic.
        /// </summary>
        public uint LastDiskBlockStart
        {
            get { return _lastDiskBlockStart; }
        }

        /// <summary>
        /// Executes the Last Disk Block End logic.
        /// </summary>
        public uint LastDiskBlockEnd
        {
            get { return _lastDiskBlockEnd; }
        }

        /// <summary>
        /// Executes the Last Disk Block Words logic.
        /// </summary>
        public int LastDiskBlockWords
        {
            get { return _lastDiskBlockWords; }
        }

        /// <summary>
        /// Attaches Disk to this component.
        /// </summary>
        public void AttachDisk(FloppyController floppyController, Action<uint, ushort> writeChipWord, Action<CustomInterruptSource> requestInterrupt)
        {
            AttachDisk(floppyController, writeChipWord, null, requestInterrupt, null, null);
        }

        /// <summary>
        /// Attaches Disk to this component.
        /// </summary>
        public void AttachDisk(
            FloppyController floppyController,
            Action<uint, ushort> writeChipWord,
            Func<uint, ushort> readChipWord,
            Action<CustomInterruptSource> requestInterrupt,
            Func<ushort> readDmaControl,
            Func<ushort> readAdkControl)
        {
            _floppyController = floppyController;
            _writeChipWord = writeChipWord;
            _readChipWord = readChipWord;
            _requestInterrupt = requestInterrupt;
            _readDmaControl = readDmaControl;
            _readAdkControl = readAdkControl;
        }

        /// <summary>
        /// Writes Paula disk, audio, DMA, and register-facing state to a savestate.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            StateSerializer.WriteObjectFields(
                writer,
                this,
                "_floppyController",
                "_writeChipWord",
                "_readChipWord",
                "_requestInterrupt",
                "_readDmaControl",
                "_readAdkControl");
        }

        /// <summary>
        /// Restores Paula disk, audio, DMA, and register-facing state from a savestate.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            StateSerializer.ReadObjectFields(
                reader,
                this,
                "_floppyController",
                "_writeChipWord",
                "_readChipWord",
                "_requestInterrupt",
                "_readDmaControl",
                "_readAdkControl");
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            AudioTicks = 0;
            LastCustomWriteAddress = 0;
            LastCustomWriteValue = 0;
            _diskPointer = 0;
            _diskLengthRegister = 0;
            _armedDiskLengthRegister = 0;
            _diskSync = 0x4489;
            _latestDiskWord = 0xAAAA;
            _pendingDiskWord = 0xAAAA;
            _latestDiskByte = 0xAA;
            _remainingDiskWords = 0;
            _diskDmaActive = false;
            _diskDmaArmed = false;
            _diskWriteMode = false;
            _waitingForDiskSync = false;
            _diskSyncMatchedForDma = false;
            _wordReady = false;
            _wordEqual = false;
            _diskByteReady = false;
            _nextDiskByteIsLow = false;
            _diskWordDivider = 0;
            _diskDmaStarts = 0;
            _diskDmaCompletedBlocks = 0;
            _diskWordsTransferred = 0;
            _diskWordsWritten = 0;
            _diskWriteRejectedWords = 0;
            _diskSyncMatches = 0;
            _currentDiskBlockStart = 0;
            _lastDiskBlockStart = 0;
            _lastDiskBlockEnd = 0;
            _lastDiskBlockWords = 0;
            _audioSampleAccumulator = 0.0;
            _audioSamplesGenerated = 0;
            _audioDmaWordsFetched = 0;
            _audioInterrupts = 0;
            _audioNonZeroSamples = 0;
            _audioPeakLeft = 0.0f;
            _audioPeakRight = 0.0f;
            for (int index = 0; index < _audioChannels.Length; index++)
            {
                _audioChannels[index].Reset();
            }
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick()
        {
            Tick(false, -1);
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick(bool diskDmaSlotAvailable)
        {
            Tick(diskDmaSlotAvailable, -1);
        }

        /// <summary>
        /// Advances this component by one emulator tick using the legacy audio-slot boolean.
        /// </summary>
        public void Tick(bool diskDmaSlotAvailable, bool audioDmaSlotAvailable)
        {
            Tick(diskDmaSlotAvailable, audioDmaSlotAvailable ? 0 : -1);
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick(bool diskDmaSlotAvailable, int audioDmaChannel)
        {
            AudioTicks++;
            TickAudio(audioDmaChannel);
            TickDiskInputStream();
            if (diskDmaSlotAvailable)
            {
                TickDiskDma();
            }
        }

        /// <summary>
        /// Notifies this component about Custom Register Write.
        /// </summary>
        public void NotifyCustomRegisterWrite(uint address, byte value)
        {
            LastCustomWriteAddress = address;
            LastCustomWriteValue = value;
        }

        /// <summary>
        /// Notifies this component about Custom Register Word Write.
        /// </summary>
        public void NotifyCustomRegisterWordWrite(uint address, ushort value)
        {
            uint offset = address & 0x01FE;
            if (offset == Dskpth)
            {
                _diskPointer = _dmaAddress.FromRegisters(value, (ushort)_diskPointer);
            }
            else if (offset == Dskptl)
            {
                _diskPointer = _dmaAddress.FromRegisters((ushort)(_diskPointer >> 16), value);
            }
            else if (offset == Dsksync)
            {
                _diskSync = value;
            }
            else if (offset == Dsklen)
            {
                WriteDiskLength(value);
            }
            else if (offset == Dskdat)
            {
                WriteDiskData(value);
            }
            else if (IsAudioRegister(offset))
            {
                WriteAudioRegister(offset, value);
            }
        }

        /// <summary>
        /// Attempts to Read Custom Register Word and reports whether it succeeded.
        /// </summary>
        public bool TryReadCustomRegisterWord(uint address, out ushort value)
        {
            uint offset = address & 0x01FE;
            if (offset == Dskbytr)
            {
                value = BuildDiskBytr();
                _diskByteReady = false;
                return true;
            }

            if (offset == Dskdatr)
            {
                value = _latestDiskWord;
                _wordReady = false;
                return true;
            }

            if (IsAudioRegister(offset))
            {
                value = ReadAudioRegister(offset);
                return true;
            }

            value = 0;
            return false;
        }

        /// <summary>
        /// Attempts to read a custom-register word without clearing Paula read-ready side effects.
        /// </summary>
        public bool TryPeekCustomRegisterWord(uint address, out ushort value)
        {
            uint offset = address & 0x01FE;
            if (offset == Dskbytr)
            {
                value = BuildDiskBytr();
                return true;
            }

            if (offset == Dskdatr)
            {
                value = _latestDiskWord;
                return true;
            }

            if (IsAudioRegister(offset))
            {
                value = ReadAudioRegister(offset);
                return true;
            }

            value = 0;
            return false;
        }

        /// <summary>
        /// Returns the Disk Debug Info.
        /// </summary>
        public string GetDiskDebugInfo()
        {
            return string.Format(
                "active:{0} write:{1} ptr:${2:X6} len:${3:X4} remaining:{4} sync:${5:X4} word:${6:X4} ready:{7} equal:{8}",
                _diskDmaActive,
                _diskWriteMode,
                _diskPointer,
                _diskLengthRegister,
                _remainingDiskWords,
                _diskSync,
                _latestDiskWord,
                _wordReady,
                _wordEqual) +
            string.Format(
                " starts:{0} done:{1} words:{2} written:{3} rejected:{4} syncs:{5}",
                _diskDmaStarts,
                _diskDmaCompletedBlocks,
                _diskWordsTransferred,
                _diskWordsWritten,
                _diskWriteRejectedWords,
                _diskSyncMatches);
        }

        /// <summary>
        /// Returns the Disk Short Debug Info.
        /// </summary>
        public string GetDiskShortDebugInfo()
        {
            return string.Format(
                "active:{0} remaining:{1} starts:{2} done:{3} words:{4} written:{5} syncs:{6}",
                _diskDmaActive,
                _remainingDiskWords,
                _diskDmaStarts,
                _diskDmaCompletedBlocks,
                _diskWordsTransferred,
                _diskWordsWritten,
                _diskSyncMatches);
        }

        /// <summary>
        /// Returns the Audio Debug Info.
        /// </summary>
        public string GetAudioDebugInfo()
        {
            return string.Format(
                "samples:{0} nonZero:{1} peakL:{2:F3} peakR:{3:F3} dmaWords:{4} irq:{5} ch0:{6} ch1:{7} ch2:{8} ch3:{9}",
                _audioSamplesGenerated,
                _audioNonZeroSamples,
                _audioPeakLeft,
                _audioPeakRight,
                _audioDmaWordsFetched,
                _audioInterrupts,
                _audioChannels[0].GetDebugInfo(IsAudioDmaEnabled(0)),
                _audioChannels[1].GetDebugInfo(IsAudioDmaEnabled(1)),
                _audioChannels[2].GetDebugInfo(IsAudioDmaEnabled(2)),
                _audioChannels[3].GetDebugInfo(IsAudioDmaEnabled(3)));
        }

        /// <summary>
        /// Executes the Audio Samples Generated logic.
        /// </summary>
        public long AudioSamplesGenerated
        {
            get { return _audioSamplesGenerated; }
        }

        /// <summary>
        /// Executes the Audio DMA Words Fetched logic.
        /// </summary>
        public long AudioDmaWordsFetched
        {
            get { return _audioDmaWordsFetched; }
        }

        /// <summary>
        /// Handles the two-step DSKLEN arming sequence and starts or stops disk DMA.
        /// </summary>
        private void WriteDiskLength(ushort value)
        {
            _diskLengthRegister = value;
            _diskWriteMode = (value & 0x4000) != 0;
            int length = value & 0x3FFF;
            if ((value & 0x8000) == 0 || length == 0)
            {
                _diskDmaActive = false;
                _diskDmaArmed = false;
                _waitingForDiskSync = false;
                _diskSyncMatchedForDma = false;
                _remainingDiskWords = 0;
                _wordReady = false;
                return;
            }

            if (!_diskDmaArmed || _armedDiskLengthRegister != value)
            {
                _armedDiskLengthRegister = value;
                _diskDmaArmed = true;
                return;
            }

            _diskDmaArmed = false;
            _remainingDiskWords = length;
            _diskDmaActive = true;
            _waitingForDiskSync = !_diskWriteMode && IsWordSyncEnabled();
            _diskSyncMatchedForDma = false;
            _diskDmaStarts++;
            _currentDiskBlockStart = _diskPointer;
            _lastDiskBlockWords = 0;
            _wordReady = false;
            _wordEqual = false;
        }

        /// <summary>
        /// Updates DSKDAT and captures manual write-data words for diagnostics.
        /// </summary>
        private void WriteDiskData(ushort value)
        {
            _latestDiskWord = value;
            _pendingDiskWord = value;
            _latestDiskByte = (byte)(value >> 8);
            _wordReady = true;
            _wordEqual = value == _diskSync;
            _diskByteReady = true;
            _nextDiskByteIsLow = true;
        }

        /// <summary>
        /// Advances the Audio state by one emulator tick.
        /// </summary>
        private void TickAudio(int dmaSlotChannel)
        {
            UpdateAudioDmaEnableState();
            if (dmaSlotChannel >= 0)
            {
                TickAudioDmaSlot(dmaSlotChannel);
            }

            for (int channel = 0; channel < _audioChannels.Length; channel++)
            {
                _audioChannels[channel].TickPeriod();
            }

            EmitAudioSamples();
        }

        /// <summary>
        /// Updates Audio DMA Enable State from current emulator state.
        /// </summary>
        private void UpdateAudioDmaEnableState()
        {
            for (int channel = 0; channel < _audioChannels.Length; channel++)
            {
                bool enabled = IsAudioDmaEnabled(channel);
                _audioChannels[channel].SetDmaEnabled(enabled);
                if (enabled)
                {
                    _audioChannels[channel].EnsureDmaStarted();
                }
            }
        }

        /// <summary>
        /// Advances the Audio DMA Slot state by one emulator tick.
        /// </summary>
        private void TickAudioDmaSlot(int channelIndex)
        {
            if (_readChipWord == null || channelIndex < 0 || channelIndex >= _audioChannels.Length)
            {
                return;
            }

            AudioChannel channel = _audioChannels[channelIndex];
            if (!IsAudioDmaEnabled(channelIndex) || !channel.NeedsDmaWord)
            {
                return;
            }

            ushort word = _readChipWord(channel.CurrentPointer);
            channel.AcceptDmaWord(word);
            _audioDmaWordsFetched++;
            if (channel.BlockCompleted)
            {
                RequestAudioInterrupt(channelIndex);
                channel.ReloadDmaBlock();
            }
        }

        /// <summary>
        /// Executes the Emit Audio Samples logic.
        /// </summary>
        private void EmitAudioSamples()
        {
            _audioSampleAccumulator += AudioOutputRate;
            while (_audioSampleAccumulator >= PaulaClockHz)
            {
                _audioSampleAccumulator -= PaulaClockHz;
                float left = (_audioChannels[0].Output + _audioChannels[3].Output) * (1.0f / (2.0f * 128.0f * 64.0f));
                float right = (_audioChannels[1].Output + _audioChannels[2].Output) * (1.0f / (2.0f * 128.0f * 64.0f));
                _audioSamplesGenerated++;
                float absLeft = Math.Abs(left);
                float absRight = Math.Abs(right);
                if (absLeft > 0.000001f || absRight > 0.000001f)
                {
                    _audioNonZeroSamples++;
                }

                if (absLeft > _audioPeakLeft)
                {
                    _audioPeakLeft = absLeft;
                }

                if (absRight > _audioPeakRight)
                {
                    _audioPeakRight = absRight;
                }

                Action<float, float> handler = AudioSampleGenerated;
                if (handler != null)
                {
                    handler(left, right);
                }
            }
        }

        /// <summary>
        /// Executes the Request Audio Interrupt logic.
        /// </summary>
        private void RequestAudioInterrupt(int channel)
        {
            if (_requestInterrupt == null)
            {
                return;
            }

            _audioInterrupts++;
            _requestInterrupt((CustomInterruptSource)((ushort)CustomInterruptSource.Audio0 << channel));
        }

        /// <summary>
        /// Returns whether Audio DMA Enabled is true.
        /// </summary>
        private bool IsAudioDmaEnabled(int channel)
        {
            ushort dmacon = _readDmaControl != null ? _readDmaControl() : (ushort)0;
            return (dmacon & 0x0200) != 0 && (dmacon & (1 << channel)) != 0;
        }

        /// <summary>
        /// Routes a write to the selected Paula audio-channel register.
        /// </summary>
        private void WriteAudioRegister(uint offset, ushort value)
        {
            int channelIndex = (int)((offset - Aud0Base) >> 4);
            uint register = offset & 0x000E;
            AudioChannel channel = _audioChannels[channelIndex];
            switch (register)
            {
                case 0x00:
                    channel.Location = _dmaAddress.FromRegisters(value, (ushort)channel.Location);
                    break;
                case 0x02:
                    channel.Location = _dmaAddress.FromRegisters((ushort)(channel.Location >> 16), value);
                    break;
                case 0x04:
                    channel.Length = value;
                    break;
                case 0x06:
                    channel.Period = value;
                    break;
                case 0x08:
                    channel.Volume = value;
                    break;
                case 0x0A:
                    channel.WriteData(value);
                    break;
            }
        }

        /// <summary>
        /// Reads the selected Paula audio-channel register shadow value.
        /// </summary>
        private ushort ReadAudioRegister(uint offset)
        {
            int channelIndex = (int)((offset - Aud0Base) >> 4);
            uint register = offset & 0x000E;
            AudioChannel channel = _audioChannels[channelIndex];
            switch (register)
            {
                case 0x00:
                    return (ushort)(channel.Location >> 16);
                case 0x02:
                    return (ushort)channel.Location;
                case 0x04:
                    return channel.Length;
                case 0x06:
                    return channel.Period;
                case 0x08:
                    return channel.Volume;
                case 0x0A:
                    return channel.Data;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Returns whether Audio Register is true.
        /// </summary>
        private static bool IsAudioRegister(uint offset)
        {
            if (offset < Aud0Base || offset > Aud3End)
            {
                return false;
            }

            uint channelOffset = (offset - Aud0Base) & 0x000F;
            return channelOffset <= 0x000A && (channelOffset & 1) == 0;
        }

        /// <summary>
        /// Advances the Disk Input Stream state by one emulator tick.
        /// </summary>
        private void TickDiskInputStream()
        {
            FloppyDrive drive = _floppyController != null ? _floppyController.Drive0 : null;
            if (drive == null || !drive.Ready)
            {
                _diskWordDivider = 0;
                _diskByteReady = false;
                _nextDiskByteIsLow = false;
                return;
            }

            if (_wordReady && _diskDmaActive && !_diskWriteMode)
            {
                return;
            }

            _diskWordDivider++;
            if (_diskWordDivider < DiskWordPeriodChipCycles / 2)
            {
                return;
            }

            _diskWordDivider = 0;
            AdvanceDiskInputByte(drive);
        }

        /// <summary>
        /// Advances the Disk DMA state by one emulator tick.
        /// </summary>
        private void TickDiskDma()
        {
            if (!_diskDmaActive || _remainingDiskWords <= 0)
            {
                return;
            }

            if (_diskWriteMode)
            {
                TransferDiskWriteWord();
                return;
            }

            if (!_wordReady)
            {
                return;
            }

            ushort word = _latestDiskWord;

            if (_waitingForDiskSync)
            {
                if (_wordEqual)
                {
                    _diskSyncMatchedForDma = true;
                    _wordReady = false;
                    return;
                }

                if (!_diskSyncMatchedForDma)
                {
                    _wordReady = false;
                    return;
                }

                _waitingForDiskSync = false;
                _diskSyncMatchedForDma = false;
            }

            _wordReady = false;
            TransferDiskWord(word);
        }

        /// <summary>
        /// Executes the Advance Disk Input Byte logic.
        /// </summary>
        private void AdvanceDiskInputByte(FloppyDrive drive)
        {
            if (!_nextDiskByteIsLow)
            {
                _pendingDiskWord = drive != null ? drive.ReadNextMfmWord() : (ushort)0xAAAA;
                _latestDiskByte = (byte)(_pendingDiskWord >> 8);
                _diskByteReady = true;
                _nextDiskByteIsLow = true;
                return;
            }

            _latestDiskByte = (byte)_pendingDiskWord;
            _latestDiskWord = _pendingDiskWord;
            _wordReady = true;
            _wordEqual = _latestDiskWord == _diskSync;
            _diskByteReady = true;
            _nextDiskByteIsLow = false;
            if (_wordEqual && _requestInterrupt != null)
            {
                _diskSyncMatches++;
                _requestInterrupt(CustomInterruptSource.DiskSync);
            }
        }

        /// <summary>
        /// Executes the Transfer Disk Word logic.
        /// </summary>
        private void TransferDiskWord(ushort word)
        {
            if (_writeChipWord != null)
            {
                _writeChipWord(_diskPointer, word);
            }

            _diskPointer = _dmaAddress.Add(_diskPointer, 2u);
            _diskWordsTransferred++;
            _lastDiskBlockWords++;
            _remainingDiskWords--;
            if (_remainingDiskWords <= 0)
            {
                CompleteDiskDma();
            }
        }

        /// <summary>
        /// Transfers one MFM word from chip memory to the selected drive during disk-write DMA.
        /// </summary>
        private void TransferDiskWriteWord()
        {
            ushort word = _readChipWord != null ? _readChipWord(_diskPointer) : _latestDiskWord;
            FloppyDrive drive = _floppyController != null ? _floppyController.Drive0 : null;
            bool accepted = drive != null && drive.TryCaptureWrittenMfmWord(word);
            if (accepted)
            {
                _diskWordsWritten++;
            }
            else
            {
                _diskWriteRejectedWords++;
            }

            _latestDiskWord = word;
            _latestDiskByte = (byte)(word >> 8);
            _wordReady = true;
            _wordEqual = word == _diskSync;
            _diskPointer = _dmaAddress.Add(_diskPointer, 2u);
            _lastDiskBlockWords++;
            _remainingDiskWords--;
            if (_remainingDiskWords <= 0)
            {
                CompleteDiskDma();
            }
        }

        /// <summary>
        /// Executes the Complete Disk DMA logic.
        /// </summary>
        private void CompleteDiskDma()
        {
            _diskDmaActive = false;
            _waitingForDiskSync = false;
            _diskSyncMatchedForDma = false;
            _remainingDiskWords = 0;
            _diskDmaCompletedBlocks++;
            _lastDiskBlockStart = _currentDiskBlockStart;
            _lastDiskBlockEnd = _diskPointer;
            if (_requestInterrupt != null)
            {
                _requestInterrupt(CustomInterruptSource.DskBlk);
            }
        }

        /// <summary>
        /// Builds the Disk byte register.
        /// </summary>
        private ushort BuildDiskBytr()
        {
            ushort value = _latestDiskByte;
            if (_diskByteReady)
            {
                value |= 0x8000;
            }

            if (_diskDmaActive && IsDiskDmaActuallyEnabled())
            {
                value |= 0x4000;
            }

            if (_wordEqual)
            {
                value |= 0x1000;
            }

            if (_diskWriteMode)
            {
                value |= 0x2000;
            }

            return value;
        }

        /// <summary>
        /// Returns whether Word sync Enabled is true.
        /// </summary>
        private bool IsWordSyncEnabled()
        {
            ushort adkcon = _readAdkControl != null ? _readAdkControl() : (ushort)0;
            return (adkcon & 0x0400) != 0;
        }

        /// <summary>
        /// Returns whether Disk DMA Actually Enabled is true.
        /// </summary>
        private bool IsDiskDmaActuallyEnabled()
        {
            ushort dmacon = _readDmaControl != null ? _readDmaControl() : (ushort)0x0210;
            return (dmacon & 0x0210) == 0x0210;
        }

        /// <summary>
        /// Provides Audio Channel behavior used by the emulator.
        /// </summary>
        private sealed class AudioChannel
        {
            /// <summary>
            /// Tracks the index.
            /// </summary>
            private readonly int _index;
            /// <summary>
            /// Tracks the DMA Enabled.
            /// </summary>
            private bool _dmaEnabled;
            /// <summary>
            /// Tracks the DMA Was Enabled.
            /// </summary>
            private bool _dmaWasEnabled;
            /// <summary>
            /// Tracks the has Current Word.
            /// </summary>
            private bool _hasCurrentWord;
            /// <summary>
            /// Tracks the has Pending Word.
            /// </summary>
            private bool _hasPendingWord;
            /// <summary>
            /// Tracks the next Byte High.
            /// </summary>
            private bool _nextByteHigh;
            /// <summary>
            /// Tracks the period Counter.
            /// </summary>
            private int _periodCounter;
            /// <summary>
            /// Tracks the current Sample.
            /// </summary>
            private int _currentSample;

            /// <summary>
            /// Initializes a new instance of the AudioChannel class.
            /// </summary>
            public AudioChannel(int index)
            {
                _index = index;
                Reset();
            }

            /// <summary>
            /// Gets or updates the Location.
            /// </summary>
            public uint Location { get; set; }

            /// <summary>
            /// Gets or updates the Current Pointer.
            /// </summary>
            public uint CurrentPointer { get; private set; }

            /// <summary>
            /// Gets or updates the Length.
            /// </summary>
            public ushort Length { get; set; }

            /// <summary>
            /// Gets or updates the Period.
            /// </summary>
            public ushort Period { get; set; }

            /// <summary>
            /// Gets or updates the Volume.
            /// </summary>
            public ushort Volume { get; set; }

            /// <summary>
            /// Gets or updates the Data.
            /// </summary>
            public ushort Data { get; private set; }

            /// <summary>
            /// Gets or updates the Block Completed.
            /// </summary>
            public bool BlockCompleted { get; private set; }

            /// <summary>
            /// Executes the Output logic.
            /// </summary>
            public int Output
            {
                get { return _currentSample * EffectiveVolume; }
            }

            /// <summary>
            /// Executes the Needs DMA Word logic.
            /// </summary>
            public bool NeedsDmaWord
            {
                get { return _dmaEnabled && !_hasPendingWord && RemainingWords > 0; }
            }

            /// <summary>
            /// Gets or updates the Remaining Words.
            /// </summary>
            public int RemainingWords { get; private set; }

            /// <summary>
            /// Resets the value state.
            /// </summary>
            public void Reset()
            {
                Location = 0;
                CurrentPointer = 0;
                Length = 0;
                Period = 0;
                Volume = 0;
                Data = 0;
                RemainingWords = 0;
                BlockCompleted = false;
                _dmaEnabled = false;
                _dmaWasEnabled = false;
                _hasCurrentWord = false;
                _hasPendingWord = false;
                _nextByteHigh = true;
                _periodCounter = 1;
                _currentSample = 0;
            }

            /// <summary>
            /// Updates the DMA Enabled.
            /// </summary>
            public void SetDmaEnabled(bool enabled)
            {
                _dmaEnabled = enabled;
                if (!enabled)
                {
                    _dmaWasEnabled = false;
                    RemainingWords = 0;
                    _hasPendingWord = false;
                    BlockCompleted = false;
                }
            }

            /// <summary>
            /// Executes the Ensure DMA Started logic.
            /// </summary>
            public void EnsureDmaStarted()
            {
                if (_dmaWasEnabled)
                {
                    return;
                }

                ReloadDmaBlock();
                _hasCurrentWord = false;
                _hasPendingWord = false;
                _nextByteHigh = true;
                _dmaWasEnabled = true;
            }

            /// <summary>
            /// Executes the Reload DMA Block logic.
            /// </summary>
            public void ReloadDmaBlock()
            {
                CurrentPointer = Location;
                RemainingWords = Length == 0 ? 0x10000 : Length;
                BlockCompleted = false;
            }

            /// <summary>
            /// Executes the Accept DMA Word logic.
            /// </summary>
            public void AcceptDmaWord(ushort word)
            {
                Data = word;
                _hasPendingWord = true;
                CurrentPointer += 2u;
                RemainingWords--;
                BlockCompleted = RemainingWords <= 0;
            }

            /// <summary>
            /// Writes a manual audio data word into the channel state.
            /// </summary>
            public void WriteData(ushort word)
            {
                Data = word;
                _hasPendingWord = true;
            }

            /// <summary>
            /// Advances the Period state by one emulator tick.
            /// </summary>
            public void TickPeriod()
            {
                int period = Period;
                if (period < 1)
                {
                    period = 1;
                }

                _periodCounter--;
                if (_periodCounter > 0)
                {
                    return;
                }

                _periodCounter = period;
                AdvanceSample();
            }

            /// <summary>
            /// Returns the Debug Info.
            /// </summary>
            public string GetDebugInfo(bool dmaEnabled)
            {
                return string.Format(
                    "dma:{0} lc:${1:X6} cur:${2:X6} len:{3} rem:{4} per:{5} vol:{6} dat:${7:X4} out:{8}",
                    dmaEnabled,
                    Location,
                    CurrentPointer,
                    Length,
                    RemainingWords,
                    Period,
                    EffectiveVolume,
                    Data,
                    _currentSample);
            }

            /// <summary>
            /// Executes the Effective Volume logic.
            /// </summary>
            private int EffectiveVolume
            {
                get
                {
                    int volume = Volume & 0x007F;
                    return volume > 64 ? 64 : volume;
                }
            }

            /// <summary>
            /// Executes the Advance Sample logic.
            /// </summary>
            private void AdvanceSample()
            {
                if (!_hasCurrentWord)
                {
                    if (!_hasPendingWord)
                    {
                        return;
                    }

                    _hasPendingWord = false;
                    _hasCurrentWord = true;
                    _nextByteHigh = true;
                }

                byte raw = _nextByteHigh ? (byte)(Data >> 8) : (byte)(Data & 0xFF);
                _currentSample = unchecked((sbyte)raw);
                _nextByteHigh = !_nextByteHigh;
                if (_nextByteHigh)
                {
                    _hasCurrentWord = false;
                }
            }
        }
    }
}
