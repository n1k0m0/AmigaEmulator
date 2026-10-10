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
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using SharpPixels;
namespace AmigaEmulator.Network
{
    public sealed partial class AmigaNetServer : IDisposable
    {
        private const int ConnectionHandshakeTimeoutMilliseconds = 8000;
        private readonly object _syncRoot = new object();
        private readonly List<ClientConnection> _clients = new List<ClientConnection>();
        private readonly HashSet<string> _bannedRemoteAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly int _videoWidth;
        private readonly int _videoHeight;
        private TcpListener _listener;
        private AmigaRelayServerListener _relayListener;
        private CancellationTokenSource _shutdown;
        private Task _acceptTask;
        private Task _relayAcceptTask;
        private string _password = string.Empty;
        private string _hostOverlayStatus = string.Empty;
        private int _nextClientId = 1;
        private long _frameId;
        private long _bytesSent;
        private long _bytesReceived;
        public event Action<string> StatusChanged;
        public AmigaNetServer(int videoWidth, int videoHeight)
        {
            _videoWidth = videoWidth;
            _videoHeight = videoHeight;
        }
        public bool IsRunning
        {
            get
            {
                lock (_syncRoot)
                {
                    return _listener != null || _relayListener != null;
                }
            }
        }
        public int Port { get; private set; }
        public bool IsRelayMode { get; private set; }
        public string RelayFingerprint
        {
            get
            {
                AmigaRelayServerListener relay = _relayListener;
                return relay == null ? "UNKNOWN" : relay.RelayFingerprint;
            }
        }
        public long BytesSent
        {
            get { return Interlocked.Read(ref _bytesSent); }
        }
        public long BytesReceived
        {
            get { return Interlocked.Read(ref _bytesReceived); }
        }
        public int GetAverageLatencyMilliseconds()
        {
            int total = 0;
            int count = 0;
            lock (_syncRoot)
            {
                for (int index = 0; index < _clients.Count; index++)
                {
                    int latency = _clients[index].LatencyMilliseconds;
                    if (latency >= 0)
                    {
                        total += latency;
                        count++;
                    }
                }
            }
            return count == 0 ? -1 : (int)Math.Round(total / (double)count);
        }
        public void Start(int port, string password)
        {
            // Restarting creates a fresh session. That also clears per-session kicks
            // exactly as the network menu promises.
            Stop();
            _password = password ?? string.Empty;
            _hostOverlayStatus = string.Empty;
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            lock (_syncRoot)
            {
                _bannedRemoteAddresses.Clear();
            }
            _shutdown = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            // Accepting is isolated from the render/emulation thread.
            _acceptTask = Task.Run(() => AcceptLoop(_shutdown.Token));
            RaiseStatus("TLS SERVER LISTENING " + Port);
        }
        public void StartRelay(string relayHost, int relayPort, string connectionId, string relayPassword, string password)
        {
            Stop();
            _password = password ?? string.Empty;
            _hostOverlayStatus = string.Empty;
            IsRelayMode = true;
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            lock (_syncRoot)
            {
                _bannedRemoteAddresses.Clear();
            }
            _shutdown = new CancellationTokenSource();
            var relay = new AmigaRelayServerListener();
            string status;
            try
            {
                if (!relay.Start(relayHost, relayPort, connectionId, relayPassword, out status))
                {
                    relay.Dispose();
                    IsRelayMode = false;
                    throw new IOException(status);
                }
            }
            catch
            {
                relay.Dispose();
                CancellationTokenSource failedShutdown = _shutdown;
                _shutdown = null;
                if (failedShutdown != null)
                {
                    failedShutdown.Cancel();
                    failedShutdown.Dispose();
                }
                IsRelayMode = false;
                throw;
            }
            lock (_syncRoot)
            {
                _relayListener = relay;
            }
            Port = relayPort;
            _relayAcceptTask = Task.Run(() => RelayAcceptLoop(_shutdown.Token));
            RaiseStatus(status);
        }
        public void Stop()
        {
            CancellationTokenSource shutdown;
            TcpListener listener;
            AmigaRelayServerListener relayListener;
            lock (_syncRoot)
            {
                // Null the listener under the lock first so IsRunning flips to false
                // before sockets start closing and callbacks begin to unwind.
                shutdown = _shutdown;
                listener = _listener;
                relayListener = _relayListener;
                _shutdown = null;
                _listener = null;
                _relayListener = null;
            }
            if (shutdown != null)
            {
                shutdown.Cancel();
            }
            if (listener != null)
            {
                try
                {
                    // Stop unblocks AcceptTcpClient on the accept loop.
                    listener.Stop();
                }
                catch
                {
                }
            }
            if (relayListener != null)
            {
                relayListener.Stop();
                relayListener.Dispose();
            }
            List<ClientConnection> clients;
            lock (_syncRoot)
            {
                clients = new List<ClientConnection>(_clients);
                _clients.Clear();
            }
            for (int index = 0; index < clients.Count; index++)
            {
                // ClientConnection.Close is idempotent; calling it here and from worker
                // loops is fine and simplifies shutdown paths.
                clients[index].Close();
            }
            if (_acceptTask != null)
            {
                try
                {
                    _acceptTask.Wait(250);
                }
                catch
                {
                }
                _acceptTask = null;
            }
            if (_relayAcceptTask != null)
            {
                try
                {
                    _relayAcceptTask.Wait(250);
                }
                catch
                {
                }
                _relayAcceptTask = null;
            }
            if (shutdown != null)
            {
                shutdown.Dispose();
            }
            Port = 0;
            IsRelayMode = false;
            RaiseStatus("SERVER STOPPED");
        }
        public void BroadcastVideoFrame(uint[] pixels, int width, int height, int bottomCrop = 0)
        {
            if (!IsRunning || pixels == null)
            {
                return;
            }
            List<ClientConnection> clients = GetClients();
            byte[] packedPixels = AmigaNetProtocol.CreatePackedVideoFrame(pixels, width, height);
            if (packedPixels.Length == 0)
            {
                return;
            }
            // Pack the host frame once, then let each client choose full/delta encoding
            // against its own last transmitted reference frame at send time.
            var frame = new PendingVideoFrame(packedPixels, width, height, Interlocked.Increment(ref _frameId), bottomCrop);
            _lastBroadcastVideo = frame;
            for (int index = 0; index < clients.Count; index++)
            {
                clients[index].EnqueueVideo(frame);
            }
        }
        public void BroadcastAudio(byte[] buffer, int count, bool ledFilterEnabled = false)
        {
            if (!IsRunning || buffer == null || count <= 0)
            {
                return;
            }
            List<ClientConnection> clients = GetClients();
            if (clients.Count == 0)
            {
                return;
            }
            // Like video, audio payloads are serialized once and shared between client
            // queues. Per-client backpressure is handled inside ClientConnection.
            byte[] payload = AmigaNetProtocol.CreateAudioChunkPayload(buffer, count, AmigaNetProtocol.DefaultAudioSampleRate);
            var message = CreateMessage(AmigaNetMessageType.AudioChunk, payload);
            message.Flags = ledFilterEnabled ? AmigaNetProtocol.AudioLedFilterEnabled : (ushort)0;
            for (int index = 0; index < clients.Count; index++)
            {
                clients[index].EnqueueAudio(message);
            }
        }
        public void SetHostOverlayStatus(string status)
        {
            status = status ?? string.Empty;
            lock (_syncRoot)
            {
                if (string.Equals(_hostOverlayStatus, status, StringComparison.Ordinal))
                {
                    // Avoid spamming the control queue every render tick while the host
                    // remains in the same menu.
                    return;
                }
                _hostOverlayStatus = status;
            }
            var message = CreateMessage(AmigaNetMessageType.HostOverlayStatus, AmigaNetProtocol.CreateTextPayload(status));
            List<ClientConnection> clients = GetClients();
            for (int index = 0; index < clients.Count; index++)
            {
                clients[index].EnqueueControl(message);
            }
        }
        public List<AmigaNetClientSnapshot> GetClientSnapshots()
        {
            var snapshots = new List<AmigaNetClientSnapshot>();
            lock (_syncRoot)
            {
                for (int index = 0; index < _clients.Count; index++)
                {
                    snapshots.Add(_clients[index].CreateSnapshot());
                }
            }
            return snapshots;
        }
        public void SetClientPermission(int clientId, AmigaNetJoystickPermission permission)
        {
            ClientConnection client = FindClient(clientId);
            if (client == null)
            {
                return;
            }
            // The changed client receives a dedicated permission update, while all
            // clients receive the refreshed list so observers see the new state too.
            client.JoystickState = 0xFF;
            client.Permission = permission;
            client.EnqueueControl(CreateMessage(AmigaNetMessageType.PermissionUpdate, AmigaNetProtocol.CreatePermissionPayload(permission, client.KeyboardEnabled, client.MouseEnabled)));
            BroadcastClientList();
            RaiseStatus("CLIENT " + clientId + " " + FormatPermission(permission));
        }
        public void SetClientKeyboardEnabled(int clientId, bool enabled)
        {
            ClientConnection client = FindClient(clientId);
            if (client == null)
            {
                return;
            }
            client.KeyboardEnabled = enabled;
            if (!enabled)
            {
                client.ClearKeyboardState();
            }
            client.EnqueueControl(CreateMessage(AmigaNetMessageType.PermissionUpdate, AmigaNetProtocol.CreatePermissionPayload(client.Permission, client.KeyboardEnabled, client.MouseEnabled)));
            BroadcastClientList();
            RaiseStatus("CLIENT " + clientId + " KEYBOARD " + (enabled ? "ON" : "OFF"));
        }
        public void CycleClientPermission(int clientId)
        {
            ClientConnection client = FindClient(clientId);
            if (client == null)
            {
                return;
            }
            SetClientPermission(clientId, GetNextPermission(client.Permission));
        }
        public void KickClient(int clientId)
        {
            ClientConnection client = FindClient(clientId);
            if (client == null)
            {
                return;
            }
            // Kicks are session-local bans by remote IP. A server restart clears the set.
            AddBannedRemoteAddress(client.RemoteAddress);
            client.EnqueueControl(CreateMessage(AmigaNetMessageType.Disconnect, AmigaNetProtocol.CreateTextPayload("KICKED BY HOST")));
            client.Close();
            RemoveClient(client);
            RaiseStatus("CLIENT " + clientId + " KICKED");
        }
        public byte GetAggregatedJoystickState(AmigaNetJoystickPermission portPermission)
        {
            byte state = 0xFF;
            lock (_syncRoot)
            {
                for (int index = 0; index < _clients.Count; index++)
                {
                    ClientConnection client = _clients[index];
                    if (!client.IsConnected || !PermissionIncludes(client.Permission, portPermission))
                    {
                        continue;
                    }
                    // Active-low joystick bits combine with AND: any client pulling a
                    // direction/fire line low makes it visible to the emulated CIA.
                    state = (byte)(state & client.JoystickState);
                }
            }
            return (byte)(state | 0x80);
        }
        public HashSet<Key> GetAggregatedKeyboardKeys()
        {
            var keys = new HashSet<Key>();
            lock (_syncRoot)
            {
                for (int index = 0; index < _clients.Count; index++)
                {
                    ClientConnection client = _clients[index];
                    if (!client.IsConnected || !client.KeyboardEnabled)
                    {
                        continue;
                    }
                    List<Key> clientKeys = client.GetPressedKeyboardKeys();
                    for (int keyIndex = 0; keyIndex < clientKeys.Count; keyIndex++)
                    {
                        keys.Add(clientKeys[keyIndex]);
                    }
                }
            }
            return keys;
        }
        public void Dispose()
        {
            Stop();
        }
        private void AcceptLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpListener listener;
                    lock (_syncRoot)
                    {
                        listener = _listener;
                    }
                    if (listener == null)
                    {
                        return;
                    }
                    TcpClient tcpClient = listener.AcceptTcpClient();
                    // The stream carries interactive input and frame/audio data. Disable
                    // Nagle so tiny control packets are not delayed behind batching.
                    tcpClient.NoDelay = true;
                    // Each handshake runs independently; a slow or malicious client
                    // should not prevent the listener from accepting the next socket.
                    Task.Run(() => HandleAcceptedClient(tcpClient, cancellationToken));
                }
                catch (SocketException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        RaiseStatus("SERVER ACCEPT FAILED");
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    RaiseStatus("SERVER ACCEPT FAILED");
                }
            }
        }
        private void RelayAcceptLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    AmigaRelayServerListener relay;
                    lock (_syncRoot)
                    {
                        relay = _relayListener;
                    }
                    if (relay == null)
                    {
                        return;
                    }
                    AmigaRelayAcceptedClient accepted = relay.AcceptClient(cancellationToken);
                    if (accepted == null)
                    {
                        continue;
                    }
                    Task.Run(() => HandleAcceptedConnection(
                        null,
                        accepted.Stream,
                        accepted.RemoteAddress,
                        accepted.RemoteEndpoint,
                        accepted.Dispose,
                        cancellationToken, accepted.SessionFingerprint));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        RaiseStatus("RELAY ACCEPT FAILED");
                    }
                }
            }
        }
        private void HandleAcceptedClient(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            try
            {
                Stream stream = AmigaNetTls.AuthenticateServer(tcpClient);
                HandleAcceptedConnection(
                    tcpClient,
                    stream,
                    FormatRemoteAddress(tcpClient),
                    FormatRemoteEndpoint(tcpClient),
                    null,
                    cancellationToken);
            }
            catch (AuthenticationException ex)
            {
                Debug.WriteLine(ex);
                RaiseStatus("TLS FAILED " + FormatRemoteEndpoint(tcpClient));
                try
                {
                    tcpClient.Close();
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                try
                {
                    tcpClient.Close();
                }
                catch
                {
                }
            }
        }
        private void HandleAcceptedConnection(TcpClient tcpClient, Stream stream, string remoteAddress, string remoteEndpoint, Action closeAction, CancellationToken cancellationToken, string sessionFingerprint = "UNKNOWN")
        {
            ClientConnection client = null;
            try
            {
                ApplyStreamTimeouts(stream, ConnectionHandshakeTimeoutMilliseconds);
                AmigaNetMessage hello = AmigaNetProtocol.ReadMessage(stream);
                AddBytesReceived(hello != null ? hello.WireLength : 0);
                if (hello == null || hello.Type != AmigaNetMessageType.ClientHello)
                {
                    // The protocol is strict at the handshake boundary. Unknown first
                    // messages are rejected before a ClientConnection is created.
                    SendReject(stream, "BAD HELLO");
                    CloseRejectedConnection(tcpClient, stream, closeAction);
                    return;
                }
                AmigaNetProtocol.ReadClientHelloPayload(
                    hello.Payload,
                    out int version,
                    out string name,
                    out string password,
                    out AmigaNetClientRole role);
                if (version != AmigaNetProtocol.Version)
                {
                    // Version is checked before password or role so incompatible clients
                    // get a deterministic error.
                    SendReject(stream, "PROTOCOL MISMATCH");
                    CloseRejectedConnection(tcpClient, stream, closeAction);
                    return;
                }
                if (IsRemoteAddressBanned(remoteAddress))
                {
                    // Session bans are keyed by IP address because a kicked client may
                    // reconnect with a new TCP port immediately.
                    RaiseStatus("BANNED CLIENT " + remoteAddress);
                    SendReject(stream, "KICKED FROM SESSION");
                    CloseRejectedConnection(tcpClient, stream, closeAction);
                    return;
                }
                if (!string.Equals(_password ?? string.Empty, password ?? string.Empty, StringComparison.Ordinal))
                {
                    // A wrong password is surfaced to the host overlay so the server
                    // operator can see rejected connection attempts.
                    RaiseStatus("BAD PASSWORD " + remoteEndpoint);
                    SendReject(stream, "PASSWORD REJECTED");
                    CloseRejectedConnection(tcpClient, stream, closeAction);
                    return;
                }
                int clientId;
                lock (_syncRoot)
                {
                    clientId = _nextClientId++;
                }
                AmigaNetJoystickPermission permission = role == AmigaNetClientRole.Observer
                    ? AmigaNetJoystickPermission.Observer
                    : AmigaNetJoystickPermission.Observer;
                // Even "Player" joins as observer first. The host grants the actual
                // joystick port explicitly from the client list.
                client = new ClientConnection(this, tcpClient, stream, remoteAddress, remoteEndpoint, closeAction, clientId, name, role, permission) { SessionFingerprint = sessionFingerprint };
                AddBytesSent(AmigaNetProtocol.WriteMessage(stream, CreateMessage(
                    AmigaNetMessageType.ServerWelcome,
                    AmigaNetProtocol.CreateServerWelcomePayload(clientId, _videoWidth, _videoHeight, AmigaNetProtocol.DefaultAudioSampleRate, role, permission, client.KeyboardEnabled, IsRelayMode ? "RELAY CONNECTED E2E" : "TLS CONNECTED"))));
                ApplyStreamTimeouts(stream, Timeout.Infinite);
                // Add the client only after welcome was sent, so the UI never shows a
                // half-handshaken socket.
                AddClient(client);
                client.Start(cancellationToken);
                if (_lastBroadcastVideo != null) client.EnqueueVideo(_lastBroadcastVideo);
                client.EnqueueControl(CreateMessage(AmigaNetMessageType.PermissionUpdate, AmigaNetProtocol.CreatePermissionPayload(client.Permission, client.KeyboardEnabled, client.MouseEnabled)));
                RaiseStatus("CLIENT " + clientId + " JOINED");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                if (client != null)
                {
                    client.Close();
                    RemoveClient(client);
                }
                else
                {
                    CloseRejectedConnection(tcpClient, stream, closeAction);
                }
            }
        }
        private static void CloseRejectedConnection(TcpClient tcpClient, Stream stream, Action closeAction)
        {
            try
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
            }
            catch
            {
            }
            try
            {
                if (tcpClient != null)
                {
                    tcpClient.Close();
                }
            }
            catch
            {
            }
            if (closeAction != null)
            {
                try
                {
                    closeAction();
                }
                catch
                {
                }
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
        private void HandleClientMessage(ClientConnection client, AmigaNetMessage message)
        {
            if (client == null || message == null)
            {
                return;
            }
            switch (message.Type)
            {
                case AmigaNetMessageType.InputState:
                    // Store only the latest input. The emulator thread samples it once
                    // per render update and combines all granted clients there.
                    if (client.Permission != AmigaNetJoystickPermission.Observer) client.JoystickState = AmigaNetProtocol.ReadInputStatePayload(message.Payload);
                    break;
                case AmigaNetMessageType.MouseInput:
                    if (client.MouseEnabled && AmigaNetProtocol.ReadMouseInputPayload(message.Payload, out int dx, out int dy, out byte buttons)) client.AddMouseInput(dx, dy, buttons);
                    break;
                case AmigaNetMessageType.KeyboardInput:
                    if (client.KeyboardEnabled && AmigaNetProtocol.ReadKeyboardInputPayload(message.Payload, out Key key, out bool pressed))
                    {
                        client.SetKeyboardKeyState(key, pressed);
                    }
                    break;
                case AmigaNetMessageType.Ping:
                    // Echo the payload and a timestamp-preserving pong for round-trip
                    // measurements on the client side.
                    client.EnqueueControl(new AmigaNetMessage
                    {
                        Type = AmigaNetMessageType.Pong,
                        Timestamp = message.Timestamp,
                        Payload = message.Payload
                    });
                    break;
                case AmigaNetMessageType.Pong:
                    client.UpdatePong(message.Timestamp);
                    BroadcastClientList();
                    break;
                case AmigaNetMessageType.Disconnect:
                    // A client-initiated leave is normal, so simply remove it.
                    client.Close();
                    RemoveClient(client);
                    break;
            }
        }
        private void AddClient(ClientConnection client)
        {
            lock (_syncRoot)
            {
                if (_clients.Count >= 32) throw new InvalidDataException("Server client limit reached.");
                _clients.Add(client);
            }
            client.EnqueueControl(CreateMessage(AmigaNetMessageType.InputConfiguration, new[] { _hostInputMode }));
            BroadcastClientList();
            string hostOverlayStatus;
            lock (_syncRoot)
            {
                hostOverlayStatus = _hostOverlayStatus;
            }
            if (!string.IsNullOrEmpty(hostOverlayStatus))
            {
                // New clients should immediately see why the remote picture might be
                // frozen if the host is already sitting in a menu.
                client.EnqueueControl(CreateMessage(AmigaNetMessageType.HostOverlayStatus, AmigaNetProtocol.CreateTextPayload(hostOverlayStatus)));
            }
        }
        private void RemoveClient(ClientConnection client)
        {
            bool removed = false;
            lock (_syncRoot)
            {
                removed = _clients.Remove(client);
            }
            if (removed)
            {
                client.Close();
                BroadcastClientList();
                RaiseStatus("CLIENT " + client.ClientId + " LEFT");
            }
        }
        private ClientConnection FindClient(int clientId)
        {
            lock (_syncRoot)
            {
                for (int index = 0; index < _clients.Count; index++)
                {
                    if (_clients[index].ClientId == clientId)
                    {
                        return _clients[index];
                    }
                }
            }
            return null;
        }
        private void AddBannedRemoteAddress(string remoteAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteAddress) || string.Equals(remoteAddress, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            lock (_syncRoot)
            {
                _bannedRemoteAddresses.Add(remoteAddress);
            }
        }
        private bool IsRemoteAddressBanned(string remoteAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteAddress))
            {
                return false;
            }
            lock (_syncRoot)
            {
                return _bannedRemoteAddresses.Contains(remoteAddress);
            }
        }
        private List<ClientConnection> GetClients()
        {
            lock (_syncRoot)
            {
                return new List<ClientConnection>(_clients);
            }
        }
        private void BroadcastClientList()
        {
            List<AmigaNetClientSnapshot> snapshots = GetClientSnapshots();
            var message = CreateMessage(AmigaNetMessageType.ClientList, AmigaNetProtocol.CreateClientListPayload(snapshots));
            List<ClientConnection> clients = GetClients();
            for (int index = 0; index < clients.Count; index++)
            {
                clients[index].EnqueueControl(message);
            }
        }
        private static bool PermissionIncludes(AmigaNetJoystickPermission permission, AmigaNetJoystickPermission portPermission)
        {
            if (portPermission == AmigaNetJoystickPermission.Port1)
            {
                return permission == AmigaNetJoystickPermission.Port1 || permission == AmigaNetJoystickPermission.Both;
            }
            if (portPermission == AmigaNetJoystickPermission.Port2)
            {
                return permission == AmigaNetJoystickPermission.Port2 || permission == AmigaNetJoystickPermission.Both;
            }
            return false;
        }
        private static AmigaNetJoystickPermission GetNextPermission(AmigaNetJoystickPermission permission)
        {
            switch (permission)
            {
                case AmigaNetJoystickPermission.Observer:
                    return AmigaNetJoystickPermission.Port1;
                case AmigaNetJoystickPermission.Port1:
                    return AmigaNetJoystickPermission.Port2;
                case AmigaNetJoystickPermission.Port2:
                    return AmigaNetJoystickPermission.Both;
                default:
                    return AmigaNetJoystickPermission.Observer;
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
        private void SendReject(Stream stream, string reason)
        {
            AddBytesSent(AmigaNetProtocol.WriteMessage(stream, CreateMessage(AmigaNetMessageType.ServerReject, AmigaNetProtocol.CreateTextPayload(reason))));
        }
        private static string FormatRemoteEndpoint(TcpClient tcpClient)
        {
            try
            {
                return tcpClient.Client.RemoteEndPoint == null ? "UNKNOWN" : tcpClient.Client.RemoteEndPoint.ToString();
            }
            catch
            {
                return "UNKNOWN";
            }
        }
        private static string FormatRemoteAddress(TcpClient tcpClient)
        {
            try
            {
                IPEndPoint endpoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
                return endpoint == null || endpoint.Address == null ? "UNKNOWN" : endpoint.Address.ToString();
            }
            catch
            {
                return "UNKNOWN";
            }
        }
        private static AmigaNetMessage CreateMessage(AmigaNetMessageType type, byte[] payload)
        {
            return new AmigaNetMessage
            {
                Type = type,
                Timestamp = DateTime.UtcNow.Ticks,
                Payload = payload
            };
        }
        private void RaiseStatus(string status)
        {
            Action<string> handler = StatusChanged;
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
        private sealed class PendingVideoFrame
        {
            public PendingVideoFrame(byte[] packedPixels, int width, int height, long frameId, int bottomCrop)
            {
                BottomCrop = bottomCrop;
                PackedPixels = packedPixels;
                Width = width;
                Height = height;
                FrameId = frameId;
            }
            public int BottomCrop { get; }
            public byte[] PackedPixels { get; private set; }
            public int Width { get; private set; }
            public int Height { get; private set; }
            public long FrameId { get; private set; }
        }
        private sealed partial class ClientConnection
        {
            private const int MaxAudioQueueLength = 8;
            private const int MaxDeltaFramesBeforeKeyFrame = 120;
            private const long LatencyPingIntervalTicks = TimeSpan.TicksPerSecond;
            private const long LatencyPingTimeoutTicks = TimeSpan.TicksPerSecond * 3;
            private readonly AmigaNetServer _server;
            private readonly TcpClient _tcpClient;
            private readonly Stream _stream;
            private readonly Action _closeAction;
            private readonly object _sendLock = new object();
            private readonly object _keyboardLock = new object();
            private readonly HashSet<Key> _pressedKeyboardKeys = new HashSet<Key>();
            private readonly Queue<AmigaNetMessage> _controlQueue = new Queue<AmigaNetMessage>();
            private readonly Queue<AmigaNetMessage> _audioQueue = new Queue<AmigaNetMessage>();
            private readonly AutoResetEvent _sendSignal = new AutoResetEvent(false);
            private PendingVideoFrame _latestVideo;
            private Task _receiveTask;
            private Task _sendTask;
            private volatile bool _closed;
            private int _latencyMilliseconds = -1;
            private bool _latencyPingPending;
            private long _lastLatencyPingTicks;
            private byte[] _lastSentVideoPixels;
            private long _lastSentVideoFrameId;
            private int _deltaFramesSinceKeyFrame;
            public ClientConnection(
                AmigaNetServer server,
                TcpClient tcpClient,
                Stream stream,
                string remoteAddress,
                string remoteEndpoint,
                Action closeAction,
                int clientId,
                string name,
                AmigaNetClientRole role,
                AmigaNetJoystickPermission permission)
            {
                _server = server;
                _tcpClient = tcpClient;
                _stream = stream;
                _closeAction = closeAction;
                ClientId = clientId;
                Name = string.IsNullOrWhiteSpace(name) ? "player" : name.Trim();
                RemoteAddress = string.IsNullOrWhiteSpace(remoteAddress) ? "UNKNOWN" : remoteAddress;
                RemoteEndpoint = string.IsNullOrWhiteSpace(remoteEndpoint) ? RemoteAddress : remoteEndpoint;
                Role = role;
                Permission = permission;
                JoystickState = 0xFF;
            }
            public int ClientId { get; private set; }
            public string Name { get; private set; }
            public string RemoteAddress { get; private set; }
            public string RemoteEndpoint { get; private set; }
            public AmigaNetClientRole Role { get; private set; }
            public AmigaNetJoystickPermission Permission { get; set; }
            public bool KeyboardEnabled { get; set; }
            public bool MouseEnabled { get; set; }
            public string SessionFingerprint { get; set; } = "UNKNOWN";
            public byte JoystickState { get; set; }
            public int LatencyMilliseconds
            {
                get { return Volatile.Read(ref _latencyMilliseconds); }
            }
            public bool IsConnected
            {
                get { return !_closed && (_tcpClient == null || _tcpClient.Connected); }
            }
            public void Start(CancellationToken parentCancellation)
            {
                _sendTask = Task.Run(() => SendLoop(parentCancellation));
                _receiveTask = Task.Run(() => ReceiveLoop(parentCancellation));
            }
            public void EnqueueControl(AmigaNetMessage message)
            {
                if (_closed || message == null)
                {
                    return;
                }
                lock (_sendLock)
                {
                    // Control messages carry permissions, disconnects, and client lists;
                    // never drop them unless the whole socket is closed.
                    if (_controlQueue.Count >= 256) { Close(); return; }
                    _controlQueue.Enqueue(message);
                }
                _sendSignal.Set();
            }
            public void EnqueueAudio(AmigaNetMessage message)
            {
                if (_closed || message == null)
                {
                    return;
                }
                lock (_sendLock)
                {
                    while (_audioQueue.Count >= MaxAudioQueueLength)
                    {
                        // Audio is time-sensitive. Dropping old chunks is less harmful
                        // than letting a slow client drift seconds behind the host.
                        _audioQueue.Dequeue();
                    }
                    _audioQueue.Enqueue(message);
                }
                _sendSignal.Set();
            }
            public void EnqueueVideo(PendingVideoFrame frame)
            {
                if (_closed || frame == null)
                {
                    return;
                }
                lock (_sendLock)
                {
                    // Only keep the newest frame. This is the main protection that keeps
                    // one slow client from building an ever-growing video backlog.
                    _latestVideo = frame;
                }
                _sendSignal.Set();
            }
            public AmigaNetClientSnapshot CreateSnapshot()
            {
                return new AmigaNetClientSnapshot
                {
                    ClientId = ClientId,
                    Name = Name,
                    RemoteAddress = RemoteAddress,
                    RemoteEndpoint = RemoteEndpoint,
                    Role = Role,
                    Permission = Permission,
                    KeyboardEnabled = KeyboardEnabled,
                    MouseEnabled = MouseEnabled,
                    SessionFingerprint = SessionFingerprint,
                    JoystickState = JoystickState,
                    LatencyMilliseconds = LatencyMilliseconds,
                    Connected = IsConnected
                };
            }
            public void SetKeyboardKeyState(Key key, bool pressed)
            {
                lock (_keyboardLock)
                {
                    if (!KeyboardEnabled) return;
                    if (pressed)
                    {
                        _pressedKeyboardKeys.Add(key);
                    }
                    else
                    {
                        _pressedKeyboardKeys.Remove(key);
                    }
                }
            }
            public void ClearKeyboardState()
            {
                lock (_keyboardLock)
                {
                    _pressedKeyboardKeys.Clear();
                }
            }
            public List<Key> GetPressedKeyboardKeys()
            {
                lock (_keyboardLock)
                {
                    return new List<Key>(_pressedKeyboardKeys);
                }
            }
            public void UpdatePong(long timestamp)
            {
                long elapsedTicks = Math.Max(0, DateTime.UtcNow.Ticks - timestamp);
                Volatile.Write(ref _latencyMilliseconds, (int)Math.Min(9999, elapsedTicks / TimeSpan.TicksPerMillisecond));
                lock (_sendLock)
                {
                    _latencyPingPending = false;
                }
            }
            public void Close()
            {
                if (_closed)
                {
                    return;
                }
                _closed = true;
                // Release all emulated joystick lines immediately when a client leaves.
                JoystickState = 0xFF;
                ClearKeyboardState();
                _sendSignal.Set();
                try
                {
                    _stream.Dispose();
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
                if (_closeAction != null)
                {
                    try
                    {
                        _closeAction();
                    }
                    catch
                    {
                    }
                }
                _sendSignal.Dispose();
            }
            private void ReceiveLoop(CancellationToken cancellationToken)
            {
                try
                {
                    Stream stream = _stream;
                    while (!_closed && !cancellationToken.IsCancellationRequested)
                    {
                        AmigaNetMessage message = AmigaNetProtocol.ReadMessage(stream);
                        if (message == null)
                        {
                            // Null means EOF/closed stream. The finally block performs
                            // the shared removal path.
                            break;
                        }
                        _server.AddBytesReceived(message.WireLength);
                        _server.HandleClientMessage(this, message);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                finally
                {
                    // Both send and receive loops may arrive here. RemoveClient is safe
                    // to call repeatedly because it checks whether the list changed.
                    _server.RemoveClient(this);
                }
            }
            private void SendLoop(CancellationToken cancellationToken)
            {
                try
                {
                    Stream stream = _stream;
                    while (!_closed && !cancellationToken.IsCancellationRequested)
                    {
                        AmigaNetMessage message = DequeueMessage();
                        if (message == null)
                        {
                            // Wake periodically in case cancellation happens without a
                            // queued message, but otherwise wait for producers to signal.
                            _sendSignal.WaitOne(25);
                            continue;
                        }
                        _server.AddBytesSent(AmigaNetProtocol.WriteMessage(stream, message));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                finally
                {
                    _server.RemoveClient(this);
                }
            }
            private AmigaNetMessage DequeueMessage()
            {
                lock (_sendLock)
                {
                    if (_controlQueue.Count > 0)
                    {
                        // Permissions, disconnects, client lists, and overlay popups must
                        // be observed even if media is flowing quickly.
                        return _controlQueue.Dequeue();
                    }
                    AmigaNetMessage ping = CreateLatencyPingIfDue(DateTime.UtcNow.Ticks);
                    if (ping != null)
                    {
                        return ping;
                    }
                    PendingVideoFrame video = _latestVideo;
                    if (video != null)
                    {
                        // Video outranks audio because the visual state should always be
                        // the freshest possible remote frame.
                        _latestVideo = null;
                        AmigaNetMessage videoMessage = CreateVideoMessage(video);
                        if (videoMessage != null)
                        {
                            return videoMessage;
                        }
                    }
                    if (_audioQueue.Count > 0)
                    {
                        return _audioQueue.Dequeue();
                    }
                    return null;
                }
            }
            private AmigaNetMessage CreateVideoMessage(PendingVideoFrame video)
            {
                if (video.BottomCrop == _lastSentBottomCrop && AmigaNetProtocol.PackedVideoFramesEqual(video.PackedPixels, _lastSentVideoPixels))
                {
                    // No visual byte changed since the last frame this client received.
                    // TCP/TLS is reliable, so the client can keep presenting its current
                    // image and we can spend zero bandwidth for this completed Amiga frame.
                    return null;
                }
                bool forceKeyFrame = _lastSentVideoPixels == null || _deltaFramesSinceKeyFrame >= MaxDeltaFramesBeforeKeyFrame;
                byte[] previous = forceKeyFrame ? null : _lastSentVideoPixels;
                long previousFrameId = forceKeyFrame ? 0 : _lastSentVideoFrameId;
                bool usedDelta;
                byte[] payload = AmigaNetProtocol.CreateBestVideoFramePayload(
                    video.PackedPixels,
                    previous,
                    video.Width,
                    video.Height,
                    video.FrameId,
                    previousFrameId,
                    out usedDelta, video.BottomCrop);
                _lastSentBottomCrop = video.BottomCrop;
                if (payload == null || payload.Length == 0)
                {
                    return null;
                }
                // Update the reference after payload creation. If the following socket
                // write fails, this connection is removed anyway.
                _lastSentVideoPixels = video.PackedPixels;
                _lastSentVideoFrameId = video.FrameId;
                _deltaFramesSinceKeyFrame = usedDelta ? _deltaFramesSinceKeyFrame + 1 : 0;
                return CreateMessage(AmigaNetMessageType.VideoFrame, payload);
            }
            private AmigaNetMessage CreateLatencyPingIfDue(long nowTicks)
            {
                long elapsedTicks = nowTicks - _lastLatencyPingTicks;
                if (_latencyPingPending)
                {
                    if (elapsedTicks < LatencyPingTimeoutTicks)
                    {
                        return null;
                    }
                }
                else if (_lastLatencyPingTicks != 0 && elapsedTicks < LatencyPingIntervalTicks)
                {
                    return null;
                }
                _lastLatencyPingTicks = nowTicks;
                _latencyPingPending = true;
                return new AmigaNetMessage
                {
                    Type = AmigaNetMessageType.Ping,
                    Timestamp = nowTicks,
                    Payload = new byte[0]
                };
            }
        }
    }
}
