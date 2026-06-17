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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AmigaEmulator.Core.Memory
{
    /// <summary>
    /// Lists the supported Kickstart ROM Kind values.
    /// </summary>
    public enum KickstartRomKind
    {
        /// <summary>
        /// Represents the M is si ng option.
        /// </summary>
        Missing,
        /// <summary>
        /// Represents the U nk no wn option.
        /// </summary>
        Unknown,
        /// <summary>
        /// Represents the K ic ks ta rt12 option.
        /// </summary>
        Kickstart12,
        /// <summary>
        /// Represents the K ic ks ta rt13 option.
        /// </summary>
        Kickstart13,
        /// <summary>
        /// Represents the K ic ks ta rt204 option.
        /// </summary>
        Kickstart204
    }

    /// <summary>
    /// Provides Kickstart ROM behavior used by the emulator.
    /// </summary>
    public sealed class KickstartRom
    {
        /// <summary>
        /// Tracks the bytes.
        /// </summary>
        private readonly byte[] _bytes;
        /// <summary>
        /// Tracks the overlay Size.
        /// </summary>
        private readonly int _overlaySize;

        /// <summary>
        /// Initializes a new instance of the KickstartRom class.
        /// </summary>
        public KickstartRom()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the KickstartRom class.
        /// </summary>
        public KickstartRom(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                _bytes = new byte[512 * 1024];
                _overlaySize = 512 * 1024;
                Kind = KickstartRomKind.Missing;
                Sha1 = string.Empty;
                return;
            }

            if (bytes.Length != 256 * 1024 && bytes.Length != 512 * 1024 && bytes.Length != 1024 * 1024)
            {
                throw new ArgumentException("Kickstart ROM must be 256 KB, 512 KB, or 1 MB.", nameof(bytes));
            }

            string rawSha1 = ComputeSha1(bytes);
            _bytes = new byte[bytes.Length == 256 * 1024 ? 512 * 1024 : bytes.Length];
            if (bytes.Length == 256 * 1024)
            {
                Array.Copy(bytes, 0, _bytes, 0, bytes.Length);
                Array.Copy(bytes, 0, _bytes, bytes.Length, bytes.Length);
                _overlaySize = 256 * 1024;
            }
            else if (bytes.Length == 512 * 1024 && IsBlank(bytes, 256 * 1024, 256 * 1024))
            {
                Array.Copy(bytes, 0, _bytes, 0, 256 * 1024);
                Array.Copy(bytes, 0, _bytes, 256 * 1024, 256 * 1024);
                _overlaySize = 256 * 1024;
            }
            else
            {
                Array.Copy(bytes, _bytes, bytes.Length);
                _overlaySize = _bytes.Length;
            }

            Sha1 = rawSha1;
            Kind = DetectKind(_bytes, Sha1);
        }

        /// <summary>
        /// Executes the Size logic.
        /// </summary>
        public int Size
        {
            get { return _bytes.Length; }
        }

        /// <summary>
        /// Executes the Overlay Size logic.
        /// </summary>
        public int OverlaySize
        {
            get { return _overlaySize; }
        }

        /// <summary>
        /// Gets or updates the Kind.
        /// </summary>
        public KickstartRomKind Kind { get; private set; }

        /// <summary>
        /// Gets or updates the Sha1.
        /// </summary>
        public string Sha1 { get; private set; }

        /// <summary>
        /// Returns whether Present is true.
        /// </summary>
        public bool IsPresent
        {
            get { return Kind != KickstartRomKind.Missing; }
        }

        /// <summary>
        /// Reads one byte from the mirrored Kickstart ROM image.
        /// </summary>
        public byte Read(uint offset)
        {
            return _bytes[offset % (uint)_bytes.Length];
        }

        /// <summary>
        /// Reads a big-endian longword from the Kickstart ROM image.
        /// </summary>
        public uint ReadLong(uint offset)
        {
            uint normalized = offset % (uint)_bytes.Length;
            return (uint)((Read(normalized) << 24) |
                (Read(normalized + 1) << 16) |
                (Read(normalized + 2) << 8) |
                Read(normalized + 3));
        }

        /// <summary>
        /// Returns the Info.
        /// </summary>
        public string GetInfo()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} size:{1} sha1:{2}",
                Kind,
                Size,
                Sha1);
        }

        /// <summary>
        /// Executes the Detect Kind logic.
        /// </summary>
        private static KickstartRomKind DetectKind(byte[] bytes, string sha1)
        {
            if (IsKnownSha1(sha1, "891E9A547772FE0C6C19B610BAF8BC4EA7FCB785"))
            {
                return KickstartRomKind.Kickstart13;
            }

            if (IsKnownSha1(sha1, "90933936CCE43CA9BC6BF375662C076B27E3C458"))
            {
                return KickstartRomKind.Kickstart13;
            }

            if (IsKnownSha1(sha1, "11F9E62CF299F72184835B7B2A70A16333FC0D88"))
            {
                return KickstartRomKind.Kickstart12;
            }

            if (IsKnownSha1(sha1, "7CCB02E02F579E6A0F7A16164B3B374AD9E89AE4"))
            {
                return KickstartRomKind.Kickstart204;
            }

            ushort wordAtTwo = (ushort)((bytes[2] << 8) | bytes[3]);
            uint resetPc = (uint)((bytes[4] << 24) | (bytes[5] << 16) | (bytes[6] << 8) | bytes[7]);
            if (wordAtTwo == 0x4EF9 && resetPc >= MemoryMap.KickstartStart && resetPc <= MemoryMap.KickstartEnd && bytes.Length == 512 * 1024)
            {
                return KickstartRomKind.Kickstart13;
            }

            return KickstartRomKind.Unknown;
        }

        /// <summary>
        /// Returns whether Known Sha1 is true.
        /// </summary>
        private static bool IsKnownSha1(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns whether Blank is true.
        /// </summary>
        private static bool IsBlank(byte[] bytes, int start, int length)
        {
            for (int index = start; index < start + length; index++)
            {
                if (bytes[index] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Executes the Compute Sha1 logic.
        /// </summary>
        private static string ComputeSha1(byte[] bytes)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("X2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }
    }
}
