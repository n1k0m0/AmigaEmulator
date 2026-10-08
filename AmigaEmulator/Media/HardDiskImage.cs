using System;
using System.Buffers.Binary;
using System.IO;

namespace AmigaEmulator.Core.Media
{
    /// <summary>A raw, file-backed ATA disk. Only image files are opened, never host devices.</summary>
    public sealed class HardDiskImage : IDisposable
    {
        public const int SectorSize = 512;
        public const int Heads = 16;
        public const int SectorsPerTrack = 63;
        public const long MaximumBytes = 2L * 1024 * 1024 * 1024;
        private readonly FileStream _stream;
        public string Path { get; }
        public bool ReadOnly { get; }
        public long SectorCount => _stream.Length / SectorSize;
        public int Cylinders => (int)(SectorCount / (Heads * SectorsPerTrack));

        public HardDiskImage(string path, bool readOnly)
        {
            Path = System.IO.Path.GetFullPath(path);
            if (!string.Equals(System.IO.Path.GetExtension(Path), ".hdf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Select a raw .hdf image.");
            ReadOnly = readOnly;
            _stream = new FileStream(Path, FileMode.Open, readOnly ? FileAccess.Read : FileAccess.ReadWrite,
                readOnly ? FileShare.Read : FileShare.None, 4096, FileOptions.RandomAccess);
            if (_stream.Length < SectorSize * Heads * SectorsPerTrack * 2L ||
                _stream.Length > MaximumBytes || _stream.Length % SectorSize != 0)
            {
                _stream.Dispose();
                throw new InvalidDataException("HDF size must be sector-aligned, at least 1 MiB and at most 2 GiB.");
            }
        }

        public void ReadSector(long sector, Span<byte> buffer)
        {
            ValidateSector(sector, buffer.Length);
            _stream.Position = sector * SectorSize;
            _stream.ReadExactly(buffer[..SectorSize]);
        }
        public void WriteSector(long sector, ReadOnlySpan<byte> buffer)
        {
            if (ReadOnly) throw new IOException("Hard disk is write protected.");
            ValidateSector(sector, buffer.Length);
            _stream.Position = sector * SectorSize;
            _stream.Write(buffer[..SectorSize]);
        }
        private void ValidateSector(long sector, int length)
        {
            if (sector < 0 || sector >= SectorCount || length < SectorSize)
                throw new IOException("Sector outside hard disk.");
        }
        public void Flush() { if (!ReadOnly) _stream.Flush(true); }
        public void Dispose() => _stream.Dispose();

        /// <summary>Creates a new whole-disk image with a bootable DH0 RDB partition; format/install in Workbench.</summary>
        public static void Create(string path, int sizeMiB)
        {
            if (sizeMiB < 16 || sizeMiB > 2048) throw new ArgumentOutOfRangeException(nameof(sizeMiB));
            if (!string.Equals(System.IO.Path.GetExtension(path), ".hdf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use the .hdf extension.");
            long cylinders = sizeMiB * 1024L * 1024 / (SectorSize * Heads * SectorsPerTrack);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            file.SetLength(cylinders * SectorSize * Heads * SectorsPerTrack);
            byte[] rdb = new byte[SectorSize], part = new byte[SectorSize];
            Put(rdb, 0, 0x5244534B); Put(rdb, 1, 64); Put(rdb, 3, 7); Put(rdb, 4, SectorSize);
            Put(rdb, 5, 7);
            Put(rdb, 6, uint.MaxValue); Put(rdb, 7, 1); Put(rdb, 8, uint.MaxValue); Put(rdb, 9, uint.MaxValue);
            Put(rdb, 16, (uint)cylinders); Put(rdb, 17, SectorsPerTrack); Put(rdb, 18, Heads);
            Put(rdb, 19, 1); Put(rdb, 24, (uint)cylinders); Put(rdb, 25, (uint)cylinders); Put(rdb, 26, 3);
            Put(rdb, 32, 0); Put(rdb, 33, Heads * SectorsPerTrack - 1);
            Put(rdb, 34, 1); Put(rdb, 35, (uint)cylinders - 1); Put(rdb, 36, Heads * SectorsPerTrack);
            Put(rdb, 38, 1);
            System.Text.Encoding.ASCII.GetBytes("CODEX   ").CopyTo(rdb, 160);
            System.Text.Encoding.ASCII.GetBytes("AMIGA HDF       ").CopyTo(rdb, 168);
            Put(part, 0, 0x50415254); Put(part, 1, 64); Put(part, 3, 7); Put(part, 4, uint.MaxValue);
            Put(part, 5, 1); // PBFF_BOOTABLE
            part[36] = 3; part[37] = (byte)'D'; part[38] = (byte)'H'; part[39] = (byte)'0';
            int e = 32;
            Put(part, e, 16); Put(part, e + 1, 128); Put(part, e + 3, Heads); Put(part, e + 4, 1);
            Put(part, e + 5, SectorsPerTrack); Put(part, e + 6, 2); Put(part, e + 8, 1);
            Put(part, e + 9, 1); Put(part, e + 10, (uint)cylinders - 1); Put(part, e + 11, 30);
            Put(part, e + 12, 1); Put(part, e + 13, 0x1FE00); Put(part, e + 14, 0x7FFFFFFE);
            Put(part, e + 15, 0); Put(part, e + 16, 0x444F5301); // ROM FastFileSystem
            Checksum(rdb); Checksum(part);
            file.Write(rdb); file.Write(part); file.Flush(true);
        }
        private static void Put(byte[] data, int word, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(word * 4, 4), value);
        private static void Checksum(byte[] data)
        {
            uint sum = 0;
            for (int i = 0; i < 64; i++) sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i * 4, 4)));
            Put(data, 2, unchecked(0u - sum));
        }
    }
}
