// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;

namespace AmigaEmulator.Core.Cpu
{
    // Integer extensions are selected at construction. The 68000 bus and save layout stay unchanged.
    public sealed partial class M68kCpu
    {
        [NonSerialized] private uint _vectorBase, _cacheControl, _cacheAddress, _masterStackPointer;
        [NonSerialized] private uint _sourceFunctionCode, _destinationFunctionCode;
        [NonSerialized] private uint _accessControl0, _accessControl1;
        [NonSerialized] private ushort _accessControlStatus;
        public AmigaCpuModel Model => _cpuModel;
        public uint VectorBase => _vectorBase;
        public uint CacheControl => _cacheControl;

        private void Reset020State()
        {
            _vectorBase = _cacheControl = _cacheAddress = _masterStackPointer = 0;
            _sourceFunctionCode = _destinationFunctionCode = 0;
            _accessControl0 = _accessControl1 = 0; _accessControlStatus = 0;
            ClearInstructionCache();
            ClearDataCache(); _dataCacheEnabled = false; _cacheAccessInProgress = false;
            _mmu?.Reset(); _functionCodeOverride = -1;
        }

        private void Save020State(BinaryWriter writer)
        {
            writer.Write(5); writer.Write((int)_cpuModel);
            writer.Write(_vectorBase); writer.Write(_cacheControl); writer.Write(_cacheAddress);
            writer.Write(_masterStackPointer); writer.Write(_sourceFunctionCode); writer.Write(_destinationFunctionCode);
            if (_cpuModel == AmigaCpuModel.M68EC030)
            { writer.Write(_accessControl0); writer.Write(_accessControl1); writer.Write(_accessControlStatus); }
            if (_cpuModel == AmigaCpuModel.M68030) _mmu.SaveState(writer);
            SaveInstructionCache(writer);
            if (Has030Cache) SaveDataCache(writer);
        }

        private void Load020State(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version < 1 || version > 5 || (version == 2 && _cpuModel != AmigaCpuModel.M68EC030) ||
                (version == 3 && _cpuModel != AmigaCpuModel.M68030) || reader.ReadInt32() != (int)_cpuModel)
                throw new InvalidDataException("CPU extension state does not match the selected processor.");
            _vectorBase = reader.ReadUInt32(); _cacheControl = reader.ReadUInt32(); _cacheAddress = reader.ReadUInt32();
            _masterStackPointer = reader.ReadUInt32(); _sourceFunctionCode = reader.ReadUInt32(); _destinationFunctionCode = reader.ReadUInt32();
            _accessControl0 = _accessControl1 = 0; _accessControlStatus = 0;
            if (version == 2 || (version >= 4 && _cpuModel == AmigaCpuModel.M68EC030))
            { _accessControl0 = reader.ReadUInt32(); _accessControl1 = reader.ReadUInt32(); _accessControlStatus = reader.ReadUInt16(); }
            if (version == 3 || (version >= 4 && _cpuModel == AmigaCpuModel.M68030)) _mmu.LoadState(reader);
            ClearInstructionCache();
            if (version >= 4) LoadInstructionCache(reader);
            ClearDataCache();
            if (version >= 5 && Has030Cache) LoadDataCache(reader);
            _dataCacheEnabled = Has030Cache && (_cacheControl & 0x100) != 0;
            _cacheAccessInProgress = false;
            _functionCodeOverride = -1;
        }

