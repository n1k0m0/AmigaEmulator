// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;

namespace AmigaEmulator.Core.Video
{
    /// <summary>
    /// Retains the latest long and short PAL fields in separate scanline rows.
    /// This is transient host presentation state, not guest chip memory.
    /// </summary>
    public sealed class InterlaceFieldBuffer
    {
        private readonly uint[] _pixels;
        private readonly int _width;
        private readonly int _fieldHeight;
        private byte _fieldMask;
        private int _lastParity = -1;

        public InterlaceFieldBuffer(int width, int fieldHeight)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (fieldHeight <= 0) throw new ArgumentOutOfRangeException(nameof(fieldHeight));
            _width = width;
            _fieldHeight = fieldHeight;
            _pixels = new uint[checked(width * fieldHeight * 2)];
        }

        public int Width => _width;
        public int Height => _fieldHeight * 2;
        public bool HasBothFields => _fieldMask == 3;
        public uint[] Pixels => _pixels;

        public void Invalidate()
        {
            _fieldMask = 0;
            _lastParity = -1;
        }

        public void Capture(uint[] source, bool longField)
        {
            if (source == null || source.Length < _width * _fieldHeight)
                throw new ArgumentException("The field source is too small.", nameof(source));

            // A real Workbench PAL LACE screen places the completed long
            // field on even raster rows and the short field on odd rows.
            // Reversing them breaks the title bar and icon text.
            int parity = longField ? 0 : 1;
            // A repeated polarity means the complementary field from the
            // previous pair is stale (for example after a guest LOF write).
            // Wait for a fresh counterpart before exposing another pair.
            if (parity == _lastParity) _fieldMask = 0;
            for (int y = 0; y < _fieldHeight; y++)
                Array.Copy(source, y * _width, _pixels, (y * 2 + parity) * _width, _width);
            _fieldMask |= (byte)(1 << parity);
            _lastParity = parity;
        }
    }
}
