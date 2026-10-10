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
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AmigaEmulator.Core;
using SharpPixels;
namespace AmigaEmulator.Network
{
    public sealed partial class AmigaNetClient : IDisposable
    {
        private const long LatencyPingIntervalTicks = TimeSpan.TicksPerSecond;
        private const long LatencyPingTimeoutTicks = TimeSpan.TicksPerSecond * 3;
        private const int ConnectionHandshakeTimeoutMilliseconds = 8000;
        private readonly object _sendLock = new object();
        private TcpClient _tcpClient;
        private Stream _stream;
        private CancellationTokenSource _shutdown;
        private Task _receiveTask;
        public event Action<byte[], int> AudioReceived;
        public event Action<byte[], int, bool> AudioReceivedWithFilterState;
        private byte _lastSentJoystickState = 0xFF;
        private byte[] _videoReferencePixels;
        private long _videoReferenceFrameId;
        private uint[] _videoDecodePixels;
        private long _bytesSent;
        private long _bytesReceived;
        private volatile bool _connected;
        private int _latencyMilliseconds = -1;
        private int _latencyPingPending;
        private long _lastLatencyPingTicks;
        private string _serverCertificateFingerprint = "UNKNOWN";
        private string _relayFingerprint = "UNKNOWN";
        private string _relaySessionFingerprint = "UNKNOWN";
        public event Action<AmigaNetVideoFrame> FrameReceived;
        public event Action<List<AmigaNetClientSnapshot>> ClientListReceived;
        public event Action<string> HostOverlayStatusChanged;
        public event Action<string> StatusChanged;
        public int ClientId { get; private set; }
        public int VideoWidth { get; private set; }
        public int VideoHeight { get; private set; }
        public int AudioSampleRate { get; private set; }
        public AmigaNetClientRole Role { get; private set; }
        public AmigaNetJoystickPermission Permission { get; private set; }
        public bool KeyboardEnabled { get; private set; }
        public bool MouseEnabled { get; private set; }
        public string StatusText { get; private set; } = "DISCONNECTED";
        public string ServerCertificateFingerprint
        {
            get { return _serverCertificateFingerprint; }
        }
        public AmigaNetTransportMode TransportMode { get; private set; } = AmigaNetTransportMode.Lan;
        public string RelayFingerprint
        {
            get { return _relayFingerprint; }
        }
        public string RelaySessionFingerprint
        {
            get { return _relaySessionFingerprint; }
        }
        public long BytesSent
        {
            get { return Interlocked.Read(ref _bytesSent); }
        }
        public long BytesReceived
        {
            get { return Interlocked.Read(ref _bytesReceived); }
        }
        public int LatencyMilliseconds
        {
            get { return Volatile.Read(ref _latencyMilliseconds); }
        }
        public bool IsConnected
        {
            get { return _connected; }
        }
        public bool Connect(string host, int port, string password, AmigaNetClientRole role, string name, out string status)
        {
            // Always start from a clean transport state. Reusing a half-closed stream can
            // otherwise leak old audio output or receive loops into the next connection.
            Disconnect();
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            status = string.Empty;
            try
            {
                TransportMode = AmigaNetTransportMode.Lan;
                _relayFingerprint = "UNKNOWN";
                _relaySessionFingerprint = "UNKNOWN";
                _tcpClient = new TcpClient();
                _tcpClient.NoDelay = true;
                Task connectTask = _tcpClient.ConnectAsync(host, port);
                if (!connectTask.Wait(3000))
                {
                    // Keep the UI responsive if a host address silently drops packets.
                    status = "CONNECT TIMEOUT";
                    Disconnect();
                    return false;
                }
                string tlsStatus;
                _stream = AmigaNetTls.AuthenticateClient(_tcpClient, host, port, out tlsStatus);
                if (!string.IsNullOrWhiteSpace(tlsStatus))
                {
                    StatusText = tlsStatus;
                    RaiseStatus(tlsStatus);
                }
                _serverCertificateFingerprint = AmigaNetTls.GetTrustedServerShortFingerprint(host, port);
                return FinishConnect(password, role, name, out status);
            }
            catch (AmigaNetTlsException ex)
            {
                Debug.WriteLine(ex);
                if (ex.IsCertificateChanged)
                {
                    Disconnect();
                    throw;
                }
                status = ex.Message;
                Disconnect();
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                status = "CONNECT FAILED";
                Disconnect();
                return false;
            }
        }
        public bool ConnectRelay(string relayHost, int relayPort, string connectionId, string relayPassword, string password, AmigaNetClientRole role, string name, out string status)
        {
            Disconnect();
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            status = string.Empty;
            try
            {
                TransportMode = AmigaNetTransportMode.Relay;
                _tcpClient = null;
                _stream = AmigaRelayClientConnector.Connect(
                    relayHost,
                    relayPort,
                    connectionId,
                    relayPassword,
                    out _relayFingerprint,
                    out _relaySessionFingerprint,
                    out status);
                if (_stream == null)
                {
                    Disconnect();
                    return false;
                }
                _serverCertificateFingerprint = "RELAY " + _relayFingerprint + " E2E " + _relaySessionFingerprint;
                return FinishConnect(password, role, name, out status);
            }
            catch (AmigaNetTlsException ex)
            {
                Debug.WriteLine(ex);
                if (ex.IsCertificateChanged)
                {
                    Disconnect();
                    throw;
                }
                status = ex.Message;
                Disconnect();
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                status = "RELAY CONNECT FAILED";
                Disconnect();
                return false;
            }
        }
        private bool FinishConnect(string password, AmigaNetClientRole role, string name, out string status)
        {
            status = string.Empty;
            try
            {
                ApplyStreamTimeouts(_stream, ConnectionHandshakeTimeoutMilliseconds);
                // ClientHello is the only message the server accepts before welcome/reject.
                AddBytesSent(AmigaNetProtocol.WriteMessage(_stream, new AmigaNetMessage
                {
                    Type = AmigaNetMessageType.ClientHello,
                    Timestamp = DateTime.UtcNow.Ticks,
                    Payload = AmigaNetProtocol.CreateClientHelloPayload(name, password, role)
                }));
                AmigaNetMessage response = AmigaNetProtocol.ReadMessage(_stream);
                AddBytesReceived(response != null ? response.WireLength : 0);
                if (response == null)
                {
                    // Socket closed before the host sent a protocol response.
                    status = "NO SERVER RESPONSE";
                    Disconnect();
                    return false;
                }
                if (response.Type == AmigaNetMessageType.ServerReject)
                {
                    // Surface the server's reason verbatim; it is already short overlay text.
                    status = AmigaNetProtocol.ReadTextPayload(response.Payload);
                    Disconnect();
                    return false;
                }
                if (response.Type != AmigaNetMessageType.ServerWelcome)
                {
                    // Any other first response means this is not a compatible AmigaNet host.
                    status = "BAD SERVER RESPONSE";
                    Disconnect();
                    return false;
                }
                AmigaNetProtocol.ReadServerWelcomePayload(
                    response.Payload,
                    out int clientId,
                    out int width,
                    out int height,
                    out int audioSampleRate,
                    out AmigaNetClientRole acceptedRole,
                    out AmigaNetJoystickPermission permission,
                    out bool keyboardEnabled,
                    out string welcomeStatus);
                ClientId = clientId;
                VideoWidth = width;
                VideoHeight = height;
                AudioSampleRate = audioSampleRate;
                Role = acceptedRole;
                Permission = permission;
                KeyboardEnabled = keyboardEnabled;
                StatusText = string.IsNullOrWhiteSpace(welcomeStatus) ? "CONNECTED" : welcomeStatus;
                _connected = true;
                ApplyStreamTimeouts(_stream, Timeout.Infinite);
                // Receiving is backgrounded so the UI/render thread can continue drawing
                // the last decoded frame and collecting joystick input.
                _shutdown = new CancellationTokenSource();
                _receiveTask = Task.Run(() => ReceiveLoop(_shutdown.Token));
                status = StatusText;
                RaiseStatus(status);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                status = TransportMode == AmigaNetTransportMode.Relay && IsTimeoutException(ex)
                    ? "RELAY HOST TIMEOUT"
                    : (TransportMode == AmigaNetTransportMode.Relay ? "RELAY HOST FAILED" : "CONNECT FAILED");
                Disconnect();
                return false;
            }
        }
        private static void ApplyStreamTimeouts(Stream stream, int timeoutMilliseconds)
        {
            if (stream == null || !stream.CanTimeout)
            {
                return;
            }
            stream.ReadTimeout = timeoutMilliseconds;
            stream.WriteTimeout = timeoutMilliseconds;
        }
        private static bool IsTimeoutException(Exception exception)
        {
            while (exception != null)
            {
                if (exception is TimeoutException)
                {
                    return true;
                }
                if (exception.Message != null &&
                    exception.Message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                if (exception is SocketException socketException &&
                    socketException.SocketErrorCode == SocketError.TimedOut)
                {
                    return true;
                }
                exception = exception.InnerException;
            }
            return false;
        }
        public void Disconnect()
        {
            CancellationTokenSource shutdown = _shutdown;
            Task receiveTask = _receiveTask;
            _shutdown = null;
            if (shutdown != null)
            {
                // Signal the receive loop before closing the socket; either path may win.
                shutdown.Cancel();
            }
            try
            {
                if (_stream != null)
                {
                    // Best-effort graceful leave. Socket close below is still the real
                    // guarantee if the peer is gone or the write fails.
                    AddBytesSent(AmigaNetProtocol.WriteMessage(_stream, new AmigaNetMessage
                    {
                        Type = AmigaNetMessageType.Disconnect,
                        Timestamp = DateTime.UtcNow.Ticks,
                        Payload = AmigaNetProtocol.CreateTextPayload("LEAVE")
                    }));
                }
            }
            catch
            {
            }
            try
            {
                if (_stream != null)
                {
                    _stream.Dispose();
                }
            }
            catch
            {
            }
            try
            {
                if (_tcpClient != null)
                {
                    _tcpClient.Close();
                }
            }
            catch
            {
            }
            try
            {
                if (receiveTask != null && (!Task.CurrentId.HasValue || Task.CurrentId.Value != receiveTask.Id))
                {
                    // Avoid deadlocking when Disconnect is called from inside ReceiveLoop
                    // after a server-initiated disconnect message.
                    receiveTask.Wait(250);
                }
            }
            catch
            {
            }
            if (shutdown != null)
            {
                shutdown.Dispose();
            }
            _receiveTask = null;
            _stream = null;
            _tcpClient = null;
            _connected = false;
            Volatile.Write(ref _latencyMilliseconds, -1);
            Interlocked.Exchange(ref _latencyPingPending, 0);
            Interlocked.Exchange(ref _lastLatencyPingTicks, 0);
            ClientId = 0;
            Permission = AmigaNetJoystickPermission.Observer;
            HostInputMode = 0;
            KeyboardEnabled = false;
            MouseEnabled = false;
            PublishInputConfiguration();
            _serverCertificateFingerprint = "UNKNOWN";
            _relayFingerprint = "UNKNOWN";
            _relaySessionFingerprint = "UNKNOWN";
            _videoReferencePixels = null;
            _videoReferenceFrameId = 0;
            _videoDecodePixels = null;
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            StatusText = "DISCONNECTED";
            // Clear stale host-menu popup text as soon as the session is gone.
            RaiseHostOverlayStatus(string.Empty);
            RaiseStatus(StatusText);
        }
        public void SendJoystickState(byte activeLowJoystickState, bool force)
        {
            activeLowJoystickState = (byte)(activeLowJoystickState | 0x80);
            if (!force && activeLowJoystickState == _lastSentJoystickState)
            {
                // Joystick polling happens every frame; suppress unchanged packets unless
                // the caller requests a periodic refresh.
                return;
            }
            _lastSentJoystickState = activeLowJoystickState;
            SendMessage(new AmigaNetMessage
            {
                Type = AmigaNetMessageType.InputState,
                Timestamp = DateTime.UtcNow.Ticks,
                Payload = AmigaNetProtocol.CreateInputStatePayload(activeLowJoystickState)
            });
        }
        public void SendKeyboardKey(Key key, bool pressed)
        {
            if (!KeyboardEnabled)
            {
                return;
            }
            SendMessage(new AmigaNetMessage
            {
                Type = AmigaNetMessageType.KeyboardInput,
                Timestamp = DateTime.UtcNow.Ticks,
                Payload = AmigaNetProtocol.CreateKeyboardInputPayload(key, pressed)
            });
        }
        public void PollLatency()
        {
            if (!_connected || _stream == null)
            {
                return;
            }
            long nowTicks = DateTime.UtcNow.Ticks;
            long lastPingTicks = Interlocked.Read(ref _lastLatencyPingTicks);
            long elapsedTicks = nowTicks - lastPingTicks;
            bool pending = Interlocked.CompareExchange(ref _latencyPingPending, 0, 0) != 0;
            if (pending)
            {
                if (elapsedTicks < LatencyPingTimeoutTicks)
                {
                    return;
                }
            }
            else if (lastPingTicks != 0 && elapsedTicks < LatencyPingIntervalTicks)
            {
                return;
            }
            Interlocked.Exchange(ref _lastLatencyPingTicks, nowTicks);
            Interlocked.Exchange(ref _latencyPingPending, 1);
            SendMessage(new AmigaNetMessage
            {
                Type = AmigaNetMessageType.Ping,
                Timestamp = nowTicks,
                Payload = new byte[0]
            });
        }
        public void Dispose()
        {
            Disconnect();
        }
        private void ReceiveLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    AmigaNetMessage message = AmigaNetProtocol.ReadMessage(_stream);
                    if (message == null)
                    {
                        // Null means the host closed the TCP stream or the socket failed.
                        break;
                    }
                    AddBytesReceived(message.WireLength);
                    HandleMessage(message);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                // The window notices IsConnected == false on its next update and returns
                // to local emulation cleanup.
                StatusText = "DISCONNECTED";
                _connected = false;
                RaiseHostOverlayStatus(string.Empty);
                RaiseStatus(StatusText);
            }
        }
        private void HandleMessage(AmigaNetMessage message)
        {
            switch (message.Type)
            {
                case AmigaNetMessageType.VideoFrame:
                    AmigaNetVideoFrame frame = AmigaNetProtocol.ReadVideoFramePayload(message.Payload, ref _videoReferencePixels, ref _videoReferenceFrameId, _videoDecodePixels);
                    if (frame != null)
                    {
                        _videoDecodePixels = frame.Pixels;
                        // The window owns frame storage because it must synchronize with
                        // the render thread and apply the user's local video filter.
                        Action<AmigaNetVideoFrame> handler = FrameReceived;
                        if (handler != null)
                        {
                            handler(frame);
                        }
                    }
                    break;
                case AmigaNetMessageType.AudioChunk:
                    if (AmigaNetProtocol.ReadAudioChunkPayload(message.Payload, out int sampleRate, out byte[] buffer, out int count))
                    {
                        if (sampleRate == AudioSampleRate)
                        {
                            AudioReceived?.Invoke(buffer, count);
                            AudioReceivedWithFilterState?.Invoke(buffer, count, (message.Flags & AmigaNetProtocol.AudioLedFilterEnabled) != 0);
                        }
                    }
                    break;
                case AmigaNetMessageType.ClientList:
                    List<AmigaNetClientSnapshot> clients = AmigaNetProtocol.ReadClientListPayload(message.Payload);
                    Action<List<AmigaNetClientSnapshot>> listHandler = ClientListReceived;
                    if (listHandler != null)
                    {
                        // Clients use this mainly for observer information and for keeping
                        // the network client list consistent with the host.
                        listHandler(clients);
                    }
                    break;
                case AmigaNetMessageType.PermissionUpdate:
                    // Permission is authoritative from the host. The local F10 joystick
                    // item is displayed read-only while connected.
                    AmigaNetProtocol.ReadPermissionPayload(message.Payload, out AmigaNetJoystickPermission permission, out bool keyboardEnabled, out bool mouseEnabled);
                    MouseEnabled = mouseEnabled;
                    _lastSentJoystickState = 0;
                    Permission = permission;
                    KeyboardEnabled = keyboardEnabled;
                    PublishInputConfiguration();
                    StatusText = "PERMISSION " + FormatPermission(Permission) + " KEYBOARD " + (KeyboardEnabled ? "ON" : "OFF");
                    RaiseStatus(StatusText);
                    break;
                case AmigaNetMessageType.InputConfiguration:
                    if (message.Payload.Length != 1 || message.Payload[0] > 2)
                        throw new InvalidDataException("Invalid host input configuration.");
                    HostInputMode = message.Payload[0];
                    PublishInputConfiguration();
                    break;
                case AmigaNetMessageType.HostOverlayStatus:
                    // Host menu state is persistent, not a short toast, so keep the last
                    // text until the server clears it.
                    RaiseHostOverlayStatus(AmigaNetProtocol.ReadTextPayload(message.Payload));
                    break;
                case AmigaNetMessageType.Ping:
                    // Preserve the host timestamp to make the round-trip calculation exact
                    // on the host side.
                    SendMessage(new AmigaNetMessage
                    {
                        Type = AmigaNetMessageType.Pong,
                        Timestamp = message.Timestamp,
                        Payload = message.Payload
                    });
                    break;
                case AmigaNetMessageType.Pong:
                    UpdateLatency(message.Timestamp);
                    break;
                case AmigaNetMessageType.Disconnect:
                    StatusText = AmigaNetProtocol.ReadTextPayload(message.Payload);
                    RaiseStatus(StatusText);
                    // Disconnect performs the full cleanup and sends a best-effort leave;
                    // that is harmless even when the server initiated the close.
                    Disconnect();
                    break;
            }
        }
        private void SendMessage(AmigaNetMessage message)
        {
            if (_stream == null || message == null)
            {
                return;
            }
            try
            {
                lock (_sendLock)
                {
                    // Input events and ping responses can originate from different
                    // threads. Serialize writes so protocol frames never interleave.
                    AddBytesSent(AmigaNetProtocol.WriteMessage(_stream, message));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                // Any write failure means the TCP stream is no longer usable.
                Disconnect();
            }
        }
        private void UpdateLatency(long timestamp)
        {
            long elapsedTicks = Math.Max(0, DateTime.UtcNow.Ticks - timestamp);
            Volatile.Write(ref _latencyMilliseconds, (int)Math.Min(9999, elapsedTicks / TimeSpan.TicksPerMillisecond));
            Interlocked.Exchange(ref _latencyPingPending, 0);
        }
        private void RaiseStatus(string status)
        {
            Action<string> handler = StatusChanged;
            if (handler != null)
            {
                handler(status);
            }
        }
        private void RaiseHostOverlayStatus(string status)
        {
            Action<string> handler = HostOverlayStatusChanged;
            if (handler != null)
            {
                handler(status);
            }
        }
        private void AddBytesSent(int byteCount)
        {
            if (byteCount > 0)
            {
                Interlocked.Add(ref _bytesSent, byteCount);
            }
        }
        private void AddBytesReceived(int byteCount)
        {
            if (byteCount > 0)
            {
                Interlocked.Add(ref _bytesReceived, byteCount);
            }
        }
        private static string FormatPermission(AmigaNetJoystickPermission permission)
        {
            switch (permission)
            {
                case AmigaNetJoystickPermission.Port1:
                    return "PORT 1";
                case AmigaNetJoystickPermission.Port2:
                    return "PORT 2";
                case AmigaNetJoystickPermission.Both:
                    return "BOTH";
                default:
                    return "OBSERVER";
            }
        }
    }
}
