// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;

namespace AmigaEmulator.Core.Audio
{
    /// <summary>
    /// Independent stereo output stages at the host sample rate. A500 uses the
    /// fixed 6.2/20 kHz stages, A1200 bypasses them. The three 7 kHz LED stages
    /// follow CIA-A in hardware profiles; Led explicitly forces them on.
    /// Frequencies are circuit approximations, not a claim of analog calibration.
    /// </summary>
    public sealed class PaulaOutputFilter
    {
        private readonly Channel _left, _right;
        public AmigaPaulaFilter Mode { get; set; } = AmigaPaulaFilter.A500;

        public PaulaOutputFilter(int sampleRate = 48000)
        {
            if (sampleRate < 40000 || sampleRate > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            _left = new Channel(sampleRate); _right = new Channel(sampleRate);
        }

        public void Process(ref float left, ref float right, bool ledEnabled)
        {
            bool fixedStages = Mode == AmigaPaulaFilter.A500 || Mode == AmigaPaulaFilter.Led;
            bool ledStages = Mode == AmigaPaulaFilter.Led || (Mode != AmigaPaulaFilter.Off && ledEnabled);
            left = _left.Process(left, fixedStages, ledStages);
            right = _right.Process(right, fixedStages, ledStages);
        }

        public void Reset() { _left.Reset(); _right.Reset(); }

        private sealed class Channel
        {
            private readonly Pole _fixed1, _fixed2, _led1, _led2, _led3;
            public Channel(int rate)
            {
                _fixed1 = new Pole(rate, 6200); _fixed2 = new Pole(rate, 20000);
                _led1 = new Pole(rate, 7000); _led2 = new Pole(rate, 7000); _led3 = new Pole(rate, 7000);
            }
            public float Process(float input, bool fixedStages, bool ledStages)
            {
                // Keep both paths warm so switching the software LED does not
                // restart a filter from silence or bleed the opposite channel.
                float fixedOutput = _fixed2.Process(_fixed1.Process(input));
                float normal = fixedStages ? fixedOutput : input;
                float filtered = _led3.Process(_led2.Process(_led1.Process(normal)));
                return ledStages ? filtered : normal;
            }
            public void Reset() { _fixed1.Reset(); _fixed2.Reset(); _led1.Reset(); _led2.Reset(); _led3.Reset(); }
        }

        private sealed class Pole
        {
            private readonly float _feedforward, _feedback;
            private float _previousInput, _previousOutput;
            public Pole(int rate, int cutoff)
            {
                double k = Math.Tan(Math.PI * cutoff / rate);
                _feedforward = (float)(k / (1 + k));
                _feedback = (float)((1 - k) / (1 + k));
            }
            public float Process(float input)
            {
                float output = _feedforward * (input + _previousInput) + _feedback * _previousOutput;
                // Flush inaudible tails to avoid slow subnormal arithmetic.
                if (Math.Abs(output) < 1e-20f) output = 0;
                _previousInput = input; _previousOutput = output;
                return output;
            }
            public void Reset() { _previousInput = _previousOutput = 0; }
        }
    }
}
