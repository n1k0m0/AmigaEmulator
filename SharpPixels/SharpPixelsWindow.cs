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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SharpPixels
{
    /// <summary>
    /// Provides a lightweight WinForms pixel presentation surface with optional post-processing filters.
    /// </summary>
    public class SharpPixelsWindow : Form
    {
        /// <summary>
        /// Stores timer.
        /// </summary>
        private readonly System.Windows.Forms.Timer _timer;
        /// <summary>
        /// Measures frame pacing and FPS timing.
        /// </summary>
        private readonly Stopwatch _frameStopwatch = new Stopwatch();
        /// <summary>
        /// Stores bitmap.
        /// </summary>
        private Bitmap _bitmap;
        /// <summary>
        /// Stores paint Upload Buffer.
        /// </summary>
        private int[] _paintUploadBuffer;
        /// <summary>
        /// Stores filter Scratch Buffer.
        /// </summary>
        private int[] _filterScratchBuffer;
        /// <summary>
        /// Stores the offscreen device context used to compose pixels and overlays without flicker.
        /// </summary>
        private IntPtr _backBufferDc;
        /// <summary>
        /// Stores the bitmap selected into the offscreen device context.
        /// </summary>
        private IntPtr _backBufferBitmap;
        /// <summary>
        /// Stores the original object selected into the offscreen device context.
        /// </summary>
        private IntPtr _backBufferPreviousBitmap;
        /// <summary>
        /// Stores the current offscreen buffer width.
        /// </summary>
        private int _backBufferWidth;
        /// <summary>
        /// Stores the current offscreen buffer height.
        /// </summary>
        private int _backBufferHeight;
        /// <summary>
        /// Stores presentation Filter Mode.
        /// </summary>
        private SharpPixelsGpuFilterMode _presentationFilterMode = SharpPixelsGpuFilterMode.Sharp;
        /// <summary>
        /// Stores presentation Upscale Mode.
        /// </summary>
        private SharpPixelsGpuUpscaleMode _presentationUpscaleMode = SharpPixelsGpuUpscaleMode.None;
        /// <summary>
        /// Stores source pixels cropped from the left before presentation scaling.
        /// </summary>
        private int _presentationCropLeft;
        /// <summary>
        /// Stores source pixels cropped from the top before presentation scaling.
        /// </summary>
        private int _presentationCropTop;
        /// <summary>
        /// Stores source pixels cropped from the right before presentation scaling.
        /// </summary>
        private int _presentationCropRight;
        /// <summary>
        /// Stores source pixels cropped from the bottom before presentation scaling.
        /// </summary>
        private int _presentationCropBottom;
        /// <summary>
        /// Stretches the presentation source to the complete client area instead of preserving aspect ratio.
        /// </summary>
        private bool _stretchPresentationToClient;
        /// <summary>
        /// Stores presentation-coordinate pixels left empty before drawing the source.
        /// </summary>
        private int _presentationTargetMarginLeft;
        /// <summary>
        /// Stores presentation-coordinate pixels left empty above the source.
        /// </summary>
        private int _presentationTargetMarginTop;
        /// <summary>
        /// Stores presentation-coordinate pixels left empty after drawing the source.
        /// </summary>
        private int _presentationTargetMarginRight;
        /// <summary>
        /// Stores presentation-coordinate pixels left empty below the source.
        /// </summary>
        private int _presentationTargetMarginBottom;
        /// <summary>
        /// Stores update Frequency.
        /// </summary>
        private double _updateFrequency = 60.0;
        /// <summary>
        /// Stores next Update Seconds.
        /// </summary>
        private double _nextUpdateSeconds;
        /// <summary>
        /// Stores fps Window Start Seconds.
        /// </summary>
        private double _fpsWindowStartSeconds;
        /// <summary>
        /// Stores fps Counter.
        /// </summary>
        private int _fpsCounter;
        /// <summary>
        /// Stores running.
        /// </summary>
        private bool _running;

        /// <summary>
        /// Initializes a new instance of the SharpPixelsWindow class.
        /// </summary>
        public SharpPixelsWindow(int pixelsWidth, int pixelsHeight, string title)
            : this(pixelsWidth, pixelsHeight, pixelsWidth, pixelsHeight, title)
        {
        }

        /// <summary>
        /// Initializes a new instance of the SharpPixelsWindow class.
        /// </summary>
        public SharpPixelsWindow(int pixelsWidth, int pixelsHeight, int presentationWidth, int presentationHeight, string title)
        {
            PixelsWidth = pixelsWidth;
            PixelsHeight = pixelsHeight;
            PresentationWidth = Math.Max(1, presentationWidth);
            PresentationHeight = Math.Max(1, presentationHeight);
            Pixels = new uint[pixelsWidth * pixelsHeight];
            Text = title;
            ClientSize = new Size(PresentationWidth * 2, PresentationHeight * 2);
            MinimumSize = new Size(320, 240);
            DoubleBuffered = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            KeyPreview = true;

            _timer = new System.Windows.Forms.Timer();
            _timer.Tick += delegate
            {
                PumpOneFrame();
            };
            ApplyTimerInterval();
        }

        /// <summary>
        /// Gets or sets Pixels Width.
        /// </summary>
        public int PixelsWidth { get; private set; }

        /// <summary>
        /// Gets or sets Pixels Height.
        /// </summary>
        public int PixelsHeight { get; private set; }

        /// <summary>
        /// Gets or sets Presentation Width.
        /// </summary>
        public int PresentationWidth { get; private set; }

        /// <summary>
        /// Gets or sets Presentation Height.
        /// </summary>
        public int PresentationHeight { get; private set; }

        /// <summary>
        /// Gets or sets Pixels.
        /// </summary>
        public uint[] Pixels { get; private set; }

        /// <summary>
        /// Gets the source width used for host-to-pixel presentation scaling.
        /// </summary>
        protected int PresentationSourceWidth
        {
            get { return Math.Max(1, PixelsWidth - _presentationCropLeft - _presentationCropRight); }
        }

        /// <summary>
        /// Gets the source height used for host-to-pixel presentation scaling.
        /// </summary>
        protected int PresentationSourceHeight
        {
            get { return Math.Max(1, PixelsHeight - _presentationCropTop - _presentationCropBottom); }
        }

        /// <summary>
        /// Gets Pixel Buffer.
        /// </summary>
        public uint[] PixelBuffer
        {
            get { return Pixels; }
        }

        /// <summary>
        /// Updates the Presentation Effects.
        /// </summary>
        public void SetPresentationEffects(SharpPixelsGpuFilterMode filterMode, SharpPixelsGpuUpscaleMode upscaleMode)
        {
            _presentationFilterMode = filterMode;
            _presentationUpscaleMode = upscaleMode;
            Invalidate();
        }

        /// <summary>
        /// Crops the uploaded source bitmap before it is stretched into the presentation rectangle.
        /// </summary>
        public void SetPresentationSourceCrop(int left, int top, int right, int bottom)
        {
            _presentationCropLeft = Math.Max(0, Math.Min(PixelsWidth - 1, left));
            _presentationCropTop = Math.Max(0, Math.Min(PixelsHeight - 1, top));
            _presentationCropRight = Math.Max(0, Math.Min(PixelsWidth - 1 - _presentationCropLeft, right));
            _presentationCropBottom = Math.Max(0, Math.Min(PixelsHeight - 1 - _presentationCropTop, bottom));
            Invalidate();
        }

        /// <summary>
        /// Selects whether the cropped source is stretched to the entire window client area.
        /// </summary>
        public void SetPresentationStretchToClient(bool enabled)
        {
            _stretchPresentationToClient = enabled;
            Invalidate();
        }

        /// <summary>
        /// Leaves margins in presentation coordinates while scaling the cropped source inside them.
        /// </summary>
        public void SetPresentationTargetMargins(int left, int top, int right, int bottom)
        {
            _presentationTargetMarginLeft = Math.Max(0, Math.Min(PresentationWidth - 1, left));
            _presentationTargetMarginTop = Math.Max(0, Math.Min(PresentationHeight - 1, top));
            _presentationTargetMarginRight = Math.Max(0, Math.Min(PresentationWidth - 1 - _presentationTargetMarginLeft, right));
            _presentationTargetMarginBottom = Math.Max(0, Math.Min(PresentationHeight - 1 - _presentationTargetMarginTop, bottom));
            Invalidate();
        }

        /// <summary>
        /// Gets or sets FPS.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int FPS { get; private set; }

        /// <summary>
        /// Updates Frequency from current emulator state.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double UpdateFrequency
        {
            get { return _updateFrequency; }
            set
            {
                _updateFrequency = value;
                ApplyTimerInterval();
            }
        }

        /// <summary>
        /// Runs the value routine.
        /// </summary>
        public void Run()
        {
            bool timerResolutionRaised = false;
            try
            {
                timeBeginPeriod(1);
                timerResolutionRaised = true;
                _running = true;
                _frameStopwatch.Restart();
                _nextUpdateSeconds = 0.0;
                _fpsWindowStartSeconds = 0.0;
                Application.Idle += OnApplicationIdle;
                _timer.Start();
                Application.Run(this);
            }
            finally
            {
                _timer.Stop();
                Application.Idle -= OnApplicationIdle;
                _running = false;
                if (timerResolutionRaised)
                {
                    timeEndPeriod(1);
                }
            }
        }

        /// <summary>
        /// Runs Request Close.
        /// </summary>
        public void RequestClose()
        {
            Close();
        }

        /// <summary>
        /// Handles the Update Frame callback.
        /// </summary>
        protected virtual void OnUpdateFrame()
        {
        }

        /// <summary>
        /// Handles the Render Overlay callback.
        /// </summary>
        protected virtual void OnRenderOverlay(Graphics graphics, Rectangle target)
        {
        }

        /// <summary>
        /// Handles the Application Idle callback.
        /// </summary>
        private void OnApplicationIdle(object sender, EventArgs e)
        {
            while (_running && !IsDisposed && IsApplicationIdle())
            {
                double now = _frameStopwatch.Elapsed.TotalSeconds;
                if (_updateFrequency > 0.0 && now < _nextUpdateSeconds)
                {
                    int sleepMilliseconds = Math.Max(1, Math.Min(5, (int)((_nextUpdateSeconds - now) * 1000.0)));
                    Thread.Sleep(sleepMilliseconds);
                    continue;
                }

                bool painted = PumpOneFrame();
                if (!painted && _updateFrequency <= 0.0)
                {
                    Thread.Yield();
                }

                if (_updateFrequency > 0.0)
                {
                    double framePeriod = 1.0 / _updateFrequency;
                    _nextUpdateSeconds = Math.Max(_nextUpdateSeconds + framePeriod, _frameStopwatch.Elapsed.TotalSeconds);
                }
            }
        }

        /// <summary>
        /// Runs Pump One Frame.
        /// </summary>
        private bool PumpOneFrame()
        {
            OnUpdateFrame();
            if (!ShouldPaintFrame())
            {
                return false;
            }

            Invalidate();
            Update();
            UpdateFpsCounter();
            return true;
        }

        /// <summary>
        /// Returns whether the current update produced something worth repainting.
        /// </summary>
        protected virtual bool ShouldPaintFrame()
        {
            return true;
        }

        /// <summary>
        /// Handles the User Key Down callback.
        /// </summary>
        public virtual void OnUserKeyDown(KeyEventArgs keyEventArgs)
        {
        }

        /// <summary>
        /// Handles the User Key Press callback.
        /// </summary>
        public virtual void OnUserKeyPress(KeyEventArgs keyEventArgs)
        {
        }

        /// <summary>
        /// Handles the User Key Up callback.
        /// </summary>
        public virtual void OnUserKeyUp(KeyEventArgs keyEventArgs)
        {
        }

        /// <summary>
        /// Runs Clear.
        /// </summary>
        public void Clear(byte red, byte green, byte blue)
        {
            uint color = ToArgb(red, green, blue);
            for (int index = 0; index < Pixels.Length; index++)
            {
                Pixels[index] = color;
            }
        }

        /// <summary>
        /// Draws Pixel into the presentation surface.
        /// </summary>
        public void DrawPixel(int x, int y, byte red, byte green, byte blue)
        {
            if ((uint)x >= (uint)PixelsWidth || (uint)y >= (uint)PixelsHeight)
            {
                return;
            }

            Pixels[(y * PixelsWidth) + x] = ToArgb(red, green, blue);
        }

        /// <summary>
        /// Draws Filled Rectangle into the presentation surface.
        /// </summary>
        public void DrawFilledRectangle(int x, int y, int width, int height, byte red, byte green, byte blue)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            int minX = Math.Max(0, x);
            int minY = Math.Max(0, y);
            int maxX = Math.Min(PixelsWidth, x + width);
            int maxY = Math.Min(PixelsHeight, y + height);
            uint color = ToArgb(red, green, blue);
            for (int yy = minY; yy < maxY; yy++)
            {
                int row = yy * PixelsWidth;
                for (int xx = minX; xx < maxX; xx++)
                {
                    Pixels[row + xx] = color;
                }
            }
        }

        /// <summary>
        /// Draws Filled Rectangle With Alpha into the presentation surface.
        /// </summary>
        public void DrawFilledRectangleWithAlpha(int x, int y, int width, int height, byte red, byte green, byte blue, byte alpha)
        {
            if (width <= 0 || height <= 0 || alpha == 0)
            {
                return;
            }

            int minX = Math.Max(0, x);
            int minY = Math.Max(0, y);
            int maxX = Math.Min(PixelsWidth, x + width);
            int maxY = Math.Min(PixelsHeight, y + height);
            for (int yy = minY; yy < maxY; yy++)
            {
                int row = yy * PixelsWidth;
                for (int xx = minX; xx < maxX; xx++)
                {
                    Pixels[row + xx] = Blend(Pixels[row + xx], red, green, blue, alpha);
                }
            }
        }

        /// <summary>
        /// Draws ARGB Pixels Gpu Presented into the presentation surface.
        /// </summary>
        public bool DrawArgbPixelsGpuPresented(
            uint[] sourcePixels,
            int sourceWidth,
            int sourceHeight,
            int cropX,
            int cropY,
            int cropWidth,
            int cropHeight,
            int targetX,
            int targetY,
            int targetWidth,
            int targetHeight,
            SharpPixelsGpuFilterMode filterMode,
            SharpPixelsGpuUpscaleMode upscaleMode,
            byte borderRed,
            byte borderGreen,
            byte borderBlue)
        {
            DrawFilledRectangle(0, 0, PixelsWidth, PixelsHeight, borderRed, borderGreen, borderBlue);
            DrawArgbPixelsCroppedToRectangle(sourcePixels, sourceWidth, sourceHeight, cropX, cropY, cropWidth, cropHeight, targetX, targetY, targetWidth, targetHeight);
            return true;
        }

        /// <summary>
        /// Draws ARGB Pixels Cropped To Rectangle into the presentation surface.
        /// </summary>
        public void DrawArgbPixelsCroppedToRectangle(
            uint[] sourcePixels,
            int sourceWidth,
            int sourceHeight,
            int cropX,
            int cropY,
            int cropWidth,
            int cropHeight,
            int targetX,
            int targetY,
            int targetWidth,
            int targetHeight)
        {
            if (sourcePixels == null || sourceWidth <= 0 || sourceHeight <= 0 || cropWidth <= 0 || cropHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
            {
                return;
            }

            if (sourcePixels.Length < sourceWidth * sourceHeight)
            {
                return;
            }

            int minTargetX = Math.Max(0, targetX);
            int minTargetY = Math.Max(0, targetY);
            int maxTargetX = Math.Min(PixelsWidth, targetX + targetWidth);
            int maxTargetY = Math.Min(PixelsHeight, targetY + targetHeight);
            for (int y = minTargetY; y < maxTargetY; y++)
            {
                int sourceY = cropY + ((y - targetY) * cropHeight / targetHeight);
                if ((uint)sourceY >= (uint)sourceHeight)
                {
                    continue;
                }

                int targetRow = y * PixelsWidth;
                int sourceRow = sourceY * sourceWidth;
                for (int x = minTargetX; x < maxTargetX; x++)
                {
                    int sourceX = cropX + ((x - targetX) * cropWidth / targetWidth);
                    if ((uint)sourceX < (uint)sourceWidth)
                    {
                        Pixels[targetRow + x] = sourcePixels[sourceRow + sourceX] | 0xFF000000u;
                    }
                }
            }
        }

        /// <summary>
        /// Handles the Paint callback.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var target = GetPresentationRectangle();
            var source = GetPresentationSourceRectangle();
            int width = Math.Max(1, ClientSize.Width);
            int height = Math.Max(1, ClientSize.Height);
            EnsureBackBuffer(e.Graphics, width, height);

            using (Graphics backGraphics = Graphics.FromHdc(_backBufferDc))
            {
                ConfigureFastGraphics(backGraphics);
                if (target.X > 0 ||
                    target.Y > 0 ||
                    target.Right < width ||
                    target.Bottom < height)
                {
                    backGraphics.Clear(Color.Black);
                }

                if (_presentationFilterMode == SharpPixelsGpuFilterMode.Sharp)
                {
                    DrawPresentationPixelsDib(_backBufferDc, source, target);
                }
                else
                {
                    EnsurePaintUploadBuffer();
                    Buffer.BlockCopy(Pixels, 0, _paintUploadBuffer, 0, Pixels.Length * sizeof(uint));
                    ApplyPresentationFilter(_paintUploadBuffer, PixelsWidth, PixelsHeight);
                    DrawPresentationDib(_backBufferDc, source, target);
                }

                OnRenderOverlay(backGraphics, target);
            }

            IntPtr hdc = e.Graphics.GetHdc();
            try
            {
                BitBlt(hdc, 0, 0, width, height, _backBufferDc, 0, 0, SrcCopyRasterOperation);
            }
            finally
            {
                e.Graphics.ReleaseHdc(hdc);
            }
        }

        /// <summary>
        /// Suppresses WinForms background erasing because the pixel surface repaints the full presentation area every frame.
        /// </summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        /// <summary>
        /// Handles the Form Closed callback.
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _running = false;
            _timer.Stop();
            DisposeBitmap();
            DisposeBackBuffer();
            _paintUploadBuffer = null;
            _filterScratchBuffer = null;

            base.OnFormClosed(e);
        }

        /// <summary>
        /// Handles the Key Down callback.
        /// </summary>
        protected override void OnKeyDown(System.Windows.Forms.KeyEventArgs e)
        {
            KeyEventArgs keyEventArgs = CreateKeyEventArgs(e.KeyData, '\0');
            OnUserKeyDown(keyEventArgs);
            e.Handled = keyEventArgs.Handled;
            base.OnKeyDown(e);
        }

        /// <summary>
        /// Handles the Key Press callback.
        /// </summary>
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            KeyEventArgs keyEventArgs = new KeyEventArgs(Key.Unknown, e.KeyChar, false, false, false);
            OnUserKeyPress(keyEventArgs);
            e.Handled = keyEventArgs.Handled;
            base.OnKeyPress(e);
        }

        /// <summary>
        /// Handles the Key Up callback.
        /// </summary>
        protected override void OnKeyUp(System.Windows.Forms.KeyEventArgs e)
        {
            KeyEventArgs keyEventArgs = CreateKeyEventArgs(e.KeyData, '\0');
            OnUserKeyUp(keyEventArgs);
            e.Handled = keyEventArgs.Handled;
            base.OnKeyUp(e);
        }

        /// <summary>
        /// Returns the Presentation Rectangle.
        /// </summary>
        protected Rectangle GetPresentationRectangle()
        {
            if (_stretchPresentationToClient)
            {
                return ApplyPresentationTargetMargins(new Rectangle(0, 0, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)));
            }

            // Upscaling changes sampling, never the displayed image size or mouse mapping.
            double scale = Math.Min(ClientSize.Width / (double)PresentationWidth, ClientSize.Height / (double)PresentationHeight);

            int width = Math.Max(1, (int)(PresentationWidth * scale));
            int height = Math.Max(1, (int)(PresentationHeight * scale));
            int x = (ClientSize.Width - width) / 2;
            int y = (ClientSize.Height - height) / 2;
            return ApplyPresentationTargetMargins(new Rectangle(x, y, width, height));
        }

        /// <summary>
        /// Converts presentation-coordinate target margins into current client pixels.
        /// </summary>
        private Rectangle ApplyPresentationTargetMargins(Rectangle baseTarget)
        {
            int left = (int)Math.Round(baseTarget.Width * (_presentationTargetMarginLeft / (double)PresentationWidth));
            int top = (int)Math.Round(baseTarget.Height * (_presentationTargetMarginTop / (double)PresentationHeight));
            int right = (int)Math.Round(baseTarget.Width * (_presentationTargetMarginRight / (double)PresentationWidth));
            int bottom = (int)Math.Round(baseTarget.Height * (_presentationTargetMarginBottom / (double)PresentationHeight));
            int width = Math.Max(1, baseTarget.Width - left - right);
            int height = Math.Max(1, baseTarget.Height - top - bottom);
            return new Rectangle(baseTarget.X + left, baseTarget.Y + top, width, height);
        }

        /// <summary>
        /// Returns the uploaded bitmap source area selected for presentation.
        /// </summary>
        protected Rectangle GetPresentationSourceRectangle()
        {
            return new Rectangle(
                _presentationCropLeft,
                _presentationCropTop,
                PresentationSourceWidth,
                PresentationSourceHeight);
        }

        /// <summary>
        /// Returns the Interpolation Mode.
        /// </summary>
        private InterpolationMode GetInterpolationMode()
        {
            switch (_presentationUpscaleMode)
            {
                case SharpPixelsGpuUpscaleMode.Hq2x:
                case SharpPixelsGpuUpscaleMode.Hq3x:
                case SharpPixelsGpuUpscaleMode.Hq4x:
                case SharpPixelsGpuUpscaleMode.Xbrz:
                    return InterpolationMode.HighQualityBicubic;
                default:
                    return InterpolationMode.NearestNeighbor;
            }
        }

        /// <summary>
        /// Returns the Presentation Integer Scale.
        /// </summary>
        private int GetPresentationIntegerScale()
        {
            switch (_presentationUpscaleMode)
            {
                case SharpPixelsGpuUpscaleMode.Scale2x:
                case SharpPixelsGpuUpscaleMode.Hq2x:
                    return 2;
                case SharpPixelsGpuUpscaleMode.Scale3x:
                case SharpPixelsGpuUpscaleMode.Hq3x:
                    return 3;
                case SharpPixelsGpuUpscaleMode.Hq4x:
                case SharpPixelsGpuUpscaleMode.Clean4:
                case SharpPixelsGpuUpscaleMode.Xbrz:
                    return 4;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Applies Presentation Filter to the current state.
        /// </summary>
        private void ApplyPresentationFilter(int[] pixels, int width, int height)
        {
            if (pixels == null || pixels.Length < width * height)
            {
                return;
            }

            switch (_presentationFilterMode)
            {
                case SharpPixelsGpuFilterMode.Crt:
                    ApplyCrtFilter(pixels, width, height);
                    break;
                case SharpPixelsGpuFilterMode.Tv:
                    ApplyTvFilter(pixels, width, height);
                    break;
            }
        }

        /// <summary>
        /// Applies Crt Filter to the current state.
        /// </summary>
        private void ApplyCrtFilter(int[] pixels, int width, int height)
        {
            EnsureFilterScratch();
            Buffer.BlockCopy(pixels, 0, _filterScratchBuffer, 0, pixels.Length * sizeof(int));

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                int scanlinePercent = (y & 1) == 0 ? 100 : 72;
                for (int x = 0; x < width; x++)
                {
                    int center = _filterScratchBuffer[row + x];
                    int left = _filterScratchBuffer[row + Math.Max(0, x - 1)];
                    int right = _filterScratchBuffer[row + Math.Min(width - 1, x + 1)];
                    int blended = Blend3(left, center, right, 18, 64, 18);
                    pixels[row + x] = ScaleRgb(blended, scanlinePercent, 100, 108);
                }
            }
        }

        /// <summary>
        /// Applies Tv Filter to the current state.
        /// </summary>
        private void ApplyTvFilter(int[] pixels, int width, int height)
        {
            EnsureFilterScratch();
            Buffer.BlockCopy(pixels, 0, _filterScratchBuffer, 0, pixels.Length * sizeof(int));

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                int prevRow = (y == 0 ? 0 : y - 1) * width;
                int nextRow = (y == height - 1 ? height - 1 : y + 1) * width;
                if (width <= 1)
                {
                    int singleCenter = _filterScratchBuffer[row];
                    int singleVertical = Blend3(_filterScratchBuffer[prevRow], singleCenter, _filterScratchBuffer[nextRow], 10, 80, 10);
                    pixels[row] = Blend2(singleVertical, singleCenter, 55, 45);
                    continue;
                }

                int center = _filterScratchBuffer[row];
                int vertical = Blend3(_filterScratchBuffer[prevRow], center, _filterScratchBuffer[nextRow], 10, 80, 10);
                int horizontal = Blend3(center, center, _filterScratchBuffer[row + 1], 24, 52, 24);
                pixels[row] = Blend2(vertical, horizontal, 55, 45);

                for (int x = 1; x < width - 1; x++)
                {
                    center = _filterScratchBuffer[row + x];
                    vertical = Blend3(_filterScratchBuffer[prevRow + x], center, _filterScratchBuffer[nextRow + x], 10, 80, 10);
                    horizontal = Blend3(_filterScratchBuffer[row + x - 1], center, _filterScratchBuffer[row + x + 1], 24, 52, 24);
                    pixels[row + x] = Blend2(vertical, horizontal, 55, 45);
                }

                int right = width - 1;
                center = _filterScratchBuffer[row + right];
                vertical = Blend3(_filterScratchBuffer[prevRow + right], center, _filterScratchBuffer[nextRow + right], 10, 80, 10);
                horizontal = Blend3(_filterScratchBuffer[row + right - 1], center, center, 24, 52, 24);
                pixels[row + right] = Blend2(vertical, horizontal, 55, 45);
            }
        }

        /// <summary>
        /// Runs Ensure Filter Scratch.
        /// </summary>
        private void EnsureFilterScratch()
        {
            if (_filterScratchBuffer == null || _filterScratchBuffer.Length != Pixels.Length)
            {
                _filterScratchBuffer = new int[Pixels.Length];
            }
        }

        /// <summary>
        /// Runs Ensure Bitmap.
        /// </summary>
        private void EnsureBitmap()
        {
            if (_bitmap == null)
            {
                _bitmap = new Bitmap(PixelsWidth, PixelsHeight, PixelFormat.Format32bppArgb);
            }

            if (_paintUploadBuffer == null || _paintUploadBuffer.Length != Pixels.Length)
            {
                _paintUploadBuffer = new int[Pixels.Length];
            }
        }

        /// <summary>
        /// Applies low-latency pixel-art drawing options to a graphics surface.
        /// </summary>
        private static void ConfigureFastGraphics(Graphics graphics)
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.None;
            graphics.SmoothingMode = SmoothingMode.None;
        }

        /// <summary>
        /// Ensures the offscreen client-size backbuffer exists.
        /// </summary>
        private void EnsureBackBuffer(Graphics ownerGraphics, int width, int height)
        {
            if (_backBufferDc != IntPtr.Zero &&
                _backBufferBitmap != IntPtr.Zero &&
                _backBufferWidth == width &&
                _backBufferHeight == height)
            {
                return;
            }

            DisposeBackBuffer();
            IntPtr ownerHdc = ownerGraphics.GetHdc();
            try
            {
                _backBufferDc = CreateCompatibleDC(ownerHdc);
                _backBufferBitmap = CreateCompatibleBitmap(ownerHdc, width, height);
                if (_backBufferDc == IntPtr.Zero || _backBufferBitmap == IntPtr.Zero)
                {
                    DisposeBackBuffer();
                    throw new InvalidOperationException("Unable to allocate the SharpPixels backbuffer.");
                }

                _backBufferPreviousBitmap = SelectObject(_backBufferDc, _backBufferBitmap);
                _backBufferWidth = width;
                _backBufferHeight = height;
            }
            finally
            {
                ownerGraphics.ReleaseHdc(ownerHdc);
            }
        }

        /// <summary>
        /// Releases the offscreen client-size backbuffer.
        /// </summary>
        private void DisposeBackBuffer()
        {
            if (_backBufferDc != IntPtr.Zero && _backBufferPreviousBitmap != IntPtr.Zero)
            {
                SelectObject(_backBufferDc, _backBufferPreviousBitmap);
            }

            if (_backBufferBitmap != IntPtr.Zero)
            {
                DeleteObject(_backBufferBitmap);
            }

            if (_backBufferDc != IntPtr.Zero)
            {
                DeleteDC(_backBufferDc);
            }

            _backBufferDc = IntPtr.Zero;
            _backBufferBitmap = IntPtr.Zero;
            _backBufferPreviousBitmap = IntPtr.Zero;
            _backBufferWidth = 0;
            _backBufferHeight = 0;
        }

        /// <summary>
        /// Ensures the paint upload buffer exists without creating a GDI+ bitmap.
        /// </summary>
        private void EnsurePaintUploadBuffer()
        {
            if (_paintUploadBuffer == null || _paintUploadBuffer.Length != Pixels.Length)
            {
                _paintUploadBuffer = new int[Pixels.Length];
            }
        }

        /// <summary>
        /// Releases the legacy bitmap buffer when the fast DIB path is used.
        /// </summary>
        private void DisposeBitmap()
        {
            if (_bitmap != null)
            {
                _bitmap.Dispose();
                _bitmap = null;
            }
        }

        /// <summary>
        /// Draws the current pixel buffer through Win32 StretchDIBits, avoiding slow GDI+ bitmap scaling.
        /// </summary>
        private void DrawPresentationPixelsDib(Graphics graphics, Rectangle source, Rectangle target)
        {
            GCHandle handle = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
            try
            {
                DrawPresentationDib(graphics, source, target, handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Draws the current pixel buffer through Win32 StretchDIBits into the supplied device context.
        /// </summary>
        private void DrawPresentationPixelsDib(IntPtr hdc, Rectangle source, Rectangle target)
        {
            GCHandle handle = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
            try
            {
                DrawPresentationDib(hdc, source, target, handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Draws the filtered upload buffer through Win32 StretchDIBits.
        /// </summary>
        private void DrawPresentationDib(Graphics graphics, Rectangle source, Rectangle target)
        {
            if (_paintUploadBuffer == null || _paintUploadBuffer.Length < PixelsWidth * PixelsHeight)
            {
                return;
            }

            GCHandle handle = GCHandle.Alloc(_paintUploadBuffer, GCHandleType.Pinned);
            try
            {
                DrawPresentationDib(graphics, source, target, handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Draws the filtered upload buffer through Win32 StretchDIBits into the supplied device context.
        /// </summary>
        private void DrawPresentationDib(IntPtr hdc, Rectangle source, Rectangle target)
        {
            if (_paintUploadBuffer == null || _paintUploadBuffer.Length < PixelsWidth * PixelsHeight)
            {
                return;
            }

            GCHandle handle = GCHandle.Alloc(_paintUploadBuffer, GCHandleType.Pinned);
            try
            {
                DrawPresentationDib(hdc, source, target, handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Draws pinned top-down 32-bit pixels through Win32 StretchDIBits.
        /// </summary>
        private void DrawPresentationDib(Graphics graphics, Rectangle source, Rectangle target, IntPtr pixelPointer)
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                DrawPresentationDib(hdc, source, target, pixelPointer);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        /// <summary>
        /// Draws pinned top-down 32-bit pixels through Win32 StretchDIBits into a device context.
        /// </summary>
        private void DrawPresentationDib(IntPtr hdc, Rectangle source, Rectangle target, IntPtr pixelPointer)
        {
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = Marshal.SizeOf(typeof(BitmapInfoHeader)),
                    Width = PixelsWidth,
                    Height = -PixelsHeight,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = PixelsWidth * PixelsHeight * 4
                }
            };

            SetStretchBltMode(hdc, ColorOnColorStretchMode);
            StretchDIBits(
                hdc,
                target.X,
                target.Y,
                target.Width,
                target.Height,
                source.X,
                source.Y,
                source.Width,
                source.Height,
                pixelPointer,
                ref info,
                DibRgbColors,
                SrcCopyRasterOperation);
        }

        /// <summary>
        /// Applies Timer Interval to the current state.
        /// </summary>
        private void ApplyTimerInterval()
        {
            if (_timer == null)
            {
                return;
            }

            _timer.Interval = _updateFrequency > 0.0
                ? Math.Max(1, (int)Math.Round(1000.0 / _updateFrequency))
                : 1;
        }

        /// <summary>
        /// Updates Fps Counter from current emulator state.
        /// </summary>
        private void UpdateFpsCounter()
        {
            _fpsCounter++;
            double now = _frameStopwatch.Elapsed.TotalSeconds;
            double elapsed = now - _fpsWindowStartSeconds;
            if (elapsed < 1.0)
            {
                return;
            }

            FPS = (int)Math.Round(_fpsCounter / elapsed);
            _fpsCounter = 0;
            _fpsWindowStartSeconds = now;
        }

        /// <summary>
        /// Returns whether Application Idle is true.
        /// </summary>
        private static bool IsApplicationIdle()
        {
            NativeMessage message;
            return !PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
        }

        /// <summary>
        /// Runs Peek Message.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out NativeMessage message, IntPtr windowHandle, uint filterMin, uint filterMax, uint remove);

        /// <summary>
        /// Runs time Begin Period.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint milliseconds);

        /// <summary>
        /// Runs time End Period.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint milliseconds);

        /// <summary>
        /// Draws a top-down 32-bit DIB into the target device context.
        /// </summary>
        [DllImport("gdi32.dll")]
        private static extern int StretchDIBits(
            IntPtr hdc,
            int xDest,
            int yDest,
            int destWidth,
            int destHeight,
            int xSrc,
            int ySrc,
            int srcWidth,
            int srcHeight,
            IntPtr bits,
            ref BitmapInfo bitmapInfo,
            uint usage,
            uint rasterOperation);

        /// <summary>
        /// Selects the stretch mode for StretchDIBits.
        /// </summary>
        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int stretchMode);

        /// <summary>
        /// Copies pixels between device contexts.
        /// </summary>
        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(
            IntPtr destinationDc,
            int destinationX,
            int destinationY,
            int width,
            int height,
            IntPtr sourceDc,
            int sourceX,
            int sourceY,
            uint rasterOperation);

        /// <summary>
        /// Creates a memory device context compatible with a window device context.
        /// </summary>
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        /// <summary>
        /// Creates a bitmap compatible with a window device context.
        /// </summary>
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

        /// <summary>
        /// Selects a GDI object into a device context.
        /// </summary>
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr gdiObject);

        /// <summary>
        /// Deletes a GDI object.
        /// </summary>
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr gdiObject);

        /// <summary>
        /// Deletes a memory device context.
        /// </summary>
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        /// <summary>
        /// Uses direct RGB colors from the DIB header.
        /// </summary>
        private const uint DibRgbColors = 0;

        /// <summary>
        /// Copies source pixels directly to the target.
        /// </summary>
        private const uint SrcCopyRasterOperation = 0x00CC0020;

        /// <summary>
        /// Uses nearest-neighbor stretching for pixel-art output.
        /// </summary>
        private const int ColorOnColorStretchMode = 3;

        /// <summary>
        /// Stores the DIB header passed to StretchDIBits.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            /// <summary>
            /// Stores Size.
            /// </summary>
            public int Size;
            /// <summary>
            /// Stores Width.
            /// </summary>
            public int Width;
            /// <summary>
            /// Stores Height.
            /// </summary>
            public int Height;
            /// <summary>
            /// Stores Planes.
            /// </summary>
            public ushort Planes;
            /// <summary>
            /// Stores Bit Count.
            /// </summary>
            public ushort BitCount;
            /// <summary>
            /// Stores Compression.
            /// </summary>
            public uint Compression;
            /// <summary>
            /// Stores Size Image.
            /// </summary>
            public int SizeImage;
            /// <summary>
            /// Stores X Pixels Per Meter.
            /// </summary>
            public int XPelsPerMeter;
            /// <summary>
            /// Stores Y Pixels Per Meter.
            /// </summary>
            public int YPelsPerMeter;
            /// <summary>
            /// Stores Colors Used.
            /// </summary>
            public uint ClrUsed;
            /// <summary>
            /// Stores Important Colors.
            /// </summary>
            public uint ClrImportant;
        }

        /// <summary>
        /// Stores the BITMAPINFO wrapper used by StretchDIBits.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            /// <summary>
            /// Stores Header.
            /// </summary>
            public BitmapInfoHeader Header;
            /// <summary>
            /// Stores the first color table slot, unused for 32-bit RGB DIBs.
            /// </summary>
            public uint Colors;
        }

        /// <summary>
        /// Stores Native Message data used by the emulator.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            /// <summary>
            /// Stores Window Handle.
            /// </summary>
            public IntPtr WindowHandle;
            /// <summary>
            /// Stores Message.
            /// </summary>
            public uint Message;
            /// <summary>
            /// Stores W Param.
            /// </summary>
            public UIntPtr WParam;
            /// <summary>
            /// Stores L Param.
            /// </summary>
            public IntPtr LParam;
            /// <summary>
            /// Stores Time.
            /// </summary>
            public uint Time;
            /// <summary>
            /// Stores Point.
            /// </summary>
            public System.Drawing.Point Point;
        }

        /// <summary>
        /// Runs To Argb.
        /// </summary>
        private static uint ToArgb(byte red, byte green, byte blue)
        {
            return 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
        }

        /// <summary>
        /// Runs Blend 2.
        /// </summary>
        private static int Blend2(int first, int second, int firstWeight, int secondWeight)
        {
            int total = firstWeight + secondWeight;
            int red = ((((first >> 16) & 0xFF) * firstWeight) + (((second >> 16) & 0xFF) * secondWeight)) / total;
            int green = ((((first >> 8) & 0xFF) * firstWeight) + (((second >> 8) & 0xFF) * secondWeight)) / total;
            int blue = (((first & 0xFF) * firstWeight) + ((second & 0xFF) * secondWeight)) / total;
            return unchecked((int)(0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | (uint)blue));
        }

        /// <summary>
        /// Runs Blend 3.
        /// </summary>
        private static int Blend3(int first, int second, int third, int firstWeight, int secondWeight, int thirdWeight)
        {
            int total = firstWeight + secondWeight + thirdWeight;
            int red = ((((first >> 16) & 0xFF) * firstWeight) + (((second >> 16) & 0xFF) * secondWeight) + (((third >> 16) & 0xFF) * thirdWeight)) / total;
            int green = ((((first >> 8) & 0xFF) * firstWeight) + (((second >> 8) & 0xFF) * secondWeight) + (((third >> 8) & 0xFF) * thirdWeight)) / total;
            int blue = (((first & 0xFF) * firstWeight) + ((second & 0xFF) * secondWeight) + ((third & 0xFF) * thirdWeight)) / total;
            return unchecked((int)(0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | (uint)blue));
        }

        /// <summary>
        /// Runs Scale Rgb.
        /// </summary>
        private static int ScaleRgb(int color, int redPercent, int greenPercent, int bluePercent)
        {
            int red = Math.Min(255, (((color >> 16) & 0xFF) * redPercent) / 100);
            int green = Math.Min(255, (((color >> 8) & 0xFF) * greenPercent) / 100);
            int blue = Math.Min(255, ((color & 0xFF) * bluePercent) / 100);
            return unchecked((int)(0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | (uint)blue));
        }

        /// <summary>
        /// Runs Blend.
        /// </summary>
        private static uint Blend(uint destination, byte red, byte green, byte blue, byte alpha)
        {
            int inverse = 255 - alpha;
            byte destinationRed = (byte)((destination >> 16) & 0xFF);
            byte destinationGreen = (byte)((destination >> 8) & 0xFF);
            byte destinationBlue = (byte)(destination & 0xFF);
            byte blendedRed = (byte)(((red * alpha) + (destinationRed * inverse)) / 255);
            byte blendedGreen = (byte)(((green * alpha) + (destinationGreen * inverse)) / 255);
            byte blendedBlue = (byte)(((blue * alpha) + (destinationBlue * inverse)) / 255);
            return ToArgb(blendedRed, blendedGreen, blendedBlue);
        }

        /// <summary>
        /// Creates the Key Event Args.
        /// </summary>
        private static KeyEventArgs CreateKeyEventArgs(Keys keyData, char keyChar)
        {
            return new KeyEventArgs(
                KeyMapper.FromFormsKey(keyData),
                keyChar,
                (keyData & Keys.Alt) == Keys.Alt,
                (keyData & Keys.Control) == Keys.Control,
                (keyData & Keys.Shift) == Keys.Shift);
        }
    }
}
