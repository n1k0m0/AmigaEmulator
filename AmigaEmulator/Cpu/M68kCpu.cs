/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Core.Cpu
{
    /// <summary>
    /// Emulates the Motorola 68000 CPU instruction execution state used by the A500.
    /// </summary>
    public sealed class M68kCpu
    {
        /// <summary>
        /// Stores one CPU bus micro-operation waiting for its real chip-bus slot.
        /// </summary>
        private struct PendingBusOperation
        {
            /// <summary>
            /// Stores whether this micro-operation writes to the bus.
            /// </summary>
            public bool Write;
            /// <summary>
            /// Stores access type used for tracing and diagnostics.
            /// </summary>
            public M68kTraceAccessType AccessType;
            /// <summary>
            /// Stores normalized address.
            /// </summary>
            public uint Address;
            /// <summary>
            /// Stores transfer size in bytes.
            /// </summary>
            public int Size;
            /// <summary>
            /// Stores value for write operations.
            /// </summary>
            public uint Value;
        }

        /// <summary>
        /// Constant for Flag C.
        /// </summary>
        private const ushort FlagC = 0x0001;
        /// <summary>
        /// Constant for Flag V.
        /// </summary>
        private const ushort FlagV = 0x0002;
        /// <summary>
        /// Constant for Flag Z.
        /// </summary>
        private const ushort FlagZ = 0x0004;
        /// <summary>
        /// Constant for Flag N.
        /// </summary>
        private const ushort FlagN = 0x0008;
        /// <summary>
        /// Constant for Flag X.
        /// </summary>
        private const ushort FlagX = 0x0010;
        /// <summary>
        /// Constant for Flag T.
        /// </summary>
        private const ushort FlagT = 0x8000;
        /// <summary>
        /// Constant for Flag S.
        /// </summary>
        private const ushort FlagS = 0x2000;
        /// <summary>
        /// Defines the INTERRUPT MASK custom register offset or bit mask.
        /// </summary>
        private const ushort InterruptMask = 0x0700;
        /// <summary>
        /// Constant for Address Mask.
        /// </summary>
        private const uint AddressMask = 0x00FFFFFF;

        /// <summary>
        /// Stores bus.
        /// </summary>
        private readonly IM68kBus _bus;
        /// <summary>
        /// Tracks optional bus arbitration supplied by the machine bus.
        /// </summary>
        private readonly IM68kBusArbitration _busArbitration;
        /// <summary>
        /// Stores state.
        /// </summary>
        private readonly M68kState _state;
        /// <summary>
        /// Stores remaining CPU Cycles.
        /// </summary>
        private int _remainingCpuCycles;
        /// <summary>
        /// Stores last Instruction Cycles.
        /// </summary>
        private int _lastInstructionCycles;
        /// <summary>
        /// Stores pending Interrupt Level.
        /// </summary>
        private int _pendingInterruptLevel;
        /// <summary>
        /// Stores last Instruction.
        /// </summary>
        private string _lastInstruction;
        /// <summary>
        /// Stores current Instruction Address.
        /// </summary>
        private uint _currentInstructionAddress;
        /// <summary>
        /// Tracks bus micro-operations from the last executed instruction that still need free chip-bus slots.
        /// </summary>
        private int _pendingBusMicroOps;
        /// <summary>
        /// Tracks bus micro-operations performed while decoding the current instruction.
        /// </summary>
        private int _currentInstructionBusMicroOps;
        /// <summary>
        /// Tracks whether the next word fetch is the opcode fetch rather than an extension-word read.
        /// </summary>
        private bool _opcodeFetchInProgress;
        /// <summary>
        /// Tracks whether the current write belongs to a read-modify-write instruction sequence.
        /// </summary>
        private bool _readModifyWriteInProgress;
        /// <summary>
        /// Tracks whether a memory effective-address read can pair with a following write as RMW.
        /// </summary>
        private bool _lastReadLocationValid;
        /// <summary>
        /// Stores last Read Location.
        /// </summary>
        private EaLocation _lastReadLocation;
        /// <summary>
        /// Tracks opcode-fetch bus micro-operations for the current instruction.
        /// </summary>
        private int _currentOpcodeFetchMicroOps;
        /// <summary>
        /// Tracks operand-read bus micro-operations for the current instruction.
        /// </summary>
        private int _currentOperandReadMicroOps;
        /// <summary>
        /// Tracks operand-write bus micro-operations for the current instruction.
        /// </summary>
        private int _currentOperandWriteMicroOps;
        /// <summary>
        /// Tracks read-modify-write bus micro-operations for the current instruction.
        /// </summary>
        private int _currentReadModifyWriteMicroOps;
        /// <summary>
        /// Tracks bus micro-operations that still need to be retired on real CPU-available slots.
        /// </summary>
        private readonly Queue<PendingBusOperation> _pendingBusOperations;
        /// <summary>
        /// Tracks whether write side effects should be delayed until their scheduled bus micro-operation retires.
        /// </summary>
        private bool _deferBusSideEffects;
        /// <summary>
        /// Tracks whether the instruction being executed has already taken an exception.
        /// </summary>
        private bool _exceptionEnteredDuringInstruction;
        /// <summary>
        /// Stores whether the sequential instruction prefetch word is valid.
        /// </summary>
        private bool _prefetchValid;
        /// <summary>
        /// Stores the bus address of the prefetched instruction word.
        /// </summary>
        private uint _prefetchAddress;
        /// <summary>
        /// Stores the prefetched instruction word.
        /// </summary>
        private ushort _prefetchWord;

        /// <summary>
        /// Stores Ea Location data used by the emulator.
        /// </summary>
        private struct EaLocation
        {
            /// <summary>
            /// Stores Mode.
            /// </summary>
            public int Mode;
            /// <summary>
            /// Stores Register.
            /// </summary>
            public int Register;
            /// <summary>
            /// Stores Size.
            /// </summary>
            public int Size;
            /// <summary>
            /// Stores Address.
            /// </summary>
            public uint Address;
        }

        /// <summary>
        /// Provides Address Error Exception functionality.
        /// </summary>
        private sealed class AddressErrorException : Exception
        {
        }

        /// <summary>
        /// Runs M 68 k Cpu.
        /// </summary>
        public M68kCpu(IM68kBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _busArbitration = bus as IM68kBusArbitration;
            _state = new M68kState();
            _pendingBusOperations = new Queue<PendingBusOperation>();
            _lastInstruction = string.Empty;
        }

        /// <summary>
        /// Gets D0.
        /// </summary>
        public uint D0
        {
            get { return _state.D[0]; }
        }

        /// <summary>
        /// Gets A7.
        /// </summary>
        public uint A7
        {
            get { return _state.A[7]; }
        }

        /// <summary>
        /// Gets PC.
        /// </summary>
        public uint PC
        {
            get { return _state.PC; }
        }

        /// <summary>
        /// Gets SR.
        /// </summary>
        public ushort SR
        {
            get { return _state.SR; }
        }

        /// <summary>
        /// Gets USP.
        /// </summary>
        public uint USP
        {
            get { return _state.USP; }
        }

        /// <summary>
        /// Gets SSP.
        /// </summary>
        public uint SSP
        {
            get { return _state.SSP; }
        }

        /// <summary>
        /// Returns whether Stopped is true.
        /// </summary>
        public bool IsStopped
        {
            get { return _state.ExecutionState == M68kExecutionState.Stopped; }
        }

        /// <summary>
        /// Gets or sets Trace Enabled.
        /// </summary>
        public bool TraceEnabled { get; set; }

        /// <summary>
        /// Raised when the CPU detects an odd-address memory access.
        /// </summary>
        public event Action<uint, int, bool, uint, ushort> OddAddressAccessed;

        /// <summary>
        /// Raised when the CPU vectors into a 68000 exception handler.
        /// </summary>
        public event Action<int, uint, ushort, uint, ushort, uint> ExceptionEntered;

        /// <summary>
        /// Gets Current Instruction Address.
        /// </summary>
        public uint CurrentInstructionAddress
        {
            get { return _currentInstructionAddress & AddressMask; }
        }

        /// <summary>
        /// Gets Current Opcode.
        /// </summary>
        public ushort CurrentOpcode
        {
            get { return _state.CurrentOpcode; }
        }

        /// <summary>
        /// Gets Last Instruction.
        /// </summary>
        public string LastInstruction
        {
            get { return _lastInstruction; }
        }

        /// <summary>
        /// Gets Snapshot.
        /// </summary>
        public M68kState Snapshot
        {
            get { return _state.Clone(); }
        }

        /// <summary>
        /// Writes CPU registers, pending cycles, interrupt state, and suspended bus micro-operations.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            StateSerializer.WriteObjectFields(writer, this, "_bus", "_busArbitration");
        }

        /// <summary>
        /// Restores CPU registers, pending cycles, interrupt state, and suspended bus micro-operations.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            StateSerializer.ReadObjectFields(reader, this, "_bus", "_busArbitration");
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            for (int index = 0; index < 8; index++)
            {
                _state.D[index] = 0;
                _state.A[index] = 0;
            }

            uint initialStackPointer;
            uint initialProgramCounter;
            if (_bus is IM68kResetVectorProvider resetVectorProvider &&
                resetVectorProvider.TryReadResetVectors(out initialStackPointer, out initialProgramCounter))
            {
                initialStackPointer &= AddressMask;
                initialProgramCounter &= AddressMask;
            }
            else
            {
                initialStackPointer = _bus.ReadLong(0) & AddressMask;
                initialProgramCounter = _bus.ReadLong(4) & AddressMask;
            }

            _state.SR = 0x2700;
            _state.SSP = initialStackPointer;
            _state.USP = initialStackPointer;
            _state.A[7] = initialStackPointer;
            _state.PC = initialProgramCounter;
            _state.CurrentOpcode = 0;
            _state.Cycles = 0;
            _state.ExecutionState = M68kExecutionState.FetchOpcode;
            _remainingCpuCycles = 0;
            _pendingBusMicroOps = 0;
            _currentInstructionBusMicroOps = 0;
            _opcodeFetchInProgress = false;
            _readModifyWriteInProgress = false;
            _lastReadLocationValid = false;
            _currentOpcodeFetchMicroOps = 0;
            _currentOperandReadMicroOps = 0;
            _currentOperandWriteMicroOps = 0;
            _currentReadModifyWriteMicroOps = 0;
            _pendingBusOperations.Clear();
            _deferBusSideEffects = false;
            _exceptionEnteredDuringInstruction = false;
            _prefetchValid = false;
            _prefetchAddress = 0;
            _prefetchWord = 0;
            _lastInstructionCycles = 0;
            _lastInstruction = "RESET";
            _currentInstructionAddress = initialProgramCounter;
        }

        /// <summary>
        /// Returns the D.
        /// </summary>
        public uint GetD(int register)
        {
            ValidateRegister(register);
            return _state.D[register];
        }

        /// <summary>
        /// Returns the A.
        /// </summary>
        public uint GetA(int register)
        {
            ValidateRegister(register);
            return _state.A[register];
        }

        /// <summary>
        /// Updates the D.
        /// </summary>
        public void SetD(int register, uint value)
        {
            ValidateRegister(register);
            _state.D[register] = value;
        }

        /// <summary>
        /// Updates the A.
        /// </summary>
        public void SetA(int register, uint value)
        {
            ValidateRegister(register);
            _state.A[register] = value;
            if (register == 7)
            {
                if (IsSupervisor())
                {
                    _state.SSP = _state.A[7];
                }
                else
                {
                    _state.USP = _state.A[7];
                }
            }
        }

        /// <summary>
        /// Updates the Program Counter.
        /// </summary>
        public void SetProgramCounter(uint pc)
        {
            SetProgramCounterInternal(pc);
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public M68kTraceEntry Tick(bool chipBusAvailable)
        {
            if (_pendingBusMicroOps > 0)
            {
                if (!CanRetireNextPendingBusOperation(chipBusAvailable))
                {
                    _state.Cycles += 2;
                    return TraceEnabled ? CreateTrace(_state.PC, _state.PC, 0, "bus-wait", _state.SR, _state.SR, M68kTraceAccessType.Internal) : null;
                }

                RetireNextPendingBusOperation();
                _pendingBusMicroOps = _pendingBusOperations.Count;
                return TraceEnabled ? CreateTrace(_state.PC, _state.PC, 0, "bus-micro-op", _state.SR, _state.SR, M68kTraceAccessType.Internal) : null;
            }

            if (_remainingCpuCycles > 0)
            {
                _remainingCpuCycles = Math.Max(0, _remainingCpuCycles - 2);
                return TraceEnabled ? CreateTrace(_state.PC, _state.PC, 0, "internal", _state.SR, _state.SR, M68kTraceAccessType.Internal) : null;
            }

            _pendingBusOperations.Clear();
            _deferBusSideEffects = true;
            M68kTraceEntry trace = Step();
            _deferBusSideEffects = false;
            if (CanRetireNextPendingBusOperation(chipBusAvailable))
            {
                RetireNextPendingBusOperation();
            }
            int busMicroOps = Math.Max(1, _currentInstructionBusMicroOps);
            _pendingBusMicroOps = _pendingBusOperations.Count;
            _remainingCpuCycles = Math.Max(0, _lastInstructionCycles - (busMicroOps * 2));
            return trace;
        }

        /// <summary>
        /// Runs Step.
        /// </summary>
        public M68kTraceEntry Step()
        {
            uint pcBefore = _state.PC;
            ushort srBefore = _state.SR;
            ushort opcode = 0;
            string instruction;
            _currentInstructionBusMicroOps = 0;
            _currentOpcodeFetchMicroOps = 0;
            _currentOperandReadMicroOps = 0;
            _currentOperandWriteMicroOps = 0;
            _currentReadModifyWriteMicroOps = 0;
            _opcodeFetchInProgress = false;
            _readModifyWriteInProgress = false;
            _lastReadLocationValid = false;
            _exceptionEnteredDuringInstruction = false;
            bool traceWasEnabled = (srBefore & FlagT) != 0;

            if (TryEnterPendingInterrupt())
            {
                _lastInstructionCycles = 44;
                _state.Cycles += _lastInstructionCycles;
                _lastInstruction = "INTERRUPT";
                return TraceEnabled ? CreateTrace(pcBefore, _state.PC, 0, _lastInstruction, srBefore, _state.SR, M68kTraceAccessType.Internal) : null;
            }

            if (_state.ExecutionState == M68kExecutionState.Stopped)
            {
                _lastInstructionCycles = 4;
                _state.Cycles += 4;
                return TraceEnabled ? CreateTrace(pcBefore, _state.PC, 0, "STOPPED", srBefore, _state.SR, M68kTraceAccessType.Internal) : null;
            }

            try
            {
                _state.ExecutionState = M68kExecutionState.ExecuteInstruction;
                _currentInstructionAddress = pcBefore;
                _opcodeFetchInProgress = true;
                opcode = FetchWord();
                _opcodeFetchInProgress = false;
                _state.CurrentOpcode = opcode;
                instruction = ExecuteOpcode(opcode);
                if (traceWasEnabled &&
                    !_exceptionEnteredDuringInstruction &&
                    _state.ExecutionState != M68kExecutionState.Stopped)
                {
                    EnterException(9, 0);
                    instruction += " + TRACE";
                }

                _lastInstruction = instruction;
                _state.ExecutionState = _state.ExecutionState == M68kExecutionState.Stopped
                    ? M68kExecutionState.Stopped
                    : M68kExecutionState.FetchOpcode;
            }
            catch (AddressErrorException)
            {
                instruction = "ADDRESS ERROR";
                _lastInstruction = instruction;
            }
            catch (InvalidOperationException)
            {
                EnterException(4);
                instruction = "ILLEGAL";
                _lastInstruction = instruction;
            }

            _lastInstructionCycles = EstimateInstructionCycles(opcode, instruction, pcBefore, _state.PC);
            _state.Cycles += _lastInstructionCycles;
            return TraceEnabled ? CreateTrace(pcBefore, _state.PC, opcode, instruction, srBefore, _state.SR, M68kTraceAccessType.OpcodeFetch) : null;
        }

        /// <summary>
        /// Runs Request Interrupt.
        /// </summary>
        public void RequestInterrupt(int level)
        {
            if (level <= 0 || level > 7)
            {
                return;
            }

            if (level > _pendingInterruptLevel)
            {
                _pendingInterruptLevel = level;
            }
        }

        /// <summary>
        /// Updates the Interrupt Level.
        /// </summary>
        public void SetInterruptLevel(int level)
        {
            if (level < 0)
            {
                level = 0;
            }
            else if (level > 7)
            {
                level = 7;
            }

            _pendingInterruptLevel = level;
        }

        /// <summary>
        /// Runs Enter Exception.
        /// </summary>
        public void EnterException(int vectorNumber)
        {
            EnterException(vectorNumber, 0);
        }

        /// <summary>
        /// Returns the Debug Info.
        /// </summary>
        public string GetDebugInfo()
        {
            return string.Format(
                "PC {0:X6} SR {1:X4} D0 {2:X8} D1 {3:X8} D2 {4:X8} D3 {5:X8} D4 {6:X8} D5 {7:X8} D6 {8:X8} D7 {9:X8} A0 {10:X8} A1 {11:X8} A2 {12:X8} A3 {13:X8} A4 {14:X8} A5 {15:X8} A6 {16:X8} A7 {17:X8} {18}",
                _state.PC,
                _state.SR,
                _state.D[0],
                _state.D[1],
                _state.D[2],
                _state.D[3],
                _state.D[4],
                _state.D[5],
                _state.D[6],
                _state.D[7],
                _state.A[0],
                _state.A[1],
                _state.A[2],
                _state.A[3],
                _state.A[4],
                _state.A[5],
                _state.A[6],
                _state.A[7],
                _lastInstruction);
        }

        /// <summary>
        /// Runs Estimate Instruction Cycles.
        /// </summary>
        private static int EstimateInstructionCycles(ushort opcode, string instruction, uint pcBefore, uint pcAfter)
        {
            instruction = instruction ?? string.Empty;
            if (instruction == "STOPPED")
            {
                return 4;
            }

            if (instruction.StartsWith("MOVEQ", StringComparison.Ordinal))
            {
                return 4;
            }

            if (instruction.StartsWith("BTST", StringComparison.Ordinal) ||
                instruction.StartsWith("BCHG", StringComparison.Ordinal) ||
                instruction.StartsWith("BCLR", StringComparison.Ordinal) ||
                instruction.StartsWith("BSET", StringComparison.Ordinal))
            {
                return EstimateEaInstructionCycles(opcode, 6, 10);
            }

            if (instruction.StartsWith("BRA", StringComparison.Ordinal) ||
                instruction.StartsWith("BSR", StringComparison.Ordinal) ||
                instruction.StartsWith("B", StringComparison.Ordinal))
            {
                return pcAfter == pcBefore + 2u ? 8 : 10;
            }

            if (instruction.StartsWith("DB", StringComparison.Ordinal))
            {
                return pcAfter == pcBefore + 2u ? 12 : 10;
            }

            if (instruction.StartsWith("LEA", StringComparison.Ordinal) ||
                instruction.StartsWith("PEA", StringComparison.Ordinal))
            {
                return 8;
            }

            if (instruction.StartsWith("JSR", StringComparison.Ordinal) ||
                instruction.StartsWith("JMP", StringComparison.Ordinal))
            {
                return 12;
            }

            if (instruction.StartsWith("RTS", StringComparison.Ordinal) ||
                instruction.StartsWith("RTE", StringComparison.Ordinal) ||
                instruction.StartsWith("RTR", StringComparison.Ordinal))
            {
                return 16;
            }

            if (instruction.StartsWith("MOVEM", StringComparison.Ordinal))
            {
                return 20;
            }

            if (instruction.StartsWith("MOVEP", StringComparison.Ordinal))
            {
                return (opcode & 0x0040) != 0 ? 24 : 16;
            }

            if (instruction.StartsWith("MUL", StringComparison.Ordinal) ||
                instruction.StartsWith("DIV", StringComparison.Ordinal))
            {
                return 70;
            }

            if (instruction.StartsWith("MOVE", StringComparison.Ordinal))
            {
                return EstimateMoveCycles(opcode, DecodeMoveSize(opcode));
            }

            if (instruction.StartsWith("ADD", StringComparison.Ordinal) ||
                instruction.StartsWith("SUB", StringComparison.Ordinal) ||
                instruction.StartsWith("CMP", StringComparison.Ordinal) ||
                instruction.StartsWith("AND", StringComparison.Ordinal) ||
                instruction.StartsWith("OR", StringComparison.Ordinal) ||
                instruction.StartsWith("EOR", StringComparison.Ordinal) ||
                instruction.StartsWith("CLR", StringComparison.Ordinal) ||
                instruction.StartsWith("TST", StringComparison.Ordinal) ||
                instruction.StartsWith("NEG", StringComparison.Ordinal) ||
                instruction.StartsWith("NOT", StringComparison.Ordinal))
            {
                return EstimateEaInstructionCycles(opcode, 4, 8);
            }

            if (instruction.StartsWith("LS", StringComparison.Ordinal) ||
                instruction.StartsWith("AS", StringComparison.Ordinal) ||
                instruction.StartsWith("RO", StringComparison.Ordinal))
            {
                return 8;
            }

            if (instruction.StartsWith("EXG", StringComparison.Ordinal) ||
                instruction.StartsWith("EXT", StringComparison.Ordinal) ||
                instruction.StartsWith("SWAP", StringComparison.Ordinal) ||
                instruction.StartsWith("S", StringComparison.Ordinal))
            {
                return 4;
            }

            if (instruction.StartsWith("LINK", StringComparison.Ordinal) ||
                instruction.StartsWith("UNLK", StringComparison.Ordinal))
            {
                return 16;
            }

            if (instruction.StartsWith("ILLEGAL", StringComparison.Ordinal) ||
                instruction.StartsWith("LINE", StringComparison.Ordinal) ||
                instruction.StartsWith("PRIV", StringComparison.Ordinal))
            {
                return 34;
            }

            return 8;
        }

        /// <summary>
        /// Runs Estimate Move Cycles.
        /// </summary>
        private static int EstimateMoveCycles(ushort opcode, int size)
        {
            int destinationRegister = (opcode >> 9) & 7;
            int destinationMode = (opcode >> 6) & 7;
            int sourceMode = (opcode >> 3) & 7;
            int sourceRegister = opcode & 7;
            int source = EstimateEaReadCycles(sourceMode, sourceRegister, size);
            int destination = EstimateEaWriteCycles(destinationMode, destinationRegister, size);
            return Math.Max(4, 4 + source + destination);
        }

        /// <summary>
        /// Runs Estimate Ea Instruction Cycles.
        /// </summary>
        private static int EstimateEaInstructionCycles(ushort opcode, int registerCycles, int memoryCycles)
        {
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            return mode <= 1 ? registerCycles : memoryCycles + EstimateEaReadCycles(mode, register, 2);
        }

        /// <summary>
        /// Runs Estimate Ea Read Cycles.
        /// </summary>
        private static int EstimateEaReadCycles(int mode, int register, int size)
        {
            switch (mode)
            {
                case 0:
                case 1:
                    return 0;
                case 2:
                case 3:
                case 4:
                    return size == 4 ? 8 : 4;
                case 5:
                    return size == 4 ? 12 : 8;
                case 6:
                    return size == 4 ? 14 : 10;
                case 7:
                    switch (register)
                    {
                        case 0: return size == 4 ? 12 : 8;
                        case 1: return size == 4 ? 16 : 12;
                        case 2: return size == 4 ? 12 : 8;
                        case 3: return size == 4 ? 14 : 10;
                        case 4: return size == 4 ? 8 : 4;
                    }
                    break;
            }

            return 4;
        }

        /// <summary>
        /// Runs Estimate Ea Write Cycles.
        /// </summary>
        private static int EstimateEaWriteCycles(int mode, int register, int size)
        {
            return mode <= 1 ? 0 : EstimateEaReadCycles(mode, register, size);
        }

        /// <summary>
        /// Decodes Move Size from Amiga-compatible data.
        /// </summary>
        private static int DecodeMoveSize(ushort opcode)
        {
            int sizeCode = (opcode >> 12) & 3;
            if (sizeCode == 1)
            {
                return 1;
            }

            if (sizeCode == 3)
            {
                return 2;
            }

            return 4;
        }

        /// <summary>
        /// Runs Execute Opcode.
        /// </summary>
        private string ExecuteOpcode(ushort opcode)
        {
            if ((opcode & 0xF000) == 0xA000)
            {
                EnterException(10);
                return "LINEA $" + opcode.ToString("X4");
            }

            if ((opcode & 0xF000) == 0xF000)
            {
                EnterException(11);
                return "LINEF $" + opcode.ToString("X4");
            }

            if ((opcode & 0xF100) == 0x7000)
            {
                return ExecuteMoveq(opcode);
            }

            if ((opcode & 0xF000) == 0x1000 || (opcode & 0xF000) == 0x2000 || (opcode & 0xF000) == 0x3000)
            {
                return ExecuteMove(opcode);
            }

            if ((opcode & 0xF000) == 0x6000)
            {
                return ExecuteBranch(opcode);
            }

            if ((opcode & 0xF0F8) == 0x50C8)
            {
                return ExecuteDbcc(opcode);
            }

            if ((opcode & 0xF0C0) == 0x50C0)
            {
                return ExecuteScc(opcode);
            }

            if ((opcode & 0xF000) == 0x5000 && ((opcode >> 6) & 3) != 3)
            {
                return ExecuteAddqSubq(opcode);
            }

            if ((opcode & 0xF000) == 0x9000)
            {
                return ExecuteAddSubCmp(opcode, false, true);
            }

            if ((opcode & 0xF1F8) == 0xC140 || (opcode & 0xF1F8) == 0xC148 || (opcode & 0xF1F8) == 0xC188)
            {
                return ExecuteExg(opcode);
            }

            if ((opcode & 0xF1C0) == 0xC0C0 || (opcode & 0xF1C0) == 0xC1C0)
            {
                return ExecuteMultiply(opcode);
            }

            if ((opcode & 0xF1C0) == 0x80C0 || (opcode & 0xF1C0) == 0x81C0)
            {
                return ExecuteDivide(opcode);
            }

            if ((opcode & 0xF000) == 0x8000)
            {
                return ExecuteOr(opcode);
            }

            if ((opcode & 0xF000) == 0xC000)
            {
                return ExecuteAnd(opcode);
            }

            if ((opcode & 0xF000) == 0xB000)
            {
                return ExecuteCmp(opcode);
            }

            if ((opcode & 0xF000) == 0xD000)
            {
                return ExecuteAddSubCmp(opcode, true, true);
            }

            if (TryExecuteImmediate(opcode, out string immediateMnemonic))
            {
                return immediateMnemonic;
            }

            if ((opcode & 0xF138) == 0x0108)
            {
                return ExecuteMovep(opcode);
            }

            if (TryExecuteImmediateBitOperation(opcode, out string bitMnemonic))
            {
                return bitMnemonic;
            }

            if (TryExecuteDynamicBitOperation(opcode, out string dynamicBitMnemonic))
            {
                return dynamicBitMnemonic;
            }

            if ((opcode & 0xF000) == 0xE000)
            {
                return ExecuteShiftRotate(opcode);
            }

            if ((opcode & 0xFF00) == 0x4000 && (opcode & 0xFFC0) != 0x40C0)
            {
                return ExecuteNegx(opcode);
            }

            if ((opcode & 0xFF00) == 0x4400 && (opcode & 0xFFC0) != 0x44C0)
            {
                return ExecuteNeg(opcode);
            }

            if ((opcode & 0xFFC0) == 0x40C0)
            {
                int mode = (opcode >> 3) & 7;
                int register = opcode & 7;
                if (mode == 1)
                {
                    EnterException(4);
                    return "ILLEGAL MOVE SR";
                }

                WriteEa(mode, register, 2, _state.SR);
                return FormatEaInstruction("MOVE SR", 2, mode, register);
            }

            if ((opcode & 0xFFC0) == 0x44C0)
            {
                int mode = (opcode >> 3) & 7;
                int register = opcode & 7;
                if (mode == 1)
                {
                    EnterException(4);
                    return "ILLEGAL MOVE CCR";
                }

                uint value = ReadEa(mode, register, 2);
                SetCcr((ushort)value);
                return FormatEaInstruction("MOVE CCR", 2, mode, register);
            }

            if ((opcode & 0xFFC0) == 0x46C0)
            {
                int mode = (opcode >> 3) & 7;
                int register = opcode & 7;
                if (TryEnterPrivilegeViolation(out string privilegeMnemonic))
                {
                    return privilegeMnemonic;
                }

                if (mode == 1)
                {
                    EnterException(4);
                    return "ILLEGAL MOVE SR";
                }

                uint value = ReadEa(mode, register, 2);
                SetStatusRegister((ushort)value);
                return FormatEaInstruction("MOVE SR", 2, mode, register);
            }

            if ((opcode & 0xFFF8) == 0x4E60)
            {
                if (TryEnterPrivilegeViolation(out string privilegeMnemonic))
                {
                    return privilegeMnemonic;
                }

                int register = opcode & 7;
                _state.USP = _state.A[register];
                return "MOVE A" + register + ",USP";
            }

            if ((opcode & 0xFFF8) == 0x4E68)
            {
                if (TryEnterPrivilegeViolation(out string privilegeMnemonic))
                {
                    return privilegeMnemonic;
                }

                int register = opcode & 7;
                SetA(register, _state.USP);
                return "MOVE USP,A" + register;
            }

            if ((opcode & 0xFFF8) == 0x4E50)
            {
                int register = opcode & 7;
                short displacement = (short)FetchWord();
                PushLong(_state.A[register]);
                SetA(register, _state.A[7]);
                SetA(7, (uint)((int)_state.A[7] + displacement));
                return "LINK A" + register;
            }

            if ((opcode & 0xFFF8) == 0x4E58)
            {
                int register = opcode & 7;
                SetA(7, _state.A[register]);
                SetA(register, PopLong());
                return "UNLK A" + register;
            }

            if ((opcode & 0xF1C0) == 0x41C0)
            {
                int addressRegister = (opcode >> 9) & 7;
                int mode = (opcode >> 3) & 7;
                int register = opcode & 7;
                if (!IsControlEa(mode, register))
                {
                    EnterException(4);
                    return "ILLEGAL LEA";
                }

                SetA(addressRegister, GetEffectiveAddress(mode, register, 4, true));
                return "LEA " + FormatEa(mode, register, 4) + ",A" + addressRegister;
            }

            if ((opcode & 0xFFF8) == 0x4840)
            {
                int register = opcode & 7;
                uint value = _state.D[register];
                uint result = ((value & 0xFFFF) << 16) | ((value >> 16) & 0xFFFF);
                _state.D[register] = result;
                SetLogicFlags(result, 4);
                return "SWAP D" + register;
            }

            if ((opcode & 0xFFF8) == 0x4880)
            {
                int register = opcode & 7;
                uint result = (uint)(ushort)(short)(sbyte)(_state.D[register] & 0xFF);
                _state.D[register] = (_state.D[register] & 0xFFFF0000) | (result & 0xFFFF);
                SetLogicFlags(result, 2);
                return "EXT.W D" + register;
            }

            if ((opcode & 0xFFF8) == 0x48C0)
            {
                int register = opcode & 7;
                uint result = (uint)(int)(short)(_state.D[register] & 0xFFFF);
                _state.D[register] = result;
                SetLogicFlags(result, 4);
                return "EXT.L D" + register;
            }

            if ((opcode & 0xFFC0) == 0x4840)
            {
                int mode = (opcode >> 3) & 7;
                int register = opcode & 7;
                if (!IsControlEa(mode, register))
                {
                    EnterException(4);
                    return "ILLEGAL PEA";
                }

                PushLong(GetEffectiveAddress(mode, register, 4, true));
                return "PEA " + FormatEa(mode, register, 4);
            }

            if ((opcode & 0xFB80) == 0x4880 || (opcode & 0xFB80) == 0x4C80)
            {
                return ExecuteMovem(opcode);
            }

            if ((opcode & 0xFF00) == 0x4200)
            {
                return ExecuteClr(opcode);
            }

            if ((opcode & 0xFFC0) == 0x4AC0)
            {
                return ExecuteTas(opcode);
            }

            if ((opcode & 0xFF00) == 0x4A00)
            {
                return ExecuteTst(opcode);
            }

            if ((opcode & 0xFF00) == 0x4600)
            {
                return ExecuteNot(opcode);
            }

            if ((opcode & 0xFFF0) == 0x4E40)
            {
                int vector = 32 + (opcode & 0x000F);
                EnterException(vector);
                return "TRAP #" + (opcode & 0x000F).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            switch (opcode)
            {
                case 0x4E70:
                    if (TryEnterPrivilegeViolation(out string resetPrivilegeMnemonic))
                    {
                        return resetPrivilegeMnemonic;
                    }

                    return "RESET";
                case 0x4E71:
                    return "NOP";
                case 0x4E72:
                    if (TryEnterPrivilegeViolation(out string stopPrivilegeMnemonic))
                    {
                        return stopPrivilegeMnemonic;
                    }

                    SetStatusRegister(FetchWord());
                    _state.ExecutionState = M68kExecutionState.Stopped;
                    return "STOP";
                case 0x4E73:
                    if (TryEnterPrivilegeViolation(out string rtePrivilegeMnemonic))
                    {
                        return rtePrivilegeMnemonic;
                    }

                    ExecuteRte();
                    return "RTE";
                case 0x4E75:
                    SetProgramCounterInternal(PopLong());
                    return "RTS";
                case 0x4E76:
                    if ((_state.SR & FlagV) != 0)
                    {
                        EnterException(7);
                    }

                    return "TRAPV";
                default:
                    break;
            }

            if ((opcode & 0xFFC0) == 0x4EC0)
            {
                int mode = (opcode >> 3) & 7;
                int register = opcode & 7;
                if (!IsControlEa(mode, register))
                {
                    EnterException(4);
                    return "ILLEGAL JMP";
                }

                SetProgramCounterInternal(GetEffectiveAddress(mode, register, 4, true));
                return "JMP " + FormatEa(mode, register, 4);
            }

            if ((opcode & 0xFFC0) == 0x4E80)
            {
                int mode = (opcode >> 3) & 7;
                int register = opcode & 7;
                if (!IsControlEa(mode, register))
                {
                    EnterException(4);
                    return "ILLEGAL JSR";
                }

                uint target = GetEffectiveAddress(mode, register, 4, true);
                PushLong(_state.PC);
                SetProgramCounterInternal(target);
                return "JSR " + FormatEa(mode, register, 4);
            }

            EnterException(4);
            return "ILLEGAL $" + opcode.ToString("X4");
        }

        /// <summary>
        /// Runs Execute Moveq.
        /// </summary>
        private string ExecuteMoveq(ushort opcode)
        {
            int register = (opcode >> 9) & 7;
            _state.D[register] = (uint)(int)(sbyte)(opcode & 0xFF);
            SetLogicFlags(_state.D[register], 4);
            return "MOVEQ #" + ((sbyte)(opcode & 0xFF)).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",D" + register;
        }

        /// <summary>
        /// Runs Execute Move.
        /// </summary>
        private string ExecuteMove(ushort opcode)
        {
            int opSize = (opcode >> 12) & 3;
            int size = opSize == 1 ? 1 : (opSize == 2 ? 4 : 2);
            int destinationRegister = (opcode >> 9) & 7;
            int destinationMode = (opcode >> 6) & 7;
            int sourceMode = (opcode >> 3) & 7;
            int sourceRegister = opcode & 7;

            if ((sourceMode == 1 && size == 1) ||
                (destinationMode == 1 && size == 1) ||
                (destinationMode == 7 && destinationRegister >= 2))
            {
                EnterException(4);
                return "ILLEGAL MOVE";
            }

            uint value = ReadEa(sourceMode, sourceRegister, size);
            if (destinationMode == 1)
            {
                SetA(destinationRegister, size == 2 ? (uint)(int)(short)(value & 0xFFFF) : value);
                return "MOVEA." + SizeSuffix(size) + " " + FormatEa(sourceMode, sourceRegister, size) + ",A" + destinationRegister;
            }

            WriteEa(destinationMode, destinationRegister, size, value);
            SetLogicFlags(value, size);
            return "MOVE." + SizeSuffix(size) + " " + FormatEa(sourceMode, sourceRegister, size) + "," + FormatEa(destinationMode, destinationRegister, size);
        }

        /// <summary>
        /// Runs Execute Branch.
        /// </summary>
        private string ExecuteBranch(ushort opcode)
        {
            int condition = (opcode >> 8) & 0x0F;
            uint displacementBase = _state.PC;
            int displacement = GetBranchDisplacement(opcode);
            uint returnAddress = _state.PC;

            if (condition == 1)
            {
                PushLong(returnAddress);
                SetProgramCounterInternal((uint)((int)displacementBase + displacement));
                return "BSR";
            }

            if (EvaluateCondition(condition))
            {
                SetProgramCounterInternal((uint)((int)displacementBase + displacement));
            }

            return condition == 0 ? "BRA" : "B" + ConditionName(condition);
        }

        /// <summary>
        /// Runs Execute Dbcc.
        /// </summary>
        private string ExecuteDbcc(ushort opcode)
        {
            int condition = (opcode >> 8) & 0x0F;
            int register = opcode & 7;
            uint displacementBase = _state.PC;
            short displacement = (short)FetchWord();
            if (!EvaluateCondition(condition))
            {
                ushort counter = (ushort)((_state.D[register] - 1) & 0xFFFF);
                _state.D[register] = (_state.D[register] & 0xFFFF0000u) | counter;
                if (counter != 0xFFFF)
                {
                    SetProgramCounterInternal((uint)((int)displacementBase + displacement));
                }
            }

            return "DB" + ConditionName(condition) + " D" + register;
        }

        /// <summary>
        /// Runs Execute Scc.
        /// </summary>
        private string ExecuteScc(ushort opcode)
        {
            int condition = (opcode >> 8) & 0x0F;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsDataAlterableEa(mode, register))
            {
                EnterException(4);
                return "ILLEGAL Scc";
            }

            WriteEa(mode, register, 1, EvaluateCondition(condition) ? 0xFFu : 0x00u);
            return "S" + ConditionName(condition) + " " + FormatEa(mode, register, 1);
        }

        /// <summary>
        /// Runs Execute Addq Subq.
        /// </summary>
        private string ExecuteAddqSubq(ushort opcode)
        {
            int amount = (opcode >> 9) & 7;
            if (amount == 0)
            {
                amount = 8;
            }

            bool add = (opcode & 0x0100) == 0;
            int size = DecodeSize((opcode >> 6) & 3);
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;

            if (mode == 1)
            {
                if (size == 1)
                {
                    EnterException(4);
                    return "ILLEGAL " + (add ? "ADDQ.B" : "SUBQ.B") + " A" + register;
                }

                uint oldAddress = _state.A[register];
                SetA(register, (uint)((int)oldAddress + (add ? amount : -amount)));
                return (add ? "ADDQ" : "SUBQ") + " #" + amount + ",A" + register;
            }

            if (!IsDataAlterableEa(mode, register))
            {
                EnterException(4);
                return "ILLEGAL " + (add ? "ADDQ" : "SUBQ");
            }

            EaLocation target = ResolveEaLocation(mode, register, size, true);
            uint oldValue = ReadLocation(target);
            uint result = add
                ? AddValues(oldValue, (uint)amount, size, true)
                : SubtractValues(oldValue, (uint)amount, size, true);
            WriteLocation(target, result);
            return (add ? "ADDQ." : "SUBQ.") + SizeSuffix(size) + " #" + amount + "," + FormatEa(mode, register, size);
        }

        /// <summary>
        /// Runs Execute Add Sub Cmp.
        /// </summary>
        private string ExecuteAddSubCmp(ushort opcode, bool add, bool allowStore)
        {
            int dataRegister = (opcode >> 9) & 7;
            int operationMode = (opcode >> 6) & 7;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;

            if (allowStore && operationMode >= 4 && operationMode <= 6 && (mode == 0 || mode == 1))
            {
                return ExecuteAddxSubx(add, operationMode, dataRegister, mode, register);
            }

            if (operationMode == 3 || operationMode == 7)
            {
                int size = operationMode == 3 ? 2 : 4;
                uint source = ReadEa(mode, register, size);
                uint destination = _state.A[dataRegister];
                uint result = add
                    ? destination + SignExtend(source, size)
                    : destination - SignExtend(source, size);
                SetA(dataRegister, result);
                return (add ? "ADDA." : "SUBA.") + SizeSuffix(size);
            }

            if (operationMode <= 2)
            {
                int size = operationMode == 0 ? 1 : (operationMode == 1 ? 2 : 4);
                uint source = ReadEa(mode, register, size);
                uint destination = ReadDataRegister(dataRegister, size);
                uint result = add
                    ? AddValues(destination, source, size, true)
                    : SubtractValues(destination, source, size, true);
                if (allowStore)
                {
                    WriteDataRegister(dataRegister, size, result);
                }

                return (add ? "ADD." : "SUB.") + SizeSuffix(size) + " " + FormatEa(mode, register, size) + ",D" + dataRegister;
            }

            if (operationMode >= 4 && operationMode <= 6)
            {
                int size = operationMode == 4 ? 1 : (operationMode == 5 ? 2 : 4);
                if (!IsAlterableMemoryEa(mode, register))
                {
                    EnterException(4);
                    return "ILLEGAL " + (add ? "ADD" : "SUB");
                }

                uint source = ReadDataRegister(dataRegister, size);
                EaLocation target = ResolveEaLocation(mode, register, size, true);
                uint destination = ReadLocation(target);
                uint result = add
                    ? AddValues(destination, source, size, true)
                    : SubtractValues(destination, source, size, true);
                if (allowStore)
                {
                    WriteLocation(target, result);
                }

                return (add ? "ADD." : "SUB.") + SizeSuffix(size) + " D" + dataRegister + "," + FormatEa(mode, register, size);
            }

            return "DC.W $" + opcode.ToString("X4");
        }

        /// <summary>
        /// Runs Execute Addx Subx.
        /// </summary>
        private string ExecuteAddxSubx(bool add, int operationMode, int destinationRegister, int mode, int sourceRegister)
        {
            int size = operationMode == 4 ? 1 : (operationMode == 5 ? 2 : 4);
            uint source;
            uint destination;

            if (mode == 0)
            {
                source = ReadDataRegister(sourceRegister, size);
                destination = ReadDataRegister(destinationRegister, size);
                uint result = add
                    ? AddExtendValues(destination, source, size)
                    : SubtractExtendValues(destination, source, size);
                WriteDataRegister(destinationRegister, size, result);
                return (add ? "ADDX." : "SUBX.") + SizeSuffix(size) + " D" + sourceRegister + ",D" + destinationRegister;
            }

            uint sourceAddress = _state.A[sourceRegister] - AddressIncrement(sourceRegister, size);
            SetA(sourceRegister, sourceAddress);
            uint destinationAddress = _state.A[destinationRegister] - AddressIncrement(destinationRegister, size);
            SetA(destinationRegister, destinationAddress);
            source = ReadMemory(sourceAddress, size);
            destination = ReadMemory(destinationAddress, size);
            uint memoryResult = add
                ? AddExtendValues(destination, source, size)
                : SubtractExtendValues(destination, source, size);
            WriteMemory(destinationAddress, size, memoryResult);
            return (add ? "ADDX." : "SUBX.") + SizeSuffix(size) + " -(A" + sourceRegister + "),-(A" + destinationRegister + ")";
        }

        /// <summary>
        /// Runs Execute Or.
        /// </summary>
        private string ExecuteOr(ushort opcode)
        {
            int dataRegister = (opcode >> 9) & 7;
            int operationMode = (opcode >> 6) & 7;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;

            if (operationMode <= 2)
            {
                int size = operationMode == 0 ? 1 : (operationMode == 1 ? 2 : 4);
                uint source = ReadEa(mode, register, size);
                uint result = (ReadDataRegister(dataRegister, size) | source) & SizeMask(size);
                WriteDataRegister(dataRegister, size, result);
                SetLogicFlags(result, size);
                return "OR." + SizeSuffix(size) + " " + FormatEa(mode, register, size) + ",D" + dataRegister;
            }

            if (operationMode >= 4 && operationMode <= 6 && IsAlterableMemoryEa(mode, register))
            {
                int size = operationMode == 4 ? 1 : (operationMode == 5 ? 2 : 4);
                EaLocation target = ResolveEaLocation(mode, register, size, true);
                uint result = (ReadLocation(target) | ReadDataRegister(dataRegister, size)) & SizeMask(size);
                WriteLocation(target, result);
                SetLogicFlags(result, size);
                return "OR." + SizeSuffix(size) + " D" + dataRegister + "," + FormatEa(mode, register, size);
            }

            if (operationMode >= 4 && operationMode <= 6)
            {
                EnterException(4);
                return "ILLEGAL OR";
            }

            return "DC.W $" + opcode.ToString("X4");
        }

        /// <summary>
        /// Runs Execute Exg.
        /// </summary>
        private string ExecuteExg(ushort opcode)
        {
            int left = (opcode >> 9) & 7;
            int right = opcode & 7;
            uint temporary;

            if ((opcode & 0xF1F8) == 0xC140)
            {
                temporary = _state.D[left];
                _state.D[left] = _state.D[right];
                _state.D[right] = temporary;
                return "EXG D" + left + ",D" + right;
            }

            if ((opcode & 0xF1F8) == 0xC148)
            {
                temporary = _state.A[left];
                SetA(left, _state.A[right]);
                SetA(right, temporary);
                return "EXG A" + left + ",A" + right;
            }

            temporary = _state.D[left];
            _state.D[left] = _state.A[right];
            SetA(right, temporary);
            return "EXG D" + left + ",A" + right;
        }

        /// <summary>
        /// Runs Execute Multiply.
        /// </summary>
        private string ExecuteMultiply(ushort opcode)
        {
            int dataRegister = (opcode >> 9) & 7;
            bool signed = (opcode & 0x0100) != 0;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            uint source = ReadEa(mode, register, 2);
            uint result = signed
                ? (uint)((int)(short)(source & 0xFFFF) * (int)(short)(_state.D[dataRegister] & 0xFFFF))
                : (uint)((source & 0xFFFF) * (_state.D[dataRegister] & 0xFFFF));

            _state.D[dataRegister] = result;
            SetLogicFlags(result, 4);
            return (signed ? "MULS.W " : "MULU.W ") + FormatEa(mode, register, 2) + ",D" + dataRegister;
        }

        /// <summary>
        /// Runs Execute Divide.
        /// </summary>
        private string ExecuteDivide(ushort opcode)
        {
            int dataRegister = (opcode >> 9) & 7;
            bool signed = (opcode & 0x0100) != 0;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            uint source = ReadEa(mode, register, 2) & 0xFFFF;
            if (source == 0)
            {
                EnterException(5);
                return signed ? "DIVS.W zero" : "DIVU.W zero";
            }

            uint dividend = _state.D[dataRegister];
            uint quotient;
            uint remainder;
            bool overflow;
            if (signed)
            {
                long signedDividend = (int)dividend;
                long signedDivisor = (short)source;
                long signedQuotient = signedDividend / signedDivisor;
                long signedRemainder = signedDividend % signedDivisor;
                overflow = signedQuotient < short.MinValue || signedQuotient > short.MaxValue;
                quotient = (uint)(ushort)signedQuotient;
                remainder = (uint)(ushort)signedRemainder;
            }
            else
            {
                quotient = dividend / source;
                remainder = dividend % source;
                overflow = quotient > 0xFFFF;
            }

            ushort preserve = (ushort)(_state.SR & unchecked((ushort)~(FlagN | FlagZ | FlagV | FlagC)));
            if (overflow)
            {
                _state.SR = (ushort)(preserve | FlagV);
                return (signed ? "DIVS.W " : "DIVU.W ") + FormatEa(mode, register, 2) + ",D" + dataRegister;
            }

            uint result = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
            _state.D[dataRegister] = result;
            ushort flags = 0;
            if ((quotient & 0x8000) != 0)
            {
                flags |= FlagN;
            }

            if ((quotient & 0xFFFF) == 0)
            {
                flags |= FlagZ;
            }

            _state.SR = (ushort)(preserve | flags);
            return (signed ? "DIVS.W " : "DIVU.W ") + FormatEa(mode, register, 2) + ",D" + dataRegister;
        }

        /// <summary>
        /// Runs Execute And.
        /// </summary>
        private string ExecuteAnd(ushort opcode)
        {
            int dataRegister = (opcode >> 9) & 7;
            int operationMode = (opcode >> 6) & 7;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;

            if (operationMode <= 2)
            {
                int size = operationMode == 0 ? 1 : (operationMode == 1 ? 2 : 4);
                uint source = ReadEa(mode, register, size);
                uint result = (ReadDataRegister(dataRegister, size) & source) & SizeMask(size);
                WriteDataRegister(dataRegister, size, result);
                SetLogicFlags(result, size);
                return "AND." + SizeSuffix(size) + " " + FormatEa(mode, register, size) + ",D" + dataRegister;
            }

            if (operationMode >= 4 && operationMode <= 6 && IsAlterableMemoryEa(mode, register))
            {
                int size = operationMode == 4 ? 1 : (operationMode == 5 ? 2 : 4);
                EaLocation target = ResolveEaLocation(mode, register, size, true);
                uint result = (ReadLocation(target) & ReadDataRegister(dataRegister, size)) & SizeMask(size);
                WriteLocation(target, result);
                SetLogicFlags(result, size);
                return "AND." + SizeSuffix(size) + " D" + dataRegister + "," + FormatEa(mode, register, size);
            }

            if (operationMode >= 4 && operationMode <= 6)
            {
                EnterException(4);
                return "ILLEGAL AND";
            }

            return "DC.W $" + opcode.ToString("X4");
        }

        /// <summary>
        /// Runs Execute Cmp.
        /// </summary>
        private string ExecuteCmp(ushort opcode)
        {
            int dataRegister = (opcode >> 9) & 7;
            int operationMode = (opcode >> 6) & 7;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;

            if (operationMode == 3 || operationMode == 7)
            {
                int size = operationMode == 3 ? 2 : 4;
                uint source = ReadEa(mode, register, size);
                SubtractValues(_state.A[dataRegister], SignExtend(source, size), 4, false);
                return "CMPA." + SizeSuffix(size);
            }

            if (operationMode <= 2)
            {
                int size = operationMode == 0 ? 1 : (operationMode == 1 ? 2 : 4);
                uint source = ReadEa(mode, register, size);
                uint destination = ReadDataRegister(dataRegister, size);
                SubtractValues(destination, source, size, false);
                return "CMP." + SizeSuffix(size) + " " + FormatEa(mode, register, size) + ",D" + dataRegister;
            }

            if (operationMode >= 4 && operationMode <= 6 && mode == 1)
            {
                int size = operationMode == 4 ? 1 : (operationMode == 5 ? 2 : 4);
                uint source = ReadEa(3, register, size);
                uint destination = ReadEa(3, dataRegister, size);
                SubtractValues(destination, source, size, false);
                return "CMPM." + SizeSuffix(size);
            }

            if (operationMode >= 4 && operationMode <= 6 && IsDataAlterableEa(mode, register))
            {
                int size = operationMode == 4 ? 1 : (operationMode == 5 ? 2 : 4);
                EaLocation target = ResolveEaLocation(mode, register, size, true);
                uint result = (ReadLocation(target) ^ ReadDataRegister(dataRegister, size)) & SizeMask(size);
                WriteLocation(target, result);
                SetLogicFlags(result, size);
                return "EOR." + SizeSuffix(size) + " D" + dataRegister + "," + FormatEa(mode, register, size);
            }

            if (operationMode >= 4 && operationMode <= 6)
            {
                EnterException(4);
                return "ILLEGAL EOR";
            }

            return "DC.W $" + opcode.ToString("X4");
        }

        /// <summary>
        /// Attempts to Execute Immediate and reports whether it succeeded.
        /// </summary>
        private bool TryExecuteImmediate(ushort opcode, out string mnemonic)
        {
            mnemonic = null;

            if (opcode == 0x003C || opcode == 0x023C || opcode == 0x0A3C)
            {
                byte ccrImmediate = (byte)(FetchWord() & 0xFF);
                if (opcode == 0x003C)
                {
                    SetCcr((ushort)(_state.SR | ccrImmediate));
                    mnemonic = "ORI CCR";
                }
                else if (opcode == 0x023C)
                {
                    SetCcr((ushort)(_state.SR & ccrImmediate));
                    mnemonic = "ANDI CCR";
                }
                else
                {
                    SetCcr((ushort)(_state.SR ^ ccrImmediate));
                    mnemonic = "EORI CCR";
                }

                return true;
            }

            if (opcode == 0x007C || opcode == 0x027C || opcode == 0x0A7C)
            {
                if (TryEnterPrivilegeViolation(out string privilegeMnemonic))
                {
                    mnemonic = privilegeMnemonic;
                    return true;
                }

                ushort srImmediate = FetchWord();
                if (opcode == 0x007C)
                {
                    SetStatusRegister((ushort)(_state.SR | srImmediate));
                    mnemonic = "ORI SR";
                }
                else if (opcode == 0x027C)
                {
                    SetStatusRegister((ushort)(_state.SR & srImmediate));
                    mnemonic = "ANDI SR";
                }
                else
                {
                    SetStatusRegister((ushort)(_state.SR ^ srImmediate));
                    mnemonic = "EORI SR";
                }

                return true;
            }

            int operation = opcode & 0xFF00;
            if (operation != 0x0000 && operation != 0x0200 && operation != 0x0400 &&
                operation != 0x0600 && operation != 0x0A00 && operation != 0x0C00)
            {
                return false;
            }

            int sizeCode = (opcode >> 6) & 3;
            if (sizeCode == 3)
            {
                return false;
            }

            int size = DecodeSize(sizeCode);
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            uint immediate = FetchImmediate(size);
            if (!IsDataAlterableEa(mode, register))
            {
                EnterException(4);
                mnemonic = "ILLEGAL immediate EA";
                return true;
            }

            EaLocation target = ResolveEaLocation(mode, register, size, true);
            uint destination = ReadLocation(target);

            if (operation == 0x0C00)
            {
                SubtractValues(destination, immediate, size, false);
                mnemonic = "CMPI." + SizeSuffix(size);
                return true;
            }

            if (operation == 0x0400)
            {
                uint subtractResult = SubtractValues(destination, immediate, size, true);
                WriteLocation(target, subtractResult);
                mnemonic = "SUBI." + SizeSuffix(size);
                return true;
            }

            if (operation == 0x0600)
            {
                uint addResult = AddValues(destination, immediate, size, true);
                WriteLocation(target, addResult);
                mnemonic = "ADDI." + SizeSuffix(size);
                return true;
            }

            uint result;
            if (operation == 0x0000)
            {
                result = destination | immediate;
                mnemonic = "ORI." + SizeSuffix(size);
            }
            else if (operation == 0x0200)
            {
                result = destination & immediate;
                mnemonic = "ANDI." + SizeSuffix(size);
            }
            else
            {
                result = destination ^ immediate;
                mnemonic = "EORI." + SizeSuffix(size);
            }

            WriteLocation(target, result);
            SetLogicFlags(result, size);
            return true;
        }

        /// <summary>
        /// Attempts to Execute Immediate Bit Operation and reports whether it succeeded.
        /// </summary>
        private bool TryExecuteImmediateBitOperation(ushort opcode, out string mnemonic)
        {
            mnemonic = null;
            int operation = opcode & 0xFFC0;
            if (operation != 0x0800 && operation != 0x0840 && operation != 0x0880 && operation != 0x08C0)
            {
                return false;
            }

            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsValidBitOperationEa(mode, register, operation != 0x0800))
            {
                EnterException(4);
                mnemonic = "ILLEGAL bit";
                return true;
            }

            int bitNumber = (int)(FetchWord() & 0x1F);
            int size = mode == 0 ? 4 : 1;
            int effectiveBit = mode == 0 ? bitNumber : (bitNumber & 7);
            uint mask = 1u << effectiveBit;
            EaLocation target = ResolveEaLocation(mode, register, size, operation != 0x0800);
            uint value = ReadLocation(target);
            bool bitWasSet = (value & mask) != 0;
            if (bitWasSet)
            {
                _state.SR &= unchecked((ushort)~FlagZ);
            }
            else
            {
                _state.SR |= FlagZ;
            }

            if (operation == 0x0840)
            {
                WriteLocation(target, value ^ mask);
                mnemonic = "BCHG";
            }
            else if (operation == 0x0880)
            {
                WriteLocation(target, value & ~mask);
                mnemonic = "BCLR";
            }
            else if (operation == 0x08C0)
            {
                WriteLocation(target, value | mask);
                mnemonic = "BSET";
            }
            else
            {
                mnemonic = "BTST";
            }

            return true;
        }

        /// <summary>
        /// Attempts to Execute Dynamic Bit Operation and reports whether it succeeded.
        /// </summary>
        private bool TryExecuteDynamicBitOperation(ushort opcode, out string mnemonic)
        {
            mnemonic = null;
            int operation = opcode & 0xF1C0;
            if (operation != 0x0100 && operation != 0x0140 && operation != 0x0180 && operation != 0x01C0)
            {
                return false;
            }

            int bitRegister = (opcode >> 9) & 7;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsValidBitOperationEa(mode, register, operation != 0x0100))
            {
                EnterException(4);
                mnemonic = "ILLEGAL bit";
                return true;
            }

            int size = mode == 0 ? 4 : 1;
            int bitNumber = (int)_state.D[bitRegister];
            int effectiveBit = mode == 0 ? (bitNumber & 31) : (bitNumber & 7);
            uint mask = 1u << effectiveBit;
            EaLocation target = ResolveEaLocation(mode, register, size, operation != 0x0100);
            uint value = ReadLocation(target);
            bool bitWasSet = (value & mask) != 0;
            if (bitWasSet)
            {
                _state.SR &= unchecked((ushort)~FlagZ);
            }
            else
            {
                _state.SR |= FlagZ;
            }

            if (operation == 0x0140)
            {
                WriteLocation(target, value ^ mask);
                mnemonic = "BCHG";
            }
            else if (operation == 0x0180)
            {
                WriteLocation(target, value & ~mask);
                mnemonic = "BCLR";
            }
            else if (operation == 0x01C0)
            {
                WriteLocation(target, value | mask);
                mnemonic = "BSET";
            }
            else
            {
                mnemonic = "BTST";
            }

            return true;
        }

        /// <summary>
        /// Runs Execute Clr.
        /// </summary>
        private string ExecuteClr(ushort opcode)
        {
            int size = DecodeSize((opcode >> 6) & 3);
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsDataAlterableEa(mode, register))
            {
                EnterException(4);
                return "ILLEGAL CLR";
            }

            WriteEa(mode, register, size, 0);
            SetLogicFlags(0, size);
            return "CLR." + SizeSuffix(size) + " " + FormatEa(mode, register, size);
        }

        /// <summary>
        /// Runs Execute Tst.
        /// </summary>
        private string ExecuteTst(ushort opcode)
        {
            int size = DecodeSize((opcode >> 6) & 3);
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsDataAlterableEa(mode, register))
            {
                EnterException(4);
                return "ILLEGAL TST";
            }

            uint value = ReadEa(mode, register, size);
            SetLogicFlags(value, size);
            return "TST." + SizeSuffix(size) + " " + FormatEa(mode, register, size);
        }

        /// <summary>
        /// Executes TAS by testing the original byte value and then setting bit 7 in the target.
        /// </summary>
        private string ExecuteTas(ushort opcode)
        {
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsDataAlterableEa(mode, register))
            {
                EnterException(4);
                return "ILLEGAL TAS";
            }

            EaLocation target = ResolveEaLocation(mode, register, 1, true);
            uint value = ReadLocation(target) & 0xFFu;
            SetLogicFlags(value, 1);
            WriteLocation(target, value | 0x80u);
            return "TAS " + FormatEa(mode, register, 1);
        }

        /// <summary>
        /// Runs Execute Negx.
        /// </summary>
        private string ExecuteNegx(ushort opcode)
        {
            int sizeCode = (opcode >> 6) & 3;
            if (sizeCode == 3)
            {
                return "DC.W $" + opcode.ToString("X4");
            }

            int size = DecodeSize(sizeCode);
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsDataAlterableEa(mode, register))
            {
                EnterException(4);
                return "ILLEGAL NEGX";
            }

            bool extend = (_state.SR & FlagX) != 0;
            EaLocation target = ResolveEaLocation(mode, register, size, true);
            uint value = ReadLocation(target) & SizeMask(size);
            uint result = (0u - value - (extend ? 1u : 0u)) & SizeMask(size);
            WriteLocation(target, result);
            SetNegxFlags(value, result, size, extend);
            return "NEGX." + SizeSuffix(size) + " " + FormatEa(mode, register, size);
        }

        /// <summary>
        /// Runs Execute Neg.
        /// </summary>
        private string ExecuteNeg(ushort opcode)
        {
            int sizeCode = (opcode >> 6) & 3;
            if (sizeCode == 3)
            {
                return "DC.W $" + opcode.ToString("X4");
            }

            int size = DecodeSize(sizeCode);
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsDataAlterableEa(mode, register))
            {
                EnterException(4);
                return "ILLEGAL NEG";
            }

            EaLocation target = ResolveEaLocation(mode, register, size, true);
            uint value = ReadLocation(target);
            uint result = SubtractValues(0, value, size, true);
            WriteLocation(target, result);
            return "NEG." + SizeSuffix(size) + " " + FormatEa(mode, register, size);
        }

        /// <summary>
        /// Runs Execute Not.
        /// </summary>
        private string ExecuteNot(ushort opcode)
        {
            int size = DecodeSize((opcode >> 6) & 3);
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsDataAlterableEa(mode, register))
            {
                EnterException(4);
                return "ILLEGAL NOT";
            }

            EaLocation target = ResolveEaLocation(mode, register, size, true);
            uint result = (~ReadLocation(target)) & SizeMask(size);
            WriteLocation(target, result);
            SetLogicFlags(result, size);
            return "NOT." + SizeSuffix(size) + " " + FormatEa(mode, register, size);
        }

        /// <summary>
        /// Runs Execute Shift Rotate.
        /// </summary>
        private string ExecuteShiftRotate(ushort opcode)
        {
            if ((opcode & 0x00C0) == 0x00C0)
            {
                return ExecuteMemoryShiftRotate(opcode);
            }

            int size = DecodeSize((opcode >> 6) & 3);
            int register = opcode & 7;
            int countRegister = (opcode >> 9) & 7;
            int count = countRegister;
            if ((opcode & 0x0020) != 0)
            {
                count = (int)(_state.D[countRegister] & 0x3F);
            }
            else if (count == 0)
            {
                count = 8;
            }

            bool left = (opcode & 0x0100) != 0;
            int kind = (opcode >> 3) & 3;
            uint value = ReadDataRegister(register, size);
            if (count == 0)
            {
                return ShiftRotateName(kind, left) + "." + SizeSuffix(size) + " " + ((opcode & 0x0020) != 0 ? "D" + countRegister : "#" + count) + ",D" + register;
            }

            uint result = ShiftRotateValue(value, size, count, left, kind, out bool carry, out bool overflow);
            WriteDataRegister(register, size, result);
            SetShiftRotateFlags(result, size, carry, overflow, count != 0 && kind != 3);

            string name = ShiftRotateName(kind, left);
            string countText = (opcode & 0x0020) != 0 ? "D" + countRegister : "#" + count;
            return name + "." + SizeSuffix(size) + " " + countText + ",D" + register;
        }

        /// <summary>
        /// Runs Shift Rotate Name.
        /// </summary>
        private static string ShiftRotateName(int kind, bool left)
        {
            if (kind == 0)
            {
                return left ? "ASL" : "ASR";
            }

            if (kind == 1)
            {
                return left ? "LSL" : "LSR";
            }

            if (kind == 2)
            {
                return left ? "ROXL" : "ROXR";
            }

            return left ? "ROL" : "ROR";
        }

        /// <summary>
        /// Runs Execute Memory Shift Rotate.
        /// </summary>
        private string ExecuteMemoryShiftRotate(ushort opcode)
        {
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsAlterableMemoryEa(mode, register))
            {
                EnterException(4);
                return "ILLEGAL shift";
            }

            bool left = (opcode & 0x0100) != 0;
            int kind = (opcode >> 9) & 3;
            EaLocation target = ResolveEaLocation(mode, register, 2, true);
            uint value = ReadLocation(target);
            uint result = ShiftRotateValue(value, 2, 1, left, kind, out bool carry, out bool overflow);
            WriteLocation(target, result);
            SetShiftRotateFlags(result, 2, carry, overflow, kind != 3);

            string name;
            if (kind == 0)
            {
                name = left ? "ASL" : "ASR";
            }
            else if (kind == 1)
            {
                name = left ? "LSL" : "LSR";
            }
            else if (kind == 2)
            {
                name = left ? "ROXL" : "ROXR";
            }
            else
            {
                name = left ? "ROL" : "ROR";
            }

            return name + ".W " + FormatEa(mode, register, 2);
        }

        /// <summary>
        /// Runs Shift Rotate Value.
        /// </summary>
        private uint ShiftRotateValue(uint value, int size, int count, bool left, int kind, out bool carry, out bool overflow)
        {
            uint mask = SizeMask(size);
            uint sign = SignBit(size);
            uint result = value & mask;
            bool extend = (_state.SR & FlagX) != 0;
            carry = false;
            overflow = false;

            for (int index = 0; index < count; index++)
            {
                if (kind == 0)
                {
                    if (left)
                    {
                        bool oldSign = (result & sign) != 0;
                        carry = oldSign;
                        result = (result << 1) & mask;
                        overflow |= oldSign != ((result & sign) != 0);
                    }
                    else
                    {
                        carry = (result & 1) != 0;
                        result = ((result >> 1) | (result & sign)) & mask;
                    }
                }
                else if (kind == 1)
                {
                    if (left)
                    {
                        carry = (result & sign) != 0;
                        result = (result << 1) & mask;
                    }
                    else
                    {
                        carry = (result & 1) != 0;
                        result >>= 1;
                    }
                }
                else if (kind == 2)
                {
                    if (left)
                    {
                        bool oldCarry = (result & sign) != 0;
                        result = ((result << 1) & mask) | (extend ? 1u : 0u);
                        carry = oldCarry;
                        extend = oldCarry;
                    }
                    else
                    {
                        bool oldCarry = (result & 1) != 0;
                        result = (result >> 1) | (extend ? sign : 0u);
                        carry = oldCarry;
                        extend = oldCarry;
                    }
                }
                else
                {
                    if (left)
                    {
                        carry = (result & sign) != 0;
                        result = ((result << 1) & mask) | (carry ? 1u : 0u);
                    }
                    else
                    {
                        carry = (result & 1) != 0;
                        result = (result >> 1) | (carry ? sign : 0u);
                    }
                }
            }

            return result & mask;
        }

        /// <summary>
        /// Updates the Shift Rotate Flags.
        /// </summary>
        private void SetShiftRotateFlags(uint result, int size, bool carry, bool overflow, bool affectExtend)
        {
            SetLogicFlags(result, size);
            if (overflow)
            {
                _state.SR |= FlagV;
            }

            if (carry)
            {
                _state.SR |= FlagC;
                if (affectExtend)
                {
                    _state.SR |= FlagX;
                }
            }
            else
            {
                _state.SR &= unchecked((ushort)~FlagC);
                if (affectExtend)
                {
                    _state.SR &= unchecked((ushort)~FlagX);
                }
            }
        }

        /// <summary>
        /// Runs Execute Movem.
        /// </summary>
        private string ExecuteMovem(ushort opcode)
        {
            bool memoryToRegisters = (opcode & 0x0400) != 0;
            int size = (opcode & 0x0040) != 0 ? 4 : 2;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (!IsValidMovemEa(mode, register, memoryToRegisters))
            {
                EnterException(4);
                return "ILLEGAL MOVEM";
            }

            ushort mask = FetchWord();

            if (memoryToRegisters)
            {
                uint address = GetEffectiveAddress(mode, register, size, false);
                for (int index = 0; index < 16; index++)
                {
                    if ((mask & (1 << index)) == 0)
                    {
                        continue;
                    }

                    uint value = size == 4 ? ReadLong(address) : (uint)(int)(short)ReadWord(address);
                    if (index < 8)
                    {
                        _state.D[index] = value;
                    }
                    else
                    {
                        SetA(index - 8, value);
                    }

                    address = (address + (uint)size) & AddressMask;
                }

                if (mode == 3)
                {
                    SetA(register, address);
                }

                return "MOVEM." + SizeSuffix(size) + " <ea>,regs";
            }

            if (mode == 4)
            {
                uint address = _state.A[register];
                for (int index = 0; index < 16; index++)
                {
                    if ((mask & (1 << index)) == 0)
                    {
                        continue;
                    }

                    int reversed = 15 - index;
                    address = (address - (uint)size) & AddressMask;
                    WriteMovemRegister(address, reversed, size);
                }

                SetA(register, address);
            }
            else
            {
                uint address = GetEffectiveAddress(mode, register, size, true);
                for (int index = 0; index < 16; index++)
                {
                    if ((mask & (1 << index)) == 0)
                    {
                        continue;
                    }

                    WriteMovemRegister(address, index, size);
                    address = (address + (uint)size) & AddressMask;
                }
            }

            return "MOVEM." + SizeSuffix(size) + " regs,<ea>";
        }

        /// <summary>
        /// Runs Execute Movep.
        /// </summary>
        private string ExecuteMovep(ushort opcode)
        {
            int dataRegister = (opcode >> 9) & 7;
            int addressRegister = opcode & 7;
            bool dataToMemory = (opcode & 0x0080) != 0;
            int size = (opcode & 0x0040) != 0 ? 4 : 2;
            uint address = (uint)((int)_state.A[addressRegister] + (short)FetchWord()) & AddressMask;

            if (dataToMemory)
            {
                uint value = _state.D[dataRegister];
                if (size == 4)
                {
                    WriteByte(address, (byte)(value >> 24));
                    WriteByte((address + 2u) & AddressMask, (byte)(value >> 16));
                    WriteByte((address + 4u) & AddressMask, (byte)(value >> 8));
                    WriteByte((address + 6u) & AddressMask, (byte)value);
                }
                else
                {
                    WriteByte(address, (byte)(value >> 8));
                    WriteByte((address + 2u) & AddressMask, (byte)value);
                }

                return "MOVEP." + SizeSuffix(size) + " D" + dataRegister + ",d16(A" + addressRegister + ")";
            }

            uint result;
            if (size == 4)
            {
                result =
                    ((uint)ReadByte(address) << 24) |
                    ((uint)ReadByte((address + 2u) & AddressMask) << 16) |
                    ((uint)ReadByte((address + 4u) & AddressMask) << 8) |
                    ReadByte((address + 6u) & AddressMask);
                _state.D[dataRegister] = result;
            }
            else
            {
                result =
                    ((uint)ReadByte(address) << 8) |
                    ReadByte((address + 2u) & AddressMask);
                _state.D[dataRegister] = (_state.D[dataRegister] & 0xFFFF0000u) | result;
            }

            return "MOVEP." + SizeSuffix(size) + " d16(A" + addressRegister + "),D" + dataRegister;
        }

        /// <summary>
        /// Writes one MOVEM memory-to-register transfer into the selected 68000 register.
        /// </summary>
        private void WriteMovemRegister(uint address, int index, int size)
        {
            uint value = index < 8 ? _state.D[index] : _state.A[index - 8];
            if (size == 4)
            {
                WriteLong(address, value);
            }
            else
            {
                WriteWord(address, (ushort)value);
            }
        }

        /// <summary>
        /// Runs Fetch Word.
        /// </summary>
        private ushort FetchWord()
        {
            uint address = _state.PC & AddressMask;
            ushort value;
            if (_prefetchValid && _prefetchAddress == address)
            {
                value = _prefetchWord;
                M68kTraceAccessType accessType = _opcodeFetchInProgress ? M68kTraceAccessType.OpcodeFetch : M68kTraceAccessType.Read;
                RecordBusMicroOps(accessType, address, 2, value, false, true);
            }
            else
            {
                value = ReadWord(address);
            }

            _state.PC = (_state.PC + 2) & AddressMask;
            PrefetchSequentialWord();
            return value;
        }

        /// <summary>
        /// Captures the next sequential instruction word before deferred writes retire.
        /// </summary>
        private void PrefetchSequentialWord()
        {
            uint address = _state.PC & AddressMask;
            _prefetchAddress = address;
            if (_bus is IM68kPeekableBus peekableBus)
            {
                _prefetchWord = peekableBus.PeekWord(address);
            }
            else
            {
                _prefetchWord = _bus.ReadWord(address);
            }

            _prefetchValid = true;
        }

        /// <summary>
        /// Drops prefetched fall-through words after an explicit control-flow change.
        /// </summary>
        private void InvalidatePrefetch()
        {
            _prefetchValid = false;
            _prefetchAddress = 0;
            _prefetchWord = 0;
        }

        /// <summary>
        /// Updates the PC for non-sequential control flow and drops the fall-through prefetch word.
        /// </summary>
        private void SetProgramCounterInternal(uint pc)
        {
            _state.PC = pc & AddressMask;
            InvalidatePrefetch();
        }

        /// <summary>
        /// Runs Fetch Long.
        /// </summary>
        private uint FetchLong()
        {
            uint high = FetchWord();
            uint low = FetchWord();
            return (high << 16) | low;
        }

        /// <summary>
        /// Runs Fetch Immediate.
        /// </summary>
        private uint FetchImmediate(int size)
        {
            if (size == 4)
            {
                return FetchLong();
            }

            ushort word = FetchWord();
            return size == 1 ? (uint)(word & 0xFF) : word;
        }

        /// <summary>
        /// Returns whether Control Ea is true.
        /// </summary>
        private static bool IsControlEa(int mode, int register)
        {
            return mode == 2 ||
                mode == 5 ||
                mode == 6 ||
                (mode == 7 && register <= 3);
        }

        /// <summary>
        /// Returns whether Alterable Memory Ea is true.
        /// </summary>
        private static bool IsAlterableMemoryEa(int mode, int register)
        {
            return mode == 2 ||
                mode == 3 ||
                mode == 4 ||
                mode == 5 ||
                mode == 6 ||
                (mode == 7 && register <= 1);
        }

        /// <summary>
        /// Returns whether Data Alterable Ea is true.
        /// </summary>
        private static bool IsDataAlterableEa(int mode, int register)
        {
            return mode == 0 || IsAlterableMemoryEa(mode, register);
        }

        /// <summary>
        /// Returns whether Memory Ea is true.
        /// </summary>
        private static bool IsMemoryEa(int mode, int register)
        {
            return IsControlEa(mode, register) || mode == 3 || mode == 4;
        }

        /// <summary>
        /// Returns whether Valid Bit Operation Ea is true.
        /// </summary>
        private static bool IsValidBitOperationEa(int mode, int register, bool forWrite)
        {
            if (mode == 0)
            {
                return true;
            }

            return forWrite ? IsAlterableMemoryEa(mode, register) : IsMemoryEa(mode, register);
        }

        /// <summary>
        /// Returns whether Valid Movem Ea is true.
        /// </summary>
        private static bool IsValidMovemEa(int mode, int register, bool memoryToRegisters)
        {
            if (memoryToRegisters)
            {
                return mode == 3 || IsControlEa(mode, register);
            }

            return mode == 4 || (mode != 3 && IsAlterableMemoryEa(mode, register));
        }

        /// <summary>
        /// Reads an effective-address operand using 68000 addressing mode rules.
        /// </summary>
        private uint ReadEa(int mode, int register, int size)
        {
            if (mode == 0)
            {
                return ReadDataRegister(register, size);
            }

            if (mode == 1)
            {
                return size == 1 ? _state.A[register] & 0xFF : (size == 2 ? _state.A[register] & 0xFFFF : _state.A[register]);
            }

            if (mode == 7 && register == 4)
            {
                return FetchImmediate(size);
            }

            uint address = GetEffectiveAddress(mode, register, size, false);
            return ReadMemory(address, size);
        }

        /// <summary>
        /// Runs Resolve Ea Location.
        /// </summary>
        private EaLocation ResolveEaLocation(int mode, int register, int size, bool forWrite)
        {
            if (mode == 7 && register == 4)
            {
                throw new InvalidOperationException("Immediate effective address is read-only.");
            }

            EaLocation location = new EaLocation
            {
                Mode = mode,
                Register = register,
                Size = size,
                Address = 0
            };

            if (mode != 0 && mode != 1)
            {
                location.Address = GetEffectiveAddress(mode, register, size, forWrite);
            }

            return location;
        }

        /// <summary>
        /// Reads the value represented by a resolved effective-address location.
        /// </summary>
        private uint ReadLocation(EaLocation location)
        {
            if (location.Mode == 0)
            {
                return ReadDataRegister(location.Register, location.Size);
            }

            if (location.Mode == 1)
            {
                return location.Size == 1
                    ? _state.A[location.Register] & 0xFF
                    : (location.Size == 2 ? _state.A[location.Register] & 0xFFFF : _state.A[location.Register]);
            }

            uint value = ReadMemory(location.Address, location.Size);
            _lastReadLocation = location;
            _lastReadLocationValid = true;
            return value;
        }

        /// <summary>
        /// Writes a value to a resolved effective-address location.
        /// </summary>
        private void WriteLocation(EaLocation location, uint value)
        {
            if (location.Mode == 0)
            {
                WriteDataRegister(location.Register, location.Size, value);
                return;
            }

            if (location.Mode == 1)
            {
                if (location.Size == 4)
                {
                    SetA(location.Register, value);
                }
                else if (location.Size == 2)
                {
                    SetA(location.Register, SignExtend(value, 2));
                }

                return;
            }

            bool readModifyWrite = IsSameMemoryLocation(_lastReadLocation, location);
            bool previousReadModifyWrite = _readModifyWriteInProgress;
            _readModifyWriteInProgress = readModifyWrite;
            try
            {
                WriteMemory(location.Address, location.Size, value);
            }
            finally
            {
                _readModifyWriteInProgress = previousReadModifyWrite;
                _lastReadLocationValid = false;
            }
        }

        /// <summary>
        /// Returns whether the supplied effective-address locations describe the same memory operand.
        /// </summary>
        private bool IsSameMemoryLocation(EaLocation left, EaLocation right)
        {
            return _lastReadLocationValid &&
                left.Mode != 0 &&
                left.Mode != 1 &&
                right.Mode != 0 &&
                right.Mode != 1 &&
                left.Size == right.Size &&
                left.Address == right.Address;
        }

        /// <summary>
        /// Writes an effective-address operand using 68000 addressing mode rules.
        /// </summary>
        private void WriteEa(int mode, int register, int size, uint value)
        {
            if (mode == 0)
            {
                WriteDataRegister(register, size, value);
                return;
            }

            if (mode == 1)
            {
                if (size == 4)
                {
                    SetA(register, value);
                }
                else if (size == 2)
                {
                    SetA(register, SignExtend(value, 2));
                }

                return;
            }

            uint address = GetEffectiveAddress(mode, register, size, true);
            WriteMemory(address, size, value);
        }

        /// <summary>
        /// Returns the Effective Address.
        /// </summary>
        private uint GetEffectiveAddress(int mode, int register, int size, bool forWrite)
        {
            switch (mode)
            {
                case 2:
                    return _state.A[register] & AddressMask;
                case 3:
                    {
                        uint address = _state.A[register] & AddressMask;
                        SetA(register, _state.A[register] + AddressIncrement(register, size));
                        return address;
                    }
                case 4:
                    {
                        uint address = _state.A[register] - AddressIncrement(register, size);
                        SetA(register, address);
                        return address & AddressMask;
                    }
                case 5:
                    return (uint)((int)_state.A[register] + (short)FetchWord()) & AddressMask;
                case 6:
                    return (uint)((int)_state.A[register] + DecodeIndexExtension()) & AddressMask;
                case 7:
                    switch (register)
                    {
                        case 0:
                            return (uint)(int)(short)FetchWord() & AddressMask;
                        case 1:
                            return FetchLong() & AddressMask;
                        case 2:
                            {
                                uint extensionPc = _state.PC;
                                return (uint)((int)extensionPc + (short)FetchWord()) & AddressMask;
                            }
                        case 3:
                            {
                                uint extensionPc = _state.PC;
                                return (uint)((int)extensionPc + DecodeIndexExtension()) & AddressMask;
                            }
                        default:
                            if (!forWrite && register == 4)
                            {
                                return _state.PC;
                            }

                            throw new InvalidOperationException("Invalid effective address.");
                    }
                default:
                    throw new InvalidOperationException("Invalid effective address.");
            }
        }

        /// <summary>
        /// Decodes Index Extension from Amiga-compatible data.
        /// </summary>
        private int DecodeIndexExtension()
        {
            ushort extension = FetchWord();
            int displacement = (sbyte)(extension & 0xFF);
            int register = (extension >> 12) & 7;
            bool addressRegister = (extension & 0x8000) != 0;
            bool longSize = (extension & 0x0800) != 0;
            uint raw = addressRegister ? _state.A[register] : _state.D[register];
            int index = longSize ? (int)raw : (short)(raw & 0xFFFF);
            return index + displacement;
        }

        /// <summary>
        /// Reads a byte, word, or longword through the CPU bus with alignment checks.
        /// </summary>
        private uint ReadMemory(uint address, int size)
        {
            if (size == 1)
            {
                return ReadByte(address & AddressMask);
            }

            CheckAlignedAccess(address, size, false);
            if (size == 2)
            {
                return ReadWord(address & AddressMask);
            }

            return ReadLong(address & AddressMask);
        }

        /// <summary>
        /// Reads one byte through the CPU bus and records one operand-read micro-operation.
        /// </summary>
        private byte ReadByte(uint address)
        {
            if (_deferBusSideEffects && _bus is IM68kPeekableBus peekableBus)
            {
                byte value = (byte)ReadWithPendingWriteForwarding(peekableBus, address & AddressMask, 1);
                RecordBusMicroOps(M68kTraceAccessType.Read, address, 1, value, false, true);
                return value;
            }

            RecordBusMicroOps(M68kTraceAccessType.Read, address, 1, 0, false, true);
            return _bus.ReadByte(address & AddressMask);
        }

        /// <summary>
        /// Reads an aligned 16-bit word through the CPU bus.
        /// </summary>
        private ushort ReadWord(uint address)
        {
            CheckAlignedAccess(address, 2, false);
            M68kTraceAccessType accessType = _opcodeFetchInProgress ? M68kTraceAccessType.OpcodeFetch : M68kTraceAccessType.Read;
            if (_deferBusSideEffects && _bus is IM68kPeekableBus peekableBus)
            {
                ushort value = (ushort)ReadWithPendingWriteForwarding(peekableBus, address & AddressMask, 2);
                RecordBusMicroOps(accessType, address, 2, value, false, true);
                return value;
            }

            RecordBusMicroOps(accessType, address, 2, 0, false, true);
            return _bus.ReadWord(address & AddressMask);
        }

        /// <summary>
        /// Reads an aligned 32-bit longword through the CPU bus.
        /// </summary>
        private uint ReadLong(uint address)
        {
            CheckAlignedAccess(address, 4, false);
            if (_deferBusSideEffects && _bus is IM68kPeekableBus peekableBus)
            {
                uint value = ReadWithPendingWriteForwarding(peekableBus, address & AddressMask, 4);
                RecordBusMicroOps(M68kTraceAccessType.Read, address, 4, value, false, true);
                return value;
            }

            RecordBusMicroOps(M68kTraceAccessType.Read, address, 4, 0, false, true);
            return _bus.ReadLong(address & AddressMask);
        }

        /// <summary>
        /// Writes a byte, word, or longword through the CPU bus with alignment checks.
        /// </summary>
        private void WriteMemory(uint address, int size, uint value)
        {
            if (size == 1)
            {
                WriteByte(address & AddressMask, (byte)value);
            }
            else if (size == 2)
            {
                CheckAlignedAccess(address, size, true);
                WriteWord(address & AddressMask, (ushort)value);
            }
            else
            {
                CheckAlignedAccess(address, size, true);
                WriteLong(address & AddressMask, value);
            }
        }

        /// <summary>
        /// Writes one byte through the CPU bus and records one operand-write micro-operation.
        /// </summary>
        private void WriteByte(uint address, byte value)
        {
            RecordBusMicroOps(M68kTraceAccessType.Write, address, 1, value, true, true);
            if (_deferBusSideEffects)
            {
                return;
            }

            _bus.WriteByte(address & AddressMask, value);
        }

        /// <summary>
        /// Writes an aligned 16-bit word through the CPU bus.
        /// </summary>
        private void WriteWord(uint address, ushort value)
        {
            CheckAlignedAccess(address, 2, true);
            RecordBusMicroOps(M68kTraceAccessType.Write, address, 2, value, true, true);
            if (_deferBusSideEffects)
            {
                return;
            }

            _bus.WriteWord(address & AddressMask, value);
        }

        /// <summary>
        /// Writes an aligned 32-bit longword through the CPU bus.
        /// </summary>
        private void WriteLong(uint address, uint value)
        {
            CheckAlignedAccess(address, 4, true);
            RecordBusMicroOps(M68kTraceAccessType.Write, address, 4, value, true, true);
            if (_deferBusSideEffects)
            {
                return;
            }

            _bus.WriteLong(address & AddressMask, value);
        }

        /// <summary>
        /// Records how many 68000 bus micro-operations are needed for one logical access.
        /// </summary>
        private void RecordBusMicroOps(M68kTraceAccessType accessType, uint address, int size, uint value, bool write, bool queueDeferredSideEffect)
        {
            int microOps = size >= 4 ? 2 : 1;
            _currentInstructionBusMicroOps += microOps;
            if (accessType == M68kTraceAccessType.OpcodeFetch)
            {
                _currentOpcodeFetchMicroOps += microOps;
            }
            else if (accessType == M68kTraceAccessType.Write)
            {
                _currentOperandWriteMicroOps += microOps;
                if (_readModifyWriteInProgress)
                {
                    _currentReadModifyWriteMicroOps += microOps;
                }
            }
            else
            {
                _currentOperandReadMicroOps += microOps;
            }

            if (_deferBusSideEffects && queueDeferredSideEffect)
            {
                EnqueueBusOperation(accessType, address, size, value, write);
            }
        }

        /// <summary>
        /// Returns whether a CPU read targets the CIA interrupt-control register, which clears on read.
        /// </summary>
        private static bool IsCiaIcrRead(uint address, int size)
        {
            uint normalized = address & AddressMask;
            if (size > 1)
            {
                for (int offset = 0; offset < size; offset++)
                {
                    if (IsCiaIcrRead(normalized + (uint)offset, 1))
                    {
                        return true;
                    }
                }

                return false;
            }

            bool ciaB = normalized >= MemoryMap.CiaBStart &&
                normalized <= MemoryMap.CiaBEnd &&
                (normalized & 1u) == 0;
            bool ciaA = normalized >= MemoryMap.CiaAStart &&
                normalized <= MemoryMap.CiaAEnd &&
                (normalized & 1u) != 0;
            return (ciaA || ciaB) && (((normalized >> 8) & 0x0Fu) == 13u);
        }

        /// <summary>
        /// Queues one or more ordered bus micro-operations for later retirement on real bus slots.
        /// </summary>
        private void EnqueueBusOperation(M68kTraceAccessType accessType, uint address, int size, uint value, bool write)
        {
            address &= AddressMask;
            if (size >= 4)
            {
                _pendingBusOperations.Enqueue(new PendingBusOperation
                {
                    Write = write,
                    AccessType = accessType,
                    Address = address,
                    Size = 2,
                    Value = (value >> 16) & 0xFFFF
                });
                _pendingBusOperations.Enqueue(new PendingBusOperation
                {
                    Write = write,
                    AccessType = accessType,
                    Address = (address + 2u) & AddressMask,
                    Size = 2,
                    Value = value & 0xFFFF
                });
                return;
            }

            _pendingBusOperations.Enqueue(new PendingBusOperation
            {
                Write = write,
                AccessType = accessType,
                Address = address,
                Size = size,
                Value = value & (size == 1 ? 0xFFu : 0xFFFFu)
            });
        }

        /// <summary>
        /// Reads through pending CPU writes so exception vector fetches see earlier ordered bus stores.
        /// </summary>
        private uint ReadWithPendingWriteForwarding(IM68kPeekableBus peekableBus, uint address, int size)
        {
            address &= AddressMask;
            uint value;
            if (size == 1)
            {
                value = peekableBus.PeekByte(address);
            }
            else if (size == 2)
            {
                value = peekableBus.PeekWord(address);
            }
            else
            {
                value = peekableBus.PeekLong(address);
            }

            foreach (PendingBusOperation operation in _pendingBusOperations)
            {
                if (operation.Write)
                {
                    ApplyPendingWriteForwarding(address, size, operation, ref value);
                }
            }

            return value;
        }

        /// <summary>
        /// Merges one pending big-endian write into an overlapping CPU read value.
        /// </summary>
        private static void ApplyPendingWriteForwarding(uint readAddress, int readSize, PendingBusOperation operation, ref uint readValue)
        {
            for (int writeOffset = 0; writeOffset < operation.Size; writeOffset++)
            {
                uint byteAddress = (operation.Address + (uint)writeOffset) & AddressMask;
                uint delta = (byteAddress - readAddress) & AddressMask;
                if (delta >= (uint)readSize)
                {
                    continue;
                }

                int writeShift = (operation.Size - 1 - writeOffset) * 8;
                uint byteValue = (operation.Value >> writeShift) & 0xFFu;
                int readShift = (readSize - 1 - (int)delta) * 8;
                readValue = (readValue & ~(0xFFu << readShift)) | (byteValue << readShift);
            }
        }

        /// <summary>
        /// Retires the next pending bus micro-operation on the current CPU-available slot.
        /// </summary>
        private void RetireNextPendingBusOperation()
        {
            if (_pendingBusOperations.Count == 0)
            {
                return;
            }

            PendingBusOperation operation = _pendingBusOperations.Dequeue();
            if (!operation.Write)
            {
                if (IsCiaIcrRead(operation.Address, operation.Size) && _bus is IM68kDeferredCiaBus deferredCiaBus)
                {
                    deferredCiaBus.RetireCiaIcrRead(operation.Address, operation.Size, operation.Value);
                    return;
                }

                if (operation.Size == 1)
                {
                    _bus.ReadByte(operation.Address);
                }
                else
                {
                    _bus.ReadWord(operation.Address);
                }

                return;
            }

            if (operation.Size == 1)
            {
                _bus.WriteByte(operation.Address, (byte)operation.Value);
                return;
            }

            _bus.WriteWord(operation.Address, (ushort)operation.Value);
        }

        /// <summary>
        /// Returns whether the next queued bus operation can retire in the current DMA slot.
        /// </summary>
        private bool CanRetireNextPendingBusOperation(bool chipBusAvailable)
        {
            if (_pendingBusOperations.Count == 0)
            {
                return true;
            }

            PendingBusOperation operation = _pendingBusOperations.Peek();
            if (_busArbitration == null || !_busArbitration.RequiresChipBus(operation.Address, operation.Size))
            {
                return true;
            }

            return chipBusAvailable;
        }

        /// <summary>
        /// Checks Aligned Access for test validation.
        /// </summary>
        private void CheckAlignedAccess(uint address, int size, bool write)
        {
            if (size <= 1 || (address & 1u) == 0)
            {
                return;
            }

            NotifyOddAddressAccess(address, size, write);
            EnterAddressError(address, size, write);
            throw new AddressErrorException();
        }

        /// <summary>
        /// Notifies this component about Odd Address Access.
        /// </summary>
        private void NotifyOddAddressAccess(uint address, int size, bool write)
        {
            if (size <= 1 || (address & 1u) == 0)
            {
                return;
            }

            Action<uint, int, bool, uint, ushort> handler = OddAddressAccessed;
            if (handler != null)
            {
                handler(address & AddressMask, size, write, _currentInstructionAddress & AddressMask, _state.CurrentOpcode);
            }
        }

        /// <summary>
        /// Returns the requested byte, word, or longword slice of a data register.
        /// </summary>
        private uint ReadDataRegister(int register, int size)
        {
            uint value = _state.D[register];
            if (size == 1)
            {
                return value & 0xFF;
            }

            if (size == 2)
            {
                return value & 0xFFFF;
            }

            return value;
        }

        /// <summary>
        /// Writes a byte, word, or longword slice of a data register while preserving upper bits.
        /// </summary>
        private void WriteDataRegister(int register, int size, uint value)
        {
            if (size == 1)
            {
                _state.D[register] = (_state.D[register] & 0xFFFFFF00) | (value & 0xFF);
            }
            else if (size == 2)
            {
                _state.D[register] = (_state.D[register] & 0xFFFF0000) | (value & 0xFFFF);
            }
            else
            {
                _state.D[register] = value;
            }
        }

        /// <summary>
        /// Runs Address Increment.
        /// </summary>
        private uint AddressIncrement(int register, int size)
        {
            return (uint)(size == 1 && register == 7 ? 2 : size);
        }

        /// <summary>
        /// Runs Push Word.
        /// </summary>
        private void PushWord(ushort value)
        {
            SetA(7, _state.A[7] - 2);
            WriteWord(_state.A[7], value);
        }

        /// <summary>
        /// Runs Push Long.
        /// </summary>
        private void PushLong(uint value)
        {
            SetA(7, _state.A[7] - 4);
            WriteLong(_state.A[7], value);
        }

        /// <summary>
        /// Pushes one word while constructing an address-error frame without recursively raising another address error.
        /// </summary>
        private void PushExceptionWordUnchecked(ushort value)
        {
            SetA(7, _state.A[7] - 2);
            RecordBusMicroOps(M68kTraceAccessType.Write, _state.A[7], 2, value, true, true);
            if (_deferBusSideEffects)
            {
                return;
            }

            _bus.WriteWord(_state.A[7] & AddressMask, value);
        }

        /// <summary>
        /// Pushes one longword while constructing an address-error frame without recursively raising another address error.
        /// </summary>
        private void PushExceptionLongUnchecked(uint value)
        {
            SetA(7, _state.A[7] - 4);
            RecordBusMicroOps(M68kTraceAccessType.Write, _state.A[7], 4, value, true, true);
            if (_deferBusSideEffects)
            {
                return;
            }

            _bus.WriteLong(_state.A[7] & AddressMask, value);
        }

        /// <summary>
        /// Runs Pop Word.
        /// </summary>
        private ushort PopWord()
        {
            ushort value = ReadWord(_state.A[7]);
            SetA(7, _state.A[7] + 2);
            return value;
        }

        /// <summary>
        /// Runs Pop Long.
        /// </summary>
        private uint PopLong()
        {
            uint value = ReadLong(_state.A[7]);
            SetA(7, _state.A[7] + 4);
            return value;
        }

        /// <summary>
        /// Runs Execute Rte.
        /// </summary>
        private void ExecuteRte()
        {
            uint frameAddress = _state.A[7] & AddressMask;
            ushort newSr = ReadWord(frameAddress);
            uint newPc = ReadLong(frameAddress + 2) & AddressMask;
            SetA(7, frameAddress + 6);
            SetStatusRegister(newSr);
            SetProgramCounterInternal(newPc);
        }

        /// <summary>
        /// Runs Enter Exception.
        /// </summary>
        private void EnterException(int vectorNumber, ushort interruptMask)
        {
            EnterException(vectorNumber, interruptMask, _state.PC);
        }

        /// <summary>
        /// Runs Enter Exception.
        /// </summary>
        private void EnterException(int vectorNumber, ushort interruptMask, uint stackedPc)
        {
            if (vectorNumber < 0 || vectorNumber > 255)
            {
                throw new ArgumentOutOfRangeException(nameof(vectorNumber));
            }

            ushort oldSr = _state.SR;
            SetSupervisor(true);
            PushLong(stackedPc & AddressMask);
            PushWord(oldSr);
            ushort newSr = (ushort)((_state.SR & unchecked((ushort)~(FlagT | InterruptMask))) | FlagS | interruptMask);
            SetStatusRegister(newSr);
            uint targetPc = ReadLong((uint)(vectorNumber * 4)) & AddressMask;
            _exceptionEnteredDuringInstruction = true;
            Action<int, uint, ushort, uint, ushort, uint> handler = ExceptionEntered;
            if (handler != null)
            {
                handler(vectorNumber, stackedPc & AddressMask, oldSr, _currentInstructionAddress & AddressMask, _state.CurrentOpcode, targetPc);
            }

            SetProgramCounterInternal(targetPc);
            _state.ExecutionState = M68kExecutionState.FetchOpcode;
        }

        /// <summary>
        /// Runs Enter Address Error.
        /// </summary>
        private void EnterAddressError(uint faultAddress, int size, bool write)
        {
            ushort oldSr = _state.SR;
            uint stackedPc = _currentInstructionAddress & AddressMask;
            ushort instruction = _state.CurrentOpcode;
            ushort specialStatus = BuildBusErrorStatusWord(faultAddress, size, write);

            SetSupervisor(true);
            PushExceptionWordUnchecked(specialStatus);
            PushExceptionLongUnchecked(faultAddress & AddressMask);
            PushExceptionWordUnchecked(instruction);
            PushExceptionLongUnchecked(stackedPc);
            PushExceptionWordUnchecked(oldSr);
            ushort newSr = (ushort)((_state.SR & unchecked((ushort)~(FlagT | InterruptMask))) | FlagS | (_state.SR & InterruptMask));
            SetStatusRegister(newSr);
            uint targetPc = ReadLong(3u * 4u) & AddressMask;
            _exceptionEnteredDuringInstruction = true;
            Action<int, uint, ushort, uint, ushort, uint> handler = ExceptionEntered;
            if (handler != null)
            {
                handler(3, stackedPc, oldSr, _currentInstructionAddress & AddressMask, instruction, targetPc);
            }

            SetProgramCounterInternal(targetPc);
            _state.ExecutionState = M68kExecutionState.FetchOpcode;
        }

        /// <summary>
        /// Builds the Bus Error Status Word.
        /// </summary>
        private static ushort BuildBusErrorStatusWord(uint faultAddress, int size, bool write)
        {
            ushort status = 0;
            if (!write)
            {
                status |= 0x0010;
            }

            if (size == 1)
            {
                status |= (faultAddress & 1u) == 0 ? (ushort)0x0008 : (ushort)0x0004;
            }
            else
            {
                status |= 0x000C;
            }

            return status;
        }

        /// <summary>
        /// Attempts to Enter Pending Interrupt and reports whether it succeeded.
        /// </summary>
        private bool TryEnterPendingInterrupt()
        {
            int level = _pendingInterruptLevel;
            if (level <= 0)
            {
                return false;
            }

            int currentMask = (_state.SR & InterruptMask) >> 8;
            if (level <= currentMask && level != 7)
            {
                return false;
            }

            EnterException(24 + level, (ushort)(level << 8));
            return true;
        }

        /// <summary>
        /// Updates the Ccr.
        /// </summary>
        private void SetCcr(ushort value)
        {
            _state.SR = (ushort)((_state.SR & 0xFFE0) | (value & 0x001F));
        }

        /// <summary>
        /// Updates the Status Register.
        /// </summary>
        private void SetStatusRegister(ushort value)
        {
            bool wasSupervisor = IsSupervisor();
            bool willBeSupervisor = (value & FlagS) != 0;
            if (wasSupervisor != willBeSupervisor)
            {
                if (wasSupervisor)
                {
                    _state.SSP = _state.A[7];
                    _state.A[7] = _state.USP;
                }
                else
                {
                    _state.USP = _state.A[7];
                    _state.A[7] = _state.SSP;
                }
            }

            _state.SR = value;
        }

        /// <summary>
        /// Updates the Supervisor.
        /// </summary>
        private void SetSupervisor(bool supervisor)
        {
            if (supervisor)
            {
                SetStatusRegister((ushort)(_state.SR | FlagS));
            }
            else
            {
                SetStatusRegister((ushort)(_state.SR & unchecked((ushort)~FlagS)));
            }
        }

        /// <summary>
        /// Attempts to Enter Privilege Violation and reports whether it succeeded.
        /// </summary>
        private bool TryEnterPrivilegeViolation(out string mnemonic)
        {
            if (IsSupervisor())
            {
                mnemonic = null;
                return false;
            }

            EnterException(8, 0, _currentInstructionAddress);
            mnemonic = "PRIVILEGE";
            return true;
        }

        /// <summary>
        /// Returns whether Supervisor is true.
        /// </summary>
        private bool IsSupervisor()
        {
            return (_state.SR & FlagS) != 0;
        }

        /// <summary>
        /// Runs Add Values.
        /// </summary>
        private uint AddValues(uint destination, uint source, int size, bool affectExtend)
        {
            uint mask = SizeMask(size);
            uint sign = SignBit(size);
            destination &= mask;
            source &= mask;
            uint result = (destination + source) & mask;
            bool carry = ((ulong)destination + source) > mask;
            bool overflow = ((~(destination ^ source) & (result ^ destination) & sign) != 0);
            SetArithmeticFlags(result, size, carry, overflow, affectExtend);
            return result;
        }

        /// <summary>
        /// Runs Subtract Values.
        /// </summary>
        private uint SubtractValues(uint destination, uint source, int size, bool affectExtend)
        {
            uint mask = SizeMask(size);
            uint sign = SignBit(size);
            destination &= mask;
            source &= mask;
            uint result = (destination - source) & mask;
            bool carry = source > destination;
            bool overflow = (((source ^ destination) & (result ^ destination) & sign) != 0);
            SetArithmeticFlags(result, size, carry, overflow, affectExtend);
            return result;
        }

        /// <summary>
        /// Runs Add Extend Values.
        /// </summary>
        private uint AddExtendValues(uint destination, uint source, int size)
        {
            uint mask = SizeMask(size);
            destination &= mask;
            source &= mask;
            uint extend = (_state.SR & FlagX) != 0 ? 1u : 0u;
            ulong total = (ulong)destination + source + extend;
            uint result = (uint)total & mask;
            bool carry = total > mask;
            bool overflow = SignedAddOverflows(destination, source, extend, size);
            SetExtendArithmeticFlags(result, size, carry, overflow);
            return result;
        }

        /// <summary>
        /// Runs Subtract Extend Values.
        /// </summary>
        private uint SubtractExtendValues(uint destination, uint source, int size)
        {
            uint mask = SizeMask(size);
            destination &= mask;
            source &= mask;
            uint extend = (_state.SR & FlagX) != 0 ? 1u : 0u;
            ulong subtrahend = (ulong)source + extend;
            uint result = (uint)((destination - subtrahend) & mask);
            bool carry = subtrahend > destination;
            bool overflow = SignedSubtractOverflows(destination, source, extend, size);
            SetExtendArithmeticFlags(result, size, carry, overflow);
            return result;
        }

        /// <summary>
        /// Updates the Arithmetic Flags.
        /// </summary>
        private void SetArithmeticFlags(uint result, int size, bool carry, bool overflow, bool affectExtend)
        {
            ushort preserve = (ushort)(_state.SR & unchecked((ushort)~(FlagN | FlagZ | FlagV | FlagC | (affectExtend ? FlagX : 0))));
            ushort flags = 0;
            if ((result & SignBit(size)) != 0)
            {
                flags |= FlagN;
            }

            if ((result & SizeMask(size)) == 0)
            {
                flags |= FlagZ;
            }

            if (overflow)
            {
                flags |= FlagV;
            }

            if (carry)
            {
                flags |= FlagC;
                if (affectExtend)
                {
                    flags |= FlagX;
                }
            }

            _state.SR = (ushort)(preserve | flags);
        }

        /// <summary>
        /// Updates the Extend Arithmetic Flags.
        /// </summary>
        private void SetExtendArithmeticFlags(uint result, int size, bool carry, bool overflow)
        {
            ushort preserve = (ushort)(_state.SR & unchecked((ushort)~(FlagN | FlagV | FlagC | FlagX)));
            ushort flags = 0;
            result &= SizeMask(size);
            if ((result & SignBit(size)) != 0)
            {
                flags |= FlagN;
            }

            if (result != 0)
            {
                preserve &= unchecked((ushort)~FlagZ);
            }

            if (overflow)
            {
                flags |= FlagV;
            }

            if (carry)
            {
                flags |= FlagC | FlagX;
            }

            _state.SR = (ushort)(preserve | flags);
        }

        /// <summary>
        /// Updates the Negx Flags.
        /// </summary>
        private void SetNegxFlags(uint value, uint result, int size, bool extend)
        {
            uint sign = SignBit(size);
            ushort preserve = (ushort)(_state.SR & unchecked((ushort)~(FlagN | FlagV | FlagC | FlagX)));
            ushort flags = 0;
            if ((result & sign) != 0)
            {
                flags |= FlagN;
            }

            uint maskedValue = value & SizeMask(size);
            if ((maskedValue == sign || (extend && maskedValue == sign - 1u)) && (result & sign) != 0)
            {
                flags |= FlagV;
            }

            if (value != 0 || extend)
            {
                flags |= FlagC | FlagX;
            }

            if ((result & SizeMask(size)) != 0)
            {
                preserve &= unchecked((ushort)~FlagZ);
            }

            _state.SR = (ushort)(preserve | flags);
        }

        /// <summary>
        /// Updates the Logic Flags.
        /// </summary>
        private void SetLogicFlags(uint value, int size)
        {
            ushort preserve = (ushort)(_state.SR & unchecked((ushort)~(FlagN | FlagZ | FlagV | FlagC)));
            ushort flags = 0;
            value &= SizeMask(size);
            if ((value & SignBit(size)) != 0)
            {
                flags |= FlagN;
            }

            if (value == 0)
            {
                flags |= FlagZ;
            }

            _state.SR = (ushort)(preserve | flags);
        }

        /// <summary>
        /// Runs Evaluate Condition.
        /// </summary>
        private bool EvaluateCondition(int condition)
        {
            bool c = (_state.SR & FlagC) != 0;
            bool v = (_state.SR & FlagV) != 0;
            bool z = (_state.SR & FlagZ) != 0;
            bool n = (_state.SR & FlagN) != 0;

            switch (condition)
            {
                case 0:
                    return true;
                case 1:
                    return false;
                case 2:
                    return !c && !z;
                case 3:
                    return c || z;
                case 4:
                    return !c;
                case 5:
                    return c;
                case 6:
                    return !z;
                case 7:
                    return z;
                case 8:
                    return !v;
                case 9:
                    return v;
                case 10:
                    return !n;
                case 11:
                    return n;
                case 12:
                    return n == v;
                case 13:
                    return n != v;
                case 14:
                    return !z && n == v;
                case 15:
                    return z || n != v;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns the Branch Displacement.
        /// </summary>
        private int GetBranchDisplacement(ushort opcode)
        {
            byte displacement = (byte)(opcode & 0xFF);
            if (displacement == 0)
            {
                return (short)FetchWord();
            }

            return (sbyte)displacement;
        }

        /// <summary>
        /// Decodes Size from Amiga-compatible data.
        /// </summary>
        private static int DecodeSize(int sizeCode)
        {
            switch (sizeCode)
            {
                case 0:
                    return 1;
                case 1:
                    return 2;
                case 2:
                    return 4;
                default:
                    throw new InvalidOperationException("Invalid size.");
            }
        }

        /// <summary>
        /// Runs Size Mask.
        /// </summary>
        private static uint SizeMask(int size)
        {
            return size == 1 ? 0xFFu : (size == 2 ? 0xFFFFu : 0xFFFFFFFFu);
        }

        /// <summary>
        /// Runs Sign Bit.
        /// </summary>
        private static uint SignBit(int size)
        {
            return size == 1 ? 0x80u : (size == 2 ? 0x8000u : 0x80000000u);
        }

        /// <summary>
        /// Runs Sign Extend.
        /// </summary>
        private static uint SignExtend(uint value, int size)
        {
            if (size == 1)
            {
                return (uint)(int)(sbyte)(value & 0xFF);
            }

            if (size == 2)
            {
                return (uint)(int)(short)(value & 0xFFFF);
            }

            return value;
        }

        /// <summary>
        /// Runs Signed Add Overflows.
        /// </summary>
        private static bool SignedAddOverflows(uint destination, uint source, uint extend, int size)
        {
            long result = ToSigned(destination, size) + ToSigned(source, size) + extend;
            return result < SignedMinimum(size) || result > SignedMaximum(size);
        }

        /// <summary>
        /// Runs Signed Subtract Overflows.
        /// </summary>
        private static bool SignedSubtractOverflows(uint destination, uint source, uint extend, int size)
        {
            long result = ToSigned(destination, size) - ToSigned(source, size) - extend;
            return result < SignedMinimum(size) || result > SignedMaximum(size);
        }

        /// <summary>
        /// Runs To Signed.
        /// </summary>
        private static long ToSigned(uint value, int size)
        {
            if (size == 1)
            {
                return (sbyte)(value & 0xFF);
            }

            if (size == 2)
            {
                return (short)(value & 0xFFFF);
            }

            return (int)value;
        }

        /// <summary>
        /// Runs Signed Minimum.
        /// </summary>
        private static long SignedMinimum(int size)
        {
            return size == 1 ? sbyte.MinValue : (size == 2 ? short.MinValue : int.MinValue);
        }

        /// <summary>
        /// Runs Signed Maximum.
        /// </summary>
        private static long SignedMaximum(int size)
        {
            return size == 1 ? sbyte.MaxValue : (size == 2 ? short.MaxValue : int.MaxValue);
        }

        /// <summary>
        /// Runs Size Suffix.
        /// </summary>
        private static string SizeSuffix(int size)
        {
            return size == 1 ? "B" : (size == 2 ? "W" : "L");
        }

        /// <summary>
        /// Runs Condition Name.
        /// </summary>
        private static string ConditionName(int condition)
        {
            switch (condition)
            {
                case 2:
                    return "HI";
                case 3:
                    return "LS";
                case 4:
                    return "CC";
                case 5:
                    return "CS";
                case 6:
                    return "NE";
                case 7:
                    return "EQ";
                case 8:
                    return "VC";
                case 9:
                    return "VS";
                case 10:
                    return "PL";
                case 11:
                    return "MI";
                case 12:
                    return "GE";
                case 13:
                    return "LT";
                case 14:
                    return "GT";
                case 15:
                    return "LE";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Runs Format Ea Instruction.
        /// </summary>
        private static string FormatEaInstruction(string name, int size, int mode, int register)
        {
            return name + "." + SizeSuffix(size) + " " + FormatEa(mode, register, size);
        }

        /// <summary>
        /// Runs Format Ea.
        /// </summary>
        private static string FormatEa(int mode, int register, int size)
        {
            switch (mode)
            {
                case 0:
                    return "D" + register;
                case 1:
                    return "A" + register;
                case 2:
                    return "(A" + register + ")";
                case 3:
                    return "(A" + register + ")+";
                case 4:
                    return "-(A" + register + ")";
                case 5:
                    return "(d16,A" + register + ")";
                case 6:
                    return "(d8,A" + register + ",Xn)";
                case 7:
                    switch (register)
                    {
                        case 0:
                            return "(xxx).W";
                        case 1:
                            return "(xxx).L";
                        case 2:
                            return "(d16,PC)";
                        case 3:
                            return "(d8,PC,Xn)";
                        case 4:
                            return "#" + SizeSuffix(size);
                        default:
                            return "<invalid>";
                    }
                default:
                    return "<invalid>";
            }
        }

        /// <summary>
        /// Runs Validate Register.
        /// </summary>
        private static void ValidateRegister(int register)
        {
            if (register < 0 || register > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(register));
            }
        }

        /// <summary>
        /// Creates the Trace.
        /// </summary>
        private M68kTraceEntry CreateTrace(
            uint pcBefore,
            uint pcAfter,
            ushort opcode,
            string mnemonic,
            ushort srBefore,
            ushort srAfter,
            M68kTraceAccessType accessType)
        {
            return new M68kTraceEntry
            {
                CpuCycle = _state.Cycles,
                PcBefore = pcBefore & AddressMask,
                PcAfter = pcAfter & AddressMask,
                Opcode = opcode,
                Instruction = mnemonic,
                SrBefore = srBefore,
                SrAfter = srAfter,
                AccessType = accessType,
                Address = pcBefore & AddressMask,
                Value = opcode
            };
        }
    }
}
