// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;

namespace AmigaEmulator.Core.Cpu
{
    /// <summary>Raw FPCR precision field; the fourth encoding is deliberately not given arithmetic behavior.</summary>
    public enum M68881PrecisionControl : byte
    {
        Extended = 0, Single = 1, Double = 2, Reserved = 3
    }

    /// <summary>
    /// Isolated development storage, created only by an explicit factory. No processor or
    /// machine creates this object. Raw register writes do not implement architectural
    /// operand loading, normalization, arithmetic, status accrual or exception dispatch.
    /// MC68881/MC68882 User's Manual 2.1 through 2.4:
    /// https://www.nxp.com/docs/en/reference-manual/MC68881UM.pdf
    /// </summary>
    public sealed class M68881DevelopmentRegisters
    {
        public const int DataRegisterCount = 8;
        private readonly M68881Extended[] data = new M68881Extended[DataRegisterCount];
        private uint control;
        private uint status;
        private uint instructionAddress;

        private M68881DevelopmentRegisters() { Reset(); }

        public static M68881DevelopmentRegisters CreateIsolated() => new M68881DevelopmentRegisters();

        public uint ControlRegister => control;
        public uint StatusRegister => status;
        public uint InstructionAddressRegister => instructionAddress;
        public byte ExceptionEnables => (byte)(control >> 8);
        public M68881RoundingMode RoundingMode => (M68881RoundingMode)((control >> 4) & 3);
        public M68881PrecisionControl PrecisionControl => (M68881PrecisionControl)((control >> 6) & 3);

        /// <summary>Standalone reset contract; not connected to CPU RESET or a guest FRESTORE instruction.</summary>
        public void Reset()
        {
            // Section 2.1 requires positive quiet NaNs. All-one payload is our deterministic
            // storage choice, not a claim that the section specifies these exact payload bits.
            var resetNaN = new M68881Extended(0x7FFF, ulong.MaxValue);
            for (int i = 0; i < DataRegisterCount; i++) data[i] = resetNaN;
            control = status = instructionAddress = 0;
        }

        public M68881Extended ReadRawDataRegister(int index)
        {
            ValidateIndex(index);
            return data[index];
        }

        /// <summary>Bit transport only: retains signed zeros, raw unnormals and signaling NaNs without side effects.</summary>
        public void WriteRawDataRegister(int index, M68881Extended value)
        {
            ValidateIndex(index);
            data[index] = value;
        }

        /// <summary>High word is ignored; the low word is retained, including uninterpreted mode bits.</summary>
        public void WriteControlRegister(uint value) => control = value & 0xFFFFU;

        /// <summary>All FPSR bits are user writable. This does not generate or replay an exception.</summary>
        public void WriteStatusRegister(uint value) => status = value;

        /// <summary>Explicit raw software write only; instruction-specific logical address capture is still absent.</summary>
        public void WriteInstructionAddressRegister(uint value) => instructionAddress = value;

        private static void ValidateIndex(int index)
        {
            if ((uint)index >= DataRegisterCount) throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
