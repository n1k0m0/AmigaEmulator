// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;

namespace AmigaEmulator.Core.Cpu
{
    /// <summary>External NaN bits and original signaling classification; not architectural exception state.</summary>
    public readonly struct M68881NaNBinaryResult
    {
        internal M68881NaNBinaryResult(ulong bits, int width, bool wasSignaling)
        { Bits = bits; Width = width; WasSignaling = wasSignaling; }
        public ulong Bits { get; }
        public int Width { get; }
        public bool WasSignaling { get; }
    }

    /// <summary>
    /// Explicit NaN output payloads for future FMOVE external destinations. Quieting sets only
    /// extended fraction bit 62, then truncates low payload bits without numeric rounding.
    /// IEEE output drops the explicit J bit; integer output uses the high 8/16/32 significand
    /// bits including J, independently of the separate sign. Source operands stay immutable.
    /// This does not select an operand, modify FPCR/FPSR, dispatch a trap or perform bus writes.
    /// Motorola MC68881/MC68882 User's Manual sections 3.2.5, 4.5.4 and 6.1.2/3:
    /// https://www.nxp.com/docs/en/reference-manual/MC68881UM.pdf
    /// </summary>
    public static class M68881NaNOutput
    {
        public static M68881Extended Quiet(M68881Extended source)
        {
            Validate(source);
            return new M68881Extended(source.SignExponent, source.Significand | 0x4000000000000000UL);
        }
        public static M68881NaNBinaryResult ToSingle(M68881Extended source) => Binary(source, 23, 32, 0x7F800000);
        public static M68881NaNBinaryResult ToDouble(M68881Extended source) => Binary(source, 52, 64, 0x7FF0000000000000);
        public static M68881NaNBinaryResult ToByte(M68881Extended source) => Integer(source, 8);
        public static M68881NaNBinaryResult ToWord(M68881Extended source) => Integer(source, 16);
        public static M68881NaNBinaryResult ToLong(M68881Extended source) => Integer(source, 32);

        private static M68881NaNBinaryResult Binary(M68881Extended source, int fractionBits, int width, ulong infinity)
        {
            M68881Extended quiet = Quiet(source);
            ulong fraction = (quiet.Significand & 0x7FFFFFFFFFFFFFFFUL) >> (63 - fractionBits);
            ulong sign = source.IsNegative ? 1UL << (width - 1) : 0;
            return new M68881NaNBinaryResult(sign | infinity | fraction, width,
                source.Classification == M68881ExtendedClass.SignalingNaN);
        }
        private static M68881NaNBinaryResult Integer(M68881Extended source, int width)
        {
            M68881Extended quiet = Quiet(source);
            return new M68881NaNBinaryResult(quiet.Significand >> (64 - width), width,
                source.Classification == M68881ExtendedClass.SignalingNaN);
        }
        private static void Validate(M68881Extended source)
        {
            var kind = source.Classification;
            if (kind != M68881ExtendedClass.QuietNaN && kind != M68881ExtendedClass.SignalingNaN)
                throw new ArgumentException("NaN output requires a NaN operand.", nameof(source));
        }
    }
}
