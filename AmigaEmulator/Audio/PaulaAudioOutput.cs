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
        /// Defines the Sample Rate constant.
        /// </summary>
        public const int SampleRate = 48000;
        /// <summary>
        /// Defines the Channel Count constant.
        /// </summary>
        private const int ChannelCount = 2;
        /// <summary>
        /// Defines the Bytes period Sample constant.
        /// </summary>
        private const int BytesPerSample = 2;
        /// <summary>
        /// Defines the Frames period Buffer constant.
        /// </summary>
        private const int FramesPerBuffer = 2048;
        /// <summary>
        /// Defines the Buffer Count constant.
        /// </summary>
        private const int BufferCount = 8;
        /// <summary>
        /// Defines the Wave Mapper constant.
        /// </summary>
        private const int WaveMapper = -1;
        /// <summary>
        /// Defines the Whdr Done constant.
        /// </summary>
        private const int WhdrDone = 0x00000001;

        /// <summary>
        /// Tracks the staging.
        /// </summary>
        private readonly byte[] _staging = new byte[FramesPerBuffer * ChannelCount * BytesPerSample];
        /// <summary>
        /// Tracks the buffers.
        /// </summary>
        private readonly WaveBuffer[] _buffers = new WaveBuffer[BufferCount];
        /// <summary>
        /// Tracks the wave Out.
        /// </summary>
        private IntPtr _waveOut;
        /// <summary>
        /// Tracks the staging Offset.
        /// </summary>
        private int _stagingOffset;
        /// <summary>
        /// Tracks the available.
        /// </summary>
        private bool _available;
        /// <summary>
        /// Tracks the submitted Frames.
        /// </summary>
        private long _submittedFrames;
        /// <summary>
        /// Tracks the dropped Frames.
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
        /// Executes the Available logic.
        /// </summary>
        public bool Available
        {
            get { return _available; }
        }

        /// <summary>
        /// Executes the Submitted Frames logic.
        /// </summary>
        public long SubmittedFrames
        {
            get { return _submittedFrames; }
        }

        /// <summary>
        /// Executes the Dropped Frames logic.
        /// </summary>
        public long DroppedFrames
        {
            get { return _droppedFrames; }
        }

        /// <summary>
        /// Executes the Submit logic.
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
        /// Executes the Clear logic.
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
        /// Executes the Dispose logic.
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
        /// Executes the Flush logic.
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
        /// Executes the Clamp To Pcm16 logic.
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
        /// Executes the wave Out Open logic.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, int dwFlags);

        /// <summary>
        /// Executes the wave Out Prepare Header logic.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        /// <summary>
        /// Executes the wave Out Unprepare Header logic.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        /// <summary>
        /// Executes the wave Out Write logic.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        /// <summary>
        /// Executes the wave Out Reset logic.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        /// <summary>
        /// Executes the wave Out Close logic.
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
            /// Stores the Format Tag.
            /// </summary>
            public ushort FormatTag;
            /// <summary>
            /// Stores the Channels.
            /// </summary>
            public ushort Channels;
            /// <summary>
            /// Stores the Samples period Sec.
            /// </summary>
            public int SamplesPerSec;
            /// <summary>
            /// Stores the Avg Bytes period Sec.
            /// </summary>
            public int AvgBytesPerSec;
            /// <summary>
            /// Stores the Block Align.
            /// </summary>
            public ushort BlockAlign;
            /// <summary>
            /// Stores the Bits period Sample.
            /// </summary>
            public ushort BitsPerSample;
            /// <summary>
            /// Stores the Size.
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
            /// Stores the Data.
            /// </summary>
            public IntPtr Data;
            /// <summary>
            /// Stores the Buffer Length.
            /// </summary>
            public int BufferLength;
            /// <summary>
            /// Stores the Bytes Recorded.
            /// </summary>
            public int BytesRecorded;
            /// <summary>
            /// Stores the User.
            /// </summary>
            public IntPtr User;
            /// <summary>
            /// Stores the Flags.
            /// </summary>
            public int Flags;
            /// <summary>
            /// Stores the Loops.
            /// </summary>
            public int Loops;
            /// <summary>
            /// Stores the Next.
            /// </summary>
            public IntPtr Next;
            /// <summary>
            /// Stores the Reserved.
            /// </summary>
            public IntPtr Reserved;
        }

        /// <summary>
        /// Provides Wave Buffer behavior used by the emulator.
        /// </summary>
        private sealed class WaveBuffer : IDisposable
        {
            /// <summary>
            /// Tracks the capacity.
            /// </summary>
            private readonly int _capacity;
            /// <summary>
            /// Tracks the data.
            /// </summary>
            private readonly IntPtr _data;
            /// <summary>
            /// Tracks the header Pointer.
            /// </summary>
            private readonly IntPtr _headerPointer;
            /// <summary>
            /// Tracks the header.
            /// </summary>
            private WaveHeader _header;
            /// <summary>
            /// Tracks the prepared.
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
            /// Executes the Header Pointer logic.
            /// </summary>
            public IntPtr HeaderPointer
            {
                get { return _headerPointer; }
            }

            /// <summary>
            /// Executes the Done logic.
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
            /// Gets or updates the In Use.
            /// </summary>
            public bool InUse { get; private set; }

            /// <summary>
            /// Executes the Reclaim If Done logic.
            /// </summary>
            public void ReclaimIfDone(IntPtr waveOut)
            {
                if (InUse && Done)
                {
                    Unprepare(waveOut);
                }
            }

            /// <summary>
            /// Executes the Prepare logic.
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
            /// Executes the Unprepare logic.
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
            /// Executes the Mark Submitted logic.
            /// </summary>
            public void MarkSubmitted()
            {
                InUse = true;
            }

            /// <summary>
            /// Executes the Dispose logic.
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
