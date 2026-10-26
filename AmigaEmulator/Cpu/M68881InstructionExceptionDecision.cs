// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
namespace AmigaEmulator.Core.Cpu
{
    /// <summary>Vector numbers are data only; their numeric order is not exception priority.</summary>
    public enum M68881InstructionTrapVector : byte
    {
        None = 0,
        BranchOrSetOnUnordered = 48,
        Inexact = 49,
        DivideByZero = 50,
        Underflow = 51,
        OperandError = 52,
        Overflow = 53,
        SignalingNaN = 54
    }

    /// <summary>
    /// Pure decision from explicitly NEW instruction flags. No registers, CPU trap,
    /// pending queue, instruction decoding, or automatic conversion-status bridge.
    /// Future FMOVEM/control-transfer handlers must bypass instruction exception processing.
    /// MC68881/MC68882 User's Manual Table 6-1, 6.1.9 and 6.1.10:
    /// https://www.nxp.com/docs/en/reference-manual/MC68881UM.pdf
    /// </summary>
    public static class M68881InstructionExceptionDecision
    {
        public static M68881InstructionTrapVector SelectFromNewInstruction(
            M68881ExceptionStatus instructionStatus, byte exceptionEnables)
        {
            int raised = (byte)instructionStatus;
            int enabled = raised & exceptionEnables;
            if ((enabled & 0x80) != 0) return M68881InstructionTrapVector.BranchOrSetOnUnordered;
            if ((enabled & 0x40) != 0) return M68881InstructionTrapVector.SignalingNaN;
            if ((enabled & 0x20) != 0) return M68881InstructionTrapVector.OperandError;
            if ((enabled & 0x10) != 0) return M68881InstructionTrapVector.Overflow;
            if ((enabled & 0x08) != 0) return M68881InstructionTrapVector.Underflow;
            if ((enabled & 0x04) != 0) return M68881InstructionTrapVector.DivideByZero;
            // Inexact can be selected even without INEX2 in EXC: masked overflow
            // plus ENABLE(INEX2) is sufficient. Do not synthesize an EXC flag.
            if (((raised & 0x12) != 0 && (exceptionEnables & 0x02) != 0) ||
                ((raised & 0x01) != 0 && (exceptionEnables & 0x01) != 0))
                return M68881InstructionTrapVector.Inexact;
            return M68881InstructionTrapVector.None;
        }
    }
}
