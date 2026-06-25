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
        /// Constant for Pal Visible Top.
        /// </summary>
        public const int PalVisibleTop = 44;
        /// <summary>
        /// Constant for Pal Visible Height.
        /// </summary>
        public const int PalVisibleHeight = 256;
        /// <summary>
        /// Constant for the standard PAL presentation width in hires pixels.
        /// </summary>
        public const int PalVisibleWidth = 640;
        /// <summary>
        /// Constant for Lowres Pixels Per Chip Cycle.
        /// </summary>
        public const int LowresPixelsPerChipCycle = 2;
        /// <summary>
        /// Constant for Hires Pixels Per Chip Cycle.
        /// </summary>
        public const int HiresPixelsPerChipCycle = 4;

        /// <summary>
        /// Defines the DMACON custom register offset or bit mask.
        /// </summary>
        private const uint Dmacon = 0x096;
        /// <summary>
        /// Constant for Diwstrt.
        /// </summary>
        private const uint Diwstrt = 0x08E;
        /// <summary>
        /// Constant for Diwstop.
        /// </summary>
        private const uint Diwstop = 0x090;
        /// <summary>
        /// Constant for Ddfstrt.
        /// </summary>
        private const uint Ddfstrt = 0x092;
        /// <summary>
        /// Constant for Ddfstop.
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
        /// Constant for Bpl Data Base.
        /// </summary>
        private const uint BplDataBase = 0x110;
        /// <summary>
        /// Defines the BITPLANE POINTER BASE custom register offset or bit mask.
        /// </summary>
        private const uint BplPointerBase = 0x0E0;
        /// <summary>
        /// Constant for Color Base.
        /// </summary>
        private const uint ColorBase = 0x180;
        /// <summary>
        /// Constant for Max Planes.
        /// </summary>
        private const int MaxPlanes = 6;
        /// <summary>
        /// Constant for Max Fetched Words.
        /// </summary>
        private const int MaxFetchedWords = 128;
        /// <summary>
        /// OCS low-resolution bitplane DMA slot order inside one 8-cycle fetch unit.
        /// </summary>
        private static readonly int[] s_lowresFetchSequence = { 8, 4, 6, 2, 7, 3, 5, 1 };
        /// <summary>
        /// OCS high-resolution bitplane DMA slot order inside one 4-cycle fetch unit.
        /// </summary>
        private static readonly int[] s_hiresFetchSequence = { 4, 2, 3, 1 };
        /// <summary>
        /// OCS two-plane bitplane DMA slot order inside one fetch unit.
        /// </summary>
        private static readonly int[] s_twoPlaneFetchSequence = { 2, 1 };

        /// <summary>
        /// Stores registers.
        /// </summary>
        private readonly CustomRegisterMap _registers;
        /// <summary>
        /// Stores fetched Words.
        /// </summary>
        private readonly ushort[,] _fetchedWords;
        /// <summary>
        /// Stores fetched Rows.
        /// </summary>
        private readonly int[,] _fetchedRows;
        /// <summary>
        /// Stores pending Fetched Words.
        /// </summary>
        private readonly ushort[,] _pendingFetchedWords;
        /// <summary>
        /// Stores pending Fetched Rows.
        /// </summary>
        private readonly int[] _pendingFetchedRows;
        /// <summary>
        /// Stores pending Fetched Plane Mask.
        /// </summary>
        private readonly int[] _pendingFetchedPlaneMask;
        /// <summary>
        /// Stores data Latches.
        /// </summary>
        private readonly ushort[] _dataLatches;
        /// <summary>
        /// Stores data Latch Valid.
        /// </summary>
        private readonly bool[] _dataLatchValid;
        /// <summary>
        /// Caches COLORxx registers as expanded ARGB values for pixel rendering.
        /// </summary>
        private readonly uint[] _paletteArgb;
        /// <summary>
        /// Stores DMA Pointers.
        /// </summary>
        private readonly uint[] _dmaPointers;
        /// <summary>
        /// Stores last Pointer Register Values.
        /// </summary>
        private readonly uint[] _lastPointerRegisterValues;
        /// <summary>
        /// Stores DMA Address.
        /// </summary>
        private readonly ChipDmaAddress _dmaAddress;
        /// <summary>
        /// Stores read Chip Word.
        /// </summary>
        private Func<uint, ushort> _readChipWord;
        /// <summary>
        /// Stores active DMA Row.
        /// </summary>
        private int _activeDmaRow;
        /// <summary>
        /// Stores last Fetched Word Index.
        /// </summary>
        private int _lastFetchedWordIndex;
        /// <summary>
        /// Stores completed DMA Line.
        /// </summary>
        private bool _completedDmaLine;
        /// <summary>
        /// Stores has Pointer Register Snapshot.
        /// </summary>
        private bool _hasPointerRegisterSnapshot;
        /// <summary>
        /// Preserves older savestate layouts that stored the former split-fetch display-delay flag.
        /// </summary>
