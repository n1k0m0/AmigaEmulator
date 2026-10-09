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
using System.Diagnostics;
using System.IO;

namespace AmigaEmulator.Core.Memory
{
    /// <summary>MSM6242 calendar registers used by the A500+ battery clock.
    /// Time advances lazily on device access, outside the chip-cycle hot path.
    /// Pulse output/test mode are not connected to the Amiga interrupt controller.
    /// </summary>
    public sealed class Msm6242Clock
    {
        private readonly Func<DateTime> _now;
        private readonly byte[] _registers = new byte[16];
        private DateTime _anchor;
        private DateTime _date;
        private bool _planning;
        internal long SavedDividerPhase { get; private set; }
        public bool Enabled { get; }
        private bool Frozen => (_registers[13] & 1) != 0 || (_registers[15] & 3) != 0;

        public Msm6242Clock(bool enabled, Func<DateTime> now = null)
        {
            Enabled = enabled;
            if (now == null)
            {
                var origin = DateTime.Now;
                var timer = Stopwatch.StartNew();
                now = () => origin + timer.Elapsed;
            }
            _now = now;
            _anchor = _now();
            _date = _anchor.AddTicks(-(_anchor.Ticks % TimeSpan.TicksPerSecond));
            _registers[15] = 4; // 24-hour mode.
            _registers[12] = (byte)_date.DayOfWeek;
            EncodeDate();
        }

        public bool Maps(uint address) => Enabled && (address & 0xFFFF0000u) == 0xDC0000u;
        internal void BeginPlanning() => _planning = true;
        internal void EndPlanning() => _planning = false;

        public byte ReadByte(uint address)
        {
            if (!Maps(address) || (address & 1) == 0) return 0xFF;
            Synchronize();
            return _registers[(address >> 2) & 15];
        }

        public void WriteByte(uint address, byte value)
        {
            if (_planning || !Maps(address) || (address & 1) == 0) return;
            Synchronize();
            int index = (int)((address >> 2) & 15);
            bool frozen = Frozen;
            value &= 15;
            if (index == 13)
            {
                // BUSY is always clear: calendar updates here are atomic.
                _registers[13] = (byte)(value & 1);
                if ((value & 8) != 0)
                {
                    AdvanceSeconds(_date.Second >= 30 ? 60 - _date.Second : -_date.Second);
                    EncodeDate();
                }
            }
            else if (index == 15)
            {
                bool modeChanged = ((_registers[15] ^ value) & 4) != 0;
                if (modeChanged) DecodeDate();
                _registers[15] = value;
                if ((value & 1) != 0) _anchor = _now();
                if (modeChanged) EncodeDate();
            }
            else
            {
                byte mask = index switch { 1 or 3 => 7, 5 => 7, 7 => 3, 9 => 1, 12 => 7, _ => 15 };
                _registers[index] = (byte)(value & mask);
                if (index < 13 && !Frozen) DecodeDate();
            }
            if (frozen && !Frozen) DecodeDate();
            if (frozen != Frozen) _anchor = _now();
        }

        private void Synchronize()
        {
            if (Frozen) return;
            var now = _now();
            long seconds = (now.Ticks - _anchor.Ticks) / TimeSpan.TicksPerSecond;
            if (seconds <= 0) return;
            AdvanceSeconds(seconds);
            _anchor = _anchor.AddSeconds(seconds);
            EncodeDate();
        }

        private void EncodeDate()
        {
            void Pair(int index, int value) { _registers[index] = (byte)(value % 10); _registers[index + 1] = (byte)(value / 10); }
            Pair(0, _date.Second); Pair(2, _date.Minute);
            bool fullDay = (_registers[15] & 4) != 0;
            int hour = fullDay ? _date.Hour : (_date.Hour % 12 == 0 ? 12 : _date.Hour % 12);
            Pair(4, hour);
            if (!fullDay && _date.Hour >= 12) _registers[5] |= 4;
            Pair(6, _date.Day); Pair(8, _date.Month); Pair(10, _date.Year % 100);
        }

        private void AdvanceSeconds(long seconds)
        {
            var oldDay = _date.Date;
            _date = _date.AddSeconds(seconds);
            int days = (int)(_date.Date - oldDay).TotalDays;
            if (days != 0) _registers[12] = (byte)(((_registers[12] + days) % 7 + 7) % 7);
        }

