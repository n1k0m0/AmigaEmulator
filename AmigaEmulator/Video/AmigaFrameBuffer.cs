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
using System.IO;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Core.Video
{
    /// <summary>
    /// Provides Amiga Frame Buffer behavior used by the emulator.
    /// </summary>
    public sealed class AmigaFrameBuffer
    {
        /// <summary>
        /// Tracks the pixels.
        /// </summary>
        private readonly uint[] _pixels;

        /// <summary>
        /// Initializes a new instance of the AmigaFrameBuffer class.
        /// </summary>
        public AmigaFrameBuffer(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Width = width;
            Height = height;
            _pixels = new uint[width * height];
        }

        /// <summary>
        /// Gets or updates the Width.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets or updates the Height.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Executes the Pixels logic.
        /// </summary>
        public uint[] Pixels
        {
            get { return _pixels; }
        }

        /// <summary>
        /// Executes the Clear logic.
        /// </summary>
        public void Clear(uint argb)
        {
            for (int index = 0; index < _pixels.Length; index++)
            {
                _pixels[index] = argb;
            }
        }

        /// <summary>
        /// Updates the Pixel.
        /// </summary>
        public void SetPixel(int x, int y, uint argb)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return;
            }

            _pixels[(y * Width) + x] = argb;
        }

        /// <summary>
        /// Executes the Copy To logic.
        /// </summary>
        public void CopyTo(uint[] target)
        {
            Array.Copy(_pixels, target, Math.Min(_pixels.Length, target.Length));
        }

        /// <summary>
        /// Executes the Copy From logic.
        /// </summary>
        public void CopyFrom(AmigaFrameBuffer source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Array.Copy(source._pixels, _pixels, Math.Min(_pixels.Length, source._pixels.Length));
        }

        /// <summary>
        /// Writes the framebuffer dimensions and pixels to a savestate.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            writer.Write(Width);
            writer.Write(Height);
            StateSerializer.WriteUIntArray(writer, _pixels);
        }

        /// <summary>
        /// Restores framebuffer pixels from a savestate when the dimensions match this instance.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            uint[] pixels = StateSerializer.ReadUIntArray(reader) ?? Array.Empty<uint>();
            if (width != Width || height != Height || pixels.Length != _pixels.Length)
            {
                throw new InvalidDataException("Savestate framebuffer size does not match the current Amiga model.");
            }

            Array.Copy(pixels, _pixels, _pixels.Length);
        }
    }
}
