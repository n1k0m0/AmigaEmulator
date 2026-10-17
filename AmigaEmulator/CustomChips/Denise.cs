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
        private readonly bool _ecs;
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
            _ecs = bitplanes.ClipSpritesToDisplayWindow;
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
            _frameBuffer.DiscardSuperHiresPlayfield();
        }

        /// <summary>
        /// Initializes the next backbuffer with the current border/background color.
        /// </summary>
        public void BeginFrame()
        {
            _bitplanes.RefreshFromRegisters();
            if (!_bitplanes.IsSuperHiresMode)
                _frameBuffer.DiscardSuperHiresPlayfield();
            _frameBuffer.Clear(_bitplanes.BorderColorArgb);
            if (_bitplanes.IsSuperHiresMode)
                _frameBuffer.EnsureSuperHiresPlayfield();
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
            // Hires ECS/AGA has no OCS host-span lead-in. Render the span
            // after its final DMA slot, retaining its actual display position.
            if (_ecs && _bitplanes.IsHiresMode)
                logicalX -= BitplaneUnit.HiresPixelsPerChipCycle;
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
            if (_ecs && _bitplanes.IsHiresMode)
                lastFrameX -= BitplaneUnit.HiresPixelsPerChipCycle;
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
                FillBackgroundSpan(y, beam.BeamY, borderStartFrameX, _frameBuffer.Width);
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
            if (_bitplanes.IsSuperHiresMode)
                _frameBuffer.EnsureSuperHiresPlayfield();
            if (_frameBuffer.SuperHiresPlayfieldPixels != null)
            {
                _frameBuffer.FillSuperHiresPlayfieldSpan((y * _frameBuffer.Width) + start, end - start, argb);
            }
            _frameBuffer.ClearSpriteSpan((y * _frameBuffer.Width) + start, end - start);
        }

        /// <summary>
        /// Fills COLOR00 inside DIW and applies ECS border blank outside it.
        /// </summary>
        private void FillBackgroundSpan(int y, int beamY, int start, int end)
        {
            uint color = _bitplanes.ReadColorArgb(0);
            if (!_bitplanes.BorderBlankEnabled)
            {
                FillPixelSpan(y, start, end, color);
                return;
            }
            if (!_bitplanes.TryGetDisplayWindowOnLine(beamY, out int left, out int right))
            {
                FillPixelSpan(y, start, end, 0xFF000000u);
                return;
            }
            left += _frameBufferXOffset;
            right += _frameBufferXOffset;
            FillPixelSpan(y, start, System.Math.Min(end, left), 0xFF000000u);
            FillPixelSpan(y, System.Math.Max(start, left), System.Math.Min(end, right), color);
            FillPixelSpan(y, System.Math.Max(start, right), end, 0xFF000000u);
        }

        /// <summary>Renders a contiguous host-framebuffer span for one beam line.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void RenderPixelSpan(int y, int beamY, long frame, int startFrameX, int endFrameX)
        {
            if (_bitplanes.IsSuperHiresMode)
                _frameBuffer.EnsureSuperHiresPlayfield();
            if (_bitplanes.IsAga) RenderPixelSpanCore(y, beamY, frame, startFrameX, endFrameX);
            else RenderLegacyPixelSpan(y, beamY, frame, startFrameX, endFrameX);
            if (_ecs && !_sprites.BorderSpritesEnabled) ApplyEcsDisplayWindow(y, beamY, startFrameX, endFrameX);
        }

        private void ApplyEcsDisplayWindow(int y, int beamY, int startFrameX, int endFrameX)
        {
            int start = System.Math.Max(0, startFrameX);
            int end = System.Math.Min(_frameBuffer.Width, endFrameX);
            if (end <= start) return;
            bool blank = _bitplanes.BorderBlankEnabled;
            if (_bitplanes.TryGetDisplayWindowOnLine(beamY, out int left, out int right))
            {
                RestoreEcsBorderSpan(y, start, System.Math.Min(end, left + _frameBufferXOffset), blank);
                RestoreEcsBorderSpan(y, System.Math.Max(start, right + _frameBufferXOffset), end, blank);
            }
            else RestoreEcsBorderSpan(y, start, end, blank);
        }

        private void RestoreEcsBorderSpan(int y, int start, int end, bool blank)
        {
            if (end <= start) return;
            if (blank) { FillPixelSpan(y, start, end, 0xFF000000u); return; }
            int row = y * _frameBuffer.Width;
            for (int x = start; x < end; x++)
                _frameBuffer.Pixels[row + x] = _frameBuffer.ReadPlayfieldPixel(row + x);
            if (_bitplanes.IsSuperHiresMode)
                _frameBuffer.FillSuperHiresPlayfieldSpan(row + start, end - start, _bitplanes.ReadColorArgb(0));
            _frameBuffer.ClearSpriteSpan(row + start, end - start);
        }

        // Shared color/collision loop: ECS window output is resolved by the caller.
        private void RenderLegacyPixelSpan(int y, int beamY, long frame, int startFrameX, int endFrameX)
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
                if (_ecs) FillBackgroundSpan(y, beamY, start, end);
                else FillPixelSpan(y, start, end, _bitplanes.ReadColorArgb(0));
                return;
            }
            bool ecsSuperHires = _bitplanes.IsSuperHiresMode;
            bool hires = _bitplanes.IsHiresMode || ecsSuperHires;

            uint[] pixels = _frameBuffer.Pixels;
            int row = y * _frameBuffer.Width;
            uint previousArgb = screenPreviousColor(row, start);
            if (_ecs && NeedsEcsHamSeed(start, beamY)) previousArgb = _bitplanes.ReadColorArgb(0);
            int bitplaneMask = 0;
            int bitplaneColorIndex = 0;
            int spriteMask = 0;
            uint spriteArgb = 0;
            int spriteGroup = 0;
            bool spriteVisible = false;
            uint playfieldArgb = 0;
            uint secondSuperHiresArgb = 0;
            int secondSuperHiresColorIndex = 0;
            int secondSuperHiresPlaneMask = 0;
            bool spriteCoversPlayfield = false;
            for (int frameBufferX = start; frameBufferX < end; frameBufferX++)
            {
                int screenX = frameBufferX - _frameBufferXOffset;
                // Lowres shifter bits and sprite bits each cover two hires framebuffer pixels.
                // Cache only within this span: CPU/Copper writes between chip ticks remain visible.
                bool firstInPair = frameBufferX == start || (screenX & 1) == 0;
                if (bitplaneActive && (hires || firstInPair))
                    bitplaneColorIndex = _bitplanes.RenderPixelInfo(screenX, beamY, out bitplaneMask);
                // Both hires framebuffer pixels of a lowres bit share a color.
                // Reapplying the same HAM component is also idempotent. Keep
                // this cache local to the span so raster writes stay immediate.
                if (bitplaneActive && (hires || firstInPair))
                {
                    if (ecsSuperHires)
                    {
                        secondSuperHiresColorIndex = _bitplanes.RenderSuperHiresPixelInfo(
                            screenX * 2 + 1, beamY, out secondSuperHiresPlaneMask);
                        _bitplanes.ResolveSuperHiresPixelPair(bitplaneColorIndex,
                            secondSuperHiresColorIndex,
                            out playfieldArgb, out secondSuperHiresArgb);
                    }
                    else
                        playfieldArgb = _bitplanes.ResolveLegacyColorArgb(bitplaneColorIndex, previousArgb);
                }
                uint argb = bitplaneActive ? playfieldArgb
                    : _bitplanes.RenderFallbackPixel(screenX, y, frame);
                if (ecsSuperHires)
                    _frameBuffer.RecordSuperHiresPlayfieldPair(row + frameBufferX,
                        bitplaneActive ? playfieldArgb : argb,
                        bitplaneActive ? secondSuperHiresArgb : argb);
                if (spritesActive && firstInPair)
                    spriteVisible = _sprites.TryRenderPixel(screenX, beamY, out spriteArgb, out spriteGroup, out spriteMask);
                if (collisionsActive && (hires || firstInPair))
                {
                    _collisions.ObservePixel(bitplaneMask, spriteMask);
                    collisionsActive = !_collisions.IsSaturated;
                }

                if (hires || firstInPair)
                    spriteCoversPlayfield = spriteVisible &&
                        ShouldSpriteCoverPlayfield(spriteGroup, bitplaneColorIndex, bitplaneMask);
                _frameBuffer.RecordSpritePixel(row + frameBufferX, argb, spriteCoversPlayfield ? spriteArgb : 0);
                if (ecsSuperHires)
                {
                    uint secondSprite = 0;
                    if (spritesActive && _sprites.TryRenderSuperHiresPixel(screenX * 2 + 1, beamY,
                        out uint nativeSprite, out int nativeGroup, out _) &&
                        ShouldSpriteCoverPlayfield(nativeGroup, secondSuperHiresColorIndex, secondSuperHiresPlaneMask))
                        secondSprite = nativeSprite;
                    _frameBuffer.RecordSuperHiresSpritePair(row + frameBufferX,
                        spriteCoversPlayfield ? spriteArgb : 0, secondSprite);
                }
                previousArgb = argb; // HAM holds the playfield color before sprite composition.
                if (spriteCoversPlayfield)
                {
                    argb = spriteArgb;
                }

                pixels[row + frameBufferX] = argb;
                if (!ecsSuperHires)
                    _frameBuffer.RecordExpandedPixelIfNative(row + frameBufferX, argb);
            }
        }

        private void RenderPixelSpanCore(int y, int beamY, long frame, int startFrameX, int endFrameX)
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
                FillBackgroundSpan(y, beamY, start, end);
                return;
            }
            bool hires = _bitplanes.RequiresHiresPixelSampling;
            bool fineSprites = _sprites.UsesSingleHiresPixels;
            uint[] pixels = _frameBuffer.Pixels;
            int row = y * _frameBuffer.Width;
            uint previousArgb = screenPreviousColor(row, start);
            if (_ecs && NeedsEcsHamSeed(start, beamY)) previousArgb = _bitplanes.ReadColorArgb(0);
            int bitplaneMask = 0;
            int bitplaneColorIndex = 0;
            int spriteMask = 0;
            uint spriteArgb = 0;
            int spriteGroup = 0;
            bool spriteVisible = false;
            uint playfieldArgb = 0;
            uint secondSuperHiresArgb = 0;
            int secondSuperHiresColorIndex = 0;
            int secondSuperHiresPlaneMask = 0;
            bool spriteCoversPlayfield = false;
            for (int frameBufferX = start; frameBufferX < end; frameBufferX++)
            {
                int screenX = frameBufferX - _frameBufferXOffset;
                // Lowres shifter bits and sprite bits each cover two hires framebuffer pixels.
                // Cache only within this span: CPU/Copper writes between chip ticks remain visible.
                bool firstInPair = frameBufferX == start || (screenX & 1) == 0;
                if (bitplaneActive && (hires || firstInPair))
                    bitplaneColorIndex = _bitplanes.RenderPixelInfo(screenX, beamY, out bitplaneMask);
                // Both hires framebuffer pixels of a lowres bit share a color.
                // Reapplying the same HAM component is also idempotent. Keep
                // this cache local to the span so raster writes stay immediate.
                if (bitplaneActive && (hires || firstInPair))
                {
                    if (_bitplanes.IsSuperHiresMode)
                    {
                        secondSuperHiresColorIndex = _bitplanes.RenderSuperHiresPixelInfo(
                            screenX * 2 + 1, beamY, out secondSuperHiresPlaneMask);
                        _bitplanes.ResolveSuperHiresPixelPair(bitplaneColorIndex,
                            secondSuperHiresColorIndex,
                            out playfieldArgb, out secondSuperHiresArgb);
                    }
                    else
                        playfieldArgb = _bitplanes.ResolveColorArgb(bitplaneColorIndex, previousArgb);
                }
                // BPLAM changes playfield palette addresses, never the COLOR00 border.
                // Border blank remains active even when BRDRSPRT allows sprite output.
                uint argb = !_bitplanes.IsInsideDisplayWindow(screenX, beamY)
                    ? _bitplanes.BorderColorArgb
                    : bitplaneActive ? playfieldArgb : _bitplanes.ReadColorArgb(0);
                if (_bitplanes.IsSuperHiresMode)
                    _frameBuffer.RecordSuperHiresPlayfieldPair(row + frameBufferX,
                        bitplaneActive && _bitplanes.IsInsideDisplayWindow(screenX, beamY) ? playfieldArgb : argb,
                        bitplaneActive && _bitplanes.IsInsideDisplayWindow(screenX, beamY) ? secondSuperHiresArgb : argb);
                if (spritesActive && (fineSprites || firstInPair))
                    spriteVisible = _sprites.TryRenderPixel(screenX, beamY, out spriteArgb, out spriteGroup, out spriteMask);
                if (collisionsActive && (hires || fineSprites || firstInPair))
                {
                    _collisions.ObservePixel(bitplaneMask, spriteMask);
                    collisionsActive = !_collisions.IsSaturated;
                }

                if (hires || fineSprites || firstInPair)
                    spriteCoversPlayfield = spriteVisible &&
                        ShouldSpriteCoverPlayfield(spriteGroup, bitplaneColorIndex, bitplaneMask);
                _frameBuffer.RecordSpritePixel(row + frameBufferX, argb, spriteCoversPlayfield ? spriteArgb : 0);
                if (_bitplanes.IsSuperHiresMode)
                {
                    uint secondSprite = 0;
                    if (spritesActive && _sprites.TryRenderSuperHiresPixel(screenX * 2 + 1, beamY,
                        out uint nativeSprite, out int nativeGroup, out _) &&
                        ShouldSpriteCoverPlayfield(nativeGroup, secondSuperHiresColorIndex, secondSuperHiresPlaneMask))
                        secondSprite = nativeSprite;
                    _frameBuffer.RecordSuperHiresSpritePair(row + frameBufferX,
                        spriteCoversPlayfield ? spriteArgb : 0, secondSprite);
                }
                previousArgb = argb; // HAM holds the playfield color before sprite composition.
                if (spriteCoversPlayfield)
                {
                    argb = spriteArgb;
                }

                pixels[row + frameBufferX] = argb;
                if (!_bitplanes.IsSuperHiresMode)
                    _frameBuffer.RecordExpandedPixelIfNative(row + frameBufferX, argb);
            }
        }

        /// <summary>
        /// Reads the previous HAM base color for this chunk.
        /// </summary>
        private bool NeedsEcsHamSeed(int x, int beamY) => _bitplanes.BorderBlankEnabled &&
            _bitplanes.IsInsideDisplayWindow(x - _frameBufferXOffset, beamY) &&
            !_bitplanes.IsInsideDisplayWindow(x - _frameBufferXOffset - 1, beamY);

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
        private bool ShouldSpriteCoverPlayfield(int spriteGroup, int playfieldColorIndex, int bitplaneMask)
        {
            if (playfieldColorIndex == 0)
            {
                return true;
            }

            ushort bplcon2 = _bitplanes.PriorityControl;
            int playfieldPriority;
            if (_bitplanes.IsDualPlayfield)
            {
                bool playfield2 = _bitplanes.IsSecondPlayfieldSelected(bitplaneMask);
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
