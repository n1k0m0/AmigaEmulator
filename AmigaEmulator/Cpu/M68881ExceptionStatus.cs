// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;

namespace AmigaEmulator.Core.Cpu
{
    /// <summary>Instruction-reported EXC byte, not conversion observations or CPU trap vectors.</summary>
    [Flags]
    public enum M68881ExceptionStatus : byte
    {
        None = 0,
        InexactDecimalInput = 0x01,
        InexactOperation = 0x02,
        DivideByZero = 0x04,
        Underflow = 0x08,
        Overflow = 0x10,
        OperandError = 0x20,
        SignalingNaN = 0x40,
        BranchOrSetOnUnordered = 0x80
    }

    /// <summary>Pure proposed status result; nothing is dispatched to a guest processor.</summary>
    public readonly struct M68881ExceptionStatusResult
    {
        internal M68881ExceptionStatusResult(uint statusRegister, M68881ExceptionStatus highestEnabledStatus)
        {
            StatusRegister = statusRegister;
            HighestEnabledStatus = highestEnabledStatus;
        }
        public uint StatusRegister { get; }
        public M68881ExceptionStatus HighestEnabledStatus { get; }
    }

    /// <summary>
    /// Isolated status transition for a future instruction backend. Callers must supply the
    /// new instruction's EXC byte explicitly. This is not an FPU, an instruction executor,
    /// FPCR/FPIAR state, a trap queue, or a connection to existing conversion observations.
    /// Motorola MC68881/MC68882 User's Manual 2.2.1, 2.3.3 and 2.3.4:
    /// https://www.nxp.com/docs/en/reference-manual/MC68881UM.pdf
    /// </summary>
    public static class M68881ExceptionStatusPolicy
    {
        public static M68881ExceptionStatusResult CompleteInstruction(
            uint previousStatusRegister, M68881ExceptionStatus instructionStatus, byte exceptionEnables)
        {
            int current = (byte)instructionStatus;
            int accrued = (byte)previousStatusRegister;
            if ((current & 0xE0) != 0) accrued |= 0x80;
            if ((current & 0x10) != 0) accrued |= 0x40;
            if ((current & 0x0A) == 0x0A) accrued |= 0x20;
            if ((current & 0x04) != 0) accrued |= 0x10;
            if ((current & 0x13) != 0) accrued |= 0x08;

            // EXC is replaced; AEXC is sticky. The condition and quotient bytes, and
            // user-written low reserved bits, survive this isolated transition.
            uint result = (previousStatusRegister & 0xFFFF0000U) | ((uint)current << 8) | (uint)accrued;
            int enabledCurrent = current & exceptionEnables;
            int highest = 0;
            for (int bit = 0x80; bit != 0; bit >>= 1)
                if ((enabledCurrent & bit) != 0)
                {
                    highest = bit;
                    break;
                }
            // Old EXC/AEXC never participate in selection: enabling a previously
            // recorded class cannot by itself request a retrospective exception.
            return new M68881ExceptionStatusResult(result, (M68881ExceptionStatus)highest);
        }
    }
}
