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
        /// Tracks the timer.
        /// </summary>
        private readonly System.Windows.Forms.Timer _timer;
        /// <summary>
        /// Measures frame pacing and FPS timing.
        /// </summary>
        private readonly Stopwatch _frameStopwatch = new Stopwatch();
        /// <summary>
        /// Tracks the bitmap.
        /// </summary>
        private Bitmap _bitmap;
        /// <summary>
        /// Tracks the paint Upload Buffer.
        /// </summary>
        private int[] _paintUploadBuffer;
        /// <summary>
        /// Tracks the filter Scratch Buffer.
        /// </summary>
        private int[] _filterScratchBuffer;
        /// <summary>
        /// Tracks the presentation Filter Mode.
        /// </summary>
        private SharpPixelsGpuFilterMode _presentationFilterMode = SharpPixelsGpuFilterMode.Sharp;
        /// <summary>
        /// Tracks the presentation Upscale Mode.
        /// </summary>
        private SharpPixelsGpuUpscaleMode _presentationUpscaleMode = SharpPixelsGpuUpscaleMode.None;
        /// <summary>
        /// Tracks the update Frequency.
        /// </summary>
        private double _updateFrequency = 60.0;
        /// <summary>
        /// Tracks the next Update Seconds.
        /// </summary>
        private double _nextUpdateSeconds;
        /// <summary>
        /// Tracks the fps Window Start Seconds.
        /// </summary>
        private double _fpsWindowStartSeconds;
        /// <summary>
        /// Tracks the fps Counter.
        /// </summary>
        private int _fpsCounter;
        /// <summary>
        /// Tracks the running.
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
        /// Gets or updates the Pixels Width.
        /// </summary>
        public int PixelsWidth { get; private set; }

        /// <summary>
        /// Gets or updates the Pixels Height.
        /// </summary>
        public int PixelsHeight { get; private set; }

        /// <summary>
        /// Gets or updates the Presentation Width.
        /// </summary>
        public int PresentationWidth { get; private set; }

        /// <summary>
        /// Gets or updates the Presentation Height.
        /// </summary>
        public int PresentationHeight { get; private set; }

        /// <summary>
        /// Gets or updates the Pixels.
        /// </summary>
        public uint[] Pixels { get; private set; }

        /// <summary>
        /// Executes the Pixel Buffer logic.
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
        /// Gets or updates the FPS.
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
        /// Executes the Request Close logic.
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
        /// Executes the Pump One Frame logic.
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
        /// Executes the Clear logic.
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
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            var target = GetPresentationRectangle();
            e.Graphics.Clear(Color.Black);
            e.Graphics.DrawImage(_bitmap, target);
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
            return new Rectangle(x, y, width, height);
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
        /// Executes the Ensure Filter Scratch logic.
        /// </summary>
        private void EnsureFilterScratch()
        {
            if (_filterScratchBuffer == null || _filterScratchBuffer.Length != Pixels.Length)
            {
                _filterScratchBuffer = new int[Pixels.Length];
            }
        }

        /// <summary>
        /// Executes the Ensure Bitmap logic.
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
        /// Executes the Peek Message logic.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out NativeMessage message, IntPtr windowHandle, uint filterMin, uint filterMax, uint remove);

        /// <summary>
        /// Executes the time Begin Period logic.
        /// </summary>
        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint milliseconds);

        /// <summary>
        /// Executes the time End Period logic.
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
            /// Stores the Window Handle.
            /// </summary>
            public IntPtr WindowHandle;
            /// <summary>
            /// Stores the Message.
            /// </summary>
            public uint Message;
            /// <summary>
            /// Stores the W Param.
            /// </summary>
            public UIntPtr WParam;
            /// <summary>
            /// Stores the L Param.
            /// </summary>
            public IntPtr LParam;
            /// <summary>
            /// Stores the Time.
            /// </summary>
            public uint Time;
            /// <summary>
            /// Stores the Point.
            /// </summary>
            public System.Drawing.Point Point;
        }

        /// <summary>
        /// Executes the To ARGB logic.
        /// </summary>
        private static uint ToArgb(byte red, byte green, byte blue)
        {
            return 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
        }

        /// <summary>
        /// Executes the Blend2 logic.
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
        /// Executes the Blend3 logic.
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
        /// Executes the Scale RGB logic.
        /// </summary>
        private static int ScaleRgb(int color, int redPercent, int greenPercent, int bluePercent)
        {
            int red = Math.Min(255, (((color >> 16) & 0xFF) * redPercent) / 100);
            int green = Math.Min(255, (((color >> 8) & 0xFF) * greenPercent) / 100);
            int blue = Math.Min(255, ((color & 0xFF) * bluePercent) / 100);
            return unchecked((int)(0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | (uint)blue));
        }

        /// <summary>
        /// Executes the Blend logic.
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
