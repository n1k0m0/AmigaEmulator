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
using System.Runtime.InteropServices;

namespace AmigaEmulator.Core.Audio
{
    /// <summary>
    /// Streams mixed Paula audio samples to the host audio device.
    /// </summary>
    public sealed class PaulaAudioOutput : IDisposable
    {
        /// <summary>
        /// Constant for Sample Rate.
        /// </summary>
        public const int SampleRate = 48000;
        /// <summary>
        /// Constant for Channel Count.
        /// </summary>
        private const int ChannelCount = 2;
        /// <summary>
        /// Constant for Bytes Per Sample.
        /// </summary>
        private const int BytesPerSample = 2;
        /// <summary>
        /// Constant for Frames Per Buffer.
        /// </summary>
        private const int FramesPerBuffer = 2048;
        /// <summary>
        /// Constant for Buffer Count.
        /// </summary>
        private const int BufferCount = 8;
        /// <summary>
        /// Constant for Wave Mapper.
        /// </summary>
        private const int WaveMapper = -1;
        /// <summary>
        /// Constant for Whdr Done.
        /// </summary>
        private const int WhdrDone = 0x00000001;

        /// <summary>
        /// Stores staging.
        /// </summary>
        private readonly byte[] _staging = new byte[FramesPerBuffer * ChannelCount * BytesPerSample];
        /// <summary>
        /// Stores buffers.
        /// </summary>
        private readonly WaveBuffer[] _buffers = new WaveBuffer[BufferCount];
        /// <summary>
        /// Stores wave Out.
        /// </summary>
        private IntPtr _waveOut;
        /// <summary>
        /// Stores staging Offset.
        /// </summary>
        private int _stagingOffset;
        /// <summary>
        /// Stores available.
        /// </summary>
        private bool _available;
        /// <summary>
        /// Stores submitted Frames.
        /// </summary>
        private long _submittedFrames;
        /// <summary>
        /// Stores dropped Frames.
        /// </summary>
        private long _droppedFrames;

        /// <summary>
        /// Initializes a new instance of the PaulaAudioOutput class.
        /// </summary>
        public PaulaAudioOutput()
        {
            var format = new WaveFormatEx
            {
                FormatTag = 1,
                Channels = ChannelCount,
                SamplesPerSec = SampleRate,
                BitsPerSample = 16,
                BlockAlign = ChannelCount * BytesPerSample,
                AvgBytesPerSec = SampleRate * ChannelCount * BytesPerSample,
                Size = 0
            };

            _available = waveOutOpen(out _waveOut, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0) == 0 && _waveOut != IntPtr.Zero;
            if (!_available)
            {
                _waveOut = IntPtr.Zero;
                return;
            }

            for (int index = 0; index < _buffers.Length; index++)
            {
                _buffers[index] = new WaveBuffer(FramesPerBuffer * ChannelCount * BytesPerSample);
            }
        }

        /// <summary>
        /// Gets Available.
        /// </summary>
        public bool Available
        {
            get { return _available; }
        }

        /// <summary>
        /// Gets Submitted Frames.
        /// </summary>
        public long SubmittedFrames
        {
            get { return _submittedFrames; }
        }

        /// <summary>
        /// Gets Dropped Frames.
        /// </summary>
        public long DroppedFrames
        {
            get { return _droppedFrames; }
        }

        /// <summary>
        /// Runs Submit.
        /// </summary>
        public void Submit(float left, float right, float volume)
        {
            if (!_available)
            {
                return;
            }

            short leftSample = ClampToPcm16(left * volume);
            short rightSample = ClampToPcm16(right * volume);
            WriteSample(leftSample);
            WriteSample(rightSample);
            _submittedFrames++;
            if (_stagingOffset >= _staging.Length)
            {
                Flush();
            }
        }

        /// <summary>
        /// Runs Clear.
        /// </summary>
        public void Clear()
        {
            if (_available)
            {
                waveOutReset(_waveOut);
            }

            _stagingOffset = 0;
        }

