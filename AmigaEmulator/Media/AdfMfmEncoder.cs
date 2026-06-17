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
using System.Collections.Generic;

namespace AmigaEmulator.Core.Media
{
    /// <summary>
    /// Represents one successfully decoded standard AmigaDOS MFM sector.
    /// </summary>
    public sealed class AdfDecodedSector
    {
        /// <summary>
        /// Gets or updates the logical AmigaDOS track encoded in the sector header.
        /// </summary>
        public int LogicalTrack { get; set; }

        /// <summary>
        /// Gets or updates the sector number encoded in the sector header.
        /// </summary>
        public int Sector { get; set; }

        /// <summary>
        /// Gets or updates the sectors-until-gap value encoded in the sector header.
        /// </summary>
        public int SectorsUntilGap { get; set; }

        /// <summary>
        /// Gets or updates the decoded 512-byte sector payload.
        /// </summary>
        public byte[] Data { get; set; }
    }

    /// <summary>
    /// Encodes AmigaDOS sectors into the MFM word stream seen by Paula.
    /// </summary>
    public static class AdfMfmEncoder
    {
        /// <summary>
        /// Defines the sync Word constant.
        /// </summary>
        public const ushort SyncWord = 0x4489;
        /// <summary>
        /// Defines the Gap Word constant.
        /// </summary>
        public const ushort GapWord = 0xAAAA;
        /// <summary>
        /// Defines the Standard Track Bytes constant.
        /// </summary>
        public const int StandardTrackBytes = 0x332C;
        /// <summary>
        /// Defines the Standard Track Words constant.
        /// </summary>
        public const int StandardTrackWords = StandardTrackBytes / 2;
        /// <summary>
        /// Defines the Sector Words constant.
        /// </summary>
        public const int SectorWords = 544;
        /// <summary>
        /// Defines the Track Gap Words constant.
        /// </summary>
        public const int TrackGapWords = StandardTrackWords - (AdfImage.SectorsPerTrack * SectorWords);

        /// <summary>
        /// Defines the MFM Data Mask constant.
        /// </summary>
        private const uint MfmDataMask = 0x55555555u;
        /// <summary>
        /// Defines the MFM Clock Mask constant.
        /// </summary>
        private const uint MfmClockMask = 0xAAAAAAAAu;
        /// <summary>
        /// Defines the Amiga Dos Format Byte constant.
        /// </summary>
        private const byte AmigaDosFormatByte = 0xFF;

        /// <summary>
        /// Encodes Track for Amiga-compatible use.
        /// </summary>
        public static AdfMfmTrack EncodeTrack(AdfImage image, int cylinder, int side)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            ValidateTrackSide(cylinder, side);
            if (!image.IsStandardDoubleDensity)
            {
                throw new NotSupportedException("Only standard double-density 880 KiB ADF images are supported.");
            }

            ushort[] words = EncodeTrackWords(image, cylinder, side);
            return new AdfMfmTrack(cylinder, side, words);
        }

        /// <summary>
        /// Encodes Track Words for Amiga-compatible use.
        /// </summary>
        public static ushort[] EncodeTrackWords(AdfImage image, int cylinder, int side)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            ValidateTrackSide(cylinder, side);
            if (!image.IsStandardDoubleDensity)
            {
                throw new NotSupportedException("Only standard double-density 880 KiB ADF images are supported.");
            }

            ushort[] words = new ushort[StandardTrackWords];
            int writeIndex = 0;
            for (; writeIndex < TrackGapWords; writeIndex++)
            {
                words[writeIndex] = GapWord;
            }

            byte[] sectorData = new byte[AdfImage.SectorSize];
            for (int sector = 0; sector < AdfImage.SectorsPerTrack; sector++)
            {
                Array.Clear(sectorData, 0, sectorData.Length);
                image.ReadSector(cylinder, side, sector, sectorData, 0);

                ushort[] sectorWords = EncodeSectorWords(
                    sectorData,
                    (byte)((cylinder * AdfImage.Sides) + side),
                    (byte)sector,
                    (byte)(AdfImage.SectorsPerTrack - sector));

                Array.Copy(sectorWords, 0, words, writeIndex, sectorWords.Length);
                writeIndex += sectorWords.Length;
            }

