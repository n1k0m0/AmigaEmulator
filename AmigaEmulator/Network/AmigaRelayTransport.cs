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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace AmigaEmulator.Network
{
    public enum AmigaNetTransportMode
    {
        Lan = 0,
        Relay = 1
    }
    public sealed class AmigaRelayServerListener : IDisposable
    {
        private const int RelayHandshakeTimeoutMilliseconds = 8000;
        private readonly BlockingCollection<AmigaRelayAcceptedClient> _acceptedClients = new BlockingCollection<AmigaRelayAcceptedClient>();
        private readonly List<Task> _handshakeTasks = new List<Task>();
        private readonly object _syncRoot = new object();
        private bool _disposed;
        private AmigaRelayConnection _connection;
        private CancellationTokenSource _shutdown;
        public string Host { get; private set; }
        public int Port { get; private set; }
        public string ConnectionId { get; private set; }
        public string RelayFingerprint { get; private set; } = "UNKNOWN";
        public bool IsRunning
        {
            get
            {
                AmigaRelayConnection connection = _connection;
                return connection != null && connection.IsConnected;
            }
        }
        public bool Start(string host, int port, string connectionId, string relayPassword, out string status)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AmigaRelayServerListener));
            }
            Stop();
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            Port = port;
            ConnectionId = NormalizeConnectionId(connectionId);
            _shutdown = new CancellationTokenSource();
            _connection = new AmigaRelayConnection(true);
            _connection.ChannelOpened += HandleChannelOpened;
            if (!_connection.Connect(Host, Port, AmigaRelayRole.Server, ConnectionId, relayPassword, out status))
            {
                Stop();
                return false;
            }
            RelayFingerprint = _connection.RelayFingerprint;
            status = "RELAY SERVER REGISTERED";
            return true;
        }
        public AmigaRelayAcceptedClient AcceptClient(CancellationToken cancellationToken)
        {
            try
            {
                return _acceptedClients.Take(cancellationToken);
            }
            catch
            {
                return null;
            }
        }
        public void Stop()
        {
            if (_disposed)
            {
                return;
            }
            CancellationTokenSource shutdown = _shutdown;
            _shutdown = null;
            if (shutdown != null)
            {
                shutdown.Cancel();
            }
            AmigaRelayConnection connection = _connection;
            _connection = null;
            if (connection != null)
            {
                connection.ChannelOpened -= HandleChannelOpened;
                connection.Dispose();
            }
            while (_acceptedClients.TryTake(out AmigaRelayAcceptedClient accepted))
            {
                accepted.Dispose();
            }
            lock (_syncRoot)
            {
                for (int index = 0; index < _handshakeTasks.Count; index++)
                {
                    try
                    {
                        _handshakeTasks[index].Wait(25);
                    }
                    catch
                    {
                    }
                }
                _handshakeTasks.Clear();
            }
            if (shutdown != null)
            {
                shutdown.Dispose();
            }
        }
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            Stop();
            _disposed = true;
            _acceptedClients.Dispose();
        }
        private void HandleChannelOpened(AmigaRelayRawStream rawStream)
        {
            lock (_syncRoot)
            {
                _handshakeTasks.RemoveAll(t => t.IsCompleted);
                if (_handshakeTasks.Count >= 32) { rawStream.Dispose(); return; }
            }
            CancellationToken token = _shutdown != null ? _shutdown.Token : CancellationToken.None;
            Task task = Task.Run(() =>
            {
                try
                {
                    string fingerprint;
                    rawStream.ReadTimeout = RelayHandshakeTimeoutMilliseconds;
                    rawStream.WriteTimeout = RelayHandshakeTimeoutMilliseconds;
                    Stream encryptedStream = AmigaRelayE2EStream.CreateServer(rawStream, ConnectionId, out fingerprint);
                    if (encryptedStream.CanTimeout)
                    {
                        encryptedStream.ReadTimeout = RelayHandshakeTimeoutMilliseconds;
                        encryptedStream.WriteTimeout = RelayHandshakeTimeoutMilliseconds;
                    }
                    var accepted = new AmigaRelayAcceptedClient(
                        encryptedStream,
                        System.Net.IPEndPoint.TryParse(rawStream.RemoteEndpoint, out var endpoint) ? endpoint.Address.ToString() : "UNKNOWN",
                        rawStream.RemoteEndpoint,
                        fingerprint);
                    if (!_acceptedClients.IsAddingCompleted && !token.IsCancellationRequested)
                    {
                        _acceptedClients.Add(accepted, token);
                    }
                    else
                    {
                        accepted.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    rawStream.Dispose();
                }
            }, token);
            lock (_syncRoot)
            {
                _handshakeTasks.Add(task);
            }
        }
        private static string NormalizeConnectionId(string connectionId)
        {
            return string.IsNullOrWhiteSpace(connectionId) ? "amiga" : connectionId.Trim().ToLowerInvariant();
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
                exception = exception.InnerException;
            }
            return false;
        }
    }
    public sealed class AmigaRelayAcceptedClient : IDisposable
    {
        public AmigaRelayAcceptedClient(Stream stream, string remoteAddress, string remoteEndpoint, string sessionFingerprint)
        {
            Stream = stream;
            RemoteAddress = remoteAddress;
            RemoteEndpoint = remoteEndpoint;
            SessionFingerprint = sessionFingerprint;
        }
        public Stream Stream { get; private set; }
        public string RemoteAddress { get; private set; }
        public string RemoteEndpoint { get; private set; }
        public string SessionFingerprint { get; private set; }
        public void Dispose()
        {
            if (Stream != null)
            {
                Stream.Dispose();
                Stream = null;
            }
        }
    }
    public static class AmigaRelayClientConnector
    {
        private const int RelayHandshakeTimeoutMilliseconds = 8000;
        public static Stream Connect(
            string host,
            int port,
            string connectionId,
            string relayPassword,
            out string relayFingerprint,
            out string sessionFingerprint,
            out string status)
        {
            relayFingerprint = "UNKNOWN";
            sessionFingerprint = "UNKNOWN";
            var connection = new AmigaRelayConnection(false);
            if (!connection.Connect(host, port, AmigaRelayRole.Client, NormalizeConnectionId(connectionId), relayPassword, out status))
            {
                connection.Dispose();
                return null;
            }
            try
            {
                relayFingerprint = connection.RelayFingerprint;
                AmigaRelayRawStream rawStream = connection.GetClientChannel();
                rawStream.ReadTimeout = RelayHandshakeTimeoutMilliseconds;
                rawStream.WriteTimeout = RelayHandshakeTimeoutMilliseconds;
                Stream encryptedStream = AmigaRelayE2EStream.CreateClient(rawStream, NormalizeConnectionId(connectionId), out sessionFingerprint);
                if (encryptedStream.CanTimeout)
                {
                    encryptedStream.ReadTimeout = RelayHandshakeTimeoutMilliseconds;
                    encryptedStream.WriteTimeout = RelayHandshakeTimeoutMilliseconds;
                }
                status = "RELAY CONNECTED";
                return new AmigaRelayOwnedStream(encryptedStream, connection);
            }
            catch (IOException ex)
            {
                Debug.WriteLine(ex);
                connection.Dispose();
                status = IsTimeoutException(ex) ? "RELAY HOST TIMEOUT" : "RELAY E2E FAILED";
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                connection.Dispose();
                status = "RELAY E2E FAILED";
                return null;
            }
        }
        private static string NormalizeConnectionId(string connectionId)
        {
            return string.IsNullOrWhiteSpace(connectionId) ? "amiga" : connectionId.Trim().ToLowerInvariant();
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
    }
    internal enum AmigaRelayRole : byte
    {
        Server = 1,
        Client = 2
    }
    internal enum AmigaRelayFrameType : byte
    {
        Register = 1,
        RegisterOk = 2,
        RegisterReject = 3,
        ChannelOpen = 4,
        ChannelData = 5,
        ChannelClose = 6,
        Ping = 7,
        Pong = 8
    }
    internal sealed class AmigaRelayConnection : IDisposable
    {
        private const int TcpConnectTimeoutMilliseconds = 3000;
        private const int RelayControlHandshakeTimeoutMilliseconds = 5000;
        private const int ProtocolVersion = 1;
        private const int HeaderLength = 18;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AMIGAR");
        private const int MaxPayloadLength = 16 * 1024 * 1024;
        private readonly object _sendLock = new object();
        private readonly ConcurrentDictionary<int, AmigaRelayRawStream> _channels = new ConcurrentDictionary<int, AmigaRelayRawStream>();
        private readonly bool _serverSide;
        private TcpClient _tcpClient;
        private Stream _stream;
        private Task _receiveTask;
        private CancellationTokenSource _shutdown;
        private int _clientChannelId;
        public AmigaRelayConnection(bool serverSide)
        {
            _serverSide = serverSide;
        }
        public event Action<AmigaRelayRawStream> ChannelOpened;
        public string RelayFingerprint { get; private set; } = "UNKNOWN";
        public bool IsConnected
        {
            get { return _tcpClient != null && _tcpClient.Connected && _stream != null; }
        }
        public bool Connect(string host, int port, AmigaRelayRole role, string connectionId, string relayPassword, out string status)
        {
            status = string.Empty;
            try
            {
                _tcpClient = new TcpClient();
                _tcpClient.NoDelay = true;
                ApplySocketTimeouts(_tcpClient, RelayControlHandshakeTimeoutMilliseconds);
                Task connectTask = _tcpClient.ConnectAsync(host, port);
                if (!connectTask.Wait(TcpConnectTimeoutMilliseconds))
                {
                    status = "RELAY TIMEOUT";
                    return false;
                }
                string tlsStatus;
                _stream = AmigaNetTls.AuthenticateClient(_tcpClient, host, port, out tlsStatus);
                ApplyStreamTimeouts(_stream, RelayControlHandshakeTimeoutMilliseconds);
                RelayFingerprint = AmigaNetTls.GetTrustedServerShortFingerprint(host, port);
                SendFrame(AmigaRelayFrameType.Register, 0, CreateRegisterPayload(role, connectionId, relayPassword));
                AmigaRelayFrame response = ReadFrame(_stream);
                if (response == null)
                {
                    status = "NO RELAY RESPONSE";
                    return false;
                }
                if (response.Type == AmigaRelayFrameType.RegisterReject)
                {
                    status = ReadTextPayload(response.Payload);
                    return false;
                }
                if (response.Type != AmigaRelayFrameType.RegisterOk)
                {
                    status = "BAD RELAY RESPONSE";
                    return false;
                }
                _clientChannelId = ReadRegisterOkPayload(response.Payload, out string relayStatus);
                if (!_serverSide && _clientChannelId <= 0)
                {
                    status = "BAD RELAY CHANNEL";
                    return false;
                }
                if (!_serverSide)
                {
                    _channels[_clientChannelId] = new AmigaRelayRawStream(this, _clientChannelId);
                }
                _shutdown = new CancellationTokenSource();
                ApplyStreamTimeouts(_stream, Timeout.Infinite);
                ApplySocketTimeouts(_tcpClient, Timeout.Infinite);
                _receiveTask = Task.Run(() => ReceiveLoop(_shutdown.Token));
                status = string.IsNullOrWhiteSpace(relayStatus) ? "RELAY CONNECTED" : relayStatus;
                return true;
            }
            catch (AmigaNetTlsException ex)
            {
                Debug.WriteLine(ex);
                if (ex.IsCertificateChanged)
                {
                    Dispose();
                    throw;
                }
                status = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                status = IsTimeoutException(ex) ? "RELAY TIMEOUT" : "RELAY CONNECT FAILED";
                return false;
            }
        }
        public AmigaRelayRawStream GetClientChannel()
        {
            if (_clientChannelId <= 0)
            {
                throw new InvalidOperationException("Relay client has no channel.");
            }
            return _channels[_clientChannelId];
        }
        public void SendChannelData(int channelId, byte[] buffer, int offset, int count)
        {
            if (count <= 0)
            {
                return;
            }
            byte[] payload = new byte[count];
            Buffer.BlockCopy(buffer, offset, payload, 0, count);
            SendFrame(AmigaRelayFrameType.ChannelData, channelId, payload);
        }
        public void CloseChannel(int channelId)
        {
            SendFrame(AmigaRelayFrameType.ChannelClose, channelId, Array.Empty<byte>());
            if (_channels.TryRemove(channelId, out AmigaRelayRawStream stream))
            {
                stream.MarkRemoteClosed();
            }
        }
        public void Dispose()
        {
            CancellationTokenSource shutdown = _shutdown;
            _shutdown = null;
            if (shutdown != null)
            {
                shutdown.Cancel();
            }
            foreach (KeyValuePair<int, AmigaRelayRawStream> pair in _channels)
            {
                pair.Value.MarkRemoteClosed();
            }
            _channels.Clear();
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
            if (_receiveTask != null)
            {
                try
                {
                    _receiveTask.Wait(250);
                }
                catch
                {
                }
            }
            if (shutdown != null)
            {
                shutdown.Dispose();
            }
        }
        private void ReceiveLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    AmigaRelayFrame frame = ReadFrame(_stream);
                    if (frame == null)
                    {
                        break;
                    }
                    switch (frame.Type)
                    {
                        case AmigaRelayFrameType.ChannelOpen:
                            HandleChannelOpen(frame.ChannelId, frame.Payload);
                            break;
                        case AmigaRelayFrameType.ChannelData:
                            if (_channels.TryGetValue(frame.ChannelId, out AmigaRelayRawStream stream))
                            {
                                stream.EnqueueData(frame.Payload);
                            }
                            break;
                        case AmigaRelayFrameType.ChannelClose:
                            if (_channels.TryRemove(frame.ChannelId, out AmigaRelayRawStream closed))
                            {
                                closed.MarkRemoteClosed();
                            }
                            break;
                        case AmigaRelayFrameType.Ping:
                            SendFrame(AmigaRelayFrameType.Pong, frame.ChannelId, frame.Payload);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                foreach (KeyValuePair<int, AmigaRelayRawStream> pair in _channels)
                {
                    pair.Value.MarkRemoteClosed();
                }
            }
        }
        private void HandleChannelOpen(int channelId, byte[] payload)
        {
            if (channelId <= 0)
            {
                return;
            }
            var stream = new AmigaRelayRawStream(this, channelId) { RemoteEndpoint = ReadTextPayload(payload) };
            if (_channels.TryAdd(channelId, stream))
            {
                Action<AmigaRelayRawStream> handler = ChannelOpened;
                if (handler != null)
                {
                    handler(stream);
                }
            }
        }
        private void SendFrame(AmigaRelayFrameType type, int channelId, byte[] payload)
        {
            payload = payload ?? Array.Empty<byte>();
            if (payload.Length > MaxPayloadLength)
            {
                throw new InvalidDataException("Relay payload is too large.");
            }
            byte[] header = new byte[HeaderLength];
            Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
            header[6] = ProtocolVersion;
            header[7] = (byte)type;
            WriteUInt16(header, 8, 0);
            WriteInt32(header, 10, channelId);
            WriteInt32(header, 14, payload.Length);
            lock (_sendLock)
            {
                _stream.Write(header, 0, header.Length);
                if (payload.Length > 0)
                {
                    _stream.Write(payload, 0, payload.Length);
                }
                _stream.Flush();
            }
        }
        private static AmigaRelayFrame ReadFrame(Stream stream)
        {
            byte[] header = new byte[HeaderLength];
            if (!ReadExact(stream, header, 0, header.Length))
            {
                return null;
            }
            if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic) || header[6] != ProtocolVersion)
            {
                throw new InvalidDataException("Invalid relay frame header.");
            }
            int channelId = ReadInt32(header, 10);
            int payloadLength = ReadInt32(header, 14);
            if (payloadLength < 0 || payloadLength > MaxPayloadLength)
            {
                throw new InvalidDataException("Invalid relay payload length.");
            }
            byte[] payload = payloadLength == 0 ? Array.Empty<byte>() : new byte[payloadLength];
            if (payloadLength > 0 && !ReadExact(stream, payload, 0, payload.Length))
            {
                return null;
            }
            return new AmigaRelayFrame
            {
                Type = (AmigaRelayFrameType)header[7],
                ChannelId = channelId,
                Payload = payload
            };
        }
        private static byte[] CreateRegisterPayload(AmigaRelayRole role, string connectionId, string relayPassword)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(ProtocolVersion);
                writer.Write((byte)role);
                WriteString(writer, connectionId);
                WriteString(writer, relayPassword);
                return stream.ToArray();
            }
        }
        private static int ReadRegisterOkPayload(byte[] payload, out string status)
        {
            using (var stream = new MemoryStream(payload ?? Array.Empty<byte>()))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                int channelId = reader.ReadInt32();
                status = ReadString(reader);
                return channelId;
            }
        }
        private static string ReadTextPayload(byte[] payload)
        {
            using (var stream = new MemoryStream(payload ?? Array.Empty<byte>()))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                return ReadString(reader);
            }
        }
        private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                {
                    return false;
                }
                offset += read;
                count -= read;
            }
            return true;
        }
        private static void ApplySocketTimeouts(TcpClient tcpClient, int timeoutMilliseconds)
        {
            if (tcpClient == null)
            {
                return;
            }
            int socketTimeout = timeoutMilliseconds == Timeout.Infinite ? 0 : timeoutMilliseconds;
            tcpClient.ReceiveTimeout = socketTimeout;
            tcpClient.SendTimeout = socketTimeout;
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
        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 65536)
            {
                throw new InvalidDataException("Invalid relay string length.");
            }
            return Encoding.UTF8.GetString(reader.ReadBytes(length));
        }
        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            unchecked
            {
                uint unsigned = (uint)value;
                buffer[offset] = (byte)(unsigned & 0xFF);
                buffer[offset + 1] = (byte)((unsigned >> 8) & 0xFF);
                buffer[offset + 2] = (byte)((unsigned >> 16) & 0xFF);
                buffer[offset + 3] = (byte)((unsigned >> 24) & 0xFF);
            }
        }
        private static int ReadInt32(byte[] buffer, int offset)
        {
            unchecked
            {
                return (int)(
                    (uint)buffer[offset] |
                    ((uint)buffer[offset + 1] << 8) |
                    ((uint)buffer[offset + 2] << 16) |
                    ((uint)buffer[offset + 3] << 24));
            }
        }
    }
    internal sealed class AmigaRelayFrame
    {
        public AmigaRelayFrameType Type { get; set; }
        public int ChannelId { get; set; }
        public byte[] Payload { get; set; }
    }
    internal sealed class AmigaRelayRawStream : Stream
    {
        private readonly AmigaRelayConnection _connection;
        private readonly object _syncRoot = new object();
        private readonly Queue<byte[]> _incoming = new Queue<byte[]>();
        private byte[] _currentReadBuffer;
        private int _currentReadOffset;
        private int _bufferedBytes;
        private const int MaxBufferedBytes = 32 * 1024 * 1024;
        private int _readTimeout = Timeout.Infinite;
        private int _writeTimeout = Timeout.Infinite;
        private bool _closed;
        private bool _disposed;
        public AmigaRelayRawStream(AmigaRelayConnection connection, int channelId)
        {
            _connection = connection;
            ChannelId = channelId;
        }
        public int ChannelId { get; private set; }
        public string RemoteEndpoint { get; set; } = "UNKNOWN";
        public override bool CanRead
        {
            get { return true; }
        }
        public override bool CanSeek
        {
            get { return false; }
        }
        public override bool CanWrite
        {
            get { return true; }
        }
        public override bool CanTimeout
        {
            get { return true; }
        }
        public override int ReadTimeout
        {
            get { return _readTimeout; }
            set { _readTimeout = ValidateTimeout(value, nameof(value)); }
        }
        public override int WriteTimeout
        {
            get { return _writeTimeout; }
            set { _writeTimeout = ValidateTimeout(value, nameof(value)); }
        }
        public override long Length
        {
            get { throw new NotSupportedException(); }
        }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (count == 0)
            {
                return 0;
            }
            lock (_syncRoot)
            {
                while (!_closed && !HasBufferedData())
                {
                    WaitForBufferedDataOrTimeout();
                }
                if (!HasBufferedData())
                {
                    return 0;
                }
                int copied = 0;
                while (count > 0 && HasBufferedData())
                {
                    if (_currentReadBuffer == null || _currentReadOffset >= _currentReadBuffer.Length)
                    {
                        _currentReadBuffer = _incoming.Dequeue();
                        _currentReadOffset = 0;
                    }
                    int available = _currentReadBuffer.Length - _currentReadOffset;
                    int chunk = Math.Min(available, count);
                    Buffer.BlockCopy(_currentReadBuffer, _currentReadOffset, buffer, offset, chunk);
                    _currentReadOffset += chunk;
                    offset += chunk;
                    count -= chunk;
                    copied += chunk;
                    _bufferedBytes -= chunk;
                    if (_currentReadOffset >= _currentReadBuffer.Length)
                    {
                        _currentReadBuffer = null;
                        _currentReadOffset = 0;
                    }
                }
                return copied;
            }
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AmigaRelayRawStream));
            }
            _connection.SendChannelData(ChannelId, buffer, offset, count);
        }
        public override void Flush()
        {
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        internal void EnqueueData(byte[] data)
        {
            if (data == null)
            {
                return;
            }
            lock (_syncRoot)
            {
                if (_closed)
                {
                    return;
                }
                if (data.Length > MaxBufferedBytes - _bufferedBytes)
                {
                    _incoming.Clear(); _currentReadBuffer = null; _bufferedBytes = 0;
                    _closed = true; Monitor.PulseAll(_syncRoot); _connection.CloseChannel(ChannelId); return;
                }
                _bufferedBytes += data.Length;
                _incoming.Enqueue(data);
                Monitor.PulseAll(_syncRoot);
            }
        }
        internal void MarkRemoteClosed()
        {
            lock (_syncRoot)
            {
                _closed = true;
                Monitor.PulseAll(_syncRoot);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (disposing)
            {
                try
                {
                    _connection.CloseChannel(ChannelId);
                }
                catch
                {
                }
                MarkRemoteClosed();
            }
            base.Dispose(disposing);
        }
        private bool HasBufferedData()
        {
            return (_currentReadBuffer != null && _currentReadOffset < _currentReadBuffer.Length) || _incoming.Count > 0;
        }
        private void WaitForBufferedDataOrTimeout()
        {
            int timeout = _readTimeout;
            if (timeout == Timeout.Infinite)
            {
                Monitor.Wait(_syncRoot);
                return;
            }
            long deadlineTicks = DateTime.UtcNow.AddMilliseconds(timeout).Ticks;
            while (!_closed && !HasBufferedData())
            {
                long remainingTicks = deadlineTicks - DateTime.UtcNow.Ticks;
                if (remainingTicks <= 0)
                {
                    throw new IOException("Relay channel read timed out.");
                }
                int remainingMilliseconds = (int)Math.Min(
                    int.MaxValue,
                    Math.Max(1, remainingTicks / TimeSpan.TicksPerMillisecond));
                Monitor.Wait(_syncRoot, remainingMilliseconds);
            }
        }
        private static int ValidateTimeout(int timeout, string parameterName)
        {
            if (timeout < Timeout.Infinite)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
            return timeout;
        }
    }
    internal sealed class AmigaRelayOwnedStream : Stream
    {
        private readonly Stream _inner;
        private readonly AmigaRelayConnection _owner;
        public AmigaRelayOwnedStream(Stream inner, AmigaRelayConnection owner)
        {
            _inner = inner;
            _owner = owner;
        }
        public override bool CanRead
        {
            get { return _inner.CanRead; }
        }
        public override bool CanSeek
        {
            get { return false; }
        }
        public override bool CanWrite
        {
            get { return _inner.CanWrite; }
        }
        public override bool CanTimeout
        {
            get { return _inner.CanTimeout; }
        }
        public override int ReadTimeout
        {
            get { return _inner.ReadTimeout; }
            set { _inner.ReadTimeout = value; }
        }
        public override int WriteTimeout
        {
            get { return _inner.WriteTimeout; }
            set { _inner.WriteTimeout = value; }
        }
        public override long Length
        {
            get { throw new NotSupportedException(); }
        }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }
        public override void Flush()
        {
            _inner.Flush();
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _owner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
    internal sealed class AmigaRelayE2EStream : Stream
    {
        private const int RecordHeaderLength = 4;
        private const int NonceLength = 12;
        private const int TagLength = 16;
        private const int MaxPlaintextLength = 16 * 1024 * 1024;
        private static readonly byte[] HandshakeMagic = Encoding.ASCII.GetBytes("AmigaE2E1");
        private readonly Stream _inner;
        private readonly byte[] _key;
        private readonly bool _serverSide;
        private readonly object _writeLock = new object();
        private byte[] _decryptedBuffer;
        private int _decryptedOffset;
        private ulong _sendCounter;
        private ulong _receiveCounter;
        private AmigaRelayE2EStream(Stream inner, byte[] key, bool serverSide)
        {
            _inner = inner;
            _key = key;
            _serverSide = serverSide;
        }
        public static Stream CreateServer(Stream inner, string connectionId, out string sessionFingerprint)
        {
            using (ECDiffieHellman local = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
            {
                byte[] serverPublicKey = local.ExportSubjectPublicKeyInfo();
                WriteHandshake(inner, 1, serverPublicKey);
                byte[] clientPublicKey = ReadHandshake(inner, 2);
                byte[] key = DeriveKey(local, clientPublicKey, connectionId);
                sessionFingerprint = FormatShortFingerprint(Sha256Hex(serverPublicKey));
                return new AmigaRelayE2EStream(inner, key, true);
            }
        }
        public static Stream CreateClient(Stream inner, string connectionId, out string sessionFingerprint)
        {
            byte[] serverPublicKey = ReadHandshake(inner, 1);
            using (ECDiffieHellman local = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
            {
                byte[] clientPublicKey = local.ExportSubjectPublicKeyInfo();
                WriteHandshake(inner, 2, clientPublicKey);
                byte[] key = DeriveKey(local, serverPublicKey, connectionId);
                sessionFingerprint = FormatShortFingerprint(Sha256Hex(serverPublicKey));
                return new AmigaRelayE2EStream(inner, key, false);
            }
        }
        public override bool CanRead
        {
            get { return true; }
        }
        public override bool CanSeek
        {
            get { return false; }
        }
        public override bool CanWrite
        {
            get { return true; }
        }
        public override bool CanTimeout
        {
            get { return _inner.CanTimeout; }
        }
        public override int ReadTimeout
        {
            get { return _inner.ReadTimeout; }
            set { _inner.ReadTimeout = value; }
        }
        public override int WriteTimeout
        {
            get { return _inner.WriteTimeout; }
            set { _inner.WriteTimeout = value; }
        }
        public override long Length
        {
            get { throw new NotSupportedException(); }
        }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (count == 0)
            {
                return 0;
            }
            if (_decryptedBuffer == null || _decryptedOffset >= _decryptedBuffer.Length)
            {
                if (!ReadEncryptedRecord())
                {
                    return 0;
                }
            }
            int available = _decryptedBuffer.Length - _decryptedOffset;
            int copy = Math.Min(available, count);
            Buffer.BlockCopy(_decryptedBuffer, _decryptedOffset, buffer, offset, copy);
            _decryptedOffset += copy;
            if (_decryptedOffset >= _decryptedBuffer.Length)
            {
                _decryptedBuffer = null;
                _decryptedOffset = 0;
            }
            return copy;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count <= 0)
            {
                return;
            }
            if (count > MaxPlaintextLength)
            {
                throw new InvalidDataException("Relay E2E record is too large.");
            }
            byte[] plaintext = new byte[count];
            Buffer.BlockCopy(buffer, offset, plaintext, 0, count);
            byte[] nonce;
            byte[] ciphertext = new byte[count];
            byte[] tag = new byte[TagLength];
            lock (_writeLock)
            {
                if (_sendCounter == ulong.MaxValue) throw new InvalidDataException("Relay E2E nonce exhausted.");
                nonce = CreateNonce(GetSendDirection(), _sendCounter++);
                using (var aes = new AesGcm(_key, TagLength))
                {
                    aes.Encrypt(nonce, plaintext, ciphertext, tag);
                }
                int bodyLength = NonceLength + TagLength + ciphertext.Length;
                byte[] header = new byte[RecordHeaderLength];
                WriteInt32(header, 0, bodyLength);
                _inner.Write(header, 0, header.Length);
                _inner.Write(nonce, 0, nonce.Length);
                _inner.Write(tag, 0, tag.Length);
                _inner.Write(ciphertext, 0, ciphertext.Length);
                _inner.Flush();
            }
        }
        public override void Flush()
        {
            _inner.Flush();
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                Array.Clear(_key);
            }
            base.Dispose(disposing);
        }
        private bool ReadEncryptedRecord()
        {
            byte[] header = new byte[RecordHeaderLength];
            if (!ReadExact(_inner, header, 0, header.Length))
            {
                return false;
            }
            int bodyLength = ReadInt32(header, 0);
            if (bodyLength < NonceLength + TagLength || bodyLength > MaxPlaintextLength + NonceLength + TagLength)
            {
                throw new InvalidDataException("Invalid Relay E2E record length.");
            }
            byte[] body = new byte[bodyLength];
            if (!ReadExact(_inner, body, 0, body.Length))
            {
                return false;
            }
            byte[] nonce = new byte[NonceLength];
            byte[] tag = new byte[TagLength];
            int ciphertextLength = bodyLength - NonceLength - TagLength;
            byte[] ciphertext = new byte[ciphertextLength];
            Buffer.BlockCopy(body, 0, nonce, 0, NonceLength);
            Buffer.BlockCopy(body, NonceLength, tag, 0, TagLength);
            Buffer.BlockCopy(body, NonceLength + TagLength, ciphertext, 0, ciphertextLength);
            if (_receiveCounter == ulong.MaxValue) throw new InvalidDataException("Relay E2E nonce exhausted.");
            byte[] expectedNonce = CreateNonce(GetReceiveDirection(), _receiveCounter++);
            if (!FixedTimeEquals(nonce, expectedNonce))
            {
                throw new InvalidDataException("Relay E2E nonce sequence mismatch.");
            }
            byte[] plaintext = new byte[ciphertextLength];
            using (var aes = new AesGcm(_key, TagLength))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }
            _decryptedBuffer = plaintext;
            _decryptedOffset = 0;
            return true;
        }
        private byte GetSendDirection()
        {
            return _serverSide ? (byte)0 : (byte)1;
        }
        private byte GetReceiveDirection()
        {
            return _serverSide ? (byte)1 : (byte)0;
        }
        private static byte[] DeriveKey(ECDiffieHellman local, byte[] peerPublicKey, string connectionId)
        {
            using (ECDiffieHellman peer = ECDiffieHellman.Create())
            {
                peer.ImportSubjectPublicKeyInfo(peerPublicKey, out _);
                byte[] salt = SHA256.HashData(Encoding.UTF8.GetBytes("AmigaRelay:" + (connectionId ?? string.Empty)));
                byte[] info = Encoding.ASCII.GetBytes("AmigaEmulator Relay E2E v1");
                return local.DeriveKeyFromHash(peer.PublicKey, HashAlgorithmName.SHA256, salt, info);
            }
        }
        private static void WriteHandshake(Stream stream, byte type, byte[] publicKey)
        {
            byte[] header = new byte[HandshakeMagic.Length + 1 + 4];
            Buffer.BlockCopy(HandshakeMagic, 0, header, 0, HandshakeMagic.Length);
            header[HandshakeMagic.Length] = type;
            WriteInt32(header, HandshakeMagic.Length + 1, publicKey.Length);
            stream.Write(header, 0, header.Length);
            stream.Write(publicKey, 0, publicKey.Length);
            stream.Flush();
        }
        private static byte[] ReadHandshake(Stream stream, byte expectedType)
        {
            byte[] header = new byte[HandshakeMagic.Length + 1 + 4];
            if (!ReadExact(stream, header, 0, header.Length))
            {
                throw new EndOfStreamException("Relay E2E handshake ended early.");
            }
            for (int index = 0; index < HandshakeMagic.Length; index++)
            {
                if (header[index] != HandshakeMagic[index])
                {
                    throw new InvalidDataException("Invalid Relay E2E handshake magic.");
                }
            }
            if (header[HandshakeMagic.Length] != expectedType)
            {
                throw new InvalidDataException("Invalid Relay E2E handshake type.");
            }
            int keyLength = ReadInt32(header, HandshakeMagic.Length + 1);
            if (keyLength <= 0 || keyLength > 8192)
            {
                throw new InvalidDataException("Invalid Relay E2E key length.");
            }
            byte[] publicKey = new byte[keyLength];
            if (!ReadExact(stream, publicKey, 0, publicKey.Length))
            {
                throw new EndOfStreamException("Relay E2E handshake key ended early.");
            }
            return publicKey;
        }
        private static byte[] CreateNonce(byte direction, ulong counter)
        {
            byte[] nonce = new byte[NonceLength];
            nonce[0] = direction;
            for (int index = 0; index < 8; index++)
            {
                nonce[4 + index] = (byte)((counter >> (index * 8)) & 0xFF);
            }
            return nonce;
        }
        private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                {
                    return false;
                }
                offset += read;
                count -= read;
            }
            return true;
        }
        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            int diff = 0;
            for (int index = 0; index < left.Length; index++)
            {
                diff |= left[index] ^ right[index];
            }
            return diff == 0;
        }
        private static string Sha256Hex(byte[] data)
        {
            byte[] hash = SHA256.HashData(data);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
        }
        private static string FormatShortFingerprint(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length < 12)
            {
                return "UNKNOWN";
            }
            return fingerprint.Substring(0, 4) + "-" + fingerprint.Substring(4, 4) + "-" + fingerprint.Substring(8, 4);
        }
        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            unchecked
            {
                uint unsigned = (uint)value;
                buffer[offset] = (byte)(unsigned & 0xFF);
                buffer[offset + 1] = (byte)((unsigned >> 8) & 0xFF);
                buffer[offset + 2] = (byte)((unsigned >> 16) & 0xFF);
                buffer[offset + 3] = (byte)((unsigned >> 24) & 0xFF);
            }
        }
        private static int ReadInt32(byte[] buffer, int offset)
        {
            unchecked
            {
                return (int)(
                    (uint)buffer[offset] |
                    ((uint)buffer[offset + 1] << 8) |
                    ((uint)buffer[offset + 2] << 16) |
                    ((uint)buffer[offset + 3] << 24));
            }
        }
    }
}
