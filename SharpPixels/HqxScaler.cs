/*
 * Copyright (c) 2014 Clément Bœsch
 *
 * This file is part of FFmpeg.
 *
 * Permission to use, copy, modify, and/or distribute this software for any
 * purpose with or without fee is hereby granted, provided that the above
 * copyright notice and this permission notice appear in all copies.
 *
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
 * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
 * ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
 * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
 * ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
 * OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
 */
// Managed adaptation of FFmpeg vf_hqx.c, commit f7368f97b92a0afe8dc8368a4b6749704b740317.
// HQx originally designed by Maxim Stepin. See THIRD-PARTY-NOTICES.md.
using System;
using System.Runtime.CompilerServices;

namespace SharpPixels
{
    internal static class HqxScaler
    {
        public static void Render(uint[] source, uint[] yuv, int width, int height, uint[] output, int factor)
        {
            for (int i = 0; i < source.Length; i++) yuv[i] = ToYuv(source[i]);
            int stride = width * factor;
            uint[] rules = Rules.Get(factor);
            Span<uint> w = stackalloc uint[9], colors = stackalloc uint[9];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int left = Math.Max(0, x - 1), right = Math.Min(width - 1, x + 1);
                    int top = Math.Max(0, y - 1) * width, bottom = Math.Min(height - 1, y + 1) * width, row = y * width;
                    w[0] = source[top + left]; w[1] = source[top + x]; w[2] = source[top + right];
                    w[3] = source[row + left]; w[4] = source[row + x]; w[5] = source[row + right];
                    w[6] = source[bottom + left]; w[7] = source[bottom + x]; w[8] = source[bottom + right];
                    int at = y * factor * stride + x * factor;
                    if (w[0] == w[4] && w[1] == w[4] && w[2] == w[4] && w[3] == w[4] &&
                        w[5] == w[4] && w[6] == w[4] && w[7] == w[4] && w[8] == w[4])
                    {
                        for (int dy = 0; dy < factor; dy++) for (int dx = 0; dx < factor; dx++) output[at + dy * stride + dx] = w[4];
                        continue;
                    }
                    colors[0] = yuv[top + left]; colors[1] = yuv[top + x]; colors[2] = yuv[top + right];
                    colors[3] = yuv[row + left]; colors[4] = yuv[row + x]; colors[5] = yuv[row + right];
                    colors[6] = yuv[bottom + left]; colors[7] = yuv[bottom + x]; colors[8] = yuv[bottom + right];
                    int pattern = 0;
                    for (int i = 0; i < 9; i++) if (i != 4 && Different(colors[4], colors[i])) pattern |= 1 << DropCenter(i);
                    // Four cardinal color differences complete the pattern key. A rule
                    // contains the original interpolation indices/weights, not output colors.
                    int key = pattern;
                    if (Different(colors[1], colors[5])) key |= 1 << 8;
                    if (Different(colors[5], colors[7])) key |= 1 << 9;
                    if (Different(colors[7], colors[3])) key |= 1 << 10;
                    if (Different(colors[3], colors[1])) key |= 1 << 11;
                    int ruleAt = key * factor * factor;
                    for (int dy = 0; dy < factor; dy++)
                        for (int dx = 0; dx < factor; dx++)
                            output[at + dy * stride + dx] = Interpolate(w, rules[ruleAt + dy * factor + dx]);
                }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ToYuv(uint c)
        {
            int r = (int)(c >> 16) & 255, g = (int)(c >> 8) & 255, b = (int)c & 255;
            int y = (299 * r + 587 * g + 114 * b) / 1000;
            int u = (-169 * (r - g) + 500 * (b - g)) / 1000 + 128;
            int v = (500 * (r - g) - 81 * (b - g)) / 1000 + 128;
            return (uint)(y << 16 | u << 8 | v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Different(uint a, uint b) => Math.Abs((int)(a & 0xff0000) - (int)(b & 0xff0000)) > (48 << 16) ||
            Math.Abs((int)(a & 0xff00) - (int)(b & 0xff00)) > (7 << 8) || Math.Abs((int)(a & 255) - (int)(b & 255)) > 6;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Mix2(uint c1, int w1, uint c2, int w2, int shift) =>
            (((((c1 & 0xff00ff00) >> 8) * (uint)w1 + ((c2 & 0xff00ff00) >> 8) * (uint)w2) << (8 - shift)) & 0xff00ff00) |
            ((((c1 & 0x00ff00ff) * (uint)w1 + (c2 & 0x00ff00ff) * (uint)w2) >> shift) & 0x00ff00ff);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Mix3(uint c1, int w1, uint c2, int w2, uint c3, int w3, int shift) =>
            (((((c1 & 0xff00ff00) >> 8) * (uint)w1 + ((c2 & 0xff00ff00) >> 8) * (uint)w2 + ((c3 & 0xff00ff00) >> 8) * (uint)w3) << (8 - shift)) & 0xff00ff00) |
            ((((c1 & 0x00ff00ff) * (uint)w1 + (c2 & 0x00ff00ff) * (uint)w2 + (c3 & 0x00ff00ff) * (uint)w3) >> shift) & 0x00ff00ff);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DropCenter(int n) => n > 4 ? n - 1 : n;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Interpolate(ReadOnlySpan<uint> w, uint rule)
        {
            uint first = w[(int)(rule & 15)];
            int w2 = (int)(rule >> 16) & 15;
            if (w2 == 0) return first;
            uint second = w[(int)(rule >> 4) & 15];
            int w1 = (int)(rule >> 12) & 15, w3 = (int)(rule >> 20) & 15;
            return w3 == 0 ? Mix2(first, w1, second, w2, 4) :
                Mix3(first, w1, second, w2, w[(int)(rule >> 8) & 15], w3, 4);
        }

        // The branching HQx kernels generate 4,096 rule entries once per algorithm.
        // Runtime frames use compact weights and preserve the exact integer rounding.
        private static class Rules
        {
            private static readonly uint[] Hq2Rules = Build(2), Hq3Rules = Build(3), Hq4Rules = Build(4);
            public static uint[] Get(int factor) => factor == 2 ? Hq2Rules : factor == 3 ? Hq3Rules : Hq4Rules;

            private static uint[] Build(int factor)
            {
                uint[] table = new uint[4096 * factor * factor];
                ReadOnlySpan<uint> w = stackalloc uint[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
                for (int key = 0; key < 4096; key++)
                {
                    int at = key * factor * factor, k = key & 255;
                    if (factor == 2)
                    {
                        table[at] = Hq2(w, key, k, 0, 1, 2, 3, 4, 5, 6, 7, 8);
                        table[at + 1] = Hq2(w, key, k, 2, 1, 0, 5, 4, 3, 8, 7, 6);
                        table[at + 2] = Hq2(w, key, k, 6, 7, 8, 3, 4, 5, 0, 1, 2);
                        table[at + 3] = Hq2(w, key, k, 8, 7, 6, 5, 4, 3, 2, 1, 0);
                    }
                    else if (factor == 3)
                    {
                        Hq3(table, at, factor, w, key, k, 0, 1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 0);
                        Hq3(table, at + 1, factor, w, key, k, 1, 3, 2, 5, 8, 1, 4, 7, 0, 3, 6, 1);
                        Hq3(table, at + factor, factor, w, key, k, 2, 0, 6, 3, 0, 7, 4, 1, 8, 5, 2, 1);
                        Hq3(table, at + factor + 1, factor, w, key, k, 3, 2, 8, 7, 6, 5, 4, 3, 2, 1, 0, 0);
                        table[at + factor + 1] = w[4];
                    }
                    else
                    {
                        Hq4(table, at, factor, w, key, k, 0, 1, 2, 3, 0, 1, 2, 3, 4, 5, 6, 7, 8);
                        Hq4(table, at + 2, factor, w, key, k, 1, 0, 3, 2, 2, 1, 0, 5, 4, 3, 8, 7, 6);
                        Hq4(table, at + 2 * factor, factor, w, key, k, 2, 3, 0, 1, 6, 7, 8, 3, 4, 5, 0, 1, 2);
                        Hq4(table, at + 2 * factor + 2, factor, w, key, k, 3, 2, 1, 0, 8, 7, 6, 5, 4, 3, 2, 1, 0);
                    }
                }
                return table;
            }

            private static bool ColorDifference(int key, int a, int b) =>
                (key & (1 << ((a + b) switch { 6 => 8, 12 => 9, 10 => 10, 4 => 11, _ => throw new InvalidOperationException("Non-cardinal HQx pair.") }))) != 0;

            // Index and weight nibbles. Normalize all denominators to 16 once,
            // so runtime interpolation uses constant shifts and identical rounding.
            private static uint Blend2(uint c1, int w1, uint c2, int w2, int shift) =>
                c1 | c2 << 4 | (uint)(w1 << (4 - shift)) << 12 | (uint)(w2 << (4 - shift)) << 16;
            private static uint Blend3(uint c1, int w1, uint c2, int w2, uint c3, int w3, int shift) =>
                Blend2(c1, w1, c2, w2, shift) | c3 << 8 | (uint)(w3 << (4 - shift)) << 20;

            private static int DropCenter(int n) => n > 4 ? n - 1 : n;

            private static int Shuffle(int k, int rotate, int p0, int p1, int p2, int p3, int p5, int p6, int p7, int p8) =>
                Shift(k, rotate, 0, p0) | Shift(k, rotate, 1, p1) | Shift(k, rotate, 2, p2) | Shift(k, rotate, 3, p3) |
                Shift(k, rotate, 5, p5) | Shift(k, rotate, 6, p6) | Shift(k, rotate, 7, p7) | Shift(k, rotate, 8, p8);

            private static int Shift(int k, int rotate, int n, int position) => ((k >> (rotate != 0 ? 7 - DropCenter(n) : DropCenter(n))) & 1) << DropCenter(position);

            private static uint Hq2(ReadOnlySpan<uint> w, int differences, int k, int p0, int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8)
            {
                int kShuffled = Shuffle(k, 0, p0, p1, p2, p3, p5, p6, p7, p8);
                uint w0 = w[p0], w1 = w[p1], w3 = w[p3], w4 = w[p4], w5 = w[p5], w7 = w[p7];

                if ((((kShuffled & 0xbf) == 0x37) || ((kShuffled & 0xdb) == 0x13)) && ColorDifference(differences, p1, p5))
                    return Blend2(w4, 3, w3, 1, 2);
                if ((((kShuffled & 0xdb) == 0x49) || ((kShuffled & 0xef) == 0x6d)) && ColorDifference(differences, p7, p3))
                    return Blend2(w4, 3, w1, 1, 2);
                if ((((kShuffled & 0x0b) == 0x0b) || ((kShuffled & 0xfe) == 0x4a) || ((kShuffled & 0xfe) == 0x1a)) && ColorDifference(differences, p3, p1))
                    return w4;
                if ((((kShuffled & 0x6f) == 0x2a) || ((kShuffled & 0x5b) == 0x0a) || ((kShuffled & 0xbf) == 0x3a) || ((kShuffled & 0xdf) == 0x5a) ||
                     ((kShuffled & 0x9f) == 0x8a) || ((kShuffled & 0xcf) == 0x8a) || ((kShuffled & 0xef) == 0x4e) || ((kShuffled & 0x3f) == 0x0e) ||
                     ((kShuffled & 0xfb) == 0x5a) || ((kShuffled & 0xbb) == 0x8a) || ((kShuffled & 0x7f) == 0x5a) || ((kShuffled & 0xaf) == 0x8a) ||
                     ((kShuffled & 0xeb) == 0x8a)) && ColorDifference(differences, p3, p1))
                    return Blend2(w4, 3, w0, 1, 2);
                if (((kShuffled & 0x0b) == 0x08))
                    return Blend3(w4, 2, w0, 1, w1, 1, 2);
                if (((kShuffled & 0x0b) == 0x02))
                    return Blend3(w4, 2, w0, 1, w3, 1, 2);
                if (((kShuffled & 0x2f) == 0x2f))
                    return Blend3(w4, 14, w3, 1, w1, 1, 4);
                if (((kShuffled & 0xbf) == 0x37) || ((kShuffled & 0xdb) == 0x13))
                    return Blend3(w4, 5, w1, 2, w3, 1, 3);
                if (((kShuffled & 0xdb) == 0x49) || ((kShuffled & 0xef) == 0x6d))
                    return Blend3(w4, 5, w3, 2, w1, 1, 3);
                if (((kShuffled & 0x1b) == 0x03) || ((kShuffled & 0x4f) == 0x43) || ((kShuffled & 0x8b) == 0x83) || ((kShuffled & 0x6b) == 0x43))
                    return Blend2(w4, 3, w3, 1, 2);
                if (((kShuffled & 0x4b) == 0x09) || ((kShuffled & 0x8b) == 0x89) || ((kShuffled & 0x1f) == 0x19) || ((kShuffled & 0x3b) == 0x19))
                    return Blend2(w4, 3, w1, 1, 2);
                if (((kShuffled & 0x7e) == 0x2a) || ((kShuffled & 0xef) == 0xab) || ((kShuffled & 0xbf) == 0x8f) || ((kShuffled & 0x7e) == 0x0e))
                    return Blend3(w4, 2, w3, 3, w1, 3, 3);
                if (((kShuffled & 0xfb) == 0x6a) || ((kShuffled & 0x6f) == 0x6e) || ((kShuffled & 0x3f) == 0x3e) || ((kShuffled & 0xfb) == 0xfa) ||
                    ((kShuffled & 0xdf) == 0xde) || ((kShuffled & 0xdf) == 0x1e))
                    return Blend2(w4, 3, w0, 1, 2);
                if (((kShuffled & 0x0a) == 0x00) || ((kShuffled & 0x4f) == 0x4b) || ((kShuffled & 0x9f) == 0x1b) || ((kShuffled & 0x2f) == 0x0b) ||
                    ((kShuffled & 0xbe) == 0x0a) || ((kShuffled & 0xee) == 0x0a) || ((kShuffled & 0x7e) == 0x0a) || ((kShuffled & 0xeb) == 0x4b) ||
                    ((kShuffled & 0x3b) == 0x1b))
                    return Blend3(w4, 2, w3, 1, w1, 1, 2);
                return Blend3(w4, 6, w3, 1, w1, 1, 3);

            }

            private static void Hq3(uint[] dst, int at, int stride, ReadOnlySpan<uint> w, int differences, int k, int pos00, int pos01, int p0, int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8, int rotate)
            {
                int kShuffled = Shuffle(k, rotate, p0, p1, p2, p3, p5, p6, p7, p8);
                uint w0 = w[p0], w1 = w[p1], w3 = w[p3], w4 = w[p4], w5 = w[p5], w7 = w[p7];

                int dst00 = at + stride * (pos00 >> 1) + (pos00 & 1);
                int dst01 = at + stride * (pos01 >> 1) + (pos01 & 1);

                if ((((kShuffled & 0xdb) == 0x49) || ((kShuffled & 0xef) == 0x6d)) && ColorDifference(differences, p7, p3))
                    dst[dst00] = Blend2(w4, 3, w1, 1, 2);
                else if ((((kShuffled & 0xbf) == 0x37) || ((kShuffled & 0xdb) == 0x13)) && ColorDifference(differences, p1, p5))
                    dst[dst00] = Blend2(w4, 3, w3, 1, 2);
                else if ((((kShuffled & 0x0b) == 0x0b) || ((kShuffled & 0xfe) == 0x4a) || ((kShuffled & 0xfe) == 0x1a)) && ColorDifference(differences, p3, p1))
                    dst[dst00] = w4;
                else if ((((kShuffled & 0x6f) == 0x2a) || ((kShuffled & 0x5b) == 0x0a) || ((kShuffled & 0xbf) == 0x3a) || ((kShuffled & 0xdf) == 0x5a) ||
                          ((kShuffled & 0x9f) == 0x8a) || ((kShuffled & 0xcf) == 0x8a) || ((kShuffled & 0xef) == 0x4e) || ((kShuffled & 0x3f) == 0x0e) ||
                          ((kShuffled & 0xfb) == 0x5a) || ((kShuffled & 0xbb) == 0x8a) || ((kShuffled & 0x7f) == 0x5a) || ((kShuffled & 0xaf) == 0x8a) ||
                          ((kShuffled & 0xeb) == 0x8a)) && ColorDifference(differences, p3, p1))
                    dst[dst00] = Blend2(w4, 3, w0, 1, 2);
                else if (((kShuffled & 0x4b) == 0x09) || ((kShuffled & 0x8b) == 0x89) || ((kShuffled & 0x1f) == 0x19) || ((kShuffled & 0x3b) == 0x19))
                    dst[dst00] = Blend2(w4, 3, w1, 1, 2);
                else if (((kShuffled & 0x1b) == 0x03) || ((kShuffled & 0x4f) == 0x43) || ((kShuffled & 0x8b) == 0x83) || ((kShuffled & 0x6b) == 0x43))
                    dst[dst00] = Blend2(w4, 3, w3, 1, 2);
                else if (((kShuffled & 0x7e) == 0x2a) || ((kShuffled & 0xef) == 0xab) || ((kShuffled & 0xbf) == 0x8f) || ((kShuffled & 0x7e) == 0x0e))
                    dst[dst00] = Blend2(w3, 1, w1, 1, 1);
                else if (((kShuffled & 0x4f) == 0x4b) || ((kShuffled & 0x9f) == 0x1b) || ((kShuffled & 0x2f) == 0x0b) || ((kShuffled & 0xbe) == 0x0a) ||
                         ((kShuffled & 0xee) == 0x0a) || ((kShuffled & 0x7e) == 0x0a) || ((kShuffled & 0xeb) == 0x4b) || ((kShuffled & 0x3b) == 0x1b))
                    dst[dst00] = Blend3(w4, 2, w3, 7, w1, 7, 4);
                else if (((kShuffled & 0x0b) == 0x08) || ((kShuffled & 0xf9) == 0x68) || ((kShuffled & 0xf3) == 0x62) || ((kShuffled & 0x6d) == 0x6c) ||
                         ((kShuffled & 0x67) == 0x66) || ((kShuffled & 0x3d) == 0x3c) || ((kShuffled & 0x37) == 0x36) || ((kShuffled & 0xf9) == 0xf8) ||
                         ((kShuffled & 0xdd) == 0xdc) || ((kShuffled & 0xf3) == 0xf2) || ((kShuffled & 0xd7) == 0xd6) || ((kShuffled & 0xdd) == 0x1c) ||
                         ((kShuffled & 0xd7) == 0x16) || ((kShuffled & 0x0b) == 0x02))
                    dst[dst00] = Blend2(w4, 3, w0, 1, 2);
                else
                    dst[dst00] = Blend3(w4, 2, w3, 1, w1, 1, 2);

                if ((((kShuffled & 0xfe) == 0xde) || ((kShuffled & 0x9e) == 0x16) || ((kShuffled & 0xda) == 0x12) || ((kShuffled & 0x17) == 0x16) ||
                     ((kShuffled & 0x5b) == 0x12) || ((kShuffled & 0xbb) == 0x12)) && ColorDifference(differences, p1, p5))
                    dst[dst01] = w4;
                else if ((((kShuffled & 0x0f) == 0x0b) || ((kShuffled & 0x5e) == 0x0a) || ((kShuffled & 0xfb) == 0x7b) || ((kShuffled & 0x3b) == 0x0b) ||
                          ((kShuffled & 0xbe) == 0x0a) || ((kShuffled & 0x7a) == 0x0a)) && ColorDifference(differences, p3, p1))
                    dst[dst01] = w4;
                else if (((kShuffled & 0xbf) == 0x8f) || ((kShuffled & 0x7e) == 0x0e) || ((kShuffled & 0xbf) == 0x37) || ((kShuffled & 0xdb) == 0x13))
                    dst[dst01] = Blend2(w1, 3, w4, 1, 2);
                else if (((kShuffled & 0x02) == 0x00) || ((kShuffled & 0x7c) == 0x28) || ((kShuffled & 0xed) == 0xa9) || ((kShuffled & 0xf5) == 0xb4) ||
                         ((kShuffled & 0xd9) == 0x90))
                    dst[dst01] = Blend2(w4, 3, w1, 1, 2);
                else if (((kShuffled & 0x4f) == 0x4b) || ((kShuffled & 0xfb) == 0x7b) || ((kShuffled & 0xfe) == 0x7e) || ((kShuffled & 0x9f) == 0x1b) ||
                         ((kShuffled & 0x2f) == 0x0b) || ((kShuffled & 0xbe) == 0x0a) || ((kShuffled & 0x7e) == 0x0a) || ((kShuffled & 0xfb) == 0x4b) ||
                         ((kShuffled & 0xfb) == 0xdb) || ((kShuffled & 0xfe) == 0xde) || ((kShuffled & 0xfe) == 0x56) || ((kShuffled & 0x57) == 0x56) ||
                         ((kShuffled & 0x97) == 0x16) || ((kShuffled & 0x3f) == 0x1e) || ((kShuffled & 0xdb) == 0x12) || ((kShuffled & 0xbb) == 0x12))
                    dst[dst01] = Blend2(w4, 7, w1, 1, 3);
                else
                    dst[dst01] = w4;

            }

            private static void Hq4(uint[] dst, int at, int stride, ReadOnlySpan<uint> w, int differences, int k, int pos00, int pos01, int pos10, int pos11, int p0, int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8)
            {
                int kShuffled = Shuffle(k, 0, p0, p1, p2, p3, p5, p6, p7, p8);
                uint w0 = w[p0], w1 = w[p1], w3 = w[p3], w4 = w[p4], w5 = w[p5], w7 = w[p7];

                int dst00 = at + stride * (pos00 >> 1) + (pos00 & 1);
                int dst01 = at + stride * (pos01 >> 1) + (pos01 & 1);
                int dst10 = at + stride * (pos10 >> 1) + (pos10 & 1);
                int dst11 = at + stride * (pos11 >> 1) + (pos11 & 1);

                bool cond00 = (((kShuffled & 0xbf) == 0x37) || ((kShuffled & 0xdb) == 0x13)) && ColorDifference(differences, p1, p5);
                bool cond01 = (((kShuffled & 0xdb) == 0x49) || ((kShuffled & 0xef) == 0x6d)) && ColorDifference(differences, p7, p3);
                bool cond02 = (((kShuffled & 0x6f) == 0x2a) || ((kShuffled & 0x5b) == 0x0a) || ((kShuffled & 0xbf) == 0x3a) ||
                                    ((kShuffled & 0xdf) == 0x5a) || ((kShuffled & 0x9f) == 0x8a) || ((kShuffled & 0xcf) == 0x8a) ||
                                    ((kShuffled & 0xef) == 0x4e) || ((kShuffled & 0x3f) == 0x0e) || ((kShuffled & 0xfb) == 0x5a) ||
                                    ((kShuffled & 0xbb) == 0x8a) || ((kShuffled & 0x7f) == 0x5a) || ((kShuffled & 0xaf) == 0x8a) ||
                                    ((kShuffled & 0xeb) == 0x8a)) && ColorDifference(differences, p3, p1);
                bool cond03 = ((kShuffled & 0xdb) == 0x49) || ((kShuffled & 0xef) == 0x6d);
                bool cond04 = ((kShuffled & 0xbf) == 0x37) || ((kShuffled & 0xdb) == 0x13);
                bool cond05 = ((kShuffled & 0x1b) == 0x03) || ((kShuffled & 0x4f) == 0x43) || ((kShuffled & 0x8b) == 0x83) ||
                                   ((kShuffled & 0x6b) == 0x43);
                bool cond06 = ((kShuffled & 0x4b) == 0x09) || ((kShuffled & 0x8b) == 0x89) || ((kShuffled & 0x1f) == 0x19) ||
                                   ((kShuffled & 0x3b) == 0x19);
                bool cond07 = ((kShuffled & 0x0b) == 0x08) || ((kShuffled & 0xf9) == 0x68) || ((kShuffled & 0xf3) == 0x62) ||
                                   ((kShuffled & 0x6d) == 0x6c) || ((kShuffled & 0x67) == 0x66) || ((kShuffled & 0x3d) == 0x3c) ||
                                   ((kShuffled & 0x37) == 0x36) || ((kShuffled & 0xf9) == 0xf8) || ((kShuffled & 0xdd) == 0xdc) ||
                                   ((kShuffled & 0xf3) == 0xf2) || ((kShuffled & 0xd7) == 0xd6) || ((kShuffled & 0xdd) == 0x1c) ||
                                   ((kShuffled & 0xd7) == 0x16) || ((kShuffled & 0x0b) == 0x02);
                bool cond08 = (((kShuffled & 0x0f) == 0x0b) || ((kShuffled & 0x2b) == 0x0b) || ((kShuffled & 0xfe) == 0x4a) ||
                                    ((kShuffled & 0xfe) == 0x1a)) && ColorDifference(differences, p3, p1);
                bool cond09 = ((kShuffled & 0x2f) == 0x2f);
                bool cond10 = ((kShuffled & 0x0a) == 0x00);
                bool cond11 = ((kShuffled & 0x0b) == 0x09);
                bool cond12 = ((kShuffled & 0x7e) == 0x2a) || ((kShuffled & 0xef) == 0xab);
                bool cond13 = ((kShuffled & 0xbf) == 0x8f) || ((kShuffled & 0x7e) == 0x0e);
                bool cond14 = ((kShuffled & 0x4f) == 0x4b) || ((kShuffled & 0x9f) == 0x1b) || ((kShuffled & 0x2f) == 0x0b) ||
                                   ((kShuffled & 0xbe) == 0x0a) || ((kShuffled & 0xee) == 0x0a) || ((kShuffled & 0x7e) == 0x0a) ||
                                   ((kShuffled & 0xeb) == 0x4b) || ((kShuffled & 0x3b) == 0x1b);
                bool cond15 = ((kShuffled & 0x0b) == 0x03);

                if (cond00)
                    dst[dst00] = Blend2(w4, 5, w3, 3, 3);
                else if (cond01)
                    dst[dst00] = Blend2(w4, 5, w1, 3, 3);
                else if ((((kShuffled & 0x0b) == 0x0b) || ((kShuffled & 0xfe) == 0x4a) || ((kShuffled & 0xfe) == 0x1a)) && ColorDifference(differences, p3, p1))
                    dst[dst00] = w4;
                else if (cond02)
                    dst[dst00] = Blend2(w4, 5, w0, 3, 3);
                else if (cond03)
                    dst[dst00] = Blend2(w4, 3, w3, 1, 2);
                else if (cond04)
                    dst[dst00] = Blend2(w4, 3, w1, 1, 2);
                else if (cond05)
                    dst[dst00] = Blend2(w4, 5, w3, 3, 3);
                else if (cond06)
                    dst[dst00] = Blend2(w4, 5, w1, 3, 3);
                else if (((kShuffled & 0x0f) == 0x0b) || ((kShuffled & 0x5e) == 0x0a) || ((kShuffled & 0x2b) == 0x0b) || ((kShuffled & 0xbe) == 0x0a) ||
                         ((kShuffled & 0x7a) == 0x0a) || ((kShuffled & 0xee) == 0x0a))
                    dst[dst00] = Blend2(w1, 1, w3, 1, 1);
                else if (cond07)
                    dst[dst00] = Blend2(w4, 5, w0, 3, 3);
                else
                    dst[dst00] = Blend3(w4, 2, w1, 1, w3, 1, 2);

                if (cond00)
                    dst[dst01] = Blend2(w4, 7, w3, 1, 3);
                else if (cond08)
                    dst[dst01] = w4;
                else if (cond02)
                    dst[dst01] = Blend2(w4, 3, w0, 1, 2);
                else if (cond09)
                    dst[dst01] = w4;
                else if (cond10)
                    dst[dst01] = Blend3(w4, 5, w1, 2, w3, 1, 3);
                else if (((kShuffled & 0x0b) == 0x08))
                    dst[dst01] = Blend3(w4, 5, w1, 2, w0, 1, 3);
                else if (cond11)
                    dst[dst01] = Blend2(w4, 5, w1, 3, 3);
                else if (cond04)
                    dst[dst01] = Blend2(w1, 3, w4, 1, 2);
                else if (cond12)
                    dst[dst01] = Blend3(w1, 2, w4, 1, w3, 1, 2);
                else if (cond13)
                    dst[dst01] = Blend2(w1, 5, w3, 3, 3);
                else if (cond05)
                    dst[dst01] = Blend2(w4, 7, w3, 1, 3);
                else if (((kShuffled & 0xf3) == 0x62) || ((kShuffled & 0x67) == 0x66) || ((kShuffled & 0x37) == 0x36) || ((kShuffled & 0xf3) == 0xf2) ||
                         ((kShuffled & 0xd7) == 0xd6) || ((kShuffled & 0xd7) == 0x16) || ((kShuffled & 0x0b) == 0x02))
                    dst[dst01] = Blend2(w4, 3, w0, 1, 2);
                else if (cond14)
                    dst[dst01] = Blend2(w1, 1, w4, 1, 1);
                else
                    dst[dst01] = Blend2(w4, 3, w1, 1, 2);

                if (cond01)
                    dst[dst10] = Blend2(w4, 7, w1, 1, 3);
                else if (cond08)
                    dst[dst10] = w4;
                else if (cond02)
                    dst[dst10] = Blend2(w4, 3, w0, 1, 2);
                else if (cond09)
                    dst[dst10] = w4;
                else if (cond10)
                    dst[dst10] = Blend3(w4, 5, w3, 2, w1, 1, 3);
                else if (((kShuffled & 0x0b) == 0x02))
                    dst[dst10] = Blend3(w4, 5, w3, 2, w0, 1, 3);
                else if (cond15)
                    dst[dst10] = Blend2(w4, 5, w3, 3, 3);
                else if (cond03)
                    dst[dst10] = Blend2(w3, 3, w4, 1, 2);
                else if (cond13)
                    dst[dst10] = Blend3(w3, 2, w4, 1, w1, 1, 2);
                else if (cond12)
                    dst[dst10] = Blend2(w3, 5, w1, 3, 3);
                else if (cond06)
                    dst[dst10] = Blend2(w4, 7, w1, 1, 3);
                else if (((kShuffled & 0x0b) == 0x08) || ((kShuffled & 0xf9) == 0x68) || ((kShuffled & 0x6d) == 0x6c) || ((kShuffled & 0x3d) == 0x3c) ||
                         ((kShuffled & 0xf9) == 0xf8) || ((kShuffled & 0xdd) == 0xdc) || ((kShuffled & 0xdd) == 0x1c))
                    dst[dst10] = Blend2(w4, 3, w0, 1, 2);
                else if (cond14)
                    dst[dst10] = Blend2(w3, 1, w4, 1, 1);
                else
                    dst[dst10] = Blend2(w4, 3, w3, 1, 2);

                if ((((kShuffled & 0x7f) == 0x2b) || ((kShuffled & 0xef) == 0xab) || ((kShuffled & 0xbf) == 0x8f) || ((kShuffled & 0x7f) == 0x0f)) &&
                     ColorDifference(differences, p3, p1))
                    dst[dst11] = w4;
                else if (cond02)
                    dst[dst11] = Blend2(w4, 7, w0, 1, 3);
                else if (cond15)
                    dst[dst11] = Blend2(w4, 7, w3, 1, 3);
                else if (cond11)
                    dst[dst11] = Blend2(w4, 7, w1, 1, 3);
                else if (((kShuffled & 0x0a) == 0x00) || ((kShuffled & 0x7e) == 0x2a) || ((kShuffled & 0xef) == 0xab) || ((kShuffled & 0xbf) == 0x8f) ||
                         ((kShuffled & 0x7e) == 0x0e))
                    dst[dst11] = Blend3(w4, 6, w3, 1, w1, 1, 3);
                else if (cond07)
                    dst[dst11] = Blend2(w4, 7, w0, 1, 3);
                else
                    dst[dst11] = w4;

            }
        }
    }
}
