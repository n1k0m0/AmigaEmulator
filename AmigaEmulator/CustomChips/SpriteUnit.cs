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
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>
    /// Tracks sprite pointer DMA, sprite data latches, and sprite pixel output.
    /// </summary>
    public sealed class SpriteUnit
    {
        /// <summary>
        /// Defines the DMACON custom register offset or bit mask.
        /// </summary>
        private const uint Dmacon = 0x096;
        /// <summary>
        /// Defines the SPRITE POINTER BASE custom register offset or bit mask.
        /// </summary>
        private const uint SpritePointerBase = 0x120;
        /// <summary>
        /// Defines the SPRITE REGISTER BASE custom register offset or bit mask.
        /// </summary>
        private const uint SpriteRegisterBase = 0x140;
        /// <summary>
        /// Defines the SPRITE REGISTER STRIDE custom register offset or bit mask.
        /// </summary>
        private const uint SpriteRegisterStride = 0x08;
        /// <summary>
        /// Defines the Color Base constant.
        /// </summary>
        private const uint ColorBase = 0x180;

        /// <summary>
        /// Tracks the registers.
        /// </summary>
        private readonly AmigaEmulator.Core.Memory.CustomRegisterMap _registers;
        /// <summary>
        /// Tracks the read Chip Word.
        /// </summary>
        private System.Func<uint, ushort> _readChipWord;
        /// <summary>
        /// Tracks the DMA Address.
        /// </summary>
        private readonly ChipDmaAddress _dmaAddress;
        /// <summary>
        /// Tracks the sprite Pointers.
        /// </summary>
        private readonly uint[] _spritePointers = new uint[8];
        /// <summary>
        /// Tracks the states.
        /// </summary>
        private readonly SpriteState[] _states = new SpriteState[8];
        /// <summary>
        /// Tracks the last Beam Y.
        /// </summary>
        private int _lastBeamY = -1;
        /// <summary>
        /// Tracks the DMA Enabled.
        /// </summary>
        private bool _dmaEnabled;

        /// <summary>
        /// Initializes a new instance of the SpriteUnit class.
        /// </summary>
        public SpriteUnit()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the SpriteUnit class.
        /// </summary>
        public SpriteUnit(AmigaEmulator.Core.Memory.CustomRegisterMap registers, System.Func<uint, ushort> readChipWord)
            : this(registers, readChipWord, 512 * 1024)
        {
        }

        /// <summary>
        /// Initializes a new instance of the SpriteUnit class.
        /// </summary>
        public SpriteUnit(AmigaEmulator.Core.Memory.CustomRegisterMap registers, System.Func<uint, ushort> readChipWord, int chipRamBytes)
        {
            _registers = registers;
            _readChipWord = readChipWord;
            _dmaAddress = new ChipDmaAddress(chipRamBytes);
            for (int sprite = 0; sprite < _states.Length; sprite++)
            {
                _states[sprite] = new SpriteState();
            }
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            for (int index = 0; index < _spritePointers.Length; index++)
            {
                _spritePointers[index] = 0;
                _states[index].Reset();
            }

            HasActiveSprites = false;
            _dmaEnabled = false;
            _lastBeamY = -1;
        }

        /// <summary>
        /// Attaches the chip-memory reader used by sprite DMA.
        /// </summary>
        public void AttachChipWordReader(System.Func<uint, ushort> readChipWord)
        {
            _readChipWord = readChipWord;
        }

        /// <summary>
        /// Writes sprite DMA, pointer, and render state to a savestate.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            StateSerializer.WriteObjectFields(writer, this, "_registers", "_readChipWord");
        }

        /// <summary>
        /// Restores sprite DMA, pointer, and render state from a savestate.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            StateSerializer.ReadObjectFields(reader, this, "_registers", "_readChipWord");
            RefreshFromRegisters();
            SanitizeLoadedSpriteStates();
        }

        /// <summary>
        /// Gets or updates the Has Active Sprites.
        /// </summary>
        public bool HasActiveSprites { get; private set; }

        /// <summary>
        /// Executes the Refresh From Registers logic.
        /// </summary>
        public void RefreshFromRegisters()
        {
            HasActiveSprites = false;
            if (_registers == null)
            {
                return;
            }

            ushort dmacon = ReadRegister(Dmacon);
            _dmaEnabled = _readChipWord != null && (dmacon & 0x0200) != 0 && (dmacon & 0x0020) != 0;

            for (int sprite = 0; sprite < 8; sprite++)
            {
                _spritePointers[sprite] = ReadSpritePointer(sprite);
                if (_spritePointers[sprite] != 0 || HasDirectSpriteRegisters(sprite))
                {
                    HasActiveSprites = true;
                }
            }
        }

        /// <summary>
        /// Applies immediate sprite side effects for CPU and Copper writes to SPRx registers.
        /// </summary>
        public void NotifyCustomRegisterWordWrite(uint registerOffset, ushort value, int beamY)
        {
            uint offset = registerOffset & 0x01FEu;
            if (offset >= SpritePointerBase && offset < SpritePointerBase + 32u)
            {
                int sprite = (int)((offset - SpritePointerBase) / 4u);
                if (sprite >= 0 && sprite < 8)
                {
                    _spritePointers[sprite] = ReadSpritePointer(sprite);
                    _states[sprite].Pointer = _spritePointers[sprite];
                    HasActiveSprites = HasActiveSprites || _spritePointers[sprite] != 0;
                }

                return;
            }

            if (offset < SpriteRegisterBase || offset >= SpriteRegisterBase + (SpriteRegisterStride * 8u))
            {
                return;
            }

            int spriteIndex = (int)((offset - SpriteRegisterBase) / SpriteRegisterStride);
            uint localOffset = (offset - SpriteRegisterBase) % SpriteRegisterStride;
            if (spriteIndex < 0 || spriteIndex >= 8)
            {
                return;
            }

            SpriteState state = _states[spriteIndex];
            HasActiveSprites = true;
            if (localOffset == 0u || localOffset == 2u)
            {
                ApplyControlWords(state, ReadSpriteRegister(spriteIndex, 0), ReadSpriteRegister(spriteIndex, 2));
                state.Active = beamY >= state.VStart && beamY < state.VStop;
                state.LineActive = state.Active;
                return;
            }

            if (localOffset == 4u)
            {
                state.DataA = value;
            }
            else if (localOffset == 6u)
            {
                state.DataB = value;
            }
            else
            {
                return;
            }

            if (state.ControlLoaded && !state.Terminated)
            {
                state.Active = beamY >= state.VStart && beamY < state.VStop;
                state.LineActive = state.Active;
            }
        }

        /// <summary>
        /// Attempts to Render Pixel and reports whether it succeeded.
        /// </summary>
        public bool TryRenderPixel(int screenX, int beamY, out uint argb)
        {
            return TryRenderPixel(screenX, beamY, out argb, out _);
        }

        /// <summary>
        /// Attempts to Render Pixel and reports whether it succeeded.
        /// </summary>
        public bool TryRenderPixel(int screenX, int beamY, out uint argb, out int spriteGroup)
        {
            argb = 0;
            spriteGroup = 0;
            if (!HasActiveSprites || _registers == null)
            {
                return false;
            }

            UpdateForBeamY(beamY);

            for (int sprite = 0; sprite < 8; sprite++)
            {
                if ((sprite & 1) == 0 &&
                    TryReadSpritePixel(sprite + 1, screenX, out int oddColor, out bool attached) &&
                    attached)
                {
                    TryReadSpritePixel(sprite, screenX, out int evenColor, out _);
                    int color = evenColor | (oddColor << 2);
                    if (color != 0)
                    {
                        argb = ReadColor(16 + color);
                        spriteGroup = sprite >> 1;
                        return true;
                    }

                    sprite++;
                    continue;
                }

                if (TryReadSpritePixel(sprite, screenX, out int colorIndex, out _))
                {
                    if (colorIndex != 0)
                    {
                        int group = sprite >> 1;
                        argb = ReadColor(16 + (group * 4) + colorIndex);
                        spriteGroup = group;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a bit mask of all sprite numbers that contribute a nontransparent pixel.
        /// </summary>
        public int RenderSpriteMask(int screenX, int beamY)
        {
            if (!HasActiveSprites || _registers == null)
            {
                return 0;
            }

            UpdateForBeamY(beamY);
            int mask = 0;
            for (int sprite = 0; sprite < 8; sprite++)
            {
                if ((sprite & 1) == 0 &&
                    TryReadSpritePixel(sprite + 1, screenX, out int oddColor, out bool attached) &&
                    attached)
                {
                    TryReadSpritePixel(sprite, screenX, out int evenColor, out _);
                    if ((evenColor | (oddColor << 2)) != 0)
                    {
                        mask |= 1 << sprite;
                        mask |= 1 << (sprite + 1);
                    }

                    sprite++;
                    continue;
                }

                if (TryReadSpritePixel(sprite, screenX, out int colorIndex, out _) && colorIndex != 0)
                {
                    mask |= 1 << sprite;
                }
            }

            return mask;
        }

        /// <summary>
        /// Updates For Beam Y from current emulator state.
        /// </summary>
        private void UpdateForBeamY(int beamY)
        {
            if (_lastBeamY < 0 || beamY < _lastBeamY)
            {
                RefreshFromRegisters();
                for (int sprite = 0; sprite < 8; sprite++)
                {
                    _states[sprite].ResetForFrame(_spritePointers[sprite]);
                }
            }

            if (beamY == _lastBeamY)
            {
                return;
            }

            _lastBeamY = beamY;
            for (int sprite = 0; sprite < 8; sprite++)
            {
                if (_dmaEnabled && _spritePointers[sprite] != 0)
                {
                    _states[sprite].LineActive = false;
                }
                else
                {
                    LoadDirectSpriteLine(sprite, beamY);
                }
            }
        }

        /// <summary>
        /// Advances the real sprite DMA state machine for one fixed OCS sprite slot.
        /// </summary>
        public void TickDmaSlot(int sprite, int phase, int beamY)
        {
            if (!_dmaEnabled || _readChipWord == null || sprite < 0 || sprite >= 8 || phase < 0 || phase > 1)
            {
                return;
            }

            UpdateForBeamY(beamY);
            SpriteState state = _states[sprite];
            if (phase == 0)
            {
                state.PendingFetch = SelectFetchKind(state, beamY);
                state.PendingFirstWord = state.PendingFetch == SpriteFetchKind.None
                    ? (ushort)0
                    : ReadChip(state.Pointer);
                return;
            }

            if (state.PendingFetch == SpriteFetchKind.None)
            {
                return;
            }

            ushort second = ReadChip(state.Pointer + 2u);
            if (state.PendingFetch == SpriteFetchKind.Control)
            {
                state.Pointer = _dmaAddress.Add(state.Pointer, 4u);
                ApplyControlWords(state, state.PendingFirstWord, second);
                state.PendingFetch = SpriteFetchKind.None;
                return;
            }

            state.DataA = state.PendingFirstWord;
            state.DataB = second;
            state.Pointer = _dmaAddress.Add(state.Pointer, 4u);
            state.LineActive = true;
            int dataStop = state.VStop + 1;
            state.Active = beamY > state.VStart && beamY <= dataStop;
            if (beamY >= dataStop)
            {
                state.Active = false;
            }

            state.PendingFetch = SpriteFetchKind.None;
        }

        /// <summary>
        /// Selects whether the next sprite slot pair fetches control words or bitmap data.
        /// </summary>
        private static SpriteFetchKind SelectFetchKind(SpriteState state, int beamY)
        {
            if (state.Pointer == 0)
            {
                state.Terminated = true;
                state.ControlLoaded = false;
                return SpriteFetchKind.None;
            }

            if (state.Terminated)
            {
                return SpriteFetchKind.None;
            }

            if (!state.ControlLoaded)
            {
                return SpriteFetchKind.Control;
            }

            int dataStop = state.VStop + 1;
            if (beamY > dataStop)
            {
                return SpriteFetchKind.Control;
            }

            if (beamY > state.VStart && beamY <= dataStop)
            {
                return SpriteFetchKind.Data;
            }

            return SpriteFetchKind.None;
        }

        /// <summary>
        /// Clamps loaded sprite states that were saved after running past a list terminator.
        /// </summary>
        private void SanitizeLoadedSpriteStates()
        {
            if (_readChipWord == null)
            {
                return;
            }

            for (int sprite = 0; sprite < _states.Length; sprite++)
            {
                SanitizeLoadedSpriteState(sprite);
            }
        }

        /// <summary>
        /// Clamps one loaded sprite state to the active list that starts at its register pointer.
        /// </summary>
        private void SanitizeLoadedSpriteState(int sprite)
        {
            uint start = _spritePointers[sprite];
            SpriteState state = _states[sprite];
            if (start == 0 || state.Pointer == 0)
            {
                return;
            }

            uint address = start;
            for (int block = 0; block < 128; block++)
            {
                ushort pos = ReadChip(address);
                ushort ctl = ReadChip(address + 2u);
                if (pos == 0 && ctl == 0)
                {
                    uint terminatorEnd = _dmaAddress.Add(address, 4u);
                    if (IsAddressAtOrAfter(state.Pointer, terminatorEnd, start))
                    {
                        state.ResetForFrame(start);
                        state.Pointer = terminatorEnd;
                        state.Terminated = true;
                        state.ControlLoaded = false;
                    }

                    return;
                }

                int vStart = ((pos >> 8) & 0xFF) | ((ctl & 0x0004) != 0 ? 0x100 : 0);
                int vStop = ((ctl >> 8) & 0xFF) | ((ctl & 0x0002) != 0 ? 0x100 : 0);
                if (vStop <= vStart)
                {
                    vStop += 256;
                }

                uint dataStart = _dmaAddress.Add(address, 4u);
                uint dataEnd = _dmaAddress.Add(dataStart, (uint)Math.Max(0, vStop - vStart) * 4u);
                if (IsAddressInRange(state.Pointer, address, dataEnd, start))
                {
                    return;
                }

                address = dataEnd;
            }
        }

        /// <summary>
        /// Returns whether an address lies inside a non-wrapping sprite-list range.
        /// </summary>
        private static bool IsAddressInRange(uint value, uint start, uint end, uint listStart)
        {
            return value >= start && value <= end && start >= listStart && end >= listStart;
        }

        /// <summary>
        /// Returns whether an address is at or after a terminator inside the same sprite list.
        /// </summary>
        private static bool IsAddressAtOrAfter(uint value, uint threshold, uint listStart)
        {
            return value >= threshold && threshold >= listStart;
        }

        /// <summary>
        /// Loads Direct Sprite Line into emulator state.
        /// </summary>
        private void LoadDirectSpriteLine(int sprite, int beamY)
        {
            SpriteState state = _states[sprite];
            ushort pos = ReadSpriteRegister(sprite, 0);
            ushort ctl = ReadSpriteRegister(sprite, 2);
            ApplyControlWords(state, pos, ctl);
            if (!state.ControlLoaded || state.Terminated)
            {
                return;
            }

            state.Active = beamY >= state.VStart && beamY < state.VStop;
            state.LineActive = state.Active;
            if (state.LineActive)
            {
                state.DataA = ReadSpriteRegister(sprite, 4);
                state.DataB = ReadSpriteRegister(sprite, 6);
            }
        }

        /// <summary>
        /// Applies Control Words to the current state.
        /// </summary>
        private static void ApplyControlWords(SpriteState state, ushort pos, ushort ctl)
        {
            state.Pos = pos;
            state.Ctl = ctl;
            state.Attached = (ctl & 0x0080) != 0;
            state.VStart = ((pos >> 8) & 0xFF) | ((ctl & 0x0004) != 0 ? 0x100 : 0);
            state.VStop = ((ctl >> 8) & 0xFF) | ((ctl & 0x0002) != 0 ? 0x100 : 0);
            if (state.VStop <= state.VStart)
            {
                state.VStop += 256;
            }

            state.HStart = (((pos & 0x00FF) << 1) | (ctl & 0x0001)) - 0x80;
            state.ControlLoaded = pos != 0 || ctl != 0;
            state.Terminated = !state.ControlLoaded;
            state.Active = false;
            state.LineActive = false;
        }

        /// <summary>
        /// Attempts to Read Sprite Pixel and reports whether it succeeded.
        /// </summary>
        private bool TryReadSpritePixel(int sprite, int screenX, out int color, out bool attached)
        {
            color = 0;
            attached = false;
            if (sprite < 0 || sprite >= 8)
            {
                return false;
            }

            SpriteState state = _states[sprite];
            if (!state.LineActive)
            {
                return false;
            }

            attached = state.Attached;
            int lowresX = screenX >> 1;
            if (lowresX < state.HStart || lowresX >= state.HStart + 16)
            {
                return false;
            }

            int bit = 15 - (lowresX - state.HStart);
            color = ((state.DataA >> bit) & 1) | (((state.DataB >> bit) & 1) << 1);
            return true;
        }

        /// <summary>
        /// Reads one sprite DMA pointer from the paired SPRxPTH/SPRxPTL registers.
        /// </summary>
        private uint ReadSpritePointer(int sprite)
        {
            uint offset = SpritePointerBase + (uint)(sprite * 4);
            ushort high = ReadRegister(offset);
            ushort low = ReadRegister(offset + 2);
            return _dmaAddress.FromRegisters(high, low);
        }

        /// <summary>
        /// Reads a sprite DMA word from chip memory using the sprite address mask.
        /// </summary>
        private ushort ReadChip(uint address)
        {
            return _readChipWord(_dmaAddress.Normalize(address));
        }

        /// <summary>
        /// Reads a sprite custom register word by offset.
        /// </summary>
        private ushort ReadRegister(uint offset)
        {
            return _registers != null ? _registers.ReadWord(offset) : (ushort)0;
        }

        /// <summary>
        /// Reads one register from the selected sprite register block.
        /// </summary>
        private ushort ReadSpriteRegister(int sprite, uint registerOffset)
        {
            return ReadRegister(SpriteRegisterBase + (uint)(sprite * SpriteRegisterStride) + registerOffset);
        }

        /// <summary>
        /// Returns whether Direct Sprite Registers is present.
        /// </summary>
        private bool HasDirectSpriteRegisters(int sprite)
        {
            return ReadSpriteRegister(sprite, 0) != 0 ||
                ReadSpriteRegister(sprite, 2) != 0 ||
                ReadSpriteRegister(sprite, 4) != 0 ||
                ReadSpriteRegister(sprite, 6) != 0;
        }

        /// <summary>
        /// Converts a sprite palette entry into a host ARGB pixel.
        /// </summary>
        private uint ReadColor(int index)
        {
            ushort color = ReadRegister(ColorBase + ((uint)(index & 0x1F) * 2u));
            byte red = ExpandNibble((color >> 8) & 0x0F);
            byte green = ExpandNibble((color >> 4) & 0x0F);
            byte blue = ExpandNibble(color & 0x0F);
            return 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
        }

        /// <summary>
        /// Executes the Expand Nibble logic.
        /// </summary>
        private static byte ExpandNibble(int value)
        {
            return (byte)((value & 0x0F) * 0x11);
        }

        /// <summary>
        /// Provides Sprite State behavior used by the emulator.
        /// </summary>
        private sealed class SpriteState
        {
            /// <summary>
            /// Stores the Pointer.
            /// </summary>
            public uint Pointer;
            /// <summary>
            /// Stores the Pos.
            /// </summary>
            public ushort Pos;
            /// <summary>
            /// Stores the control.
            /// </summary>
            public ushort Ctl;
            /// <summary>
            /// Stores the Data A.
            /// </summary>
            public ushort DataA;
            /// <summary>
            /// Stores the Data B.
            /// </summary>
            public ushort DataB;
            /// <summary>
            /// Stores the V Start.
            /// </summary>
            public int VStart;
            /// <summary>
            /// Stores the V Stop.
            /// </summary>
            public int VStop;
            /// <summary>
            /// Stores the H Start.
            /// </summary>
            public int HStart;
            /// <summary>
            /// Stores the Attached.
            /// </summary>
            public bool Attached;
            /// <summary>
            /// Stores the Control Loaded.
            /// </summary>
            public bool ControlLoaded;
            /// <summary>
            /// Stores the Terminated.
            /// </summary>
            public bool Terminated;
            /// <summary>
            /// Stores the Active.
            /// </summary>
            public bool Active;
            /// <summary>
            /// Stores the Line Active.
            /// </summary>
            public bool LineActive;
            /// <summary>
            /// Stores the pending fetch type selected by the first slot of a sprite DMA pair.
            /// </summary>
            public SpriteFetchKind PendingFetch;
            /// <summary>
            /// Stores the first word fetched by the first slot of a sprite DMA pair.
            /// </summary>
            public ushort PendingFirstWord;

            /// <summary>
            /// Resets the value state.
            /// </summary>
            public void Reset()
            {
                ResetForFrame(0);
                Terminated = true;
            }

            /// <summary>
            /// Resets the For Frame state.
            /// </summary>
            public void ResetForFrame(uint pointer)
            {
                Pointer = pointer;
                Pos = 0;
                Ctl = 0;
                DataA = 0;
                DataB = 0;
                VStart = 0;
                VStop = 0;
                HStart = 0;
                Attached = false;
                ControlLoaded = false;
                Terminated = false;
                Active = false;
                LineActive = false;
                PendingFetch = SpriteFetchKind.None;
                PendingFirstWord = 0;
            }
        }

        /// <summary>
        /// Lists the two-word sprite DMA fetch kinds.
        /// </summary>
        private enum SpriteFetchKind
        {
            /// <summary>
            /// No sprite DMA word is fetched for the current pair.
            /// </summary>
            None,
            /// <summary>
            /// The pair fetches SPRxPOS and SPRxCTL control words.
            /// </summary>
            Control,
            /// <summary>
            /// The pair fetches SPRxDATA and SPRxDATB bitmap words.
            /// </summary>
            Data
        }
    }
}