        private uint Decode020IndexedAddress(uint baseAddress, ushort extension, int index)
        {
            index = unchecked(index << ((extension >> 9) & 3));
            if ((extension & 0x100) == 0) return unchecked(baseAddress + (uint)(index + (sbyte)extension));
            int displacementSize = (extension >> 4) & 3;
            int indirect = extension & 7;
            bool suppressIndex = (extension & 0x40) != 0;
            if (displacementSize == 0 || (extension & 8) != 0 || indirect == 4 || (suppressIndex && indirect >= 4))
                throw new InvalidOperationException("Reserved full extension word.");
            if ((extension & 0x80) != 0) baseAddress = 0;
            if (suppressIndex) index = 0;
            int displacement = displacementSize == 2 ? (short)FetchWord() : displacementSize == 3 ? unchecked((int)FetchLong()) : 0;
            int outer = (indirect & 3) == 2 ? (short)FetchWord() : (indirect & 3) == 3 ? unchecked((int)FetchLong()) : 0;
            uint address = unchecked(baseAddress + (uint)displacement);
            if (indirect == 0) return unchecked(address + (uint)index);
            if (indirect < 4) return unchecked(ReadLong(address + (uint)index) + (uint)outer);
            return unchecked(ReadLong(address) + (uint)index + (uint)outer);
        }

        private void Set020StatusRegister(ushort value)
        {
            // T1/T0, S/M, IPL and CCR. Reserved bits read zero.
            value &= 0xF71F;
            int previous = !IsSupervisor() ? 0 : (_state.SR & 0x1000) != 0 ? 2 : 1;
            int next = (value & FlagS) == 0 ? 0 : (value & 0x1000) != 0 ? 2 : 1;
            if (previous != next)
            {
                if (previous == 0) _state.USP = _state.A[7];
                else if (previous == 1) _state.SSP = _state.A[7];
                else _masterStackPointer = _state.A[7];
                _state.A[7] = next == 0 ? _state.USP : next == 1 ? _state.SSP : _masterStackPointer;
            }
            _state.SR = value;
        }

        private void Enter020Exception(int vector, ushort interruptMask, uint pc, ushort oldSr)
        {
            SetStatusRegister((ushort)((oldSr | FlagS) & ~0xC000));
            bool instructionFrame = vector == 5 || vector == 6 || vector == 7 || vector == 9;
            if (instructionFrame) PushLong(_currentInstructionAddress);
            PushWord((ushort)((instructionFrame ? 0x2000 : 0) | vector * 4));
            PushLong(pc); PushWord(oldSr);
            if (interruptMask != 0 && (oldSr & 0x3000) == 0x3000)
            {
                // Master interrupt saves a normal MSP frame and an ISP throwaway frame.
                SetStatusRegister((ushort)(_state.SR & ~0x1000));
                PushWord((ushort)(0x1000 | vector * 4)); PushLong(pc); PushWord(oldSr);
            }
            if (interruptMask != 0)
                SetStatusRegister((ushort)((_state.SR & ~(InterruptMask | 0x1000)) | interruptMask));
            _exceptionEnteredDuringInstruction = true;
            uint target = ReadLong(unchecked(_vectorBase + (uint)vector * 4)) & AddressMask;
            ExceptionEntered?.Invoke(vector, pc, oldSr, _currentInstructionAddress, _state.CurrentOpcode, target);
            SetProgramCounterInternal(target);
            _state.ExecutionState = M68kExecutionState.FetchOpcode;
        }

        private void Execute020Rte(uint frame, ushort sr, uint pc)
        {
            // Throwaway frames switch back to the interrupted master stack before reading the real frame.
            for (int count = 0; count < 2; count++)
            {
                int format = ReadWord(frame + 6) >> 12;
                int length = format switch { 0 => 8, 1 => 8, 2 => 12, 9 => 20, 10 => 32, 11 => 92, _ => 0 };
                if (length == 0) { EnterException(14, 0, _currentInstructionAddress); return; }
                SetA(7, unchecked(frame + (uint)length));
                SetStatusRegister(sr);
                if (format != 1) { SetProgramCounterInternal(pc); return; }
                frame = _state.A[7]; sr = ReadWord(frame); pc = ReadLong(frame + 2);
            }
            EnterException(14, 0, _currentInstructionAddress);
        }

