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
using AmigaEmulator.Core.DevTools;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.Video;

namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>
    /// Emulates Denise video output by combining bitplanes, sprites, palette state, and display timing.
    /// </summary>
    public sealed class Denise
    {
        /// <summary>
        /// Stores s registers.
        /// </summary>
        private static CustomRegisterMap s_registers;

        /// <summary>
        /// Stores frame Buffer.
        /// </summary>
        private readonly AmigaFrameBuffer _frameBuffer;
        /// <summary>
        /// Stores bitplanes.
        /// </summary>
        private readonly BitplaneUnit _bitplanes;
        /// <summary>
        /// Stores sprites.
        /// </summary>
        private readonly SpriteUnit _sprites;
        /// <summary>
        /// Tracks collision register state shared with CLXDAT/CLXCON.
        /// </summary>
        private readonly CollisionUnit _collisions;
        /// <summary>
        /// Horizontal host-framebuffer offset used to keep left overscan pixels available for presentation.
        /// </summary>
        private readonly int _frameBufferXOffset;
        /// <summary>
        /// Vertical host-framebuffer offset used to keep top/bottom overscan pixels available for presentation.
        /// </summary>
        private readonly int _frameBufferYOffset;
        /// <summary>
        /// Constant for Pal Lowres Display Start.
        /// </summary>
        private const int PalLowresDisplayStart = 0x3E;

        /// <summary>
        /// Initializes a new instance of the Denise class.
        /// </summary>
        public Denise(AmigaFrameBuffer frameBuffer)
            : this(frameBuffer, s_registers, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Denise class.
        /// </summary>
        public Denise(AmigaFrameBuffer frameBuffer, CustomRegisterMap registers)
            : this(frameBuffer, registers, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Denise class.
        /// </summary>
        public Denise(AmigaFrameBuffer frameBuffer, CustomRegisterMap registers, System.Func<uint, ushort> readChipWord)
            : this(frameBuffer, new BitplaneUnit(registers, readChipWord), new SpriteUnit(registers, readChipWord))
        {
        }

        /// <summary>
        /// Initializes a new instance of the Denise class.
        /// </summary>
        public Denise(AmigaFrameBuffer frameBuffer, BitplaneUnit bitplanes, CustomRegisterMap registers, System.Func<uint, ushort> readChipWord)
            : this(frameBuffer, bitplanes, new SpriteUnit(registers, readChipWord))
        {
        }

        /// <summary>
        /// Initializes a new instance of the Denise class.
        /// </summary>
        public Denise(AmigaFrameBuffer frameBuffer, BitplaneUnit bitplanes, SpriteUnit sprites)
            : this(frameBuffer, bitplanes, sprites, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Denise class.
        /// </summary>
        public Denise(AmigaFrameBuffer frameBuffer, BitplaneUnit bitplanes, SpriteUnit sprites, CollisionUnit collisions)
            : this(frameBuffer, bitplanes, sprites, collisions, 0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Denise class.
        /// </summary>
        public Denise(AmigaFrameBuffer frameBuffer, BitplaneUnit bitplanes, SpriteUnit sprites, CollisionUnit collisions, int frameBufferXOffset)
            : this(frameBuffer, bitplanes, sprites, collisions, frameBufferXOffset, 0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Denise class.
        /// </summary>
        public Denise(AmigaFrameBuffer frameBuffer, BitplaneUnit bitplanes, SpriteUnit sprites, CollisionUnit collisions, int frameBufferXOffset, int frameBufferYOffset)
            : this(frameBuffer, bitplanes, sprites, collisions, true, frameBufferXOffset, frameBufferYOffset)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Denise class.
        /// </summary>
        private Denise(AmigaFrameBuffer frameBuffer, BitplaneUnit bitplanes, SpriteUnit sprites, CollisionUnit collisions, bool useSharedSpriteState, int frameBufferXOffset, int frameBufferYOffset)
        {
            _frameBuffer = frameBuffer;
            _bitplanes = bitplanes;
            _sprites = sprites;
            _collisions = collisions;
            _frameBufferXOffset = frameBufferXOffset;
            _frameBufferYOffset = frameBufferYOffset;
        }

        /// <summary>
        /// Attaches Custom Registers to this component.
        /// </summary>
        public static void AttachCustomRegisters(CustomRegisterMap registers)
        {
            s_registers = registers;
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            _bitplanes.Reset();
            _sprites.Reset();
            if (_collisions != null)
            {
                _collisions.Reset();
            }

            _frameBuffer.Clear(0xFF0C1020);
        }

        /// <summary>
        /// Initializes the next backbuffer with the current border/background color.
        /// </summary>
        public void BeginFrame()
        {
            _bitplanes.RefreshFromRegisters();
            _frameBuffer.Clear(_bitplanes.ReadColorArgb(0));
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick(BeamTraceEntry beam)
        {
            _bitplanes.RefreshFromRegisters();
            _sprites.RefreshFromRegisters();
            int y = beam.BeamY - BitplaneUnit.PalVisibleTop + _frameBufferYOffset;
            if (y < 0 || y >= _frameBuffer.Height)
            {
                return;
            }

            int logicalX = (beam.BeamX - PalLowresDisplayStart) * BitplaneUnit.HiresPixelsPerChipCycle;
            int frameX = logicalX + _frameBufferXOffset;
            if (frameX >= _frameBuffer.Width || frameX + BitplaneUnit.HiresPixelsPerChipCycle <= 0)
            {
                return;
            }

            bool bitplaneActive = _bitplanes.IsBitplaneDisplayActiveCached;
            bool spritesActive = _sprites.HasActiveSprites;
            uint[] pixels = _frameBuffer.Pixels;
            int row = y * _frameBuffer.Width;
            uint previousArgb = screenPreviousColor(row, frameX);
            for (int pixel = 0; pixel < BitplaneUnit.HiresPixelsPerChipCycle; pixel++)
            {
                int frameBufferX = frameX + pixel;
                if (frameBufferX < 0 || frameBufferX >= _frameBuffer.Width)
                {
                    continue;
                }

                int screenX = logicalX + pixel;
                int bitplaneMask = 0;
                int bitplaneColorIndex = bitplaneActive
                    ? _bitplanes.RenderPixelInfo(screenX, beam.BeamY, out bitplaneMask)
                    : 0;
                uint argb = bitplaneActive
                    ? _bitplanes.ResolveColorArgb(bitplaneColorIndex, previousArgb)
                    : _bitplanes.RenderFallbackPixel(screenX, y, beam.Frame);
                int spriteMask = 0;
                uint spriteArgb = 0;
                int spriteGroup = 0;
                bool spriteVisible = spritesActive &&
                    _sprites.TryRenderPixel(screenX, beam.BeamY, out spriteArgb, out spriteGroup, out spriteMask);
                if (_collisions != null)
                {
                    _collisions.ObservePixel(bitplaneMask, spriteMask);
                }

                if (spriteVisible &&
                    ShouldSpriteCoverPlayfield(spriteGroup, bitplaneColorIndex))
                {
                    argb = spriteArgb;
                }

                pixels[row + frameBufferX] = argb;
                previousArgb = argb;
            }
        }

        /// <summary>
        /// Reads the previous HAM base color for this chunk.
        /// </summary>
        private uint screenPreviousColor(int row, int x)
        {
            int previousX = x - 1;
            if (previousX >= 0 && previousX < _frameBuffer.Width)
            {
                return _frameBuffer.Pixels[row + previousX];
            }

            return _bitplanes.ReadColorArgb(0);
        }

        /// <summary>
        /// Runs Should Sprite Cover Playfield.
        /// </summary>
        private bool ShouldSpriteCoverPlayfield(int spriteGroup, int playfieldColorIndex)
        {
            if (playfieldColorIndex == 0)
            {
                return true;
            }

            ushort bplcon2 = _bitplanes.PriorityControl;
            int playfieldPriority;
            if (_bitplanes.IsDualPlayfield)
            {
                bool playfield2 = playfieldColorIndex >= 8 && playfieldColorIndex < 16;
                playfieldPriority = playfield2
                    ? (bplcon2 >> 3) & 0x07
                    : bplcon2 & 0x07;
            }
            else
            {
                playfieldPriority = (bplcon2 >> 3) & 0x07;
            }

            return (spriteGroup & 0x03) < System.Math.Min(playfieldPriority, 4);
        }
    }
}
