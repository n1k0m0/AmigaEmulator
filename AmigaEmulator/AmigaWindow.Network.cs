using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Input;
using AmigaEmulator.Network;
using SharpPixels;

namespace AmigaEmulator;

public sealed partial class AmigaWindow
{
    private const int NetworkItemCount = 20;
    private AmigaNetServer _networkServer;
    private AmigaNetClient _networkClient;
    private bool _remoteSession, _networkBusy, _networkInputApplied;
    private volatile bool _networkDisposed;
    private int _networkOperation;
    private bool IsNetworkClient => _remoteSession;
    private bool IsNetworkHost => _networkServer?.IsRunning == true;
    private bool _networkRelay => string.Equals(_settings.NetworkTransportMode, "Relay", StringComparison.OrdinalIgnoreCase);
    private string NetworkStatus => _networkBusy ? "CONNECTING" : IsNetworkClient ? "CLIENT " + _networkClient.StatusText : IsNetworkHost ? "SERVER " + _networkServer.GetClientSnapshots().Count + " CLIENTS" : "OFFLINE";
    private readonly ConcurrentQueue<Action> _networkUiActions = new();
    private readonly ConcurrentQueue<(byte[] Pcm, bool LedEnabled)> _remoteAudio = new();
    private bool _networkAudioLedEnabled;
    private readonly object _remoteFrameLock = new();
    private uint[] _remotePixels;
    private bool _remoteFrameReady;
    private int _remoteBottomCrop, _selectedNetworkClient, _controlOwner;
    private byte _remoteMouseButtons;
    private AmigaJoystickButtons _remoteKeyboardJoystick;
    private readonly HashSet<byte> _localKeyboardKeys = new();
    private HashSet<byte> _appliedRemoteKeys = new();
    private readonly byte[] _networkAudioStaging = new byte[960 * 4];
    private int _networkAudioCount;
    private int _remoteInputConfiguration = -1;

    private List<AmigaNetClientSnapshot> NetworkClients => _networkServer?.GetClientSnapshots() ?? new();
    private AmigaNetClientSnapshot SelectedNetworkClient
    {
        get
        {
            var clients = NetworkClients;
            if (clients.Count == 0) return null;
            _selectedNetworkClient = Math.Clamp(_selectedNetworkClient, 0, clients.Count - 1);
            return clients[_selectedNetworkClient];
        }
    }
    private bool RemoteOwnsControl => _controlOwner != 0 && NetworkClients.Any(c => c.ClientId == _controlOwner);
    private bool RemoteOwnsMouse => IsNetworkHost && NetworkClients.Any(c => c.MouseEnabled);

    private void ChangeNetworkItem(int direction)
    {
        var client = SelectedNetworkClient;
        switch (_networkSelection)
        {
            case 0:
                if (IsNetworkHost || IsNetworkClient || _networkBusy) { _statusText = "DISCONNECT BEFORE CHANGING MODE"; return; }
                _settings.NetworkTransportMode = _networkRelay ? "Lan" : "Relay"; break;
            case 1: if (!_networkRelay) _settings.NetworkServerPort = Math.Clamp(_settings.NetworkServerPort + direction, 1, 65535); break;
            case 5:
                int count = NetworkClients.Count;
                if (count > 0) _selectedNetworkClient = (_selectedNetworkClient + direction + count) % count;
                break;
            case 6: _controlOwner = 0; if (client != null) _networkServer.SetClientPermission(client.ClientId, CycleEnum(client.Permission, direction)); break;
            case 7: _controlOwner = 0; if (client != null) _networkServer.SetClientKeyboardEnabled(client.ClientId, !client.KeyboardEnabled); break;
            case 11:
                if (_networkRelay) _settings.NetworkRelayPort = Math.Clamp(_settings.NetworkRelayPort + direction, 1, 65535);
                else _settings.NetworkClientPort = Math.Clamp(_settings.NetworkClientPort + direction, 1, 65535);
                break;
            case 13: _settings.NetworkRequestedRole = _settings.NetworkRequestedRole == "Observer" ? "Player" : "Observer"; break;
            case 15: ApplyVideoFilterMode(CycleEnum(_videoFilterMode, direction)); break;
            case 16: ApplyVideoUpscaleMode(CycleEnum(_videoUpscaleMode, direction)); break;
            case 17: _controlOwner = 0; if (client != null) _networkServer.SetClientMouseEnabled(client.ClientId, !client.MouseEnabled); break;
            case 18: ToggleRemoteControl(client); break;
        }
        AmigaEmulatorSettingsStore.Save(_settings);
    }

