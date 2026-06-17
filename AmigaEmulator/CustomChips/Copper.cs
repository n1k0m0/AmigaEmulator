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
        /// Represents the M ov e option.
        /// </summary>
        Move,
        /// <summary>
        /// Represents the W ai t option.
        /// </summary>
        Wait,
        /// <summary>
        /// Represents the S ki p option.
        /// </summary>
        Skip,
        /// <summary>
        /// Represents the J um p1 option.
        /// </summary>
        Jump1,
        /// <summary>
        /// Represents the J um p2 option.
        /// </summary>
        Jump2,
        /// <summary>
        /// Represents the B lo ck ed Mo ve option.
        /// </summary>
        BlockedMove
    }

    /// <summary>
    /// Provides Copper Trace Entry behavior used by the emulator.
    /// </summary>
    public sealed class CopperTraceEntry
    {
        /// <summary>
        /// Gets or updates the Global Chip Cycle.
        /// </summary>
        public long GlobalChipCycle { get; set; }

        /// <summary>
        /// Gets or updates the Beam X.
        /// </summary>
        public int BeamX { get; set; }

        /// <summary>
        /// Gets or updates the Beam Y.
        /// </summary>
        public int BeamY { get; set; }

        /// <summary>
        /// Gets or updates the Program Counter.
        /// </summary>
        public uint ProgramCounter { get; set; }

        /// <summary>
        /// Gets or updates the First Word.
        /// </summary>
        public ushort FirstWord { get; set; }

        /// <summary>
        /// Gets or updates the Second Word.
        /// </summary>
        public ushort SecondWord { get; set; }

        /// <summary>
        /// Gets or updates the Kind.
        /// </summary>
        public CopperInstructionKind Kind { get; set; }

        /// <summary>
        /// Gets or updates the Register Offset.
        /// </summary>
        public uint RegisterOffset { get; set; }

        /// <summary>
        /// Gets or updates the Value.
        /// </summary>
        public ushort Value { get; set; }

        /// <summary>
        /// Gets or updates the Target.
        /// </summary>
        public uint Target { get; set; }

        /// <summary>
        /// Gets or updates the Waiting.
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
        /// Tracks the registers.
        /// </summary>
        private CustomRegisterMap _registers;
        /// <summary>
        /// Tracks the read Chip Word.
        /// </summary>
        private Func<uint, ushort> _readChipWord;
        /// <summary>
        /// Tracks the is Blitter Busy.
        /// </summary>
        private Func<bool> _isBlitterBusy;
        /// <summary>
        /// Tracks the DMA Address.
        /// </summary>
        private readonly ChipDmaAddress _dmaAddress;
        /// <summary>
        /// Tracks the program Counter Loaded.
        /// </summary>
        private bool _programCounterLoaded;
        /// <summary>
        /// Tracks the waiting.
        /// </summary>
        private bool _waiting;
        /// <summary>
        /// Tracks the wait Instruction.
        /// </summary>
        private ushort _waitInstruction;
        /// <summary>
        /// Tracks the wait Mask.
        /// </summary>
        private ushort _waitMask;
        /// <summary>
        /// Tracks the first Word Latched.
        /// </summary>
        private bool _firstWordLatched;
        /// <summary>
        /// Tracks the first Word.
        /// </summary>
        private ushort _firstWord;

        /// <summary>
        /// Gets or updates the Program Counter.
        /// </summary>
        public uint ProgramCounter { get; private set; }

        /// <summary>
        /// Gets or updates the Enabled.
        /// </summary>
        public bool Enabled { get; set; }

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
        /// Executes the strobe Jump logic.
        /// </summary>
        public void StrobeJump(int list)
        {
            if (_registers == null)
            {
                return;
            }

            if (list == 1)
            {
                LoadList(ReadPointer(Cop1lch));
                return;
            }

            if (list == 2)
            {
                LoadList(ReadPointer(Cop2lch));
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
            if (!Enabled || !dmaSlotAvailable || !_programCounterLoaded)
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
        /// Returns whether Frame End Wait is true.
        /// </summary>
        private static bool IsFrameEndWait(ushort instruction, ushort mask)
        {
            return mask == 0xFFFE && instruction == 0xFFFF;
        }

        /// <summary>
        /// Executes the Beam Reached logic.
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
        /// Executes the Condition Reached logic.
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
        /// Executes the Blitter Finished Disabled logic.
        /// </summary>
        private static bool BlitterFinishedDisabled(ushort mask)
        {
            return (mask & 0x8000) != 0;
        }

        /// <summary>
        /// Executes the Refresh From Registers logic.
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
        /// Executes the Restart At Copper List1 logic.
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
        /// Executes the Move logic.
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
                return false;
            }

            if (registerOffset == Copjmp1)
            {
                uint target = ReadPointer(Cop1lch);
                LoadList(target);

                EmitTrace(globalChipCycle, beamX, beamY, pcBefore, instruction, value, CopperInstructionKind.Jump1, registerOffset, value, target, false);
                return true;
            }

            if (registerOffset == Copjmp2)
            {
                uint target = ReadPointer(Cop2lch);
                LoadList(target);

                EmitTrace(globalChipCycle, beamX, beamY, pcBefore, instruction, value, CopperInstructionKind.Jump2, registerOffset, value, target, false);
                return true;
            }

            _registers.WriteWord(registerOffset, value);
            RaiseCustomRegisterWritten(registerOffset, 2);
            EmitTrace(globalChipCycle, beamX, beamY, pcBefore, instruction, value, CopperInstructionKind.Move, registerOffset, value, 0, false);
            ProgramCounter = _dmaAddress.Add(ProgramCounter, 4u);
            return true;
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
        /// Executes the Emit Trace logic.
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