        /// <summary>Battery-backed calendar, distinct from a paused savestate.
        /// Elapsed UTC time between application sessions advances a running clock.</summary>
        public void SaveBattery(string path, DateTime? utcNow = null)
        {
            if (!Enabled) return;
            Synchronize();
            long phase = Frozen ? 0 : Math.Clamp(_now().Ticks - _anchor.Ticks, 0, TimeSpan.TicksPerSecond - 1);
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
                    writer.Write("AMIGA-RTC-1"); writer.Write((utcNow ?? DateTime.UtcNow).Ticks);
                    writer.Write(_date.Ticks); writer.Write(_registers); writer.Write(phase); writer.Flush(); stream.Flush(true);
                }
                File.Move(temporary, fullPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void LoadBattery(string path, DateTime? utcNow = null)
        {
            if (!Enabled || !File.Exists(path)) return;
            using var stream = File.OpenRead(path);
            if (stream.Length != 52) throw new InvalidDataException("Invalid battery-clock file size.");
            using var reader = new BinaryReader(stream);
            if (reader.ReadString() != "AMIGA-RTC-1") throw new InvalidDataException("Invalid battery-clock file.");
            long timestamp = reader.ReadInt64(); var date = new DateTime(reader.ReadInt64());
            byte[] registers = reader.ReadBytes(16); long phase = reader.ReadInt64();
            if (timestamp < 0 || timestamp > DateTime.MaxValue.Ticks || phase < 0 || phase >= TimeSpan.TicksPerSecond)
                throw new InvalidDataException("Invalid battery-clock time.");
            foreach (byte value in registers) if (value > 15) throw new InvalidDataException("Invalid battery-clock register.");
            bool frozen = (registers[13] & 1) != 0 || (registers[15] & 3) != 0;
            if (!frozen)
            {
                long elapsed = Math.Max(0, (utcNow ?? DateTime.UtcNow).Ticks - timestamp) + phase;
                long seconds = elapsed / TimeSpan.TicksPerSecond;
                var advanced = date.AddSeconds(seconds);
                int days = (int)(advanced.Date - date.Date).TotalDays;
                registers[12] = (byte)((registers[12] + days) % 7);
                date = advanced; phase = elapsed % TimeSpan.TicksPerSecond;
            }
            _date = date; Array.Copy(registers, _registers, 16);
            _anchor = _now().AddTicks(-phase); _planning = false;
            if (!frozen) EncodeDate();
        }

        private void DecodeDate()
        {
            int Pair(int index) => _registers[index] + 10 * _registers[index + 1];
            int hour = _registers[4] + 10 * (_registers[5] & 3);
            if ((_registers[15] & 4) == 0) hour = hour % 12 + ((_registers[5] & 4) != 0 ? 12 : 0);
            int year = Pair(10);
            // AmigaOS battery-clock epoch: 1978 through 2077.
            year += year >= 78 ? 1900 : 2000;
            try { _date = new DateTime(year, Pair(8), Pair(6), hour, Pair(2), Pair(0)); }
            catch (ArgumentOutOfRangeException) { return; } // Incomplete guest programming sequence.
            _anchor = _now();
        }

        public void SaveState(BinaryWriter writer, bool includeDivider = false)
        {
            Synchronize();
            SavedDividerPhase = Frozen ? 0 : Math.Clamp(_now().Ticks - _anchor.Ticks, 0, TimeSpan.TicksPerSecond - 1);
            writer.Write(_date.Ticks);
            writer.Write(_registers);
            if (includeDivider) writer.Write(SavedDividerPhase);
        }

        public void LoadState(BinaryReader reader, bool includeDivider = false)
        {
            var date = new DateTime(reader.ReadInt64());
            var registers = reader.ReadBytes(16);
            if (registers.Length != 16) throw new EndOfStreamException();
            for (int i = 0; i < registers.Length; i++)
                if (registers[i] > 15) throw new InvalidDataException("Invalid battery-clock register.");
            _date = date;
            Array.Copy(registers, _registers, 16);
            _anchor = _now();
            _planning = false;
            if (includeDivider) RestoreDividerPhase(reader.ReadInt64());
        }

        internal void RestoreDividerPhase(long ticks)
        {
            if (ticks < 0 || ticks >= TimeSpan.TicksPerSecond) throw new InvalidDataException("Invalid clock divider phase.");
            _anchor = _now().AddTicks(-ticks);
        }
    }
}
