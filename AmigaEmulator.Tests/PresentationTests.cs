using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SharpPixels;
using AmigaEmulator.Core;

namespace AmigaEmulator.Tests
{
    internal static class PresentationTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("Scale2x and Scale3x reconstruct a diagonal", TestDiagonal);
            yield return Test("All modes isolate crop edges and preserve source", TestCrop);
            yield return Test("Changed frames invalidate cache without steady state allocations", TestCache);
            yield return Test("Upscale modes share viewport; stretch fills client despite margins", () => InSta(TestGeometry));
            yield return Test("Real DIB paint applies upscale with correct crop and row stride", () => InSta(TestPaint));
            yield return Test("Frame failure diagnostic hook preserves original exception", () => InSta(TestFailureHook));
            yield return Test("Host failure reports preserve evidence and tolerate reporting failures", TestFailureReport);
        }

        private static ChipTestCase Test(string name, Action action) => new("Presentation", name,
            "Published Scale2x/3x rules; HQx; Win32 DIB and viewport regression", action);

        private static void TestDiagonal()
        {
            uint black = 0xFF000000, white = 0xFFFFFFFF;
            uint[] input = { white, white, black, white, black, black, black, black, black };
            var up = new PixelUpscaler();
            up.Render(input, 3, 3, new Rectangle(0, 0, 3, 3), SharpPixelsGpuUpscaleMode.Scale2x);
            Check.Equal(white, up.Pixels[2 * 6 + 2], "Diagonal corner becomes white.");
            Check.Equal(black, up.Pixels[2 * 6 + 3], "Top right stays black.");
            Check.Equal(black, up.Pixels[3 * 6 + 2], "Bottom left stays black.");
            Check.Equal(black, up.Pixels[3 * 6 + 3], "Bottom right stays black.");
            up.Render(input, 3, 3, new Rectangle(0, 0, 3, 3), SharpPixelsGpuUpscaleMode.Scale3x);
            uint[] expected = { white, black, black, black, black, black, black, black, black };
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++) Check.Equal(expected[y * 3 + x], up.Pixels[(3 + y) * 9 + 3 + x], "Scale3x diagonal.");
        }

        private static void TestCrop()
        {
            uint[] input = new uint[7 * 5];
            Array.Fill(input, 0xFFFF00FFu);
            for (int y = 1; y < 4; y++) for (int x = 2; x < 5; x++) input[y * 7 + x] = 0xFF123456;
            uint[] original = (uint[])input.Clone();
            var up = new PixelUpscaler();
            foreach (var mode in Enum.GetValues<SharpPixelsGpuUpscaleMode>())
            {
                up.Render(input, 7, 5, new Rectangle(2, 1, 3, 3), mode);
                int factor = mode switch { SharpPixelsGpuUpscaleMode.None => 1,
                    SharpPixelsGpuUpscaleMode.Scale2x or SharpPixelsGpuUpscaleMode.Hq2x => 2,
                    SharpPixelsGpuUpscaleMode.Scale3x or SharpPixelsGpuUpscaleMode.Hq3x => 3, _ => 4 };
                Check.Equal(3 * factor, up.Width, "Crop width " + mode);
                Check.Equal(3 * factor, up.Height, "Crop height " + mode);
                foreach (uint pixel in up.Pixels) Check.Equal(0xFF123456u, pixel, "No hidden border bleeding " + mode);
                up.Render(input, 7, 5, new Rectangle(2, 1, 1, 1), mode);
                foreach (uint pixel in up.Pixels) Check.Equal(0xFF123456u, pixel, "Single pixel clamps neighbors " + mode);
            }
            Check.True(input.AsSpan().SequenceEqual(original), "Source framebuffer remains unchanged.");
        }

        private static void TestCache()
        {
            var up = new PixelUpscaler(); uint[] input = new uint[64 * 32]; Array.Fill(input, 0xFF112233u);
            var crop = new Rectangle(2, 1, 60, 30);
            foreach (var mode in Enum.GetValues<SharpPixelsGpuUpscaleMode>())
            {
                up.Render(input, 64, 32, crop, mode);
                uint[] buffer = up.Pixels;
                for (int i = 0; i < 20; i++) up.Render(input, 64, 32, crop, mode);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    Array.Fill(input, (uint)(0xFF000000 | i));
                    up.Render(input, 64, 32, crop, mode);
                    Check.Equal(input[64 + 2], up.Pixels[0], "Changed frame reaches top left.");
                    up.Render(input, 64, 32, crop, mode);
                }
                Check.True(GC.GetAllocatedBytesForCurrentThread() - before <= 1024, "No per-frame upscale buffer allocation " + mode);
                Check.True(ReferenceEquals(buffer, up.Pixels), "Buffer reused after input changes " + mode);
                // Exercise the non-uniform edge/interpolation path as well as the solid-color fast path.
                Array.Fill(input, 0xFF000000u);
                input[15 * 64 + 32] = 0xFFFFFFFF;
                up.Render(input, 64, 32, crop, mode);
                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    input[15 * 64 + 32] ^= 1;
                    up.Render(input, 64, 32, crop, mode);
                }
                Check.True(GC.GetAllocatedBytesForCurrentThread() - before <= 1024, "No per-frame edge interpolation allocation " + mode);
                Check.True(ReferenceEquals(buffer, up.Pixels), "Edge interpolation reuses output " + mode);
            }
            uint[] diagonal = { 0xFFFFFFFF, 0xFFFFFFFF, 0xFF000000, 0xFFFFFFFF, 0xFF000000, 0xFF000000, 0xFF000000, 0xFF000000, 0xFF000000 };
            up.Render(diagonal, 3, 3, new Rectangle(0, 0, 3, 3), SharpPixelsGpuUpscaleMode.Scale2x);
            uint[] sharpBuffer = up.Pixels;
            up.Render(diagonal, 3, 3, new Rectangle(0, 0, 3, 3), SharpPixelsGpuUpscaleMode.Hq2x);
            Check.True(ReferenceEquals(sharpBuffer, up.Pixels), "Same size mode reuses buffer.");
            // FFmpeg hqx=n=2 reference for this complete 3x3 fixture.
            Check.Equal(0xFF7F7F7Fu, up.Pixels[2 * 6 + 2], "Changing mode recomputes even when source is unchanged.");
        }

        private static void TestGeometry()
        {
            using var window = new Fixture(704, 288, 352, 288);
            window.ClientSize = new Size(1920, 1080);
            foreach (var mode in Enum.GetValues<SharpPixelsGpuUpscaleMode>())
            {
                window.SetPresentationEffects(SharpPixelsGpuFilterMode.Sharp, mode);
                Check.Equal(new Rectangle(300, 0, 1320, 1080), window.Target, "Same original aspect target " + mode);
                window.SetPresentationSourceCrop(8, 3, 10, 4);
                window.SetPresentationTargetMargins(4, 2, 5, 3);
                window.SetPresentationStretchToClient(true);
                Check.Equal(new Rectangle(0, 0, 1920, 1080), window.Target, "Entire client in stretch " + mode);
                Check.Equal(new Rectangle(8, 3, 686, 281), window.Source, "Native crop independent of upscale " + mode);
                window.SetPresentationStretchToClient(false);
                window.SetPresentationTargetMargins(0, 0, 0, 0);
            }
        }

        private static void TestPaint()
        {
            using var window = new Fixture(5, 4, 3, 2);
            window.ClientSize = new Size(600, 400);
            Array.Fill(window.Pixels, 0xFFFF00FFu);
            uint[] input = { 0xFF00FF00, 0xFF00FF00, 0xFF000000, 0xFF00FF00, 0xFF000000, 0xFF000000 };
            for (int y = 0; y < 2; y++) Array.Copy(input, y * 3, window.Pixels, (y + 1) * 5 + 1, 3);
            window.SetPresentationSourceCrop(1, 1, 1, 1);
            window.SetPresentationStretchToClient(true);
            foreach (var mode in Enum.GetValues<SharpPixelsGpuUpscaleMode>())
            {
                window.SetPresentationEffects(SharpPixelsGpuFilterMode.Sharp, mode);
                using Bitmap painted = window.PaintFrame();
                Check.Equal(unchecked((int)0xFF00FF00), painted.GetPixel(0, 0).ToArgb(), "Top left crop " + mode);
                Check.Equal(Color.Black.ToArgb(), painted.GetPixel(599, 399).ToArgb(), "Bottom right crop/stride " + mode);
                if (mode == SharpPixelsGpuUpscaleMode.Scale2x || mode == SharpPixelsGpuUpscaleMode.Scale3x)
                    Check.Equal(Color.Lime.ToArgb(), painted.GetPixel(201, 201).ToArgb(), "Actual painted diagonal corner is upscaled " + mode);
                if (mode == SharpPixelsGpuUpscaleMode.None)
                    Check.Equal(Color.Black.ToArgb(), painted.GetPixel(201, 201).ToArgb(), "Native nearest-neighbor diagonal.");
                if (mode == SharpPixelsGpuUpscaleMode.Hq2x || mode == SharpPixelsGpuUpscaleMode.Hq3x || mode == SharpPixelsGpuUpscaleMode.Hq4x)
                {
                    // Independent ffmpeg -vf hqx=n=2/3/4 outputs for this cropped 3x2 source.
                    int expected = mode == SharpPixelsGpuUpscaleMode.Hq2x ? unchecked((int)0xFF00BF00) : Color.Lime.ToArgb();
                    Check.Equal(expected, painted.GetPixel(201, 201).ToArgb(), "HQx reference color reaches real paint " + mode);
                }
            }
        }

        private static void InSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) throw failure;
        }

        private static void TestFailureHook()
        {
            using var fixture = new FailureFixture();
            MethodInfo pump = typeof(SharpPixelsWindow).GetMethod("PumpOneFrame", BindingFlags.Instance | BindingFlags.NonPublic);
            Exception observed = null;
            try { pump.Invoke(fixture, null); }
            catch (TargetInvocationException error) { observed = error.InnerException; }
            Check.True(ReferenceEquals(fixture.Failure, observed), "Original host exception is not swallowed or replaced by reporting.");
            Check.True(ReferenceEquals(fixture.Failure, fixture.Reported), "Failure hook receives the precise original exception.");
        }

        private static void TestFailureReport()
        {
            string directory = Path.Combine(Path.GetTempPath(), "amiga-host-report-test-" + Guid.NewGuid().ToString("N"));
            var error = new InvalidOperationException("synthetic frame error", new IOException("synthetic disk detail"));
            string first = HostFailureReport.TryWrite(directory, error, () => "CPU test PC\nDF0 test image");
            Check.True(first != null, "Report written.");
            string original = File.ReadAllText(first);
            Check.True(original.Contains("synthetic frame error") && original.Contains("synthetic disk detail") &&
                original.Contains("CPU test PC") && original.Contains("DF0 test image"), "Original/inner errors and machine context recorded.");
            string second = HostFailureReport.TryWrite(directory, error, () => throw new InvalidOperationException("failed context"));
            Check.True(second != null && second != first && File.ReadAllText(second).Contains("Context unavailable"), "Context failure still records original error in a fresh file.");
            Check.Equal(original, File.ReadAllText(first), "Earlier evidence is never overwritten.");
            Check.True(HostFailureReport.TryWrite(first, error, () => "context") == null, "Invalid output directory does not throw during crash reporting.");
        }

        private sealed class FailureFixture : SharpPixelsWindow
        {
            public readonly Exception Failure = new InvalidOperationException("synthetic frame failure");
            public Exception Reported;
            public FailureFixture() : base(8, 8, "Failure test") { }
            protected override void OnUpdateFrame() => throw Failure;
            protected override void OnFrameFailure(Exception error) { Reported = error; throw new IOException("report hook failure"); }
        }

        private sealed class Fixture : SharpPixelsWindow
        {
            public Fixture(int w, int h, int pw, int ph) : base(w, h, pw, ph, "Presentation test") { }
            public Rectangle Target => GetPresentationRectangle();
            public Rectangle Source => GetPresentationSourceRectangle();
            public Bitmap PaintFrame()
            {
                var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
                using var graphics = Graphics.FromImage(bitmap);
                OnPaint(new PaintEventArgs(graphics, new Rectangle(Point.Empty, ClientSize)));
                return bitmap;
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) OnFormClosed(new FormClosedEventArgs(CloseReason.None));
                base.Dispose(disposing);
            }
        }
    }
}
