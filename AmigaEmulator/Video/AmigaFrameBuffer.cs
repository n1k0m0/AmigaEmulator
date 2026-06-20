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
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Core.Video
{
    /// <summary>
    /// Provides Amiga Frame Buffer functionality.
    /// </summary>
    public sealed class AmigaFrameBuffer
    {
        /// <summary>
        /// Stores pixels.
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
        /// Gets or sets Width.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets or sets Height.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Gets Pixels.
        /// </summary>
        public uint[] Pixels
        {
            get { return _pixels; }
        }

        /// <summary>
        /// Runs Clear.
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
        /// Runs Copy To.
        /// </summary>
        public void CopyTo(uint[] target)
        {
            Array.Copy(_pixels, target, Math.Min(_pixels.Length, target.Length));
        }

        /// <summary>
        /// Runs Copy From.
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
        /// Copies a rectangular source window into this framebuffer.
        /// </summary>
        public void CopyWindowFrom(AmigaFrameBuffer source, int sourceX, int sourceY)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Clear(0xFF000000);
            int startX = Math.Max(0, sourceX);
            int startY = Math.Max(0, sourceY);
            int width = Math.Min(Width, source.Width - startX);
            int height = Math.Min(Height, source.Height - startY);
            if (width <= 0 || height <= 0)
            {
                return;
            }

            for (int y = 0; y < height; y++)
            {
                Array.Copy(
                    source._pixels,
                    ((startY + y) * source.Width) + startX,
                    _pixels,
                    y * Width,
                    width);
            }
        }

        /// <summary>
        /// Removes sparse non-border pixels from the top overscan rows without cropping real top-edge artwork.
        /// </summary>
        public void ClearSparseTopBorderArtifacts(int rows, int maxNonBorderPixels)
        {
            if (rows <= 0 || maxNonBorderPixels <= 0 || _pixels.Length == 0)
            {
                return;
            }

            int scanRows = Math.Min(rows, Height);
            uint borderColor = FindDominantColor(0, scanRows);
            int nonBorderPixels = 0;
            for (int y = 0; y < scanRows; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    if (_pixels[row + x] != borderColor)
                    {
                        nonBorderPixels++;
                        if (nonBorderPixels > maxNonBorderPixels)
                        {
                            return;
                        }
                    }
                }
            }

            if (nonBorderPixels == 0)
            {
                return;
            }

            for (int y = 0; y < scanRows; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    if (_pixels[row + x] != borderColor)
                    {
                        _pixels[row + x] = borderColor;
                    }
                }
            }
        }

        /// <summary>
        /// Finds the most common color in a small framebuffer band.
        /// </summary>
        private uint FindDominantColor(int startY, int rows)
        {
            var counts = new Dictionary<uint, int>();
            int endY = Math.Min(Height, startY + rows);
            uint dominant = 0;
            int dominantCount = 0;
            for (int y = Math.Max(0, startY); y < endY; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    uint color = _pixels[row + x];
                    counts.TryGetValue(color, out int count);
                    count++;
                    counts[color] = count;
                    if (count > dominantCount)
                    {
                        dominant = color;
                        dominantCount = count;
                    }
                }
            }

            return dominant;
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
            if (width == Width && height == Height && pixels.Length == _pixels.Length)
            {
                Array.Copy(pixels, _pixels, _pixels.Length);
                return;
            }

            if (width <= 0 || height <= 0 || pixels.Length != width * height)
            {
                throw new InvalidDataException("Savestate framebuffer size does not match the current Amiga model.");
            }

            Clear(0xFF000000);
            int copyWidth = Math.Min(width, Width);
            int copyHeight = Math.Min(height, Height);
            for (int y = 0; y < copyHeight; y++)
            {
                Array.Copy(pixels, y * width, _pixels, y * Width, copyWidth);
            }
        }
    }
}
