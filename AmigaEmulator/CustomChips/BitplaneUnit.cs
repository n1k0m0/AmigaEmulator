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
    /// Tracks bitplane registers, DMA fetches, modulos, and latched pixel data.
    /// </summary>
    public sealed class BitplaneUnit
    {
        /// <summary>
        /// Defines the Pal Visible Top constant.
        /// </summary>
        public const int PalVisibleTop = 44;
        /// <summary>
        /// Defines the Pal Visible Height constant.
        /// </summary>
        public const int PalVisibleHeight = 256;
        /// <summary>
        /// Defines the Lowres Pixels period Chip Cycle constant.
        /// </summary>
        public const int LowresPixelsPerChipCycle = 2;
        /// <summary>
        /// Defines the Hires Pixels period Chip Cycle constant.
        /// </summary>
        public const int HiresPixelsPerChipCycle = 4;

        /// <summary>
        /// Defines the DMACON custom register offset or bit mask.
        /// </summary>
        private const uint Dmacon = 0x096;
        /// <summary>
        /// Defines the Diwstrt constant.
        /// </summary>
        private const uint Diwstrt = 0x08E;
        /// <summary>
        /// Defines the Diwstop constant.
        /// </summary>
        private const uint Diwstop = 0x090;
        /// <summary>
        /// Defines the Ddfstrt constant.
        /// </summary>
        private const uint Ddfstrt = 0x092;
        /// <summary>
        /// Defines the Ddfstop constant.
        /// </summary>
        private const uint Ddfstop = 0x094;
        /// <summary>
        /// Defines the BPLCON0 custom register offset or bit mask.
        /// </summary>
        private const uint Bplcon0 = 0x100;
        /// <summary>
        /// Defines the BPLCON1 custom register offset or bit mask.
        /// </summary>
        private const uint Bplcon1 = 0x102;
        /// <summary>
        /// Defines the BPLCON2 custom register offset or bit mask.
        /// </summary>
        private const uint Bplcon2 = 0x104;
        /// <summary>
        /// Defines the BPL1MOD custom register offset or bit mask.
        /// </summary>
        private const uint Bpl1mod = 0x108;
        /// <summary>
        /// Defines the BPL2MOD custom register offset or bit mask.
        /// </summary>
        private const uint Bpl2mod = 0x10A;
        /// <summary>
        /// Defines the bitplane Data Base constant.
        /// </summary>
        private const uint BplDataBase = 0x110;
        /// <summary>
        /// Defines the BITPLANE POINTER BASE custom register offset or bit mask.
        /// </summary>
        private const uint BplPointerBase = 0x0E0;
        /// <summary>
        /// Defines the Color Base constant.
        /// </summary>
        private const uint ColorBase = 0x180;
        /// <summary>
        /// Defines the Max Planes constant.
        /// </summary>
        private const int MaxPlanes = 6;
        /// <summary>
        /// Defines the Max Fetched Words constant.
        /// </summary>
        private const int MaxFetchedWords = 128;

        /// <summary>
        /// Tracks the registers.
        /// </summary>
        private readonly CustomRegisterMap _registers;
        /// <summary>
        /// Tracks the fetched Words.
        /// </summary>
        private readonly ushort[,] _fetchedWords;
        /// <summary>
        /// Tracks the fetched Rows.
        /// </summary>
        private readonly int[,] _fetchedRows;
        /// <summary>
        /// Tracks the pending Fetched Words.
        /// </summary>
        private readonly ushort[,] _pendingFetchedWords;
        /// <summary>
        /// Tracks the pending Fetched Rows.
        /// </summary>
        private readonly int[] _pendingFetchedRows;
        /// <summary>
        /// Tracks the pending Fetched Plane Mask.
        /// </summary>
        private readonly int[] _pendingFetchedPlaneMask;
        /// <summary>
        /// Tracks the data Latches.
        /// </summary>
        private readonly ushort[] _dataLatches;
        /// <summary>
        /// Tracks the data Latch Valid.
        /// </summary>
        private readonly bool[] _dataLatchValid;
        /// <summary>
        /// Tracks the DMA Pointers.
        /// </summary>
        private readonly uint[] _dmaPointers;
        /// <summary>
        /// Tracks the last Pointer Register Values.
        /// </summary>
        private readonly uint[] _lastPointerRegisterValues;
        /// <summary>
        /// Tracks the DMA Address.
        /// </summary>
        private readonly ChipDmaAddress _dmaAddress;
        /// <summary>
        /// Tracks the read Chip Word.
        /// </summary>
        private Func<uint, ushort> _readChipWord;
        /// <summary>
        /// Tracks the active DMA Row.
        /// </summary>
        private int _activeDmaRow;
        /// <summary>
        /// Tracks the last Fetched Word Index.
        /// </summary>
        private int _lastFetchedWordIndex;
        /// <summary>
        /// Tracks the completed DMA Line.
        /// </summary>
        private bool _completedDmaLine;
        /// <summary>
        /// Tracks the has Pointer Register Snapshot.
        /// </summary>
        private bool _hasPointerRegisterSnapshot;
        /// <summary>
        /// Tracks the split Fetch Active.
        /// </summary>
        private bool _splitFetchActive;
        /// <summary>
        /// Tracks the DMA Active.
        /// </summary>
        private bool _dmaActive;
        /// <summary>
        /// Tracks the display Active.
        /// </summary>
        private bool _displayActive;
        /// <summary>
        /// Tracks the diw Top.
        /// </summary>
        private int _diwTop;
        /// <summary>
        /// Tracks the diw Bottom.
        /// </summary>
        private int _diwBottom;
        /// <summary>
        /// Tracks the ddf Start.
        /// </summary>
        private int _ddfStart;
        /// <summary>
        /// Tracks the ddf Stop.
        /// </summary>
        private int _ddfStop;
        /// <summary>
        /// Tracks the words period Fetched Line.
        /// </summary>
        private int _wordsPerFetchedLine;
        /// <summary>
        /// Tracks the display Fetch Width.
        /// </summary>
        private int _displayFetchWidth;
        /// <summary>
        /// Tracks the scroll Even.
        /// </summary>
        private int _scrollEven;
        /// <summary>
        /// Tracks the scroll Odd.
        /// </summary>
        private int _scrollOdd;
        /// <summary>
        /// Tracks the hires.
        /// </summary>
        private bool _hires;

        /// <summary>
        /// Initializes a new instance of the BitplaneUnit class.
        /// </summary>
        public BitplaneUnit()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the BitplaneUnit class.
        /// </summary>
        public BitplaneUnit(CustomRegisterMap registers)
            : this(registers, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the BitplaneUnit class.
        /// </summary>
        public BitplaneUnit(CustomRegisterMap registers, Func<uint, ushort> readChipWord)
            : this(registers, readChipWord, 512 * 1024)
        {
        }

        /// <summary>
        /// Initializes a new instance of the BitplaneUnit class.
        /// </summary>
        public BitplaneUnit(CustomRegisterMap registers, Func<uint, ushort> readChipWord, int chipRamBytes)
        {
            _registers = registers;
            _readChipWord = readChipWord;
            _dmaAddress = new ChipDmaAddress(chipRamBytes);
            _fetchedWords = new ushort[MaxPlanes, MaxFetchedWords];
            _fetchedRows = new int[MaxPlanes, MaxFetchedWords];
            _pendingFetchedWords = new ushort[MaxPlanes, MaxFetchedWords];
            _pendingFetchedRows = new int[MaxFetchedWords];
            _pendingFetchedPlaneMask = new int[MaxFetchedWords];
            _dataLatches = new ushort[MaxPlanes];
            _dataLatchValid = new bool[MaxPlanes];
            _dmaPointers = new uint[MaxPlanes];
            _lastPointerRegisterValues = new uint[MaxPlanes];
            Reset();
        }

        /// <summary>
        /// Gets or updates the Enabled Planes.
        /// </summary>
        public int EnabledPlanes { get; private set; }

        /// <summary>
        /// Executes the DMA Active logic.
        /// </summary>
        public bool DmaActive
        {
            get
            {
                RefreshFromRegisters();
                return _dmaActive;
            }
        }

        /// <summary>
        /// Returns whether Chip Memory Reader is present.
        /// </summary>
        public bool HasChipMemoryReader
        {
            get { return _readChipWord != null; }
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            EnabledPlanes = 0;
            for (int plane = 0; plane < MaxPlanes; plane++)
            {
                _dmaPointers[plane] = 0;
                for (int word = 0; word < MaxFetchedWords; word++)
                {
                    _fetchedWords[plane, word] = 0;
                    _fetchedRows[plane, word] = int.MinValue;
                    _pendingFetchedWords[plane, word] = 0;
                }

                _lastPointerRegisterValues[plane] = 0;
                _dataLatches[plane] = 0;
                _dataLatchValid[plane] = false;
            }

            _activeDmaRow = int.MinValue;
            _lastFetchedWordIndex = -1;
            _completedDmaLine = false;
            _hasPointerRegisterSnapshot = false;
            _splitFetchActive = false;
            for (int word = 0; word < MaxFetchedWords; word++)
            {
                _pendingFetchedRows[word] = int.MinValue;
                _pendingFetchedPlaneMask[word] = 0;
            }

            RefreshFromRegisters();
        }

        /// <summary>
        /// Invalidates per-frame fetch buffers before the first display DMA slots of a new frame.
        /// </summary>
        public void BeginFrame()
        {
            for (int plane = 0; plane < MaxPlanes; plane++)
            {
                for (int word = 0; word < MaxFetchedWords; word++)
                {
                    _fetchedRows[plane, word] = int.MinValue;
                    _pendingFetchedWords[plane, word] = 0;
                }
            }

            for (int word = 0; word < MaxFetchedWords; word++)
            {
                _pendingFetchedRows[word] = int.MinValue;
                _pendingFetchedPlaneMask[word] = 0;
            }

            _activeDmaRow = int.MinValue;
            _lastFetchedWordIndex = -1;
            _completedDmaLine = false;
            _splitFetchActive = false;
        }

        /// <summary>
        /// Attaches Chip Word Reader to this component.
        /// </summary>
        public void AttachChipWordReader(Func<uint, ushort> readChipWord)
        {
            _readChipWord = readChipWord;
        }

        /// <summary>
        /// Writes the bitplane DMA and display latches to a savestate.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            StateSerializer.WriteObjectFields(writer, this, "_registers", "_readChipWord");
        }

        /// <summary>
        /// Restores the bitplane DMA and display latches from a savestate.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            StateSerializer.ReadObjectFields(reader, this, "_registers", "_readChipWord");
        }

        /// <summary>
        /// Notifies this component about Custom Register Word Write.
        /// </summary>
        public void NotifyCustomRegisterWordWrite(uint registerOffset, ushort value)
        {
            registerOffset &= 0x01FE;
            if (registerOffset < BplDataBase || registerOffset >= BplDataBase + (MaxPlanes * 2u))
            {
                return;
            }

            int plane = (int)((registerOffset - BplDataBase) >> 1);
            _dataLatches[plane] = value;
            _dataLatchValid[plane] = true;
        }

        /// <summary>
        /// Executes the Refresh From Registers logic.
        /// </summary>
        public void RefreshFromRegisters()
        {
            ushort bplcon0 = Read(Bplcon0);
            ushort bplcon1 = Read(Bplcon1);
            ushort dmacon = Read(Dmacon);
            ushort diwStart = Read(Diwstrt);
            ushort diwStop = Read(Diwstop);
            ushort ddfStart = Read(Ddfstrt);
            ushort ddfStop = Read(Ddfstop);

            EnabledPlanes = Math.Min(6, (bplcon0 >> 12) & 0x07);
            _hires = (bplcon0 & 0x8000) != 0;
            _dmaActive = (dmacon & 0x0200) != 0 && (dmacon & 0x0100) != 0;
            _displayActive = EnabledPlanes > 0 && (_dmaActive || HasValidDataLatch());

            int rawTop = diwStart == 0 ? PalVisibleTop : (diwStart >> 8) & 0xFF;
            _diwTop = rawTop < PalVisibleTop ? PalVisibleTop : rawTop;
            _diwBottom = diwStop == 0 ? rawTop + PalVisibleHeight : (diwStop >> 8) & 0xFF;
            if (rawTop < PalVisibleTop && _diwBottom <= PalVisibleTop + 32)
            {
                _diwBottom = PalVisibleTop + PalVisibleHeight;
            }

            if (_diwBottom <= rawTop)
            {
                _diwBottom += 256;
            }

            int fetchMask = _hires ? ~0x03 : ~0x07;
            _ddfStart = ddfStart == 0 ? 0x38 : (ddfStart & 0x00FE) & fetchMask;
            _ddfStop = ddfStop == 0 ? 0xD0 : (ddfStop & 0x00FE) & fetchMask;
            int span = _ddfStop - _ddfStart;
            int lowresFetchGroups = span < 0 ? 0 : (span / 8) + 1;
            _wordsPerFetchedLine = _hires ? lowresFetchGroups * 2 : lowresFetchGroups;
            int bitplaneBits = _wordsPerFetchedLine * 16;
            _displayFetchWidth = _hires ? bitplaneBits : bitplaneBits * 2;
            _scrollEven = bplcon1 & 0x000F;
            _scrollOdd = (bplcon1 >> 4) & 0x000F;
        }

        /// <summary>
        /// Returns whether Bitplane Display Active is true.
        /// </summary>
        public bool IsBitplaneDisplayActive()
        {
            RefreshFromRegisters();
            return _displayActive;
        }

        /// <summary>
        /// Returns whether Bitplane Display Active Cached is true.
        /// </summary>
        public bool IsBitplaneDisplayActiveCached
        {
            get { return _displayActive; }
        }

        /// <summary>
        /// Returns whether Fetch Slot is true.
        /// </summary>
        public bool IsFetchSlot(int beamX, int beamY)
        {
            RefreshFromRegisters();
            if (!_displayActive || !IsInsideDiw(beamY))
            {
                return false;
            }

            int fetchStep = GetFetchStep();
            int fetchStopExclusive = _ddfStart + (_wordsPerFetchedLine * fetchStep);
            return beamX >= _ddfStart && beamX < fetchStopExclusive && ((beamX - _ddfStart) % fetchStep) == 0;
        }

        /// <summary>
        /// Executes the Fetch DMA Words logic.
        /// </summary>
        public void FetchDmaWords(int beamX, int beamY)
        {
            if (_readChipWord == null || !IsFetchSlot(beamX, beamY))
            {
                return;
            }

            _splitFetchActive = false;
            int row = GetScreenY(beamY);
            if (row < 0)
            {
                return;
            }

            int wordIndex = (beamX - _ddfStart) / GetFetchStep();
            if (wordIndex < 0 || wordIndex >= MaxFetchedWords)
            {
                return;
            }

            EnsureDmaLine(row, wordIndex);

            for (int plane = 0; plane < EnabledPlanes; plane++)
            {
                uint address = _dmaAddress.Normalize(_dmaPointers[plane]);
                ushort data = _readChipWord(address);
                _fetchedWords[plane, wordIndex] = data;
                _fetchedRows[plane, wordIndex] = row;
                _dataLatches[plane] = data;
                _dataLatchValid[plane] = true;
                _dmaPointers[plane] = _dmaAddress.Add(_dmaPointers[plane], 2u);
            }

            _lastFetchedWordIndex = wordIndex;
            if (wordIndex + 1 >= _wordsPerFetchedLine)
            {
                EndDmaLine();
            }
        }

        /// <summary>
        /// Returns whether Plane Fetch Slot is true.
        /// </summary>
        public bool IsPlaneFetchSlot(int beamX, int beamY)
        {
            RefreshFromRegisters();
            if (!_displayActive || !IsInsideDiw(beamY))
            {
                return false;
            }

            int fetchStep = GetFetchStep();
            int fetchStopExclusive = _ddfStart + (_wordsPerFetchedLine * fetchStep);
            int fetchOffset = beamX - _ddfStart;
            if (fetchOffset < 0 || beamX >= fetchStopExclusive)
            {
                return false;
            }

            int planeSlot = fetchOffset % fetchStep;
            return planeSlot < EnabledPlanes;
        }

        /// <summary>
        /// Executes the Fetch DMA Plane Word logic.
        /// </summary>
        public void FetchDmaPlaneWord(int beamX, int beamY)
        {
            if (_readChipWord == null || !IsPlaneFetchSlot(beamX, beamY))
            {
                return;
            }

            _splitFetchActive = true;
            int row = GetScreenY(beamY);
            if (row < 0)
            {
                return;
            }

            int fetchStep = GetFetchStep();
            int fetchOffset = beamX - _ddfStart;
            int wordIndex = fetchOffset / fetchStep;
            int plane = fetchOffset % fetchStep;
            if (wordIndex < 0 || wordIndex >= MaxFetchedWords || plane < 0 || plane >= EnabledPlanes)
            {
                return;
            }

            EnsureDmaLine(row, wordIndex);
            if (_pendingFetchedRows[wordIndex] != row)
            {
                _pendingFetchedRows[wordIndex] = row;
                _pendingFetchedPlaneMask[wordIndex] = 0;
            }

            uint address = _dmaAddress.Normalize(_dmaPointers[plane]);
            _pendingFetchedWords[plane, wordIndex] = _readChipWord(address);
            _pendingFetchedPlaneMask[wordIndex] |= 1 << plane;
            _dmaPointers[plane] = _dmaAddress.Add(_dmaPointers[plane], 2u);

            int completeMask = (1 << EnabledPlanes) - 1;
            if ((_pendingFetchedPlaneMask[wordIndex] & completeMask) != completeMask)
            {
                return;
            }

            for (int committedPlane = 0; committedPlane < EnabledPlanes; committedPlane++)
            {
                ushort data = _pendingFetchedWords[committedPlane, wordIndex];
                _fetchedWords[committedPlane, wordIndex] = data;
                _fetchedRows[committedPlane, wordIndex] = row;
                _dataLatches[committedPlane] = data;
                _dataLatchValid[committedPlane] = true;
            }

            _lastFetchedWordIndex = wordIndex;
            if (wordIndex + 1 >= _wordsPerFetchedLine)
            {
                EndDmaLine();
            }
        }

        /// <summary>
        /// Returns the Screen Y.
        /// </summary>
        public int GetScreenY(int beamY)
        {
            return beamY - _diwTop;
        }

        /// <summary>
        /// Renders Lowres Pixel into the current frame buffer.
        /// </summary>
        public uint RenderLowresPixel(int screenX, int beamX, int beamY)
        {
            return ReadColor(RenderColorIndex(screenX, beamY));
        }

        /// <summary>
        /// Renders Color Index into the current frame buffer.
        /// </summary>
        public int RenderColorIndex(int screenX, int beamY)
        {
            if (!_displayActive || !IsInsideDiw(beamY))
            {
                return 0;
            }

            int displayX = GetDisplayX(screenX);
            if (displayX < 0 || displayX >= _displayFetchWidth)
            {
                return 0;
            }

            return ReadColorIndex(displayX, beamY);
        }

        /// <summary>
        /// Returns a palette entry expanded to host ARGB format.
        /// </summary>
        public uint ReadColorArgb(int index)
        {
            return ReadColor(index);
        }

        /// <summary>
        /// Executes the Priority Control logic.
        /// </summary>
        public ushort PriorityControl
        {
            get { return Read(Bplcon2); }
        }

        /// <summary>
        /// Returns whether Dual Playfield is true.
        /// </summary>
        public bool IsDualPlayfield
        {
            get { return (Read(Bplcon0) & 0x0400) != 0; }
        }

        /// <summary>
        /// Renders Fallback Pixel into the current frame buffer.
        /// </summary>
        public uint RenderFallbackPixel(int screenX, int screenY, long frame)
        {
            return ReadColor(0);
        }

        /// <summary>
        /// Resolves the active bitplane color index for one visible display pixel.
        /// </summary>
        private int ReadColorIndex(int displayX, int beamY)
        {
            int row = GetScreenY(beamY);
            if (row < 0)
            {
                return 0;
            }

            ushort bplcon0 = Read(Bplcon0);
            if ((bplcon0 & 0x0400) != 0)
            {
                return ReadDualPlayfieldColorIndex(displayX, row);
            }

            return ReadSinglePlayfieldColorIndex(displayX, row);
        }

        /// <summary>
        /// Combines enabled bitplanes into a single-playfield palette index.
        /// </summary>
        private int ReadSinglePlayfieldColorIndex(int displayX, int row)
        {
            int colorIndex = 0;

            for (int plane = 0; plane < EnabledPlanes; plane++)
            {
                colorIndex |= ReadPlanePixelPair(plane, row, displayX) << plane;
            }

            return colorIndex & 0x3F;
        }

        /// <summary>
        /// Combines odd and even bitplanes using dual-playfield priority rules.
        /// </summary>
        private int ReadDualPlayfieldColorIndex(int displayX, int row)
        {
            int playfield1 = 0;
            int playfield2 = 0;
            int playfield1Bit = 0;
            int playfield2Bit = 0;

            for (int plane = 0; plane < EnabledPlanes; plane++)
            {
                int pixelBit = ReadPlanePixelPair(plane, row, displayX);
                if ((plane & 1) == 0)
                {
                    playfield1 |= pixelBit << playfield1Bit;
                    playfield1Bit++;
                }
                else
                {
                    playfield2 |= pixelBit << playfield2Bit;
                    playfield2Bit++;
                }
            }

            bool playfield2Priority = (Read(Bplcon2) & 0x0040) != 0;
            if (playfield2Priority)
            {
                return playfield2 != 0 ? 8 + playfield2 : playfield1;
            }

            if (playfield1 != 0)
            {
                return playfield1;
            }

            return playfield2 != 0 ? 8 + playfield2 : 0;
        }

        /// <summary>
        /// Returns the fetched bitplane word for a plane and display word position.
        /// </summary>
        private ushort ReadFetchedWord(int plane, int row, int wordIndex)
        {
            if (plane < 0 || plane >= MaxPlanes || wordIndex < 0 || wordIndex >= MaxFetchedWords)
            {
                return 0;
            }

            if (_fetchedRows[plane, wordIndex] == row)
            {
                return _fetchedWords[plane, wordIndex];
            }

            return !_dmaActive && _dataLatchValid[plane] ? _dataLatches[plane] : (ushort)0;
        }

        /// <summary>
        /// Extracts one pixel bit from a fetched bitplane word after scroll adjustment.
        /// </summary>
        private int ReadPlanePixelPair(int plane, int row, int displayX)
        {
            int firstX = GetBitplaneX(displayX) - GetPlaneScroll(plane);
            int planeDisplayWidth = _wordsPerFetchedLine * 16;
            if (firstX < 0 || firstX >= planeDisplayWidth)
            {
                return 0;
            }

            int wordIndex = firstX >> 4;
            int bit = 15 - (firstX & 0x0F);
            ushort word = ReadFetchedWord(plane, row, wordIndex);
            return (word >> bit) & 1;
        }

        /// <summary>
        /// Executes the Ensure DMA Line logic.
        /// </summary>
        private void EnsureDmaLine(int row, int wordIndex)
        {
            if (_activeDmaRow == row && wordIndex >= _lastFetchedWordIndex)
            {
                return;
            }

            bool pointerRegistersChanged = HavePointerRegistersChanged();
            bool continuesPreviousLine = row == _activeDmaRow + 1 && _completedDmaLine && !pointerRegistersChanged;
            _activeDmaRow = row;
            _lastFetchedWordIndex = -1;
            _completedDmaLine = false;

            if (!continuesPreviousLine)
            {
                for (int plane = 0; plane < MaxPlanes; plane++)
                {
                    _dmaPointers[plane] = pointerRegistersChanged
                        ? GetBitplanePointer(plane)
                        : GetBitplaneLineStart(plane, row);
                }

                CapturePointerRegisterSnapshot();
            }

            if (wordIndex > 0)
            {
                for (int plane = 0; plane < MaxPlanes; plane++)
                {
                    _dmaPointers[plane] = _dmaAddress.Add(_dmaPointers[plane], (uint)(wordIndex * 2));
                }
            }
        }

        /// <summary>
        /// Executes the End DMA Line logic.
        /// </summary>
        private void EndDmaLine()
        {
            for (int plane = 0; plane < MaxPlanes; plane++)
            {
                int modulo = (plane & 1) == 0 ? SignedWord(Read(Bpl1mod)) : SignedWord(Read(Bpl2mod));
                _dmaPointers[plane] = _dmaAddress.Add(_dmaPointers[plane], modulo);
            }

            _completedDmaLine = true;
        }

        /// <summary>
        /// Returns the Bitplane Line Start.
        /// </summary>
        private uint GetBitplaneLineStart(int plane, int row)
        {
            int bytesPerFetchedLine = _wordsPerFetchedLine * 2;
            int modulo = (plane & 1) == 0 ? SignedWord(Read(Bpl1mod)) : SignedWord(Read(Bpl2mod));
            long stride = bytesPerFetchedLine + modulo;
            long address = GetBitplanePointer(plane) + (row * stride);
            return _dmaAddress.Normalize((uint)address);
        }

        /// <summary>
        /// Returns the Bitplane Pointer.
        /// </summary>
        private uint GetBitplanePointer(int plane)
        {
            ushort high = Read(BplPointerBase + ((uint)plane * 4u));
            ushort low = Read(BplPointerBase + ((uint)plane * 4u) + 2u);
            return _dmaAddress.FromRegisters(high, low);
        }

        /// <summary>
        /// Executes the Have Pointer Registers Changed logic.
        /// </summary>
        private bool HavePointerRegistersChanged()
        {
            if (!_hasPointerRegisterSnapshot)
            {
                return true;
            }

            for (int plane = 0; plane < MaxPlanes; plane++)
            {
                if (GetBitplanePointer(plane) != _lastPointerRegisterValues[plane])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Executes the Capture Pointer Register Snapshot logic.
        /// </summary>
        private void CapturePointerRegisterSnapshot()
        {
            for (int plane = 0; plane < MaxPlanes; plane++)
            {
                _lastPointerRegisterValues[plane] = GetBitplanePointer(plane);
            }

            _hasPointerRegisterSnapshot = true;
        }

        /// <summary>
        /// Returns the Display X.
        /// </summary>
        private int GetDisplayX(int screenX)
        {
            int left = (_ddfStart - 0x38) * HiresPixelsPerChipCycle;
            int splitPrefetchDelay = _splitFetchActive && _dmaActive
                ? EnabledPlanes * HiresPixelsPerChipCycle
                : 0;
            return screenX - Math.Max(0, left) - splitPrefetchDelay;
        }

        /// <summary>
        /// Returns the Plane Scroll.
        /// </summary>
        private int GetPlaneScroll(int plane)
        {
            return (plane & 1) == 0
                ? _scrollEven
                : _scrollOdd;
        }

        /// <summary>
        /// Returns the Bitplane X.
        /// </summary>
        private int GetBitplaneX(int displayX)
        {
            return _hires ? displayX : displayX >> 1;
        }

        /// <summary>
        /// Returns the Fetch Step.
        /// </summary>
        private int GetFetchStep()
        {
            return _hires ? 4 : 8;
        }

        /// <summary>
        /// Returns whether DMA Active is true.
        /// </summary>
        private bool IsDmaActive()
        {
            return _dmaActive;
        }

        /// <summary>
        /// Returns whether Inside Diw is true.
        /// </summary>
        private bool IsInsideDiw(int beamY)
        {
            return beamY >= _diwTop && beamY < _diwBottom;
        }

        /// <summary>
        /// Returns whether Valid Data Latch is present.
        /// </summary>
        private bool HasValidDataLatch()
        {
            for (int plane = 0; plane < EnabledPlanes; plane++)
            {
                if (_dataLatchValid[plane])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Executes the Diw Raw Top logic.
        /// </summary>
        private int DiwRawTop
        {
            get
            {
                ushort value = Read(Diwstrt);
                return value == 0 ? PalVisibleTop : (value >> 8) & 0xFF;
            }
        }

        /// <summary>
        /// Executes the Diw Raw Bottom logic.
        /// </summary>
        private int DiwRawBottom
        {
            get
            {
                ushort start = Read(Diwstrt);
                ushort stop = Read(Diwstop);
                int top = start == 0 ? PalVisibleTop : (start >> 8) & 0xFF;
                int bottom = stop == 0 ? top + PalVisibleHeight : (stop >> 8) & 0xFF;
                if (top < PalVisibleTop && bottom <= PalVisibleTop + 32)
                {
                    return PalVisibleTop + PalVisibleHeight;
                }

                if (bottom <= top)
                {
                    bottom += 256;
                }

                return bottom;
            }
        }

        /// <summary>
        /// Executes the Ddf Start logic.
        /// </summary>
        private int DdfStart
        {
            get
            {
                ushort value = Read(Ddfstrt);
                return value == 0 ? 0x38 : value & 0x00FE;
            }
        }

        /// <summary>
        /// Executes the Ddf Stop logic.
        /// </summary>
        private int DdfStop
        {
            get
            {
                ushort value = Read(Ddfstop);
                return value == 0 ? 0xD0 : value & 0x00FE;
            }
        }

        /// <summary>
        /// Executes the Words period Fetched Line logic.
        /// </summary>
        private int WordsPerFetchedLine
        {
            get
            {
                return _wordsPerFetchedLine;
            }
        }

        /// <summary>
        /// Executes the Display Fetch Width logic.
        /// </summary>
        private int DisplayFetchWidth
        {
            get { return _displayFetchWidth; }
        }

        /// <summary>
        /// Converts an Amiga 12-bit color register into a 32-bit ARGB pixel.
        /// </summary>
        private uint ReadColor(int index)
        {
            if ((index & 0x20) != 0)
            {
                uint baseColor = ReadColorRegister(index & 0x1F);
                uint red = ((baseColor >> 16) & 0xFF) >> 1;
                uint green = ((baseColor >> 8) & 0xFF) >> 1;
                uint blue = (baseColor & 0xFF) >> 1;
                return 0xFF000000u | (red << 16) | (green << 8) | blue;
            }

            return ReadColorRegister(index & 0x1F);
        }

        /// <summary>
        /// Reads and expands one bitplane palette register to ARGB format.
        /// </summary>
        private uint ReadColorRegister(int index)
        {
            ushort color = Read(ColorBase + ((uint)(index & 0x1F) * 2u));
            byte red = ExpandNibble((color >> 8) & 0x0F);
            byte green = ExpandNibble((color >> 4) & 0x0F);
            byte blue = ExpandNibble(color & 0x0F);
            return 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
        }

        /// <summary>
        /// Reads a bitplane-related custom register word by offset.
        /// </summary>
        private ushort Read(uint registerOffset)
        {
            return _registers != null ? _registers.ReadWord(registerOffset) : (ushort)0;
        }

        /// <summary>
        /// Executes the Expand Nibble logic.
        /// </summary>
        private static byte ExpandNibble(int value)
        {
            return (byte)((value & 0x0F) * 0x11);
        }

        /// <summary>
        /// Executes the Signed Word logic.
        /// </summary>
        private static int SignedWord(ushort value)
        {
            return (short)value;
        }
    }
}
