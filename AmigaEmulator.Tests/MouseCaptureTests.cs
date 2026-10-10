using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Input;
using SharpPixels;

namespace AmigaEmulator.Tests;

internal static class MouseCaptureTests
{
    public static IEnumerable<ChipTestCase> Build()
    {
        yield return Test("Focus and hover leave the guest mouse free; only an image click captures", CaptureClick);
        yield return Test("Release shortcut clears held and deferred buttons without forwarding F8", Shortcut);
        yield return Test("Menus retain capture until Release mouse is clicked; focus loss and closing still release", ReleaseLifecycle);
        yield return Test("Mouse clicks resolve scrolled media and ROM rows without forwarding guest input", ScrolledMenus);
        yield return Test("Relative movement keeps its mapping across upscalers, stretch and fullscreen", Mapping);
    }

    private static ChipTestCase Test(string name, Action run) => new("Mouse Capture", name,
        "Actual AmigaWindow callbacks, Win32 cursor clipping and emulated input registers", () => Sta(run));
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Get(AmigaWindow window, string name) => typeof(AmigaWindow).GetField(name, Private).GetValue(window);
    private static void SetEnum(AmigaWindow window, string name, string value)
    {
        var field = typeof(AmigaWindow).GetField(name, Private);
        field.SetValue(window, Enum.Parse(field.FieldType, value));
    }
    private static object Call(AmigaWindow window, string name, params object[] args)
    {
        var method = typeof(AmigaWindow).GetMethod(name, Private);
        var parameters = method.GetParameters();
        for (int i = 0; i < args.Length; i++)
            if (parameters[i].ParameterType.IsEnum && args[i] is string text) args[i] = Enum.Parse(parameters[i].ParameterType, text);
        return method.Invoke(window, args);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static bool Captured(AmigaWindow window) => (bool)Get(window, "_hostMouseCaptured");
    private static AmigaSystem Core(AmigaWindow window) => (AmigaSystem)Get(window, "_system");
    private static Rectangle Viewport(AmigaWindow window) => (Rectangle)typeof(SharpPixelsWindow).GetMethod("GetPresentationRectangle", Private).Invoke(window, null);
    private static Point Center(AmigaWindow window)
    {
        Rectangle r = Viewport(window);
        return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
    }
    private static void Mouse(AmigaWindow window, string method, MouseButtons button, Point point)
        => Call(window, method, new MouseEventArgs(button, 1, point.X, point.Y, 0));
    private static void Click(AmigaWindow window, MouseButtons button = MouseButtons.Left)
    {
        Point point = Center(window);
        Mouse(window, "OnMouseDown", button, point);
        Mouse(window, "OnMouseUp", button, point);
    }
    private static void Capture(AmigaWindow window)
    {
        Click(window);
        Require(Captured(window), "An explicit click must capture the focused image.");
        Rectangle r = Viewport(window);
        Require(Clip() == new Rectangle(window.PointToScreen(r.Location), r.Size), "Windows clips exactly the displayed image.");
        RequireCentered(window);
        Mouse(window, "OnMouseMove", MouseButtons.None, Center(window)); // consume the synthetic warp
    }
    private static void Free(AmigaWindow window)
    {
        Require(!Captured(window) && !(bool)Get(window, "_hostMouseCaptureRequested"), "Capture stays disarmed.");
        Require(ReferenceEquals(window.Cursor, Cursors.Default), "The normal Windows cursor is restored.");
        Rectangle desktop = SystemInformation.VirtualScreen, clip = Clip();
        Require(clip.Contains(desktop.Location) && clip.Contains(desktop.Right - 1, desktop.Bottom - 1), "Windows clipping is released for the entire desktop.");
    }
    private static void RequireCentered(AmigaWindow window)
    {
        Point center = window.PointToScreen(Center(window));
        // On an interactive desktop physical input may move the cursor immediately
        // after SetCursorPos. Verify its target as well as the real native clip.
        Point actual = Cursor.Position;
        Point target = (Point)Get(window, "_pendingHostMouseWarpScreenPoint");
        Require(actual == center || target == center,
            "Relative capture targets the displayed image's center: expected=" + center + ", actual=" + actual + ", target=" + target + ".");
    }
    private static void MenuCaptured(AmigaWindow window)
    {
        Require(Captured(window) && (bool)Get(window, "_hostMouseCaptureRequested"), "Menus retain the user's mouse capture.");
        Require(ReferenceEquals(window.Cursor, Cursors.Default), "The menu shows a pointer for selecting Release mouse.");
        Require(Clip() == new Rectangle(window.PointToScreen(Point.Empty), window.ClientSize), "Menu pointer stays confined to the emulator window.");
    }
    private static void PaintMenu(AmigaWindow window)
    {
        using var bitmap = new Bitmap(window.ClientSize.Width, window.ClientSize.Height);
        using var graphics = Graphics.FromImage(bitmap);
        Call(window, "OnRenderOverlay", graphics, Viewport(window));
    }
    private static Point MenuPoint(AmigaWindow window, int selection)
    {
        foreach (object row in (System.Collections.IEnumerable)Get(window, "_overlayMenuMouseRows"))
        {
            if ((int)row.GetType().GetProperty("Selection").GetValue(row) != selection) continue;
            var r = (Rectangle)row.GetType().GetProperty("Bounds").GetValue(row);
            return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
        }
        throw new InvalidOperationException("Rendered menu lacks selection " + selection);
    }
    private static void ClickMenu(AmigaWindow window, int selection)
    {
        Point point = MenuPoint(window, selection);
        Cursor.Position = window.PointToScreen(point);
        Mouse(window, "OnMouseMove", MouseButtons.None, point);
        Require(!(bool)Get(window, "_pendingHostMouseWarp"), "Menu pointer movement never requests recentering.");
        Require((int)Call(window, "GetSelection") == selection, "Hover selects the visible menu row.");
        Mouse(window, "OnMouseDown", MouseButtons.Left, point);
        Mouse(window, "OnMouseUp", MouseButtons.Left, point);
    }