    private void ActivateNetworkItem()
    {
        switch (_networkSelection)
        {
            case 1:
                if (_networkRelay) EditNetworkText("RELAY PASSWORD", _settings.NetworkRelayPassword, true, v => _settings.NetworkRelayPassword = v);
                else EditNetworkPort("SERVER PORT", _settings.NetworkServerPort, v => _settings.NetworkServerPort = v);
                return;
            case 2: EditNetworkText("SERVER PASSWORD", _settings.NetworkServerPassword, true, v => _settings.NetworkServerPassword = v); return;
            case 3: EditNetworkText("CONNECTION ID", _settings.NetworkConnectionId, false, v => _settings.NetworkConnectionId = v); return;
            case 4: ToggleNetworkServer(); return;
            case 8:
                if (SelectedNetworkClient != null) _networkServer.KickClient(SelectedNetworkClient.ClientId);
                return;
            case 9: EditNetworkText("PLAYER NAME", _settings.NetworkPlayerName, false, v => _settings.NetworkPlayerName = v); return;
            case 10: EditNetworkText(_networkRelay ? "RELAY HOST" : "CLIENT HOST", _settings.NetworkClientHost, false, v => _settings.NetworkClientHost = v); return;
            case 11: EditNetworkPort(_networkRelay ? "RELAY PORT" : "CLIENT PORT", _networkRelay ? _settings.NetworkRelayPort : _settings.NetworkClientPort,
                v => { if (_networkRelay) _settings.NetworkRelayPort = v; else _settings.NetworkClientPort = v; }); return;
            case 12: EditNetworkText("CLIENT PASSWORD", _settings.NetworkClientPassword, true, v => _settings.NetworkClientPassword = v); return;
            case 14: ToggleNetworkClient(); return;
            case 19:
                _statusText = IsNetworkClient ? "TLS " + _networkClient.ServerCertificateFingerprint + " E2E " + _networkClient.RelaySessionFingerprint
                    : IsNetworkHost ? (_networkRelay ? "RELAY TLS " + _networkServer.RelayFingerprint + " E2E " + (SelectedNetworkClient?.SessionFingerprint ?? "NO CLIENT") : "HOST TLS " + AmigaNetTls.GetServerCertificateShortFingerprint()) : "TLS PINNING / RELAY ECDH P256 + AES GCM";
                return;
            default: ChangeNetworkItem(1); return;
        }
    }

    private void DrawNetworkOverlay(Graphics g)
    {
        var client = SelectedNetworkClient;
        DrawMenu(g, "NETWORK", "ENTER CHANGE/EDIT  LEFT/RIGHT ADJUST  ESC BACK", new[]
        {
            MenuLine("MODE", _networkRelay ? "RELAY" : "LAN"),
            MenuLine(_networkRelay ? "RELAY PASSWORD" : "SERVER PORT", _networkRelay ? PasswordStatus(_settings.NetworkRelayPassword) : _settings.NetworkServerPort.ToString()),
            MenuLine("SERVER PASSWORD", PasswordStatus(_settings.NetworkServerPassword)),
            MenuLine("CONNECTION ID", _settings.NetworkConnectionId),
            MenuLine("SERVER", IsNetworkHost ? "STOP" : "START"),
            MenuLine("SELECT CLIENT", client == null ? "NONE" : client.ClientId + " " + client.Name + " " + client.LatencyMilliseconds + " MS"),
            MenuLine("CLIENT RIGHT", client?.Permission.ToString().ToUpperInvariant() ?? "OBSERVER"),
            MenuLine("KEYBOARD RIGHT", client?.KeyboardEnabled == true ? "ON" : "OFF"),
            MenuLine("KICK CLIENT", client == null ? "NONE" : client.Name),
            MenuLine("PLAYER NAME", _settings.NetworkPlayerName),
            MenuLine(_networkRelay ? "RELAY HOST" : "CLIENT HOST", _settings.NetworkClientHost),
            MenuLine(_networkRelay ? "RELAY PORT" : "CLIENT PORT", (_networkRelay ? _settings.NetworkRelayPort : _settings.NetworkClientPort).ToString()),
            MenuLine("CLIENT PASSWORD", PasswordStatus(_settings.NetworkClientPassword)),
            MenuLine("CLIENT ROLE", _settings.NetworkRequestedRole.ToUpperInvariant()),
            MenuLine("CLIENT", IsNetworkClient ? "LEAVE" : "JOIN"),
            MenuLine("VIDEO FILTER", FormatVideoFilter(_videoFilterMode)),
            MenuLine("VIDEO UPSCALE", FormatVideoUpscale(_videoUpscaleMode)),
            MenuLine("MOUSE RIGHT", client?.MouseEnabled == true ? "ON" : "OFF"),
            MenuLine("CONTROL OWNER", RemoteOwnsControl ? "CLIENT " + _controlOwner : "HOST"),
            MenuLine("SECURITY", _networkRelay ? "TLS + ECDH P256 / AES GCM" : "TLS 1.2/1.3 + PINNING")
        }, _networkSelection);
    }

