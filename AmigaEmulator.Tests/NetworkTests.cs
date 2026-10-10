// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using AmigaEmulator.Core;
using AmigaEmulator.Network;
using SharpPixels;

namespace AmigaEmulator.Tests;

internal static class NetworkTests
{
    public static IEnumerable<ChipTestCase> Build()
    {
        yield return Test("AMIGAR wire header uses six bytes and exact field offsets", Header);
        yield return Test("Full and delta frames retain every ARGB32 color and crop", Frames);
        yield return Test("Malformed frame and audio allocation lengths are bounded", Bounds);
        yield return Test("Stereo IMA ADPCM retains channel separation and duration", Stereo);
        yield return Test("AES GCM rejects tampering, replay and exhausted nonces", Encryption);
        yield return Test("LAN TLS transports cached frames, stereo and independent input rights", () => Session(false));
        yield return Test("Python relay transports frames and input through TLS and ECDH AES GCM", () => Session(true));
        yield return Test("LAN rejects wrong password and changed certificate pin", Reject);
        yield return Test("Network settings round trip preserves hardware and media", Settings);
    }

    private static ChipTestCase Test(string name, Action action) => new("Network", name, "C64 transport port; real isolated TLS/relay peers and independent wire expectations", action);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Wait(Func<bool> ready, string message)
    {
        Require(SpinWait.SpinUntil(ready, TimeSpan.FromSeconds(8)), message);
    }

    private static void Header()
    {
        using var stream = new MemoryStream();
        AmigaNetProtocol.WriteMessage(stream, new() { Type = AmigaNetMessageType.MouseInput, Flags = 0x1234,
            Sequence = 0x98765432, Timestamp = 0x0102030405060708, Payload = new byte[] { 42 } });
        byte[] wire = stream.ToArray();
        Require(Encoding.ASCII.GetString(wire, 0, 6) == "AMIGAR" && wire.Length == 27, "Six byte session magic/header.");
        Require(BitConverter.ToInt32(wire, 6) == 1 && BitConverter.ToUInt16(wire, 10) == 14 && BitConverter.ToUInt16(wire, 12) == 0x1234, "Length/type/flags offsets.");
        Require(BitConverter.ToUInt32(wire, 14) == 0x98765432 && BitConverter.ToInt64(wire, 18) == 0x0102030405060708, "Sequence/time offsets.");
        stream.Position = 0; var decoded = AmigaNetProtocol.ReadMessage(stream);
        Require(decoded.Payload.Single() == 42, "Wire payload round trip.");
        wire[0] = 0; ExpectFailure(() => AmigaNetProtocol.ReadMessage(new MemoryStream(wire)));
        Require(AmigaNetProtocol.ReadMessage(new MemoryStream()) == null, "Clean EOF ends a session.");
        byte[] validWire = stream.ToArray();
        ExpectEndOfStream(() => AmigaNetProtocol.ReadMessage(new MemoryStream(validWire[..10])));
        ExpectEndOfStream(() => AmigaNetProtocol.ReadMessage(new MemoryStream(validWire[..^1])));
    }