    private static void CaptureClick()
    {
        using var session = new Session(); var window = session.Window; var input = Core(window).Input;
        Free(window);
        ushort start = input.ReadJoyDat(AmigaControlPort.Port0);
        Point center = Center(window);
        Mouse(window, "OnMouseMove", MouseButtons.None, center);
        Mouse(window, "OnMouseMove", MouseButtons.None, new Point(center.X + 40, center.Y + 20));
        Call(window, "DrainBufferedMouseMotionSlice");
        Call(window, "OnActivated", EventArgs.Empty);
        window.ClientSize = new Size(800, 640);
        Require(input.ReadJoyDat(AmigaControlPort.Port0) == start, "Hover must not move the guest mouse.");
        Free(window);

        window.SetPresentationTargetMargins(0, 0, 0, 40);
        Point border = new(window.ClientSize.Width / 2, window.ClientSize.Height - 2);
        Require(!Viewport(window).Contains(border), "Test click is outside the image.");
        Mouse(window, "OnMouseDown", MouseButtons.Left, border);
        Mouse(window, "OnMouseUp", MouseButtons.Left, border);
        Free(window);
        Capture(window);
        Require((input.GetCiaAPortAInputValue() & 0x40) != 0, "The capture click must not press guest fire.");
        Require(input.ReadJoyDat(AmigaControlPort.Port0) == start, "The initial recenter must not move the guest mouse.");
        Mouse(window, "OnMouseDown", MouseButtons.Left, Center(window));
        Require((input.GetCiaAPortAInputValue() & 0x40) == 0, "The following click reaches guest hardware.");
        Mouse(window, "OnMouseUp", MouseButtons.Left, Center(window));
        Require((input.GetCiaAPortAInputValue() & 0x40) != 0, "Guest fire releases normally.");

        Call(window, "ReleaseMouse");
        SetEnum(window, "_inputPortMode", "DualJoystick"); Call(window, "ApplyInputPortMode");
        Click(window); Free(window);
    }

