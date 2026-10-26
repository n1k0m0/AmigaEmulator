// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;

namespace AmigaEmulator.Core.Cpu
{
    /// <summary>Pure signed-integer output and observations, without guest FPSR or trap mutation.</summary>
    public readonly struct M68881IntegerResult
    {
        internal M68881IntegerResult(uint bits, int width, bool precisionLost, bool outOfRange)
        {
            Bits = bits; Width = width; PrecisionLost = precisionLost; OutOfRange = outOfRange;
        }
        /// <summary>Zero-extended destination bytes, in two's-complement encoding.</summary>
        public uint Bits { get; }
        public int Width { get; }
        public int SignedValue => unchecked((int)(Bits << (32 - Width))) >> (32 - Width);
        /// <summary>The finite source had a nonzero fractional part before integer rounding.</summary>
        public bool PrecisionLost { get; }
        /// <summary>The rounded integer is outside the signed destination range, or the source is infinity.</summary>
        public bool OutOfRange { get; }
    }

    /// <summary>
    /// Integer-only extended-to-signed-byte/word/long output. Range is checked after rounding;
    /// overflow and infinity saturate to the signed endpoint as FMOVE external results do.
    /// Raw finite unnormals are interpreted numerically. NaNs require the separate payload path.
    /// Observations are not FPSR bits: exception priority, enabled traps and bus writes belong
    /// to a future instruction layer. No functional FPU or CPU instruction is connected here.
    /// Motorola MC68881/MC68882 User's Manual, FMOVE and sections 3.5, 6.1.3/7:
    /// https://www.nxp.com/docs/en/reference-manual/MC68881UM.pdf
    /// </summary>
    public static class M68881IntegerConversion
    {
        public static M68881IntegerResult ToByte(M68881Extended source, M68881RoundingMode mode = M68881RoundingMode.NearestEven)
            => Convert(source, mode, 8);
        public static M68881IntegerResult ToWord(M68881Extended source, M68881RoundingMode mode = M68881RoundingMode.NearestEven)
            => Convert(source, mode, 16);
        public static M68881IntegerResult ToLong(M68881Extended source, M68881RoundingMode mode = M68881RoundingMode.NearestEven)
            => Convert(source, mode, 32);

        private static M68881IntegerResult Convert(M68881Extended source, M68881RoundingMode mode, int width)
        {
            if ((uint)mode > 3) throw new ArgumentOutOfRangeException(nameof(mode));
            var kind = source.Classification;
            if (kind == M68881ExtendedClass.QuietNaN || kind == M68881ExtendedClass.SignalingNaN)
                throw new ArgumentException("NaNs need explicit payload output and instruction-level exception handling.", nameof(source));
            bool negative = source.IsNegative;
            ulong limit = (1UL << (width - 1)) - (negative ? 0UL : 1UL);
            bool outside = kind == M68881ExtendedClass.Infinity;
            bool lost = false;
            ulong magnitude = 0;
            if (!outside && source.Significand != 0)
            {
                int power = source.BiasedExponent - M68881Extended.ExponentBias - 63;
                if (power >= 0)
                {
                    // Check before shifting: C# masks shift counts, and no wrapped value may fit by accident.
                    outside = power >= width || source.Significand > (limit >> power);
                    if (!outside) magnitude = source.Significand << power;
                }
                else
                {
                    magnitude = M68881BinaryRounding.RoundInteger(source.Significand, -power, negative, mode, out lost);
                    outside = magnitude > limit;
                }
            }
            if (outside) magnitude = limit;
            uint mask = width == 32 ? uint.MaxValue : (1U << width) - 1;
            uint bits = unchecked((uint)(negative ? 0UL - magnitude : magnitude)) & mask;
            return new M68881IntegerResult(bits, width, lost, outside);
        }
    }
}
