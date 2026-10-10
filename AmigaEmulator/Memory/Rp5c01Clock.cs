// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Diagnostics;
using System.IO;

namespace AmigaEmulator.Core.Memory
{
    /// <summary>Ricoh RP5C01 calendar and battery RAM on the desktop motherboard.
    /// Alarm/test frequency outputs have no connection to the emulated interrupt lines.
    /// Register semantics follow Ricoh EK-086-9804, pages 5/6.</summary>
    public sealed class Rp5c01Clock
    {
        private readonly Func<DateTime> _now;
        private readonly byte[] _banks = new byte[52];
        private DateTime _date, _anchor;
        private byte _mode = 8, _test, _reset = 12;
        private bool _planning;
        public bool Enabled { get; }
        private bool Running => (_mode & 8) != 0;
        private bool FullDay => (_banks[23] & 1) != 0;

        public Rp5c01Clock(bool enabled, Func<DateTime> now = null)
        {
            Enabled = enabled;
            if (now == null)
            {
                var origin = DateTime.Now; var timer = Stopwatch.StartNew();
                now = () => origin + timer.Elapsed;
            }
            _now = now; _anchor = _now();
            _date = _anchor.AddTicks(-(_anchor.Ticks % TimeSpan.TicksPerSecond));
            _banks[23] = 1; _banks[24] = (byte)(_date.Year & 3);
            _banks[6] = (byte)_date.DayOfWeek; Encode();
        }

        public bool Maps(uint address) => Enabled && (address & 0xFFFF0000u) == 0xDC0000u;
        internal void BeginPlanning() => _planning = true;
        internal void EndPlanning() => _planning = false;
        public byte ReadByte(uint address)
        {
            if (!Maps(address) || (address & 3) != 3) return 0xFF;
            Synchronize(); int index = (int)((address >> 2) & 15);
            return index == 13 ? _mode : index >= 14 ? (byte)0 : _banks[(_mode & 3) * 13 + index];
        }

        public void WriteByte(uint address, byte value)
        {
            if (_planning || !Maps(address) || (address & 3) != 3) return;
            Synchronize(); value &= 15; int index = (int)((address >> 2) & 15);
            int bank = _mode & 3;
            if (index == 13)
            {
                bool running = Running; _mode = value;
                if (!running && Running) Decode();
                if (running != Running) _anchor = _now();
                return;
            }
            if (index == 14) { _test = value; return; }
            if (index == 15)
            {
                _reset = value;
                if ((value & 1) != 0) Array.Clear(_banks, 13, 9);
                if ((value & 2) != 0) _anchor = _now();
                return;
            }
            if (bank == 1)
            {
                byte mask = index switch { 2 or 4 or 7 => 15, 3 => 7, 5 or 8 or 11 => 3, 6 => 7, 10 => 1, _ => 0 };
                bool modeChanged = index == 10 && ((_banks[23] ^ value) & 1) != 0;
                if (modeChanged) Decode();
                _banks[13 + index] = (byte)(value & mask);
                if (modeChanged) Encode();
            }
            else
            {
                int mask = bank != 0 ? 15 : index switch { 1 or 3 or 6 => 7, 5 or 8 => 3, 10 => 1, _ => 15 };
                _banks[bank * 13 + index] = (byte)(value & mask);
                if (bank == 0 && Running) Decode();
            }
        }

