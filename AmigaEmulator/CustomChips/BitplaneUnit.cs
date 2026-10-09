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
    public sealed partial class BitplaneUnit
    {
        [NonSerialized]
        private readonly bool _hasEcs;
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
        [NonSerialized] private readonly int MaxPlanes;
        [NonSerialized] private readonly bool _hasAga;
        [NonSerialized] private int _fetchWords = 1, _fetchStep = 8, _fetchUnit = 8, _fetchStopExclusive;
        [NonSerialized] private int[] _fetchSequence = s_hiresFetchSequence;
        [NonSerialized] private uint _decodedDisplayGeneration;
        [NonSerialized] private static readonly ulong[] s_expandedPlaneBytes = BuildExpandedPlaneBytes();
        [NonSerialized] private int _pixelCacheRow = int.MinValue, _pixelCacheWord = -1, _pixelCachePlanes;
        [NonSerialized] private ulong _pixelCacheFirst, _pixelCacheSecond;
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

        private int _alignNewPlaneFetchMask;

        // Transient decode cache; raw register comparisons also cover callers
        // that write the register map directly rather than notifying this unit.
        private bool _decodedRegistersValid;
        private ulong _decodedControlKey;
        private ulong _decodedWindowKey;
        private int _decodedDiwHighKey;
        private bool _fetchRowsMonotonic;
        /// <summary>
        /// Stores completed DMA Line.
        /// </summary>
        private bool _completedDmaLine;
        /// <summary>
        /// Stores has Pointer Register Snapshot.
        /// </summary>
        private bool _hasPointerRegisterSnapshot;
        /// <summary>
        /// Forces the next fetched line to reload BPLxPT after one deferred partial rewrite.
        /// </summary>
        private bool _reloadPointersAfterDeferredPartialRewrite;
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
        /// Tracks whether the current frame observed at least one active bitplane presentation window.
        /// </summary>
        [NonSerialized]
        private bool _hasFramePresentationWindow;
        /// <summary>
        /// Stores the leftmost viewport-relative presentation edge observed during the current frame.
        /// </summary>
        [NonSerialized]
        private int _framePresentationLeft;
        /// <summary>
        /// Stores the rightmost viewport-relative presentation edge observed during the current frame.
        /// </summary>
        [NonSerialized]
        private int _framePresentationRight;
        /// <summary>
        /// Stores the topmost beam-relative presentation edge observed during the current frame.
        /// </summary>
        [NonSerialized]
        private int _framePresentationTop;
        /// <summary>
        /// Stores the bottommost beam-relative presentation edge observed during the current frame.
        /// </summary>
        [NonSerialized]
        private int _framePresentationBottom;
        /// <summary>
        /// Tracks which PAL beam lines observed a bitplane presentation window this frame.
        /// </summary>
        [NonSerialized]
        private bool[] _linePresentationObserved = Array.Empty<bool>();
        [NonSerialized]
        private bool[] _lineSpriteFrameObserved = Array.Empty<bool>();
        [NonSerialized]
        private bool[] _previousLineSpriteFrameObserved = Array.Empty<bool>();

        /// <summary>
        /// Stores the leftmost viewport-relative presentation edge observed per PAL beam line.
        /// </summary>
        [NonSerialized]
        private int[] _linePresentationLeft = Array.Empty<int>();
        /// <summary>
        /// Stores the rightmost viewport-relative presentation edge observed per PAL beam line.
        /// </summary>
        [NonSerialized]
        private int[] _linePresentationRight = Array.Empty<int>();
        /// <summary>
        /// Stores the first viewport-relative bitplane-content edge observed per PAL beam line.
        /// </summary>
        [NonSerialized]
        private int[] _linePresentationContentLeft = Array.Empty<int>();
        /// <summary>
        /// Stores the previous completed frame's per-line presentation-window flags for presentation.
        /// </summary>
        [NonSerialized]
        private bool[] _previousLinePresentationObserved = Array.Empty<bool>();
        /// <summary>
        /// Stores the previous completed frame's per-line presentation left edges.
        /// </summary>
        [NonSerialized]
        private int[] _previousLinePresentationLeft = Array.Empty<int>();
        /// <summary>
        /// Stores the previous completed frame's per-line presentation right edges.
        /// </summary>
        [NonSerialized]
        private int[] _previousLinePresentationRight = Array.Empty<int>();
        /// <summary>
        /// Stores the previous completed frame's first per-line bitplane-content edges.
        /// </summary>
        [NonSerialized]
        private int[] _previousLinePresentationContentLeft = Array.Empty<int>();
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
        /// Stores the viewport-relative display start derived from DDF/BPLCON1.
        /// </summary>
        private int _displayLeft;
        /// <summary>
        /// Stores the display width including BPLCON1 fine-scroll extension.
        /// </summary>
        private int _scrolledDisplayWidth;
        /// <summary>
        /// Stores the per-plane fetched pixel width.
        /// </summary>
        private int _planeDisplayWidth;
        /// <summary>
        /// Stores scroll Even.
        /// </summary>
        private int _scrollEven;
        /// <summary>
        /// Stores scroll Odd.
        /// </summary>
        private int _scrollOdd;
        [NonSerialized] private int _scrollOddSubpixel, _scrollEvenSubpixel;
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
            _hasEcs = registers != null && registers.HasEcs;
            _hasAga = registers != null && registers.HasAga;
            MaxPlanes = _hasAga ? 8 : 6;
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
            _pixelCacheRow = int.MinValue;
            _decodedRegistersValid = false;
            _fetchRowsMonotonic = true;
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
            _alignNewPlaneFetchMask = 0;
            _completedDmaLine = false;
            _hasPointerRegisterSnapshot = false;
            _reloadPointersAfterDeferredPartialRewrite = false;
            _paletteDirty = true;
            ResetFramePresentationWindow();
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
            SnapshotLinePresentationWindow();
            ResetFramePresentationWindow();
            ResetDmaLineTracking();
        }

        /// <summary>
        /// Clears transient line-fetch bookkeeping while keeping register and latch state intact.
        /// </summary>
        private void ResetDmaLineTracking()
        {
            _pixelCacheRow = int.MinValue;
            _fetchRowsMonotonic = true;
            _alignNewPlaneFetchMask = 0;
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
            _reloadPointersAfterDeferredPartialRewrite = false;
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
            StateSerializer.WriteObjectFields(writer, this, "_registers", "_readChipWord", "_decodedRegistersValid", "_decodedControlKey", "_decodedWindowKey", "_decodedDiwHighKey", "_fetchRowsMonotonic");
        }

        /// <summary>
        /// Restores the bitplane DMA and display latches from a savestate.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            StateSerializer.ReadObjectFields(reader, this, "_registers", "_readChipWord", "_decodedRegistersValid", "_decodedControlKey", "_decodedWindowKey", "_decodedDiwHighKey", "_fetchRowsMonotonic");
            _decodedRegistersValid = false;
            _pixelCacheRow = int.MinValue;
            _fetchRowsMonotonic = false;
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
                if (!_dataLatchValid[plane]) _decodedRegistersValid = false;
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
                (registerOffset == Bplcon2 && _registers != null && _registers.HasEcs) ||
                (_hasAga && registerOffset == 0x1FC) ||
                registerOffset == Dmacon ||
                registerOffset == Diwstrt ||
                registerOffset == Diwstop ||
                registerOffset == 0x1E4 ||
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
            // Most DMA slots see unchanged control registers. Writes, manual
            // latch activation, reset and restore invalidate this fast path.
            if (_registers != null && _decodedRegistersValid &&
                _decodedDisplayGeneration == _registers.DisplayControlGeneration) return;
            if (_registers != null) _decodedDisplayGeneration = _registers.DisplayControlGeneration;
            ushort bplcon0 = GetEffectiveBplcon0();
            ushort bplcon1 = Read(Bplcon1);
            ushort dmacon = Read(Dmacon);
            ushort diwStart = Read(Diwstrt);
            ushort diwStop = Read(Diwstop);
            ushort ddfStart = Read(Ddfstrt);
            ushort ddfStop = Read(Ddfstop);
            int enabledPlanes = _hasAga && (bplcon0 & 0x10) != 0 ? 8 : Math.Min(MaxPlanes, (bplcon0 >> 12) & 7);
            bool hasValidDataLatch = HasValidDataLatch(enabledPlanes);
            int fmode = _hasAga ? Read(0x1FC) & 3 : 0;

            ulong controlKey = bplcon0 | ((ulong)bplcon1 << 16) | ((ulong)(dmacon & 0x0300) << 32) |
                (hasValidDataLatch ? 1UL << 48 : 0);
            bool ecs = _hasEcs;
            bool killEhb = ecs && (Read(Bplcon2) & 0x0200) != 0;
            if (killEhb) controlKey |= 1UL << 49;
            if (_hasAga) controlKey |= (ulong)fmode << 50;
            ulong windowKey = diwStart | ((ulong)diwStop << 16) | ((ulong)ddfStart << 32) | ((ulong)ddfStop << 48);
            int diwHighKey = _registers != null && _registers.DiwHighWritten ? Read(0x1E4) | 0x10000 : 0;
            if (_decodedRegistersValid && controlKey == _decodedControlKey && windowKey == _decodedWindowKey && diwHighKey == _decodedDiwHighKey)
                return;
            _decodedControlKey = controlKey;
            _decodedWindowKey = windowKey;
            _decodedDiwHighKey = diwHighKey;
            _decodedRegistersValid = true;
            _fetchWords = fmode == 3 ? 4 : fmode != 0 ? 2 : 1;
            bool hires = (bplcon0 & 0x8000) != 0;
            _fetchStep = (hires ? 4 : 8) * _fetchWords;
            _fetchUnit = 8 * _fetchWords;
            _fetchSequence = (_hasAga && enabledPlanes > 4 && _fetchStep >= 8) || (!hires && enabledPlanes > 4)
                ? s_lowresFetchSequence : s_hiresFetchSequence;

            if (enabledPlanes > EnabledPlanes && _activeDmaRow != int.MinValue)
            {
                _alignNewPlaneFetchMask |= ((1 << enabledPlanes) - 1) & ~((1 << EnabledPlanes) - 1);
                // A raster split can finish rewriting newly enabled pointers after
                // the first fetch. Reload the complete set on the following line
                // rather than treating those writes as an unrelated partial update.
                _reloadPointersAfterDeferredPartialRewrite = true;
            }
            EnabledPlanes = enabledPlanes;
            _hires = (bplcon0 & 0x8000) != 0;
            _ham = (bplcon0 & 0x0800) != 0;
            _dualPlayfield = (bplcon0 & 0x0400) != 0;
            bool previousEhb = _ehb;
            _ehb = !_ham && EnabledPlanes == 6 &&
                (!ecs || (!_hires && !_dualPlayfield && !killEhb));
            if (previousEhb != _ehb)
            {
                _paletteDirty = true;
            }

            _dmaActive = (dmacon & 0x0200) != 0 && (dmacon & 0x0100) != 0;
            _displayActive = EnabledPlanes > 0 && (_dmaActive || hasValidDataLatch);

            _diwTop = CalculateDiwTop(diwStart);
            _diwBottom = CalculateDiwBottom(diwStart, diwStop);

            _ddfStart = NormalizeDdfStart(ddfStart);
            _ddfStop = NormalizeDdfStop(ddfStop);
            int fetchUnit = GetFetchUnit();
            int fetchStart = GetFetchStart();
            int span = _ddfStop - _ddfStart;
            int fetchGroups = span < 0 ? 0 : ((span + fetchUnit - 1) / fetchUnit) + 1;
            _wordsPerFetchedLine = fetchGroups * fetchUnit / fetchStart;
            _fetchStopExclusive = _ddfStart + _wordsPerFetchedLine / _fetchWords * _fetchStep;
            int bitplaneBits = _wordsPerFetchedLine * 16;
            _planeDisplayWidth = bitplaneBits;
            _displayFetchWidth = _hires ? bitplaneBits : bitplaneBits * 2;
            // BPLCON1: PF1 (odd planes) is the low nibble; PF2 (even) is the high nibble.
            _scrollOdd = bplcon1 & 0x000F;
            _scrollEven = (bplcon1 >> 4) & 0x000F;
            _scrollOddSubpixel = _scrollEvenSubpixel = 0;
            if (_hasAga)
            {
                // Lisa's eight-bit delay is measured in 35 ns pixels. The
                // original nibbles are bits 2..5, not the two fractional bits.
                int odd = ((bplcon1 & 15) << 2) | ((bplcon1 >> 8) & 3) | ((bplcon1 >> 4) & 0xC0);
                int even = ((bplcon1 & 0xF0) >> 2) | ((bplcon1 >> 12) & 3) | ((bplcon1 >> 8) & 0xC0);
                int shift = _hires ? 1 : 2;
                _scrollOdd = odd >> shift; _scrollEven = even >> shift;
                _scrollOddSubpixel = odd & ((1 << shift) - 1);
                _scrollEvenSubpixel = even & ((1 << shift) - 1);
            }
            _displayLeft = CalculateDisplayLeft();
            _scrolledDisplayWidth = CalculateScrolledDisplayWidth();
            RefreshHorizontalDisplayWindow(diwStart, diwStop);
        }

        /// <summary>
        /// Refreshes the viewport-relative horizontal display-window clipping.
        /// </summary>
        private void RefreshHorizontalDisplayWindow(ushort diwStart, ushort diwStop)
        {
            if (_registers != null && !_registers.DiwHighWritten &&
                (diwStart != 0 || diwStop != 0))
            {
                // Both OCS and ECS legacy comparators imply HSTART bit 8=0
                // and HSTOP bit 8=1. DDF fetches outside DIW stay invisible.
                _diwLeft = ((diwStart & 0xFF) - 0x81) * 2;
                _diwRight = (((diwStop & 0xFF) | 0x100) - 0x81) * 2;
                return;
            }
            if (_registers != null && _registers.DiwHighWritten)
            {
                ushort high = Read(0x1E4);
                int left = (diwStart & 0xFF) | ((high & 0x20) << 3);
                int right = (diwStop & 0xFF) | ((high & 0x2000) >> 5);
                // DIW horizontal values are lowres pixels; host X=0 is the standard $81 edge.
                _diwLeft = (left - 0x81) * 2;
                _diwRight = (right - 0x81) * 2;
                return;
            }
            int rawLeft = diwStart & 0x00FF;
            int rawRight = diwStop & 0x00FF;
            _diwLeft = Math.Min(0, GetDisplayLeft());
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
            _diwRight = Math.Max(registerWidth, GetDisplayLeft() + GetScrolledDisplayWidth());
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

        public bool IsHiresMode => _hires;
        // Fractional Lisa delays can move lores transitions onto odd host
        // pixels. Such spans cannot reuse a single sample for an even pair.
        public bool RequiresHiresPixelSampling => _hires ||
            ((((_scrollOddSubpixel + 1) / 2) | ((_scrollEvenSubpixel + 1) / 2)) & 1) != 0;
        public bool IsAga => _hasAga;
        public bool ClipSpritesToDisplayWindow => _hasEcs;
        public bool BorderBlankEnabled => _hasEcs &&
            (Read(Bplcon0) & 1) != 0 && (Read(0x106) & 0x0020) != 0;
        public uint BorderColorArgb => BorderBlankEnabled ? 0xFF000000u : ReadColorArgb(0);

        public bool TryGetDisplayWindowOnLine(int beamY, out int left, out int right)
        {
            left = _diwLeft;
            right = _diwRight;
            return IsInsideDiw(beamY) && right > left;
        }
        public bool IsInsideDisplayWindow(int screenX, int beamY) => IsInsideDiw(screenX, beamY);

        public bool IsBitplaneDisplayActiveOnLine(int beamY) => _displayActive && IsInsideDiw(beamY);

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
            int fetchStopExclusive = _fetchStopExclusive;
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

            InvalidatePixelWord(row, wordIndex);
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
            int fetchStopExclusive = _fetchStopExclusive;
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

            if (_hasAga) { FetchAgaPlaneGroup(beamX, row); return; }

            int fetchStep = GetFetchStep();
            int fetchOffset = beamX - _ddfStart;
            int wordIndex = fetchOffset / fetchStep;
            if (!_hires && EnabledPlanes <= 4)
            {
                int slot = fetchOffset % fetchStep;
                int[] sequence = GetFetchSequence();
                int finalSlot = -1;
                for (int index = sequence.Length - 1; index >= 0; index--)
                {
                    if (sequence[index] - 1 < EnabledPlanes)
                    {
                        finalSlot = index;
                        break;
                    }
                }

                if (slot == finalSlot)
                {
                    FetchDmaWords(beamX - slot, beamY);
                }

                return;
            }

            int plane = GetFetchPlaneForSlot(fetchOffset % fetchStep);
            if (wordIndex < 0 || wordIndex >= MaxFetchedWords || plane < 0 || plane >= EnabledPlanes)
            {
                return;
            }

            EnsureDmaLine(row, wordIndex);
            // A compact lowres group may have completed before BPLCON0
            // enables more planes within that same group. Do not read the
            // already latched planes a second time and advance their pointers.
            if (_fetchedRows[plane, wordIndex] == row)
            {
                return;
            }
            if (_pendingFetchedRows[wordIndex] != row)
            {
                _pendingFetchedRows[wordIndex] = row;
                _pendingFetchedPlaneMask[wordIndex] = 0;
                for (int existingPlane = 0; existingPlane < EnabledPlanes; existingPlane++)
                {
                    if (_fetchedRows[existingPlane, wordIndex] == row)
                    {
                        _pendingFetchedWords[existingPlane, wordIndex] = _fetchedWords[existingPlane, wordIndex];
                        _pendingFetchedPlaneMask[wordIndex] |= 1 << existingPlane;
                    }
                }
            }

            if ((_alignNewPlaneFetchMask & (1 << plane)) != 0)
            {
                _dmaPointers[plane] = _dmaAddress.Add(GetBitplanePointer(plane), (uint)(wordIndex * 2));
                _alignNewPlaneFetchMask &= ~(1 << plane);
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

            InvalidatePixelWord(row, wordIndex);
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
            if (displayX < 0 || displayX >= GetScrolledDisplayWidth())
            {
                return 0;
            }

            int row = GetFetchRowKey(beamY);
            if (row < 0)
            {
                return 0;
            }

            planeMask = ReadPlaneMask(displayX, row);
            return _dualPlayfield ? ResolveDualPlayfieldColorIndex(planeMask) : planeMask;
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
            if (displayX < 0 || displayX >= GetScrolledDisplayWidth())
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
            // BPLAM applies to playfield indices, never COLOR00 borders or the HAM seed.
            return _hasAga ? _registers.AgaColors.ReadArgb(index) : ReadColor(index);
        }

        /// <summary>
        /// Resolves a Denise color code, including HAM and EHB modes.
        /// </summary>
        public uint ResolveColorArgb(int index, uint previousArgb)
        {
            if (!_hasAga) return ResolveLegacyColorArgb(index, previousArgb);
            index &= 0xFF;
            if (_ham && EnabledPlanes == 8) return ResolveHam8(index, previousArgb);
            return ResolveHam6Color(index, previousArgb, true);
        }

        public uint ResolveLegacyColorArgb(int index, uint previousArgb)
        {
            index &= 0x3F;
            if (!_ham) { EnsurePaletteCache(); return _paletteArgb[index]; }
            return ResolveHam6Color(index, previousArgb, false);
        }

        private uint ResolveHam6Color(int index, uint previousArgb, bool aga)
        {
            if (_ham)
            {
                int control = (index >> 4) & 0x03;
                int data = index & 0x0F;
                if (control == 0)
                {
                    if (aga) return ReadAgaColor(data);
                    EnsurePaletteCache(); return _paletteArgb[data];
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

            return ReadAgaColor(index);
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
            return ReadPlaneMask(displayX, row);
        }

        /// <summary>
        /// Reads all enabled bitplane bits for one display pixel.
        /// </summary>
        private int ReadPlaneMask(int displayX, int row)
        {
            int mask = 0;
            int bitplaneX = _hires ? displayX : displayX >> 1;
            if (_dmaActive && _scrollOdd == _scrollEven && _scrollOddSubpixel == _scrollEvenSubpixel)
            {
                int firstX = ScrolledBitX(displayX, bitplaneX, _scrollOdd, _scrollOddSubpixel);
                if ((uint)firstX >= (uint)_planeDisplayWidth) return 0;
                int word = firstX >> 4;
                if ((_pixelCacheRow == row && _pixelCacheWord == word && _pixelCachePlanes == EnabledPlanes) ||
                    TryCachePixelWord(row, word))
                {
                    ulong pixels = (firstX & 8) == 0 ? _pixelCacheFirst : _pixelCacheSecond;
                    return (int)(pixels >> ((firstX & 7) * 8)) & 255;
                }
            }
            for (int plane = 0; plane < EnabledPlanes; plane++)
            {
                int firstX = (plane & 1) == 0
                    ? ScrolledBitX(displayX, bitplaneX, _scrollOdd, _scrollOddSubpixel)
                    : ScrolledBitX(displayX, bitplaneX, _scrollEven, _scrollEvenSubpixel);
                if (firstX < 0 || firstX >= _planeDisplayWidth)
                {
                    continue;
                }

                int wordIndex = firstX >> 4;
                int bit = 15 - (firstX & 0x0F);
                ushort word = ReadFetchedWord(plane, row, wordIndex);
                mask |= ((word >> bit) & 1) << plane;
            }

            // EnabledPlanes is bounded by the chipset at register decoding.
            return mask;
        }

        private int ScrolledBitX(int displayX, int bitplaneX, int scroll, int subpixel)
            => subpixel == 0 ? bitplaneX - scroll :
                ((displayX * 2 - subpixel) >> (_hires ? 1 : 2)) - scroll;

        private int ScrollHostPixels(int scroll, int subpixel)
            => (_hires ? scroll : scroll * LowresPixelsPerChipCycle) + (subpixel + 1) / 2;

        private bool TryCachePixelWord(int row, int word)
        {
            if ((uint)word >= MaxFetchedWords) return false;
            ulong first = 0, second = 0;
            for (int plane = 0; plane < EnabledPlanes; plane++)
            {
                // Do not cache an incomplete DMA group: a later chip slot can
                // supply the missing planes before the next output pixel.
                if (_fetchedRows[plane, word] != row) return false;
                ushort bits = _fetchedWords[plane, word];
                first |= s_expandedPlaneBytes[bits >> 8] << plane;
                second |= s_expandedPlaneBytes[bits & 255] << plane;
            }
            _pixelCacheRow = row; _pixelCacheWord = word; _pixelCachePlanes = EnabledPlanes;
            _pixelCacheFirst = first; _pixelCacheSecond = second;
            return true;
        }

        private void InvalidatePixelWord(int row, int word)
        {
            if (_pixelCacheRow == row && _pixelCacheWord == word) _pixelCacheRow = int.MinValue;
        }

        private static ulong[] BuildExpandedPlaneBytes()
        {
            var expanded = new ulong[256];
            for (int bits = 0; bits < 256; bits++)
            for (int pixel = 0; pixel < 8; pixel++)
                expanded[bits] |= (ulong)((bits >> (7 - pixel)) & 1) << (pixel * 8);
            return expanded;
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
                return playfield2 != 0 ? GetSecondPlayfieldOffset() + playfield2 : playfield1;
            }

            if (playfield1 != 0)
            {
                return playfield1;
            }

            return playfield2 != 0 ? GetSecondPlayfieldOffset() + playfield2 : 0;
        }

        public bool IsSecondPlayfieldSelected(int planeMask)
        {
            if (!_dualPlayfield) return false;
            int oddMask = _hasAga ? 0x55 : 0x15;
            int evenMask = _hasAga ? 0xAA : 0x2A;
            bool first = (planeMask & oddMask) != 0;
            bool second = (planeMask & evenMask) != 0;
            return second && (!first || (Read(Bplcon2) & 0x40) != 0);
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

            bool forcePointerReload = _reloadPointersAfterDeferredPartialRewrite;
            bool pointerRegistersChanged = forcePointerReload || HavePointerRegistersChanged();
            bool sequentialLine = row == _activeDmaRow + 1 && _completedDmaLine;
            bool partialPointerRewrite = !forcePointerReload && pointerRegistersChanged && sequentialLine && !HaveAllEnabledPointerRegistersChanged();
            bool continuesPreviousLine = sequentialLine && !forcePointerReload && (!pointerRegistersChanged || partialPointerRewrite);
            bool revisitingFetchedRow = row <= _activeDmaRow;
            if (revisitingFetchedRow)
                _fetchRowsMonotonic = false;
            _activeDmaRow = row;
            _alignNewPlaneFetchMask = 0;
            _lastFetchedWordIndex = -1;
            _completedDmaLine = false;
            // Forward scanlines cannot contain words tagged with their new row:
            // BeginFrame invalidates all row tags before DMA starts. Only a
            // repeated or backward fetch needs the full cache scan.
            if (!_fetchRowsMonotonic)
                InvalidateFetchedRow(row);

            if (!continuesPreviousLine)
            {
                for (int plane = 0; plane < MaxPlanes; plane++)
                {
                    _dmaPointers[plane] = GetBitplanePointer(plane);
                }

                CapturePointerRegisterSnapshot();
                _reloadPointersAfterDeferredPartialRewrite = false;
            }
            else if (partialPointerRewrite)
            {
                _reloadPointersAfterDeferredPartialRewrite = true;
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
            if (_pixelCacheRow == row) _pixelCacheRow = int.MinValue;
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
            // A plane-count change can complete another group after the compact
            // path already finished this line. Modulo belongs to the line once.
            if (_completedDmaLine)
                return;
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
        /// Returns true only after every enabled bitplane pointer register has been rewritten.
        /// </summary>
        private bool HaveAllEnabledPointerRegistersChanged()
        {
            if (!_hasPointerRegisterSnapshot)
            {
                return true;
            }

            for (int plane = 0; plane < EnabledPlanes; plane++)
            {
                if (GetBitplanePointer(plane) == _lastPointerRegisterValues[plane])
                {
                    return false;
                }
            }

            return true;
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
            return _displayLeft;
        }

        /// <summary>
        /// Returns the first viewport-relative X coordinate that can contain fetched bitplane data.
        /// </summary>
        private int GetDisplayContentLeft()
        {
            return GetDisplayLeft() + GetFineScrollContentLeadIn();
        }

        /// <summary>
        /// Calculates the viewport-relative X coordinate where the fetched display data begins.
        /// </summary>
        private int CalculateDisplayLeft()
        {
            int fetchLeadIn = GetRawDisplayLeft();
            if (_dmaActive)
            {
                fetchLeadIn += GetDmaPipelineLeadIn();
                // The hires $3C origin already accounts for the fetch-to-display
                // phase. ECS/AGA retain it: an earlier $38 fetch preloads one
                // word before DIW, which must not reappear at the left edge.
            }

            return fetchLeadIn;
        }

        /// <summary>
        /// Returns the fine-scroll delay before any enabled bitplane can produce a visible bit.
        /// </summary>
        private int GetFineScrollContentLeadIn()
        {
            if (EnabledPlanes <= 0)
            {
                return 0;
            }

            int minScroll = int.MaxValue;
            for (int plane = 0; plane < EnabledPlanes; plane++)
            {
                int scroll = (plane & 1) == 0 ? ScrollHostPixels(_scrollOdd, _scrollOddSubpixel)
                    : ScrollHostPixels(_scrollEven, _scrollEvenSubpixel);
                minScroll = Math.Min(minScroll, scroll);
            }

            if (minScroll == int.MaxValue)
            {
                return 0;
            }

            return minScroll;
        }

        /// <summary>
        /// Returns the remaining fetch-pipeline delay after BPLCON1 fine scroll has consumed it.
        /// </summary>
        private int GetDmaPipelineLeadIn()
        {
            // ECS/AGA hires screens preload a word with an early DDF start.
            // Its entire 16 bits precede DIW; applying the OCS host-span delay
            // here exposes the previous row's final four pixels at the left.
            if (_registers != null && _registers.HasEcs && _hires)
                return 0;
            int earliestScroll = Math.Min(ScrollHostPixels(_scrollOdd, _scrollOddSubpixel),
                ScrollHostPixels(_scrollEven, _scrollEvenSubpixel));
            return Math.Max(0, HiresPixelsPerChipCycle - earliestScroll);
        }

        /// <summary>
        /// Returns the viewport-relative width covered by fetched shifter data plus BPLCON1 fine scroll.
        /// </summary>
        private int GetScrolledDisplayWidth()
        {
            return _scrolledDisplayWidth;
        }

        /// <summary>
        /// Calculates the viewport-relative width covered by fetched shifter data plus BPLCON1 fine scroll.
        /// </summary>
        private int CalculateScrolledDisplayWidth()
        {
            int scroll = Math.Max(ScrollHostPixels(_scrollOdd, _scrollOddSubpixel),
                ScrollHostPixels(_scrollEven, _scrollEvenSubpixel));
            return _displayFetchWidth + scroll;
        }

        /// <summary>
        /// Returns the raw viewport-relative X coordinate implied by DDFSTRT.
        /// </summary>
        private int GetRawDisplayLeft()
        {
            int standardDdfStart = _hires ? 0x3C : 0x38;
            return (_ddfStart - standardDdfStart) * HiresPixelsPerChipCycle;
        }

        /// <summary>
        /// Returns the Fetch Step.
        /// </summary>
        private int GetFetchStep()
        {
            return _fetchStep;
        }

        /// <summary>
        /// Returns the indivisible OCS DDF block size in color clocks.
        /// </summary>
        private int GetFetchUnit()
        {
            return _fetchUnit;
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
        private int NormalizeDdfStart(ushort value)
        {
            return value == 0 ? 0x38 : value & (_registers != null && _registers.HasEcs ? 0x00FE : 0x00FC);
        }

        /// <summary>
        /// Applies the A500 OCS DDFSTOP mask and default value.
        /// </summary>
        private int NormalizeDdfStop(ushort value)
        {
            return value == 0 ? 0xD0 : value & (_registers != null && _registers.HasEcs ? 0x00FE : 0x00FC);
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
            return _fetchSequence;
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
            return (ushort)(Read(Bplcon0) & (_hasAga ? 0xFFFF : 0xFF0F));
        }

        /// <summary>
        /// Gets Diw Raw Top.
        /// </summary>
        private int DiwRawTop
        {
            get
            {
                ushort value = Read(Diwstrt);
                return CalculateDiwTop(value);
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
                return CalculateDiwBottom(start, stop);
            }
        }

        /// <summary>
        /// Decodes the vertical DIW start line from the OCS display-window registers.
        /// </summary>
        private int CalculateDiwTop(ushort diwStart)
        {
            if (_registers != null && _registers.DiwHighWritten)
                return ((diwStart >> 8) & 0xFF) | ((Read(0x1E4) & 0x000F) << 8);
            return diwStart == 0 ? PalVisibleTop : (diwStart >> 8) & 0xFF;
        }

        /// <summary>
        /// Decodes the vertical DIW stop line, including the PAL wrap used by top overscan screens.
        /// </summary>
        private int CalculateDiwBottom(ushort diwStart, ushort diwStop)
        {
            if (_registers != null && _registers.DiwHighWritten)
                return ((diwStop >> 8) & 0xFF) | (Read(0x1E4) & 0x0F00);
            int top = CalculateDiwTop(diwStart);
            int bottom = diwStop == 0 ? top + PalVisibleHeight : (diwStop >> 8) & 0xFF;
            if (bottom <= top || (top < PalVisibleTop && bottom <= PalVisibleTop + 32))
            {
                bottom += 256;
            }

            return bottom;
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
                int displayLeft = GetDisplayLeft();
                int displayRight = displayLeft + GetScrolledDisplayWidth();
                int sourceLeft = displayLeft > 0 ? displayLeft : Math.Min(0, displayLeft);
                int sourceRight = Math.Max(PalVisibleWidth, displayRight);
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
                int displayLeft = GetDisplayLeft();
                return displayLeft > 0 ? displayLeft : Math.Min(0, displayLeft);
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
        /// Gets the left edge accumulated from every bitplane window observed in the current frame.
        /// </summary>
        public int FramePresentationSourceLeft
        {
            get
            {
                return _hasFramePresentationWindow ? _framePresentationLeft : PresentationSourceLeft;
            }
        }

        /// <summary>
        /// Gets the source width accumulated from every bitplane window observed in the current frame.
        /// </summary>
        public int FramePresentationSourceWidth
        {
            get
            {
                return _hasFramePresentationWindow
                    ? Math.Max(1, _framePresentationRight - _framePresentationLeft)
                    : PresentationSourceWidth;
            }
        }

        /// <summary>
        /// Gets the top edge accumulated from every bitplane window observed in the current frame.
        /// </summary>
        public int FramePresentationSourceTop
        {
            get
            {
                return _hasFramePresentationWindow ? _framePresentationTop : PresentationSourceTop;
            }
        }

        /// <summary>
        /// Gets the source height accumulated from every bitplane window observed in the current frame.
        /// </summary>
        public int FramePresentationSourceHeight
        {
            get
            {
                return _hasFramePresentationWindow
                    ? Math.Max(1, _framePresentationBottom - _framePresentationTop)
                    : PresentationSourceHeight;
            }
        }

        /// <summary>
        /// Clears the per-frame presentation aperture accumulator.
        /// </summary>
        private void ResetFramePresentationWindow()
        {
            _hasFramePresentationWindow = false;
            _framePresentationLeft = 0;
            _framePresentationRight = 0;
            _framePresentationTop = 0;
            _framePresentationBottom = 0;
            EnsureLinePresentationStorage();
            Array.Clear(_linePresentationObserved, 0, _linePresentationObserved.Length);
            Array.Clear(_lineSpriteFrameObserved, 0, _lineSpriteFrameObserved.Length);
            Array.Clear(_linePresentationLeft, 0, _linePresentationLeft.Length);
            Array.Clear(_linePresentationRight, 0, _linePresentationRight.Length);
            Array.Clear(_linePresentationContentLeft, 0, _linePresentationContentLeft.Length);
        }

        /// <summary>
        /// Merges the currently programmed DIW/DDF presentation window into the frame aperture.
        /// </summary>
        private void ObserveFramePresentationWindow()
        {
            if (!_displayActive)
            {
                return;
            }

            GetCurrentPresentationWindow(
                out int sourceLeft,
                out int sourceRight,
                out int sourceTop,
                out int sourceBottom);
            if (!_hasFramePresentationWindow)
            {
                _framePresentationLeft = sourceLeft;
                _framePresentationRight = sourceRight;
                _framePresentationTop = sourceTop;
                _framePresentationBottom = sourceBottom;
                _hasFramePresentationWindow = true;
                return;
            }

            _framePresentationLeft = Math.Min(_framePresentationLeft, sourceLeft);
            _framePresentationRight = Math.Max(_framePresentationRight, sourceRight);
            _framePresentationTop = Math.Min(_framePresentationTop, sourceTop);
            _framePresentationBottom = Math.Max(_framePresentationBottom, sourceBottom);
        }

        /// <summary>
        /// Records the currently active presentation aperture for one beam line.
        /// </summary>
        public void ObservePresentationWindowForBeamLine(int beamY)
        {
            if (!_displayActive ||
                beamY < 0 ||
                beamY >= _linePresentationObserved.Length ||
                _linePresentationObserved[beamY])
            {
                return;
            }

            GetCurrentPresentationWindow(
                out int sourceLeft,
                out int sourceRight,
                out int sourceTop,
                out int sourceBottom);
            MergeFramePresentationWindow(sourceLeft, sourceRight, sourceTop, sourceBottom);
            _linePresentationLeft[beamY] = sourceLeft;
            _linePresentationRight[beamY] = sourceRight;
            _linePresentationContentLeft[beamY] = Math.Min(sourceRight - 1, Math.Max(sourceLeft, GetDisplayContentLeft()));
            _linePresentationObserved[beamY] = true;
        }

        /// <summary>
        /// Includes latched sprite extents so host normalization preserves sprite borders around a playfield.
        /// </summary>
        public void IncludeSpritePresentationWindowForBeamLine(int beamY, int spriteLeft, int spriteWidth)
        {
            if (spriteWidth < PalVisibleWidth || beamY < 0 || beamY >= _linePresentationObserved.Length ||
                !_linePresentationObserved[beamY] ||
                spriteLeft > _linePresentationContentLeft[beamY] ||
                spriteLeft + spriteWidth < _linePresentationRight[beamY])
            {
                return;
            }
            _lineSpriteFrameObserved[beamY] = true;
            _linePresentationLeft[beamY] = Math.Min(_linePresentationLeft[beamY], spriteLeft);
            _linePresentationContentLeft[beamY] = Math.Min(_linePresentationContentLeft[beamY], spriteLeft);
            _linePresentationRight[beamY] = Math.Max(_linePresentationRight[beamY], spriteLeft + spriteWidth);
        }

        /// <summary>
        /// Returns one stable horizontal sprite aperture for the whole framed display.
        /// </summary>
        public bool TryGetSpriteFrameHorizontalWindow(out int left, out int width)
        {
            for (int y = 0; y < _linePresentationObserved.Length; y++)
            {
                bool current = _linePresentationObserved[y];
                bool framed = current ? _lineSpriteFrameObserved[y] : _previousLineSpriteFrameObserved[y];
                if (!framed) continue;
                left = current ? _linePresentationContentLeft[y] : _previousLinePresentationContentLeft[y];
                int right = current ? _linePresentationRight[y] : _previousLinePresentationRight[y];
                width = right - left;
                return true;
            }
            left = width = 0;
            return false;
        }

        /// <summary>
        /// Returns the first sprite-framed line and the end of the register-selected display below it.
        /// </summary>
        public bool TryGetSpriteFramedPresentationBounds(out int top, out int bottom)
        {
            top = int.MaxValue;
            bottom = 0;
            for (int y = 0; y < _linePresentationObserved.Length; y++)
            {
                bool current = _linePresentationObserved[y];
                if (!current && !_previousLinePresentationObserved[y]) continue;
                if (current ? _lineSpriteFrameObserved[y] : _previousLineSpriteFrameObserved[y])
                    top = Math.Min(top, y);
                bottom = y + 1;
            }
            return top < bottom;
        }

        /// <summary>
        /// Returns the presentation aperture observed on one beam line during the current frame.
        /// </summary>
        public bool TryGetLinePresentationSourceWindow(int beamY, out int sourceLeft, out int sourceWidth)
        {
            if (beamY >= 0 &&
                beamY < _linePresentationObserved.Length &&
                _linePresentationObserved[beamY])
            {
                sourceLeft = _linePresentationLeft[beamY];
                sourceWidth = Math.Max(1, _linePresentationRight[beamY] - _linePresentationLeft[beamY]);
                return true;
            }

            if (beamY >= 0 &&
                beamY < _previousLinePresentationObserved.Length &&
                _previousLinePresentationObserved[beamY])
            {
                sourceLeft = _previousLinePresentationLeft[beamY];
                sourceWidth = Math.Max(1, _previousLinePresentationRight[beamY] - _previousLinePresentationLeft[beamY]);
                return true;
            }

            sourceLeft = 0;
            sourceWidth = 0;
            return false;
        }

        /// <summary>
        /// Returns the renderable bitplane-content aperture observed on one beam line during the current frame.
        /// </summary>
        public bool TryGetLinePresentationContentSourceWindow(int beamY, out int sourceLeft, out int sourceWidth)
        {
            if (beamY >= 0 &&
                beamY < _linePresentationObserved.Length &&
                _linePresentationObserved[beamY])
            {
                sourceLeft = _linePresentationContentLeft[beamY];
                sourceWidth = Math.Max(1, _linePresentationRight[beamY] - sourceLeft);
                return true;
            }

            if (beamY >= 0 &&
                beamY < _previousLinePresentationObserved.Length &&
                _previousLinePresentationObserved[beamY])
            {
                sourceLeft = _previousLinePresentationContentLeft[beamY];
                sourceWidth = Math.Max(1, _previousLinePresentationRight[beamY] - sourceLeft);
                return true;
            }

            sourceLeft = 0;
            sourceWidth = 0;
            return false;
        }

        /// <summary>
        /// Returns a compact debug description for one observed presentation beam line.
        /// </summary>
        public string GetLinePresentationDebugInfo(int beamY)
        {
            if (TryGetLinePresentationSourceWindow(beamY, out int sourceLeft, out int sourceWidth))
            {
                return beamY.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ":" +
                    sourceLeft.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "+" +
                    sourceWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return beamY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":none";
        }

        /// <summary>
        /// Ensures per-line presentation arrays exist after construction or deserialization.
        /// </summary>
        private void EnsureLinePresentationStorage()
        {
            const int maxPalLines = 512;
            if (_linePresentationObserved.Length == maxPalLines)
            {
                return;
            }

            _linePresentationObserved = new bool[maxPalLines];
            _lineSpriteFrameObserved = new bool[maxPalLines];
            _previousLineSpriteFrameObserved = new bool[maxPalLines];
            _linePresentationLeft = new int[maxPalLines];
            _linePresentationRight = new int[maxPalLines];
            _linePresentationContentLeft = new int[maxPalLines];
            _previousLinePresentationObserved = new bool[maxPalLines];
            _previousLinePresentationLeft = new int[maxPalLines];
            _previousLinePresentationRight = new int[maxPalLines];
            _previousLinePresentationContentLeft = new int[maxPalLines];
        }

        /// <summary>
        /// Saves the just-completed frame's per-line aperture before starting the next frame.
        /// </summary>
        private void SnapshotLinePresentationWindow()
        {
            EnsureLinePresentationStorage();
            Array.Copy(_linePresentationObserved, _previousLinePresentationObserved, _linePresentationObserved.Length);
            Array.Copy(_lineSpriteFrameObserved, _previousLineSpriteFrameObserved, _lineSpriteFrameObserved.Length);
            Array.Copy(_linePresentationLeft, _previousLinePresentationLeft, _linePresentationLeft.Length);
            Array.Copy(_linePresentationRight, _previousLinePresentationRight, _linePresentationRight.Length);
            Array.Copy(_linePresentationContentLeft, _previousLinePresentationContentLeft, _linePresentationContentLeft.Length);
        }

        /// <summary>
        /// Merges one current aperture into the frame-wide aperture.
        /// </summary>
        private void MergeFramePresentationWindow(int sourceLeft, int sourceRight, int sourceTop, int sourceBottom)
        {
            if (!_hasFramePresentationWindow)
            {
                _framePresentationLeft = sourceLeft;
                _framePresentationRight = sourceRight;
                _framePresentationTop = sourceTop;
                _framePresentationBottom = sourceBottom;
                _hasFramePresentationWindow = true;
                return;
            }

            _framePresentationLeft = Math.Min(_framePresentationLeft, sourceLeft);
            _framePresentationRight = Math.Max(_framePresentationRight, sourceRight);
            _framePresentationTop = Math.Min(_framePresentationTop, sourceTop);
            _framePresentationBottom = Math.Max(_framePresentationBottom, sourceBottom);
        }

        /// <summary>
        /// Calculates the current register-selected source window without mutating frame state.
        /// </summary>
        private void GetCurrentPresentationWindow(out int sourceLeft, out int sourceRight, out int sourceTop, out int sourceBottom)
        {
            int displayLeft = GetDisplayLeft();
            int displayRight = displayLeft + GetScrolledDisplayWidth();
            sourceLeft = displayLeft > 0 ? displayLeft : Math.Min(0, displayLeft);
            sourceRight = displayLeft > 0
                ? Math.Max(PalVisibleWidth, displayRight)
                : Math.Max(PalVisibleWidth, displayRight);
            sourceTop = Math.Min(PalVisibleTop, _diwTop);
            sourceBottom = Math.Max(PalVisibleTop + PalVisibleHeight, _diwBottom);
            if (sourceRight <= sourceLeft)
            {
                sourceRight = sourceLeft + 1;
            }

            if (sourceBottom <= sourceTop)
            {
                sourceBottom = sourceTop + 1;
            }
        }

        /// <summary>
        /// Converts an Amiga 12-bit color register into a 32-bit ARGB pixel.
        /// </summary>
        private uint ReadColor(int index)
        {
            EnsurePaletteCache();
            if (_hasAga)
            {
                return ReadAgaColor(index);
            }
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
            return _registers != null ? _registers.ReadStoredWord(registerOffset) : (ushort)0;
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
