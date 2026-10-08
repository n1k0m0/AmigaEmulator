using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AmigaEmulator.Core.Input
{
    /// <summary>Polls Windows joystick drivers, including generic USB HID gamepads.</summary>
    public sealed class WindowsGamepadInput
    {
        private readonly List<Device> _devices = new List<Device>();
        private readonly List<GamepadState> _states = new List<GamepadState>();
        private long _nextDiscovery;
        public IReadOnlyList<GamepadState> States => _states;

        public void Poll(bool rediscover = false)
        {
            _states.Clear();
            if (rediscover || Environment.TickCount64 >= _nextDiscovery)
            {
                _devices.Clear();
                _nextDiscovery = Environment.TickCount64 + 1000;
                uint count = Math.Min(joyGetNumDevs(), 16u);
                for (uint id = 0; id < count; id++)
                {
                    var position = NewPosition();
                    if (joyGetPosEx(id, ref position) == 0 &&
                        joyGetDevCapsW((UIntPtr)id, out JoyCaps caps, (uint)Marshal.SizeOf<JoyCaps>()) == 0)
                        _devices.Add(new Device { Id = id, Caps = caps });
                }
            }
            foreach (Device device in _devices)
            {
                var position = NewPosition();
                if (joyGetPosEx(device.Id, ref position) != 0)
                {
                    _nextDiscovery = 0; // Unplug releases inputs immediately.
                    continue;
                }
                JoyCaps caps = device.Caps;
                _states.Add(new GamepadState(caps.Name, MapButtons(position.X, position.Y,
                    caps.XMin, caps.XMax, caps.YMin, caps.YMax, position.Buttons,
                    (caps.Capabilities & 0x10) != 0 ? position.Pov : 0xFFFF)));
            }
        }

        /// <summary>Maps calibrated axes, eight-way POV and buttons to Amiga inputs.</summary>
        public static AmigaJoystickButtons MapButtons(uint x, uint y, uint xMin, uint xMax,
            uint yMin, uint yMax, uint buttons, uint pov)
        {
            var result = Axis(x, xMin, xMax, AmigaJoystickButtons.Left, AmigaJoystickButtons.Right)
                | Axis(y, yMin, yMax, AmigaJoystickButtons.Up, AmigaJoystickButtons.Down);
            if (pov < 36000)
            {
                int sector = (int)((pov + 2250) / 4500) % 8;
                if (sector == 7 || sector == 0 || sector == 1) result |= AmigaJoystickButtons.Up;
                if (sector >= 1 && sector <= 3) result |= AmigaJoystickButtons.Right;
                if (sector >= 3 && sector <= 5) result |= AmigaJoystickButtons.Down;
                if (sector >= 5 && sector <= 7) result |= AmigaJoystickButtons.Left;
            }
            // All four face buttons work as fire; shoulders supply extra buttons.
            if ((buttons & 0x0F) != 0) result |= AmigaJoystickButtons.Fire;
            if ((buttons & 0x10) != 0) result |= AmigaJoystickButtons.Second;
            if ((buttons & 0x20) != 0) result |= AmigaJoystickButtons.Third;
            return result;
        }

        private static AmigaJoystickButtons Axis(uint value, uint min, uint max,
            AmigaJoystickButtons negative, AmigaJoystickButtons positive)
        {
            if (max <= min) return AmigaJoystickButtons.None;
            double center = min + ((double)max - min) / 2;
            double deadZone = ((double)max - min) * 0.2;
            return value < center - deadZone ? negative :
                value > center + deadZone ? positive : AmigaJoystickButtons.None;
        }
        private static JoyPosition NewPosition() => new JoyPosition
        { Size = (uint)Marshal.SizeOf<JoyPosition>(), Flags = 0xFF, Pov = 0xFFFF };

        public readonly struct GamepadState
        {
            public GamepadState(string name, AmigaJoystickButtons buttons) { Name = name; Buttons = buttons; }
            public string Name { get; }
            public AmigaJoystickButtons Buttons { get; }
        }
        private sealed class Device { public uint Id; public JoyCaps Caps; }
        // Windows SDK joystickapi.h: JOYINFOEX and JOYCAPSW.
        [StructLayout(LayoutKind.Sequential)]
        private struct JoyPosition
        {
            public uint Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JoyCaps
        {
            public ushort Manufacturer, Product;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            public uint XMin, XMax, YMin, YMax, ZMin, ZMax, NumButtons, PeriodMin, PeriodMax;
            public uint RMin, RMax, UMin, UMax, VMin, VMax, Capabilities, MaxAxes, NumAxes, MaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegistryKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemDriver;
        }
        [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint joyGetNumDevs();
        [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint joyGetPosEx(uint id, ref JoyPosition position);
        [DllImport("winmm.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern uint joyGetDevCapsW(UIntPtr id, out JoyCaps caps, uint size);
    }
}
