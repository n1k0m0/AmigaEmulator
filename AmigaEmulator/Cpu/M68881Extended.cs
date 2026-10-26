// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Buffers.Binary;
using System.Numerics;

namespace AmigaEmulator.Core.Cpu
{
    /// <summary>Classifies the raw Motorola extended encoding, without executing an FPU operation.</summary>
    public enum M68881ExtendedClass
    {
        Zero, Normal, Denormal, Unnormal, Infinity, QuietNaN, SignalingNaN
    }

    /// <summary>
    /// Lossless 80-bit operand encoding used by the MC68881/MC68882. This is not an
    /// arithmetic engine or an architectural register load: unnormals and signaling
    /// NaNs remain unchanged. No FPCR/FPSR state, exceptions or rounding are applied.
    /// Reference: Motorola MC68881/MC68882 User's Manual, section 3, tables 3-1 to 3-3:
    /// https://www.nxp.com/docs/en/reference-manual/MC68881UM.pdf
    /// </summary>
    public readonly struct M68881Extended : IEquatable<M68881Extended>
    {
        public const int MemorySize = 12;
        public const int ExponentBias = 16383;
        private const ulong IntegerBit = 0x8000000000000000UL;
        private const ulong FractionMask = 0x7FFFFFFFFFFFFFFFUL;
        private const ulong QuietBit = 0x4000000000000000UL;

        public M68881Extended(ushort signExponent, ulong significand)
        {
            SignExponent = signExponent;
            Significand = significand;
        }

        public ushort SignExponent { get; }
        public ulong Significand { get; }
        public bool IsNegative => (SignExponent & 0x8000) != 0;
        public int BiasedExponent => SignExponent & 0x7FFF;

        public M68881ExtendedClass Classification
        {
            get
            {
                int exponent = BiasedExponent;
                if (exponent == 0x7FFF)
                {
                    // The explicit integer bit is a don't-care for Motorola infinity/NaN.
                    if ((Significand & FractionMask) == 0) return M68881ExtendedClass.Infinity;
                    return (Significand & QuietBit) != 0
                        ? M68881ExtendedClass.QuietNaN : M68881ExtendedClass.SignalingNaN;
                }
                if (exponent == 0 && Significand == 0) return M68881ExtendedClass.Zero;
                // Unlike an x87 classification rule, E=0 with J=1 is normal here.
                if ((Significand & IntegerBit) != 0) return M68881ExtendedClass.Normal;
                return exponent == 0 ? M68881ExtendedClass.Denormal : M68881ExtendedClass.Unnormal;
            }
        }

        /// <summary>Reads Motorola's 96-bit big-endian memory operand; ignores its reserved word.</summary>
        public static M68881Extended ReadMemory(ReadOnlySpan<byte> source)
        {
            if (source.Length < MemorySize) throw new ArgumentException("An extended operand needs 12 bytes.", nameof(source));
            return new M68881Extended(BinaryPrimitives.ReadUInt16BigEndian(source),
                BinaryPrimitives.ReadUInt64BigEndian(source.Slice(4)));
        }

        /// <summary>Writes the raw operand with a zero reserved word; rejects short buffers before writing.</summary>
        public void WriteMemory(Span<byte> destination)
        {
            if (destination.Length < MemorySize) throw new ArgumentException("An extended operand needs 12 bytes.", nameof(destination));
            BinaryPrimitives.WriteUInt16BigEndian(destination, SignExponent);
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2), 0);
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(4), Significand);
        }

        /// <summary>Exactly encodes a signed long integer; sign-extended byte/word operands also fit.</summary>
        public static M68881Extended FromSignedInteger(int value)
        {
            ulong magnitude = value < 0 ? (ulong)-(long)value : (ulong)value;
            return FromDyadic(value < 0, magnitude, 0);
        }

        /// <summary>
        /// Expands IEEE binary32 bits exactly. NaN sign, payload and signaling bit are
        /// transported only; a future instruction must separately perform SNAN processing.
        /// </summary>
        public static M68881Extended FromSingleBits(uint bits)
        {
            bool negative = (bits & 0x80000000U) != 0;
            int exponent = (int)((bits >> 23) & 0xFF);
            uint fraction = bits & 0x7FFFFF;
            if (exponent == 0xFF)
                return new M68881Extended((ushort)((negative ? 0x8000 : 0) | 0x7FFF), IntegerBit | ((ulong)fraction << 40));
            return exponent == 0
                ? FromDyadic(negative, fraction, -149)
                : FromDyadic(negative, 0x800000UL | fraction, exponent - 127 - 23);
        }

        /// <summary>Expands IEEE binary64 bits exactly, including signed zeros and raw NaN payloads.</summary>
        public static M68881Extended FromDoubleBits(ulong bits)
        {
            bool negative = (bits & IntegerBit) != 0;
            int exponent = (int)((bits >> 52) & 0x7FF);
            ulong fraction = bits & 0xFFFFFFFFFFFFFUL;
            if (exponent == 0x7FF)
                return new M68881Extended((ushort)((negative ? 0x8000 : 0) | 0x7FFF), IntegerBit | (fraction << 11));
            return exponent == 0
                ? FromDyadic(negative, fraction, -1074)
                : FromDyadic(negative, 0x10000000000000UL | fraction, exponent - 1023 - 52);
        }

        private static M68881Extended FromDyadic(bool negative, ulong magnitude, int power)
        {
            ushort sign = (ushort)(negative ? 0x8000 : 0);
            if (magnitude == 0) return new M68881Extended(sign, 0);
            int shift = BitOperations.LeadingZeroCount(magnitude);
            return new M68881Extended((ushort)(sign | (ExponentBias + 63 - shift + power)), magnitude << shift);
        }

        // Equality is bitwise, not numerical: signs of zero and noncanonical operands matter.
        public bool Equals(M68881Extended other) => SignExponent == other.SignExponent && Significand == other.Significand;
        public override bool Equals(object obj) => obj is M68881Extended other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SignExponent, Significand);
        public static bool operator ==(M68881Extended left, M68881Extended right) => left.Equals(right);
        public static bool operator !=(M68881Extended left, M68881Extended right) => !left.Equals(right);
    }
}
