// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System.IO;

namespace AmigaEmulator.Core.Memory
{
    /// <summary>Fat Gary control latches and Ramsey -07 registers. RAM wait-state,
    /// refresh and bus-timeout timing are not yet represented by the scheduler.</summary>
    public sealed class DesktopMotherboardResources
    {
        private byte _ramseyControl;
        private bool _coldBoot, _timeout, _timeoutEnabled;
        private readonly byte _resetControl;
        public bool Enabled { get; }
        public DesktopMotherboardResources(bool enabled, int fastRamBytes)
        {
            Enabled = enabled;
            // 25 MHz refresh interval, four 1 MiB banks or larger 4 MiB banks.
            _resetControl = (byte)(0x20 | (fastRamBytes > 4 * 1024 * 1024 ? 8 : 0));
            Reset(true);
        }
        public bool Maps(uint address) => Enabled && address >= 0xDE0000 && address < 0xDE8000;
        public void Reset(bool cold)
        {
            _coldBoot = cold; _timeout = _timeoutEnabled = false;
            if (cold) _ramseyControl = _resetControl;
        }
        public byte ReadByte(uint address)
        {
            uint offset = address & 0xFF;
            switch (offset & 3)
            {
                case 0: return (byte)(_timeout ? 0xFF : 0x7F);
                case 1: return (byte)(_timeoutEnabled ? 0xFF : 0x7F);
                case 2: return (byte)(_coldBoot ? 0xFF : 0x7F);
                default: return (offset & 0xC0) == 0 ? _ramseyControl : (offset & 0xC0) == 0x40 ? (byte)0x0F : (byte)0xFF;
            }
        }
        public void WriteByte(uint address, byte value)
        {
            switch (address & 3)
            {
                case 0: _timeout = (value & 0x80) != 0; break;
                case 1: _timeoutEnabled = (value & 0x80) != 0; break;
                case 2: _coldBoot = (value & 0x80) != 0; break;
                case 3: if ((address & 0xC0) == 0) _ramseyControl = value; break;
            }
        }
        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_ramseyControl); writer.Write(_coldBoot); writer.Write(_timeout); writer.Write(_timeoutEnabled);
        }
        public void LoadState(BinaryReader reader)
        {
            _ramseyControl = reader.ReadByte(); _coldBoot = reader.ReadBoolean();
            _timeout = reader.ReadBoolean(); _timeoutEnabled = reader.ReadBoolean();
        }
    }
}
