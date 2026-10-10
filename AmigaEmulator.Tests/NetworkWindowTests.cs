// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Input;
using AmigaEmulator.Network;
using SharpPixels;

namespace AmigaEmulator.Tests;

internal static class NetworkWindowTests
{
    public static IEnumerable<ChipTestCase> Build()
    {
        yield return new("Network UI", "Settings hub has six complete pages and nested back navigation", "Actual AmigaWindow menus; isolated profile", () => Sta(Menus));
        yield return new("Network UI", "Actual host/client windows transfer native frames and control without running client CPU", "Actual AmigaWindow LAN transport, local HQ4x and guest input", () => Sta(Windows));
    }

    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Get(AmigaWindow window, string name) => typeof(AmigaWindow).GetField(name, Private).GetValue(window);
    private static void Set(AmigaWindow window, string name, object value)
    {
        FieldInfo field = typeof(AmigaWindow).GetField(name, Private);
        field.SetValue(window, field.FieldType.IsEnum ? Enum.Parse(field.FieldType, (string)value) : value);
    }
    private static object Call(AmigaWindow window, string name, params object[] args)
    {
        MethodInfo method = typeof(AmigaWindow).GetMethod(name, Private);
        var parameters = method.GetParameters();
        for (int i = 0; i < args.Length; i++) if (parameters[i].ParameterType.IsEnum && args[i] is string str) args[i] = Enum.Parse(parameters[i].ParameterType, str);
        return method.Invoke(window, args);
    }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    private static void Menus()
    {
        using var profile = new Profile(); using var lifetime = new WindowLifetime(); var window = lifetime.Window;
        Call(window, "ShowOverlay", "MainMenu"); Call(window, "ShowOverlay", "Settings");
        Require((int)Call(window, "GetSelectionCount") == 6, "Six categories.");
        string[] pages = { "Machine", "Roms", "Ram", "Display", "Sound", "HardDisk" };
        for (int i = 0; i < pages.Length; i++)
        {
            Set(window, "_settingsSelection", i); Call(window, "ChangeSettingsItem", 1);
            Require(Get(window, "_overlayMode").ToString() == pages[i], "Category opens " + pages[i]);
            Require((int)Call(window, "GetSelectionCount") > 0, "Category has actionable rows.");
            Call(window, "CloseOrBackOverlay"); Require(Get(window, "_overlayMode").ToString() == "Settings", "Back returns to hub.");
        }
        Set(window, "_settingsSelection", 0); Call(window, "ChangeSettingsItem", 1);
        Set(window, "_machineSelection", 5); Call(window, "ChangeMachineItem", 1);
        Require(Get(window, "_overlayMode").ToString() == "Input", "Nested input menu.");
        Call(window, "CloseOrBackOverlay"); Require(Get(window, "_overlayMode").ToString() == "Machine", "Input back goes to hardware.");
        Call(window, "CloseOrBackOverlay"); Call(window, "CloseOrBackOverlay");
        Require(Get(window, "_overlayMode").ToString() == "MainMenu", "Hub back goes to main.");
        Call(window, "ShowOverlay", "Network"); window.ClientSize = new Size(704, 576); Set(window, "_networkSelection", 19);
        using var bitmap = new Bitmap(704, 576); using var graphics = Graphics.FromImage(bitmap);
        Call(window, "DrawNetworkOverlay", graphics); bitmap.Save(Path.Combine(profile.Directory, "network-menu.png"));
        Call(window, "CloseOrBackOverlay"); Call(window, "ShowOverlay", "Settings");
        graphics.Clear(Color.Black);
        Call(window, "DrawSettingsOverlay", graphics); bitmap.Save(Path.Combine(profile.Directory, "settings-menu.png"));
        Call(window, "ToggleOverlay", "MainMenu"); Require(Get(window, "_overlayMode").ToString() == "None", "F10 closes entire menu family.");
    }

