// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;

namespace AmigaEmulator.Core.Cpu
{
    public sealed partial class M68kCpu
    {
        [NonSerialized] private bool _locked030Access;

        private sealed class MmuAccessException : Exception
        {
            public readonly uint Address;
            public readonly int FunctionCode, Size;
            public readonly bool Write, Instruction, Locked;
            public MmuAccessException(uint address, int fc, bool write, int size, bool locked = false)
            { Address = address; FunctionCode = fc; Write = write; Size = size; Instruction = (fc & 3) == 2; Locked = locked; }
        }

        // Table walks use physical addresses and participate in deferred bus retirement.
        private uint Read030TableWord(uint address)
        {
            uint value = _deferBusSideEffects && _bus is IM68kPeekableBus peek
                ? ReadWithPendingWriteForwarding(peek, address, 4) : _bus.ReadLong(address);
            RecordBusMicroOps(M68kTraceAccessType.Read, address, 4, value, false, true);
            return value;
        }
        private void Write030TableWord(uint address, uint value)
        {
            RecordBusMicroOps(M68kTraceAccessType.Write, address, 4, value, true, true);
            if (!_deferBusSideEffects) _bus.WriteLong(address, value);
        }
        private uint Translate030Address(uint address, bool write, int size = 2)
        {
            int fc = _functionCodeOverride >= 0 ? _functionCodeOverride : (IsSupervisor() ? 5 : 1);
            // A locked RMW read already requires write permission, including
            // a CAS whose comparison would otherwise suppress its write.
            bool needsWrite = write || _locked030Access;
            var translation = _mmu.Translate(address, fc, needsWrite);
            if (!translation.Accessible(needsWrite)) throw new MmuAccessException(address, fc, write, size, _locked030Access);
            return translation.Physical;
        }
        private uint ReadLockedMemory(uint address, int size)
        {
            if (!Has030Cache) return ReadMemory(address, size);
            bool previous = _locked030Access;
            _locked030Access = true;
            try { return ReadMemory(address, size); }
            finally { _locked030Access = previous; }
        }
        private uint ReadLockedLocation(EaLocation location)
        {
            if (!Has030Cache) return ReadLocation(location);
            bool previous = _locked030Access;
            _locked030Access = true;
            try { return ReadLocation(location); }
            finally { _locked030Access = previous; }
        }
        private ushort Fetch030Word()
        {
            uint address = _state.PC;
            if ((address & 1) != 0) { Enter020AddressError(address); throw new AddressErrorException(); }
            int old = _functionCodeOverride;
            _functionCodeOverride = IsSupervisor() ? 6 : 2;
            try
            {
                ushort value = ReadWord(address);
                _state.PC = unchecked(address + 2);
                return value;
            }
            finally { _functionCodeOverride = old; }
        }
        private void Enter030BusError(MmuAccessException fault)
        {
            ushort sr = _state.SR;
            _functionCodeOverride = -1;
            SetStatusRegister((ushort)((sr | FlagS) & ~0xC000));
            ushort ssw = fault.Instruction ? (ushort)0xA000 :
                (ushort)(0x100 | (fault.Locked ? 0x80 : 0) | (fault.Write ? 0 : 0x40) | (fault.Size == 1 ? 0x10 : 0x20) | fault.FunctionCode);
            try
            {
                // Format A. Pipeline rerun and partial operand restart are still approximate.
                PushLong(0); PushLong(0); PushLong(0); PushLong(fault.Address);
                PushWord(0); PushWord(_state.CurrentOpcode); PushWord(ssw); PushWord(0);
                PushWord(0xA008); PushLong(_currentInstructionAddress); PushWord(sr);
                _exceptionEnteredDuringInstruction = true;
                uint target = ReadLong(_vectorBase + 8);
                ExceptionEntered?.Invoke(2, _currentInstructionAddress, sr, _currentInstructionAddress, _state.CurrentOpcode, target);
                SetProgramCounterInternal(target);
                _state.ExecutionState = M68kExecutionState.FetchOpcode;
            }
            catch (MmuAccessException)
            {
                // Fault during exception stacking/vector fetch halts the guest CPU.
                _state.ExecutionState = M68kExecutionState.Stopped;
            }
        }