#pragma warning disable 0169, 0414
        private bool _splitFetchActive;
#pragma warning restore 0169, 0414
        /// <summary>
        /// Stores DMA Active.
        /// </summary>
        private bool _dmaActive;
        /// <summary>
        /// Stores display Active.
        /// </summary>
        private bool _displayActive;
        /// <summary>
        /// Stores diw Top.
        /// </summary>
        private int _diwTop;
        /// <summary>
        /// Stores diw Bottom.
        /// </summary>
        private int _diwBottom;
        /// <summary>
        /// Stores diw Left.
        /// </summary>
        private int _diwLeft;
        /// <summary>
        /// Stores diw Right.
        /// </summary>
        private int _diwRight;
        /// <summary>
        /// Stores ddf Start.
        /// </summary>
        private int _ddfStart;
        /// <summary>
        /// Stores ddf Stop.
        /// </summary>
        private int _ddfStop;
        /// <summary>
        /// Stores words Per Fetched Line.
        /// </summary>
        private int _wordsPerFetchedLine;
        /// <summary>
        /// Stores display Fetch Width.
        /// </summary>
        private int _displayFetchWidth;
        /// <summary>
        /// Stores scroll Even.
        /// </summary>
        private int _scrollEven;
        /// <summary>
        /// Stores scroll Odd.
        /// </summary>
        private int _scrollOdd;
        /// <summary>
        /// Stores hires.
        /// </summary>
        private bool _hires;
        /// <summary>
        /// Tracks whether BPLCON0 selects HAM output.
        /// </summary>
        private bool _ham;
        /// <summary>
        /// Tracks whether the effective BPLCON0 selects dual-playfield output.
        /// </summary>
        private bool _dualPlayfield;
        /// <summary>
        /// Tracks whether six bitplanes select Extra Half-Brite output.
        /// </summary>
        private bool _ehb;
        /// <summary>
        /// Tracks whether the palette cache must be rebuilt from custom registers.
        /// </summary>
        private bool _paletteDirty = true;
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
            _paletteArgb = new uint[64];
            _dmaPointers = new uint[MaxPlanes];
            _lastPointerRegisterValues = new uint[MaxPlanes];
            Reset();
        }

        /// <summary>
        /// Gets or sets Enabled Planes.
        /// </summary>
        public int EnabledPlanes { get; private set; }

        /// <summary>
        /// Gets DMA Active.
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
            _paletteDirty = true;
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
            _paletteDirty = true;
        }

        /// <summary>
        /// Notifies this component about Custom Register Word Write.
        /// </summary>
        public void NotifyCustomRegisterWordWrite(uint registerOffset, ushort value)
        {
            registerOffset &= 0x01FE;
            if (registerOffset >= BplDataBase && registerOffset < BplDataBase + (MaxPlanes * 2u))
            {
                int plane = (int)((registerOffset - BplDataBase) >> 1);
                _dataLatches[plane] = value;
                _dataLatchValid[plane] = true;
                return;
            }

            if (registerOffset >= ColorBase && registerOffset < ColorBase + 64u)
            {
                UpdatePaletteEntry((int)((registerOffset - ColorBase) >> 1), value);
                return;
            }

            if (registerOffset == Bplcon0 ||
                registerOffset == Bplcon1 ||
                registerOffset == Dmacon ||
                registerOffset == Diwstrt ||
                registerOffset == Diwstop ||
                registerOffset == Ddfstrt ||
                registerOffset == Ddfstop)
            {
                RefreshFromRegisters();
            }
        }

        /// <summary>
        /// Runs Refresh From Registers.
        /// </summary>
        public void RefreshFromRegisters()
        {
            ushort bplcon0 = GetEffectiveBplcon0();
            ushort bplcon1 = Read(Bplcon1);
            ushort dmacon = Read(Dmacon);
            ushort diwStart = Read(Diwstrt);
            ushort diwStop = Read(Diwstop);
            ushort ddfStart = Read(Ddfstrt);
            ushort ddfStop = Read(Ddfstop);
            int enabledPlanes = Math.Min(6, (bplcon0 >> 12) & 0x07);
            bool hasValidDataLatch = HasValidDataLatch(enabledPlanes);

            EnabledPlanes = enabledPlanes;
            _hires = (bplcon0 & 0x8000) != 0;
            _ham = (bplcon0 & 0x0800) != 0;
            _dualPlayfield = (bplcon0 & 0x0400) != 0;
            bool previousEhb = _ehb;
            _ehb = !_ham && EnabledPlanes == 6;
            if (previousEhb != _ehb)
            {
                _paletteDirty = true;
            }

            _dmaActive = (dmacon & 0x0200) != 0 && (dmacon & 0x0100) != 0;
            _displayActive = EnabledPlanes > 0 && (_dmaActive || hasValidDataLatch);

            int rawTop = diwStart == 0 ? PalVisibleTop : (diwStart >> 8) & 0xFF;
            _diwTop = rawTop;
            _diwBottom = diwStop == 0 ? rawTop + PalVisibleHeight : (diwStop >> 8) & 0xFF;
            if (rawTop < PalVisibleTop && _diwBottom <= PalVisibleTop + 32)
            {
                _diwTop = PalVisibleTop;
                _diwBottom = PalVisibleTop + PalVisibleHeight;
            }

            if (_diwBottom <= rawTop)
            {
                _diwBottom += 256;
            }

            _ddfStart = NormalizeDdfStart(ddfStart);
            _ddfStop = NormalizeDdfStop(ddfStop);
            int fetchUnit = GetFetchUnit();
            int fetchStart = GetFetchStart();
            int span = _ddfStop - _ddfStart;
            int fetchGroups = span < 0 ? 0 : ((span + fetchUnit - 1) / fetchUnit) + 1;
            _wordsPerFetchedLine = fetchGroups * fetchUnit / fetchStart;
            int bitplaneBits = _wordsPerFetchedLine * 16;
            _displayFetchWidth = _hires ? bitplaneBits : bitplaneBits * 2;
            _scrollEven = bplcon1 & 0x000F;
            _scrollOdd = (bplcon1 >> 4) & 0x000F;
            RefreshHorizontalDisplayWindow(diwStart, diwStop);
        }

        /// <summary>
        /// Refreshes the viewport-relative horizontal display-window clipping.
        /// </summary>
        private void RefreshHorizontalDisplayWindow(ushort diwStart, ushort diwStop)
        {
            int rawLeft = diwStart & 0x00FF;
            int rawRight = diwStop & 0x00FF;
            _diwLeft = 0;
            if (diwStart == 0 && diwStop == 0)
            {
                _diwRight = int.MaxValue;
                return;
            }

            if (rawRight <= rawLeft)
            {
                _diwRight = int.MaxValue;
                return;
            }

            int registerWidth = (rawRight - rawLeft) * HiresPixelsPerChipCycle;
            _diwRight = Math.Max(registerWidth, _displayFetchWidth);
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
        /// Runs Fetch Dma Words.
        /// </summary>
        public void FetchDmaWords(int beamX, int beamY)
        {
            if (_readChipWord == null || !IsFetchSlot(beamX, beamY))
            {
                return;
            }

            int row = GetFetchRowKey(beamY);
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

            int plane = GetFetchPlaneForSlot(fetchOffset % fetchStep);
            return plane >= 0 && plane < EnabledPlanes;
        }

        /// <summary>
        /// Runs Fetch Dma Plane Word.
        /// </summary>
        public void FetchDmaPlaneWord(int beamX, int beamY)
        {
            if (_readChipWord == null || !IsPlaneFetchSlot(beamX, beamY))
            {
                return;
            }

            int row = GetFetchRowKey(beamY);
            if (row < 0)
            {
                return;
            }

            int fetchStep = GetFetchStep();
            int fetchOffset = beamX - _ddfStart;
            int wordIndex = fetchOffset / fetchStep;
            int plane = GetFetchPlaneForSlot(fetchOffset % fetchStep);
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
        /// Uses the physical beam line as the bitplane DMA row key.
        /// </summary>
        private static int GetFetchRowKey(int beamY)
        {
            return beamY;
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
            return RenderPixelInfo(screenX, beamY, out _);
        }

        /// <summary>
        /// Renders the bitplane color index and raw plane mask using one fetched-bit pass.
        /// </summary>
        public int RenderPixelInfo(int screenX, int beamY, out int planeMask)
        {
            planeMask = 0;
            if (!_displayActive || !IsInsideDiw(screenX, beamY))
            {
                return 0;
            }

            int displayX = GetDisplayX(screenX);
            if (displayX < 0 || displayX >= _displayFetchWidth)
            {
                return 0;
            }

            int row = GetFetchRowKey(beamY);
            if (row < 0)
            {
                return 0;
            }

            planeMask = ReadPlaneMask(displayX, row);
            return _dualPlayfield ? ResolveDualPlayfieldColorIndex(planeMask) : planeMask & 0x3F;
        }

        /// <summary>
        /// Returns the six raw BPLx pixel bits for Denise collision and HAM/EHB logic.
        /// </summary>
        public int RenderPlaneMask(int screenX, int beamY)
        {
            if (!_displayActive || !IsInsideDiw(screenX, beamY))
            {
                return 0;
            }

            int displayX = GetDisplayX(screenX);
            if (displayX < 0 || displayX >= _displayFetchWidth)
            {
                return 0;
            }

            int row = GetFetchRowKey(beamY);
            if (row < 0)
            {
                return 0;
            }

            return ReadPlaneMask(displayX, row);
        }

        /// <summary>
        /// Returns a palette entry expanded to host ARGB format.
        /// </summary>
        public uint ReadColorArgb(int index)
        {
            return ReadColor(index);
        }

        /// <summary>
        /// Resolves a Denise color code, including HAM and EHB modes.
        /// </summary>
        public uint ResolveColorArgb(int index, uint previousArgb)
        {
            index &= 0x3F;
            if (_ham)
            {
                int control = (index >> 4) & 0x03;
                int data = index & 0x0F;
                if (control == 0)
                {
                    return ReadColor(data);
                }

                byte component = ExpandNibble(data);
                if (control == 1)
                {
                    return (previousArgb & 0xFFFFFF00u) | component;
                }

                if (control == 2)
                {
                    return (previousArgb & 0xFF00FFFFu) | ((uint)component << 16);
                }

                return (previousArgb & 0xFFFF00FFu) | ((uint)component << 8);
            }

            return ReadColor(index);
        }

        /// <summary>
        /// Gets Priority Control.
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
            get { return _dualPlayfield; }
        }

        /// <summary>
        /// Returns whether HAM mode is selected.
        /// </summary>
        public bool IsHamMode
        {
            get { return _ham; }
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
            int row = GetFetchRowKey(beamY);
            if (row < 0)
            {
                return 0;
            }

            if (_dualPlayfield)
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
            return ReadPlaneMask(displayX, row) & 0x3F;
        }

        /// <summary>
        /// Reads all enabled bitplane bits for one display pixel.
        /// </summary>
        private int ReadPlaneMask(int displayX, int row)
        {
            int mask = 0;
            int bitplaneX = _hires ? displayX : displayX >> 1;
            int planeDisplayWidth = _wordsPerFetchedLine * 16;
            for (int plane = 0; plane < EnabledPlanes; plane++)
            {
                int firstX = bitplaneX - (((plane & 1) == 0) ? _scrollOdd : _scrollEven);
                if (firstX < 0 || firstX >= planeDisplayWidth)
                {
                    continue;
                }

                int wordIndex = firstX >> 4;
                int bit = 15 - (firstX & 0x0F);
                ushort word = ReadFetchedWord(plane, row, wordIndex);
                mask |= ((word >> bit) & 1) << plane;
            }

            return mask & 0x3F;
        }

        /// <summary>
        /// Combines odd and even bitplanes using dual-playfield priority rules.
        /// </summary>
        private int ReadDualPlayfieldColorIndex(int displayX, int row)
        {
            return ResolveDualPlayfieldColorIndex(ReadPlaneMask(displayX, row));
        }

        /// <summary>
        /// Combines an already-read plane mask using dual-playfield priority rules.
        /// </summary>
        private int ResolveDualPlayfieldColorIndex(int planeMask)
        {
            int playfield1 = 0;
            int playfield2 = 0;
            int playfield1Bit = 0;
            int playfield2Bit = 0;

            for (int plane = 0; plane < EnabledPlanes; plane++)
            {
                int pixelBit = (planeMask >> plane) & 1;
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
        /// Runs Ensure Dma Line.
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
            InvalidateFetchedRow(row);

            if (!continuesPreviousLine)
            {
                for (int plane = 0; plane < MaxPlanes; plane++)
                {
                    _dmaPointers[plane] = GetBitplanePointer(plane);
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
        /// Clears cached fetch words for a display row before fresh DMA data for that row arrives.
        /// </summary>
        private void InvalidateFetchedRow(int row)
        {
            for (int plane = 0; plane < MaxPlanes; plane++)
            {
                for (int word = 0; word < MaxFetchedWords; word++)
                {
                    if (_fetchedRows[plane, word] == row)
                    {
                        _fetchedRows[plane, word] = int.MinValue;
                    }
                }
            }

            for (int word = 0; word < MaxFetchedWords; word++)
            {
                if (_pendingFetchedRows[word] == row)
                {
                    _pendingFetchedRows[word] = int.MinValue;
                    _pendingFetchedPlaneMask[word] = 0;
                }
            }
        }

        /// <summary>
        /// Runs End Dma Line.
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
        /// Returns the Bitplane Pointer.
        /// </summary>
        private uint GetBitplanePointer(int plane)
        {
            ushort high = Read(BplPointerBase + ((uint)plane * 4u));
            ushort low = Read(BplPointerBase + ((uint)plane * 4u) + 2u);
            return _dmaAddress.FromRegisters(high, low);
        }

        /// <summary>
        /// Runs Have Pointer Registers Changed.
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
        /// Runs Capture Pointer Register Snapshot.
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
            return screenX - GetDisplayLeft();
        }

        /// <summary>
        /// Returns the viewport-relative X coordinate where the fetched display data begins.
        /// </summary>
        private int GetDisplayLeft()
        {
            int fetchLeadIn = GetRawDisplayLeft();
            if (_dmaActive && !_hires && EnabledPlanes > 4)
            {
                fetchLeadIn += HiresPixelsPerChipCycle;
            }

            return fetchLeadIn;
        }

        /// <summary>
        /// Returns the raw viewport-relative X coordinate implied by DDFSTRT.
        /// </summary>
        private int GetRawDisplayLeft()
        {
            return (_ddfStart - 0x38) * HiresPixelsPerChipCycle;
        }

        /// <summary>
        /// Returns the Fetch Step.
        /// </summary>
        private int GetFetchStep()
        {
            return GetFetchStart();
        }

        /// <summary>
        /// Returns the indivisible OCS DDF block size in color clocks.
        /// </summary>
        private static int GetFetchUnit()
        {
            return 8;
        }

        /// <summary>
        /// Returns the distance before the same bitplane can be fetched again.
        /// </summary>
        private int GetFetchStart()
        {
            return _hires ? 4 : 8;
        }

        /// <summary>
        /// Applies the A500 OCS DDFSTRT mask and default value.
        /// </summary>
        private static int NormalizeDdfStart(ushort value)
        {
            return value == 0 ? 0x38 : value & 0x00FC;
        }

        /// <summary>
        /// Applies the A500 OCS DDFSTOP mask and default value.
        /// </summary>
        private static int NormalizeDdfStop(ushort value)
        {
            return value == 0 ? 0xD0 : value & 0x00FC;
        }

        /// <summary>
        /// Maps an OCS DMA cycle inside the fetch unit to a bitplane index.
        /// </summary>
        private int GetFetchPlaneForSlot(int slot)
        {
            int[] sequence = GetFetchSequence();
            if (slot < 0 || slot >= sequence.Length)
            {
                return -1;
            }

            return sequence[slot] - 1;
        }

        /// <summary>
        /// Returns the active OCS bitplane DMA sequence for the current resolution and plane count.
        /// </summary>
        private int[] GetFetchSequence()
        {
            if (EnabledPlanes <= 2)
            {
                return s_twoPlaneFetchSequence;
            }

            if (_hires || EnabledPlanes <= 4)
            {
                return s_hiresFetchSequence;
            }

            return s_lowresFetchSequence;
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
        /// Returns whether a viewport pixel is inside the display window.
        /// </summary>
        private bool IsInsideDiw(int screenX, int beamY)
        {
            return IsInsideDiw(beamY) && screenX >= _diwLeft && screenX < _diwRight;
        }

        /// <summary>
        /// Returns whether Valid Data Latch is present.
        /// </summary>
        private bool HasValidDataLatch()
        {
            return HasValidDataLatch(EnabledPlanes);
        }

        /// <summary>
        /// Returns whether a direct data latch is valid for the requested plane count.
        /// </summary>
        private bool HasValidDataLatch(int enabledPlanes)
        {
            for (int plane = 0; plane < enabledPlanes; plane++)
            {
                if (_dataLatchValid[plane])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns BPLCON0 after the OCS Agnus mask has removed unsupported bits.
        /// </summary>
        private ushort GetEffectiveBplcon0()
        {
            return (ushort)(Read(Bplcon0) & 0xFF0F);
        }

        /// <summary>
        /// Gets Diw Raw Top.
        /// </summary>
        private int DiwRawTop
        {
            get
            {
                ushort value = Read(Diwstrt);
                ushort stop = Read(Diwstop);
                int top = value == 0 ? PalVisibleTop : (value >> 8) & 0xFF;
                int bottom = stop == 0 ? top + PalVisibleHeight : (stop >> 8) & 0xFF;
                return top < PalVisibleTop && bottom <= PalVisibleTop + 32 ? PalVisibleTop : top;
            }
        }

        /// <summary>
        /// Gets Diw Raw Bottom.
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
        /// Gets Ddf Start.
        /// </summary>
        private int DdfStart
        {
            get
            {
                ushort value = Read(Ddfstrt);
                return NormalizeDdfStart(value);
            }
        }

        /// <summary>
        /// Gets Ddf Stop.
        /// </summary>
        private int DdfStop
        {
            get
            {
                ushort value = Read(Ddfstop);
                return NormalizeDdfStop(value);
            }
        }

        /// <summary>
        /// Gets Words period Fetched Line.
        /// </summary>
        private int WordsPerFetchedLine
        {
            get
            {
                return _wordsPerFetchedLine;
            }
        }

        /// <summary>
        /// Gets Display Fetch Width.
        /// </summary>
        private int DisplayFetchWidth
        {
            get { return _displayFetchWidth; }
        }

        /// <summary>
        /// Gets the host presentation source width needed for wide horizontal overscan fetches.
        /// </summary>
        public int PresentationSourceWidth
        {
            get
            {
                RefreshFromRegisters();
                int displayLeft = GetRawDisplayLeft();
                int sourceLeft = Math.Min(0, displayLeft);
                int sourceRight = Math.Max(PalVisibleWidth, displayLeft + _displayFetchWidth);
                return Math.Max(0, sourceRight - sourceLeft);
            }
        }

        /// <summary>
        /// Gets the viewport-relative source X coordinate used for presentation cropping.
        /// </summary>
        public int PresentationSourceLeft
        {
            get
            {
                RefreshFromRegisters();
                return Math.Min(0, GetRawDisplayLeft());
            }
        }

        /// <summary>
        /// Gets the beam-relative source Y coordinate used for presentation.
        /// </summary>
        public int PresentationSourceTop
        {
            get
            {
                RefreshFromRegisters();
                return Math.Min(PalVisibleTop, _diwTop);
            }
        }

        /// <summary>
        /// Gets the source height needed to keep DIW-programmed vertical overscan visible.
        /// </summary>
        public int PresentationSourceHeight
        {
            get
            {
                RefreshFromRegisters();
                int sourceTop = Math.Min(PalVisibleTop, _diwTop);
                int sourceBottom = Math.Max(PalVisibleTop + PalVisibleHeight, _diwBottom);
                return Math.Max(1, sourceBottom - sourceTop);
            }
        }

        /// <summary>
        /// Converts an Amiga 12-bit color register into a 32-bit ARGB pixel.
        /// </summary>
        private uint ReadColor(int index)
        {
            EnsurePaletteCache();
            return _paletteArgb[index & 0x3F];
        }

        /// <summary>
        /// Rebuilds the ARGB palette cache when COLORxx or EHB state changed.
        /// </summary>
        private void EnsurePaletteCache()
        {
            if (!_paletteDirty)
            {
                return;
            }

            for (int index = 0; index < 32; index++)
            {
                UpdatePaletteEntry(index, Read(ColorBase + ((uint)index * 2u)));
            }

            _paletteDirty = false;
        }

        /// <summary>
        /// Updates one cached COLORxx entry and its EHB mirror.
        /// </summary>
        private void UpdatePaletteEntry(int index, ushort color)
        {
            index &= 0x1F;
            uint argb = ExpandColor(color);
            _paletteArgb[index] = argb;
            _paletteArgb[index | 0x20] = _ehb ? HalfBright(argb) : argb;
        }

        /// <summary>
        /// Converts an Amiga 12-bit color word into 32-bit ARGB.
        /// </summary>
        private static uint ExpandColor(ushort color)
        {
            byte red = ExpandNibble((color >> 8) & 0x0F);
            byte green = ExpandNibble((color >> 4) & 0x0F);
            byte blue = ExpandNibble(color & 0x0F);
            return 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
        }

        /// <summary>
        /// Returns the Extra Half-Brite version of an expanded ARGB color.
        /// </summary>
        private static uint HalfBright(uint argb)
        {
            uint red = ((argb >> 16) & 0xFF) >> 1;
            uint green = ((argb >> 8) & 0xFF) >> 1;
            uint blue = (argb & 0xFF) >> 1;
            return 0xFF000000u | (red << 16) | (green << 8) | blue;
        }

        /// <summary>
        /// Reads a bitplane-related custom register word by offset.
        /// </summary>
        private ushort Read(uint registerOffset)
        {
            return _registers != null ? _registers.ReadWord(registerOffset) : (ushort)0;
        }

        /// <summary>
        /// Runs Expand Nibble.
        /// </summary>
        private static byte ExpandNibble(int value)
        {
            return (byte)((value & 0x0F) * 0x11);
        }

        /// <summary>
        /// Runs Signed Word.
        /// </summary>
        private static int SignedWord(ushort value)
        {
            return (short)value;
        }
    }
}