    private static void Windows()
    {
        using var profile = new Profile(); using var h = new WindowLifetime(); using var c = new WindowLifetime();
        AmigaWindow host = h.Window, client = c.Window;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var hs = (AmigaEmulatorSettings)Get(host, "_settings"); var cs = (AmigaEmulatorSettings)Get(client, "_settings");
        hs.NetworkServerPort = cs.NetworkClientPort = port;
        hs.NetworkServerPassword = cs.NetworkClientPassword = "window-secret";
        var core = (AmigaSystem)Get(host, "_system"); var clientCore = (AmigaSystem)Get(client, "_system");
        string rom = Environment.GetEnvironmentVariable("AMIGAEMULATOR_NETWORK_TEST_ROM");
        if (!string.IsNullOrEmpty(rom))
        {
            core.LoadKickstart(File.ReadAllBytes(rom));
            for (int i = 0; i < 80; i++) core.RunChipCycles(core.Model.ChipCyclesPerLine * core.Model.LinesPerFrame);
        }
        else
            for (int i = 0; i < core.PresentedFrameBuffer.Pixels.Length; i++) core.PresentedFrameBuffer.Pixels[i] = 0xFF000000u | (uint)(i * 719 % 0x1000000);
        string preserved = clientCore.GetCpuDebugInfo();
        Set(client, "_videoUpscaleMode", "Hq4x"); Call(client, "ApplyVideoPresentation");
        cs.StretchToFullWidth = true; client.SetPresentationStretchToClient(true);
        Set(client, "_inputPortMode", "DualJoystick"); Call(client, "ApplyInputPortMode");
        Call(host, "ToggleNetworkServer");
        PumpUntil(host, client, () => Get(host, "_networkServer") is AmigaNetServer s && s.IsRunning && !(bool)Get(host, "_networkBusy"));
        Call(client, "ToggleNetworkClient");
        PumpUntil(host, client, () => (bool)Get(client, "_remoteSession") && client.Pixels.SequenceEqual(core.PresentedFrameBuffer.Pixels));
        Require(clientCore.GetCpuDebugInfo() == preserved, "Client CPU remains parked with its own state retained.");
        Require(Get(client, "_videoUpscaleMode").ToString() == "Hq4x" && Get(host, "_videoUpscaleMode").ToString() == "None", "Scaling is independent.");
        var server = (AmigaNetServer)Get(host, "_networkServer"); var net = (AmigaNetClient)Get(client, "_networkClient");
        server.SetClientPermission(net.ClientId, AmigaNetJoystickPermission.Port2);
        // Deliberately press before another frontend update. A newly received grant
        // must not cause the next update to release and swallow this first press.
        Require(SpinWait.SpinUntil(() => net.Permission == AmigaNetJoystickPermission.Port2
            && (net.InputConfiguration & 0xFF00) == (int)AmigaNetJoystickPermission.Port2 << 8, 8000), "Port 2 grant arrives between frontend updates.");
        client.OnUserKeyDown(new SharpPixels.KeyEventArgs(Key.Up, '\0', false, false, false));
        // The receiver can publish its state between the host UI update and the
        // condition check. Wait for guest application as well as packet receipt.
        PumpUntil(host, client, () => server.GetAggregatedJoystickState(AmigaNetJoystickPermission.Port2) == 0xFE
            && core.Input.ReadJoyDat(AmigaControlPort.Port1) != 0);
        Require(core.Input.ReadJoyDat(AmigaControlPort.Port1) != 0, "Remote second joystick reaches actual guest hardware.");
        client.OnUserKeyUp(new SharpPixels.KeyEventArgs(Key.Up, '\0', false, false, false));
        PumpUntil(host, client, () => server.GetAggregatedJoystickState(AmigaNetJoystickPermission.Port2) == 255);
        Call(host, "ToggleRemoteControl", server.GetClientSnapshots().Single());
        PumpUntil(host, client, () => net.KeyboardEnabled && net.MouseEnabled && net.Permission == AmigaNetJoystickPermission.Both);
        int keyboardEventsBefore = core.Input.QueuedKeyboardEvents;
        host.OnUserKeyDown(new SharpPixels.KeyEventArgs(Key.B, 'b', false, false, false));
        Require(core.Input.QueuedKeyboardEvents == keyboardEventsBefore, "Host game input is suppressed while control is handed over.");
        client.OnUserKeyDown(new SharpPixels.KeyEventArgs(Key.B, 'b', false, false, false));
        PumpUntil(host, client, () => core.Input.QueuedKeyboardEvents > keyboardEventsBefore);
        client.OnUserKeyDown(new SharpPixels.KeyEventArgs(Key.A, 'a', false, false, false));
        PumpUntil(host, client, () => server.GetAggregatedKeyboardKeys().Contains(Key.A));
        Require(net.HostInputMode == 0 && Get(client, "_inputPortMode").ToString() == "DualJoystick", "Guest keys follow the host mapping without modifying the client profile.");
        Set(host, "_inputPortMode", "DualJoystick"); Call(host, "ApplyInputPortMode");
        PumpUntil(host, client, () => net.HostInputMode == 2 && server.GetAggregatedKeyboardKeys().Count == 0);
        client.OnUserKeyUp(new SharpPixels.KeyEventArgs(Key.A, 'a', false, false, false));
        client.OnUserKeyDown(new SharpPixels.KeyEventArgs(Key.A, 'a', false, false, false));
        PumpUntil(host, client, () => server.GetAggregatedJoystickState(AmigaNetJoystickPermission.Port1) == 0xFB);
        Require(server.GetAggregatedKeyboardKeys().Count == 0, "Live host dual-joystick configuration maps A to joystick instead of a guest key.");
        Set(host, "_inputPortMode", "MousePort1JoystickPort2"); Call(host, "ApplyInputPortMode");
        PumpUntil(host, client, () => net.HostInputMode == 0 && server.GetAggregatedJoystickState(AmigaNetJoystickPermission.Port1) == 255);
        client.OnUserKeyUp(new SharpPixels.KeyEventArgs(Key.A, 'a', false, false, false));
        ushort mouseBefore = core.Input.ReadJoyDat(AmigaControlPort.Port0);
        Call(client, "QueueBufferedMouseMotion", AmigaControlPort.Port0, 7, 5);
        Call(client, "QueueOrApplyMouseButton", AmigaControlPort.Port0, AmigaJoystickButtons.Fire, true);
        PumpUntil(host, client, () => (core.Input.GetCiaAPortAInputValue() & 0x40) == 0);
        Call(host, "DrainBufferedMouseMotionSlice");
        Require(core.Input.ReadJoyDat(AmigaControlPort.Port0) != mouseBefore, "Relative mouse reaches hardware counters.");
        Call(host, "ReleaseMouse");
        Require((core.Input.GetCiaAPortAInputValue() & 0x40) == 0, "Releasing the host cursor preserves the remote mouse owner's held button.");
        Call(client, "ReleaseMouse");
        PumpUntil(host, client, () => (core.Input.GetCiaAPortAInputValue() & 0x40) != 0);
        Call(client, "QueueOrApplyMouseButton", AmigaControlPort.Port0, AmigaJoystickButtons.Fire, true);
        PumpUntil(host, client, () => (core.Input.GetCiaAPortAInputValue() & 0x40) == 0);
        Call(client, "ShowOverlay", "MainMenu");
        PumpUntil(host, client, () => server.GetAggregatedKeyboardKeys().Count == 0 && (core.Input.GetCiaAPortAInputValue() & 0x40) != 0);
        Set(client, "_settingsSelection", 0); Call(client, "ChangeSettingsItem", 1);
        Require(Get(client, "_overlayMode").ToString() == "MainMenu", "Remote viewer cannot reconfigure host hardware.");
        Call(client, "LeaveNetworkClient");
        Require(clientCore.GetCpuDebugInfo() == preserved && !(bool)Get(client, "_remoteSession"), "Leaving restores the parked local machine.");
        PumpUntil(host, client, () => server.GetClientSnapshots().Count == 0);
        Call(host, "UpdateNetwork"); Require((int)Get(host, "_controlOwner") == 0, "Control returns to host after disconnect.");
    }