    private static void Frames()
    {
        const int width = 127, height = 81;
        var random = new Random(812); uint[] pixels = Enumerable.Range(0, width * height).Select(_ => 0xFF000000u | (uint)random.Next(0x1000000)).ToArray();
        byte[] packed = AmigaNetProtocol.CreatePackedVideoFrame(pixels, width, height);
        byte[] payload = AmigaNetProtocol.CreateBestVideoFramePayload(packed, null, width, height, 1, 0, out bool delta, 7);
        byte[] reference = null; long frameId = 0;
        var frame = AmigaNetProtocol.ReadVideoFramePayload(payload, ref reference, ref frameId);
        Require(!delta && frame.Pixels.SequenceEqual(pixels) && frame.BottomCrop == 7, "Full native HAM/AGA colors.");
        uint[] previousPixels = (uint[])pixels.Clone(); pixels[450] = 0xFFAA5501;
        byte[] changed = AmigaNetProtocol.CreatePackedVideoFrame(pixels, width, height);
        payload = AmigaNetProtocol.CreateBestVideoFramePayload(changed, packed, width, height, 2, 1, out delta, 0);
        Require(delta && payload.Length < packed.Length / 10, "Sparse change chooses compact delta.");
        frame = AmigaNetProtocol.ReadVideoFramePayload(payload, ref reference, ref frameId);
        Require(frame.Pixels.SequenceEqual(pixels) && frame.FrameId == 2 && frame.BottomCrop == 0, "Delta + changed aperture.");
        Require(previousPixels[450] != frame.Pixels[450], "Changed pixel transported.");
        byte[] missingReference = null; long missingId = 0;
        Require(AmigaNetProtocol.ReadVideoFramePayload(payload, ref missingReference, ref missingId) == null, "Missing base rejects delta.");
        payload = AmigaNetProtocol.CreateBestVideoFramePayload(changed, changed, width, height, 3, 2, out delta, 9);
        frame = AmigaNetProtocol.ReadVideoFramePayload(payload, ref reference, ref frameId);
        Require(frame != null && frame.BottomCrop == 9 && frame.Pixels.SequenceEqual(pixels), "Crop changes without pixel changes.");
    }

    private static void Bounds()
    {
        Require(AmigaNetProtocol.CreatePackedVideoFrame(new uint[1], int.MaxValue, 2).Length == 0, "Oversized dimensions.");
        byte[] payload = new byte[32]; BitConverter.GetBytes(1).CopyTo(payload, 4); BitConverter.GetBytes(int.MaxValue).CopyTo(payload, 8);
        Require(AmigaNetProtocol.ReadVideoFramePayload(payload) == null, "Huge frame rejected before allocation.");
        byte[] mono = new byte[20]; BitConverter.GetBytes(48000).CopyTo(mono, 0); BitConverter.GetBytes(1).CopyTo(mono, 4);
        BitConverter.GetBytes(int.MaxValue).CopyTo(mono, 8); BitConverter.GetBytes(4).CopyTo(mono, 12);
        byte[] stereo = new byte[44]; BitConverter.GetBytes(20).CopyTo(stereo, 0); mono.CopyTo(stereo, 4); mono.CopyTo(stereo, 24);
        Require(!AmigaNetProtocol.ReadAudioChunkPayload(stereo, out _, out _, out _), "Huge decoded sound rejected.");
        Require(!AmigaNetProtocol.ReadMouseInputPayload(new byte[10], out _, out _, out _), "Mouse framing.");
        Require(!AmigaNetProtocol.ReadKeyboardInputPayload(new byte[] { 255, 255, 255, 127, 1 }, out _, out _), "Invalid key enum.");
    }

    private static byte[] Audio()
    {
        byte[] pcm = new byte[960 * 4];
        for (int i = 0; i < 960; i++)
        {
            short l = (short)(Math.Sin(i * 0.08) * 12000), r = (short)(Math.Cos(i * 0.04) * 6000);
            BitConverter.GetBytes(l).CopyTo(pcm, i * 4); BitConverter.GetBytes(r).CopyTo(pcm, i * 4 + 2);
        }
        return pcm;
    }

    private static void Stereo()
    {
        byte[] source = Audio(), payload = AmigaNetProtocol.CreateAudioChunkPayload(source, source.Length, 48000);
        Require(payload.Length < source.Length / 2, "ADPCM packs stereo.");
        Require(AmigaNetProtocol.ReadAudioChunkPayload(payload, out int rate, out byte[] decoded, out int count) && count == source.Length && rate == 48000, "Stereo duration/rate.");
        double error = 0, separation = 0;
        for (int i = 0; i < count; i += 2) error += Math.Pow(BitConverter.ToInt16(source, i) - BitConverter.ToInt16(decoded, i), 2);
        for (int i = 0; i < count; i += 4) separation += Math.Abs(BitConverter.ToInt16(decoded, i) - BitConverter.ToInt16(decoded, i + 2));
        Require(Math.Sqrt(error / (count / 2)) < 500 && separation / (count / 4) > 4000, "Independent predictions and bounded RMS error.");
    }