    private static void Shortcut()
    {
        using var session = new Session(); var window = session.Window; var input = Core(window).Input;
        Capture(window);
        Mouse(window, "OnMouseDown", MouseButtons.Left, Center(window));
        Mouse(window, "OnMouseMove", MouseButtons.None, new Point(Center(window).X + 120, Center(window).Y + 80));
        Mouse(window, "OnMouseDown", MouseButtons.Right, Center(window));
        Mouse(window, "OnMouseUp", MouseButtons.Right, Center(window));
        Require((int)Get(window, "_pendingMouseDx") != 0 && (AmigaJoystickButtons)Get(window, "_queuedMouseDownButtons") != 0, "Exercise actual pending motion and deferred clicks.");
        window.OnUserKeyDown(new SharpPixels.KeyEventArgs(Key.F8, '\0', false, false, false));
        Require(((HashSet<byte>)Get(window, "_localKeyboardKeys")).Contains(0x57), "Exercise F8 held before the shortcut modifiers arrive.");
        window.OnUserKeyDown(new SharpPixels.KeyEventArgs(Key.ControlLeft, '\0', false, true, false));
        window.OnUserKeyDown(new SharpPixels.KeyEventArgs(Key.AltLeft, '\0', true, true, false));
        var message = Message.Create(window.Handle, 0x100, (IntPtr)Keys.F8, IntPtr.Zero);
        Require((bool)Call(window, "ProcessCmdKey", message, Keys.Control | Keys.Alt | Keys.F8), "The real command-key path consumes the release chord.");
        Free(window);
        Require(!((HashSet<byte>)Get(window, "_localKeyboardKeys")).Contains(0x57), "The host shortcut cannot strand a previously held guest F8.");
        Require((input.GetCiaAPortAInputValue() & 0xC0) == 0xC0, "Mouse and modifier joystick fire cannot remain held.");
        Require((input.ReadPotInp(0) & 0x0500) == 0x0500, "Deferred right/middle buttons cannot remain held.");
        Require((int)Get(window, "_pendingMouseDx") == 0 && (int)Get(window, "_pendingMouseDy") == 0
            && (AmigaJoystickButtons)Get(window, "_queuedMouseDownButtons") == 0
            && (AmigaJoystickButtons)Get(window, "_queuedMouseUpButtons") == 0, "Buffered input is discarded on release.");
        Require(!((HashSet<byte>)Get(window, "_localKeyboardKeys")).Contains(0x64), "Shortcut Alt is released in the guest.");
        int events = input.QueuedKeyboardEvents;
        var repeat = new SharpPixels.KeyEventArgs(Key.F8, '\0', false, false, false);
        window.OnUserKeyDown(repeat);
        var up = new SharpPixels.KeyEventArgs(Key.F8, '\0', false, false, false);
        window.OnUserKeyUp(up);
        Require(repeat.Handled && up.Handled && events == input.QueuedKeyboardEvents, "A repeated/released host shortcut never becomes a guest F8.");
        var guest = new SharpPixels.KeyEventArgs(Key.F8, '\0', false, false, false);
        window.OnUserKeyDown(guest);
        Require(input.QueuedKeyboardEvents == events + 1, "Ordinary F8 remains available to Amiga software.");
        window.OnUserKeyUp(guest);
        Mouse(window, "OnMouseMove", MouseButtons.None, Center(window)); Free(window);
        Capture(window);
        window.OnUserKeyDown(new SharpPixels.KeyEventArgs(Key.F8, '\0', true, true, false));
        Free(window);
    }

