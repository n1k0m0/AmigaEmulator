// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AmigaEmulator.Core;
using SharpPixels;

namespace AmigaEmulator.Tests
{
    internal static class HqxTests
    {
        // Independent BGRA output hashes from FFmpeg 9.0.1:
        // ffmpeg -f rawvideo -pixel_format bgra -video_size WxH -i input.bgra
        //        -frames:v 1 -vf hqx=n=2/3/4 -f rawvideo -pix_fmt bgra output.bgra
        private static readonly string[] PatternHashes =
        {
            "54E6B768F11F23286A72FC1E2305393ECBBAB163B64B187F21EC27839BD73337",
            "793874BFD0602844F21C2A03F64E8C9901B3A24B4B65A11322B091BC16311004",
            "42B6AC5AB9EF02C283EA773B5BFBF4A401978538A818740F851F3349AA974183"
        };
        private static readonly string[] ColorHashes =
        {
            "B547B45A8E5A38B4A4E4F37D9E3BEA33262EFAFC305E2E579BCECAC92C7EF5FB",
            "9D21381FBDC8FF364570816D7C0F4CFD874929E3D3CAB5B9CE241033E07E70BB",
            "8E7A4BDE28555FFD05DFA0DB00B18EA9DD49DA5FF7C0079BA0DA4100A2A79E4A"
        };

        public static IEnumerable<ChipTestCase> Build()
        {
            for (int factor = 2; factor <= 4; factor++)
            {
                int n = factor;
                yield return new("Presentation", "HQ" + n + "x all 256 neighbor patterns match FFmpeg", "Independent FFmpeg hqx BGRA golden hashes", () => ComparePatterns(n));
                yield return new("Presentation", "HQ" + n + "x colored edges and YUV thresholds match FFmpeg", "Independent FFmpeg hqx BGRA golden hashes", () => CompareColors(n));
            }
            yield return new("Presentation", "Upscale selection exposes exactly the six requested algorithms", "Overlay and presentation mode correspondence", TestSelection);
            yield return new("Presentation", "Retired and current upscale settings preserve the complete user profile", "Real settings-store serializer; no user files accessed", TestSettings);
        }

        private static void ComparePatterns(int factor)
        {
            uint[] source = new uint[80 * 80]; Array.Fill(source, 0xFF202020u);
            for (int pattern = 0; pattern < 256; pattern++)
            {
                int x = pattern % 16 * 5 + 2, y = pattern / 16 * 5 + 2;
                for (int neighbor = 0; neighbor < 9; neighbor++)
                    if (neighbor != 4 && (pattern & (1 << (neighbor < 4 ? neighbor : neighbor - 1))) != 0)
                        source[(y + neighbor / 3 - 1) * 80 + x + neighbor % 3 - 1] = 0xFFE0E0E0;
            }
            Compare(source, 80, 80, factor, PatternHashes[factor - 2]);
        }

        private static void CompareColors(int factor)
        {
            uint[] palette = { 0xFF000000, 0xFFFFFFFF, 0xFF202020, 0xFF505050, 0xFF515151, 0xFF202A20, 0xFF203020, 0xFFDD4422,
                0xFF44DD22, 0xFF2244DD, 0xFFFF00FF, 0xFF00FFFF, 0xFF55AA99, 0xFF565D62, 0xFF123456, 0xFF654321 };
            uint[] source = new uint[19 * 17]; uint state = 0x12345678;
            for (int i = 0; i < source.Length; i++)
            {
                state = unchecked(state * 1664525 + 1013904223);
                source[i] = palette[(state >> 24) & 15];
            }
            Compare(source, 19, 17, factor, ColorHashes[factor - 2]);
        }