        /// <summary>
        /// Runs Dispose.
        /// </summary>
        public void Dispose()
        {
            if (!_available)
            {
                return;
            }

            Flush();
            waveOutReset(_waveOut);
            for (int index = 0; index < _buffers.Length; index++)
            {
                if (_buffers[index] != null)
                {
                    if (_buffers[index].InUse)
                    {
                        _buffers[index].Unprepare(_waveOut);
                    }

                    _buffers[index].Dispose();
                    _buffers[index] = null;
                }
            }

            waveOutClose(_waveOut);
            _waveOut = IntPtr.Zero;
            _available = false;
        }

        /// <summary>
        /// Runs Flush.
        /// </summary>
        private void Flush()
        {
            if (!_available || _stagingOffset <= 0)
            {
                return;
            }

            WaveBuffer buffer = FindFreeBuffer();
            if (buffer == null)
            {
                _droppedFrames += _stagingOffset / (ChannelCount * BytesPerSample);
                _stagingOffset = 0;
                return;
            }

            buffer.ReclaimIfDone(_waveOut);
            buffer.Write(_staging, _stagingOffset);
            buffer.Prepare(_waveOut);
            waveOutWrite(_waveOut, buffer.HeaderPointer, Marshal.SizeOf(typeof(WaveHeader)));
            buffer.MarkSubmitted();
            _stagingOffset = 0;
        }

        /// <summary>
        /// Finds Free Buffer in the available emulator data.
        /// </summary>
        private WaveBuffer FindFreeBuffer()
        {
            for (int index = 0; index < _buffers.Length; index++)
            {
                WaveBuffer buffer = _buffers[index];
                if (buffer != null && buffer.Done)
                {
                    return buffer;
                }
            }

            return null;
        }

        /// <summary>
        /// Writes one signed PCM sample into the circular host audio buffer.
        /// </summary>
        private void WriteSample(short sample)
        {
            _staging[_stagingOffset++] = (byte)(sample & 0xFF);
            _staging[_stagingOffset++] = (byte)((sample >> 8) & 0xFF);
        }

        /// <summary>
        /// Runs Clamp To Pcm 16.
        /// </summary>
        private static short ClampToPcm16(float value)
        {
            if (value > 1.0f)
            {
                value = 1.0f;
            }
            else if (value < -1.0f)
            {
                value = -1.0f;
            }

            return (short)(value * 32767.0f);
        }

        /// <summary>
        /// Runs wave Out Open.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, int dwFlags);

        /// <summary>
        /// Runs wave Out Prepare Header.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        /// <summary>
        /// Runs wave Out Unprepare Header.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        /// <summary>
        /// Runs wave Out Write.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        /// <summary>
        /// Runs wave Out Reset.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        /// <summary>
        /// Runs wave Out Close.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);

        /// <summary>
        /// Stores Wave Format Ex data used by the emulator.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            /// <summary>
            /// Stores Format Tag.
            /// </summary>
            public ushort FormatTag;
            /// <summary>
            /// Stores Channels.
            /// </summary>
            public ushort Channels;
            /// <summary>
            /// Stores Samples period Sec.
            /// </summary>
            public int SamplesPerSec;
            /// <summary>
            /// Stores Avg Bytes period Sec.
            /// </summary>
            public int AvgBytesPerSec;
            /// <summary>
            /// Stores Block Align.
            /// </summary>
            public ushort BlockAlign;
            /// <summary>
            /// Stores Bits period Sample.
            /// </summary>
            public ushort BitsPerSample;
            /// <summary>
            /// Stores Size.
            /// </summary>
            public ushort Size;
        }

