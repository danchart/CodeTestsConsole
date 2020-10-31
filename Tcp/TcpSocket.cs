namespace CodeTestsConsole
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;

    public class TcpSocketListener
    {
        private bool _isRunning;

        private TcpListener _listener;

        private readonly TcpReceiveBuffer _receiveBuffer;
        private readonly TcpClients _tcpClients;

        private readonly ILogger _logger;

        public TcpSocketListener(ILogger logger, TcpReceiveBuffer receiveBuffer)
        {
            this._isRunning = false;

            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

            this._receiveBuffer = receiveBuffer;
            this._tcpClients = new TcpClients(16);
        }

        public bool IsRunning => this._isRunning;

        public void Start(IPEndPoint ipEndPoint)
        {
            // From: https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.tcplistener?view=netcore-3.1

            this._listener = new TcpListener(ipEndPoint);
            this._isRunning = true;

            // Start listening for client requests.
            _listener.Start();

            this._logger.Info("Waiting for a connection...");

            _listener.BeginAcceptTcpClient(AcceptClient, _listener);
        }

        public void Receive()
        {
            while (this._isRunning)
            {
                this._tcpClients.Lock();

                try
                {
                    for (int i = 0; i < this._tcpClients.Count; i++)
                    {
                        TcpClientData clientData = this._tcpClients.Get(i);
                        TcpClient client = clientData.Client;

                        if (!client.Connected)
                        {
                            _logger.Warning($"TCP client disconnected: clientEp={client.Client.RemoteEndPoint}");

                            this._tcpClients.RemoveAndClose(i);

                            continue;
                        }

                        NetworkStream stream = clientData.Stream;

                        // Loop to receive all the data sent by the client.
                        if (stream.DataAvailable)
                        {
                            byte[] data;
                            int offset;
                            int size;
                            bool isWriteBufferWait = false;
                            while (!this._receiveBuffer.GetWriteData(out data, out offset, out size))
                            {
                                if (!isWriteBufferWait)
                                {
                                    isWriteBufferWait = true;

                                    _logger.Error("TCP server socket is out of writable buffer space.");
                                }
                                // Wait for write queue to become available.
                                Thread.Sleep(1);
                            }

                            int readCount = stream.Read(data, offset, size);

                            bool isReadSuccess = true;

                            if (readCount > size)
                            {
                                isReadSuccess = false;

                                this._logger.Error($"Received TCP packet exceeded buffer size: bufferSize={size}");

                                break;
                            }

                            if (isReadSuccess)
                            {
                                this._receiveBuffer.NextWrite(readCount, client);
                            }
                        }
                    }
                }
                finally
                {
                    this._tcpClients.Unlock();
                }
            }
        }

        private void AcceptClient(IAsyncResult ar)
        {
            if (!_isRunning)
            {
                return;
            }

            var listener = ar.AsyncState as TcpListener;

            if (listener == null)
            {
                return;
            }

            TcpClient client = listener.EndAcceptTcpClient(ar);
            this._tcpClients.Add(client, client.GetStream());

            this._logger.Info($"Connected. RemoteEp={client.Client.RemoteEndPoint}");

            // Begin waiting for the next request.
            listener.BeginAcceptTcpClient(AcceptClient, listener);
        }

        public void Stop()
        {
            this._isRunning = false;

            // Wait a short period to ensure the cancellation flag has propogated. 
            Thread.Sleep(100);

            _listener.Stop();
        }

        private sealed class TcpClients
        {
            private TcpClientData[] _clients;
            private int _count;

            private TcpClientData[] _pendingAddClients;
            private int _pendingAddCount;

            private int[] _pendingRemoveIndices;
            private int _pendingRemoveCount;

            private object _lockObj = new object();
            private int _lockCount;

            public TcpClients(int capacity)
            {
                this._clients = new TcpClientData[capacity];
                this._count = 0;

                this._pendingAddClients = new TcpClientData[capacity];
                this._pendingAddCount = 0;

                this._pendingRemoveIndices = new int[capacity];
                this._pendingRemoveCount = 0;

                this._lockCount = 0;
            }

            public int Count => this._count;

            public ref TcpClientData Get(int index) => ref this._clients[index];

            public void Lock()
            {
                lock (_lockObj)
                {
                    this._lockCount++;
                }
            }

            public void Unlock()
            {
                lock (_lockObj)
                {
                    if (--this._lockCount == 0)
                    {
                        // Process remove list

                        for (int i = 0; i < this._pendingRemoveCount; i++)
                        {
                            RemoveAndCloseInternal(this._pendingRemoveIndices[i]);
                        }

                        this._pendingRemoveCount = 0;

                        // Process add list

                        for (int i = 0; i < this._pendingAddCount; i++)
                        {
                            AddInternal(this._pendingAddClients[i]);

                            this._pendingAddClients[i] = default; // for GC
                        }

                        this._pendingAddCount = 0;
                    }
                }
            }

            public void Add(TcpClient client, NetworkStream stream)
            {
                if (_lockCount > 0)
                {
                    lock (_lockObj)
                    {
                        if (this._pendingAddCount == this._pendingAddClients.Length)
                        {
                            Array.Resize(ref this._pendingAddClients, 2 * this._pendingAddCount);
                        }

                        this._pendingAddClients[this._pendingAddCount++] =
                            new TcpClientData
                            {
                                Client = client,
                                Stream = stream,
                            };

                        return;
                    }
                }

                AddInternal(
                    new TcpClientData
                    {
                        Client = client,
                        Stream = stream,
                    });
            }

            public void RemoveAndClose(int index)
            {
                if (_lockCount > 0)
                {
                    lock (_lockObj)
                    {
                        if (this._pendingRemoveCount == this._pendingRemoveIndices.Length)
                        {
                            Array.Resize(ref this._pendingRemoveIndices, 2 * this._pendingRemoveCount);
                        }

                        this._pendingRemoveIndices[this._pendingRemoveCount++] = index;

                        return;
                    }
                }

                RemoveAndCloseInternal(index);
            }

            private void AddInternal(TcpClientData client)
            {
                if (this._count == this._clients.Length)
                {
                    Array.Resize(ref this._clients, 2 * _count);
                }

                this._clients[this._count++] = client;
            }

            private void RemoveAndCloseInternal(int index)
            {
                this._clients[index].ClearAndClose();

                if (this._count > 1)
                {
                    // swap with last
                    this._clients[index] = this._clients[this._count - 1];
                    this._clients[this._count - 1] = default; // for GC
                }

                this._count--;
            }
        }

        internal struct TcpClientData
        {
            public TcpClient Client;
            public NetworkStream Stream;

            public void ClearAndClose()
            {
                Stream.Close();
                Stream = null;
                Client.Close();
                Client = null;
            }
        }
    }

    public class TcpSocketClient
    {
        private TcpClient _client;
        private NetworkStream _stream;

        public TcpSocketClient(ILogger logger)
        {
            _client = new TcpClient();
        }

        public void Connect(string server, int port)
        {
            _client.Connect(server, port);
            _stream = _client.GetStream();
        }

        public void Disconnect()
        {
            _stream.Close();
            _client.Close();
        }

        public void Send(
            byte[] data,
            int offset,
            int size)
        {
            _stream.Write(data, offset, size);
        }

        public void Read(
            byte[] receiveData,
            int receiveOffset,
            int receiveSize,
            out int receivedBytes)
        {
            receivedBytes = _stream.Read(receiveData, receiveOffset, receiveSize);
        }
    }
}