        private void Enter020AddressError(uint address)
        {
            ushort sr = _state.SR;
            SetStatusRegister((ushort)((sr | FlagS) & ~0xC000));
            // Short format A; no data transfer occurred for an odd instruction address.
            for (int i = 0; i < 8; i++) PushExceptionWordUnchecked(0);
            PushExceptionWordUnchecked(0); PushExceptionWordUnchecked(0);
            PushExceptionWordUnchecked(0x2000); // Rerun instruction pipe stage C.
            PushExceptionWordUnchecked(0);
            PushExceptionWordUnchecked(0xA00C);
            PushExceptionLongUnchecked(address); PushExceptionWordUnchecked(sr);
            _exceptionEnteredDuringInstruction = true;
            uint target = ReadLong(_vectorBase + 12) & AddressMask;
            ExceptionEntered?.Invoke(3, address, sr, _currentInstructionAddress, _state.CurrentOpcode, target);
            SetProgramCounterInternal(target);
        }

        private bool TryExecute020(ushort opcode, out string mnemonic)
        {
            mnemonic = null;
            if (_mmu != null && (opcode & 0xFFC0) == 0xF000)
            { mnemonic = Execute030MmuInstruction(opcode); return true; }
            if (_cpuModel == AmigaCpuModel.M68EC030 && (opcode & 0xFFC0) == 0xF000)
            { mnemonic = ExecuteEc030AccessControl(opcode); return true; }
            if (opcode == 0x4E7A || opcode == 0x4E7B)
            {
                if (TryEnterPrivilegeViolation(out mnemonic)) return true;
                ushort ext = FetchWord(); int reg = (ext >> 12) & 7; bool address = (ext & 0x8000) != 0;
                int control = ext & 0xFFF;
                if (opcode == 0x4E7B) Write020Control(control, address ? _state.A[reg] : _state.D[reg]);
                else { uint value = Read020Control(control); if (address) SetA(reg, value); else SetD(reg, value); }
                mnemonic = "MOVEC"; return true;
            }
            if ((opcode & 0xFFF8) == 0x49C0)
            {
                int reg = opcode & 7; _state.D[reg] = unchecked((uint)(int)(sbyte)_state.D[reg]);
                SetLogicFlags(_state.D[reg], 4); mnemonic = "EXTB.L"; return true;
            }
            if ((opcode & 0xFFF8) == 0x4808)
            {
                int reg = opcode & 7; int displacement = unchecked((int)FetchLong());
                PushLong(_state.A[reg]); SetA(reg, _state.A[7]); SetA(7, unchecked(_state.A[7] + (uint)displacement));
                mnemonic = "LINK.L"; return true;
            }
            if (opcode == 0x4E74)
            {
                int displacement = (short)FetchWord(); uint pc = PopLong();
                SetA(7, unchecked(_state.A[7] + (uint)displacement)); SetProgramCounterInternal(pc);
                mnemonic = "RTD"; return true;
            }
            if ((opcode & 0xFFC0) == 0x42C0)
            {
                int mode = (opcode >> 3) & 7, reg = opcode & 7;
                if (!IsDataAlterableEa(mode, reg)) throw new InvalidOperationException();
                WriteEa(mode, reg, 2, (uint)(_state.SR & 0x1F)); mnemonic = "MOVE CCR"; return true;
            }
            if ((opcode & 0xF0F8) == 0x50F8 && (opcode & 7) >= 2 && (opcode & 7) <= 4)
            {
                if ((opcode & 7) == 2) FetchWord(); else if ((opcode & 7) == 3) FetchLong();
                if (EvaluateCondition((opcode >> 8) & 15)) EnterException(7);
                mnemonic = "TRAPcc"; return true;
            }
            if ((opcode & 0xF1C0) == 0x4100)
            {
                int mode = (opcode >> 3) & 7, reg = opcode & 7;
                if (mode == 1 || (mode == 7 && reg > 4)) throw new InvalidOperationException();
                int bound = unchecked((int)ReadEa(mode, reg, 4)), value = unchecked((int)_state.D[(opcode >> 9) & 7]);
                if (value < 0 || value > bound)
                {
                    _state.SR = value < 0 ? (ushort)(_state.SR | FlagN) : (ushort)(_state.SR & ~FlagN);
                    EnterException(6);
                }
                mnemonic = "CHK.L"; return true;
            }
            if ((opcode & 0xF8C0) == 0xE8C0) { mnemonic = Execute020BitField(opcode); return true; }
            if ((opcode & 0xFF80) == 0x4C00) { mnemonic = Execute020MultiplyDivide(opcode); return true; }
            if (opcode == 0x0CFC || opcode == 0x0EFC) { mnemonic = Execute020Cas2(opcode); return true; }
            if ((opcode & 0xF9C0) == 0x08C0 && ((opcode >> 9) & 3) != 0)
            { mnemonic = Execute020Cas(opcode); return true; }
            if ((opcode & 0xF9C0) == 0x00C0 && ((opcode >> 9) & 3) != 3)
            { mnemonic = Execute020Bounds(opcode); return true; }
            if ((opcode & 0xFF00) == 0x0E00 && ((opcode >> 6) & 3) != 3)
            { mnemonic = Execute020Moves(opcode); return true; }
            if ((opcode & 0xF1F0) == 0x8140 || (opcode & 0xF1F0) == 0x8180)
            { mnemonic = Execute020Pack(opcode); return true; }
            return false;
        }