    private static void ReleaseLifecycle()
    {
        using var session = new Session(); var window = session.Window;
        Capture(window);
        ushort mouseBefore = Core(window).Input.ReadJoyDat(AmigaControlPort.Port0);
        Call(window, "ShowOverlay", "MainMenu"); MenuCaptured(window);
        Require(((string)Get(window, "_statusText")).StartsWith("MOUSE CAPTURED"), "The menu correctly reports retained capture.");
        var items = (string[])Get(window, "_mainMenuItems");
        int release = Array.IndexOf(items, "RELEASE MOUSE");
        Require(release >= 0, "The main menu exposes Release mouse.");
        using (var bitmap = new Bitmap(window.ClientSize.Width, window.ClientSize.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            Call(window, "DrawMainMenuOverlay", graphics);
            bitmap.Save(Path.Combine(session.Directory, "release-mouse-menu.png"));
        }
        ClickMenu(window, 0); MenuCaptured(window);
        Require(Get(window, "_overlayMode").ToString() == "Settings", "Main menu settings opens by pointer click.");
        PaintMenu(window); ClickMenu(window, 3); MenuCaptured(window);
        Require(Get(window, "_overlayMode").ToString() == "Display", "Graphics submenu opens by pointer click.");
        Call(window, "CloseOrBackOverlay"); Call(window, "CloseOrBackOverlay"); PaintMenu(window);
        ClickMenu(window, release);
        Require(Get(window, "_overlayMode").ToString() == "None", "Release mouse returns to the emulator with a free cursor.");
        Require(Core(window).Input.ReadJoyDat(AmigaControlPort.Port0) == mouseBefore, "Menu pointer movement never moves the guest mouse.");
        Call(window, "UpdateHostMouseCapture"); Free(window);
        Call(window, "ShowOverlay", "MainMenu"); PaintMenu(window); Free(window);
        ClickMenu(window, 0); Free(window);
        Call(window, "CloseOrBackOverlay"); Call(window, "CloseOrBackOverlay"); Free(window);
        Capture(window);
        Call(window, "ShowOverlay", "Settings"); MenuCaptured(window);
        Call(window, "CloseOrBackOverlay");
        Require(Captured(window) && ReferenceEquals(window.Cursor, Get(window, "_hiddenHostCursor")), "Closing a menu restores guest control without recapturing.");
        Mouse(window, "OnMouseMove", MouseButtons.None, Center(window));
        Require(Core(window).Input.ReadJoyDat(AmigaControlPort.Port0) == mouseBefore, "Returning from a menu cannot jump the guest pointer.");
        Mouse(window, "OnMouseDown", MouseButtons.Left, Center(window));
        Call(window, "OnDeactivate", EventArgs.Empty); Free(window);
        Require((Core(window).Input.GetCiaAPortAInputValue() & 0x40) != 0, "Losing focus releases held guest buttons.");
        Call(window, "OnActivated", EventArgs.Empty);
        Mouse(window, "OnMouseMove", MouseButtons.None, Center(window)); Free(window);
        Capture(window);
        window.Close(); Free(window);
    }

    private static void ScrolledMenus()
    {
        using var session = new Session(); var window = session.Window;
        Capture(window);
        ushort before = Core(window).Input.ReadJoyDat(AmigaControlPort.Port0);
        var media = (List<string>)Get(window, "_mediaEntries"); media.Clear();
        for (int i = 0; i < 31; i++) media.Add(Path.Combine(session.Directory, "missing-disk-" + i + ".adf"));
        typeof(AmigaWindow).GetField("_mediaSelection", Private).SetValue(window, 20);
        Call(window, "ShowOverlay", "Media"); PaintMenu(window); MenuCaptured(window);
        ClickMenu(window, 0);
        Require((int)Get(window, "_mediaDriveIndex") == 1, "A fixed media header stays fixed when entries are scrolled.");
        typeof(AmigaWindow).GetField("_mediaSelection", Private).SetValue(window, 20);
        PaintMenu(window); ClickMenu(window, 20);
        Require((int)Get(window, "_mediaSelection") == 20 && ((string)Get(window, "_statusText")).Contains("MOUNT FAILED"), "A visible entry maps to its actual disk index (synthetic missing fixture).");

        var roms = (List<KickstartRomInfo>)Get(window, "_romEntries"); roms.Clear();
        for (int i = 0; i < 26; i++) roms.Add(KickstartRomResolver.Probe(Path.Combine(session.Directory, "missing-rom-" + i + ".rom")));
        typeof(AmigaWindow).GetField("_romSelection", Private).SetValue(window, 16);
        Call(window, "ShowOverlay", "Roms"); PaintMenu(window); ClickMenu(window, 16);
        Require((int)Get(window, "_romSelection") == 16 && (string)Get(window, "_statusText") == "ROM NOT USABLE", "A visible ROM entry maps to its actual index without loading any ROM.");
        MenuCaptured(window);
        Require(Core(window).Input.ReadJoyDat(AmigaControlPort.Port0) == before, "Scrolled menu clicks are host UI input only.");
    }

    private static void Mapping()
    {
        using var session = new Session(); var window = session.Window; var input = Core(window).Input;
        foreach (bool stretch in new[] { false, true })
        {
            ((AmigaEmulatorSettings)Get(window, "_settings")).StretchToFullWidth = stretch;
            window.SetPresentationStretchToClient(stretch);
            ushort? expected = null;
            Rectangle viewport = Viewport(window);
            foreach (string mode in new[] { "None", "Scale2x", "Scale3x", "Hq2x", "Hq3x", "Hq4x" })
            {
                SetEnum(window, "_videoUpscaleMode", mode); Call(window, "ApplyVideoPresentation");
                Require(Viewport(window) == viewport, "Upscaling preserves the capture area.");
                Capture(window);
                ushort before = input.ReadJoyDat(AmigaControlPort.Port0);
                Mouse(window, "OnMouseMove", MouseButtons.None, new Point(Center(window).X + 30, Center(window).Y + 20));
                for (int i = 0; i < 16; i++) Call(window, "DrainBufferedMouseMotionSlice");
                ushort after = input.ReadJoyDat(AmigaControlPort.Port0);
                ushort delta = (ushort)(((byte)((after >> 8) - (before >> 8)) << 8) | (byte)(after - before));
                expected ??= delta;
                Require(delta != 0 && delta == expected, "Relative mouse mapping is independent of source upscaling.");
                RequireCentered(window);
                Call(window, "ReleaseMouse"); Free(window);
            }
        }
        SetEnum(window, "_videoUpscaleMode", "None"); Call(window, "ApplyVideoPresentation");
        window.SetPresentationStretchToClient(true);
        Capture(window);
        Call(window, "ToggleFullscreen");
        Require(Viewport(window) == window.ClientRectangle, "Fullscreen stretch covers the entire image area.");
        Rectangle full = Viewport(window);
        Require(Captured(window) && Clip() == new Rectangle(window.PointToScreen(full.Location), full.Size), "An existing capture follows fullscreen geometry.");
        Call(window, "ReleaseMouse"); Free(window);
        Call(window, "ToggleFullscreen"); Free(window);
    }

    private static void Sta(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
    private sealed class Session : IDisposable
    {
        private readonly string _previous = Environment.GetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY");
        private readonly Point _cursor = Cursor.Position;
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "AmigaMouseCaptureTests", Guid.NewGuid().ToString("N"));
        public AmigaWindow Window { get; }
        public Session()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Environment.SetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY", Directory);
            AmigaEmulatorSettingsStore.Save(new() { AudioEnabled = false, DriveOverlayEnabled = false });
            Window = new(AmigaModel.A500Pal, "Amiga mouse capture regression")
            { StartPosition = FormStartPosition.Manual, Location = new Point(80, 80), ClientSize = new Size(704, 576) };
            Window.Show(); Window.Activate(); Window.Focus();
            Application.DoEvents();
            Require(Window.Focused || Window.ContainsFocus, "The actual test window must own focus.");
        }
        public void Dispose()
        {
            try { Window.Close(); Window.Dispose(); }
            finally
            {
                Cursor.Position = _cursor;
                Environment.SetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY", _previous);
            }
        }
    }
    private static Rectangle Clip()
    {
        Require(GetClipCursor(out Rect r), "GetClipCursor succeeds.");
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }
    [DllImport("user32.dll")]
    private static extern bool GetClipCursor(out Rect rect);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
}
