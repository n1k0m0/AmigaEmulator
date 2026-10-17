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
        /// Constant for Bltcon 0.
        /// </summary>
        private const uint Bltcon0 = 0x040;
        /// <summary>
        /// Constant for Bltcon 1.
        /// </summary>
        private const uint Bltcon1 = 0x042;
        /// <summary>
        /// Constant for Bltafwm.
        /// </summary>
        private const uint Bltafwm = 0x044;
        /// <summary>
        /// Constant for Bltalwm.
        /// </summary>
        private const uint Bltalwm = 0x046;
        /// <summary>
        /// Constant for Bltcpt.
        /// </summary>
        private const uint Bltcpt = 0x048;
        /// <summary>
        /// Constant for Bltbpt.
        /// </summary>
        private const uint Bltbpt = 0x04C;
        /// <summary>
        /// Constant for Bltapt.
        /// </summary>
        private const uint Bltapt = 0x050;
        /// <summary>
        /// Constant for Bltdpt.
        /// </summary>
        private const uint Bltdpt = 0x054;
        /// <summary>
        /// Constant for Bltsize.
        /// </summary>
        private const uint Bltsize = 0x058;
        /// <summary>
        /// Constant for Bltcmod.
        /// </summary>
        private const uint Bltcmod = 0x060;
        /// <summary>
        /// Constant for Bltbmod.
        /// </summary>
        private const uint Bltbmod = 0x062;
        /// <summary>
        /// Constant for Bltamod.
        /// </summary>
        private const uint Bltamod = 0x064;
        /// <summary>
        /// Constant for Bltdmod.
        /// </summary>
        private const uint Bltdmod = 0x066;
        /// <summary>
        /// Constant for Bltcdat.
        /// </summary>
        private const uint Bltcdat = 0x070;
        /// <summary>
        /// Constant for Bltbdat.
        /// </summary>
        private const uint Bltbdat = 0x072;
        /// <summary>
        /// Constant for Bltadat.
        /// </summary>
        private const uint Bltadat = 0x074;

        /// <summary>
        /// Stores registers.
        /// </summary>
        private readonly CustomRegisterMap _registers;
        /// <summary>
        /// Stores DMA Address.
        /// </summary>
        private readonly ChipDmaAddress _dmaAddress;
        /// <summary>
        /// Stores read Chip Word.
        /// </summary>
        private Func<uint, ushort> _readChipWord;
        /// <summary>
        /// Stores write Chip Word.
        /// </summary>
        private Action<uint, ushort> _writeChipWord;
        /// <summary>
        /// Stores request Interrupt.
        /// </summary>
        private Action<CustomInterruptSource> _requestInterrupt;
        /// <summary>
        /// Stores remaining Slots.
        /// </summary>
        private int _remainingSlots;
        /// <summary>
        /// Stores zero Flag.
        /// </summary>
        private bool _zeroFlag;
        /// <summary>
        /// Stores width Words.
        /// </summary>
        private int _widthWords;
        /// <summary>
        /// Stores height Lines.
        /// </summary>
        private int _heightLines;
        /// <summary>
        /// Stores current Word.
        /// </summary>
        private int _currentWord;
        /// <summary>
        /// Stores current Line.
        /// </summary>
        private int _currentLine;
        /// <summary>
        /// Stores block Slots Per Word.
        /// </summary>
        private int _blockSlotsPerWord;
        /// <summary>
        /// Stores block Slot Phase.
        /// </summary>
        private int _blockSlotPhase;
        /// <summary>
        /// Stores use A.
        /// </summary>
        private bool _useA;
        /// <summary>
        /// Stores use B.
        /// </summary>
        private bool _useB;
        /// <summary>
        /// Stores use C.
        /// </summary>
        private bool _useC;
        /// <summary>
        /// Stores use D.
        /// </summary>
        private bool _useD;
        /// <summary>
        /// Stores minterm.
        /// </summary>
        private byte _minterm;
        /// <summary>
        /// Stores shift A.
        /// </summary>
        private int _shiftA;
        /// <summary>
        /// Stores shift B.
        /// </summary>
        private int _shiftB;
        /// <summary>
        /// Stores descending.
        /// </summary>
        private bool _descending;
        /// <summary>
        /// Stores line Mode.
        /// </summary>
        private bool _lineMode;
        /// <summary>
        /// Stores single Dot Per Line.
        /// </summary>
        private bool _singleDotPerLine;
        /// <summary>
        /// Stores fill Inclusive.
        /// </summary>
        private bool _fillInclusive;
        /// <summary>
        /// Stores fill Exclusive.
        /// </summary>
        private bool _fillExclusive;
        /// <summary>
        /// Stores fill Carry In.
        /// </summary>
        private bool _fillCarryIn;
        /// <summary>
        /// Stores fill Carry.
        /// </summary>
        private bool _fillCarry;
        /// <summary>
        /// Stores row A.
        /// </summary>
        private uint _rowA;
        /// <summary>
        /// Stores row B.
        /// </summary>
        private uint _rowB;
        /// <summary>
        /// Stores row C.
        /// </summary>
        private uint _rowC;
        /// <summary>
        /// Stores row D.
        /// </summary>
        private uint _rowD;
        /// <summary>
        /// Stores amod.
        /// </summary>
        private int _amod;
        /// <summary>
        /// Stores bmod.
        /// </summary>
        private int _bmod;
        /// <summary>
        /// Stores cmod.
        /// </summary>
        private int _cmod;
        /// <summary>
        /// Stores dmod.
        /// </summary>
        private int _dmod;
        /// <summary>
        /// Stores afwm.
        /// </summary>
        private ushort _afwm;
        /// <summary>
        /// Stores alwm.
        /// </summary>
        private ushort _alwm;
        /// <summary>
        /// Stores adat.
        /// </summary>
        private ushort _adat;
        /// <summary>
        /// Stores bdat.
        /// </summary>
        private ushort _bdat;
        /// <summary>
        /// Stores cdat.
        /// </summary>
        private ushort _cdat;
        /// <summary>
        /// Stores previous A.
        /// </summary>
        private ushort _previousA;
        /// <summary>
        /// Stores previous B.
        /// </summary>
        private ushort _previousB;
        /// <summary>
        /// Stores the shifted B source hold value used when B DMA is disabled.
        /// </summary>
        private ushort _bhold;
        /// <summary>
        /// Stores micro Address A.
        /// </summary>
        private uint _microAddressA;
        /// <summary>
        /// Stores micro Address B.
        /// </summary>
        private uint _microAddressB;
        /// <summary>
        /// Stores micro Address C.
        /// </summary>
        private uint _microAddressC;
        /// <summary>
        /// Stores micro Address D.
        /// </summary>
        private uint _microAddressD;
        /// <summary>
        /// Stores micro Mask.
        /// </summary>
        private ushort _microMask;
        /// <summary>
        /// Stores micro Raw A.
        /// </summary>
        private ushort _microRawA;
        /// <summary>
        /// Stores micro Raw B.
        /// </summary>
        private ushort _microRawB;
        /// <summary>
        /// Stores micro Raw C.
        /// </summary>
        private ushort _microRawC;
        /// <summary>
        /// Stores micro Shifted A.
        /// </summary>
        private ushort _microShiftedA;
        /// <summary>
        /// Stores micro Shifted B.
        /// </summary>
        private ushort _microShiftedB;
        /// <summary>
        /// Stores micro Result Before Fill.
        /// </summary>
        private ushort _microResultBeforeFill;
        /// <summary>
        /// Stores micro Result.
        /// </summary>
        private ushort _microResult;
        /// <summary>
        /// Stores micro Fill Carry In.
        /// </summary>
        private bool _microFillCarryIn;
        /// <summary>
        /// Tracks whether the current micro-operation word result has been calculated.
        /// </summary>
        private bool _microResultComputed;
        /// <summary>
        /// Tracks whether the D channel has a delayed destination write pending.
        /// </summary>
        private bool _pendingDWrite;
        /// <summary>
        /// Stores the delayed D-channel destination address.
        /// </summary>
        private uint _pendingDAddress;
        /// <summary>
        /// Stores the delayed D-channel destination value.
        /// </summary>
        private ushort _pendingDValue;
        /// <summary>
        /// Stores line Error.
        /// </summary>
        private int _lineError;
        /// <summary>
        /// Stores line X.
        /// </summary>
        private int _lineX;
        /// <summary>
        /// Stores line Y.
        /// </summary>
        private int _lineY;
        /// <summary>
        /// Stores line Last Drawn Y.
        /// </summary>
        private int _lineLastDrawnY;
        /// <summary>
        /// Stores line Texture Bit.
        /// </summary>
        private int _lineTextureBit;
        /// <summary>
        /// Stores line Slots Per Pixel.
        /// </summary>
        private int _lineSlotsPerPixel;
        /// <summary>
        /// Stores line Slot Phase.
        /// </summary>
        private int _lineSlotPhase;
        /// <summary>
        /// Stores line Sud.
        /// </summary>
        private bool _lineSud;
        /// <summary>
        /// Stores line Sul.
        /// </summary>
        private bool _lineSul;
        /// <summary>
        /// Stores line Aul.
        /// </summary>
        private bool _lineAul;
        /// <summary>
        /// Stores line Sign.
        /// </summary>
        private bool _lineSign;
        /// <summary>
        /// Stores busy Visible.
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
        /// Gets Busy.
        /// </summary>
        public bool Busy
        {
            get { return _remainingSlots > 0 && _busyVisible; }
        }

        /// <summary>
        /// Gets DMA Pending.
        /// </summary>
        public bool DmaPending
        {
            get { return _remainingSlots > 0; }
        }

        /// <summary>
        /// Gets Zero Flag.
        /// </summary>
        public bool ZeroFlag
        {
            get { return _zeroFlag; }
        }

        /// <summary>
        /// Returns a compact snapshot of the active blitter pipeline for diagnostics.
        /// </summary>
        public string GetDebugInfo()
        {
            return "busy:" + Busy +
                " pending:" + DmaPending +
                " rem:" + _remainingSlots +
                " lineMode:" + _lineMode +
                " width:" + _widthWords +
                " height:" + _heightLines +
                " line:" + _currentLine +
                " word:" + _currentWord +
                " phase:" + _blockSlotPhase +
                " slotsPerWord:" + _blockSlotsPerWord +
                " ABCD:" + (_useA ? "A" : "-") + (_useB ? "B" : "-") + (_useC ? "C" : "-") + (_useD ? "D" : "-");
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
            // Older states retained the unwired modulo bit. Preserve the
            // pipeline and addresses, but normalize its subsequent row/slope steps.
            _amod &= ~1; _bmod &= ~1; _cmod &= ~1; _dmod &= ~1;
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
            _bhold = 0;
            _pendingDWrite = false;
            _pendingDAddress = 0;
            _pendingDValue = 0;
            _lineMode = false;
            _fillCarry = false;
            _busyVisible = false;
        }

        /// <summary>
        /// Runs Start Block Blit.
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
            if (size == 2 && (offset & 0x01FEu) == Bltbdat)
            {
                LoadBData(Read(Bltbdat));
            }

            if ((size == 2 && (offset & 0x01FEu) == Bltsize) ||
                (size == 1 && offset == Bltsize + 1u))
            {
                StartBlockBlitFromRegisters();
            }
            if (_registers != null && _registers.HasEcs && ((size == 2 && (offset & 0x01FEu) == 0x05E) || (size == 1 && offset == 0x05F)))
                StartBlockBlitFromRegisters(true);
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
        /// Runs Start Block Blit From Registers.
        /// </summary>
        private void StartBlockBlitFromRegisters(bool extended = false)
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

            if (extended)
            {
                widthWords = Read(0x05E) & 0x07FF;
                height = Read(0x05C) & 0x7FFF;
                if (widthWords == 0) widthWords = 2048;
                if (height == 0) height = 32768;
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
            _blockSlotsPerWord = CountBlockWordPhases(_useA, _useB, _useC, _useD);
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
            _amod = SignedModulo(Read(Bltamod));
            _bmod = SignedModulo(Read(Bltbmod));
            _cmod = SignedModulo(Read(Bltcmod));
            _dmod = SignedModulo(Read(Bltdmod));
            _afwm = Read(Bltafwm);
            _alwm = Read(Bltalwm);
            _adat = Read(Bltadat);
            _bdat = Read(Bltbdat);
            _cdat = Read(Bltcdat);
            _previousA = 0;
            _previousB = 0;
            _pendingDWrite = false;
            _pendingDAddress = 0;
            _pendingDValue = 0;
            _fillCarry = _fillCarryIn;
            _lineError = (short)(_rowA & 0xFFFF);
            _lineX = _shiftA & 0x0F;
            _lineY = 0;
            _lineLastDrawnY = int.MinValue;
            _lineTextureBit = _shiftB & 0x0F;
            _lineSlotsPerPixel = 4;
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
        /// Runs Execute One Word.
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
                    FlushPendingDWrite();
                    EnsureBlockWordResultComputed();
                    ArmPendingDWrite(_microAddressD, _microResult);
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
            int wordIndex = _currentWord;
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
            _microShiftedB = _useB
                ? ShiftSource(_previousB, _microRawB, _shiftB, _descending)
                : _bhold;
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

            // Null-conditional invocation also skips constructing the diagnostic snapshot.
            WordExecuted?.Invoke(new BlitterWordTraceEntry
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
            if (_useB)
            {
                _previousB = _microRawB;
                _bhold = _microShiftedB;
            }
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
            int activePhase = 0;
            if (_useA)
            {
                if (activePhase == phase)
                {
                    return BlitterBlockChannel.A;
                }

                activePhase++;
            }

            if (_useB)
            {
                if (activePhase == phase)
                {
                    return BlitterBlockChannel.B;
                }

                activePhase++;
            }

            if (_useC)
            {
                if (activePhase == phase)
                {
                    return BlitterBlockChannel.C;
                }

                activePhase++;
            }

            if (_useD && activePhase == phase)
            {
                return BlitterBlockChannel.D;
            }

            return BlitterBlockChannel.Idle;
        }

        /// <summary>
        /// Runs Execute One Line Slot.
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
        /// Runs Execute One Line Pixel.
        /// </summary>
        private void ExecuteOneLinePixel()
        {
            bool draw = !_singleDotPerLine || _lineY != _lineLastDrawnY;
            uint addressC = _rowC;
            uint addressD = _useC ? _rowC : _rowD;
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
                if (_useC)
                {
                    WriteChip(_rowC, result);
                    wrotePixel = true;
                }

                _lineLastDrawnY = _lineY;
            }

            if (lineResultComputed && result != 0)
            {
                _zeroFlag = false;
            }

            _cdat = rawC;
            WordExecuted?.Invoke(new BlitterWordTraceEntry
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
        /// Runs Complete Block Blit.
        /// </summary>
        private void CompleteBlockBlit()
        {
            FlushPendingDWrite();
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
        /// Constant for Bltssize Compat.
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
        /// Arms the OCS Blitter D-channel writeback latch for the next destination slot.
        /// </summary>
        private void ArmPendingDWrite(uint address, ushort value)
        {
            _pendingDAddress = address;
            _pendingDValue = value;
            _pendingDWrite = true;
        }

        /// <summary>
        /// Flushes the delayed OCS Blitter D-channel writeback latch.
        /// </summary>
        private void FlushPendingDWrite()
        {
            if (!_pendingDWrite)
            {
                return;
            }

            WriteChip(_pendingDAddress, _pendingDValue);
            _pendingDWrite = false;
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
        /// Runs Shift Source.
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
        /// Loads BLTBDAT and updates the B source hold latch just like a B-channel DMA fetch.
        /// </summary>
        private void LoadBData(ushort value)
        {
            int shift = (Read(Bltcon1) >> 12) & 0x0F;
            bool descending = (Read(Bltcon1) & 0x0002) != 0;
            _bdat = value;
            _bhold = ShiftSource(_previousB, value, shift, descending);
            _previousB = value;
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
        /// Runs Advance Line Texture.
        /// </summary>
        private void AdvanceLineTexture()
        {
            _lineTextureBit = (_lineTextureBit - 1) & 0x0F;
        }

        /// <summary>
        /// Runs Step Line.
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
        /// Runs Step Line Major Axis.
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
        /// Runs Step Line Minor Axis.
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
        /// Runs Step Line Horizontal.
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
        /// Runs Step Line Vertical.
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
        /// Runs Advance Line Destination Horizontal.
        /// </summary>
        private void AdvanceLineDestinationHorizontal(int delta)
        {
            _rowD = _useC ? _rowC : _dmaAddress.Add(_rowD, delta);
        }

        /// <summary>
        /// Decodes the signed, word-aligned BLTxMOD register.
        /// </summary>
        private static int SignedModulo(ushort value)
        {
            // Bit zero is not connected. Clear it before negating a descending
            // stride or adding a line-mode slope; pointer masking afterwards
            // rounds an odd negative step down by an extra word.
            return (short)(value & 0xFFFE);
        }

        private uint Advance(uint address, bool descending)
        {
            return descending ? _dmaAddress.Add(address, -2) : _dmaAddress.Add(address, 2u);
        }

        /// <summary>
        /// Runs Add Signed.
        /// </summary>
        private uint AddSigned(uint address, int modulo, bool descending)
        {
            return _dmaAddress.Add(address, descending ? -modulo : modulo);
        }

        /// <summary>
        /// Returns the number of bus phases a block-blit word occupies.
        /// </summary>
        private static int CountBlockWordPhases(bool a, bool b, bool c, bool d)
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
