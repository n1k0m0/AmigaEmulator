// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Numerics;

namespace AmigaEmulator.Core.Cpu
{
    /// <summary>Motorola rounding directions; these values correspond to the two FPCR RND bits.</summary>
    public enum M68881RoundingMode
    {
        NearestEven = 0, TowardZero = 1, TowardNegative = 2, TowardPositive = 3
    }

    /// <summary>
    /// Pure binary-format rounding observations. These are not FPSR bits or trap decisions.
    /// Tiny is detected before rounding; PrecisionLost records discarded nonzero bits.
    /// Overflow can make the stored value inexact even when its mantissa needed no rounding.
    /// </summary>
    public readonly struct M68881BinaryRoundResult
    {
        internal M68881BinaryRoundResult(ulong bits, int width, bool precisionLost, bool tiny, bool overflow)
        {
            Bits = bits;
            Width = width;
            PrecisionLost = precisionLost;
            Tiny = tiny;
            Overflow = overflow;
        }

        public ulong Bits { get; }
        public int Width { get; }
        public bool PrecisionLost { get; }
        public bool Tiny { get; }
        public bool Overflow { get; }
        public bool Inexact => PrecisionLost || Overflow;
    }

    /// <summary>
    /// Integer-only extended-to-binary32/binary64 rounding, without guest CPU/FPSR mutation.
    /// Finite raw unnormals are interpreted as their exact dyadic value; infinity is transported.
    /// NaNs are rejected: a future instruction layer must handle payloads, SNAN and quieting.
    /// This rounds an already stored 64-bit significand, not a wider ALU intermediate result.
    /// Motorola MC68881/MC68882 User's Manual sections 3.5, 6.1.4, 6.1.5 and 6.1.7:
    /// https://www.nxp.com/docs/en/reference-manual/MC68881UM.pdf
    /// </summary>
    public static class M68881BinaryRounding
    {
        public static M68881BinaryRoundResult ToSingle(M68881Extended source, M68881RoundingMode mode = M68881RoundingMode.NearestEven)
            => Convert(source, mode, 23, 127, 32);

        public static M68881BinaryRoundResult ToDouble(M68881Extended source, M68881RoundingMode mode = M68881RoundingMode.NearestEven)
            => Convert(source, mode, 52, 1023, 64);

        private static M68881BinaryRoundResult Convert(M68881Extended source, M68881RoundingMode mode, int fractionBits, int bias, int width)
        {
            if ((uint)mode > 3) throw new ArgumentOutOfRangeException(nameof(mode));
            M68881ExtendedClass kind = source.Classification;
            if (kind == M68881ExtendedClass.QuietNaN || kind == M68881ExtendedClass.SignalingNaN)
                throw new ArgumentException("NaN conversion requires instruction-level payload and exception handling.", nameof(source));

            bool negative = source.IsNegative;
            ulong sign = negative ? 1UL << (width - 1) : 0;
            ulong infinity = (ulong)(2 * bias + 1) << fractionBits;
            if (kind == M68881ExtendedClass.Infinity)
                return new M68881BinaryRoundResult(sign | infinity, width, false, false, false);
            ulong significand = source.Significand;
            if (significand == 0)
                return new M68881BinaryRoundResult(sign, width, false, false, false);

            // In Motorola extended format, E=0 uses the same bias as E>0, with an explicit J bit.
            int sourcePower = source.BiasedExponent - M68881Extended.ExponentBias - 63;
            int exponent = sourcePower + 63 - BitOperations.LeadingZeroCount(significand);
            int minNormalExponent = 1 - bias;
            bool tiny = exponent < minNormalExponent;
            int quantum = Math.Max(exponent - fractionBits, minNormalExponent - fractionBits);
            ulong rounded = RoundInteger(significand, quantum - sourcePower, negative, mode, out bool precisionLost);

            if (tiny)
            {
                // The rounded fraction can reach the smallest normal; its exponent bit is already in place.
                return new M68881BinaryRoundResult(sign | rounded, width, precisionLost, true, false);
            }
            if (rounded == (1UL << (fractionBits + 1)))
            {
                rounded >>= 1;
                exponent++;
            }
            if (exponent > bias)
            {
                bool toInfinity = mode == M68881RoundingMode.NearestEven ||
                    (mode == M68881RoundingMode.TowardPositive && !negative) ||
                    (mode == M68881RoundingMode.TowardNegative && negative);
                ulong magnitude = toInfinity ? infinity : infinity - 1;
                return new M68881BinaryRoundResult(sign | magnitude, width, precisionLost, false, true);
            }
            ulong fraction = rounded & ((1UL << fractionBits) - 1);
            ulong result = sign | ((ulong)(exponent + bias) << fractionBits) | fraction;
            return new M68881BinaryRoundResult(result, width, precisionLost, false, false);
        }

        internal static ulong RoundInteger(ulong magnitude, int discardedBits, bool negative, M68881RoundingMode mode, out bool precisionLost)
        {
            if (discardedBits <= 0)
            {
                precisionLost = false;
                return magnitude << -discardedBits;
            }
            ulong whole, remainder;
            int halfComparison;
            if (discardedBits < 64)
            {
                whole = magnitude >> discardedBits;
                remainder = magnitude & ((1UL << discardedBits) - 1);
                halfComparison = remainder.CompareTo(1UL << (discardedBits - 1));
            }
            else
            {
                // C# masks shift counts modulo 64; handle total loss explicitly, including exact half at 64.
                whole = 0;
                remainder = magnitude;
                halfComparison = discardedBits == 64 ? magnitude.CompareTo(0x8000000000000000UL) : -1;
            }
            precisionLost = remainder != 0;
            if (!precisionLost) return whole;
            bool increment = mode switch
            {
                M68881RoundingMode.NearestEven => halfComparison > 0 || (halfComparison == 0 && (whole & 1) != 0),
                M68881RoundingMode.TowardNegative => negative,
                M68881RoundingMode.TowardPositive => !negative,
                _ => false
            };
            return whole + (increment ? 1UL : 0);
        }
    }
}
