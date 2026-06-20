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
            DoubleBuffered = true;
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
                Application.Run(this);
            }
            finally
            {
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

                PumpOneFrame();
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
        private void PumpOneFrame()
        {
            OnUpdateFrame();
            Invalidate();
            Update();
            UpdateFpsCounter();
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
            EnsureBitmap();
            BitmapData data = _bitmap.LockBits(
                new Rectangle(0, 0, PixelsWidth, PixelsHeight),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                Buffer.BlockCopy(Pixels, 0, _paintUploadBuffer, 0, Pixels.Length * sizeof(uint));
                ApplyPresentationFilter(_paintUploadBuffer, PixelsWidth, PixelsHeight);
                Marshal.Copy(_paintUploadBuffer, 0, data.Scan0, Pixels.Length);
            }
            finally
            {
                _bitmap.UnlockBits(data);
            }

            e.Graphics.InterpolationMode = GetInterpolationMode();
            e.Graphics.PixelOffsetMode = PixelOffsetMode.None;
            var target = GetPresentationRectangle();
            var source = GetPresentationSourceRectangle();
            e.Graphics.Clear(Color.Black);
            e.Graphics.DrawImage(_bitmap, target, source, GraphicsUnit.Pixel);
            OnRenderOverlay(e.Graphics, target);
        }

        /// <summary>
        /// Handles the Form Closed callback.
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _running = false;
            _timer.Stop();
            if (_bitmap != null)
            {
                _bitmap.Dispose();
                _bitmap = null;
            }
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

            int fixedScale = GetPresentationIntegerScale();
            double scale = fixedScale > 0
                ? fixedScale
                : Math.Min(ClientSize.Width / (double)PresentationWidth, ClientSize.Height / (double)PresentationHeight);

            if (fixedScale > 0 && (PresentationWidth * scale > ClientSize.Width || PresentationHeight * scale > ClientSize.Height))
            {
                scale = Math.Min(ClientSize.Width / (double)PresentationWidth, ClientSize.Height / (double)PresentationHeight);
            }

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
                int prevRow = Math.Max(0, y - 1) * width;
                int nextRow = Math.Min(height - 1, y + 1) * width;
                for (int x = 0; x < width; x++)
                {
                    int center = _filterScratchBuffer[row + x];
                    int left = _filterScratchBuffer[row + Math.Max(0, x - 1)];
                    int right = _filterScratchBuffer[row + Math.Min(width - 1, x + 1)];
                    int vertical = Blend3(_filterScratchBuffer[prevRow + x], center, _filterScratchBuffer[nextRow + x], 10, 80, 10);
                    int horizontal = Blend3(left, center, right, 24, 52, 24);
                    pixels[row + x] = Blend2(vertical, horizontal, 55, 45);
                }
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
