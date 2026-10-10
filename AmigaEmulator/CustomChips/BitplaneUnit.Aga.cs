// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
namespace AmigaEmulator.Core.CustomChips
{
    public sealed partial class BitplaneUnit
    {
        private int AgaFetchWords => _fetchWords;
        private uint ReadAgaColor(int index)
        {
            int xor = Read(0x10C) >> 8;
            return _ehb && (index & 0x20) != 0 ? HalfBright(_registers.AgaColors.ReadArgb((index & 31) ^ xor))
                : _registers.AgaColors.ReadArgb(index ^ xor);
        }
        private int GetSecondPlayfieldOffset()
        {
            if (!_hasAga) return 8;
            int code = (Read(0x106) >> 10) & 7;
            return code == 0 ? 0 : 1 << code;
        }

        private uint ResolveHam8(int index, uint previous)
        {
            int control = index & 3;
            if (control == 0) return ReadColor(index >> 2);
            uint component = (uint)(index & 0xFC);
            // HAM8 replaces six MSBs and keeps the two LSBs of the held component.
            if (control == 1) return (previous & 0xFFFFFF03u) | component;
            if (control == 2) return (previous & 0xFF03FFFFu) | (component << 16);
            return (previous & 0xFFFF03FFu) | (component << 8);
        }

        private void FetchAgaPlaneGroup(int beamX, int row)
        {
            int words = AgaFetchWords;
            int offset = beamX - _ddfStart;
            int wordIndex = offset / GetFetchStep() * words;
            int plane = GetFetchPlaneForSlot(offset % GetFetchStep());
            if (plane < 0 || plane >= EnabledPlanes || wordIndex < 0 || wordIndex + words > MaxFetchedWords) return;
            EnsureDmaLine(row, wordIndex);
            if (_fetchedRows[plane, wordIndex] == row) return;
            for (int word = 0; word < words; word++)
            {
                int at = wordIndex + word;
                if (_pendingFetchedRows[at] != row)
                {
                    _pendingFetchedRows[at] = row;
                    _pendingFetchedPlaneMask[at] = 0;
                }
            }
            if ((_alignNewPlaneFetchMask & (1 << plane)) != 0)
            {
                _dmaPointers[plane] = _dmaAddress.Add(GetBitplanePointer(plane), (uint)(wordIndex * 2));
                _alignNewPlaneFetchMask &= ~(1 << plane);
            }
            // Wide DMA starts at the selected aligned RAM boundary; low pointer bits still advance.
            uint address = _dmaAddress.Normalize(_dmaPointers[plane]) & ~(uint)(words * 2 - 1);
            for (int word = 0; word < words; word++)
            {
                int at = wordIndex + word;
                _pendingFetchedWords[plane, at] = _readChipWord(_dmaAddress.Add(address, word * 2));
                _pendingFetchedPlaneMask[at] |= 1 << plane;
            }
            _dmaPointers[plane] = _dmaAddress.Add(_dmaPointers[plane], words * 2);
            int complete = (1 << EnabledPlanes) - 1;
            if ((_pendingFetchedPlaneMask[wordIndex] & complete) != complete) return;
            for (int word = 0; word < words; word++)
            for (int committed = 0; committed < EnabledPlanes; committed++)
            {
                int at = wordIndex + word;
                ushort value = _pendingFetchedWords[committed, at];
                _fetchedWords[committed, at] = value; _fetchedRows[committed, at] = row;
                _dataLatches[committed] = value; _dataLatchValid[committed] = true;
            }
            for (int word = 0; word < words; word++) InvalidatePixelWord(row, wordIndex + word);
            _lastFetchedWordIndex = wordIndex + words - 1;
            if (wordIndex + words >= _wordsPerFetchedLine) EndDmaLine();
        }
    }
}