            return words;
        }

        /// <summary>
        /// Encodes Sector Words for Amiga-compatible use.
        /// </summary>
        public static ushort[] EncodeSectorWords(byte[] sectorData, byte logicalTrack, byte sector, byte sectorsUntilGap)
        {
            if (sectorData == null)
            {
                throw new ArgumentNullException(nameof(sectorData));
            }

            if (sectorData.Length != AdfImage.SectorSize)
            {
                throw new ArgumentException("A standard AmigaDOS sector must contain exactly 512 bytes.", nameof(sectorData));
            }

            var words = new List<ushort>(SectorWords);
            words.Add(GapWord);
            words.Add(GapWord);
            words.Add(SyncWord);
            words.Add(SyncWord);

            uint headerLong =
                ((uint)AmigaDosFormatByte << 24) |
                ((uint)logicalTrack << 16) |
                ((uint)sector << 8) |
                sectorsUntilGap;

            var headerWords = new List<ushort>(20);
            uint headerChecksum = 0;
            AppendOddEvenLong(headerWords, headerLong, ref headerChecksum);

            byte[] label = new byte[16];
            AppendOddEvenBlock(headerWords, label, ref headerChecksum);
            headerChecksum &= MfmDataMask;

            var dataWords = new List<ushort>(AdfImage.SectorSize);
            uint dataChecksum = 0;
            AppendOddEvenBlock(dataWords, sectorData, ref dataChecksum);
            dataChecksum &= MfmDataMask;

            foreach (ushort word in headerWords)
            {
                words.Add(word);
            }

            uint ignoredChecksum = 0;
            AppendOddEvenLong(words, headerChecksum, ref ignoredChecksum);
            ignoredChecksum = 0;
            AppendOddEvenLong(words, dataChecksum, ref ignoredChecksum);

            foreach (ushort word in dataWords)
            {
                words.Add(word);
            }

            if (words.Count != SectorWords)
            {
                throw new InvalidOperationException("Internal AmigaDOS MFM sector size mismatch.");
            }

            return words.ToArray();
        }

        /// <summary>
        /// Decodes Odd Even Long from Amiga-compatible data.
        /// </summary>
        public static uint DecodeOddEvenLong(uint oddMfmLong, uint evenMfmLong)
        {
            return ((oddMfmLong & MfmDataMask) << 1) | (evenMfmLong & MfmDataMask);
        }

        /// <summary>
        /// Attempts to decode one 544-word standard AmigaDOS MFM sector from a word stream.
        /// </summary>
        public static bool TryDecodeSectorWords(IList<ushort> words, int startIndex, out AdfDecodedSector sector)
        {
            sector = null;
            if (words == null || startIndex < 0 || startIndex + SectorWords > words.Count)
            {
                return false;
            }

            if (words[startIndex + 2] != SyncWord || words[startIndex + 3] != SyncWord)
            {
                return false;
            }

            uint headerLong = DecodeOddEvenLong(
                ReadMfmLong(words, startIndex + 4),
                ReadMfmLong(words, startIndex + 6));

            byte format = (byte)(headerLong >> 24);
            int logicalTrack = (byte)(headerLong >> 16);
            int sectorNumber = (byte)(headerLong >> 8);
            int sectorsUntilGap = (byte)headerLong;
            if (format != AmigaDosFormatByte || sectorNumber < 0 || sectorNumber >= AdfImage.SectorsPerTrack)
            {
                return false;
            }

            uint expectedHeaderChecksum = DecodeOddEvenLong(
                ReadMfmLong(words, startIndex + 24),
                ReadMfmLong(words, startIndex + 26));
            uint actualHeaderChecksum = ChecksumEncodedWords(words, startIndex + 4, 20);
            if (actualHeaderChecksum != expectedHeaderChecksum)
            {
                return false;
            }

            uint expectedDataChecksum = DecodeOddEvenLong(
                ReadMfmLong(words, startIndex + 28),
                ReadMfmLong(words, startIndex + 30));
            uint actualDataChecksum = ChecksumEncodedWords(words, startIndex + 32, 512);
            if (actualDataChecksum != expectedDataChecksum)
            {
                return false;
            }

            byte[] data = new byte[AdfImage.SectorSize];
            DecodeOddEvenBlock(words, startIndex + 32, data);
            sector = new AdfDecodedSector
            {
                LogicalTrack = logicalTrack,
                Sector = sectorNumber,
                SectorsUntilGap = sectorsUntilGap,
                Data = data
            };
            return true;
        }

        /// <summary>
        /// Executes the Validate Track Side logic.
        /// </summary>
        private static void ValidateTrackSide(int cylinder, int side)
        {
            if (cylinder < 0 || cylinder >= AdfImage.Tracks)
            {
                throw new ArgumentOutOfRangeException(nameof(cylinder));
            }

            if (side < 0 || side >= AdfImage.Sides)
            {
                throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        /// <summary>
        /// Executes the Append Odd Even Block logic.
        /// </summary>
        private static void AppendOddEvenBlock(List<ushort> destination, byte[] bytes, ref uint checksum)
        {
            int longCount = bytes.Length / 4;
            uint[] oddBits = new uint[longCount];
            uint[] evenBits = new uint[longCount];

            for (int index = 0; index < longCount; index++)
            {
                uint value = ReadBigEndianLong(bytes, index * 4);
                oddBits[index] = (value >> 1) & MfmDataMask;
                evenBits[index] = value & MfmDataMask;
            }

            for (int index = 0; index < longCount; index++)
            {
                AppendEncodedMaskedLong(destination, oddBits[index], ref checksum);
            }

            for (int index = 0; index < longCount; index++)
            {
                AppendEncodedMaskedLong(destination, evenBits[index], ref checksum);
            }
        }

        /// <summary>
        /// Executes the Append Odd Even Long logic.
        /// </summary>
        private static void AppendOddEvenLong(List<ushort> destination, uint value, ref uint checksum)
        {
            AppendEncodedMaskedLong(destination, (value >> 1) & MfmDataMask, ref checksum);
            AppendEncodedMaskedLong(destination, value & MfmDataMask, ref checksum);
        }

        /// <summary>
        /// Executes the Append Encoded Masked Long logic.
        /// </summary>
        private static void AppendEncodedMaskedLong(List<ushort> destination, uint dataBits, ref uint checksum)
        {
            uint encoded = EncodeMaskedLong(dataBits);
            checksum ^= encoded & MfmDataMask;
            destination.Add((ushort)(encoded >> 16));
            destination.Add((ushort)encoded);
        }

        /// <summary>
        /// Encodes Masked Long for Amiga-compatible use.
        /// </summary>
        private static uint EncodeMaskedLong(uint dataBits)
        {
            dataBits &= MfmDataMask;
            uint clocks = (~(dataBits << 1) & ~(dataBits >> 1)) & MfmClockMask;
            return dataBits | clocks;
        }

        /// <summary>
        /// Decodes one odd/even encoded byte block from a standard AmigaDOS sector.
        /// </summary>
        private static void DecodeOddEvenBlock(IList<ushort> source, int wordOffset, byte[] destination)
        {
            int longCount = destination.Length / 4;
            int evenOffset = wordOffset + (longCount * 2);
            for (int index = 0; index < longCount; index++)
            {
                uint value = DecodeOddEvenLong(
                    ReadMfmLong(source, wordOffset + (index * 2)),
                    ReadMfmLong(source, evenOffset + (index * 2)));
                WriteBigEndianLong(destination, index * 4, value);
            }
        }

        /// <summary>
        /// Computes the AmigaDOS checksum over already encoded MFM words.
        /// </summary>
        private static uint ChecksumEncodedWords(IList<ushort> words, int wordOffset, int wordCount)
        {
            uint checksum = 0;
            for (int index = 0; index < wordCount; index += 2)
            {
                checksum ^= ReadMfmLong(words, wordOffset + index) & MfmDataMask;
            }

            return checksum & MfmDataMask;
        }

        /// <summary>
        /// Reads one big-endian MFM long from two 16-bit words.
        /// </summary>
        private static uint ReadMfmLong(IList<ushort> words, int wordOffset)
        {
            return ((uint)words[wordOffset] << 16) | words[wordOffset + 1];
        }

        /// <summary>
        /// Reads a big-endian 32-bit value from raw sector data.
        /// </summary>
        private static uint ReadBigEndianLong(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24) |
                ((uint)bytes[offset + 1] << 16) |
                ((uint)bytes[offset + 2] << 8) |
                bytes[offset + 3];
        }

        /// <summary>
        /// Writes a big-endian 32-bit value into raw sector data.
        /// </summary>
        private static void WriteBigEndianLong(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }
    }
}
