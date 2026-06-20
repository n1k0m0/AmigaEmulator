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

        /// <summary>
        /// Gets or sets Collision Flags.
        /// </summary>
        public ushort CollisionFlags { get; private set; }

        /// <summary>
        /// Gets or sets the CLXCON collision control register.
        /// </summary>
        public ushort Control
        {
            get { return _control; }
            set { _control = value; }
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            _control = 0;
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
            bool pf1 = IsPlayfieldMatched(bitplaneMask, 0x15);
            bool pf2 = IsPlayfieldMatched(bitplaneMask, 0x2A);
            bool[] spritePairs = BuildSpritePairMask(spriteMask);

            ushort flags = 0;
            if (pf1 && pf2)
            {
                flags |= 1 << 0;
            }

            for (int pair = 0; pair < 4; pair++)
            {
                if (spritePairs[pair])
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
            CollisionFlags |= (ushort)(flags & 0x7FFF);
        }

        /// <summary>
        /// Returns whether one playfield group satisfies the current CLXCON mask.
        /// </summary>
        private bool IsPlayfieldMatched(int bitplaneMask, int playfieldMask)
        {
            if ((bitplaneMask & playfieldMask) == 0)
            {
                return false;
            }

            int enabled = (_control >> 6) & 0x3F;
            if (enabled == 0)
            {
                return true;
            }

            int match = _control & 0x3F;
            for (int plane = 0; plane < 6; plane++)
            {
                int bit = 1 << plane;
                if ((enabled & bit) == 0)
                {
                    continue;
                }

                bool actualSet = (bitplaneMask & bit) != 0;
                bool requiredSet = (match & bit) != 0;
                if (actualSet != requiredSet)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds the CLXDAT sprite pair inputs after CLXCON odd-sprite inclusion bits.
        /// </summary>
        private bool[] BuildSpritePairMask(int spriteMask)
        {
            var pairs = new bool[4];
            for (int pair = 0; pair < 4; pair++)
            {
                int even = pair * 2;
                int odd = even + 1;
                bool includeOdd = (_control & (1 << (12 + pair))) != 0;
                pairs[pair] = (spriteMask & (1 << even)) != 0 ||
                    (includeOdd && (spriteMask & (1 << odd)) != 0);
            }

            return pairs;
        }

        /// <summary>
        /// Adds one sprite-pair to sprite-pair collision flag.
        /// </summary>
        private static void SetSpritePairCollision(bool[] spritePairs, int first, int second, int flagBit, ref ushort flags)
        {
            if (spritePairs[first] && spritePairs[second])
            {
                flags |= (ushort)(1 << flagBit);
            }
        }
    }
}
