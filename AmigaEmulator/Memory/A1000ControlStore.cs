// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;
using System.Security.Cryptography;

namespace AmigaEmulator.Core.Memory
{
    /// <summary>
    /// A1000 bootstrap/WCS address decoder, available through explicit bus bring-up.
    /// It is not a selectable motherboard profile and owns no host files or ROM downloads.
    /// </summary>
    public sealed class A1000ControlStore
    {
        public const uint WindowStart = 0xF80000;
        public const uint RamStart = 0xFC0000;
        public const uint WindowEnd = 0xFFFFFF;
        public const int RamSize = 256 * 1024;
        private readonly byte[] _bootstrap;
        private readonly byte[] _bootstrapHash;
        private readonly byte[] _ram = new byte[RamSize];

        public bool BootstrapVisible { get; private set; } = true;
        public bool WriteProtected => !BootstrapVisible;

        internal A1000ControlStore ReadStateCopy(BinaryReader reader)
        {
            var restored = new A1000ControlStore(_bootstrap);
            restored.LoadState(reader);
            return restored;
        }

        /// <param name="bootstrap">
        /// User-provided 8 KiB logical bootstrap or 64 KiB interleaved physical ROM dump.
        /// </param>
        public A1000ControlStore(byte[] bootstrap)
        {
            ArgumentNullException.ThrowIfNull(bootstrap);
            if (bootstrap.Length != 8192 && bootstrap.Length != 65536)
                throw new ArgumentException("A1000 bootstrap must be an 8 or 64 KiB image.", nameof(bootstrap));
            _bootstrap = (byte[])bootstrap.Clone();
            _bootstrapHash = SHA256.HashData(_bootstrap);
        }

        public byte ReadByte(uint address)
        {
            CheckAddress(address);
            if (BootstrapVisible && address < RamStart)
                return _bootstrap[(address - WindowStart) % (uint)_bootstrap.Length];
            return _ram[(address - WindowStart) & (RamSize - 1)];
        }

        public void WriteByte(uint address, byte value)
        {
            CheckAddress(address);
            if (WriteProtected) return;
            if (address < RamStart)
            {
                // Any write to the bootstrap aperture closes the write latch.
                // The triggering write does not reach the newly visible RAM mirror.
                BootstrapVisible = false;
                return;
            }
            _ram[address - RamStart] = value;
        }

        /// <summary>RESET exposes the bootstrap and permits WCS writes, retaining its contents.</summary>
        public void Reset() => BootstrapVisible = true;

        /// <summary>Power loss also clears the volatile WCS. No Kickstart is supplied by the host.</summary>
        public void PowerCycle()
        {
            Array.Clear(_ram);
            Reset();
        }

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(1);
            writer.Write(_bootstrapHash);
            writer.Write(BootstrapVisible);
            writer.Write(_ram);
        }

        public void LoadState(BinaryReader reader)
        {
            if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported A1000 control store state.");
            byte[] hash = reader.ReadBytes(_bootstrapHash.Length);
            if (hash.Length != _bootstrapHash.Length) throw new EndOfStreamException();
            if (!CryptographicOperations.FixedTimeEquals(hash, _bootstrapHash))
                throw new InvalidDataException("A1000 bootstrap image does not match the saved state.");
            byte visible = reader.ReadByte();
            if (visible > 1) throw new InvalidDataException("Invalid A1000 bootstrap latch.");
            byte[] ram = reader.ReadBytes(RamSize);
            if (ram.Length != RamSize) throw new EndOfStreamException();
            ram.CopyTo(_ram, 0);
            BootstrapVisible = visible != 0;
        }

        private static void CheckAddress(uint address)
        {
            if (address < WindowStart || address > WindowEnd)
                throw new ArgumentOutOfRangeException(nameof(address));
        }
    }
}