        private bool Try030FunctionCode(int field, out int fc)
        {
            fc = field switch
            {
                0 => (int)_sourceFunctionCode, 1 => (int)_destinationFunctionCode,
                >= 8 and <= 15 => (int)(_state.D[field & 7] & 7),
                >= 16 and <= 23 => field & 7, _ => -1
            };
            if (fc < 0) return false;
            fc &= 7; return true;
        }
        private string Execute030MmuInstruction(ushort opcode)
        {
            if (TryEnterPrivilegeViolation(out string privilege)) return privilege;
            ushort ext = FetchWord();
            string Illegal() { EnterException(11, 0, _currentInstructionAddress); return "LINE-F"; }
            if (ext == 0x2400) { _mmu.Flush(); InvalidatePrefetch(); return "PFLUSHA"; }
            int mode = (opcode >> 3) & 7, reg = opcode & 7;
            bool control = mode == 2 || mode == 5 || mode == 6 || (mode == 7 && reg <= 1);
            if ((ext & 0xE300) == 0x2000 && ((ext >> 10) & 7) == 4)
            {
                if (!Try030FunctionCode(ext & 31, out int fc)) return Illegal();
                _mmu.Flush(fc, (ext >> 5) & 7); InvalidatePrefetch(); return "PFLUSH";
            }
            if (!control) return Illegal();
            if ((ext & 0xE300) == 0x2000 && ((ext >> 10) & 7) == 6)
            {
                if (!Try030FunctionCode(ext & 31, out int fc)) return Illegal();
                _mmu.Flush(fc, (ext >> 5) & 7, GetEffectiveAddress(mode, reg, 4, false));
                InvalidatePrefetch(); return "PFLUSH";
            }
            if ((ext & 0xFDE0) == 0x2000)
            {
                if (!Try030FunctionCode(ext & 31, out int fc)) return Illegal();
                _mmu.Load(GetEffectiveAddress(mode, reg, 4, false), fc, (ext & 0x200) == 0);
                return (ext & 0x200) != 0 ? "PLOADR" : "PLOADW";
            }
            if ((ext & 0xE000) == 0x8000)
            {
                int level = (ext >> 10) & 7;
                bool returnAddress = (ext & 0x100) != 0;
                if ((level == 0 && returnAddress) || (!returnAddress && (ext & 0xE0) != 0) ||
                    !Try030FunctionCode(ext & 31, out int fc)) return Illegal();
                var result = _mmu.Test(GetEffectiveAddress(mode, reg, 4, false), fc, (ext & 0x200) == 0, level);
                if (returnAddress) SetA((ext >> 5) & 7, result.Descriptor);
                return (ext & 0x200) != 0 ? "PTESTR" : "PTESTW";
            }
            int kind = ext & 0xFCFF;
            if (kind != 0x4000 && kind != 0x4800 && kind != 0x4C00 && kind != 0x0800 && kind != 0x0C00 && kind != 0x6000)
                return Illegal();
            bool toMemory = (ext & 0x200) != 0, flush = (ext & 0x100) == 0;
            if (kind == 0x6000 && !flush) return Illegal();
            uint address = GetEffectiveAddress(mode, reg, kind == 0x6000 ? 2 : 4, false);
            if (toMemory)
            {
                if (kind == 0x6000) WriteWord(address, _mmu.Status);
                else if (kind == 0x4800 || kind == 0x4C00)
                {
                    ulong value = kind == 0x4800 ? _mmu.SupervisorRoot : _mmu.CpuRoot;
                    WriteLong(address, (uint)(value >> 32)); WriteLong(address + 4, (uint)value);
                }
                else WriteLong(address, kind == 0x4000 ? _mmu.TranslationControl : kind == 0x0800 ? _mmu.Transparent0 : _mmu.Transparent1);
            }
            else
            {
                bool valid = true;
                if (kind == 0x6000) _mmu.Status = (ushort)(ReadWord(address) & 0xEE47);
                else if (kind == 0x4800 || kind == 0x4C00)
                {
                    ulong value = ((ulong)ReadLong(address) << 32) | ReadLong(address + 4);
                    valid = _mmu.SetRoot(kind == 0x4800, value, flush);
                }
                else if (kind == 0x4000) valid = _mmu.SetTranslationControl(ReadLong(address), flush);
                else _mmu.SetTransparent(kind == 0x0800 ? 0 : 1, ReadLong(address), flush);
                InvalidatePrefetch();
                if (!valid) EnterException(56, 0, _currentInstructionAddress);
            }
            return "PMOVE";
        }
    }
}