        /// <summary>
        /// Stores Wave Header data used by the emulator.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            /// <summary>
            /// Stores Data.
            /// </summary>
            public IntPtr Data;
            /// <summary>
            /// Stores Buffer Length.
            /// </summary>
            public int BufferLength;
            /// <summary>
            /// Stores Bytes Recorded.
            /// </summary>
            public int BytesRecorded;
            /// <summary>
            /// Stores User.
            /// </summary>
            public IntPtr User;
            /// <summary>
            /// Stores Flags.
            /// </summary>
            public int Flags;
            /// <summary>
            /// Stores Loops.
            /// </summary>
            public int Loops;
            /// <summary>
            /// Stores Next.
            /// </summary>
            public IntPtr Next;
            /// <summary>
            /// Stores Reserved.
            /// </summary>
            public IntPtr Reserved;
        }

        /// <summary>
        /// Provides Wave Buffer functionality.
        /// </summary>
        private sealed class WaveBuffer : IDisposable
        {
            /// <summary>
            /// Stores capacity.
            /// </summary>
            private readonly int _capacity;
            /// <summary>
            /// Stores data.
            /// </summary>
            private readonly IntPtr _data;
            /// <summary>
            /// Stores header Pointer.
            /// </summary>
            private readonly IntPtr _headerPointer;
            /// <summary>
            /// Stores header.
            /// </summary>
            private WaveHeader _header;
            /// <summary>
            /// Stores prepared.
            /// </summary>
            private bool _prepared;

            /// <summary>
            /// Initializes a new instance of the WaveBuffer class.
            /// </summary>
            public WaveBuffer(int capacity)
            {
                _capacity = capacity;
                _data = Marshal.AllocHGlobal(capacity);
                _headerPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHeader)));
                _header = new WaveHeader
                {
                    Data = _data,
                    BufferLength = capacity,
                    Flags = WhdrDone
                };
                WriteHeader();
            }

            /// <summary>
            /// Gets Header Pointer.
            /// </summary>
            public IntPtr HeaderPointer
            {
                get { return _headerPointer; }
            }

            /// <summary>
            /// Gets Done.
            /// </summary>
            public bool Done
            {
                get
                {
                    if (!InUse)
                    {
                        return true;
                    }

                    _header = Marshal.PtrToStructure<WaveHeader>(_headerPointer);
                    return (_header.Flags & WhdrDone) != 0;
                }
            }

            /// <summary>
            /// Gets or sets In Use.
            /// </summary>
            public bool InUse { get; private set; }

            /// <summary>
            /// Runs Reclaim If Done.
            /// </summary>
            public void ReclaimIfDone(IntPtr waveOut)
            {
                if (InUse && Done)
                {
                    Unprepare(waveOut);
                }
            }

            /// <summary>
            /// Runs Prepare.
            /// </summary>
            public void Prepare(IntPtr waveOut)
            {
                if (_prepared)
                {
                    return;
                }

                waveOutPrepareHeader(waveOut, _headerPointer, Marshal.SizeOf(typeof(WaveHeader)));
                _prepared = true;
            }

            /// <summary>
            /// Runs Unprepare.
            /// </summary>
            public void Unprepare(IntPtr waveOut)
            {
                if (!_prepared)
                {
                    return;
                }

                waveOutUnprepareHeader(waveOut, _headerPointer, Marshal.SizeOf(typeof(WaveHeader)));
                _prepared = false;
                InUse = false;
            }

            /// <summary>
            /// Copies PCM bytes from Paula mixing into the buffered wave provider.
            /// </summary>
            public void Write(byte[] source, int count)
            {
                count = Math.Min(count, _capacity);
                Marshal.Copy(source, 0, _data, count);
                if (count < _capacity)
                {
                    byte[] silence = new byte[_capacity - count];
                    Marshal.Copy(silence, 0, _data + count, silence.Length);
                }

                _header = Marshal.PtrToStructure<WaveHeader>(_headerPointer);
                _header.Flags &= ~WhdrDone;
                WriteHeader();
            }

            /// <summary>
            /// Runs Mark Submitted.
            /// </summary>
            public void MarkSubmitted()
            {
                InUse = true;
            }

            /// <summary>
            /// Runs Dispose.
            /// </summary>
            public void Dispose()
            {
                Marshal.FreeHGlobal(_data);
                Marshal.FreeHGlobal(_headerPointer);
            }

            /// <summary>
            /// Writes the RIFF/WAVE header for a captured audio stream.
            /// </summary>
            private void WriteHeader()
            {
                Marshal.StructureToPtr(_header, _headerPointer, false);
            }
        }
    }
}
