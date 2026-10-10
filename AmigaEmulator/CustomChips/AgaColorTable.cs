// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;

namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>Lisa's eight banks of 32 color words, with high/low component selection.</summary>
    public sealed class AgaColorTable
    {
        private readonly uint[] _colors = new uint[256];
        public void Reset() => Array.Clear(_colors);
        public uint ReadArgb(int index) => 0xFF000000u | (_colors[index & 255] & 0xFFFFFF);

        public void Write(int register, ushort bplcon3, ushort value)
        {
            int index = ((bplcon3 >> 13) & 7) * 32 + (register & 31);
            uint nibbles = ((uint)(value & 0xF00) << 8) | ((uint)(value & 0xF0) << 4) | (uint)(value & 15);
            uint genlock = (value & 0x8000) != 0 ? 0x1000000u : 0;
            // MSB writes also load the LSBs for old 12-bit Copper lists.
            _colors[index] = (bplcon3 & 0x200) != 0
                ? (_colors[index] & 0xF0F0F0) | nibbles | genlock
                : (nibbles * 17) | genlock;
        }

        public ushort ReadWord(int register, ushort bplcon3)
        {
            int index = ((bplcon3 >> 13) & 7) * 32 + (register & 31);
            uint value = _colors[index];
            if ((bplcon3 & 0x200) == 0) value >>= 4;
            return (ushort)(((value >> 8) & 0xF00) | ((value >> 4) & 0xF0) | (value & 15) |
                ((_colors[index] & 0x1000000) != 0 ? 0x8000u : 0));
        }

        public void SaveState(BinaryWriter writer) { for (int i = 0; i < 256; i++) writer.Write(_colors[i]); }
        public void LoadState(BinaryReader reader)
        {
            var colors = new uint[256];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = reader.ReadUInt32();
                if ((colors[i] & 0xFE000000) != 0) throw new InvalidDataException("Invalid Lisa color table entry.");
            }
            Array.Copy(colors, _colors, colors.Length);
        }
    }
}
