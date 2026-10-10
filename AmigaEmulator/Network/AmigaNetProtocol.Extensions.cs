// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;

namespace AmigaEmulator.Network;

public static partial class AmigaNetProtocol
{
    // The native frame is sent before local filtering/scaling. Bottom overscan is
    // guest dependent; CRT aperture and scaling remain a local viewer preference.
    public static byte[] CreateBestVideoFramePayload(byte[] pixels, byte[] previous, int width, int height,
        long frameId, long previousFrameId, out bool usedDelta, int bottomCrop = 0)
    {
        byte[] data = CreateBestVideoFrameData(pixels, previous, width, height, frameId, previousFrameId, out usedDelta);
        if (data.Length == 0) return data;
        if (bottomCrop < 0 || bottomCrop >= height) throw new ArgumentOutOfRangeException(nameof(bottomCrop));
        byte[] payload = new byte[data.Length + 4];
        WriteInt32(payload, 0, bottomCrop);
        Buffer.BlockCopy(data, 0, payload, 4, data.Length);
        return payload;
    }

    public static AmigaNetVideoFrame ReadVideoFramePayload(byte[] payload, ref byte[] reference,
        ref long referenceFrameId, uint[] target)
    {
        if (payload == null || payload.Length < 24) return null;
        int crop = ReadInt32(payload, 0), height = ReadInt32(payload, 8);
        if (crop < 0 || crop >= height) return null;
        byte[] data = payload.AsSpan(4).ToArray();
        AmigaNetVideoFrame frame = ReadVideoFrameData(data, ref reference, ref referenceFrameId, target);
        if (frame != null) frame.BottomCrop = crop;
        return frame;
    }

    // Two independently predicted IMA ADPCM blocks preserve Paula's stereo channels.
    // The underlying mono block codec and raw fallback are identical to C64Net.
    public static byte[] CreateAudioChunkPayload(byte[] pcm, int count, int sampleRate)
    {
        if (pcm == null || count <= 0 || count > pcm.Length || count > 192000 || count % 4 != 0) return Array.Empty<byte>();
        byte[] left = new byte[count / 2], right = new byte[count / 2];
        for (int i = 0; i < count / 4; i++)
        {
            left[i * 2] = pcm[i * 4]; left[i * 2 + 1] = pcm[i * 4 + 1];
            right[i * 2] = pcm[i * 4 + 2]; right[i * 2 + 1] = pcm[i * 4 + 3];
        }
        left = CreateMonoAudioChunkPayload(left, left.Length, sampleRate);
        right = CreateMonoAudioChunkPayload(right, right.Length, sampleRate);
        byte[] payload = new byte[4 + left.Length + right.Length];
        WriteInt32(payload, 0, left.Length);
        Buffer.BlockCopy(left, 0, payload, 4, left.Length);
        Buffer.BlockCopy(right, 0, payload, 4 + left.Length, right.Length);
        return payload;
    }

    public static bool ReadAudioChunkPayload(byte[] payload, out int sampleRate, out byte[] pcm, out int count)
    {
        sampleRate = DefaultAudioSampleRate; pcm = Array.Empty<byte>(); count = 0;
        if (payload == null || payload.Length < 20) return false;
        int leftLength = ReadInt32(payload, 0);
        if (leftLength < 8 || leftLength > payload.Length - 12) return false;
        if (!ReadMonoAudioChunkPayload(payload.AsSpan(4, leftLength).ToArray(), out sampleRate, out byte[] left, out int lc) ||
            !ReadMonoAudioChunkPayload(payload.AsSpan(4 + leftLength).ToArray(), out int rightRate, out byte[] right, out int rc) ||
            sampleRate != rightRate || sampleRate != DefaultAudioSampleRate || lc != rc || lc % 2 != 0) return false;
        count = lc * 2; pcm = new byte[count];
        for (int i = 0; i < lc / 2; i++)
        {
            pcm[i * 4] = left[i * 2]; pcm[i * 4 + 1] = left[i * 2 + 1];
            pcm[i * 4 + 2] = right[i * 2]; pcm[i * 4 + 3] = right[i * 2 + 1];
        }
        return true;
    }

    public static byte[] CreateMouseInputPayload(int dx, int dy, byte buttons)
    {
        byte[] payload = new byte[9];
        WriteInt32(payload, 0, Math.Clamp(dx, -2048, 2048));
        WriteInt32(payload, 4, Math.Clamp(dy, -2048, 2048));
        payload[8] = (byte)(buttons & 0x70);
        return payload;
    }

    public static bool ReadMouseInputPayload(byte[] payload, out int dx, out int dy, out byte buttons)
    {
        dx = dy = 0; buttons = 0;
        if (payload == null || payload.Length != 9) return false;
        dx = ReadInt32(payload, 0); dy = ReadInt32(payload, 4); buttons = payload[8];
        return dx >= -2048 && dx <= 2048 && dy >= -2048 && dy <= 2048 && (buttons & ~0x70) == 0;
    }

    public static byte[] CreatePermissionPayload(AmigaNetJoystickPermission permission, bool keyboard, bool mouse)
        => new[] { (byte)permission, (byte)(keyboard ? 1 : 0), (byte)(mouse ? 1 : 0) };

    public static void ReadPermissionPayload(byte[] payload, out AmigaNetJoystickPermission permission, out bool keyboard, out bool mouse)
    {
        ReadPermissionPayload(payload, out permission, out keyboard);
        if (!Enum.IsDefined(permission)) throw new InvalidDataException("Invalid joystick permission.");
        mouse = payload != null && payload.Length > 2 && payload[2] != 0;
    }
}