        private uint Read020Control(int register) => register switch
        {
            0 => _sourceFunctionCode, 1 => _destinationFunctionCode, 2 => _cacheControl,
            0x800 => _state.USP, 0x801 => _vectorBase, 0x802 => _cacheAddress,
            0x803 => (IsSupervisor() && (_state.SR & 0x1000) != 0) ? _state.A[7] : _masterStackPointer,
            0x804 => (IsSupervisor() && (_state.SR & 0x1000) == 0) ? _state.A[7] : _state.SSP,
            _ => throw new InvalidOperationException("Unknown 020 control register.")
        };

        private void Write020Control(int register, uint value)
        {
            switch (register)
            {
                case 0: _sourceFunctionCode = value & 7; break;
                case 1: _destinationFunctionCode = value & 7; break;
                case 2:
                    // The EC030 adds data-cache and burst/write-allocation controls.
                    // Clear-entry/all are commands and always read back as zero.
                    _cacheControl = value & (_cpuModel == AmigaCpuModel.M68EC030 || _cpuModel == AmigaCpuModel.M68030 ? 0x3313u : 3u);
                    if ((value & 8) != 0) ClearInstructionCache();
                    if ((value & 4) != 0) ClearInstructionCacheEntry();
                    if (Has030Cache)
                    {
                        if ((value & 0x800) != 0) ClearDataCache();
                        if ((value & 0x400) != 0) ClearDataCacheEntry();
                    }
                    _dataCacheEnabled = Has030Cache && (_cacheControl & 0x100) != 0;
                    InvalidatePrefetch(); break;
                case 0x800: _state.USP = value; break;
                case 0x801: _vectorBase = value; break;
                case 0x802: _cacheAddress = Has030Cache ? value : value & 0xFC; break;
                case 0x803: _masterStackPointer = value; if (IsSupervisor() && (_state.SR & 0x1000) != 0) SetA(7, value); break;
                case 0x804: _state.SSP = value; if (IsSupervisor() && (_state.SR & 0x1000) == 0) SetA(7, value); break;
                default: throw new InvalidOperationException("Unknown 020 control register.");
            }
        }

