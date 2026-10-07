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
            if (beam.BeamX == PalLowresDisplayStart)
            {
                _bitplanes.RefreshFromRegisters();
                _sprites.RefreshFromRegisters();
                _bitplanes.ObservePresentationWindowForBeamLine(beam.BeamY);
            }

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

            RenderPixelSpan(y, beam.BeamY, beam.Frame, frameX, frameX + BitplaneUnit.HiresPixelsPerChipCycle);
        }

        /// <summary>
        /// Renders overscan pixels that are selected by a wide DDF window after all line DMA is available.
        /// </summary>
        public void FinishLineTail(BeamTraceEntry beam)
        {
            if (!_bitplanes.TryGetLinePresentationSourceWindow(beam.BeamY, out int sourceLeft, out int sourceWidth))
            {
                return;
            }

            int y = beam.BeamY - BitplaneUnit.PalVisibleTop + _frameBufferYOffset;
            if (y < 0 || y >= _frameBuffer.Height)
            {
                return;
            }

            int lastFrameX = ((beam.BeamX - PalLowresDisplayStart) * BitplaneUnit.HiresPixelsPerChipCycle) +
                _frameBufferXOffset +
                BitplaneUnit.HiresPixelsPerChipCycle;
            int firstFrameX = _frameBufferXOffset;
            int requestedLeft = _frameBufferXOffset + sourceLeft;
            int requestedRight = _frameBufferXOffset + sourceLeft + sourceWidth;

            _bitplanes.RefreshFromRegisters();
            _sprites.RefreshFromRegisters();

            if (_sprites.TryGetLinePresentationWindow(out int spriteLeft, out int spriteWidth))
            {
                requestedLeft = System.Math.Min(requestedLeft, _frameBufferXOffset + spriteLeft);
                requestedRight = System.Math.Max(requestedRight, _frameBufferXOffset + spriteLeft + spriteWidth);
            }

            if (_sprites.TryGetLineSpriteFrameWindow(out int frameLeft, out int frameWidth))
            {
                _bitplanes.IncludeSpritePresentationWindowForBeamLine(beam.BeamY, frameLeft, frameWidth);
            }

            int leadEndFrameX = System.Math.Min(firstFrameX, _frameBuffer.Width);
            if (requestedLeft < leadEndFrameX)
            {
                RenderPixelSpan(y, beam.BeamY, beam.Frame, requestedLeft, leadEndFrameX);
            }

            int bitplaneTailEndFrameX = System.Math.Min(_frameBuffer.Width, requestedRight);
            if (bitplaneTailEndFrameX > lastFrameX)
            {
                RenderPixelSpan(y, beam.BeamY, beam.Frame, lastFrameX, bitplaneTailEndFrameX);
            }

            int borderStartFrameX = System.Math.Max(lastFrameX, bitplaneTailEndFrameX);
            if (borderStartFrameX < _frameBuffer.Width)
            {
                FillPixelSpan(y, borderStartFrameX, _frameBuffer.Width, _bitplanes.ReadColorArgb(0));
            }
        }

        /// <summary>
        /// Fills a contiguous host-framebuffer X span for one beam line.
        /// </summary>
        private void FillPixelSpan(int y, int startFrameX, int endFrameX, uint argb)
        {
            int start = System.Math.Max(0, startFrameX);
            int end = System.Math.Min(_frameBuffer.Width, endFrameX);
            if (end <= start)
            {
                return;
            }

            uint[] pixels = _frameBuffer.Pixels;
            System.Array.Fill(pixels, argb, (y * _frameBuffer.Width) + start, end - start);
            _frameBuffer.ClearSpriteSpan((y * _frameBuffer.Width) + start, end - start);
        }

        /// <summary>
        /// Renders a contiguous host-framebuffer X span for one beam line.
        /// </summary>
        private void RenderPixelSpan(int y, int beamY, long frame, int startFrameX, int endFrameX)
        {
            int start = System.Math.Max(0, startFrameX);
            int end = System.Math.Min(_frameBuffer.Width, endFrameX);
            if (end <= start)
            {
                return;
            }

            bool bitplaneActive = _bitplanes.IsBitplaneDisplayActiveOnLine(beamY);
            bool spritesActive = _sprites.HasRenderablePixelsOnLine(beamY);
            bool collisionsActive = _collisions != null && !_collisions.IsSaturated;
            if (!bitplaneActive && !spritesActive)
            {
                if (collisionsActive) _collisions.ObservePixel(0, 0);
                FillPixelSpan(y, start, end, _bitplanes.ReadColorArgb(0));
                return;
            }
            bool hires = _bitplanes.IsHiresMode;
            uint[] pixels = _frameBuffer.Pixels;
            int row = y * _frameBuffer.Width;
            uint previousArgb = screenPreviousColor(row, start);
            int bitplaneMask = 0;
            int bitplaneColorIndex = 0;
            int spriteMask = 0;
            uint spriteArgb = 0;
            int spriteGroup = 0;
            bool spriteVisible = false;
            for (int frameBufferX = start; frameBufferX < end; frameBufferX++)
            {
                int screenX = frameBufferX - _frameBufferXOffset;
                // Lowres shifter bits and sprite bits each cover two hires framebuffer pixels.
                // Cache only within this span: CPU/Copper writes between chip ticks remain visible.
                bool firstInPair = frameBufferX == start || (screenX & 1) == 0;
                if (bitplaneActive && (hires || firstInPair))
                    bitplaneColorIndex = _bitplanes.RenderPixelInfo(screenX, beamY, out bitplaneMask);
                uint argb = bitplaneActive
                    ? _bitplanes.ResolveColorArgb(bitplaneColorIndex, previousArgb)
                    : _bitplanes.RenderFallbackPixel(screenX, y, frame);
                if (spritesActive && firstInPair)
                    spriteVisible = _sprites.TryRenderPixel(screenX, beamY, out spriteArgb, out spriteGroup, out spriteMask);
                if (collisionsActive)
                {
                    _collisions.ObservePixel(bitplaneMask, spriteMask);
                    collisionsActive = !_collisions.IsSaturated;
                }

                bool spriteCoversPlayfield = spriteVisible &&
                    ShouldSpriteCoverPlayfield(spriteGroup, bitplaneColorIndex);
                _frameBuffer.RecordSpritePixel(row + frameBufferX, argb, spriteCoversPlayfield ? spriteArgb : 0);
                previousArgb = argb; // HAM holds the playfield color before sprite composition.
                if (spriteCoversPlayfield)
                {
                    argb = spriteArgb;
                }

                pixels[row + frameBufferX] = argb;
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
                return _frameBuffer.ReadPlayfieldPixel(row + previousX);
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
