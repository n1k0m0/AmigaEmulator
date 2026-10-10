// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;

namespace AmigaEmulator.Core.CustomChips
{
    public sealed partial class SpriteUnit
    {
        private int AgaSpriteWords => (ReadRegister(0x1FC) & 12) == 12 ? 4 : (ReadRegister(0x1FC) & 12) != 0 ? 2 : 1;
        public bool UsesSingleHiresPixels => _hasAga && SpriteResolution > 1;
        public bool BorderSpritesEnabled => _hasAga && (ReadRegister(0x100) & 1) != 0 && (ReadRegister(0x106) & 2) != 0;
        private int SpriteResolution
        {
            get
            {
                int value = (ReadRegister(0x106) >> 6) & 3;
                return value != 0 ? value : (ReadRegister(0x100) & 0x40) != 0 ? 2 : 1;
            }
        }

        private bool HasSpriteData(SpriteState state) => _hasAga
            ? (state.AgaDataA | state.AgaDataB) != 0 : (state.DataA | state.DataB) != 0;
        private int GetSpriteOutputWidth(SpriteState state) => !_hasAga ? 32 :
            SpriteResolution == 1 ? state.AgaWidth * 2 : SpriteResolution == 2 ? state.AgaWidth : state.AgaWidth / 2;

        private ulong ReadAgaSpriteGroup(uint pointer, int words)
        {
            uint address = _dmaAddress.Normalize(pointer) & ~(uint)(words * 2 - 1);
            ulong bits = 0;
            for (int word = 0; word < words; word++) bits = (bits << 16) | ReadChip(address + (uint)word * 2);
            return bits;
        }

        private void TickAgaDma(SpriteState state, int phase, int beamY)
        {
            int words = AgaSpriteWords;
            if (phase == 0)
            {
                state.PendingFetch = SelectFetchKind(state, beamY);
                if (state.PendingFetch == SpriteFetchKind.None) return;
                state.AgaWidth = words * 16;
                state.AgaPendingA = ReadAgaSpriteGroup(state.Pointer, words);
                state.PendingFirstWord = (ushort)(state.AgaPendingA >> (state.AgaWidth - 16));
                return;
            }
            if (state.PendingFetch == SpriteFetchKind.None) return;
            words = state.AgaWidth / 16;
            ulong second = ReadAgaSpriteGroup(state.Pointer + (uint)words * 2, words);
            state.Pointer = _dmaAddress.Add(state.Pointer, words * 4);
            if (state.PendingFetch == SpriteFetchKind.Control)
            {
                ApplyControlWords(state, state.PendingFirstWord, (ushort)(second >> (state.AgaWidth - 16)), true);
                state.DmaActive = !state.Terminated && beamY == state.VStart;
            }
            else
            {
                state.AgaDataA = state.AgaPendingA;
                state.AgaDataB = second;
                state.DataA = state.PendingFirstWord;
                state.DataB = (ushort)(second >> (state.AgaWidth - 16));
                state.LineActive = true;
                state.Active = beamY >= state.VStart && beamY < state.VStop;
            }
            state.PendingFetch = SpriteFetchKind.None;
        }

        private void SetAgaDirectData(SpriteState state, ushort value, bool first)
        {
            // A CPU/Copper 16-bit DATA write drives every word of the wider latch.
            int words = AgaSpriteWords;
            ulong data = 0;
            for (int i = 0; i < words; i++) data = (data << 16) | value;
            state.AgaWidth = words * 16;
            if (first) state.AgaDataA = data; else state.AgaDataB = data;
        }

        private bool TryReadAgaPixel(SpriteState state, int screenX, out int color)
        {
            color = 0;
            // Position uses quarter-lores units. The existing framebuffer samples
            // two of those units per hires pixel; superhires is sampled accordingly.
            int start = state.HStart * 4 + ((state.Ctl >> 3) & 1) + ((state.Ctl >> 4) & 1) * 2;
            int units = screenX * 2 - start;
            int shift = SpriteResolution == 1 ? 2 : SpriteResolution == 2 ? 1 : 0;
            if (state.LineActive && units >= 0 && (units >> shift) < state.AgaWidth &&
                (!state.OutputValid || state.OutputHStart != start))
            {
                state.OutputValid = true;
                state.OutputHStart = start;
                state.OutputDataA = state.AgaDataA;
                state.OutputDataB = state.AgaDataB;
                state.OutputWidth = state.AgaWidth;
                state.OutputShift = shift;
            }
            units = screenX * 2 - state.OutputHStart;
            if (!state.OutputValid || units < 0) return false;
            int index = units >> state.OutputShift;
            if (index >= state.OutputWidth) return false;
            int bit = state.OutputWidth - 1 - index;
            color = (int)((state.OutputDataA >> bit) & 1) | (int)(((state.OutputDataB >> bit) & 1) << 1);
            return true;
        }

        private void SaveAgaState(BinaryWriter writer)
        {
            writer.Write(1);
            foreach (var state in _states)
            {
                writer.Write(state.AgaDataA); writer.Write(state.AgaDataB);
                writer.Write(state.AgaPendingA); writer.Write(state.AgaWidth);
            }
        }
        private void LoadAgaState(BinaryReader reader)
        {
            if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported AGA sprite state.");
            foreach (var state in _states)
            {
                state.AgaDataA = reader.ReadUInt64(); state.AgaDataB = reader.ReadUInt64();
                state.AgaPendingA = reader.ReadUInt64(); state.AgaWidth = reader.ReadInt32();
                if (state.AgaWidth != 16 && state.AgaWidth != 32 && state.AgaWidth != 64)
                    throw new InvalidDataException("Invalid AGA sprite width.");
            }
        }
    }
}