    private static void PumpUntil(AmigaWindow host, AmigaWindow client, Func<bool> condition, [CallerArgumentExpression(nameof(condition))] string expected = null)
    {
        var start = DateTime.UtcNow;
        do
        {
            Call(host, "UpdateNetwork"); Call(client, "UpdateNetwork");
            if (condition()) return;
            Thread.Sleep(10);
        } while (DateTime.UtcNow - start < TimeSpan.FromSeconds(8));
        throw new InvalidOperationException("Window transport timed out waiting for " + expected + ": " + Get(host, "_statusText") + " / " + Get(client, "_statusText"));
    }

    private static void Sta(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
    private sealed class Profile : IDisposable
    {
        private readonly string _previous = Environment.GetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY");
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "AmigaNetworkWindowTests", Guid.NewGuid().ToString("N"));
        public Profile()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Environment.SetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY", Directory);
            AmigaEmulatorSettingsStore.Save(new() { AudioEnabled = false });
        }
        public void Dispose() => Environment.SetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY", _previous);
    }
    private sealed class WindowLifetime : IDisposable
    {
        public AmigaWindow Window { get; } = new(AmigaModel.A500Pal, "Network test");
        public void Dispose()
        {
            Call(Window, "OnFormClosed", new FormClosedEventArgs(CloseReason.None)); Window.Dispose();
        }
    }
}
