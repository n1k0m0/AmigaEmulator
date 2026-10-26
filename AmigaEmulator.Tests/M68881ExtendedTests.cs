// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Numerics;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Tests
{
    internal static class M68881ExtendedTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("raw classes retain Motorola zero-exponent normals and unnormals", TestClasses);
            yield return Test("96-bit memory layout ignores input padding and clears output padding", TestMemory);
            yield return Test("short operand buffers reject atomically at every byte boundary", TestShortBuffers);
            yield return Test("signed integer boundaries and exact integer identities", TestIntegers);
            yield return Test("binary32 fixed extrema, subnormals and signed zeros", TestSingleVectors);
            yield return Test("binary64 fixed extrema, subnormals and signed zeros", TestDoubleVectors);
            yield return Test("binary32 exponent sweep and deterministic dyadic oracle", TestSingleOracle);
            yield return Test("binary64 exponent sweep and deterministic dyadic oracle", TestDoubleOracle);
            yield return Test("NaN sign, payload and signaling survive raw format transport", TestNaNs);
        }

        private static ChipTestCase Test(string name, Action action) => new ChipTestCase("CPU/FPU operand format", name,
            "Motorola MC68881/MC68882 User's Manual section 3, tables 3-1 to 3-3; independent BigInteger dyadic identities", action);

        private static void Encoding(M68881Extended value, ushort exponent, ulong significand)
        {
            Check.Equal(exponent, value.SignExponent, "Raw sign/exponent.");
            Check.Equal(significand, value.Significand, "Raw significand.");
        }

        private static void TestClasses()
        {
            var cases = new (ushort exponent, ulong significand, M68881ExtendedClass kind)[] {
                (0, 0, M68881ExtendedClass.Zero),
                (0, 1, M68881ExtendedClass.Denormal),
                (0, 0x7FFFFFFFFFFFFFFF, M68881ExtendedClass.Denormal),
                (0, 0x8000000000000000, M68881ExtendedClass.Normal),
                (0x3FFF, 0x8000000000000000, M68881ExtendedClass.Normal),
                (0x7FFE, ulong.MaxValue, M68881ExtendedClass.Normal),
                (1, 0, M68881ExtendedClass.Unnormal),
                (0x3FFF, 1, M68881ExtendedClass.Unnormal),
                (0x7FFE, 0x7FFFFFFFFFFFFFFF, M68881ExtendedClass.Unnormal),
                (0x7FFF, 0, M68881ExtendedClass.Infinity),
                (0x7FFF, 0x8000000000000000, M68881ExtendedClass.Infinity),
                (0x7FFF, 1, M68881ExtendedClass.SignalingNaN),
                (0x7FFF, 0x8000000000000001, M68881ExtendedClass.SignalingNaN),
                (0x7FFF, 0x4000000000000000, M68881ExtendedClass.QuietNaN),
                (0x7FFF, 0xC000000000000000, M68881ExtendedClass.QuietNaN)
            };
            foreach (var item in cases)
                foreach (ushort sign in new ushort[] { 0, 0x8000 })
                {
                    var value = new M68881Extended((ushort)(item.exponent | sign), item.significand);
                    Check.Equal(item.kind, value.Classification, "Raw class including noncanonical J bit.");
                    Check.Equal(sign != 0, value.IsNegative, "Raw sign.");
                    Encoding(value, (ushort)(item.exponent | sign), item.significand);
                }
            var positive = new M68881Extended(0, 0);
            var negative = new M68881Extended(0x8000, 0);
            Check.True(positive != negative && !positive.Equals((object)negative), "Bitwise equality distinguishes zero signs.");
            Check.True(positive == default && positive.Equals((object)default(M68881Extended)), "Default is positive zero.");
        }

        private static void TestMemory()
        {
            byte[] raw = { 0xBF, 0xFF, 0xAA, 0x55, 0x81, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
            var value = M68881Extended.ReadMemory(raw);
            Encoding(value, 0xBFFF, 0x8123456789ABCDEF);
            byte[] target = new byte[16];
            Array.Fill(target, (byte)0xA5);
            value.WriteMemory(target.AsSpan(2, 12));
            Check.Equal("A5A5BFFF00008123456789ABCDEFA5A5", Convert.ToHexString(target), "Big-endian fields and bounded writes.");
            var random = new Random(0x68881);
            for (int i = 0; i < 10000; i++)
            {
                random.NextBytes(raw);
                value = M68881Extended.ReadMemory(raw);
                value.WriteMemory(target);
                Check.Equal(value, M68881Extended.ReadMemory(target), "All 80-bit encodings survive raw transport.");
                Check.Equal((byte)0, target[2], "Reserved high byte is zero.");
                Check.Equal((byte)0, target[3], "Reserved low byte is zero.");
                for (int b = 0; b < 12; b++)
                    if (b != 2 && b != 3) Check.Equal(raw[b], target[b], "Only reserved word may change.");
            }
        }

        private static void TestShortBuffers()
        {
            byte[] target = new byte[14];
            var value = new M68881Extended(0x7FFF, 1);
            for (int length = 0; length < 12; length++)
            {
                Array.Fill(target, (byte)0xA5);
                bool rejected = false;
                try { value.WriteMemory(target.AsSpan(1, length)); } catch (ArgumentException) { rejected = true; }
                Check.True(rejected, "Every truncated output is rejected.");
                foreach (byte b in target) Check.Equal((byte)0xA5, b, "Rejected output is unchanged, including guards.");
                rejected = false;
                try { M68881Extended.ReadMemory(target.AsSpan(1, length)); } catch (ArgumentException) { rejected = true; }
                Check.True(rejected, "Every truncated input is rejected.");
            }
            bool emptyRejected = false;
            try { value.WriteMemory(default); } catch (ArgumentException) { emptyRejected = true; }
            Check.True(emptyRejected, "Default span rejects without mutation.");
        }

        private static void TestIntegers()
        {
            Encoding(M68881Extended.FromSignedInteger(int.MinValue), 0xC01E, 0x8000000000000000);
            Encoding(M68881Extended.FromSignedInteger(int.MaxValue), 0x401D, 0xFFFFFFFE00000000);
            Encoding(M68881Extended.FromSignedInteger(-1), 0xBFFF, 0x8000000000000000);
            Encoding(M68881Extended.FromSignedInteger(0), 0, 0);
            Encoding(M68881Extended.FromSignedInteger(1), 0x3FFF, 0x8000000000000000);
            for (int i = sbyte.MinValue; i <= sbyte.MaxValue; i++)
                Dyadic(M68881Extended.FromSignedInteger(i), i < 0, BigInteger.Abs(i), 0);
            foreach (int edge in new[] { short.MinValue, short.MaxValue, int.MinValue, int.MaxValue })
                Dyadic(M68881Extended.FromSignedInteger(edge), edge < 0, BigInteger.Abs(edge), 0);
            var random = new Random(0x68882);
            for (int i = 0; i < 4096; i++)
            {
                int value = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
                Dyadic(M68881Extended.FromSignedInteger(value), value < 0, BigInteger.Abs(value), 0);
            }
        }

        private static void TestSingleVectors()
        {
            var cases = new (uint bits, ushort exponent, ulong significand)[] {
                (0, 0, 0), (0x80000000, 0x8000, 0),
                (0x3F800000, 0x3FFF, 0x8000000000000000),
                (0xBF800000, 0xBFFF, 0x8000000000000000),
                (0x3F000000, 0x3FFE, 0x8000000000000000),
                (0x3FC00000, 0x3FFF, 0xC000000000000000),
                (0x3F800001, 0x3FFF, 0x8000010000000000),
                (1, 0x3F6A, 0x8000000000000000),
                (0x007FFFFF, 0x3F80, 0xFFFFFE0000000000),
                (0x00800000, 0x3F81, 0x8000000000000000),
                (0x7F7FFFFF, 0x407E, 0xFFFFFF0000000000),
                (0xFF7FFFFF, 0xC07E, 0xFFFFFF0000000000),
                (0x7F800000, 0x7FFF, 0x8000000000000000),
                (0xFF800000, 0xFFFF, 0x8000000000000000)
            };
            foreach (var item in cases) Encoding(M68881Extended.FromSingleBits(item.bits), item.exponent, item.significand);
        }

        private static void TestDoubleVectors()
        {
            var cases = new (ulong bits, ushort exponent, ulong significand)[] {
                (0, 0, 0), (0x8000000000000000, 0x8000, 0),
                (0x3FF0000000000000, 0x3FFF, 0x8000000000000000),
                (0xBFF0000000000000, 0xBFFF, 0x8000000000000000),
                (0x3FE0000000000000, 0x3FFE, 0x8000000000000000),
                (0x3FF8000000000000, 0x3FFF, 0xC000000000000000),
                (0x3FF0000000000001, 0x3FFF, 0x8000000000000800),
                (1, 0x3BCD, 0x8000000000000000),
                (0x000FFFFFFFFFFFFF, 0x3C00, 0xFFFFFFFFFFFFF000),
                (0x0010000000000000, 0x3C01, 0x8000000000000000),
                (0x7FEFFFFFFFFFFFFF, 0x43FE, 0xFFFFFFFFFFFFF800),
                (0xFFEFFFFFFFFFFFFF, 0xC3FE, 0xFFFFFFFFFFFFF800),
                (0x7FF0000000000000, 0x7FFF, 0x8000000000000000),
                (0xFFF0000000000000, 0xFFFF, 0x8000000000000000)
            };
            foreach (var item in cases) Encoding(M68881Extended.FromDoubleBits(item.bits), item.exponent, item.significand);
        }

        // Compare exact integer-times-power-of-two values instead of reusing the converter's normalization.
        // BigInteger absorbs the entire exponent difference, so no host float/double rounding can hide lost bits.
        private static void Dyadic(M68881Extended actual, bool negative, BigInteger magnitude, int power)
        {
            Check.Equal(negative, actual.IsNegative, "Sign survives exact import.");
            if (magnitude.IsZero)
            {
                Check.Equal(M68881ExtendedClass.Zero, actual.Classification, "Exact signed zero.");
                return;
            }
            Check.Equal(M68881ExtendedClass.Normal, actual.Classification, "Finite source imports to a normal extended value.");
            int actualPower = actual.BiasedExponent - 16383 - 63;
            int commonPower = Math.Min(actualPower, power);
            Check.Equal(magnitude << (power - commonPower),
                new BigInteger(actual.Significand) << (actualPower - commonPower), "Exact rational identity, all source bits retained.");
        }

        private static void SingleOracle(uint bits)
        {
            int e = (int)((bits >> 23) & 255);
            uint f = bits & 0x7FFFFF;
            if (e == 255) return;
            Dyadic(M68881Extended.FromSingleBits(bits), bits > int.MaxValue,
                e == 0 ? f : (BigInteger.One << 23) + f, e == 0 ? -149 : e - 150);
        }

        private static void TestSingleOracle()
        {
            foreach (uint sign in new uint[] { 0, 0x80000000 })
                for (uint e = 0; e < 255; e++)
                    foreach (uint f in new uint[] { 0, 1, 0x400000, 0x7FFFFF }) SingleOracle(sign | (e << 23) | f);
            var random = new Random(32);
            for (int i = 0; i < 8192; i++) SingleOracle((uint)random.NextInt64(0, 1L << 32));
        }

        private static void DoubleOracle(ulong bits)
        {
            int e = (int)((bits >> 52) & 2047);
            ulong f = bits & 0xFFFFFFFFFFFFF;
            if (e == 2047) return;
            Dyadic(M68881Extended.FromDoubleBits(bits), (bits >> 63) != 0,
                e == 0 ? f : (BigInteger.One << 52) + f, e == 0 ? -1074 : e - 1075);
        }

        private static void TestDoubleOracle()
        {
            foreach (ulong sign in new ulong[] { 0, 0x8000000000000000 })
                for (ulong e = 0; e < 2047; e++)
                    foreach (ulong f in new ulong[] { 0, 1, 0x8000000000000, 0xFFFFFFFFFFFFF }) DoubleOracle(sign | (e << 52) | f);
            var random = new Random(64);
            byte[] raw = new byte[8];
            for (int i = 0; i < 8192; i++) { random.NextBytes(raw); DoubleOracle(BitConverter.ToUInt64(raw)); }
        }

        private static void TestNaNs()
        {
            foreach (uint sign in new uint[] { 0, 0x80000000 })
                for (int bit = 0; bit < 23; bit++)
                {
                    uint fraction = 1U << bit;
                    var value = M68881Extended.FromSingleBits(sign | 0x7F800000 | fraction);
                    Encoding(value, (ushort)(sign == 0 ? 0x7FFF : 0xFFFF), 0x8000000000000000 | ((ulong)fraction << 40));
                    Check.Equal(bit == 22 ? M68881ExtendedClass.QuietNaN : M68881ExtendedClass.SignalingNaN,
                        value.Classification, "Single NaN quiet bit and payload positions.");
                }
            foreach (ulong sign in new ulong[] { 0, 0x8000000000000000 })
                for (int bit = 0; bit < 52; bit++)
                {
                    ulong fraction = 1UL << bit;
                    var value = M68881Extended.FromDoubleBits(sign | 0x7FF0000000000000 | fraction);
                    Encoding(value, (ushort)(sign == 0 ? 0x7FFF : 0xFFFF), 0x8000000000000000 | (fraction << 11));
                    Check.Equal(bit == 51 ? M68881ExtendedClass.QuietNaN : M68881ExtendedClass.SignalingNaN,
                        value.Classification, "Double NaN quiet bit and payload positions.");
                    byte[] memory = new byte[12];
                    value.WriteMemory(memory);
                    Check.Equal(value, M68881Extended.ReadMemory(memory), "Transport does not quiet an SNAN.");
                }
            Encoding(M68881Extended.FromSingleBits(0xFFFFFFFF), 0xFFFF, 0xFFFFFF0000000000);
            Encoding(M68881Extended.FromDoubleBits(ulong.MaxValue), 0xFFFF, 0xFFFFFFFFFFFFF800);
        }
    }
}