        private static void Compare(uint[] source, int width, int height, int factor, string expected)
        {
            var mode = factor switch { 2 => SharpPixelsGpuUpscaleMode.Hq2x, 3 => SharpPixelsGpuUpscaleMode.Hq3x, _ => SharpPixelsGpuUpscaleMode.Hq4x };
            var up = new PixelUpscaler();
            up.Render(source, width, height, new Rectangle(0, 0, width, height), mode);
            Check.Equal(expected, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(up.Pixels.AsSpan()))), "Entire output matches reference.");
            // The same fixture inside a hidden overscan border must have identical output.
            uint[] padded = new uint[(width + 2) * (height + 2)]; Array.Fill(padded, 0xFFFF00FFu);
            for (int y = 0; y < height; y++) Array.Copy(source, y * width, padded, (y + 1) * (width + 2) + 1, width);
            up.Render(padded, width + 2, height + 2, new Rectangle(1, 1, width, height), mode);
            Check.Equal(expected, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(up.Pixels.AsSpan()))), "HQx crop excludes hidden neighbors.");
        }

        private static void TestSelection()
        {
            string[] expected = { "NONE", "SCALE2X", "SCALE3X", "HQ2X", "HQ3X", "HQ4X" };
            Check.Equal(expected.Length, Enum.GetValues<AmigaUpscaleMode>().Length, "Saved modes.");
            Check.Equal(expected.Length, Enum.GetValues<SharpPixelsGpuUpscaleMode>().Length, "Renderer modes.");
            Check.Equal(expected.Length, AmigaOverlaySettingCatalog.UpscaleModes.Count, "Overlay options.");
            Type window = typeof(AmigaEmulatorSettings).Assembly.GetType("AmigaEmulator.AmigaWindow", true);
            Type videoModes = window.GetNestedType("VideoUpscaleMode", BindingFlags.NonPublic);
            Check.Equal(expected.Length, Enum.GetValues(videoModes).Length, "Menu cycle modes.");
            MethodInfo format = window.GetMethod("FormatVideoUpscale", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo map = window.GetMethod("ToSharpPixelsUpscale", BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = 0; i < expected.Length; i++)
            {
                Check.Equal((AmigaUpscaleMode)i, AmigaOverlaySettingCatalog.UpscaleModes[i].Value, "Option order.");
                Check.Equal(expected[i], AmigaOverlaySettingCatalog.UpscaleModes[i].Label, "Exact visible label.");
                object value = Enum.ToObject(videoModes, i);
                Check.Equal(expected[i], (string)format.Invoke(null, new[] { value }), "Actual menu label.");
                Check.Equal((SharpPixelsGpuUpscaleMode)i, (SharpPixelsGpuUpscaleMode)map.Invoke(null, new[] { value }), "Actual renderer mapping.");
            }
        }

        private static void TestSettings()
        {
            var options = (JsonSerializerOptions)typeof(AmigaEmulatorSettingsStore)
                .GetMethod("CreateJsonOptions", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            string[] tokens = { "\"None\"", "\"Scale2x\"", "\"Scale3x\"", "\"Hq2x\"", "\"Hq3x\"", "\"Hq4x\"", "\"Xbrz\"", "\"clean4\"",
                "6", "7", "\"unknown\"", "99", "null", "{\"future\":true}" };
            int[] modes = { 0, 1, 2, 3, 4, 5, 5, 5, 5, 5, 0, 0, 0, 0 };
            for (int i = 0; i < tokens.Length; i++)
            {
                string json = "{\"UpscaleMode\":" + tokens[i] + ",\"CustomKickstartPath\":\"purchased.rom\",\"HardDiskPath\":\"installed.hdf\",\"Df0Path\":\"game.adf\",\"Df3Path\":\"extra.adf\",\"BootFromHardDisk\":true,\"StretchToFullWidth\":true,\"RenderFps\":\"Native\"}";
                var settings = JsonSerializer.Deserialize<AmigaEmulatorSettings>(json, options);
                Check.Equal((AmigaUpscaleMode)modes[i], settings.UpscaleMode, "Compatible upscale value.");
                Check.Equal("purchased.rom", settings.CustomKickstartPath, "ROM preserved.");
                Check.Equal("installed.hdf", settings.HardDiskPath, "HDF preserved.");
                Check.Equal("game.adf", settings.Df0Path, "DF0 preserved.");
                Check.Equal("extra.adf", settings.Df3Path, "DF3 preserved.");
                Check.True(settings.BootFromHardDisk && settings.StretchToFullWidth, "Boot/display settings preserved.");
                using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(settings, options));
                Check.Equal(AmigaOverlaySettingCatalog.GetLabel(settings.UpscaleMode), serialized.RootElement.GetProperty("UpscaleMode").GetString(), "Canonical uppercase setting.");
            }
            foreach (var mode in Enum.GetValues<AmigaUpscaleMode>())
            {
                string token = JsonSerializer.Serialize(mode, options);
                Check.Equal(mode, JsonSerializer.Deserialize<AmigaUpscaleMode>(token, options), "Canonical value round trip.");
                Check.Equal(mode, JsonSerializer.Deserialize<AmigaUpscaleMode>(((int)mode).ToString(), options), "Legacy numeric value.");
            }
        }
    }
}
