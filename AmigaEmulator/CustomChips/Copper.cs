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
using System.IO;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>
    /// Lists the supported Copper Instruction Kind values.
    /// </summary>
    public enum CopperInstructionKind
    {
        /// <summary>
        /// Represents Move.
        /// </summary>
        Move,
        /// <summary>
        /// Represents Wait.
        /// </summary>
        Wait,
        /// <summary>
        /// Represents Skip.
        /// </summary>
        Skip,
        /// <summary>
        /// Represents Jump 1.
        /// </summary>
        Jump1,
        /// <summary>
        /// Represents Jump 2.
        /// </summary>
        Jump2,
        /// <summary>
        /// Represents Blocked Move.
        /// </summary>
        BlockedMove
    }

    /// <summary>
    /// Provides Copper Trace Entry functionality.
    /// </summary>
    public sealed class CopperTraceEntry
    {
        /// <summary>
        /// Gets or sets Global Chip Cycle.
        /// </summary>
        public long GlobalChipCycle { get; set; }

        /// <summary>
        /// Gets or sets Beam X.
        /// </summary>
        public int BeamX { get; set; }

        /// <summary>
        /// Gets or sets Beam Y.
        /// </summary>
        public int BeamY { get; set; }

        /// <summary>
        /// Gets or sets Program Counter.
        /// </summary>
        public uint ProgramCounter { get; set; }

        /// <summary>
        /// Gets or sets First Word.
        /// </summary>
        public ushort FirstWord { get; set; }

        /// <summary>
        /// Gets or sets Second Word.
        /// </summary>
        public ushort SecondWord { get; set; }

        /// <summary>
        /// Gets or sets Kind.
        /// </summary>
        public CopperInstructionKind Kind { get; set; }

        /// <summary>
        /// Gets or sets Register Offset.
        /// </summary>
        public uint RegisterOffset { get; set; }

        /// <summary>
        /// Gets or sets Value.
        /// </summary>
        public ushort Value { get; set; }

        /// <summary>
        /// Gets or sets Target.
        /// </summary>
        public uint Target { get; set; }

        /// <summary>
        /// Gets or sets Waiting.
        /// </summary>
        public bool Waiting { get; set; }
    }

    /// <summary>
    /// Emulates the Copper co-processor and its MOVE, WAIT, and SKIP instruction stream.
    /// </summary>
    public sealed class Copper
    {
        /// <summary>
        /// Defines the DMACON custom register offset or bit mask.
        /// </summary>
        private const uint Dmacon = 0x096;
        /// <summary>
        /// Defines the COPCON custom register offset or bit mask.
        /// </summary>
        private const uint Copcon = 0x02E;
        /// <summary>
        /// Defines the COP1LCH custom register offset or bit mask.
        /// </summary>
        private const uint Cop1lch = 0x080;
        /// <summary>
        /// Defines the COP1LCL custom register offset or bit mask.
        /// </summary>
        private const uint Cop1lcl = 0x082;
        /// <summary>
        /// Defines the COP2LCH custom register offset or bit mask.
        /// </summary>
        private const uint Cop2lch = 0x084;
        /// <summary>
        /// Defines the COP2LCL custom register offset or bit mask.
        /// </summary>
        private const uint Cop2lcl = 0x086;
        /// <summary>
        /// Defines the COPJMP1 custom register offset or bit mask.
        /// </summary>
        private const uint Copjmp1 = 0x088;
        /// <summary>
        /// Defines the COPJMP2 custom register offset or bit mask.
        /// </summary>
        private const uint Copjmp2 = 0x08A;

        /// <summary>
        /// Stores registers.
        /// </summary>
        private CustomRegisterMap _registers;
        /// <summary>
        /// Stores read Chip Word.
        /// </summary>
        private Func<uint, ushort> _readChipWord;
        /// <summary>
        /// Stores is Blitter Busy.
        /// </summary>
        private Func<bool> _isBlitterBusy;
        /// <summary>
        /// Stores DMA Address.
        /// </summary>
        private readonly ChipDmaAddress _dmaAddress;
        /// <summary>
        /// Stores program Counter Loaded.
        /// </summary>
        private bool _programCounterLoaded;
        /// <summary>
        /// Stores waiting.
        /// </summary>
        private bool _waiting;
        /// <summary>
        /// Stores wait Instruction.
        /// </summary>
        private ushort _waitInstruction;
        /// <summary>
        /// Stores wait Mask.
        /// </summary>
        private ushort _waitMask;
        /// <summary>
        /// Stores first Word Latched.
        /// </summary>
        private bool _firstWordLatched;
        /// <summary>
        /// Stores first Word.
        /// </summary>
        private ushort _firstWord;

        /// <summary>
        /// Gets or sets Program Counter.
        /// </summary>
        public uint ProgramCounter { get; private set; }

        /// <summary>
        /// Gets or sets Enabled.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets whether the Copper currently needs a DMA slot to fetch instruction data.
        /// </summary>
        public bool NeedsDmaBus
        {
            get
            {
                RefreshFromRegisters();
                return Enabled && _programCounterLoaded && !_waiting && _readChipWord != null;
            }
        }

        /// <summary>
        /// Raised when the Copper executes an instruction or writes a custom register.
        /// </summary>
        public event Action<CopperTraceEntry> InstructionExecuted;

        /// <summary>
        /// Raised when the Copper executes an instruction or writes a custom register.
        /// </summary>
        public event Action<uint, int> CustomRegisterWritten;

        /// <summary>
        /// Initializes a new instance of the Copper class.
        /// </summary>
        public Copper()
            : this(null, null, 512 * 1024)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Copper class.
        /// </summary>
        public Copper(CustomRegisterMap registers)
            : this(registers, null, 512 * 1024)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Copper class.
        /// </summary>
        public Copper(CustomRegisterMap registers, int chipRamBytes)
            : this(registers, null, chipRamBytes)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Copper class.
        /// </summary>
        public Copper(CustomRegisterMap registers, Func<uint, ushort> readChipWord)
            : this(registers, readChipWord, 512 * 1024)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Copper class.
        /// </summary>
        public Copper(CustomRegisterMap registers, Func<uint, ushort> readChipWord, int chipRamBytes)
        {
            _registers = registers;
            _readChipWord = readChipWord;
            _dmaAddress = new ChipDmaAddress(chipRamBytes);
        }

        /// <summary>
        /// Attaches Registers to this component.
        /// </summary>
        public void AttachRegisters(CustomRegisterMap registers)
        {
            _registers = registers;
        }

        /// <summary>
        /// Attaches Chip Word Reader to this component.
        /// </summary>
        public void AttachChipWordReader(Func<uint, ushort> readChipWord)
        {
            _readChipWord = readChipWord;
        }

        /// <summary>
        /// Attaches Blitter Status to this component.
        /// </summary>
        public void AttachBlitterStatus(Func<bool> isBlitterBusy)
        {
            _isBlitterBusy = isBlitterBusy;
        }

        /// <summary>
        /// Writes Copper DMA, wait, latch, and program-counter state to a savestate.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            StateSerializer.WriteObjectFields(writer, this, "_registers", "_readChipWord", "_isBlitterBusy");
        }

        /// <summary>
        /// Restores Copper DMA, wait, latch, and program-counter state from a savestate.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            StateSerializer.ReadObjectFields(reader, this, "_registers", "_readChipWord", "_isBlitterBusy");
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            ProgramCounter = 0;
            Enabled = false;
            _programCounterLoaded = false;
            _waiting = false;
            _waitInstruction = 0;
            _waitMask = 0;
            _firstWordLatched = false;
            _firstWord = 0;
        }

        /// <summary>
        /// Loads List into emulator state.
        /// </summary>
        public void LoadList(uint address)
        {
            ProgramCounter = _dmaAddress.Normalize(address);
            _programCounterLoaded = true;
            _waiting = false;
            _firstWordLatched = false;
        }

        /// <summary>
        /// Loads a Copper list exactly like a COPJMP strobe. Hardware does not validate the target contents.
        /// </summary>
        private bool TryLoadList(uint address)
        {
            LoadList(address);
            return true;
        }

        /// <summary>
        /// Runs Strobe Jump.
        /// </summary>
        public void StrobeJump(int list)
        {
            if (_registers == null)
            {
                return;
            }

            if (list == 1)
            {
                TryLoadList(ReadPointer(Cop1lch));
                return;
            }

            if (list == 2)
            {
                TryLoadList(ReadPointer(Cop2lch));
            }
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick(bool dmaSlotAvailable)
        {
            Tick(dmaSlotAvailable, 0, 0, 0);
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick(bool dmaSlotAvailable, int beamX, int beamY)
        {
            Tick(dmaSlotAvailable, beamX, beamY, 0);
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick(bool dmaSlotAvailable, int beamX, int beamY, long globalChipCycle)
        {
            RefreshFromRegisters();
            if (!Enabled || !_programCounterLoaded)
            {
                return;
            }

            if (_waiting)
            {
                if (IsFrameEndWait(_waitInstruction, _waitMask))
                {
                    return;
                }

                if (!ConditionReached(_waitInstruction, _waitMask, beamX, beamY))
                {
                    return;
                }

                _waiting = false;
                ProgramCounter = _dmaAddress.Add(ProgramCounter, 4u);
                EmitTrace(globalChipCycle, beamX, beamY, ProgramCounter - 4, _waitInstruction, _waitMask, CopperInstructionKind.Wait, 0, 0, 0, false);
                return;
            }

            if (!dmaSlotAvailable)
            {
                return;
            }

            if (_readChipWord == null)
            {
                return;
            }

            if (!_firstWordLatched)
            {
                _firstWord = _readChipWord(ProgramCounter);
                _firstWordLatched = true;
                return;
            }

            ushort first = _firstWord;
            ushort second = _readChipWord(_dmaAddress.Add(ProgramCounter, 2u));
            _firstWordLatched = false;

            if ((first & 1) == 0)
            {
                if (!Move(first, second, beamX, beamY, globalChipCycle))
                {
                    ProgramCounter = _dmaAddress.Add(ProgramCounter, 4u);
                }

                return;
            }

            if ((second & 1) != 0)
            {
                EmitTrace(globalChipCycle, beamX, beamY, ProgramCounter, first, second, CopperInstructionKind.Skip, 0, 0, 0, false);
                ProgramCounter = ConditionReached(first, second, beamX, beamY)
                    ? _dmaAddress.Add(ProgramCounter, 8u)
                    : _dmaAddress.Add(ProgramCounter, 4u);
                return;
            }

            if (ConditionReached(first, second, beamX, beamY))
            {
                if (IsFrameEndWait(first, second))
                {
                    _waiting = true;
                    _waitInstruction = first;
                    _waitMask = second;
                    return;
                }

                ProgramCounter = _dmaAddress.Add(ProgramCounter, 4u);
                EmitTrace(globalChipCycle, beamX, beamY, ProgramCounter - 4, first, second, CopperInstructionKind.Wait, 0, 0, 0, false);
                return;
            }

            _waiting = true;
            _waitInstruction = first;
            _waitMask = second;
            EmitTrace(globalChipCycle, beamX, beamY, ProgramCounter, first, second, CopperInstructionKind.Wait, 0, 0, 0, true);
        }

        /// <summary>
        /// Returns whether the WAIT instruction is a Copper end-of-list sentinel.
        /// </summary>
        private static bool IsFrameEndWait(ushort instruction, ushort mask)
        {
            return instruction == 0xFFFF
                && (mask & 0x0001) == 0
                && (mask & 0xFF00) == 0xFF00
                && (instruction & 0x00FE) == 0x00FE;
        }

        /// <summary>
        /// Runs Beam Reached.
        /// </summary>
        private static bool BeamReached(ushort instruction, ushort mask, int beamX, int beamY)
        {
            int waitY = instruction & 0xFF00;
            int waitX = instruction & 0x00FE;
            int maskY = mask & 0xFF00;
            int maskX = mask & 0x00FE;
            int beamPositionY = (beamY & 0xFF) << 8;
            int beamPositionX = beamX & 0x00FE;

            int wantedY = waitY & maskY;
            int currentY = beamPositionY & maskY;
            if (currentY != wantedY)
            {
                return currentY > wantedY;
            }

            return (beamPositionX & maskX) >= (waitX & maskX);
        }

        /// <summary>
        /// Runs Condition Reached.
        /// </summary>
        private bool ConditionReached(ushort instruction, ushort mask, int beamX, int beamY)
        {
            if (!BeamReached(instruction, mask, beamX, beamY))
            {
                return false;
            }

            return BlitterFinishedDisabled(mask) || _isBlitterBusy == null || !_isBlitterBusy();
        }

        /// <summary>
        /// Runs Blitter Finished Disabled.
        /// </summary>
        private static bool BlitterFinishedDisabled(ushort mask)
        {
            return (mask & 0x8000) != 0;
        }

        /// <summary>
        /// Runs Refresh From Registers.
        /// </summary>
        private void RefreshFromRegisters()
        {
            if (_registers == null)
            {
                return;
            }

            ushort dmacon = _registers.ReadWord(Dmacon);
            Enabled = (dmacon & 0x0200) != 0 && (dmacon & 0x0080) != 0;
        }

        /// <summary>
        /// Runs Restart At Copper List 1.
        /// </summary>
        public void RestartAtCopperList1()
        {
            if (_registers == null)
            {
                return;
            }

            uint address = ReadPointer(Cop1lch);
            if (address != 0)
            {
                LoadList(address);
            }
        }

        /// <summary>
        /// Runs Move.
        /// </summary>
        private bool Move(ushort instruction, ushort value, int beamX, int beamY, long globalChipCycle)
        {
            if (_registers == null)
            {
                return false;
            }

            uint pcBefore = ProgramCounter;
            uint registerOffset = (uint)(instruction & 0x01FE);
            if (IsProtectedRegister(registerOffset))
            {
                EmitTrace(globalChipCycle, beamX, beamY, pcBefore, instruction, value, CopperInstructionKind.BlockedMove, registerOffset, value, 0, false);
                StopOnDangerousMove();
                return true;
            }

            if (registerOffset == Copjmp1)
            {
                uint target = ReadPointer(Cop1lch);
                bool jumped = TryLoadList(target);

                EmitTrace(globalChipCycle, beamX, beamY, pcBefore, instruction, value, jumped ? CopperInstructionKind.Jump1 : CopperInstructionKind.BlockedMove, registerOffset, value, target, !jumped);
                return true;
            }

            if (registerOffset == Copjmp2)
            {
                uint target = ReadPointer(Cop2lch);
                bool jumped = TryLoadList(target);

                EmitTrace(globalChipCycle, beamX, beamY, pcBefore, instruction, value, jumped ? CopperInstructionKind.Jump2 : CopperInstructionKind.BlockedMove, registerOffset, value, target, !jumped);
                return true;
            }

            _registers.WriteWord(registerOffset, value);
            RaiseCustomRegisterWritten(registerOffset, 2);
            EmitTrace(globalChipCycle, beamX, beamY, pcBefore, instruction, value, CopperInstructionKind.Move, registerOffset, value, 0, false);
            ProgramCounter = _dmaAddress.Add(ProgramCounter, 4u);
            return true;
        }

        /// <summary>
        /// Stops Copper execution after a forbidden MOVE, matching the OCS dangerous-register guard.
        /// </summary>
        private void StopOnDangerousMove()
        {
            _programCounterLoaded = false;
            _waiting = false;
            _firstWordLatched = false;
        }

        /// <summary>
        /// Returns whether Protected Register is true.
        /// </summary>
        private bool IsProtectedRegister(uint registerOffset)
        {
            if (registerOffset < 0x040)
            {
                return true;
            }

            if (registerOffset < 0x080)
            {
                ushort copcon = _registers.ReadWord(Copcon);
                return (copcon & 0x0002) == 0;
            }

            return false;
        }

        /// <summary>
        /// Runs Emit Trace.
        /// </summary>
        private void EmitTrace(
            long globalChipCycle,
            int beamX,
            int beamY,
            uint pc,
            ushort first,
            ushort second,
            CopperInstructionKind kind,
            uint registerOffset,
            ushort value,
            uint target,
            bool waiting)
        {
            Action<CopperTraceEntry> handler = InstructionExecuted;
            if (handler == null)
            {
                return;
            }

            handler(new CopperTraceEntry
            {
                GlobalChipCycle = globalChipCycle,
                BeamX = beamX,
                BeamY = beamY,
                ProgramCounter = _dmaAddress.Normalize(pc),
                FirstWord = first,
                SecondWord = second,
                Kind = kind,
                RegisterOffset = registerOffset,
                Value = value,
                Target = target,
                Waiting = waiting
            });
        }

        /// <summary>
        /// Reads a Copper pointer from a high/low custom-register pair.
        /// </summary>
        private uint ReadPointer(uint highOffset)
        {
            return ReadRawPointer(highOffset);
        }

        /// <summary>
        /// Reads a Copper pointer pair without applying DMA address normalization.
        /// </summary>
        private uint ReadRawPointer(uint highOffset)
        {
            ushort high = _registers.ReadWord(highOffset);
            ushort low = _registers.ReadWord(highOffset + 2);
            return _dmaAddress.FromRegisters(high, low);
        }

        /// <summary>
        /// Raises the Custom Register Written notification.
        /// </summary>
        private void RaiseCustomRegisterWritten(uint registerOffset, int size)
        {
            Action<uint, int> handler = CustomRegisterWritten;
            if (handler != null)
            {
                handler(registerOffset, size);
            }
        }

    }
}
