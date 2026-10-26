// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Numerics;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Tests
{
    internal static class M68881BinaryRoundingTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("exact finite single/double round trips in all four directions", TestRoundTrips);
            yield return Test("nearest-even ties, sticky bits and significand carry", TestMidpoints);
            yield return Test("signed gradual underflow and tininess before rounding", TestUnderflow);
            yield return Test("rounded overflow distinguishes range loss from precision loss", TestOverflow);
            yield return Test("finite raw unnormals normalize numerically and special inputs are guarded", TestRawInputs);
            yield return Test("signed integer-to-single rounding preserves directed bounds", TestIntegerBounds);
            yield return Test("independent ordered-neighbor oracle covers boundary exponents and 63/64/65-bit losses", TestBoundaryOracle);
            yield return Test("independent exact BigInteger neighbor search checks seeded raw operands", TestRandomOracle);
        }

        private static ChipTestCase Test(string name, Action action) => new ChipTestCase("CPU/FPU binary rounding", name,
            "Motorola MC68881/MC68882 User's Manual 3.5, 6.1.4/5/7; independent exact BigInteger search over ordered output encodings", action);
        private static readonly M68881RoundingMode[] Modes = { M68881RoundingMode.NearestEven,
            M68881RoundingMode.TowardZero, M68881RoundingMode.TowardNegative, M68881RoundingMode.TowardPositive };

        private static M68881BinaryRoundResult Round(M68881Extended source, int width, M68881RoundingMode mode)
        {
            var result = width == 32 ? M68881BinaryRounding.ToSingle(source, mode) : M68881BinaryRounding.ToDouble(source, mode);
            Check.Equal(width, result.Width, "Output format width.");
            return result;
        }

        private static M68881Extended Value(int exponent, ulong significand, bool negative = false)
            => new M68881Extended((ushort)((exponent + 16383) | (negative ? 0x8000 : 0)), significand);

        private static void CheckResult(M68881BinaryRoundResult actual, ulong bits, bool lost = false, bool tiny = false, bool overflow = false)
        {
            Check.Equal(bits, actual.Bits, "Rounded output bits.");
            Check.Equal(lost, actual.PrecisionLost, "Discarded nonzero precision bits.");
            Check.Equal(tiny, actual.Tiny, "Tininess is detected before rounding.");
            Check.Equal(overflow, actual.Overflow, "Range overflow after significand rounding.");
            Check.Equal(lost || overflow, actual.Inexact, "Stored value inexactness includes overflow.");
        }

        private static void TestRoundTrips()
        {
            var random = new Random(0x881882);
            byte[] raw = new byte[8];
            for (int i = 0; i < 4096; i++)
            {
                random.NextBytes(raw);
                uint single = BitConverter.ToUInt32(raw);
                ulong binary64 = BitConverter.ToUInt64(raw);
                foreach (var mode in Modes)
                {
                    if ((single & 0x7F800000) != 0x7F800000)
                        CheckResult(Round(M68881Extended.FromSingleBits(single), 32, mode), single,
                            tiny: (single & 0x7F800000) == 0 && (single & 0x7FFFFF) != 0);
                    if ((binary64 & 0x7FF0000000000000) != 0x7FF0000000000000)
                        CheckResult(Round(M68881Extended.FromDoubleBits(binary64), 64, mode), binary64,
                            tiny: (binary64 & 0x7FF0000000000000) == 0 && (binary64 & 0xFFFFFFFFFFFFF) != 0);
                }
            }
            foreach (var mode in Modes)
            {
                CheckResult(M68881BinaryRounding.ToSingle(default, mode), 0);
                CheckResult(M68881BinaryRounding.ToDouble(new M68881Extended(0x8000, 0), mode), 0x8000000000000000);
                CheckResult(M68881BinaryRounding.ToSingle(new M68881Extended(0x7FFF, 0), mode), 0x7F800000);
                CheckResult(M68881BinaryRounding.ToDouble(new M68881Extended(0xFFFF, 0x8000000000000000), mode), 0xFFF0000000000000);
            }
        }

        private static void TestMidpoints()
        {
            foreach (bool negative in new[] { false, true })
            {
                uint s = negative ? 0x80000000U : 0;
                ulong d = negative ? 0x8000000000000000UL : 0;
                CheckResult(Round(Value(0, 0x8000008000000000, negative), 32, Modes[0]), s | 0x3F800000, true);
                CheckResult(Round(Value(0, 0x8000008000000001, negative), 32, Modes[0]), s | 0x3F800001, true);
                CheckResult(Round(Value(0, 0x8000007FFFFFFFFF, negative), 32, Modes[0]), s | 0x3F800000, true);
                CheckResult(Round(Value(0, 0x8000018000000000, negative), 32, Modes[0]), s | 0x3F800002, true);
                CheckResult(Round(Value(0, 0xFFFFFF8000000000, negative), 32, Modes[0]), s | 0x40000000, true);
                CheckResult(Round(Value(0, 0x8000000000000400, negative), 64, Modes[0]), d | 0x3FF0000000000000, true);
                CheckResult(Round(Value(0, 0x8000000000000401, negative), 64, Modes[0]), d | 0x3FF0000000000001, true);
                CheckResult(Round(Value(0, 0x8000000000000C00, negative), 64, Modes[0]), d | 0x3FF0000000000002, true);
                CheckResult(Round(Value(0, 0xFFFFFFFFFFFFFC00, negative), 64, Modes[0]), d | 0x4000000000000000, true);
            }
        }

        private static void TestUnderflow()
        {
            foreach (int width in new[] { 32, 64 })
            {
                int minSub = width == 32 ? -149 : -1074;
                ulong normal = width == 32 ? 0x00800000UL : 0x0010000000000000UL;
                int boundaryExponent = width == 32 ? -127 : -1023;
                ulong boundaryMidpoint = width == 32 ? 0xFFFFFF0000000000UL : 0xFFFFFFFFFFFFF800UL;
                foreach (bool negative in new[] { false, true })
                {
                    ulong sign = negative ? 1UL << (width - 1) : 0;
                    foreach (var mode in Modes)
                    {
                        CheckResult(Round(Value(minSub, 0x8000000000000000, negative), width, mode), sign | 1, tiny: true);
                        bool away = mode == M68881RoundingMode.TowardNegative && negative || mode == M68881RoundingMode.TowardPositive && !negative;
                        CheckResult(Round(Value(minSub - 1, 0x8000000000000000, negative), width, mode), sign | (away ? 1UL : 0), true, true);
                        CheckResult(Round(Value(minSub - 2, ulong.MaxValue, negative), width, mode), sign | (away ? 1UL : 0), true, true);
                    }
                    CheckResult(Round(Value(minSub - 1, 0x8000000000000001, negative), width, Modes[0]), sign | 1, true, true);
                    CheckResult(Round(Value(boundaryExponent, boundaryMidpoint, negative), width, Modes[0]), sign | normal, true, true);
                    CheckResult(Round(Value(boundaryExponent, boundaryMidpoint - 1, negative), width, Modes[0]), sign | (normal - 1), true, true);
                }
            }
        }

        private static void TestOverflow()
        {
            foreach (int width in new[] { 32, 64 })
            {
                int maxExp = width == 32 ? 127 : 1023;
                ulong infinity = width == 32 ? 0x7F800000UL : 0x7FF0000000000000UL;
                ulong maxMantissa = width == 32 ? 0xFFFFFF0000000000UL : 0xFFFFFFFFFFFFF800UL;
                ulong halfUlp = width == 32 ? 0x8000000000UL : 0x400UL;
                foreach (bool negative in new[] { false, true })
                {
                    ulong sign = negative ? 1UL << (width - 1) : 0;
                    foreach (var mode in Modes)
                    {
                        bool toInfinity = mode == Modes[0] || mode == M68881RoundingMode.TowardNegative && negative || mode == M68881RoundingMode.TowardPositive && !negative;
                        CheckResult(Round(Value(maxExp + 1, 0x8000000000000000, negative), width, mode), sign | (toInfinity ? infinity : infinity - 1), overflow: true);
                    }
                    CheckResult(Round(Value(maxExp, maxMantissa, negative), width, Modes[0]), sign | (infinity - 1));
                    CheckResult(Round(Value(maxExp, maxMantissa + halfUlp - 1, negative), width, Modes[0]), sign | (infinity - 1), true);
                    CheckResult(Round(Value(maxExp, maxMantissa + halfUlp, negative), width, Modes[0]), sign | infinity, true, overflow: true);
                    CheckResult(Round(Value(maxExp, maxMantissa + 1, negative), width, Modes[1]), sign | (infinity - 1), true);
                }
            }
        }

        private static void TestRawInputs()
        {
            foreach (int width in new[] { 32, 64 })
                foreach (var mode in Modes)
                {
                    CheckResult(Round(new M68881Extended(0xBFFF, 0), width, mode), 1UL << (width - 1));
                    Oracle(new M68881Extended(0x4000, 0x4000000000000000), width, mode);
                    Oracle(new M68881Extended(0, 0x8000000000000000), width, mode);
                    Oracle(new M68881Extended(0, 1), width, mode);
                    foreach (ulong payload in new[] { 1UL, 0x8000000000000001UL, 0x4000000000000000UL })
                    {
                        var source = new M68881Extended(0xFFFF, payload);
                        bool rejected = false;
                        try { Round(source, width, mode); } catch (ArgumentException) { rejected = true; }
                        Check.True(rejected, "NaN requires explicit instruction-level processing.");
                        Check.Equal(payload, source.Significand, "Rejecting NaN cannot quiet or mutate the raw operand.");
                    }
                }
            foreach (var invalid in new[] { (M68881RoundingMode)(-1), (M68881RoundingMode)4, (M68881RoundingMode)256 })
            {
                bool rejected = false;
                try { M68881BinaryRounding.ToSingle(default, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check.True(rejected, "Invalid rounding mode is rejected even for zero.");
            }
        }

        private static void TestIntegerBounds()
        {
            CheckResult(M68881BinaryRounding.ToSingle(M68881Extended.FromSignedInteger(int.MaxValue)), 0x4F000000, true);
            CheckResult(M68881BinaryRounding.ToSingle(M68881Extended.FromSignedInteger(int.MaxValue), Modes[1]), 0x4EFFFFFF, true);
            CheckResult(M68881BinaryRounding.ToSingle(M68881Extended.FromSignedInteger(-int.MaxValue), Modes[2]), 0xCF000000, true);
            CheckResult(M68881BinaryRounding.ToSingle(M68881Extended.FromSignedInteger(-int.MaxValue), Modes[3]), 0xCEFFFFFF, true);
            CheckResult(M68881BinaryRounding.ToSingle(M68881Extended.FromSignedInteger(int.MinValue)), 0xCF000000);
        }

        // Independent oracle: binary-search the monotone positive output encodings,
        // compare exact BigInteger distances to both neighbors and apply the direction.
        // This does not reuse production guard/sticky shifts or its rounded significand.
        private static void Oracle(M68881Extended source, int width, M68881RoundingMode mode)
        {
            int fractionBits = width == 32 ? 23 : 52, bias = width == 32 ? 127 : 1023;
            int minSubPower = 1 - bias - fractionBits;
            int sourcePower = source.BiasedExponent - 16383 - 63;
            int commonPower = Math.Min(minSubPower, sourcePower);
            BigInteger exact = new BigInteger(source.Significand) << (sourcePower - commonPower);
            ulong infinity = (ulong)(2 * bias + 1) << fractionBits, maximum = infinity - 1;
            BigInteger At(ulong bits)
            {
                int exponent = (int)(bits >> fractionBits);
                BigInteger significand = bits & ((1UL << fractionBits) - 1);
                int power = minSubPower;
                if (exponent != 0) { significand += BigInteger.One << fractionBits; power = exponent - bias - fractionBits; }
                return significand << (power - commonPower);
            }
            ulong lower = 0, upper = maximum;
            while (lower < upper)
            {
                ulong middle = lower + ((upper - lower + 1) >> 1);
                if (At(middle) <= exact) lower = middle; else upper = middle - 1;
            }
            BigInteger floor = At(lower);
            BigInteger firstOutOfRange = BigInteger.One << (bias + 1 - commonPower);
            bool ceil = false;
            if (exact != floor)
            {
                BigInteger next = lower == maximum ? firstOutOfRange : At(lower + 1);
                if (mode == Modes[0])
                {
                    int distance = (exact - floor).CompareTo(next - exact);
                    ceil = distance > 0 || distance == 0 && (lower & 1) != 0;
                }
                else ceil = mode == Modes[2] && source.IsNegative || mode == Modes[3] && !source.IsNegative;
            }
            bool overflow = exact >= firstOutOfRange || ceil && lower == maximum;
            ulong magnitude = lower + (ceil ? 1UL : 0);
            if (overflow)
            {
                bool inf = mode == Modes[0] || mode == Modes[2] && source.IsNegative || mode == Modes[3] && !source.IsNegative;
                magnitude = inf ? infinity : maximum;
            }
            bool tiny = exact > 0 && exact < At(1UL << fractionBits);
            int latticePower = exact.IsZero ? minSubPower : Math.Max(sourcePower + (int)new BigInteger(source.Significand).GetBitLength() - 1 - fractionBits, minSubPower);
            bool lost = !exact.IsZero && exact % (BigInteger.One << (latticePower - commonPower)) != 0;
            ulong sign = source.IsNegative ? 1UL << (width - 1) : 0;
            CheckResult(Round(source, width, mode), sign | magnitude, lost, tiny, overflow);
        }

        private static void TestBoundaryOracle()
        {
            foreach (int width in new[] { 32, 64 })
            {
                int minSub = width == 32 ? -149 : -1074, minNorm = width == 32 ? -126 : -1022, max = width == 32 ? 127 : 1023;
                foreach (int e in new[] { -16383, minSub - 2, minSub - 1, minSub, minSub + 1, minNorm - 1, minNorm, minNorm + 1, -1, 0, 1, max - 1, max, max + 1, 16383 })
                    foreach (ulong m in new[] { 1UL, 0x7FFFFFFFFFFFFFFFUL, 0x8000000000000000UL, 0x8000000000000001UL, 0xFFFFFFFFFFFFFC00UL, ulong.MaxValue })
                        foreach (bool negative in new[] { false, true })
                            foreach (var mode in Modes) Oracle(Value(e, m, negative), width, mode);
            }
        }

        private static void TestRandomOracle()
        {
            var random = new Random(0x82F00);
            byte[] raw = new byte[8];
            foreach (int width in new[] { 32, 64 })
                for (int i = 0; i < 512; i++)
                {
                    random.NextBytes(raw);
                    int range = width == 32 ? 220 : 1200;
                    var source = Value(random.Next(-range, range), BitConverter.ToUInt64(raw), random.Next(2) != 0);
                    foreach (var mode in Modes) Oracle(source, width, mode);
                }
        }
    }
}
