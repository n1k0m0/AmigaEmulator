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
namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>
    /// Tracks Denise collision masks and collision register state.
    /// </summary>
    public sealed class CollisionUnit
    {
        /// <summary>
        /// Tracks CLXCON collision control bits.
        /// </summary>
        private ushort _control;
        [System.NonSerialized] private ushort _extendedControl;
        [System.NonSerialized] private int _match, _oddEnabled, _evenEnabled;
        public ushort ExtendedControl
        {
            get => _extendedControl;
            set { _extendedControl = (ushort)(value & 0xC3); DecodeMasks(); }
        }
        private void DecodeMasks()
        {
            int enabled = ((_control >> 6) & 0x3F) | (_extendedControl & 0xC0);
            _match = (_control & 0x3F) | ((_extendedControl & 3) << 6);
            _oddEnabled = enabled & 0x55; _evenEnabled = enabled & 0xAA;
        }

        public ushort CollisionFlags { get; private set; }

        /// <summary>
        /// Returns whether every readable CLXDAT collision bit is already latched.
        /// </summary>
        public bool IsSaturated
        {
            get { return (CollisionFlags & 0x7FFF) == 0x7FFF; }
        }

        public ushort Control
        {
            get { return _control; }
            set { _control = value; DecodeMasks(); }
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            _control = 0;
            _extendedControl = 0;
            DecodeMasks();
            CollisionFlags = 0;
        }

        /// <summary>
        /// Reads CLXDAT and clears the latched collision bits.
        /// </summary>
        public ushort ReadAndClear()
        {
            ushort value = CollisionFlags;
            CollisionFlags = 0;
            return value;
        }

        /// <summary>
        /// Reports the bitplane and sprite classes visible at one Denise pixel.
        /// </summary>
        public void ObservePixel(int bitplaneMask, int spriteMask)
        {
            if (IsSaturated)
            {
                return;
            }

            int compared = bitplaneMask ^ _match;
            bool pf1 = (compared & _oddEnabled) == 0;
            bool pf2 = (compared & _evenEnabled) == 0;
            int spritePairs = BuildSpritePairMask(spriteMask);

            ushort flags = 0;
            if (pf1 && pf2)
            {
                flags |= 1 << 0;
            }

            if (spritePairs != 0)
            {
                for (int pair = 0; pair < 4; pair++)
                {
                    if ((spritePairs & (1 << pair)) != 0)
                    {
                        if (pf1)
                        {
                            flags |= (ushort)(1 << (1 + pair));
                        }

                        if (pf2)
                        {
                            flags |= (ushort)(1 << (5 + pair));
                        }
                    }
                }

                SetSpritePairCollision(spritePairs, 0, 1, 9, ref flags);
                SetSpritePairCollision(spritePairs, 0, 2, 10, ref flags);
                SetSpritePairCollision(spritePairs, 0, 3, 11, ref flags);
                SetSpritePairCollision(spritePairs, 1, 2, 12, ref flags);
                SetSpritePairCollision(spritePairs, 1, 3, 13, ref flags);
                SetSpritePairCollision(spritePairs, 2, 3, 14, ref flags);
            }

            if (flags != 0)
            {
                CollisionFlags |= (ushort)(flags & 0x7FFF);
            }
        }

        private int BuildSpritePairMask(int spriteMask)
        {
            if (spriteMask == 0) return 0;
            int pairs = 0;
            for (int pair = 0; pair < 4; pair++)
            {
                int even = pair * 2;
                int odd = even + 1;
                bool includeOdd = (_control & (1 << (12 + pair))) != 0;
                if ((spriteMask & (1 << even)) != 0 ||
                    (includeOdd && (spriteMask & (1 << odd)) != 0))
                {
                    pairs |= 1 << pair;
                }
            }

            return pairs;
        }

        /// <summary>
        /// Adds one sprite-pair to sprite-pair collision flag.
        /// </summary>
        private static void SetSpritePairCollision(int spritePairs, int first, int second, int flagBit, ref ushort flags)
        {
            int mask = (1 << first) | (1 << second);
            if ((spritePairs & mask) == mask)
            {
                flags |= (ushort)(1 << flagBit);
            }
        }
    }
}
