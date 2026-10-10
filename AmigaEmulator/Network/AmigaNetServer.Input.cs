using System;
using System.Collections.Generic;

namespace AmigaEmulator.Network;

public sealed partial class AmigaNetServer
{
    private PendingVideoFrame _lastBroadcastVideo;
    private byte _hostInputMode;

    public void SetHostInputMode(byte mode)
    {
        if (mode > 2) throw new ArgumentOutOfRangeException(nameof(mode));
        if (_hostInputMode == mode) return;
        _hostInputMode = mode;
        foreach (var client in GetClients())
            client.EnqueueControl(CreateMessage(AmigaNetMessageType.InputConfiguration, new[] { mode }));
    }

    public void SetClientMouseEnabled(int clientId, bool enabled)
    {
        ClientConnection client = FindClient(clientId);
        if (client == null) return;
        // A mouse has one owner; summing unrelated pointers would be unusable.
        if (enabled)
            foreach (ClientConnection other in GetClients())
                if (other.ClientId != clientId && other.MouseEnabled) SetClientMouseEnabled(other.ClientId, false);
        client.MouseEnabled = enabled;
        client.ClearMouseInput();
        client.EnqueueControl(CreateMessage(AmigaNetMessageType.PermissionUpdate,
            AmigaNetProtocol.CreatePermissionPayload(client.Permission, client.KeyboardEnabled, enabled)));
        BroadcastClientList();
    }

    public void DrainMouseInput(out int dx, out int dy, out byte buttons)
    {
        dx = dy = 0; buttons = 0;
        foreach (ClientConnection client in GetClients())
            if (client.IsConnected && client.MouseEnabled) client.DrainMouseInput(out dx, out dy, out buttons);
    }

    private sealed partial class ClientConnection
    {
        private int _mouseDx, _mouseDy, _lastSentBottomCrop;
        private byte _mouseButtons;
        private byte _appliedMouseButtons;
        private readonly Queue<(int Dx, int Dy, byte Buttons)> _mouseTransitions = new();

        public void AddMouseInput(int dx, int dy, byte buttons)
        {
            lock (_keyboardLock)
            {
                if (!MouseEnabled) return;
                _mouseDx = Math.Clamp(_mouseDx + dx, -4096, 4096);
                _mouseDy = Math.Clamp(_mouseDy + dy, -4096, 4096);
                if (buttons != _mouseButtons)
                {
                    if (_mouseTransitions.Count >= 64) { Close(); return; }
                    _mouseTransitions.Enqueue((_mouseDx, _mouseDy, buttons));
                    _mouseDx = _mouseDy = 0;
                }
                _mouseButtons = buttons;
            }
        }

        public void ClearMouseInput()
        {
            lock (_keyboardLock) { _mouseDx = _mouseDy = 0; _mouseButtons = _appliedMouseButtons = 0; _mouseTransitions.Clear(); }
        }

        public void DrainMouseInput(out int dx, out int dy, out byte buttons)
        {
            lock (_keyboardLock)
            {
                if (_mouseTransitions.TryDequeue(out var transition))
                {
                    dx = transition.Dx; dy = transition.Dy; buttons = _appliedMouseButtons = transition.Buttons;
                    return;
                }
                dx = _mouseDx; dy = _mouseDy; buttons = _appliedMouseButtons;
                _mouseDx = _mouseDy = 0;
            }
        }
    }
}