        private string Execute020BitField(ushort opcode)
        {
            int operation = (opcode >> 8) & 7, mode = (opcode >> 3) & 7, reg = opcode & 7;
            bool writes = operation == 2 || operation == 4 || operation == 6 || operation == 7;
            if (mode != 0 && (!IsControlEa(mode, reg) || (writes && mode == 7 && reg >= 2)))
                throw new InvalidOperationException("Invalid bitfield address.");
            ushort extension = FetchWord();
            if ((extension & 0x8000) != 0 || ((extension & 0x800) != 0 && (extension & 0x600) != 0) ||
                ((extension & 0x20) != 0 && (extension & 0x18) != 0)) throw new InvalidOperationException();
            int offset = (extension & 0x800) != 0 ? unchecked((int)_state.D[(extension >> 6) & 7]) : (extension >> 6) & 31;
            int width = (int)((extension & 0x20) != 0 ? _state.D[extension & 7] & 31 : (uint)extension & 31);
            if (width == 0) width = 32;
            uint mask = width == 32 ? uint.MaxValue : (1u << width) - 1;
            int destination = (extension >> 12) & 7;
            uint field;
            uint address = 0; int bitOffset = 0, byteCount = 0; ulong memory = 0;
            if (mode == 0)
            {
                offset &= 31;
                uint value = _state.D[reg];
                field = ((value << offset) | (value >> ((32 - offset) & 31))) >> (32 - width);
            }
            else
            {
                address = unchecked(GetEffectiveAddress(mode, reg, 1, writes) + (uint)(offset >> 3));
                bitOffset = offset & 7; byteCount = (bitOffset + width + 7) / 8;
                for (int i = 0; i < byteCount; i++) memory = (memory << 8) | ReadByte(address + (uint)i);
                field = (uint)(memory >> (byteCount * 8 - bitOffset - width)) & mask;
            }
            uint flagField = operation == 7 ? _state.D[destination] & mask : field;
            _state.SR = (ushort)((_state.SR & ~(FlagN | FlagZ | FlagV | FlagC)) |
                (flagField == 0 ? FlagZ : 0) | ((flagField & (1u << (width - 1))) != 0 ? FlagN : 0));
            switch (operation)
            {
                case 1: SetD(destination, field); break;
                case 2: field ^= mask; break;
                case 3: SetD(destination, unchecked((uint)((int)(field << (32 - width)) >> (32 - width)))); break;
                case 4: field = 0; break;
                case 5:
                    int first = 0;
                    while (first < width && (field & (1u << (width - first - 1))) == 0) first++;
                    SetD(destination, unchecked((uint)(offset + first))); break;
                case 6: field = mask; break;
                case 7: field = flagField; break;
            }
            if (writes)
            {
                if (mode == 0)
                {
                    uint shiftedMask = mask << (32 - width), shiftedValue = field << (32 - width);
                    uint rotatedMask = (shiftedMask >> offset) | (shiftedMask << ((32 - offset) & 31));
                    uint rotatedValue = (shiftedValue >> offset) | (shiftedValue << ((32 - offset) & 31));
                    _state.D[reg] = (_state.D[reg] & ~rotatedMask) | rotatedValue;
                }
                else
                {
                    int shift = byteCount * 8 - bitOffset - width;
                    memory = (memory & ~((ulong)mask << shift)) | ((ulong)field << shift);
                    for (int i = 0; i < byteCount; i++) WriteByte(address + (uint)i, (byte)(memory >> ((byteCount - 1 - i) * 8)));
                }
            }
            return operation switch { 0 => "BFTST", 1 => "BFEXTU", 2 => "BFCHG", 3 => "BFEXTS",
                4 => "BFCLR", 5 => "BFFFO", 6 => "BFSET", _ => "BFINS" };
        }

