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
    /// Emulates the Amiga Blitter data path for fills, copies, masks, shifts, and line drawing.
    /// </summary>
    public sealed class Blitter
    {
        /// <summary>
        /// Lists the micro-operation driven block-blitter channel phases.
        /// </summary>
        private enum BlitterBlockChannel
        {
            /// <summary>
            /// Represents an idle blitter slot used by degenerate channel combinations.
            /// </summary>
            Idle,
            /// <summary>
            /// Represents a source A read slot.
            /// </summary>
            A,
            /// <summary>
            /// Represents a source B read slot.
            /// </summary>
            B,
            /// <summary>
            /// Represents a source C read slot.
            /// </summary>
            C,
            /// <summary>
            /// Represents a destination D write slot.
            /// </summary>
            D
        }

        /// <summary>
        /// Defines the Bltcon0 constant.
        /// </summary>
        private const uint Bltcon0 = 0x040;
        /// <summary>
        /// Defines the Bltcon1 constant.
        /// </summary>
        private const uint Bltcon1 = 0x042;
        /// <summary>
        /// Defines the Bltafwm constant.
        /// </summary>
        private const uint Bltafwm = 0x044;
        /// <summary>
        /// Defines the Bltalwm constant.
        /// </summary>
        private const uint Bltalwm = 0x046;
        /// <summary>
        /// Defines the Bltcpt constant.
        /// </summary>
        private const uint Bltcpt = 0x048;
        /// <summary>
        /// Defines the Bltbpt constant.
        /// </summary>
        private const uint Bltbpt = 0x04C;
        /// <summary>
        /// Defines the Bltapt constant.
        /// </summary>
        private const uint Bltapt = 0x050;
        /// <summary>
        /// Defines the Bltdpt constant.
        /// </summary>
        private const uint Bltdpt = 0x054;
        /// <summary>
        /// Defines the Bltsize constant.
        /// </summary>
        private const uint Bltsize = 0x058;
        /// <summary>
        /// Defines the Bltcmod constant.
        /// </summary>
        private const uint Bltcmod = 0x060;
        /// <summary>
        /// Defines the Bltbmod constant.
        /// </summary>
        private const uint Bltbmod = 0x062;
        /// <summary>
        /// Defines the Bltamod constant.
        /// </summary>
        private const uint Bltamod = 0x064;
        /// <summary>
        /// Defines the Bltdmod constant.
        /// </summary>
        private const uint Bltdmod = 0x066;
        /// <summary>
        /// Defines the Bltcdat constant.
        /// </summary>
        private const uint Bltcdat = 0x070;
        /// <summary>
        /// Defines the Bltbdat constant.
        /// </summary>
        private const uint Bltbdat = 0x072;
        /// <summary>
        /// Defines the Bltadat constant.
        /// </summary>
        private const uint Bltadat = 0x074;

        /// <summary>
        /// Tracks the registers.
        /// </summary>
        private readonly CustomRegisterMap _registers;
        /// <summary>
        /// Tracks the DMA Address.
        /// </summary>
        private readonly ChipDmaAddress _dmaAddress;
        /// <summary>
        /// Tracks the read Chip Word.
        /// </summary>
        private Func<uint, ushort> _readChipWord;
        /// <summary>
        /// Tracks the write Chip Word.
        /// </summary>
        private Action<uint, ushort> _writeChipWord;
        /// <summary>
        /// Tracks the request Interrupt.
        /// </summary>
        private Action<CustomInterruptSource> _requestInterrupt;
        /// <summary>
        /// Tracks the remaining Slots.
        /// </summary>
        private int _remainingSlots;
        /// <summary>
        /// Tracks the zero Flag.
        /// </summary>
        private bool _zeroFlag;
        /// <summary>
        /// Tracks the width Words.
        /// </summary>
        private int _widthWords;
        /// <summary>
        /// Tracks the height Lines.
        /// </summary>
        private int _heightLines;
        /// <summary>
        /// Tracks the current Word.
        /// </summary>
        private int _currentWord;
        /// <summary>
        /// Tracks the current Line.
        /// </summary>
        private int _currentLine;
        /// <summary>
        /// Tracks the block Slots period Word.
        /// </summary>
        private int _blockSlotsPerWord;
        /// <summary>
        /// Tracks the block Slot Phase.
        /// </summary>
        private int _blockSlotPhase;
        /// <summary>
        /// Tracks the use A.
        /// </summary>
        private bool _useA;
        /// <summary>
        /// Tracks the use B.
        /// </summary>
        private bool _useB;
        /// <summary>
        /// Tracks the use C.
        /// </summary>
        private bool _useC;
        /// <summary>
        /// Tracks the use D.
        /// </summary>
        private bool _useD;
        /// <summary>
        /// Tracks the minterm.
        /// </summary>
        private byte _minterm;
        /// <summary>
        /// Tracks the shift A.
        /// </summary>
        private int _shiftA;
        /// <summary>
        /// Tracks the shift B.
        /// </summary>
        private int _shiftB;
        /// <summary>
        /// Tracks the descending.
        /// </summary>
        private bool _descending;
        /// <summary>
        /// Tracks the line Mode.
        /// </summary>
        private bool _lineMode;
        /// <summary>
        /// Tracks the single Dot period Line.
        /// </summary>
        private bool _singleDotPerLine;
        /// <summary>
        /// Tracks the fill Inclusive.
        /// </summary>
        private bool _fillInclusive;
        /// <summary>
        /// Tracks the fill Exclusive.
        /// </summary>
        private bool _fillExclusive;
        /// <summary>
        /// Tracks the fill Carry In.
        /// </summary>
        private bool _fillCarryIn;
        /// <summary>
        /// Tracks the fill Carry.
        /// </summary>
        private bool _fillCarry;
        /// <summary>
        /// Tracks the row A.
        /// </summary>
        private uint _rowA;
        /// <summary>
        /// Tracks the row B.
        /// </summary>
        private uint _rowB;
        /// <summary>
        /// Tracks the row C.
        /// </summary>
        private uint _rowC;
        /// <summary>
        /// Tracks the row D.
        /// </summary>
        private uint _rowD;
        /// <summary>
        /// Tracks the amod.
        /// </summary>
        private int _amod;
        /// <summary>
        /// Tracks the bmod.
        /// </summary>
        private int _bmod;
        /// <summary>
        /// Tracks the cmod.
        /// </summary>
        private int _cmod;
        /// <summary>
        /// Tracks the dmod.
        /// </summary>
        private int _dmod;
        /// <summary>
        /// Tracks the afwm.
        /// </summary>
        private ushort _afwm;
        /// <summary>
        /// Tracks the alwm.
        /// </summary>
        private ushort _alwm;
        /// <summary>
        /// Tracks the adat.
        /// </summary>
        private ushort _adat;
        /// <summary>
        /// Tracks the bdat.
        /// </summary>
        private ushort _bdat;
        /// <summary>
        /// Tracks the cdat.
        /// </summary>
        private ushort _cdat;
        /// <summary>
        /// Tracks the previous A.
        /// </summary>
        private ushort _previousA;
        /// <summary>
        /// Tracks the previous B.
        /// </summary>
        private ushort _previousB;
        /// <summary>
        /// Tracks the block micro-operation A address for the current word.
        /// </summary>
        private uint _microAddressA;
        /// <summary>
        /// Tracks the block micro-operation B address for the current word.
        /// </summary>
        private uint _microAddressB;
        /// <summary>
        /// Tracks the block micro-operation C address for the current word.
        /// </summary>
        private uint _microAddressC;
        /// <summary>
        /// Tracks the block micro-operation D address for the current word.
        /// </summary>
        private uint _microAddressD;
        /// <summary>
        /// Tracks the block micro-operation first/last word mask for the current word.
        /// </summary>
        private ushort _microMask;
        /// <summary>
        /// Tracks the raw source A value latched by its DMA slot.
        /// </summary>
        private ushort _microRawA;
        /// <summary>
        /// Tracks the raw source B value latched by its DMA slot.
        /// </summary>
        private ushort _microRawB;
        /// <summary>
        /// Tracks the raw source C value latched by its DMA slot.
        /// </summary>
        private ushort _microRawC;
        /// <summary>
        /// Tracks the shifted source A value calculated after source reads complete.
        /// </summary>
        private ushort _microShiftedA;
        /// <summary>
        /// Tracks the shifted source B value calculated after source reads complete.
        /// </summary>
        private ushort _microShiftedB;
        /// <summary>
        /// Tracks the current word result before fill logic.
        /// </summary>
        private ushort _microResultBeforeFill;
        /// <summary>
        /// Tracks the current word result after fill logic.
        /// </summary>
        private ushort _microResult;
        /// <summary>
        /// Tracks the fill carry before the current word result is calculated.
        /// </summary>
        private bool _microFillCarryIn;
        /// <summary>
        /// Tracks whether the current micro-operation word result has been calculated.
        /// </summary>
        private bool _microResultComputed;
        /// <summary>
        /// Tracks the line Error.
        /// </summary>
        private int _lineError;
        /// <summary>
        /// Tracks the line X.
        /// </summary>
        private int _lineX;
        /// <summary>
        /// Tracks the line Y.
        /// </summary>
        private int _lineY;
        /// <summary>
        /// Tracks the line Last Drawn Y.
        /// </summary>
        private int _lineLastDrawnY;
        /// <summary>
        /// Tracks the line Texture Bit.
        /// </summary>
        private int _lineTextureBit;
        /// <summary>
        /// Tracks the line Slots period Pixel.
        /// </summary>
        private int _lineSlotsPerPixel;
        /// <summary>
        /// Tracks the line Slot Phase.
        /// </summary>
        private int _lineSlotPhase;
        /// <summary>
        /// Tracks the line Sud.
        /// </summary>
        private bool _lineSud;
        /// <summary>
        /// Tracks the line Sul.
        /// </summary>
        private bool _lineSul;
        /// <summary>
        /// Tracks the line Aul.
        /// </summary>
        private bool _lineAul;
        /// <summary>
        /// Tracks the line Sign.
        /// </summary>
        private bool _lineSign;
        /// <summary>
        /// Tracks the busy Visible.
        /// </summary>
        private bool _busyVisible;

        /// <summary>
        /// Raised when the Blitter writes or executes traced chip-memory words.
        /// </summary>
        public event Action<uint, ushort> ChipWordWritten;

        /// <summary>
        /// Raised when the Blitter writes or executes traced chip-memory words.
        /// </summary>
        public event Action<BlitterWordTraceEntry> WordExecuted;

        /// <summary>
        /// Initializes a new instance of the Blitter class.
        /// </summary>
        public Blitter(CustomRegisterMap registers)
            : this(registers, 512 * 1024)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Blitter class.
        /// </summary>
        public Blitter(CustomRegisterMap registers, int chipRamBytes)
        {
            _registers = registers;
            _dmaAddress = new ChipDmaAddress(chipRamBytes);
            _zeroFlag = true;
        }

        /// <summary>
        /// Executes the Busy logic.
        /// </summary>
        public bool Busy
        {
            get { return _remainingSlots > 0 && _busyVisible; }
        }

        /// <summary>
        /// Executes the DMA Pending logic.
        /// </summary>
        public bool DmaPending
        {
            get { return _remainingSlots > 0; }
        }

        /// <summary>
        /// Executes the Zero Flag logic.
        /// </summary>
        public bool ZeroFlag
        {
            get { return _zeroFlag; }
        }

        /// <summary>
        /// Attaches Chip Memory to this component.
        /// </summary>
        public void AttachChipMemory(Func<uint, ushort> readChipWord, Action<uint, ushort> writeChipWord)
        {
            _readChipWord = readChipWord;
            _writeChipWord = writeChipWord;
        }

        /// <summary>
        /// Attaches Interrupt Requester to this component.
        /// </summary>
        public void AttachInterruptRequester(Action<CustomInterruptSource> requestInterrupt)
        {
            _requestInterrupt = requestInterrupt;
        }

        /// <summary>
        /// Writes the Blitter pipeline, address, fill, line, and visibility state to a savestate.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            StateSerializer.WriteObjectFields(writer, this, "_registers", "_readChipWord", "_writeChipWord", "_requestInterrupt");
        }

        /// <summary>
        /// Restores the Blitter pipeline, address, fill, line, and visibility state from a savestate.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            StateSerializer.ReadObjectFields(reader, this, "_registers", "_readChipWord", "_writeChipWord", "_requestInterrupt");
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            _remainingSlots = 0;
            _zeroFlag = true;
            _widthWords = 0;
            _heightLines = 0;
            _currentWord = 0;
            _currentLine = 0;
            _previousA = 0;
            _previousB = 0;
            _lineMode = false;
            _fillCarry = false;
            _busyVisible = false;
        }

        /// <summary>
        /// Executes the Start Block blitter logic.
        /// </summary>
        public void StartBlockBlit(int widthWords, int heightLines)
        {
            _widthWords = System.Math.Max(0, widthWords);
            _heightLines = System.Math.Max(0, heightLines);
            _remainingSlots = _widthWords * _heightLines;
            _busyVisible = _remainingSlots > 0;
        }

        /// <summary>
        /// Notifies this component about Custom Register Write.
        /// </summary>
        public void NotifyCustomRegisterWrite(uint address, int size)
        {
            uint offset = address & 0x01FFu;
            if ((size == 2 && (offset & 0x01FEu) == Bltsize) ||
                (size == 1 && offset == Bltsize + 1u))
            {
                StartBlockBlitFromRegisters();
            }
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick(bool dmaSlotAvailable)
        {
            if (dmaSlotAvailable && _remainingSlots > 0)
            {
                ExecuteOneWord();
            }
        }

        /// <summary>
        /// Executes the Start Block blitter From Registers logic.
        /// </summary>
        private void StartBlockBlitFromRegisters()
        {
            if (_registers == null || _readChipWord == null || _writeChipWord == null)
            {
                return;
            }

            ushort size = Read(BltssizeCompat);
            int widthWords = size & 0x003F;
            int height = (size >> 6) & 0x03FF;
            if (widthWords == 0)
            {
                widthWords = 64;
            }

            if (height == 0)
            {
                height = 1024;
            }

            ushort bltcon0 = Read(Bltcon0);
            ushort bltcon1 = Read(Bltcon1);
            _widthWords = widthWords;
            _heightLines = height;
            _currentWord = 0;
            _currentLine = 0;
            _blockSlotPhase = 0;
            _useA = (bltcon0 & 0x0800) != 0;
            _useB = (bltcon0 & 0x0400) != 0;
            _useC = (bltcon0 & 0x0200) != 0;
            _useD = (bltcon0 & 0x0100) != 0;
            _blockSlotsPerWord = Math.Max(1, CountActiveChannels(_useA, _useB, _useC, _useD));
            _minterm = (byte)(bltcon0 & 0x00FF);
            _shiftA = (bltcon0 >> 12) & 0x0F;
            _shiftB = (bltcon1 >> 12) & 0x0F;
            _descending = (bltcon1 & 0x0002) != 0;
            bool hardwareLineMode = (bltcon1 & 0x0001) != 0;
            _lineMode = hardwareLineMode;
            _singleDotPerLine = (bltcon1 & 0x0002) != 0;
            _fillCarryIn = (bltcon1 & 0x0004) != 0;
            _fillInclusive = !hardwareLineMode && (bltcon1 & 0x0008) != 0;
            _fillExclusive = !hardwareLineMode && (bltcon1 & 0x0010) != 0;
            _rowA = ReadPointer(Bltapt);
            _rowB = ReadPointer(Bltbpt);
            _rowC = ReadPointer(Bltcpt);
            _rowD = ReadPointer(Bltdpt);
            _amod = SignedWord(Read(Bltamod));
            _bmod = SignedWord(Read(Bltbmod));
            _cmod = SignedWord(Read(Bltcmod));
            _dmod = SignedWord(Read(Bltdmod));
            _afwm = Read(Bltafwm);
            _alwm = Read(Bltalwm);
            _adat = Read(Bltadat);
            _bdat = Read(Bltbdat);
            _cdat = Read(Bltcdat);
            _previousA = 0;
            _previousB = 0;
            _fillCarry = _fillCarryIn;
            _lineError = (short)(_rowA & 0xFFFF);
            _lineX = _shiftA & 0x0F;
            _lineY = 0;
            _lineLastDrawnY = int.MinValue;
            _lineTextureBit = _shiftB & 0x0F;
            _lineSlotsPerPixel = Math.Max(1, widthWords);
            _lineSlotPhase = 0;
            _lineSign = (bltcon1 & 0x0040) != 0;
            _lineSud = (bltcon1 & 0x0010) != 0;
            _lineSul = (bltcon1 & 0x0008) != 0;
            _lineAul = (bltcon1 & 0x0004) != 0;

            _zeroFlag = true;
            _remainingSlots = _lineMode ? _lineSlotsPerPixel * height : _blockSlotsPerWord * widthWords * height;
            _busyVisible = _remainingSlots > 0;
            if (_remainingSlots == 0)
            {
                CompleteBlockBlit();
            }
        }

        /// <summary>
        /// Executes the Execute One Word logic.
        /// </summary>
        private void ExecuteOneWord()
        {
            _busyVisible = true;
            if (_lineMode)
            {
                ExecuteOneLineSlot();
                return;
            }

            ExecuteOneBlockSlot();
        }

        /// <summary>
        /// Executes one source-read, destination-write, or idle micro-operation for a block blit.
        /// </summary>
        private void ExecuteOneBlockSlot()
        {
            if (_blockSlotPhase == 0)
            {
                PrepareBlockWordMicroOps();
            }

            BlitterBlockChannel channel = GetBlockChannelForPhase(_blockSlotPhase);
            _remainingSlots--;
            switch (channel)
            {
                case BlitterBlockChannel.A:
                    _microRawA = ReadChip(_microAddressA);
                    break;
                case BlitterBlockChannel.B:
                    _microRawB = ReadChip(_microAddressB);
                    break;
                case BlitterBlockChannel.C:
                    _microRawC = ReadChip(_microAddressC);
                    break;
                case BlitterBlockChannel.D:
                    EnsureBlockWordResultComputed();
                    WriteChip(_microAddressD, _microResult);
                    break;
            }

            bool wordComplete = _blockSlotPhase >= _blockSlotsPerWord - 1 || _remainingSlots <= 0;
            if (!wordComplete)
            {
                _blockSlotPhase++;
                return;
            }

            _blockSlotPhase = 0;
            FinalizeBlockWordMicroOps();
        }

        /// <summary>
        /// Prepares the latched addresses and register defaults for one block-blitter word.
        /// </summary>
        private void PrepareBlockWordMicroOps()
        {
            int wordIndex = _descending ? _widthWords - 1 - _currentWord : _currentWord;
            ushort mask = 0xFFFF;
            if (wordIndex == 0)
            {
                mask &= _afwm;
            }

            if (wordIndex == _widthWords - 1)
            {
                mask &= _alwm;
            }

            _microAddressA = _rowA;
            _microAddressB = _rowB;
            _microAddressC = _rowC;
            _microAddressD = _rowD;
            _microMask = mask;
            _microRawA = _adat;
            _microRawB = _bdat;
            _microRawC = _cdat;
            _microShiftedA = 0;
            _microShiftedB = 0;
            _microResultBeforeFill = 0;
            _microResult = 0;
            _microFillCarryIn = _fillCarry;
            _microResultComputed = false;
        }

        /// <summary>
        /// Computes the current block-blitter word after all required source slots have run.
        /// </summary>
        private void EnsureBlockWordResultComputed()
        {
            if (_microResultComputed)
            {
                return;
            }

            ushort maskedA = (ushort)(_microRawA & _microMask);
            _microShiftedA = ShiftSource(_previousA, maskedA, _shiftA, _descending);
            _microShiftedB = ShiftSource(_previousB, _microRawB, _shiftB, _descending);
            _microResult = ApplyMinterm(_microShiftedA, _microShiftedB, _microRawC, _minterm);
            _microResultBeforeFill = _microResult;
            if (_fillInclusive || _fillExclusive)
            {
                _microResult = ApplyFill(_microResult, _fillInclusive, ref _fillCarry);
            }

            _microResultComputed = true;
        }

        /// <summary>
        /// Finalizes pointer, latch, fill, trace, and completion state after one block word.
        /// </summary>
        private void FinalizeBlockWordMicroOps()
        {
            EnsureBlockWordResultComputed();
            ushort maskedA = (ushort)(_microRawA & _microMask);
            if (_microResult != 0)
            {
                _zeroFlag = false;
            }

            RaiseWordExecuted(new BlitterWordTraceEntry
            {
                LineMode = false,
                Line = _currentLine,
                Word = _currentWord,
                AAddress = _microAddressA,
                BAddress = _microAddressB,
                CAddress = _microAddressC,
                DAddress = _microAddressD,
                RawA = maskedA,
                RawB = _microRawB,
                RawC = _microRawC,
                ShiftedA = _microShiftedA,
                ShiftedB = _microShiftedB,
                Mask = _microMask,
                ResultBeforeFill = _microResultBeforeFill,
                Result = _microResult,
                Minterm = _minterm,
                UseA = _useA,
                UseB = _useB,
                UseC = _useC,
                UseD = _useD,
                FillInclusive = _fillInclusive,
                FillExclusive = _fillExclusive,
                FillCarryIn = _microFillCarryIn,
                FillCarryOut = _fillCarry
            });

            _previousA = maskedA;
            _previousB = _microRawB;
            if (_useA)
            {
                _rowA = Advance(_rowA, _descending);
            }

            if (_useB)
            {
                _rowB = Advance(_rowB, _descending);
            }

            if (_useC)
            {
                _rowC = Advance(_rowC, _descending);
            }

            if (_useD)
            {
                _rowD = Advance(_rowD, _descending);
            }

            if (_useA)
            {
                _adat = _microRawA;
            }

            if (_useB)
            {
                _bdat = _microRawB;
            }

            if (_useC)
            {
                _cdat = _microRawC;
            }

            _currentWord++;

            if (_currentWord >= _widthWords)
            {
                if (_useA)
                {
                    _rowA = AddSigned(_rowA, _amod, _descending);
                }

                if (_useB)
                {
                    _rowB = AddSigned(_rowB, _bmod, _descending);
                }

                if (_useC)
                {
                    _rowC = AddSigned(_rowC, _cmod, _descending);
                }

                if (_useD)
                {
                    _rowD = AddSigned(_rowD, _dmod, _descending);
                }

                _previousA = 0;
                _previousB = 0;
                _fillCarry = _fillCarryIn;
                _currentWord = 0;
                _currentLine++;
            }

            if (_remainingSlots == 0 || _currentLine >= _heightLines)
            {
                CompleteBlockBlit();
            }
        }

        /// <summary>
        /// Returns the active block-blitter channel assigned to a micro-operation phase.
        /// </summary>
        private BlitterBlockChannel GetBlockChannelForPhase(int phase)
        {
            int current = 0;
            if (_useA)
            {
                if (phase == current)
                {
                    return BlitterBlockChannel.A;
                }

                current++;
            }

            if (_useB)
            {
                if (phase == current)
                {
                    return BlitterBlockChannel.B;
                }

                current++;
            }

            if (_useC)
            {
                if (phase == current)
                {
                    return BlitterBlockChannel.C;
                }

                current++;
            }

            if (_useD)
            {
                if (phase == current)
                {
                    return BlitterBlockChannel.D;
                }
            }

            return BlitterBlockChannel.Idle;
        }

        /// <summary>
        /// Executes the Execute One Line Slot logic.
        /// </summary>
        private void ExecuteOneLineSlot()
        {
            _remainingSlots--;
            _lineSlotPhase++;
            if (_lineSlotPhase < _lineSlotsPerPixel && _remainingSlots > 0)
            {
                return;
            }

            _lineSlotPhase = 0;
            ExecuteOneLinePixel();

            if (_remainingSlots == 0 || _currentLine >= _heightLines)
            {
                CompleteBlockBlit();
            }
        }

        /// <summary>
        /// Executes the Execute One Line Pixel logic.
        /// </summary>
        private void ExecuteOneLinePixel()
        {
            bool draw = !_singleDotPerLine || _lineY != _lineLastDrawnY;
            uint addressC = _rowC;
            uint addressD = _rowD;
            int lineError = _lineError;
            int lineX = _lineX;
            int lineY = _lineY;
            ushort rawC = _useC ? ReadChip(_rowC) : _cdat;
            ushort result = rawC;
            ushort a = 0;
            ushort b = 0;
            bool wrotePixel = false;
            bool lineResultComputed = false;
            if (draw)
            {
                a = BuildLineMask();
                b = IsLineTextureBitSet() ? (ushort)0xFFFF : (ushort)0;
                result = ApplyMinterm(a, b, rawC, _minterm);
                lineResultComputed = true;
                if (_useD)
                {
                    WriteChip(_rowD, result);
                    wrotePixel = true;
                }

                _lineLastDrawnY = _lineY;
            }

            if (lineResultComputed && result != 0)
            {
                _zeroFlag = false;
            }

            _cdat = rawC;
            RaiseWordExecuted(new BlitterWordTraceEntry
            {
                LineMode = true,
                Line = _currentLine,
                Word = 0,
                AAddress = _rowA,
                BAddress = _rowB,
                CAddress = addressC,
                DAddress = addressD,
                RawA = _adat,
                RawB = _bdat,
                RawC = rawC,
                ShiftedA = a,
                ShiftedB = b,
                Mask = (ushort)(_afwm & _alwm),
                ResultBeforeFill = result,
                Result = result,
                Minterm = _minterm,
                UseA = _useA,
                UseB = _useB,
                UseC = _useC,
                UseD = _useD,
                Drawn = wrotePixel,
                LineX = lineX,
                LineY = lineY,
                LineError = lineError
            });
            _currentLine++;
            AdvanceLineTexture();
            StepLine();
        }

        /// <summary>
        /// Executes the Complete Block blitter logic.
        /// </summary>
        private void CompleteBlockBlit()
        {
            if (_lineMode)
            {
                ushort bltcon0 = Read(Bltcon0);
                ushort bltcon1 = Read(Bltcon1);
                bltcon0 = (ushort)((bltcon0 & 0x0FFF) | ((_lineX & 0x0F) << 12));
                bltcon1 = (ushort)((bltcon1 & 0x0FBF) | ((_lineTextureBit & 0x0F) << 12));
                if (_lineSign)
                {
                    bltcon1 |= 0x0040;
                }

                _registers.WriteWord(Bltcon0, bltcon0);
                _registers.WriteWord(Bltcon1, bltcon1);
            }

            WritePointer(Bltapt, _rowA);
            WritePointer(Bltbpt, _rowB);
            WritePointer(Bltcpt, _rowC);
            WritePointer(Bltdpt, _rowD);
            _registers.WriteWord(Bltadat, _adat);
            _registers.WriteWord(Bltbdat, _bdat);
            _registers.WriteWord(Bltcdat, _cdat);
            _remainingSlots = 0;
            _busyVisible = false;
            if (_requestInterrupt != null)
            {
                _requestInterrupt(CustomInterruptSource.Blitter);
            }
        }

        /// <summary>
        /// Defines the Bltssize Compat constant.
        /// </summary>
        private const uint BltssizeCompat = Bltsize;

        /// <summary>
        /// Reads a Blitter custom register word from the register map.
        /// </summary>
        private ushort Read(uint offset)
        {
            return _registers.ReadWord(offset);
        }

        /// <summary>
        /// Reads a DMA word from chip memory using the Blitter address mask.
        /// </summary>
        private ushort ReadChip(uint address)
        {
            return _readChipWord(_dmaAddress.Normalize(address));
        }

        /// <summary>
        /// Writes a DMA word to chip memory and emits the trace notification.
        /// </summary>
        private void WriteChip(uint address, ushort value)
        {
            uint normalized = _dmaAddress.Normalize(address);
            _writeChipWord(normalized, value);
            Action<uint, ushort> handler = ChipWordWritten;
            if (handler != null)
            {
                handler(normalized, value);
            }
        }

        /// <summary>
        /// Raises the Word Executed notification.
        /// </summary>
        private void RaiseWordExecuted(BlitterWordTraceEntry entry)
        {
            Action<BlitterWordTraceEntry> handler = WordExecuted;
            if (handler != null)
            {
                handler(entry);
            }
        }

        /// <summary>
        /// Reads a Blitter pointer from the high and low register pair at the given offset.
        /// </summary>
        private uint ReadPointer(uint offset)
        {
            ushort high = Read(offset);
            ushort low = Read(offset + 2);
            return _dmaAddress.FromRegisters(high, low);
        }

        /// <summary>
        /// Writes a Blitter pointer into the high and low register pair at the given offset.
        /// </summary>
        private void WritePointer(uint offset, uint value)
        {
            _registers.WriteWord(offset, (ushort)(value >> 16));
            _registers.WriteWord(offset + 2, (ushort)value);
        }

        /// <summary>
        /// Executes the Shift Source logic.
        /// </summary>
        private static ushort ShiftSource(ushort previous, ushort current, int shift, bool descending)
        {
            if (shift == 0)
            {
                return current;
            }

            if (!descending)
            {
                uint combined = (uint)((previous << 16) | current);
                return (ushort)((combined >> shift) & 0xFFFF);
            }

            uint reversed = ((uint)current << 16) | previous;
            return (ushort)((reversed << shift) >> 16);
        }

        /// <summary>
        /// Applies Minterm to the current state.
        /// </summary>
        private static ushort ApplyMinterm(ushort a, ushort b, ushort c, byte minterm)
        {
            ushort result = 0;
            for (int bit = 0; bit < 16; bit++)
            {
                int selector = 0;
                if ((a & (1 << bit)) != 0)
                {
                    selector |= 4;
                }

                if ((b & (1 << bit)) != 0)
                {
                    selector |= 2;
                }

                if ((c & (1 << bit)) != 0)
                {
                    selector |= 1;
                }

                if ((minterm & (1 << selector)) != 0)
                {
                    result |= (ushort)(1 << bit);
                }
            }

            return result;
        }

        /// <summary>
        /// Applies Fill to the current state.
        /// </summary>
        private static ushort ApplyFill(ushort value, bool inclusive, ref bool carry)
        {
            ushort result = 0;
            for (int bit = 0; bit < 16; bit++)
            {
                bool input = (value & (1 << bit)) != 0;
                bool output = inclusive ? input || carry : input ^ carry;
                if (output)
                {
                    result |= (ushort)(1 << bit);
                }

                if (input)
                {
                    carry = !carry;
                }
            }

            return result;
        }

        /// <summary>
        /// Builds the Line Mask.
        /// </summary>
        private ushort BuildLineMask()
        {
            return (ushort)((_adat & _afwm & _alwm) >> (_lineX & 0x0F));
        }

        /// <summary>
        /// Returns whether Line Texture Bit Set is true.
        /// </summary>
        private bool IsLineTextureBitSet()
        {
            return (_bdat & (1 << (_lineTextureBit & 0x0F))) != 0;
        }

        /// <summary>
        /// Executes the Advance Line Texture logic.
        /// </summary>
        private void AdvanceLineTexture()
        {
            _lineTextureBit = (_lineTextureBit - 1) & 0x0F;
        }

        /// <summary>
        /// Executes the Step Line logic.
        /// </summary>
        private void StepLine()
        {
            bool takeMinorStep = !_lineSign;
            if (_lineSign)
            {
                _lineError = (short)(_lineError + _bmod);
            }
            else
            {
                _lineError = (short)(_lineError + _amod);
            }

            if (takeMinorStep)
            {
                StepLineMinorAxis();
            }

            StepLineMajorAxis();
            _lineSign = _lineError < 0;
            _rowA = (uint)(_lineError & 0xFFFF);
        }

        /// <summary>
        /// Executes the Step Line Major Axis logic.
        /// </summary>
        private void StepLineMajorAxis()
        {
            if (_lineSud)
            {
                StepLineHorizontal(_lineAul ? -1 : 1);
                return;
            }

            StepLineVertical(_lineAul ? -1 : 1);
        }

        /// <summary>
        /// Executes the Step Line Minor Axis logic.
        /// </summary>
        private void StepLineMinorAxis()
        {
            if (_lineSud)
            {
                StepLineVertical(_lineSul ? -1 : 1);
                return;
            }

            StepLineHorizontal(_lineSul ? -1 : 1);
        }

        /// <summary>
        /// Executes the Step Line Horizontal logic.
        /// </summary>
        private void StepLineHorizontal(int direction)
        {
            _lineX += direction;
            if (direction > 0)
            {
                if ((_lineX & 0x0F) == 0)
                {
                    _rowC = _dmaAddress.Add(_rowC, 2u);
                    AdvanceLineDestinationHorizontal(2);
                }

                return;
            }

            if ((_lineX & 0x0F) == 0x0F)
            {
                _rowC = _dmaAddress.Add(_rowC, -2);
                AdvanceLineDestinationHorizontal(-2);
            }
        }

        /// <summary>
        /// Executes the Step Line Vertical logic.
        /// </summary>
        private void StepLineVertical(int direction)
        {
            _lineY += direction;
            int cDelta = direction > 0 ? _cmod : -_cmod;
            int dDelta = direction > 0 ? _dmod : -_dmod;
            _rowC = AddSigned(_rowC, cDelta, false);
            _rowD = _useC ? _rowC : AddSigned(_rowD, dDelta, false);
        }

        /// <summary>
        /// Executes the Advance Line Destination Horizontal logic.
        /// </summary>
        private void AdvanceLineDestinationHorizontal(int delta)
        {
            _rowD = _useC ? _rowC : _dmaAddress.Add(_rowD, delta);
        }

        /// <summary>
        /// Executes the Signed Word logic.
        /// </summary>
        private static int SignedWord(ushort value)
        {
            return (short)value;
        }

        /// <summary>
        /// Executes the Advance logic.
        /// </summary>
        private uint Advance(uint address, bool descending)
        {
            return descending ? _dmaAddress.Add(address, -2) : _dmaAddress.Add(address, 2u);
        }

        /// <summary>
        /// Executes the Add Signed logic.
        /// </summary>
        private uint AddSigned(uint address, int modulo, bool descending)
        {
            return _dmaAddress.Add(address, modulo);
        }

        /// <summary>
        /// Executes the Count Active Channels logic.
        /// </summary>
        private static int CountActiveChannels(bool a, bool b, bool c, bool d)
        {
            int count = 0;
            if (a)
            {
                count++;
            }

            if (b)
            {
                count++;
            }

            if (c)
            {
                count++;
            }

            if (d)
            {
                count++;
            }

            return count;
        }
    }
}
