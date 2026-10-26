// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Numerics;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Tests
{
    internal static class M68881OutputConversionTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("signed byte/word/long exact imports, endpoints and saturation", TestExact);
            yield return Test("integer nearest-even ties, sticky bits and directed fractions", TestFractions);
            yield return Test("signed range checks happen after rounding at both endpoints", TestRange);
            yield return Test("raw unnormals, tiny magnitudes, infinities and invalid modes", TestSpecials);
            yield return Test("independent signed BigInteger division oracle at integer boundaries", TestBoundaryOracle);
            yield return Test("independent signed rational oracle across seeded raw encodings", TestSeededOracle);
            yield return Test("external NaN output preserves sign, J and quiets without source mutation", TestNaNFixed);
            yield return Test("NaN payload truncation covers every bit and both signs and J bits", TestNaNBits);
            yield return Test("NaN single/double imported payloads round trip with explicit quieting", TestNaNRoundTrips);
            yield return Test("NaN-only conversion rejects every non-NaN operand class", TestNaNRejects);
        }
        private static ChipTestCase Test(string name, Action action) => new ChipTestCase("CPU/FPU output conversion", name,
            "Motorola MC68881/MC68882 User's Manual 3.2.5, FMOVE, 6.1.2/3/7; independent signed BigInteger rational oracle", action);
        private static readonly M68881RoundingMode[] Modes = { M68881RoundingMode.NearestEven,
            M68881RoundingMode.TowardZero, M68881RoundingMode.TowardNegative, M68881RoundingMode.TowardPositive };
        private static M68881IntegerResult Convert(M68881Extended v, int width, M68881RoundingMode mode)
            => width == 8 ? M68881IntegerConversion.ToByte(v, mode) : width == 16
                ? M68881IntegerConversion.ToWord(v, mode) : M68881IntegerConversion.ToLong(v, mode);
        private static M68881Extended Dyadic(long numerator, int fractionalBits = 0)
        {
            if (numerator == 0) return default;
            ulong magnitude = numerator < 0 ? (ulong)-numerator : (ulong)numerator;
            int top = 63 - BitOperations.LeadingZeroCount(magnitude);
            return new M68881Extended((ushort)((16383 + top - fractionalBits) | (numerator < 0 ? 0x8000 : 0)), magnitude << (63 - top));
        }
        private static void Expected(M68881Extended v, int width, M68881RoundingMode mode, int value, bool lost = false, bool outside = false)
        {
            var actual = Convert(v, width, mode);
            uint mask = width == 32 ? uint.MaxValue : (1U << width) - 1;
            Check.Equal(width, actual.Width, "Integer output width.");
            Check.Equal(unchecked((uint)value) & mask, actual.Bits, "Zero-extended two's-complement output bits.");
            Check.Equal(value, actual.SignedValue, "Signed interpretation.");
            Check.Equal(lost, actual.PrecisionLost, "Nonzero finite fractional part.");
            Check.Equal(outside, actual.OutOfRange, "Range checked after rounding, or infinity.");
        }
        private static void TestExact()
        {
            var random = new Random(0x811003);
            foreach (int width in new[] { 8, 16, 32 })
                foreach (var mode in Modes)
                {
                    long minimum = -(1L << (width - 1)), maximum = (1L << (width - 1)) - 1;
                    foreach (long n in new[] { minimum - 1, minimum, minimum + 1, -1L, 0, 1, maximum - 1, maximum, maximum + 1 })
                        Expected(Dyadic(n), width, mode, (int)Math.Clamp(n, minimum, maximum), outside: n < minimum || n > maximum);
                    Expected(new M68881Extended(0x8000, 0), width, mode, 0);
                }
            byte[] bits = new byte[4];
            for (int i = 0; i < 4096; i++)
            {
                random.NextBytes(bits); int n = BitConverter.ToInt32(bits);
                foreach (var mode in Modes) Expected(M68881Extended.FromSignedInteger(n), 32, mode, n);
            }
        }
        private static void TestFractions()
        {
            foreach (int width in new[] { 8, 16, 32 })
            {
                foreach (int sign in new[] { -1, 1 })
                {
                    Expected(Dyadic(sign * 5, 1), width, Modes[0], sign * 2, true);
                    Expected(Dyadic(sign * 7, 1), width, Modes[0], sign * 4, true);
                    var aboveHalf = new M68881Extended((ushort)(0x4000 | (sign < 0 ? 0x8000 : 0)), 0xA000000000000001);
                    Expected(aboveHalf, width, Modes[0], sign * 3, true);
                    foreach (var mode in Modes)
                    {
                        int result = mode == Modes[0] ? sign * 2 : mode == Modes[1] ? sign * 2
                            : mode == Modes[2] ? (sign < 0 ? -3 : 2) : (sign < 0 ? -2 : 3);
                        Expected(Dyadic(sign * 5, 1), width, mode, result, true);
                    }
                }
            }
        }
        private static void TestRange()
        {
            foreach (int width in new[] { 8, 16, 32 })
            {
                long max = (1L << (width - 1)) - 1, min = -(1L << (width - 1));
                Expected(Dyadic(max * 4 + 1, 2), width, Modes[0], (int)max, true);
                Expected(Dyadic(max * 2 + 1, 1), width, Modes[0], (int)max, true, true);
                Expected(Dyadic(min * 2 - 1, 1), width, Modes[0], (int)min, true);
                Expected(Dyadic(min * 4 - 3, 2), width, Modes[0], (int)min, true, true);
                Expected(Dyadic(max * 4 + 3, 2), width, Modes[1], (int)max, true);
                Expected(Dyadic(min * 4 - 3, 2), width, Modes[1], (int)min, true);
                Expected(Dyadic(max * 4 + 1, 2), width, Modes[3], (int)max, true, true);
                Expected(Dyadic(min * 4 - 1, 2), width, Modes[2], (int)min, true, true);
            }
        }
        private static void TestSpecials()
        {
            foreach (int width in new[] { 8, 16, 32 }) foreach (var mode in Modes)
                foreach (bool negative in new[] { false, true })
                {
                    ushort sign = negative ? (ushort)0x8000 : (ushort)0;
                    int end = negative ? (int)-(1L << (width - 1)) : (int)((1L << (width - 1)) - 1);
                    foreach (ulong mantissa in new[] { 0UL, 0x8000000000000000UL })
                        Expected(new M68881Extended((ushort)(sign | 0x7FFF), mantissa), width, mode, end, outside: true);
                    Expected(new M68881Extended((ushort)(sign | 0x7FFE), 1), width, mode, end, outside: true);
                    Expected(new M68881Extended((ushort)(sign | 0x4000), 0), width, mode, 0);
                    foreach (int exponent in new[] { 0, 16382, 16383, 16446 })
                        Oracle(new M68881Extended((ushort)(sign | exponent), 1), width, mode);
                    foreach (ulong mantissa in new[] { 1UL, 0x4000000000000000UL })
                    {
                        var nan = new M68881Extended((ushort)(sign | 0x7FFF), mantissa);
                        bool rejected = false; try { Convert(nan, width, mode); } catch (ArgumentException) { rejected = true; }
                        Check.True(rejected, "Numeric path must reject NaNs for explicit payload handling.");
                    }
                }
            foreach (var source in new[] { default(M68881Extended), Dyadic(1), new M68881Extended(0x7FFF, 1) })
                foreach (int width in new[] { 8, 16, 32 })
                {
                    bool rejected = false;
                    try { Convert(source, width, (M68881RoundingMode)4); } catch (ArgumentOutOfRangeException) { rejected = true; }
                    Check.True(rejected, "Invalid rounding direction rejected even before special input handling.");
                }
        }
        private static void Oracle(M68881Extended source, int width, M68881RoundingMode mode)
        {
            int power = source.BiasedExponent - 16383 - 63;
            BigInteger n = source.Significand, denominator = BigInteger.One;
            if (power >= 0) n <<= power; else denominator <<= -power;
            if (source.IsNegative) n = -n;
            BigInteger q = BigInteger.DivRem(n, denominator, out BigInteger remainder);
            bool lost = !remainder.IsZero;
            if (lost)
            {
                int comparison = (BigInteger.Abs(remainder) * 2).CompareTo(denominator);
                if (mode == Modes[0] && (comparison > 0 || (comparison == 0 && !q.IsEven))) q += n.Sign;
                if (mode == Modes[2] && n.Sign < 0) q--;
                if (mode == Modes[3] && n.Sign > 0) q++;
            }
            BigInteger min = -(BigInteger.One << (width - 1)), max = (BigInteger.One << (width - 1)) - 1;
            bool outside = q < min || q > max;
            if (q < min) q = min; if (q > max) q = max;
            Expected(source, width, mode, (int)q, lost, outside);
        }
        private static void TestBoundaryOracle()
        {
            foreach (int width in new[] { 8, 16, 32 })
                foreach (long center in new[] { -(1L << (width - 1)), -1L, 0, 1, (1L << (width - 1)) - 1 })
                    for (int offset = -9; offset <= 9; offset++)
                        foreach (var mode in Modes) Oracle(Dyadic(center * 8 + offset, 3), width, mode);
        }
        private static void TestSeededOracle()
        {
            var random = new Random(0x68881); byte[] bits = new byte[8];
            for (int i = 0; i < 4096; i++)
            {
                random.NextBytes(bits); ulong mantissa = BitConverter.ToUInt64(bits);
                int exponent = (i % 3) == 0 ? random.Next(0x7FFF) : 16383 + random.Next(-66, 68);
                var source = new M68881Extended((ushort)(exponent | ((i & 1) << 15)), mantissa);
                foreach (int width in new[] { 8, 16, 32 }) foreach (var mode in Modes) Oracle(source, width, mode);
            }
        }
        private static void NaNOracle(M68881Extended source)
        {
            BigInteger quiet = new BigInteger(source.Significand) | (BigInteger.One << 62);
            bool signaling = (source.Significand & 0x4000000000000000UL) == 0;
            var q = M68881NaNOutput.Quiet(source);
            Check.Equal(source.SignExponent, q.SignExponent, "Quieting preserves raw sign/exponent.");
            Check.Equal((ulong)quiet, q.Significand, "Only quiet bit can change, including J preservation.");
            var outputs = new[] { M68881NaNOutput.ToByte(source), M68881NaNOutput.ToWord(source),
                M68881NaNOutput.ToLong(source), M68881NaNOutput.ToSingle(source), M68881NaNOutput.ToDouble(source) };
            for (int i = 0; i < outputs.Length; i++)
            {
                var result = outputs[i]; int width = new[] { 8, 16, 32, 32, 64 }[i];
                BigInteger expected;
                if (i < 3) expected = quiet / (BigInteger.One << (64 - width));
                else
                {
                    int fractionBits = i == 3 ? 23 : 52, exponentBits = width - fractionBits - 1;
                    BigInteger fraction = quiet & ((BigInteger.One << 63) - 1);
                    expected = fraction / (BigInteger.One << (63 - fractionBits));
                    expected |= ((BigInteger.One << exponentBits) - 1) << fractionBits;
                    if (source.IsNegative) expected |= BigInteger.One << (width - 1);
                }
                Check.Equal(width, result.Width, "NaN destination width.");
                Check.Equal((ulong)expected, result.Bits, "Independent NaN payload projection.");
                Check.Equal(signaling, result.WasSignaling, "Original signaling observation before quieting.");
            }
        }
        private static void TestNaNFixed()
        {
            var s = new M68881Extended(0xFFFF, 0x8123456789ABCDEF);
            Check.Equal(0xFFC12345UL, M68881NaNOutput.ToSingle(s).Bits, "Fixed negative single payload.");
            Check.Equal(0xC1UL, M68881NaNOutput.ToByte(s).Bits, "Integer output includes J, independently of sign.");
            Check.Equal(0x8123456789ABCDEFUL, s.Significand, "Quiet output cannot mutate source.");
            foreach (ulong m in new[] { 1UL, 0x4000000000000000UL, 0x8000000000000001UL, ulong.MaxValue })
                foreach (ushort e in new ushort[] { 0x7FFF, 0xFFFF }) NaNOracle(new M68881Extended(e, m));
        }
        private static void TestNaNBits()
        {
            for (int bit = 0; bit < 63; bit++) foreach (ulong j in new[] { 0UL, 0x8000000000000000UL })
                foreach (ushort sign in new ushort[] { 0, 0x8000 })
                    NaNOracle(new M68881Extended((ushort)(0x7FFF | sign), j | (1UL << bit)));
        }
        private static void TestNaNRoundTrips()
        {
            var random = new Random(0x8810A); byte[] bytes = new byte[8];
            for (int i = 0; i < 4096; i++)
            {
                random.NextBytes(bytes);
                uint single = BitConverter.ToUInt32(bytes) | 0x7F800001;
                ulong binary64 = BitConverter.ToUInt64(bytes) | 0x7FF0000000000001;
                var s = M68881Extended.FromSingleBits(single); var d = M68881Extended.FromDoubleBits(binary64);
                Check.Equal((ulong)(single | 0x00400000), M68881NaNOutput.ToSingle(s).Bits, "Imported single payload quiet output.");
                Check.Equal(binary64 | 0x0008000000000000, M68881NaNOutput.ToDouble(d).Bits, "Imported double payload quiet output.");
                NaNOracle(s); NaNOracle(d);
            }
        }
        private static void TestNaNRejects()
        {
            foreach (var source in new[] { default(M68881Extended), Dyadic(1), new M68881Extended(0, 1),
                new M68881Extended(0x4000, 0), new M68881Extended(0x7FFF, 0), new M68881Extended(0xFFFF, 0x8000000000000000) })
            {
                Action[] calls = { () => M68881NaNOutput.Quiet(source), () => M68881NaNOutput.ToByte(source),
                    () => M68881NaNOutput.ToWord(source), () => M68881NaNOutput.ToLong(source),
                    () => M68881NaNOutput.ToSingle(source), () => M68881NaNOutput.ToDouble(source) };
                foreach (var call in calls)
                {
                    bool rejected = false; try { call(); } catch (ArgumentException) { rejected = true; }
                    Check.True(rejected, "NaN-only path cannot silently process a finite or infinite operand.");
                }
            }
        }
    }
}
