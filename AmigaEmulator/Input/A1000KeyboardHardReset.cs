// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;

namespace AmigaEmulator.Core.Input
{
    /// <summary>
    /// Protocol-level external keyboard personality without the optional reset
    /// warning. This is the HRM hard-reset pulse, not a keyboard MCU/ROM emulator.
    /// Motherboard reset must not reset this independently powered transmitter.
    /// </summary>
    public sealed class A1000KeyboardHardReset
    {
        private readonly int _eClockHz;
        private readonly int _minimumTicks;
        private byte _held;
        private bool _pending;
        private int _remaining;
        public bool ClockLow { get; private set; }

        public A1000KeyboardHardReset(int eClockHz)
        {
            if (eClockHz <= 0) throw new ArgumentOutOfRangeException(nameof(eClockHz));
            _eClockHz = eClockHz;
            _minimumTicks = checked((eClockHz + 1) / 2);
        }

        private static byte KeyBit(byte rawKey) => (rawKey & 0x7F) switch
        {
            0x63 => 1, // Control
            0x66 => 2, // Left Amiga
            0x67 => 4, // Right Amiga
            _ => 0
        };

        public void KeyChanged(byte rawKey, bool down)
        {
            byte bit = KeyBit(rawKey), previous = _held;
            if (down) _held |= bit; else _held &= (byte)~bit;
            // Once recognized, the chord is latched across the final serial
            // edges. A quick release must not cancel its minimum reset pulse.
            if (!ClockLow && _held == 7 && previous != 7) _pending = true;
        }

        // Called before ticking the motherboard detector, including while it
        // holds the CPU. Finish a previously queued serial byte before starting.
        // Returns true exactly when the keyboard releases KCLK and restarts.
        public bool TickEClock(bool serialInputPending)
        {
            if (!ClockLow)
            {
                if (_pending && !serialInputPending)
                {
                    _pending = false;
                    ClockLow = true;
                    _remaining = _minimumTicks;
                }
                return false;
            }
            if (_remaining > 0) _remaining--;
            if (_remaining != 0 || _held == 7) return false;
            ClockLow = false;
            return true;
        }

        public void PowerCycle() { _held = 0; _pending = false; _remaining = 0; ClockLow = false; }

        internal void ImportHeldKeys(bool control, bool leftAmiga, bool rightAmiga)
        {
            _held = (byte)((control ? 1 : 0) | (leftAmiga ? 2 : 0) | (rightAmiga ? 4 : 0));
            _pending = false; // A legacy snapshot cannot invent a new reset edge.
        }

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(1); writer.Write(_eClockHz); writer.Write(_held);
            writer.Write(_pending); writer.Write(ClockLow); writer.Write(_remaining);
        }

        internal A1000KeyboardHardReset ReadStateCopy(BinaryReader reader)
        {
            int version = reader.ReadInt32(), clock = reader.ReadInt32();
            byte held = reader.ReadByte(), pending = reader.ReadByte(), low = reader.ReadByte();
            int remaining = reader.ReadInt32();
            if (version != 1 || clock != _eClockHz || held > 7 || pending > 1 || low > 1 ||
                remaining < 0 || remaining > _minimumTicks ||
                (low == 0 && remaining != 0) || (pending != 0 && low != 0))
                throw new InvalidDataException("Invalid A1000 external keyboard hard-reset state.");
            return new A1000KeyboardHardReset(_eClockHz) { _held = held, _pending = pending != 0,
                ClockLow = low != 0, _remaining = remaining };
        }
    }
}
