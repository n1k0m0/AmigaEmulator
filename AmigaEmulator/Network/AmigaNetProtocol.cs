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
using System.IO;
using System.IO.Compression;
using System.Text;
using SharpPixels;
namespace AmigaEmulator.Network
{
    public enum AmigaNetMessageType : ushort
    {
        ClientHello = 1,
        ServerWelcome = 2,
        ServerReject = 3,
        InputState = 4,
        VideoFrame = 5,
        AudioChunk = 6,
        ClientList = 7,
        PermissionUpdate = 8,
        Ping = 9,
        Pong = 10,
        Disconnect = 11,
        HostOverlayStatus = 12,
        KeyboardInput = 13, MouseInput = 14, InputConfiguration = 15
    }
    public enum AmigaNetClientRole : byte
    {
        Observer = 0,
        Player = 1
    }
    public enum AmigaNetJoystickPermission : byte
    {
        Observer = 0,
        Port1 = 1,
        Port2 = 2,
        Both = 3
    }
    public sealed class AmigaNetMessage
    {
        public AmigaNetMessageType Type { get; set; }
        public ushort Flags { get; set; }
        public uint Sequence { get; set; }
        public long Timestamp { get; set; }
        public byte[] Payload { get; set; }
        public int WireLength { get; set; }
    }
    public sealed class AmigaNetClientSnapshot
    {
        public int ClientId { get; set; }
        public string Name { get; set; }
        public string RemoteAddress { get; set; }
        public string RemoteEndpoint { get; set; }
        public AmigaNetClientRole Role { get; set; }
        public AmigaNetJoystickPermission Permission { get; set; }
        public bool KeyboardEnabled { get; set; }
        public bool MouseEnabled { get; set; }
        public string SessionFingerprint { get; set; } = "UNKNOWN";
        public byte JoystickState { get; set; }
        public int LatencyMilliseconds { get; set; }
        public bool Connected { get; set; }
    }
    public sealed class AmigaNetVideoFrame
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public long FrameId { get; set; }
        public uint[] Pixels { get; set; }
        public int BottomCrop { get; set; }
    }
    public static partial class AmigaNetProtocol
    {
        public const int Version = 1;
        public const int DefaultPort = 6464;
        public const int DefaultRelayPort = 6465;
        public const int DefaultAudioSampleRate = 48000;
        // AudioChunk flag; raw PCM/ADPCM remains independent of local filters.
        public const ushort AudioLedFilterEnabled = 1;
        public const int HeaderLength = 26;
        public const int MaxPayloadLength = 16 * 1024 * 1024;
        private const int VideoFormatArgb32 = 0;
        private const int VideoFormatPackedArgb32 = 1;
        private const int VideoFormatPackedArgb32Deflate = 2;
        private const int VideoFormatPackedArgb32Delta = 3;
        private const int VideoFormatPackedArgb32DeltaDeflate = 4;
        private const int VideoFormatPackedArgb32XorRleDelta = 5;
        private const int VideoFormatPackedArgb32XorRleDeltaDeflate = 6;
        private const int MinimumCompressionSavings = 32;
        private const int AudioFormatPcm16 = 0;
        private const int AudioFormatImaAdpcm4 = 1;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AMIGAR");
        private static readonly byte[] EmptyPayload = new byte[0];
        private static readonly int[] ImaAdpcmStepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17,
            19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
            50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
            130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
            337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
            876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
            2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358,
            5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
            15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        };
        private static readonly int[] ImaAdpcmIndexTable =
        {
            -1, -1, -1, -1, 2, 4, 6, 8,
            -1, -1, -1, -1, 2, 4, 6, 8
        };
        public static int WriteMessage(Stream stream, AmigaNetMessage message)
        {
            if (stream == null || message == null)
            {
                return 0;
            }
            byte[] payload = message.Payload ?? EmptyPayload;
            if (payload.Length > MaxPayloadLength)
            {
                throw new InvalidDataException("AmigaNet payload is too large.");
            }
            // TCP is a byte stream, so every protocol message starts with a small
            // fixed header that tells the receiver how many payload bytes to read next.
            byte[] header = new byte[HeaderLength];
            Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
            WriteInt32(header, 6, payload.Length);
            WriteUInt16(header, 10, (ushort)message.Type);
            WriteUInt16(header, 12, message.Flags);
            WriteUInt32(header, 14, message.Sequence);
            WriteInt64(header, 18, message.Timestamp);
            stream.Write(header, 0, header.Length);
            if (payload.Length > 0)
            {
                // Payloads are already fully serialized by the message-specific helpers.
                stream.Write(payload, 0, payload.Length);
            }
            message.WireLength = HeaderLength + payload.Length;
            return message.WireLength;
        }
        public static AmigaNetMessage ReadMessage(Stream stream)
        {
            if (stream == null)
            {
                return null;
            }
            byte[] header = new byte[HeaderLength];
            if (!ReadExact(stream, header, 0, header.Length))
            {
                // A short header means the peer disconnected cleanly or the socket died.
                return null;
            }
            if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("Invalid AMIGAR session header.");
            int payloadLength = ReadInt32(header, 6);
            if (payloadLength < 0 || payloadLength > MaxPayloadLength)
            {
                throw new InvalidDataException("Invalid AmigaNet payload length.");
            }
            byte[] payload = payloadLength == 0 ? EmptyPayload : new byte[payloadLength];
            if (payloadLength > 0 && !ReadExact(stream, payload, 0, payloadLength))
            {
                // The frame header was valid, but the peer closed before the body arrived.
                return null;
            }
            return new AmigaNetMessage
            {
                Type = (AmigaNetMessageType)ReadUInt16(header, 10),
                Flags = ReadUInt16(header, 12),
                Sequence = ReadUInt32(header, 14),
                Timestamp = ReadInt64(header, 18),
                Payload = payload,
                WireLength = HeaderLength + payloadLength
            };
        }
        public static byte[] CreateClientHelloPayload(string name, string password, AmigaNetClientRole role)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                // Version goes first so the host can reject incompatible clients before
                // interpreting later fields.
                writer.Write(Version);
                WriteString(writer, name);
                WriteString(writer, password);
                writer.Write((byte)role);
                return stream.ToArray();
            }
        }
        public static void ReadClientHelloPayload(byte[] payload, out int version, out string name, out string password, out AmigaNetClientRole role)
        {
            using (var stream = new MemoryStream(payload ?? EmptyPayload))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                version = reader.ReadInt32();
                name = ReadString(reader);
                password = ReadString(reader);
                role = (AmigaNetClientRole)reader.ReadByte();
            }
        }
        public static byte[] CreateServerWelcomePayload(int clientId, int width, int height, int audioSampleRate, AmigaNetClientRole role, AmigaNetJoystickPermission permission, bool keyboardEnabled, string status)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                // Dimensions are part of the handshake because the client disables its
                // local emulator and sizes the remote frame buffer from this metadata.
                writer.Write(clientId);
                writer.Write(width);
                writer.Write(height);
                writer.Write(audioSampleRate);
                writer.Write((byte)role);
                writer.Write((byte)permission);
                writer.Write(keyboardEnabled);
                WriteString(writer, status);
                return stream.ToArray();
            }
        }
        public static void ReadServerWelcomePayload(byte[] payload, out int clientId, out int width, out int height, out int audioSampleRate, out AmigaNetClientRole role, out AmigaNetJoystickPermission permission, out bool keyboardEnabled, out string status)
        {
            using (var stream = new MemoryStream(payload ?? EmptyPayload))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                clientId = reader.ReadInt32();
                width = reader.ReadInt32();
                height = reader.ReadInt32();
                audioSampleRate = reader.ReadInt32();
                role = (AmigaNetClientRole)reader.ReadByte();
                permission = (AmigaNetJoystickPermission)reader.ReadByte();
                keyboardEnabled = reader.ReadBoolean();
                status = ReadString(reader);
            }
        }
        public static byte[] CreateTextPayload(string text)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteString(writer, text);
                return stream.ToArray();
            }
        }
        public static string ReadTextPayload(byte[] payload)
        {
            using (var stream = new MemoryStream(payload ?? EmptyPayload))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                return ReadString(reader);
            }
        }
        public static byte[] CreateInputStatePayload(byte activeLowJoystickState)
        {
            // Bit 7 is not an Amiga joystick line. Keep it high so accidental values
            // cannot pull unrelated CIA input bits low when the host aggregates states.
            return new[] { (byte)(activeLowJoystickState | 0x80) };
        }
        public static byte ReadInputStatePayload(byte[] payload)
        {
            return payload != null && payload.Length > 0 ? (byte)(payload[0] | 0x80) : (byte)0xFF;
        }
        public static byte[] CreateKeyboardInputPayload(Key key, bool pressed)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write((int)key);
                writer.Write(pressed);
                return stream.ToArray();
            }
        }
        public static bool ReadKeyboardInputPayload(byte[] payload, out Key key, out bool pressed)
        {
            key = default(Key);
            pressed = false;
            if (payload == null || payload.Length < 5)
            {
                return false;
            }
            using (var stream = new MemoryStream(payload))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                key = (Key)reader.ReadInt32();
                pressed = reader.ReadBoolean();
                return Enum.IsDefined(key);
            }
        }
        public static byte[] CreatePackedVideoFrame(uint[] pixels, int width, int height)
        {
            if (pixels == null || !TryGetPixelCount(width, height, out int pixelCount) || pixels.Length < pixelCount) return EmptyPayload;
            byte[] packed = new byte[GetPackedLength(pixelCount)];
            Buffer.BlockCopy(pixels, 0, packed, 0, packed.Length);
            return packed;
        }
        public static bool PackedVideoFramesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }
            return true;
        }
        public static byte[] CreateVideoFramePayload(uint[] pixels, int width, int height, long frameId)
        {
            byte[] packed = CreatePackedVideoFrame(pixels, width, height);
            if (packed.Length == 0)
            {
                return EmptyPayload;
            }
            return CreateBestVideoFramePayload(packed, null, width, height, frameId, 0, out _);
        }
        private static byte[] CreateBestVideoFrameData(byte[] packedPixels, byte[] previousPackedPixels, int width, int height, long frameId, long previousFrameId, out bool usedDelta)
        {
            usedDelta = false;
            if (!IsValidPackedFrame(packedPixels, width, height))
            {
                return EmptyPayload;
            }
            byte[] fullPayload = CreateFullVideoFramePayload(packedPixels, width, height, frameId);
            if (!IsValidPackedFrame(previousPackedPixels, width, height) || previousFrameId <= 0)
            {
                return fullPayload;
            }
            byte[] bestPayload = fullPayload;
            bool bestUsesDelta = false;
            byte[] deltaPayload = CreateDeltaVideoFramePayload(packedPixels, previousPackedPixels, width, height, frameId, previousFrameId);
            if (deltaPayload.Length > 0 && deltaPayload.Length < bestPayload.Length)
            {
                bestPayload = deltaPayload;
                bestUsesDelta = true;
            }
            byte[] xorRlePayload = CreateXorRleDeltaVideoFramePayload(packedPixels, previousPackedPixels, width, height, frameId, previousFrameId);
            if (xorRlePayload.Length > 0 && xorRlePayload.Length < bestPayload.Length)
            {
                bestPayload = xorRlePayload;
                bestUsesDelta = true;
            }
            usedDelta = bestUsesDelta;
            return bestPayload;
        }
        public static AmigaNetVideoFrame ReadVideoFramePayload(byte[] payload)
        {
            byte[] referencePixels = null;
            long referenceFrameId = 0;
            return ReadVideoFramePayload(payload, ref referencePixels, ref referenceFrameId);
        }
        public static AmigaNetVideoFrame ReadVideoFramePayload(byte[] payload, ref byte[] referencePixels, ref long referenceFrameId)
        {
            return ReadVideoFramePayload(payload, ref referencePixels, ref referenceFrameId, null);
        }
        private static AmigaNetVideoFrame ReadVideoFrameData(byte[] payload, ref byte[] referencePixels, ref long referenceFrameId, uint[] pixelTarget)
        {
            if (payload == null || payload.Length < 20)
            {
                return null;
            }
            int width = ReadInt32(payload, 0);
            int height = ReadInt32(payload, 4);
            long frameId = ReadInt64(payload, 8);
            int formatOrByteCount = ReadInt32(payload, 16);
            if (!TryGetPixelCount(width, height, out int pixelCount))
            {
                return null;
            }
            byte[] packedPixels = null;
            uint[] pixels = null;
            if (payload.Length >= 24 && formatOrByteCount == VideoFormatPackedArgb32)
            {
                // Base full-frame format: lossless native ARGB32 pixels.
                int encodedLength = ReadInt32(payload, 20);
                if (encodedLength != GetPackedLength(pixelCount) || payload.Length < 24 + encodedLength)
                {
                    return null;
                }
                packedPixels = new byte[encodedLength];
                Buffer.BlockCopy(payload, 24, packedPixels, 0, encodedLength);
            }
            else if (payload.Length >= 28 && formatOrByteCount == VideoFormatPackedArgb32Deflate)
            {
                int encodedLength = ReadInt32(payload, 20);
                int compressedLength = ReadInt32(payload, 24);
                if (encodedLength != GetPackedLength(pixelCount) || compressedLength <= 0 || payload.Length < 28 + compressedLength)
                {
                    return null;
                }
                packedPixels = Inflate(payload, 28, compressedLength, encodedLength);
                if (packedPixels == null)
                {
                    return null;
                }
            }
            else if (payload.Length >= 36 && formatOrByteCount == VideoFormatPackedArgb32Delta)
            {
                packedPixels = ReadDeltaVideoFramePayload(payload, 36, ReadInt32(payload, 20), ReadInt64(payload, 24), ReadInt32(payload, 32), false, pixelCount, referencePixels, referenceFrameId);
            }
            else if (payload.Length >= 40 && formatOrByteCount == VideoFormatPackedArgb32DeltaDeflate)
            {
                packedPixels = ReadDeltaVideoFramePayload(payload, 40, ReadInt32(payload, 20), ReadInt64(payload, 24), ReadInt32(payload, 32), true, pixelCount, referencePixels, referenceFrameId);
            }
            else if (payload.Length >= 36 && formatOrByteCount == VideoFormatPackedArgb32XorRleDelta)
            {
                packedPixels = ReadXorRleDeltaVideoFramePayload(payload, 36, ReadInt32(payload, 20), ReadInt64(payload, 24), ReadInt32(payload, 32), false, pixelCount, referencePixels, referenceFrameId);
            }
            else if (payload.Length >= 40 && formatOrByteCount == VideoFormatPackedArgb32XorRleDeltaDeflate)
            {
                packedPixels = ReadXorRleDeltaVideoFramePayload(payload, 40, ReadInt32(payload, 20), ReadInt64(payload, 24), ReadInt32(payload, 32), true, pixelCount, referencePixels, referenceFrameId);
            }
            else
            {
                // Compatibility path for the original ARGB32 payload layout. Keeping it
                // lets test tools or older prototypes still decode full-color frames.
                int byteCount = formatOrByteCount;
                if (byteCount != pixelCount * 4 || payload.Length < 20 + byteCount)
                {
                    return null;
                }
                pixels = GetReusablePixelBuffer(pixelTarget, pixelCount);
                int offset = 20;
                for (int index = 0; index < pixelCount; index++)
                {
                    pixels[index] =
                        (uint)(payload[offset] |
                        (payload[offset + 1] << 8) |
                        (payload[offset + 2] << 16) |
                        (payload[offset + 3] << 24));
                    offset += 4;
                }
                packedPixels = CreatePackedVideoFrame(pixels, width, height);
            }
            if (packedPixels == null || !IsValidPackedFrame(packedPixels, width, height))
            {
                return null;
            }
            if (pixels == null)
            {
                pixels = DecodePackedFrame(packedPixels, pixelCount, pixelTarget);
                if (pixels == null)
                {
                    return null;
                }
            }
            referencePixels = packedPixels;
            referenceFrameId = frameId;
            return new AmigaNetVideoFrame
            {
                Width = width,
                Height = height,
                FrameId = frameId,
                Pixels = pixels
            };
        }
        private static byte[] CreateFullVideoFramePayload(byte[] packedPixels, int width, int height, long frameId)
        {
            byte[] rawPayload = CreateRawPackedPayload(packedPixels, width, height, frameId);
            byte[] compressed = Deflate(packedPixels, 0, packedPixels.Length);
            if (compressed == null)
            {
                return rawPayload;
            }
            byte[] compressedPayload = new byte[28 + compressed.Length];
            WriteCommonVideoHeader(compressedPayload, width, height, frameId, VideoFormatPackedArgb32Deflate);
            WriteInt32(compressedPayload, 20, packedPixels.Length);
            WriteInt32(compressedPayload, 24, compressed.Length);
            Buffer.BlockCopy(compressed, 0, compressedPayload, 28, compressed.Length);
            return compressedPayload.Length + MinimumCompressionSavings < rawPayload.Length
                ? compressedPayload
                : rawPayload;
        }
        private static byte[] CreateRawPackedPayload(byte[] packedPixels, int width, int height, long frameId)
        {
            byte[] payload = new byte[24 + packedPixels.Length];
            WriteCommonVideoHeader(payload, width, height, frameId, VideoFormatPackedArgb32);
            WriteInt32(payload, 20, packedPixels.Length);
            Buffer.BlockCopy(packedPixels, 0, payload, 24, packedPixels.Length);
            return payload;
        }
        private static byte[] CreateDeltaVideoFramePayload(byte[] packedPixels, byte[] previousPackedPixels, int width, int height, long frameId, long previousFrameId)
        {
            if (packedPixels == null || previousPackedPixels == null || packedPixels.Length != previousPackedPixels.Length)
            {
                return EmptyPayload;
            }
            byte[] deltaData = CreateDeltaRunData(packedPixels, previousPackedPixels);
            byte[] rawPayload = new byte[36 + deltaData.Length];
            WriteCommonVideoHeader(rawPayload, width, height, frameId, VideoFormatPackedArgb32Delta);
            WriteInt32(rawPayload, 20, packedPixels.Length);
            WriteInt64(rawPayload, 24, previousFrameId);
            WriteInt32(rawPayload, 32, deltaData.Length);
            Buffer.BlockCopy(deltaData, 0, rawPayload, 36, deltaData.Length);
            byte[] compressed = Deflate(deltaData, 0, deltaData.Length);
            if (compressed == null)
            {
                return rawPayload;
            }
            byte[] compressedPayload = new byte[40 + compressed.Length];
            WriteCommonVideoHeader(compressedPayload, width, height, frameId, VideoFormatPackedArgb32DeltaDeflate);
            WriteInt32(compressedPayload, 20, packedPixels.Length);
            WriteInt64(compressedPayload, 24, previousFrameId);
            WriteInt32(compressedPayload, 32, deltaData.Length);
            WriteInt32(compressedPayload, 36, compressed.Length);
            Buffer.BlockCopy(compressed, 0, compressedPayload, 40, compressed.Length);
            return compressedPayload.Length + MinimumCompressionSavings < rawPayload.Length
                ? compressedPayload
                : rawPayload;
        }
        private static byte[] CreateXorRleDeltaVideoFramePayload(byte[] packedPixels, byte[] previousPackedPixels, int width, int height, long frameId, long previousFrameId)
        {
            if (packedPixels == null || previousPackedPixels == null || packedPixels.Length != previousPackedPixels.Length)
            {
                return EmptyPayload;
            }
            byte[] deltaData = CreateXorRleDeltaData(packedPixels, previousPackedPixels);
            byte[] rawPayload = new byte[36 + deltaData.Length];
            WriteCommonVideoHeader(rawPayload, width, height, frameId, VideoFormatPackedArgb32XorRleDelta);
            WriteInt32(rawPayload, 20, packedPixels.Length);
            WriteInt64(rawPayload, 24, previousFrameId);
            WriteInt32(rawPayload, 32, deltaData.Length);
            Buffer.BlockCopy(deltaData, 0, rawPayload, 36, deltaData.Length);
            byte[] compressed = Deflate(deltaData, 0, deltaData.Length);
            if (compressed == null)
            {
                return rawPayload;
            }
            byte[] compressedPayload = new byte[40 + compressed.Length];
            WriteCommonVideoHeader(compressedPayload, width, height, frameId, VideoFormatPackedArgb32XorRleDeltaDeflate);
            WriteInt32(compressedPayload, 20, packedPixels.Length);
            WriteInt64(compressedPayload, 24, previousFrameId);
            WriteInt32(compressedPayload, 32, deltaData.Length);
            WriteInt32(compressedPayload, 36, compressed.Length);
            Buffer.BlockCopy(compressed, 0, compressedPayload, 40, compressed.Length);
            return compressedPayload.Length + MinimumCompressionSavings < rawPayload.Length
                ? compressedPayload
                : rawPayload;
        }
        private static byte[] CreateDeltaRunData(byte[] current, byte[] previous)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                int index = 0;
                while (index < current.Length)
                {
                    if (current[index] == previous[index])
                    {
                        index++;
                        continue;
                    }
                    int start = index;
                    index++;
                    while (index < current.Length && current[index] != previous[index])
                    {
                        index++;
                    }
                    int length = index - start;
                    writer.Write(start);
                    writer.Write(length);
                    writer.Write(current, start, length);
                }
                return stream.ToArray();
            }
        }
        private static byte[] CreateXorRleDeltaData(byte[] current, byte[] previous)
        {
            using (var stream = new MemoryStream())
            {
                byte[] literalScratch = new byte[128];
                int index = 0;
                while (index < current.Length)
                {
                    byte xor = (byte)(current[index] ^ previous[index]);
                    if (xor == 0)
                    {
                        int length = 1;
                        while (index + length < current.Length && length < 128 && (current[index + length] ^ previous[index + length]) == 0)
                        {
                            length++;
                        }
                        stream.WriteByte((byte)(length - 1));
                        index += length;
                        continue;
                    }
                    int literalLength = 0;
                    while (index < current.Length && literalLength < literalScratch.Length)
                    {
                        xor = (byte)(current[index] ^ previous[index]);
                        if (xor == 0)
                        {
                            break;
                        }
                        literalScratch[literalLength++] = xor;
                        index++;
                    }
                    stream.WriteByte((byte)(0x80 | (literalLength - 1)));
                    stream.Write(literalScratch, 0, literalLength);
                }
                return stream.ToArray();
            }
        }
        private static byte[] ReadDeltaVideoFramePayload(byte[] payload, int dataOffset, int packedLength, long baseFrameId, int deltaLength, bool compressed, int pixelCount, byte[] referencePixels, long referenceFrameId)
        {
            if (packedLength != GetPackedLength(pixelCount) ||
                deltaLength < 0 || deltaLength > MaxPayloadLength ||
                referencePixels == null ||
                referencePixels.Length != packedLength ||
                referenceFrameId != baseFrameId)
            {
                return null;
            }
            byte[] deltaData;
            if (compressed)
            {
                int compressedLength = ReadInt32(payload, 36);
                if (compressedLength <= 0 || payload.Length < dataOffset + compressedLength)
                {
                    return null;
                }
                deltaData = Inflate(payload, dataOffset, compressedLength, deltaLength);
            }
            else
            {
                if (payload.Length < dataOffset + deltaLength)
                {
                    return null;
                }
                deltaData = new byte[deltaLength];
                Buffer.BlockCopy(payload, dataOffset, deltaData, 0, deltaLength);
            }
            if (deltaData == null)
            {
                return null;
            }
            byte[] packedPixels = new byte[packedLength];
            Buffer.BlockCopy(referencePixels, 0, packedPixels, 0, packedLength);
            return ApplyDeltaRunData(packedPixels, deltaData) ? packedPixels : null;
        }
        private static byte[] ReadXorRleDeltaVideoFramePayload(byte[] payload, int dataOffset, int packedLength, long baseFrameId, int deltaLength, bool compressed, int pixelCount, byte[] referencePixels, long referenceFrameId)
        {
            if (packedLength != GetPackedLength(pixelCount) ||
                deltaLength < 0 || deltaLength > MaxPayloadLength ||
                referencePixels == null ||
                referencePixels.Length != packedLength ||
                referenceFrameId != baseFrameId)
            {
                return null;
            }
            byte[] deltaData;
            if (compressed)
            {
                int compressedLength = ReadInt32(payload, 36);
                if (compressedLength <= 0 || payload.Length < dataOffset + compressedLength)
                {
                    return null;
                }
                deltaData = Inflate(payload, dataOffset, compressedLength, deltaLength);
            }
            else
            {
                if (payload.Length < dataOffset + deltaLength)
                {
                    return null;
                }
                deltaData = new byte[deltaLength];
                Buffer.BlockCopy(payload, dataOffset, deltaData, 0, deltaLength);
            }
            if (deltaData == null)
            {
                return null;
            }
            byte[] packedPixels = new byte[packedLength];
            Buffer.BlockCopy(referencePixels, 0, packedPixels, 0, packedLength);
            return ApplyXorRleDeltaData(packedPixels, deltaData) ? packedPixels : null;
        }
        private static bool ApplyDeltaRunData(byte[] target, byte[] deltaData)
        {
            int offset = 0;
            while (offset < deltaData.Length)
            {
                if (deltaData.Length - offset < 8)
                {
                    return false;
                }
                int targetOffset = ReadInt32(deltaData, offset);
                int length = ReadInt32(deltaData, offset + 4);
                offset += 8;
                if (targetOffset < 0 || length < 0 || targetOffset > target.Length - length || deltaData.Length - offset < length)
                {
                    return false;
                }
                Buffer.BlockCopy(deltaData, offset, target, targetOffset, length);
                offset += length;
            }
            return true;
        }
        private static bool ApplyXorRleDeltaData(byte[] target, byte[] deltaData)
        {
            int sourceOffset = 0;
            int targetOffset = 0;
            while (sourceOffset < deltaData.Length)
            {
                int header = deltaData[sourceOffset++];
                int length = (header & 0x7F) + 1;
                if (targetOffset > target.Length - length)
                {
                    return false;
                }
                if ((header & 0x80) == 0)
                {
                    targetOffset += length;
                    continue;
                }
                if (deltaData.Length - sourceOffset < length)
                {
                    return false;
                }
                for (int index = 0; index < length; index++)
                {
                    target[targetOffset++] ^= deltaData[sourceOffset++];
                }
            }
            return targetOffset == target.Length;
        }
        private static uint[] DecodePackedFrame(byte[] packedPixels, int pixelCount)
        {
            return DecodePackedFrame(packedPixels, pixelCount, null);
        }
        private static uint[] DecodePackedFrame(byte[] packedPixels, int pixelCount, uint[] pixelTarget)
        {
            if (packedPixels == null || packedPixels.Length != GetPackedLength(pixelCount)) return null;
            uint[] pixels = GetReusablePixelBuffer(pixelTarget, pixelCount);
            Buffer.BlockCopy(packedPixels, 0, pixels, 0, packedPixels.Length);
            return pixels;
        }
        private static uint[] GetReusablePixelBuffer(uint[] pixelTarget, int pixelCount)
        {
            return pixelTarget != null && pixelTarget.Length >= pixelCount
                ? pixelTarget
                : new uint[pixelCount];
        }
        private static void WriteCommonVideoHeader(byte[] payload, int width, int height, long frameId, int format)
        {
            WriteInt32(payload, 0, width);
            WriteInt32(payload, 4, height);
            WriteInt64(payload, 8, frameId);
            WriteInt32(payload, 16, format);
        }
        private static bool TryGetPixelCount(int width, int height, out int pixelCount)
        {
            pixelCount = 0;
            if (width <= 0 || height <= 0 || width > int.MaxValue / height)
            {
                return false;
            }
            pixelCount = width * height;
            return pixelCount > 0 && pixelCount <= (MaxPayloadLength - 128) / sizeof(uint);
        }
        private static int GetPackedLength(int pixelCount)
        {
            return checked(pixelCount * sizeof(uint));
        }
        private static bool IsValidPackedFrame(byte[] packedPixels, int width, int height)
        {
            return TryGetPixelCount(width, height, out int pixelCount) &&
                packedPixels != null &&
                packedPixels.Length == GetPackedLength(pixelCount);
        }
        private static byte[] Deflate(byte[] buffer, int offset, int count)
        {
            if (buffer == null || offset < 0 || count < 0 || offset > buffer.Length - count)
            {
                return null;
            }
            try
            {
                using (var output = new MemoryStream())
                {
                    using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                    {
                        if (count > 0)
                        {
                            deflate.Write(buffer, offset, count);
                        }
                    }
                    return output.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }
        private static byte[] Inflate(byte[] buffer, int offset, int count, int expectedLength)
        {
            if (buffer == null || offset < 0 || count <= 0 || expectedLength < 0 || expectedLength > MaxPayloadLength || offset > buffer.Length - count)
            {
                return null;
            }
            try
            {
                byte[] output = new byte[expectedLength];
                using (var input = new MemoryStream(buffer, offset, count))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                {
                    int totalRead = 0;
                    while (totalRead < expectedLength)
                    {
                        int read = deflate.Read(output, totalRead, expectedLength - totalRead);
                        if (read <= 0)
                        {
                            break;
                        }
                        totalRead += read;
                    }
                    return totalRead == expectedLength ? output : null;
                }
            }
            catch
            {
                return null;
            }
        }


        private static byte[] CreateMonoAudioChunkPayload(byte[] buffer, int count, int sampleRate)
        {
            if (buffer == null || count <= 0)
            {
                return EmptyPayload;
            }
            count = Math.Min(count, buffer.Length);
            count -= count % 2;
            if (count <= 0)
            {
                return EmptyPayload;
            }
            byte[] rawPayload = CreateRawAudioChunkPayload(buffer, count, sampleRate);
            byte[] adpcmBytes = EncodeImaAdpcmBlock(buffer, count);
            if (adpcmBytes == null || adpcmBytes.Length == 0)
            {
                return rawPayload;
            }
            byte[] adpcmPayload = new byte[16 + adpcmBytes.Length];
            // Store the sample rate with every chunk so clients can validate that
            // their audio output still matches the host stream.
            WriteInt32(adpcmPayload, 0, sampleRate);
            WriteInt32(adpcmPayload, 4, AudioFormatImaAdpcm4);
            WriteInt32(adpcmPayload, 8, count);
            WriteInt32(adpcmPayload, 12, adpcmBytes.Length);
            Buffer.BlockCopy(adpcmBytes, 0, adpcmPayload, 16, adpcmBytes.Length);
            return adpcmPayload.Length + MinimumCompressionSavings < rawPayload.Length
                ? adpcmPayload
                : rawPayload;
        }
        private static bool ReadMonoAudioChunkPayload(byte[] payload, out int sampleRate, out byte[] buffer, out int count)
        {
            sampleRate = DefaultAudioSampleRate;
            buffer = EmptyPayload;
            count = 0;
            if (payload == null || payload.Length < 8)
            {
                return false;
            }
            sampleRate = ReadInt32(payload, 0);
            int formatOrCount = ReadInt32(payload, 4);
            if (payload.Length >= 16 && (formatOrCount == AudioFormatPcm16 || formatOrCount == AudioFormatImaAdpcm4))
            {
                int decodedCount = ReadInt32(payload, 8);
                int encodedCount = ReadInt32(payload, 12);
                if (sampleRate <= 0 || decodedCount <= 0 || decodedCount > 192000 || encodedCount <= 0 || encodedCount > payload.Length - 16)
                {
                    count = 0;
                    return false;
                }
                if (formatOrCount == AudioFormatPcm16)
                {
                    if (decodedCount != encodedCount)
                    {
                        count = 0;
                        return false;
                    }
                    buffer = new byte[decodedCount];
                    Buffer.BlockCopy(payload, 16, buffer, 0, decodedCount);
                    count = decodedCount;
                    return true;
                }
                buffer = DecodeImaAdpcmBlock(payload, 16, encodedCount, decodedCount);
                if (buffer == null)
                {
                    buffer = EmptyPayload;
                    count = 0;
                    return false;
                }
                count = buffer.Length;
                return true;
            }
            count = formatOrCount;
            if (sampleRate <= 0 || count <= 0 || count > 192000 || count > payload.Length - 8)
            {
                // Invalid chunks are ignored instead of tearing down the whole session;
                // video and control traffic can keep going.
                count = 0;
                return false;
            }
            buffer = new byte[count];
            Buffer.BlockCopy(payload, 8, buffer, 0, count);
            return true;
        }
        private static byte[] CreateRawAudioChunkPayload(byte[] buffer, int count, int sampleRate)
        {
            byte[] payload = new byte[8 + count];
            WriteInt32(payload, 0, sampleRate);
            WriteInt32(payload, 4, count);
            Buffer.BlockCopy(buffer, 0, payload, 8, count);
            return payload;
        }
        private static byte[] EncodeImaAdpcmBlock(byte[] buffer, int count)
        {
            if (buffer == null || count <= 0 || (count & 1) != 0 || buffer.Length < count)
            {
                return null;
            }
            int sampleCount = count / 2;
            int predictor = ReadInt16(buffer, 0);
            int stepIndex = EstimateImaAdpcmInitialIndex(buffer, count);
            int nibbleCount = Math.Max(0, sampleCount - 1);
            byte[] encoded = new byte[4 + ((nibbleCount + 1) / 2)];
            WriteInt16(encoded, 0, (short)predictor);
            encoded[2] = (byte)stepIndex;
            encoded[3] = 0;
            for (int sampleIndex = 1; sampleIndex < sampleCount; sampleIndex++)
            {
                int nibbleIndex = sampleIndex - 1;
                int sample = ReadInt16(buffer, sampleIndex * 2);
                int nibble = EncodeImaAdpcmNibble(sample, ref predictor, ref stepIndex);
                int packedOffset = 4 + (nibbleIndex / 2);
                if ((nibbleIndex & 1) == 0)
                {
                    encoded[packedOffset] = (byte)(nibble & 0x0F);
                }
                else
                {
                    encoded[packedOffset] |= (byte)((nibble & 0x0F) << 4);
                }
            }
            return encoded;
        }
        private static byte[] DecodeImaAdpcmBlock(byte[] buffer, int offset, int count, int decodedCount)
        {
            if (buffer == null || offset < 0 || count < 4 || decodedCount <= 0 || (decodedCount & 1) != 0 || offset > buffer.Length - count)
            {
                return null;
            }
            int sampleCount = decodedCount / 2;
            int expectedEncodedCount = 4 + (Math.Max(0, sampleCount - 1) + 1) / 2;
            if (count != expectedEncodedCount)
            {
                return null;
            }
            int predictor = ReadInt16(buffer, offset);
            int stepIndex = buffer[offset + 2];
            if (stepIndex < 0 || stepIndex >= ImaAdpcmStepTable.Length)
            {
                return null;
            }
            byte[] decoded = new byte[decodedCount];
            WriteInt16(decoded, 0, (short)predictor);
            for (int sampleIndex = 1; sampleIndex < sampleCount; sampleIndex++)
            {
                int nibbleIndex = sampleIndex - 1;
                int packed = buffer[offset + 4 + (nibbleIndex / 2)];
                int nibble = (nibbleIndex & 1) == 0
                    ? packed & 0x0F
                    : (packed >> 4) & 0x0F;
                DecodeImaAdpcmNibble(nibble, ref predictor, ref stepIndex);
                WriteInt16(decoded, sampleIndex * 2, (short)predictor);
            }
            return decoded;
        }
        private static int EstimateImaAdpcmInitialIndex(byte[] buffer, int count)
        {
            if (count < 4)
            {
                return 0;
            }
            int first = ReadInt16(buffer, 0);
            int second = ReadInt16(buffer, 2);
            int difference = Math.Abs(second - first);
            int index = 0;
            while (index + 1 < ImaAdpcmStepTable.Length && ImaAdpcmStepTable[index + 1] <= Math.Max(7, difference))
            {
                index++;
            }
            return index;
        }
        private static int EncodeImaAdpcmNibble(int sample, ref int predictor, ref int stepIndex)
        {
            int step = ImaAdpcmStepTable[stepIndex];
            int difference = sample - predictor;
            int nibble = 0;
            if (difference < 0)
            {
                nibble = 8;
                difference = -difference;
            }
            int remaining = difference;
            if (remaining >= step)
            {
                nibble |= 4;
                remaining -= step;
            }
            if (remaining >= step / 2)
            {
                nibble |= 2;
                remaining -= step / 2;
            }
            if (remaining >= step / 4)
            {
                nibble |= 1;
            }
            DecodeImaAdpcmNibble(nibble, ref predictor, ref stepIndex);
            return nibble;
        }
        private static void DecodeImaAdpcmNibble(int nibble, ref int predictor, ref int stepIndex)
        {
            int step = ImaAdpcmStepTable[stepIndex];
            int difference = step >> 3;
            if ((nibble & 4) != 0)
            {
                difference += step;
            }
            if ((nibble & 2) != 0)
            {
                difference += step >> 1;
            }
            if ((nibble & 1) != 0)
            {
                difference += step >> 2;
            }
            predictor = (nibble & 8) != 0
                ? ClampToInt16(predictor - difference)
                : ClampToInt16(predictor + difference);
            stepIndex += ImaAdpcmIndexTable[nibble & 0x0F];
            if (stepIndex < 0)
            {
                stepIndex = 0;
            }
            else if (stepIndex >= ImaAdpcmStepTable.Length)
            {
                stepIndex = ImaAdpcmStepTable.Length - 1;
            }
        }
        public static byte[] CreateClientListPayload(IList<AmigaNetClientSnapshot> clients)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                int count = clients == null ? 0 : clients.Count;
                writer.Write(count);
                for (int index = 0; index < count; index++)
                {
                    AmigaNetClientSnapshot client = clients[index];
                    // Keep this structure intentionally flat. It is cheap to parse and
                    // mirrors exactly what the overlay needs to display.
                    writer.Write(client.ClientId);
                    WriteString(writer, client.Name);
                    WriteString(writer, client.RemoteAddress);
                    WriteString(writer, client.RemoteEndpoint);
                    writer.Write((byte)client.Role);
                    writer.Write((byte)client.Permission);
                    writer.Write(client.KeyboardEnabled);
                    writer.Write(client.MouseEnabled);
                    WriteString(writer, client.SessionFingerprint);
                    writer.Write(client.JoystickState);
                    writer.Write(client.LatencyMilliseconds);
                    writer.Write(client.Connected);
                }
                return stream.ToArray();
            }
        }
        public static List<AmigaNetClientSnapshot> ReadClientListPayload(byte[] payload)
        {
            var clients = new List<AmigaNetClientSnapshot>();
            using (var stream = new MemoryStream(payload ?? EmptyPayload))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > 64) throw new InvalidDataException("Invalid client count.");
                for (int index = 0; index < count; index++)
                {
                    clients.Add(new AmigaNetClientSnapshot
                    {
                        ClientId = reader.ReadInt32(),
                        Name = ReadString(reader),
                        RemoteAddress = ReadString(reader),
                        RemoteEndpoint = ReadString(reader),
                        Role = (AmigaNetClientRole)reader.ReadByte(),
                        Permission = (AmigaNetJoystickPermission)reader.ReadByte(),
                        KeyboardEnabled = reader.ReadBoolean(),
                        MouseEnabled = reader.ReadBoolean(),
                        SessionFingerprint = ReadString(reader),
                        JoystickState = reader.ReadByte(),
                        LatencyMilliseconds = reader.ReadInt32(),
                        Connected = reader.ReadBoolean()
                    });
                }
            }
            return clients;
        }
        public static byte[] CreatePermissionPayload(AmigaNetJoystickPermission permission, bool keyboardEnabled)
        {
            return new[] { (byte)permission, (byte)(keyboardEnabled ? 1 : 0) };
        }
        public static void ReadPermissionPayload(byte[] payload, out AmigaNetJoystickPermission permission, out bool keyboardEnabled)
        {
            permission = payload != null && payload.Length > 0
                ? (AmigaNetJoystickPermission)payload[0]
                : AmigaNetJoystickPermission.Observer;
            keyboardEnabled = payload != null && payload.Length > 1 && payload[1] != 0;
        }
        private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                {
                    return false;
                }
                // Stream.Read can return partial data even when the connection is
                // healthy, so keep going until this frame fragment is complete.
                offset += read;
                count -= read;
            }
            return true;
        }
        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 65536)
            {
                throw new InvalidDataException("Invalid AmigaNet string length.");
            }
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException("Truncated AmigaNet string.");
            return Encoding.UTF8.GetString(bytes);
        }
        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }
        private static void WriteInt16(byte[] buffer, int offset, short value)
        {
            WriteUInt16(buffer, offset, unchecked((ushort)value));
        }
        private static short ReadInt16(byte[] buffer, int offset)
        {
            return unchecked((short)ReadUInt16(buffer, offset));
        }
        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            WriteUInt32(buffer, offset, unchecked((uint)value));
        }
        private static int ReadInt32(byte[] buffer, int offset)
        {
            return unchecked((int)ReadUInt32(buffer, offset));
        }
        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return
                (uint)(buffer[offset] |
                (buffer[offset + 1] << 8) |
                (buffer[offset + 2] << 16) |
                (buffer[offset + 3] << 24));
        }
        private static void WriteInt64(byte[] buffer, int offset, long value)
        {
            ulong unsigned = unchecked((ulong)value);
            for (int index = 0; index < 8; index++)
            {
                buffer[offset + index] = (byte)((unsigned >> (index * 8)) & 0xFF);
            }
        }
        private static long ReadInt64(byte[] buffer, int offset)
        {
            ulong value = 0;
            for (int index = 0; index < 8; index++)
            {
                value |= ((ulong)buffer[offset + index]) << (index * 8);
            }
            return unchecked((long)value);
        }
        private static int ClampToInt16(int value)
        {
            if (value < short.MinValue)
            {
                return short.MinValue;
            }
            if (value > short.MaxValue)
            {
                return short.MaxValue;
            }
            return value;
        }
    }
}
