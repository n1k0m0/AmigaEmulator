// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;

namespace AmigaEmulator.Core.Input
{
    /// <summary>
    /// Ideal, nominal A1000 motherboard KCLK reset detector. This accepts the
    /// external keyboard clock pin; it does not invent a keyboard MCU or ROM.
    /// The 22 kohm/10 uF detector and 100 kohm/10 uF reset stretcher follow
    /// Commodore drawing 252276, sheet 1 (LM2901 network). Component tolerances,
    /// the power-on diode and keyboard reset-warning protocol are separate.
    /// </summary>
    public sealed class A1000KeyboardReset
    {
        private const double Supply = 5.0;
        private const double DetectThreshold = 2.499;
        private const double HeldThreshold = 2.501;
        private const double ReleaseThreshold = 2.526;
        private readonly int _eClockHz;
        private readonly double _detectorDecay, _stretcherDecay;
        private double _detectorVoltage;
        private double _stretcherVoltage = Supply;
        public bool ClockHigh { get; private set; } = true;
        public bool Asserted { get; private set; }

        public A1000KeyboardReset(int eClockHz)
        {
            if (eClockHz < 1) throw new ArgumentOutOfRangeException(nameof(eClockHz));
            _eClockHz = eClockHz;
            // Exact RC evolution over one E-clock; no per-tick exponentiation.
            _detectorDecay = Math.Exp(-1.0 / (eClockHz * 0.22));
            _stretcherDecay = Math.Exp(-1.0 / eClockHz);
        }

        public void DriveClock(bool high)
        {
            ClockHigh = high;
            if (high) _detectorVoltage = 0; // Low-impedance comparator discharge.
        }

        public void TickEClock()
        {
            if (!ClockHigh)
                _detectorVoltage = Supply - (Supply - _detectorVoltage) * _detectorDecay;
            if (!Asserted && _detectorVoltage >= DetectThreshold)
            {
                Asserted = true;
                _stretcherVoltage = 0;
            }
            if (Asserted && _detectorVoltage >= HeldThreshold)
                _stretcherVoltage = 0;
            else
                _stretcherVoltage = Supply - (Supply - _stretcherVoltage) * _stretcherDecay;
            if (Asserted && _stretcherVoltage >= ReleaseThreshold)
            {
                Asserted = false;
                // Releasing reset lowers the detector threshold again. A clock
                // voltage still inside the hysteresis band retriggers the pulse.
                if (_detectorVoltage >= DetectThreshold)
                {
                    Asserted = true;
                    _stretcherVoltage = 0;
                }
            }
        }

        public void PowerCycle()
        {
            ClockHigh = true; Asserted = false;
            _detectorVoltage = 0; _stretcherVoltage = Supply;
        }

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(1); writer.Write(_eClockHz);
            writer.Write(ClockHigh); writer.Write(Asserted);
            writer.Write(_detectorVoltage); writer.Write(_stretcherVoltage);
        }

        public void LoadState(BinaryReader reader)
        {
            var restored = ReadStateCopy(reader);
            ClockHigh = restored.ClockHigh; Asserted = restored.Asserted;
            _detectorVoltage = restored._detectorVoltage;
            _stretcherVoltage = restored._stretcherVoltage;
        }

        internal A1000KeyboardReset ReadStateCopy(BinaryReader reader)
        {
            if (reader.ReadInt32() != 1 || reader.ReadInt32() != _eClockHz)
                throw new InvalidDataException("A1000 keyboard reset timing does not match.");
            byte clock = reader.ReadByte(), reset = reader.ReadByte();
            double detector = reader.ReadDouble(), stretcher = reader.ReadDouble();
            if (clock > 1 || reset > 1 || !double.IsFinite(detector) || !double.IsFinite(stretcher) ||
                detector < 0 || detector > Supply || stretcher < 0 || stretcher > Supply ||
                (clock != 0 && detector != 0) || (reset != 0 && stretcher >= ReleaseThreshold))
                throw new InvalidDataException("Invalid A1000 keyboard reset circuit state.");
            return new A1000KeyboardReset(_eClockHz) { ClockHigh = clock != 0, Asserted = reset != 0,
                _detectorVoltage = detector, _stretcherVoltage = stretcher };
        }
    }
}