    private static string PasswordStatus(string value) => string.IsNullOrEmpty(value) ? "EMPTY" : "SET";

    private void EditNetworkText(string title, string initial, bool password, Action<string> apply)
    {
        using var dialog = new Form { Text = title, ClientSize = new Size(480, 130), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = Color.FromArgb(10, 13, 24), ForeColor = Color.FromArgb(255, 230, 150) };
        var label = new Label { Text = title, Left = 20, Top = 12, Width = 440 };
        var input = new TextBox { Text = initial ?? "", Left = 20, Top = 42, Width = 440, MaxLength = 256, UseSystemPasswordChar = password };
        var accept = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 282, Top = 88, Width = 82 };
        var cancel = new Button { Text = "CANCEL", DialogResult = DialogResult.Cancel, Left = 378, Top = 88, Width = 82 };
        dialog.Controls.AddRange(new Control[] { label, input, accept, cancel }); dialog.AcceptButton = accept; dialog.CancelButton = cancel;
        ReleaseHostMouseCapture();
        if (dialog.ShowDialog(this) == DialogResult.OK) { apply(password ? input.Text : input.Text.Trim()); AmigaEmulatorSettingsStore.Save(_settings); }
        UpdateHostMouseCapture();
    }

    private void EditNetworkPort(string title, int initial, Action<int> apply) => EditNetworkText(title, initial.ToString(), false,
        v => { if (int.TryParse(v, out int port) && port >= 1 && port <= 65535) apply(port); else _statusText = "PORT MUST BE 1 TO 65535"; });

    private void ToggleNetworkServer()
    {
        if (_networkBusy) return;
        if (IsNetworkHost) { _networkServer.Stop(); _controlOwner = 0; return; }
        if (IsNetworkClient) { _statusText = "LEAVE CLIENT SESSION FIRST"; return; }
        _networkServer?.Dispose();
        int operation = ++_networkOperation;
        bool relay = _networkRelay;
        string host = _settings.NetworkClientHost, connectionId = _settings.NetworkConnectionId, relayPassword = _settings.NetworkRelayPassword, password = _settings.NetworkServerPassword;
        int port = relay ? _settings.NetworkRelayPort : _settings.NetworkServerPort;
        var server = new AmigaNetServer(AmigaSystem.PresentationViewportWidth, AmigaSystem.PresentationViewportHeight);
        server.StatusChanged += s => PostNetwork(operation, () => _statusText = s);
        _networkServer = server; _networkBusy = true;
        Task.Run(() =>
        {
            try
            {
                if (relay) server.StartRelay(host, port, connectionId, relayPassword, password);
                else server.Start(port, password);
                PostNetwork(operation, () => PublishNetworkFrame());
            }
            catch (AmigaNetTlsException ex) { PostNetwork(operation, () => HandleNetworkPinChange(ex, ToggleNetworkServer)); }
            catch (Exception ex) { PostNetwork(operation, () => _statusText = ex.Message); }
            finally { PostNetwork(operation, () => _networkBusy = false); if (_networkDisposed) server.Dispose(); }
        });
    }

    private void ToggleNetworkClient()
    {
        if (_networkBusy) return;
        if (IsNetworkClient) { LeaveNetworkClient(); return; }
        if (IsNetworkHost) { _statusText = "STOP SERVER FIRST"; return; }
        _networkClient?.Dispose();
        int operation = ++_networkOperation;
        bool relay = _networkRelay;
        string host = _settings.NetworkClientHost, connectionId = _settings.NetworkConnectionId, relayPassword = _settings.NetworkRelayPassword, password = _settings.NetworkClientPassword, playerName = _settings.NetworkPlayerName;
        int port = relay ? _settings.NetworkRelayPort : _settings.NetworkClientPort;
        var client = new AmigaNetClient(); _networkClient = client;
        client.StatusChanged += s => PostNetwork(operation, () => _statusText = s);
        client.HostOverlayStatusChanged += s => PostNetwork(operation, () => { if (!string.IsNullOrEmpty(s)) _statusText = s; });
        client.FrameReceived += frame =>
        {
            if (_networkDisposed || !ReferenceEquals(client, _networkClient)) return;
            if (frame.Width != AmigaSystem.PresentationViewportWidth || frame.Height != AmigaSystem.PresentationViewportHeight) return;
            lock (_remoteFrameLock)
            {
                _remotePixels ??= new uint[frame.Width * frame.Height];
                Array.Copy(frame.Pixels, _remotePixels, _remotePixels.Length);
                _remoteBottomCrop = frame.BottomCrop; _remoteFrameReady = true;
            }
        };
        client.AudioReceivedWithFilterState += (pcm, count, ledEnabled) => { if (_networkDisposed || !ReferenceEquals(client, _networkClient)) return; while (_remoteAudio.Count >= 8) _remoteAudio.TryDequeue(out _); _remoteAudio.Enqueue((pcm, ledEnabled)); };
        _networkBusy = true;
        var role = _settings.NetworkRequestedRole == "Observer" ? AmigaNetClientRole.Observer : AmigaNetClientRole.Player;
        Task.Run(() =>
        {
            try
            {
                string status;
                bool connected = relay
                    ? client.ConnectRelay(host, port, connectionId, relayPassword, password, role, playerName, out status)
                    : client.Connect(host, port, password, role, playerName, out status);
                PostNetwork(operation, () =>
                {
                    _statusText = status;
                    if (connected && client.VideoWidth == AmigaSystem.PresentationViewportWidth && client.VideoHeight == AmigaSystem.PresentationViewportHeight)
                    {
                        ReleaseAllHostInput(); _localKeyboardKeys.Clear(); _remoteSession = true;
                        ResetAudioOutput(); ResetEmulationClock(); _overlayMode = OverlayMode.None; _overlayHistory.Clear(); UpdateHostMouseCapture();
                    }
                    else client.Disconnect();
                });
            }
            catch (AmigaNetTlsException ex) { PostNetwork(operation, () => HandleNetworkPinChange(ex, ToggleNetworkClient)); }
            catch (Exception ex) { PostNetwork(operation, () => _statusText = ex.Message); }
            finally { PostNetwork(operation, () => _networkBusy = false); if (_networkDisposed) client.Dispose(); }
        });
    }

    private void PostNetwork(int operation, Action action) => _networkUiActions.Enqueue(() => { if (operation == _networkOperation && !_networkDisposed) action(); });

    private void HandleNetworkPinChange(AmigaNetTlsException ex, Action retry)
    {
        _networkBusy = false;
        if (!ex.IsCertificateChanged) { _statusText = ex.Message; return; }
        var pin = ex.PinChange;
        ReleaseHostMouseCapture();
        if (MessageBox.Show(this, "Certificate changed for " + pin.Host + ":" + pin.Port + "\n\nPrevious: " + pin.OldFingerprint +
            "\nNew: " + pin.NewFingerprint + "\n\nTrust the new certificate and reconnect?", "NETWORK CERTIFICATE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            AmigaNetTls.ReplaceTrustedServerCertificate(pin.Host, pin.Port, pin.NewFingerprint); retry();
        }
        else _statusText = "CERTIFICATE CHANGE REJECTED";
    }

    private void ToggleRemoteControl(AmigaNetClientSnapshot selected)
    {
        if (selected == null) return;
        bool grant = _controlOwner != selected.ClientId;
        foreach (var other in NetworkClients)
        {
            _networkServer.SetClientPermission(other.ClientId, AmigaNetJoystickPermission.Observer);
            _networkServer.SetClientKeyboardEnabled(other.ClientId, false);
            _networkServer.SetClientMouseEnabled(other.ClientId, false);
        }
        _controlOwner = grant ? selected.ClientId : 0;
        ReleaseAllHostInput(); _localKeyboardKeys.Clear();
        if (grant)
        {
            _networkServer.SetClientPermission(selected.ClientId, AmigaNetJoystickPermission.Both);
            _networkServer.SetClientKeyboardEnabled(selected.ClientId, true);
            _networkServer.SetClientMouseEnabled(selected.ClientId, true);
        }
    }

    private void PublishNetworkFrame()
    {
        if (IsNetworkHost)
        {
            _networkServer.SetHostInputMode((byte)_inputPortMode);
            _networkServer.BroadcastVideoFrame(_system.PresentedFrameBuffer.Pixels,
                _system.PresentedFrameBuffer.Width, _system.PresentedFrameBuffer.Height, _presentationCropBottom);
        }
    }

    private bool UpdateNetwork()
    {
        while (_networkUiActions.TryDequeue(out var action)) if (!_networkDisposed) action();
        if (!IsNetworkClient)
        {
            if (IsNetworkHost || _appliedRemoteKeys.Count > 0 || _networkInputApplied) ApplyNetworkInput();
            if (IsNetworkHost) _networkServer.SetHostInputMode((byte)_inputPortMode);
            if (IsNetworkHost) UpdateHostMouseCapture();
            if (IsNetworkHost) _networkServer.SetHostOverlayStatus(_overlayMode == OverlayMode.None ? "" : "HOST MENU OPEN");
            return false;
        }
        if (!_networkClient.IsConnected) { LeaveNetworkClient(); return false; }
        SynchronizeNetworkInputConfiguration();
        _networkClient.PollLatency();
        UpdateHostMouseCapture();
        lock (_remoteFrameLock)
        {
            if (_remoteFrameReady)
            {
                Array.Copy(_remotePixels, Pixels, Pixels.Length); _remoteFrameReady = false;
                _presentationCropBottom = _remoteBottomCrop; ApplyPresentationSourceCrop(); _paintFrameReady = true;
            }
        }
        while (_remoteAudio.TryDequeue(out var audio))
        {
            if (!_audioEnabled) continue;
            byte[] pcm = audio.Pcm;
            for (int i = 0; i + 3 < pcm.Length; i += 4)
            {
                float left = BitConverter.ToInt16(pcm, i) / 32768f, right = BitConverter.ToInt16(pcm, i + 2) / 32768f;
                ApplyPaulaFilter(ref left, ref right, audio.LedEnabled); _audioOutput.Submit(left, right, _masterVolume);
            }
        }
        ResetEmulationClock(); return true;
    }

    private void ApplyNetworkInput()
    {
        _networkInputApplied = IsNetworkHost;
        var keys = IsNetworkHost ? _networkServer.GetAggregatedKeyboardKeys().Where(k => !IsEmulatorHotkey(k)) : Enumerable.Empty<Key>();
        var raw = new HashSet<byte>();
        foreach (Key key in keys) if (TryMapAmigaRawKey(key, out byte code)) raw.Add(code);
        foreach (byte previous in _appliedRemoteKeys) if (!raw.Contains(previous) && !_localKeyboardKeys.Contains(previous)) _system.Input.KeyboardKeyUp(previous);
        foreach (byte next in raw) if (!_appliedRemoteKeys.Contains(next)) _system.Input.KeyboardKeyDown(next);
        _appliedRemoteKeys = raw;
        for (int p = 0; p < 2; p++)
        {
            byte state = IsNetworkHost ? _networkServer.GetAggregatedJoystickState(p == 0 ? AmigaNetJoystickPermission.Port1 : AmigaNetJoystickPermission.Port2) : (byte)255;
            _system.Input.SetNetworkButtons((AmigaControlPort)p, (AmigaJoystickButtons)(~state & 0x7F));
        }
        if (IsNetworkHost && TryGetMousePort(out var port))
        {
            _networkServer.DrainMouseInput(out int dx, out int dy, out byte buttons);
            QueueBufferedMouseMotionFromNetwork(port, dx, dy);
            // Independent input source prevents a remote release from releasing host buttons.
            byte joy = _networkServer.GetAggregatedJoystickState(port == AmigaControlPort.Port0 ? AmigaNetJoystickPermission.Port1 : AmigaNetJoystickPermission.Port2);
            _system.Input.SetNetworkButtons(port, (AmigaJoystickButtons)((~joy & 0x7F) | buttons));
        }
        if (_controlOwner != 0 && !RemoteOwnsControl) _controlOwner = 0;
    }

    private void QueueBufferedMouseMotionFromNetwork(AmigaControlPort port, int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        _pendingMousePort = port; _pendingMouseDx += dx; _pendingMouseDy += dy;
    }

    private bool HandleNetworkKey(Key key, bool pressed)
    {
        if (RemoteOwnsControl) return true;
        if (!IsNetworkClient) return false;
        if (IsEmulatorHotkey(key)) return true;
        SynchronizeNetworkInputConfiguration();
        // Shared joystick keys mirror the host's mapping; permission chooses the actual host port.
        if (TryMapJoystickKeyForMode(key, (InputPortMode)(_remoteInputConfiguration & 255), out _, out var button))
        {
            if (pressed) _remoteKeyboardJoystick |= button; else _remoteKeyboardJoystick &= ~button;
            SendNetworkJoystick();
        }
        else _networkClient.SendKeyboardKey(key, pressed);
        return true;
    }

    private void SendNetworkJoystick()
    {
        if (!IsNetworkClient) return;
        SynchronizeNetworkInputConfiguration();
        var buttons = _remoteKeyboardJoystick;
        if ((Focused || ContainsFocus) && _overlayMode == OverlayMode.None && _gamepads.States.Count > 0) buttons |= _gamepads.States[0].Buttons;
        _networkClient.SendJoystickState((byte)~(byte)buttons, false);
    }

    private void SynchronizeNetworkInputConfiguration()
    {
        if (!IsNetworkClient) return;
        int configuration = _networkClient.InputConfiguration;
        if (_remoteInputConfiguration == configuration) return;
        // Also run before input callbacks: a grant can arrive between two UI updates.
        // Releasing after a fresh key-down would otherwise swallow that first input.
        ReleaseNetworkClientInput();
        _remoteInputConfiguration = configuration;
    }

    private void ReleaseNetworkClientInput()
    {
        _localKeyboardKeys.Clear();
        if (!IsNetworkClient) return;
        _remoteKeyboardJoystick = AmigaJoystickButtons.None; _remoteMouseButtons = 0;
        _networkClient.SendJoystickState(255, true); _networkClient.SendMouseInput(0, 0, 0);
        // Release all possible guest keys, including keys released while the menu has focus.
        foreach (Key key in Enum.GetValues<Key>()) if (!IsEmulatorHotkey(key)) _networkClient.SendKeyboardKey(key, false);
    }

    private void CaptureNetworkAudio(float left, float right)
    {
        if (!IsNetworkHost) { _networkAudioCount = 0; return; }
        bool ledEnabled = _system.CiaA.AudioFilterLedEnabled;
        // A chunk describes one LED state, including a switch inside a frame.
        if (_networkAudioCount > 0 && ledEnabled != _networkAudioLedEnabled)
        {
            _networkServer.BroadcastAudio(_networkAudioStaging, _networkAudioCount, _networkAudioLedEnabled);
            _networkAudioCount = 0;
        }
        _networkAudioLedEnabled = ledEnabled;
        short l = (short)Math.Clamp(left * 32767f, -32768, 32767), r = (short)Math.Clamp(right * 32767f, -32768, 32767);
        _networkAudioStaging[_networkAudioCount++] = (byte)l; _networkAudioStaging[_networkAudioCount++] = (byte)(l >> 8);
        _networkAudioStaging[_networkAudioCount++] = (byte)r; _networkAudioStaging[_networkAudioCount++] = (byte)(r >> 8);
        if (_networkAudioCount == _networkAudioStaging.Length) { _networkServer.BroadcastAudio(_networkAudioStaging, _networkAudioCount, _networkAudioLedEnabled); _networkAudioCount = 0; }
    }

    private void LeaveNetworkClient()
    {
        ReleaseNetworkClientInput(); _networkClient?.Disconnect(); _remoteSession = false;
        _remoteInputConfiguration = -1;
        while (_remoteAudio.TryDequeue(out _)) { }
        ResetAudioOutput(); _system.PresentedFrameBuffer.CopyTo(Pixels); ResetEmulationClock();
        UpdateAdaptivePresentationCrop(); ApplyPresentationSourceCrop(); _paintFrameReady = true;
    }

    private void DisposeNetwork()
    {
        _networkDisposed = true; ++_networkOperation; _networkClient?.Dispose(); _networkServer?.Dispose();
    }
}