    private static Stream E2E(Stream raw, byte[] key, bool server)
    {
        var type = typeof(AmigaNetProtocol).Assembly.GetType("AmigaEmulator.Network.AmigaRelayE2EStream");
        return (Stream)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { raw, (byte[])key.Clone(), server }, null);
    }

    private static void Encryption()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32), plain = Encoding.UTF8.GetBytes("PRIVATE AMIGA VIDEO AUDIO INPUT");
        using var raw = new MemoryStream(); using var encrypted = E2E(raw, key, true);
        encrypted.Write(plain); byte[] wire = raw.ToArray();
        Require(!Encoding.UTF8.GetString(wire).Contains("PRIVATE AMIGA"), "No plaintext channel data.");
        using var decoded = E2E(new MemoryStream(wire), key, false); byte[] buffer = new byte[plain.Length]; decoded.ReadExactly(buffer);
        Require(buffer.SequenceEqual(plain), "AES GCM round trip.");
        byte[] corrupt = (byte[])wire.Clone(); corrupt[^1] ^= 1;
        using var tampered = E2E(new MemoryStream(corrupt), key, false); ExpectFailure(() => tampered.ReadExactly(buffer));
        using var replay = E2E(new MemoryStream(wire.Concat(wire).ToArray()), key, false);
        replay.ReadExactly(buffer); ExpectFailure(() => replay.ReadExactly(buffer));
        encrypted.GetType().GetField("_sendCounter", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(encrypted, ulong.MaxValue);
        ExpectFailure(() => encrypted.Write(plain));
    }

    private static void Session(bool relay)
    {
        using var isolated = new IsolatedProfile();
        using var proxy = relay ? new RelayProcess(isolated.Directory) : null;
        int port = relay ? proxy.Port : FreePort();
        using var server = new AmigaNetServer(127, 81);
        if (relay) server.StartRelay("127.0.0.1", port, " Test-Room ", "relay-secret", "game-secret");
        else server.Start(port, "game-secret");
        uint[] pixels = Enumerable.Range(0, 127 * 81).Select(i => 0xFF000000u | (uint)(i * 719 % 0x1000000)).ToArray();
        server.BroadcastVideoFrame(pixels, 127, 81, 3); // Cached before the first peer joins.
        using var client = new AmigaNetClient(); using var observer = new AmigaNetClient();
        uint[] received = null; int audioBytes = 0; int crop = -1; int audioLed = -1;
        client.FrameReceived += f => { received = (uint[])f.Pixels.Clone(); Volatile.Write(ref crop, f.BottomCrop); };
        client.AudioReceived += (_, count) => Volatile.Write(ref audioBytes, count);
        client.AudioReceivedWithFilterState += (_, _, led) => Volatile.Write(ref audioLed, led ? 1 : 0);
        bool connected = relay ? client.ConnectRelay("127.0.0.1", port, "test-room", "relay-secret", "game-secret", AmigaNetClientRole.Player, "alice", out _)
            : client.Connect("127.0.0.1", port, "game-secret", AmigaNetClientRole.Player, "alice", out _);
        Require(connected, "Client joins protected session."); Wait(() => Volatile.Read(ref crop) == 3, "Cached frame reaches new client.");
        Require(received.SequenceEqual(pixels), "Exact frame over encrypted transport.");
        if (relay) Require(client.RelaySessionFingerprint != "UNKNOWN", "ECDH session fingerprint.");
        connected = relay ? observer.ConnectRelay("127.0.0.1", port, "test-room", "relay-secret", "game-secret", AmigaNetClientRole.Observer, "bob", out _)
            : observer.Connect("127.0.0.1", port, "game-secret", AmigaNetClientRole.Observer, "bob", out _);
        Require(connected, "Second observer joins.");
        Wait(() => server.GetClientSnapshots().Count == 2, "Two peers visible.");
        Require(client.Permission == AmigaNetJoystickPermission.Observer && !client.KeyboardEnabled && !client.MouseEnabled, "Player starts without authority.");
        client.SendJoystickState(0, true); Thread.Sleep(40);
        Require(server.GetAggregatedJoystickState(AmigaNetJoystickPermission.Port2) == 255, "Ungrant input ignored.");
        int id = client.ClientId;
        server.SetClientPermission(id, AmigaNetJoystickPermission.Port2); server.SetClientKeyboardEnabled(id, true); server.SetClientMouseEnabled(id, true);
        Wait(() => client.Permission == AmigaNetJoystickPermission.Port2 && client.KeyboardEnabled && client.MouseEnabled, "Independent grants received.");
        client.SendJoystickState(0x8E, true); client.SendKeyboardKey(Key.A, true); client.SendMouseInput(14, -9, 0x30);
        Wait(() => server.GetAggregatedJoystickState(AmigaNetJoystickPermission.Port2) == 0x8E && server.GetAggregatedKeyboardKeys().Contains(Key.A), "Directions and all three buttons plus keyboard.");
        Require(server.GetAggregatedJoystickState(AmigaNetJoystickPermission.Port1) == 255, "First joystick remains host owned.");
        if (relay) Require(server.GetClientSnapshots().Single(c => c.ClientId == id).SessionFingerprint == client.RelaySessionFingerprint, "Host and client show matching E2E identities.");
        int dx = 0, dy = 0; byte buttons = 0;
        Wait(() => { server.DrainMouseInput(out dx, out dy, out buttons); return buttons == 0x30; }, "Mouse reaches host.");
        Require(dx == 14 && dy == -9, "Relative mouse coordinates unaffected by scaling.");
        client.SendMouseInput(0, 0, 0x10); client.SendMouseInput(0, 0, 0);
        bool sawPress = false, sawRelease = false;
        Wait(() => { server.DrainMouseInput(out _, out _, out byte b); if (b == 0x10) sawPress = true; if (sawPress && b == 0) sawRelease = true; return sawRelease; }, "Quick mouse click retains both transitions.");
        byte[] audio = Audio(); server.BroadcastAudio(audio, audio.Length); Wait(() => Volatile.Read(ref audioBytes) == audio.Length, "Stereo sound reaches client.");
        Wait(() => Volatile.Read(ref audioLed) == 0, "LED bypass reaches the client with its audio chunk.");
        server.BroadcastAudio(audio, audio.Length, true); Wait(() => Volatile.Read(ref audioLed) == 1, "LED filter on reaches the client over the encrypted transport.");
        server.BroadcastAudio(audio, audio.Length, false); Wait(() => Volatile.Read(ref audioLed) == 0, "Later audio chunks switch the client's LED filter off again.");
        pixels[20] ^= 0x123456; server.BroadcastVideoFrame(pixels, 127, 81, 0);
        Wait(() => Volatile.Read(ref crop) == 0, "Delta frame and crop arrive."); Require(received.SequenceEqual(pixels), "Exact delta transport.");
        server.SetClientPermission(id, AmigaNetJoystickPermission.Observer); server.SetClientKeyboardEnabled(id, false); server.SetClientMouseEnabled(id, false);
        Wait(() => !client.KeyboardEnabled && !client.MouseEnabled && client.Permission == AmigaNetJoystickPermission.Observer, "Grants revoked.");
        Require(server.GetAggregatedKeyboardKeys().Count == 0 && server.GetAggregatedJoystickState(AmigaNetJoystickPermission.Port2) == 255, "No stuck keys/buttons after revoke.");
        server.DrainMouseInput(out dx, out dy, out buttons); Require(buttons == 0, "Mouse releases on revoke.");
        client.Disconnect(); Wait(() => server.GetClientSnapshots().Count == 1, "Disconnect removes only that client.");
        Require(observer.IsConnected, "Observer survives peer disconnect.");
        server.KickClient(observer.ClientId); Wait(() => !observer.IsConnected && server.GetClientSnapshots().Count == 0, "Kick releases last peer.");
    }

    private static void Reject()
    {
        using var isolated = new IsolatedProfile(); int port = FreePort();
        using var server = new AmigaNetServer(32, 24); server.Start(port, "secret");
        using var wrong = new AmigaNetClient(); Require(!wrong.Connect("127.0.0.1", port, "wrong", AmigaNetClientRole.Player, "wrong", out _), "Wrong game password rejected.");
        Require(server.GetClientSnapshots().Count == 0, "Rejected client has no rights.");
        AmigaNetTls.ReplaceTrustedServerCertificate("127.0.0.1", port, new string('A', 64));
        using var changed = new AmigaNetClient(); bool pinRejected = false;
        try { changed.Connect("127.0.0.1", port, "secret", AmigaNetClientRole.Player, "changed", out _); }
        catch (AmigaNetTlsException ex) { pinRejected = ex.IsCertificateChanged && ex.PinChange.OldFingerprint == new string('A', 64); }
        Require(pinRejected, "Certificate changed fails closed with old/new identity.");
    }

    private static void Settings()
    {
        using var isolated = new IsolatedProfile();
        AmigaEmulatorSettingsStore.Save(new() { MachineModel = AmigaMachineModel.A1200, Df0Path = "keep.adf", HardDiskPath = "keep.hdf", NetworkTransportMode = "Relay", NetworkConnectionId = "friends", NetworkRelayPort = 7000, NetworkClientPassword = "secret", MasterVolume = .6f });
        var settings = AmigaEmulatorSettingsStore.Load();
        Require(settings.MachineModel == AmigaMachineModel.A1200 && settings.Df0Path == "keep.adf" && settings.HardDiskPath == "keep.hdf", "Hardware/media preserved.");
        Require(settings.NetworkTransportMode == "Relay" && settings.NetworkConnectionId == "friends" && settings.NetworkRelayPort == 7000 && settings.NetworkClientPassword == "secret" && settings.MasterVolume == .6f, "Network and audio preferences saved.");
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        catch (CryptographicException) { return; }
        catch (EndOfStreamException) { return; }
        throw new InvalidOperationException("Malformed or unauthenticated data was accepted.");
    }
    private static void ExpectEndOfStream(Action action)
    {
        try { action(); }
        catch (EndOfStreamException) { return; }
        throw new InvalidOperationException("Truncated message was accepted as a clean disconnect.");
    }
    private static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }

    private sealed class IsolatedProfile : IDisposable
    {
        private readonly string _previous = Environment.GetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY");
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "AmigaNetworkTests", Guid.NewGuid().ToString("N"));
        public IsolatedProfile() { System.IO.Directory.CreateDirectory(Directory); Environment.SetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY", Directory); }
        public void Dispose() => Environment.SetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY", _previous);
    }

    private sealed class RelayProcess : IDisposable
    {
        private readonly Process _process;
        public int Port { get; } = FreePort();
        public RelayProcess(string directory)
        {
            string path = Path.GetFullPath("relay_server/amiga_relay_server.py");
            var start = new ProcessStartInfo("python") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "-u", path, "--host", "127.0.0.1", "--port", Port.ToString(), "--cert", Path.Combine(directory, "relay.crt"), "--key", Path.Combine(directory, "relay.key"), "--password", "relay-secret", "--log-file", Path.Combine(directory, "relay.log") }) start.ArgumentList.Add(arg);
            _process = Process.Start(start); _ = _process.StandardOutput.ReadToEndAsync(); _ = _process.StandardError.ReadToEndAsync();
            Wait(() => { if (_process.HasExited) throw new InvalidOperationException("Relay exited before listening: " + directory); try { using var test = new TcpClient(); test.Connect("127.0.0.1", Port); return true; } catch (SocketException) { return false; } }, "Python TLS relay starts.");
        }
        public void Dispose() { if (!_process.HasExited) _process.Kill(); _process.WaitForExit(3000); _process.Dispose(); }
    }
}
