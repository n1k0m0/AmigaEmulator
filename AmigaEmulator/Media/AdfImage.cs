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

namespace AmigaEmulator.Core.Media
{
    /// <summary>
    /// Represents a standard Amiga ADF disk image and exposes sector and track access helpers.
    /// </summary>
    public sealed class AdfImage
    {
        /// <summary>
        /// Standard 880 KB double-density ADF size in bytes.
        /// </summary>
        public const int StandardSize = 901120;
        /// <summary>
        /// Number of cylinders in a standard double-density ADF.
        /// </summary>
        public const int Tracks = 80;
        /// <summary>
        /// Number of disk sides in a standard double-density ADF.
        /// </summary>
        public const int Sides = 2;
        /// <summary>
        /// Number of AmigaDOS sectors per logical track.
        /// </summary>
        public const int SectorsPerTrack = 11;
        /// <summary>
        /// Size of one AmigaDOS sector in bytes.
        /// </summary>
        public const int SectorSize = 512;

        /// <summary>
        /// Stores the mutable in-memory disk image bytes.
        /// </summary>
        private readonly byte[] _bytes;
        /// <summary>
        /// Tracks whether the in-memory ADF differs from its host file.
        /// </summary>
        private bool _dirty;

        /// <summary>
        /// Initializes a new instance of the AdfImage class.
        /// </summary>
        private AdfImage(string path, byte[] bytes)
        {
            Path = path;
            _bytes = bytes;
        }

        /// <summary>
        /// Gets the host file path backing this ADF image.
        /// </summary>
        public string Path { get; private set; }

        /// <summary>
        /// Gets the current in-memory image size in bytes.
        /// </summary>
        public int Size
        {
            get { return _bytes.Length; }
        }

        /// <summary>
        /// Gets whether sector writes need to be flushed to the host file.
        /// </summary>
        public bool IsDirty
        {
            get { return _dirty; }
        }

        /// <summary>
        /// Returns whether Standard Double Density is true.
        /// </summary>
        public bool IsStandardDoubleDensity
        {
            get { return _bytes.Length == StandardSize; }
        }

        /// <summary>
        /// Loads data into emulator state.
        /// </summary>
        public static AdfImage Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("ADF path is required.", nameof(path));
            }

            byte[] bytes = File.ReadAllBytes(path);
            return new AdfImage(path, bytes);
        }

        /// <summary>
        /// Reads one byte from the raw ADF image with bounds protection.
        /// </summary>
        public byte ReadByte(int offset)
        {
            if (offset < 0 || offset >= _bytes.Length)
            {
                return 0;
            }

            return _bytes[offset];
        }

        /// <summary>
        /// Copies a bounded byte range from the raw ADF image into a destination buffer.
        /// </summary>
        public int ReadBytes(int offset, byte[] destination, int destinationOffset, int count)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destinationOffset < 0 || count < 0 || destinationOffset + count > destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationOffset));
            }

            if (offset < 0 || offset >= _bytes.Length || count == 0)
            {
                return 0;
            }

            int available = Math.Min(count, _bytes.Length - offset);
            Buffer.BlockCopy(_bytes, offset, destination, destinationOffset, available);
            return available;
        }

        /// <summary>
        /// Returns the Sector Offset.
        /// </summary>
        public int GetSectorOffset(int track, int side, int sector)
        {
            if (track < 0 || track >= Tracks || side < 0 || side >= Sides || sector < 0 || sector >= SectorsPerTrack)
            {
                return -1;
            }

            return (((track * Sides) + side) * SectorsPerTrack + sector) * SectorSize;
        }

        /// <summary>
        /// Copies one logical ADF sector into the destination buffer.
        /// </summary>
        public int ReadSector(int track, int side, int sector, byte[] destination, int destinationOffset)
        {
            int offset = GetSectorOffset(track, side, sector);
            if (offset < 0)
            {
                return 0;
            }

            return ReadBytes(offset, destination, destinationOffset, SectorSize);
        }

        /// <summary>
        /// Copies one logical sector into the mutable in-memory ADF image.
        /// </summary>
        public bool WriteSector(int track, int side, int sector, byte[] source, int sourceOffset)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (sourceOffset < 0 || sourceOffset + SectorSize > source.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceOffset));
            }

            int offset = GetSectorOffset(track, side, sector);
            if (offset < 0 || offset + SectorSize > _bytes.Length)
            {
                return false;
            }

            Buffer.BlockCopy(source, sourceOffset, _bytes, offset, SectorSize);
            _dirty = true;
            return true;
        }

        /// <summary>
        /// Persists dirty ADF bytes back to the original host file using a replace-by-rename write.
        /// </summary>
        public void Flush()
        {
            if (!_dirty)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(Path))
            {
                throw new InvalidOperationException("ADF image has no host path.");
            }

            string fullPath = System.IO.Path.GetFullPath(Path);
            string directory = System.IO.Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Directory.GetCurrentDirectory();
            }

            Directory.CreateDirectory(directory);
            string tempPath = System.IO.Path.Combine(directory, System.IO.Path.GetFileName(fullPath) + ".tmp");
            string backupPath = System.IO.Path.Combine(directory, System.IO.Path.GetFileName(fullPath) + ".bak");
            File.WriteAllBytes(tempPath, _bytes);
            if (File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, backupPath, true);
                TryDelete(backupPath);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }

            _dirty = false;
        }

        /// <summary>
        /// Deletes a temporary file while ignoring cleanup errors.
        /// </summary>
        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Builds the Track Stream.
        /// </summary>
        public AdfTrackStream BuildTrackStream(int track, int side)
        {
            return AdfTrackStream.Build(this, track, side);
        }
    }
}