        private string Execute020MultiplyDivide(ushort opcode)
        {
            int mode = (opcode >> 3) & 7, reg = opcode & 7;
            if (mode == 1 || (mode == 7 && reg > 4)) throw new InvalidOperationException();
            ushort ext = FetchWord(); if ((ext & 0x83F8) != 0) throw new InvalidOperationException();
            int low = (ext >> 12) & 7, high = ext & 7;
            bool signed = (ext & 0x800) != 0, wide = (ext & 0x400) != 0, divide = (opcode & 0x40) != 0;
            uint source = ReadEa(mode, reg, 4);
            if (!divide)
            {
                ulong product = signed ? unchecked((ulong)((long)(int)source * (int)_state.D[low])) : (ulong)source * _state.D[low];
                bool overflow = !wide && (signed ? unchecked((long)product) != (int)product : product > uint.MaxValue);
                SetD(low, (uint)product); if (wide) SetD(high, (uint)(product >> 32));
                bool negative = (product & (wide ? 0x8000000000000000UL : 0x80000000UL)) != 0;
                bool zero = wide ? product == 0 : (uint)product == 0;
                Set020LongFlags(negative, zero, overflow);
                return signed ? "MULS.L" : "MULU.L";
            }
            if (source == 0) { EnterException(5); return "DIV.L zero"; }
            ulong dividend = wide ? ((ulong)_state.D[high] << 32) | _state.D[low] : _state.D[low];
            uint quotient, remainder; bool tooLarge;
            if (signed)
            {
                long value = wide ? unchecked((long)dividend) : (int)dividend;
                long divisor = (int)source;
                if (value == long.MinValue && divisor == -1) { Set020LongFlags(false, false, true); return "DIVS.L overflow"; }
                long result = value / divisor; tooLarge = result < int.MinValue || result > int.MaxValue;
                quotient = unchecked((uint)result); remainder = unchecked((uint)(value % divisor));
            }
            else
            {
                ulong result = dividend / source; tooLarge = result > uint.MaxValue;
                quotient = (uint)result; remainder = (uint)(dividend % source);
            }
            if (!tooLarge) { if (high != low) SetD(high, remainder); SetD(low, quotient); }
            Set020LongFlags((quotient & 0x80000000) != 0, quotient == 0, tooLarge);
            return signed ? "DIVS.L" : "DIVU.L";
        }

        private void Set020LongFlags(bool negative, bool zero, bool overflow) => _state.SR =
            (ushort)((_state.SR & ~(FlagN | FlagZ | FlagV | FlagC)) | (negative ? FlagN : 0) | (zero ? FlagZ : 0) | (overflow ? FlagV : 0));

        private string Execute020Cas(ushort opcode)
        {
            int sizeCode = (opcode >> 9) & 3, size = sizeCode == 1 ? 1 : sizeCode == 2 ? 2 : 4;
            int mode = (opcode >> 3) & 7, reg = opcode & 7;
            if (!IsAlterableMemoryEa(mode, reg)) throw new InvalidOperationException();
            ushort ext = FetchWord(); if ((ext & 0xFE38) != 0) throw new InvalidOperationException();
            int compare = ext & 7, update = (ext >> 6) & 7;
            EaLocation location = ResolveEaLocation(mode, reg, size, true);
            uint value = ReadLockedLocation(location);
            SubtractValues(value, _state.D[compare], size, false);
            if ((_state.SR & FlagZ) != 0) WriteLocation(location, _state.D[update]);
            else WriteDataRegister(compare, size, value);
            return "CAS";
        }

        private string Execute020Cas2(ushort opcode)
        {
            int size = opcode == 0x0CFC ? 2 : 4;
            ushort first = FetchWord(), second = FetchWord();
            if (((first | second) & 0x0E38) != 0) throw new InvalidOperationException();
            uint Address(ushort ext) => (ext & 0x8000) != 0 ? _state.A[(ext >> 12) & 7] : _state.D[(ext >> 12) & 7];
            uint a = Address(first), b = Address(second);
            uint av = ReadLockedMemory(a, size), bv = ReadLockedMemory(b, size);
            int ac = first & 7, bc = second & 7;
            uint au = _state.D[(first >> 6) & 7], bu = _state.D[(second >> 6) & 7];
            SubtractValues(av, _state.D[ac], size, false);
            if ((_state.SR & FlagZ) != 0) SubtractValues(bv, _state.D[bc], size, false);
            if ((_state.SR & FlagZ) != 0) { WriteMemory(a, size, au); WriteMemory(b, size, bu); }
            else { WriteDataRegister(bc, size, bv); WriteDataRegister(ac, size, av); }
            return "CAS2";
        }