        private void Synchronize()
        {
            if (!Running) return;
            long seconds = (_now().Ticks - _anchor.Ticks) / TimeSpan.TicksPerSecond;
            if (seconds <= 0) return;
            Advance(seconds); _anchor = _anchor.AddSeconds(seconds); Encode();
        }
        private void Advance(long seconds)
        {
            DateTime old = _date; _date = _date.AddSeconds(seconds);
            int days = (int)(_date.Date - old.Date).TotalDays;
            _banks[6] = (byte)(((_banks[6] + days) % 7 + 7) % 7);
            _banks[24] = (byte)((_banks[24] + _date.Year - old.Year) & 3);
        }
        private void Encode()
        {
            void Pair(int i, int n) { _banks[i] = (byte)(n % 10); _banks[i + 1] = (byte)(n / 10); }
            Pair(0, _date.Second); Pair(2, _date.Minute);
            Pair(4, FullDay ? _date.Hour : _date.Hour % 12);
            if (!FullDay && _date.Hour >= 12) _banks[5] |= 2;
            Pair(7, _date.Day); Pair(9, _date.Month); Pair(11, _date.Year % 100);
        }
        private void Decode()
        {
            int Pair(int i) => _banks[i] + 10 * _banks[i + 1];
            int hour = FullDay ? Pair(4) : _banks[4] + 10 * (_banks[5] & 1) + ((_banks[5] & 2) != 0 ? 12 : 0);
            int year = Pair(11); year += year >= 78 ? 1900 : 2000;
            try { _date = new DateTime(year, Pair(9), Pair(7), hour, Pair(2), Pair(0)); }
            catch (ArgumentOutOfRangeException) { return; }
            _anchor = _now();
        }

        public void SaveState(BinaryWriter writer)
        {
            Synchronize(); writer.Write(_date.Ticks); writer.Write(_banks);
            writer.Write(_mode); writer.Write(_test); writer.Write(_reset);
            writer.Write(Running ? Math.Clamp(_now().Ticks - _anchor.Ticks, 0, TimeSpan.TicksPerSecond - 1) : 0L);
        }
        public void LoadState(BinaryReader reader)
        {
            DateTime date = new DateTime(reader.ReadInt64()); byte[] banks = reader.ReadBytes(52);
            if (banks.Length != 52) throw new EndOfStreamException();
            byte mode = reader.ReadByte(), test = reader.ReadByte(), reset = reader.ReadByte(); long phase = reader.ReadInt64();
            foreach (byte b in banks) if (b > 15) throw new InvalidDataException("Invalid Ricoh clock register.");
            if (mode > 15 || test > 15 || reset > 15 || phase < 0 || phase >= TimeSpan.TicksPerSecond)
                throw new InvalidDataException("Invalid Ricoh clock state.");
            _date = date; Array.Copy(banks, _banks, 52); _mode = mode; _test = test; _reset = reset;
            _anchor = _now().AddTicks(-phase); _planning = false;
        }
        public void SaveBattery(string path, DateTime? utcNow = null)
        {
            if (!Enabled) return;
            string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
                    writer.Write("AMIGA-RICOH-1"); writer.Write((utcNow ?? DateTime.UtcNow).Ticks); SaveState(writer);
                    writer.Flush(); stream.Flush(true);
                }
                File.Move(temporary, full, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void LoadBattery(string path, DateTime? utcNow = null)
        {
            if (!Enabled || !File.Exists(path)) return;
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadString() != "AMIGA-RICOH-1") throw new InvalidDataException("Invalid Ricoh battery file.");
            long saved = reader.ReadInt64();
            if (saved < 0 || saved > DateTime.MaxValue.Ticks || reader.BaseStream.Length - reader.BaseStream.Position != 71)
                throw new InvalidDataException("Invalid Ricoh battery file size/time.");
            // Validate in a separate device so malformed storage cannot alter the live clock.
            var restored = new Rp5c01Clock(true, _now); restored.LoadState(reader);
            if (restored.Running)
            {
                long elapsed = Math.Max(0, (utcNow ?? DateTime.UtcNow).Ticks - saved) + (_now().Ticks - restored._anchor.Ticks);
                restored.Advance(elapsed / TimeSpan.TicksPerSecond); restored.Encode();
                restored._anchor = _now().AddTicks(-(elapsed % TimeSpan.TicksPerSecond));
            }
            _date = restored._date; _anchor = restored._anchor; _mode = restored._mode;
            _test = restored._test; _reset = restored._reset; Array.Copy(restored._banks, _banks, 52); _planning = false;
        }
    }
}
