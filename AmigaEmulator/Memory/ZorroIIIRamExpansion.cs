// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;

namespace AmigaEmulator.Core.Memory
{
    /// <summary>RAM-only slots with the sequential /CFGIN to /CFGOUT Autoconfig chain.</summary>
    public sealed class ZorroIIIRamExpansion
    {
        private ZorroIIIRamAutoConfig[] _cards;
        private readonly ZorroIIIRamAutoConfig _empty = new ZorroIIIRamAutoConfig(0);
        public IReadOnlyList<ZorroIIIRamAutoConfig> Cards => Array.AsReadOnly(_cards);
        public ZorroIIIRamAutoConfig First => _cards.Length == 0 ? _empty : _cards[0];
        private ZorroIIIRamAutoConfig ConfigCard
        {
            get
            {
                foreach (var card in _cards) if (!card.Configured) return card;
                return null;
            }
        }
        public ZorroIIIRamExpansion(IReadOnlyList<int> sizes)
        {
            _cards = new ZorroIIIRamAutoConfig[sizes.Count];
            for (int i = 0; i < sizes.Count; i++) _cards[i] = new ZorroIIIRamAutoConfig(sizes[i]);
        }
        private ZorroIIIRamAutoConfig RamCard(uint address)
        {
            foreach (var card in _cards) if (card.MapsRam(address)) return card;
            throw new InvalidOperationException("No Zorro III RAM card decodes this address.");
        }
        public bool MapsRam(uint address)
        {
            foreach (var card in _cards) if (card.MapsRam(address)) return true;
            return false;
        }
        public bool MapsConfig(uint address) => ConfigCard?.MapsConfig(address) == true;
        public byte ReadRam(uint address) => RamCard(address).ReadRam(address);
        public byte PeekRam(uint address) => RamCard(address).PeekRam(address);
        public void WriteRam(uint address, byte value) => RamCard(address).WriteRam(address, value);
        public byte ReadConfig(uint address) => ConfigCard?.ReadConfig(address) ?? 0xFF;
        public byte PeekConfig(uint address) => ConfigCard?.PeekConfig(address) ?? 0xFF;
        public void WriteConfigByte(uint address, byte value) => ConfigCard?.WriteConfigByte(address, value);
        public void WriteConfigWord(uint address, ushort value) => ConfigCard?.WriteConfigWord(address, value);
        public void Reset() { foreach (var card in _cards) card.Reset(); }
        public void ClearRam() { foreach (var card in _cards) card.ClearRam(); }
        public void SaveState(BinaryWriter writer) { foreach (var card in _cards) card.SaveState(writer); }
        public void LoadState(BinaryReader reader)
        {
            // Parse every card first, so a truncated later card cannot partly replace this expansion.
            var restored = new ZorroIIIRamAutoConfig[_cards.Length];
            bool unconfigured = false;
            for (int i = 0; i < restored.Length; i++)
            {
                restored[i] = new ZorroIIIRamAutoConfig(_cards[i].Size);
                restored[i].LoadState(reader);
                if (unconfigured && restored[i].Configured)
                    throw new InvalidDataException("Zorro III state skips an unconfigured slot.");
                unconfigured |= !restored[i].Configured;
            }
            _cards = restored;
        }
    }
}
