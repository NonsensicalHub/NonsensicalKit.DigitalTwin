using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin
{
    public class WebSocket
    {
        public const string GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    }

    /// <summary>
    /// 不支持WebGL
    /// </summary>
    public class WebSocketServer
    {
        public int Port = 9099;
        public int MaxMessagesPerSecond = 60;
        public bool IsRunning { get; private set; }

        private Socket _serverSocket;
        private bool _isClosed;
        private readonly object _clientLock = new object();
        private readonly List<ClientSession> _clients = new List<ClientSession>();

        public readonly Queue<string> MainThreadMsgQueue = new Queue<string>();
        private readonly object _queueLock = new object();

        public void StartServer()
        {
            if (IsRunning) return;
            IsRunning = true;
            _isClosed = false;

            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _serverSocket.Bind(new IPEndPoint(IPAddress.Any, Port));
            _serverSocket.Listen(100);
            new Thread(AcceptLoop) { IsBackground = true }.Start();
            Debug.Log($"✅ WebSocket 服务启动: {Port} 限流:{MaxMessagesPerSecond}/秒");
        }

        private void AcceptLoop()
        {
            while (!_isClosed)
            {
                try
                {
                    Socket clientSocket = _serverSocket.Accept();
                    ClientSession session = new ClientSession(clientSocket, this);
                    lock (_clientLock) _clients.Add(session);
                    new Thread(session.ReceiveLoop) { IsBackground = true }.Start();
                }
                catch
                {
                    break;
                }
            }
        }

        public void EnqueueMessage(string msg)
        {
            lock (_queueLock) MainThreadMsgQueue.Enqueue(msg);
        }

        public void RemoveClient(ClientSession session)
        {
            lock (_clientLock) _clients.Remove(session);
        }

        public void StopServer()
        {
            _isClosed = true;
            IsRunning = false;

            // 创建临时列表遍历，避免“枚举时集合被修改”错误
            List<ClientSession> tempClients;
            lock (_clientLock)
            {
                tempClients = new List<ClientSession>(_clients);
            }

            foreach (var c in tempClients)
            {
                c.Close();
            }

            lock (_clientLock)
            {
                _clients.Clear();
            }

            _serverSocket?.Close();
        }
    }

    public class ClientSession
    {
        public readonly Socket Socket;
        private readonly WebSocketServer _server;
        private bool _handShaked;

        private readonly byte[] _receiveBuffer = new byte[8192];
        private readonly List<byte> _recvCache = new List<byte>();
        private readonly List<byte> _frameBuffer = new List<byte>();

        private int _msgCount;
        private DateTime _lastReset = DateTime.Now;

        public ClientSession(Socket socket, WebSocketServer server)
        {
            Socket = socket;
            _server = server;
        }

        public void ReceiveLoop()
        {
            try
            {
                while (Socket.Connected && _server.IsRunning)
                {
                    int len = Socket.Receive(_receiveBuffer);
                    if (len <= 0) break;

                    var data = new byte[len];
                    Array.Copy(_receiveBuffer, data, len);

                    if (!_handShaked)
                    {
                        _recvCache.AddRange(data);
                        TryHandshake();
                    }
                    else
                    {
                        if (DateTime.Now - _lastReset > TimeSpan.FromSeconds(1))
                        {
                            _msgCount = 0;
                            _lastReset = DateTime.Now;
                        }

                        if (_msgCount < _server.MaxMessagesPerSecond)
                        {
                            _msgCount++;
                            _frameBuffer.AddRange(data);
                            TryUnpackFullFrame();
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }

            Close();
        }

        private void TryHandshake()
        {
            var s = Encoding.UTF8.GetString(_recvCache.ToArray());
            if (s.Contains("Sec-WebSocket-Key") && s.Contains("\r\n\r\n"))
            {
                string key = null;
                var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(':');
                    if (parts.Length >= 2 && parts[0].Trim() == "Sec-WebSocket-Key")
                    {
                        key = parts[1].Trim();
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(key))
                {
                    byte[] hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(key + WebSocket.GUID));
                    string accept = Convert.ToBase64String(hash);

                    string resp = "HTTP/1.1 101 Switching Protocols\r\n" +
                                  "Upgrade: websocket\r\n" +
                                  "Connection: Upgrade\r\n" +
                                  $"Sec-WebSocket-Accept: {accept}\r\n\r\n";

                    Socket.Send(Encoding.UTF8.GetBytes(resp));
                    _handShaked = true;
                    _recvCache.Clear();
                    Debug.Log("✅ 握手成功！");
                }
            }
        }

        private void TryUnpackFullFrame()
        {
            while (_frameBuffer.Count >= 2)
            {
                byte[] buf = _frameBuffer.ToArray();
                bool fin = (buf[0] & 0x80) != 0;
                int payloadLen = buf[1] & 0x7F;
                bool mask = (buf[1] & 0x80) != 0;
                if (!mask) return;

                int offset = 2;
                int realLen = payloadLen;

                if (payloadLen == 126)
                {
                    if (buf.Length < 4) return;
                    realLen = (buf[2] << 8) | buf[3];
                    offset = 4;
                }
                else if (payloadLen == 127)
                {
                    return;
                }

                int headerEnd = offset + 4;
                int total = headerEnd + realLen;
                if (buf.Length < total) return;

                byte[] maskKey = new byte[4];
                Array.Copy(buf, offset, maskKey, 0, 4);

                byte[] data = new byte[realLen];
                for (int i = 0; i < realLen; i++)
                {
                    data[i] = (byte)(buf[headerEnd + i] ^ maskKey[i % 4]);
                }

                string json = Encoding.UTF8.GetString(data);
                if (fin && !string.IsNullOrEmpty(json))
                {
                    _server.EnqueueMessage(json);
                }

                _frameBuffer.RemoveRange(0, total);
            }
        }

        public void Close()
        {
            try { Socket.Shutdown(SocketShutdown.Both); }
            catch
            {
                // ignored
            }

            try { Socket.Close(); }
            catch
            {
                // ignored
            }

            _server.RemoveClient(this);
        }
    }
}