        private string Execute020Bounds(ushort opcode)
        {
            int size = 1 << ((opcode >> 9) & 3), mode = (opcode >> 3) & 7, reg = opcode & 7;
            if (!IsControlEa(mode, reg)) throw new InvalidOperationException();
            ushort ext = FetchWord(); if ((ext & 0x7FF) != 0) throw new InvalidOperationException();
            uint address = GetEffectiveAddress(mode, reg, size, false);
            int Signed(uint value) => size == 1 ? (sbyte)value : size == 2 ? (short)value : unchecked((int)value);
            int lower = Signed(ReadMemory(address, size)), upper = Signed(ReadMemory(address + (uint)size, size));
            bool addressRegister = (ext & 0x8000) != 0;
            int register = (ext >> 12) & 7;
            int value = addressRegister ? unchecked((int)_state.A[register]) : Signed(_state.D[register]);
            bool outside = lower <= upper ? value < lower || value > upper : value < lower && value > upper;
            _state.SR = (ushort)((_state.SR & ~(FlagC | FlagZ)) | (outside ? FlagC : 0) | (value == lower || value == upper ? FlagZ : 0));
            if (outside && (ext & 0x800) != 0) EnterException(6);
            return (ext & 0x800) != 0 ? "CHK2" : "CMP2";
        }

        private string Execute020Moves(ushort opcode)
        {
            if (TryEnterPrivilegeViolation(out string mnemonic)) return mnemonic;
            int size = DecodeSize((opcode >> 6) & 3), mode = (opcode >> 3) & 7, reg = opcode & 7;
            if (!IsAlterableMemoryEa(mode, reg)) throw new InvalidOperationException();
            ushort ext = FetchWord(); if ((ext & 0x7FF) != 0) throw new InvalidOperationException();
            int register = (ext >> 12) & 7; bool addressRegister = (ext & 0x8000) != 0;
            int oldFunctionCode = _functionCodeOverride;
            _functionCodeOverride = (int)(((ext & 0x800) != 0 ? _destinationFunctionCode : _sourceFunctionCode) & 7);
            try
            {
            if ((ext & 0x800) != 0)
            {
                uint value = addressRegister ? _state.A[register] : _state.D[register];
                WriteEa(mode, reg, size, value);
            }
            else
            {
                uint value = ReadEa(mode, reg, size);
                if (addressRegister) SetA(register, size == 1 ? unchecked((uint)(int)(sbyte)value) : size == 2 ? unchecked((uint)(int)(short)value) : value);
                else WriteDataRegister(register, size, value);
            }
            }
            finally { _functionCodeOverride = oldFunctionCode; }
            return "MOVES";
        }

        private byte Read020PredecrementByte(int register)
        {
            SetA(register, _state.A[register] - AddressIncrement(register, 1));
            return ReadByte(_state.A[register]);
        }

        private void Write020PredecrementByte(int register, byte value)
        {
            SetA(register, _state.A[register] - AddressIncrement(register, 1));
            WriteByte(_state.A[register], value);
        }

        private string Execute020Pack(ushort opcode)
        {
            int source = opcode & 7, destination = (opcode >> 9) & 7;
            bool memory = (opcode & 8) != 0, unpack = (opcode & 0x80) != 0;
            ushort adjustment = FetchWord(); uint value;
            if (memory)
            {
                value = Read020PredecrementByte(source);
                if (!unpack) value |= (uint)Read020PredecrementByte(source) << 8;
            }
            else value = _state.D[source];
            if (unpack) value = (((value & 0xF0) << 4) | (value & 15)) + adjustment;
            else { value += adjustment; value = ((value >> 4) & 0xF0) | (value & 15); }
            if (memory)
            {
                Write020PredecrementByte(destination, (byte)value);
                if (unpack) Write020PredecrementByte(destination, (byte)(value >> 8));
            }
            else WriteDataRegister(destination, unpack ? 2 : 1, value);
            return unpack ? "UNPK" : "PACK";
        }
    }
}
