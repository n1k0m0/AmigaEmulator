using System;
using System.Drawing;

namespace SharpPixels
{
    /// <summary>Reusable CPU source upscalers. Display size and mouse geometry are independent.</summary>
    public sealed class PixelUpscaler
    {
        private uint[] _source = Array.Empty<uint>(), _intermediate = Array.Empty<uint>();
        private Rectangle _crop;
        private SharpPixelsGpuUpscaleMode _mode;
        private bool _valid;
        public uint[] Pixels { get; private set; } = Array.Empty<uint>();
        public int Width { get; private set; }
        public int Height { get; private set; }

        public void Render(ReadOnlySpan<uint> source, int width, int height, Rectangle crop, SharpPixelsGpuUpscaleMode mode)
        {
            if (width <= 0 || height <= 0 || source.Length < checked(width * height) ||
                crop.Width <= 0 || crop.Height <= 0 || crop.Left < 0 || crop.Top < 0 || crop.Right > width || crop.Bottom > height)
                throw new ArgumentOutOfRangeException(nameof(crop));
            int factor = mode switch
            {
                SharpPixelsGpuUpscaleMode.None => 1,
                SharpPixelsGpuUpscaleMode.Scale2x or SharpPixelsGpuUpscaleMode.Hq2x => 2,
                SharpPixelsGpuUpscaleMode.Scale3x or SharpPixelsGpuUpscaleMode.Hq3x => 3,
                SharpPixelsGpuUpscaleMode.Clean4 or SharpPixelsGpuUpscaleMode.Hq4x or SharpPixelsGpuUpscaleMode.Xbrz => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            int size = checked(crop.Width * crop.Height);
            bool same = _valid && _crop == crop && _mode == mode && _source.Length == size;
            if (same)
            {
                for (int y = 0; y < crop.Height && same; y++)
                    same = source.Slice((crop.Y + y) * width + crop.X, crop.Width)
                        .SequenceEqual(_source.AsSpan(y * crop.Width, crop.Width));
                if (same) return;
            }
            _valid = false;
            if (_source.Length != size) _source = new uint[size];
            for (int y = 0; y < crop.Height; y++)
                source.Slice((crop.Y + y) * width + crop.X, crop.Width).CopyTo(_source.AsSpan(y * crop.Width));
            Width = checked(crop.Width * factor); Height = checked(crop.Height * factor);
            if (Pixels.Length != checked(Width * Height)) Pixels = new uint[Width * Height];
            switch (mode)
            {
                case SharpPixelsGpuUpscaleMode.None: _source.CopyTo(Pixels, 0); break;
                case SharpPixelsGpuUpscaleMode.Scale2x: ScaleEdge(_source, crop.Width, crop.Height, Pixels, 2); break;
                case SharpPixelsGpuUpscaleMode.Scale3x: ScaleEdge(_source, crop.Width, crop.Height, Pixels, 3); break;
                case SharpPixelsGpuUpscaleMode.Clean4:
                case SharpPixelsGpuUpscaleMode.Xbrz:
                    if (_intermediate.Length != size * 4) _intermediate = new uint[size * 4];
                    ScaleEdge(_source, crop.Width, crop.Height, _intermediate, 2);
                    ScaleEdge(_intermediate, crop.Width * 2, crop.Height * 2, Pixels, 2);
                    if (mode == SharpPixelsGpuUpscaleMode.Xbrz) Smooth(crop.Width, crop.Height, factor, true);
                    break;
                default: Smooth(crop.Width, crop.Height, factor, false); break;
            }
            _crop = crop; _mode = mode; _valid = true;
        }

        // Independent implementation of the published Scale2x/Scale3x rules:
        // https://www.scale2x.it/algorithm . Neighbors clamp at the selected crop,
        // so hidden overscan cannot bleed into the presented picture.
        private static void ScaleEdge(uint[] source, int width, int height, uint[] output, int factor)
        {
            int stride = width * factor;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int left = Math.Max(0, x - 1), right = Math.Min(width - 1, x + 1);
                int top = Math.Max(0, y - 1) * width, bottom = Math.Min(height - 1, y + 1) * width, row = y * width;
                uint a = source[top + left], b = source[top + x], c = source[top + right];
                uint d = source[row + left], e = source[row + x], f = source[row + right];
                uint g = source[bottom + left], h = source[bottom + x], i = source[bottom + right];
                int at = y * factor * stride + x * factor;
                if (b == h || d == f)
                {
                    for (int dy = 0; dy < factor; dy++)
                    for (int dx = 0; dx < factor; dx++) output[at + dy * stride + dx] = e;
                    continue;
                }
                if (factor == 2)
                {
                    output[at] = d == b ? d : e; output[at + 1] = b == f ? f : e;
                    output[at + stride] = d == h ? d : e; output[at + stride + 1] = h == f ? f : e;
                }
                else
                {
                    output[at] = d == b ? d : e;
                    output[at + 1] = (d == b && e != c) || (b == f && e != a) ? b : e;
                    output[at + 2] = b == f ? f : e;
                    output[at + stride] = (d == b && e != g) || (d == h && e != a) ? d : e;
                    output[at + stride + 1] = e;
                    output[at + stride + 2] = (b == f && e != i) || (h == f && e != c) ? f : e;
                    output[at + stride * 2] = d == h ? d : e;
                    output[at + stride * 2 + 1] = (d == h && e != i) || (h == f && e != g) ? h : e;
                    output[at + stride * 2 + 2] = h == f ? f : e;
                }
            }
        }

        // Pixel-center bilinear interpolation for the legacy smooth settings.
        // These generic smooth/soft modes do not claim HQx or xBRZ conformance.
        private void Smooth(int width, int height, int factor, bool mixEdges)
        {
            for (int y = 0; y < Height; y++)
            {
                int fy = (2 * y + 1) * 128 / factor - 128;
                int y0 = Math.Clamp(fy >> 8, 0, height - 1), y1 = Math.Clamp((fy >> 8) + 1, 0, height - 1);
                int weightY = fy & 255;
                for (int x = 0; x < Width; x++)
                {
                    int fx = (2 * x + 1) * 128 / factor - 128;
                    int x0 = Math.Clamp(fx >> 8, 0, width - 1), x1 = Math.Clamp((fx >> 8) + 1, 0, width - 1);
                    int weightX = fx & 255;
                    uint top = Blend(_source[y0 * width + x0], _source[y0 * width + x1], weightX);
                    uint bottom = Blend(_source[y1 * width + x0], _source[y1 * width + x1], weightX);
                    uint color = Blend(top, bottom, weightY);
                    int at = y * Width + x;
                    Pixels[at] = mixEdges ? Blend(Pixels[at], color, 128) : color;
                }
            }
        }

        private static uint Blend(uint first, uint second, int weight)
        {
            int inverse = 256 - weight;
            uint rb = (((first & 0xFF00FF) * (uint)inverse + (second & 0xFF00FF) * (uint)weight + 0x800080) >> 8) & 0xFF00FF;
            uint green = (((first & 0xFF00) * (uint)inverse + (second & 0xFF00) * (uint)weight + 0x8000) >> 8) & 0xFF00;
            return 0xFF000000 | rb | green;
        }
    }
}
