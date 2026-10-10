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
    public sealed class AmigaFrameBuffer
    {
        /// <summary>
        /// Stores pixels.
        /// </summary>
        private readonly uint[] _pixels;
        /// <summary>
        /// Reused horizontal nearest-neighbor map for scaled presentation copies.
        /// </summary>
        private readonly int[] _scaleMap;
        // Transient compositor data; the savestate format continues to store the raw composite only.
        private readonly uint[] _spritePixels;
        private readonly uint[] _pixelsBehindSprites;

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
            _scaleMap = new int[width];
            _spritePixels = new uint[width * height];
            _pixelsBehindSprites = new uint[width * height];
        }

        public int Width { get; private set; }

        public int Height { get; private set; }

        /// <summary>
        /// Gets Pixels.
        /// </summary>
        public uint[] Pixels
        {
            get { return _pixels; }
        }

        public void Clear(uint argb)
        {
            Array.Clear(_spritePixels);
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
            _spritePixels[(y * Width) + x] = 0;
        }

        /// <summary>
        /// Records the already priority-tested hardware sprite and the pixel it covered.
        /// </summary>
        public void RecordSpritePixel(int index, uint background, uint sprite)
        {
            _spritePixels[index] = sprite;
            if (sprite != 0) _pixelsBehindSprites[index] = background;
        }

        public void ClearSpriteSpan(int index, int length)
        {
            Array.Clear(_spritePixels, index, length);
        }

        /// <summary>
        /// Samples normalized playfield data while keeping sprites in one frame-wide coordinate system.
        /// </summary>
        public uint ReadPlayfieldPixel(int index)
        {
            return _spritePixels[index] != 0 ? _pixelsBehindSprites[index] : _pixels[index];
        }

        public uint SamplePresentationPixel(int playfieldIndex, int spriteIndex)
        {
            uint sprite = _spritePixels[spriteIndex];
            if (sprite != 0) return sprite;
            return ReadPlayfieldPixel(playfieldIndex);
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
            Array.Clear(_spritePixels);
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

            int startX = Math.Max(0, sourceX);
            int startY = Math.Max(0, sourceY);
            int width = Math.Min(Width, source.Width - startX);
            int height = Math.Min(Height, source.Height - startY);
            if (width <= 0 || height <= 0)
            {
                Clear(0xFF000000);
                return;
            }

            if (width < Width || height < Height)
            {
                Clear(0xFF000000);
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
        /// Copies a bounded source window into this framebuffer at a target offset.
        /// </summary>
        public void CopyWindowFrom(AmigaFrameBuffer source, int sourceX, int sourceY, int targetX, int targetY, int copyWidth, int copyHeight)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            int startX = Math.Max(0, sourceX);
            int startY = Math.Max(0, sourceY);
            int destX = Math.Max(0, targetX);
            int destY = Math.Max(0, targetY);
            int width = Math.Min(copyWidth, Math.Min(source.Width - startX, Width - destX));
            int height = Math.Min(copyHeight, Math.Min(source.Height - startY, Height - destY));
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
                    ((destY + y) * Width) + destX,
                    width);
            }
        }

        /// <summary>
        /// Copies a source window into this framebuffer, scaling it to the destination size.
        /// </summary>
        public void CopyScaledWindowFrom(AmigaFrameBuffer source, int sourceX, int sourceY, int sourceWidth, int sourceHeight)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                Clear(0xFF000000);
                return;
            }

            int startX = Math.Max(0, sourceX);
            int startY = Math.Max(0, sourceY);
            int width = Math.Min(sourceWidth, source.Width - startX);
            int height = Math.Min(sourceHeight, source.Height - startY);
            if (width <= 0 || height <= 0)
            {
                Clear(0xFF000000);
                return;
            }

            BuildScaleMap(_scaleMap, width, startX);
            for (int y = 0; y < Height; y++)
            {
                int sourceRow = startY + ScaleCoordinate(y, Height, height);
                int targetRow = y * Width;
                int sourceRowOffset = sourceRow * source.Width;
                for (int x = 0; x < Width; x++)
                {
                    _pixels[targetRow + x] = source._pixels[sourceRowOffset + _scaleMap[x]];
                }
            }
        }

        /// <summary>
        /// Builds one horizontal source lookup table for nearest-neighbor scaling.
        /// </summary>
        private static void BuildScaleMap(int[] map, int sourceSize, int sourceOffset)
        {
            for (int index = 0; index < map.Length; index++)
            {
                map[index] = sourceOffset + ScaleCoordinate(index, map.Length, sourceSize);
            }
        }

        /// <summary>
        /// Maps a destination coordinate to a source coordinate while preserving both endpoints.
        /// </summary>
        private static int ScaleCoordinate(int destination, int destinationSize, int sourceSize)
        {
            if (destinationSize <= 1 || sourceSize <= 1)
            {
                return 0;
            }

            return Math.Min(sourceSize - 1, (destination * (sourceSize - 1)) / (destinationSize - 1));
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
            Array.Clear(_spritePixels);
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
