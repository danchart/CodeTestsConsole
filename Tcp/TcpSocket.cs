namespace CodeTestsConsole
{
    using Networking.Core;
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;

    public sealed class TcpSocketListener
    {
        public readonly TcpClients Clients;

        private bool _isRunning;

        private TcpListener _listener;

        private byte[] _acceptReadBuffer;

        private readonly ILogger _logger;

        private readonly int MaxPacketSize, PacketQueueCapacity;

        public TcpSocketListener(ILogger logger, int clientCapacity, int maxPacketSize, int packetQueueCapacity)
        {
            this._isRunning = false;

            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

            this.Clients = new TcpClients(clientCapacity);

            this.MaxPacketSize = maxPacketSize;
            this.PacketQueueCapacity = packetQueueCapacity;

            this._acceptReadBuffer = new byte[MaxPacketSize];
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

        public void Stop()
        {
            this._isRunning = false;

            // Wait a short period to ensure the cancellation flag has propagated. 
            Thread.Sleep(100);

            _listener.Stop();
        }

        private void AcceptClient(IAsyncResult ar)
        {
            if (!_isRunning)
            {
                // Server stopped.
                return;
            }

            var listener = ar.AsyncState as TcpListener;

            if (listener == null)
            {
                return;
            }

            TcpClient client = listener.EndAcceptTcpClient(ar);
            NetworkStream stream = client.GetStream();
            TcpReceiveBuffer buffer = new TcpReceiveBuffer(maxPacketSize: MaxPacketSize, packetQueueCapacity: PacketQueueCapacity);

            var clientData = new TcpClientData
            {
                Client = client,
                Stream = stream,
                ReceiveBuffer = buffer,
            };
            this.Clients.Add(clientData);

            //buffer.GetWriteData(out byte[] data, out int offset, out int size);
            //stream.BeginRead(data, offset, size, AcceptRead, clientData);

            stream.BeginRead(this._acceptReadBuffer, 0, this._acceptReadBuffer.Length, AcceptRead, clientData);

            this._logger.Info($"Connected. RemoteEp={client.Client.RemoteEndPoint}");

            // Begin waiting for the next request.
            listener.BeginAcceptTcpClient(AcceptClient, listener);
        }

        private void AcceptRead(IAsyncResult ar)
        {
            if (!_isRunning)
            {
                // Server stopped.
                return;
            }

            TcpClientData clientData = (TcpClientData)ar.AsyncState;
            NetworkStream stream = clientData.Stream; ;

            int bytesRead = stream.EndRead(ar);

            // Seperate stream into packets before adding to the receive buffer.

            int pos = 0;
            while (pos < bytesRead)
            {
                var packetSize = BitConverter.ToUInt16(this._acceptReadBuffer, pos);

                if (packetSize > bytesRead - 2)
                {
                    throw new InvalidOperationException($"Invalid packet received: packetSize={packetSize}, bytesRead={bytesRead}");
                }

                clientData.ReceiveBuffer.GetWriteData(out byte[] data, out int offset, out int size);

                Array.Copy(this._acceptReadBuffer, pos + 2, data, 0, packetSize);

                pos += 2;
                pos += packetSize;

                clientData.ReceiveBuffer.NextWrite(packetSize, clientData.Client);
            }

            //clientData.ReceiveBuffer.GetWriteData(out byte[] data, out int offset, out int size);

            //clientData.ReceiveBuffer.NextWrite(bytesRead, clientData.Client);

            // Begin waiting for more stream data.
            stream.BeginRead(this._acceptReadBuffer, 0, this._acceptReadBuffer.Length, AcceptRead, clientData);

            //clientData.ReceiveBuffer.GetWriteData(out byte[] data, out int offset, out int size);
            //stream.BeginRead(data, offset, size, AcceptRead, clientData);
        }

        public class TcpClientsEventArgs : EventArgs
        {
            public TcpClientsEventArgs(TcpReceiveBuffer receiveBuffer)
            {
                this.ReceiveBuffer = receiveBuffer;
            }

            public TcpReceiveBuffer ReceiveBuffer { get; private set; }
        }

        public sealed class TcpClients
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

            public event EventHandler<TcpClientsEventArgs> OnClientAdded;
            public event EventHandler<TcpClientsEventArgs> OnClientRemoved;

            public TcpClientData Get(int index) => this._clients[index];

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

            public void Add(TcpClientData clientData)
            {
                if (_lockCount > 0)
                {
                    lock (_lockObj)
                    {
                        if (this._pendingAddCount == this._pendingAddClients.Length)
                        {
                            Array.Resize(ref this._pendingAddClients, 2 * this._pendingAddCount);
                        }

                        this._pendingAddClients[this._pendingAddCount++] = clientData;

                        return;
                    }
                }

                AddInternal(clientData);
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

            private void AddInternal(TcpClientData clientData)
            {
                if (this._count == this._clients.Length)
                {
                    Array.Resize(ref this._clients, 2 * _count);
                }

                this._clients[this._count++] = clientData;

                // Raise add event
                this.OnClientAdded?.Invoke(this, new TcpClientsEventArgs(clientData.ReceiveBuffer));
            }

            private void RemoveAndCloseInternal(int index)
            {
                // Raise remove event
                this.OnClientRemoved?.Invoke(this, new TcpClientsEventArgs(this._clients[index].ReceiveBuffer));

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

        public class TcpClientData
        {
            public TcpClient Client;
            public NetworkStream Stream;
            public TcpReceiveBuffer ReceiveBuffer;

            public void ClearAndClose()
            {
                Stream.Close();
                Stream = null;
                Client.Close();
                Client = null;
                ReceiveBuffer = null;
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
            ushort count)
        {
            _stream.WriteWithSizePreamble(data, offset, count);
        }

        public void Read(
            byte[] receiveData,
            int receiveOffset,
            int receiveSize,
            out int receivedBytes)
        {
            _stream.ReadWithSizePreamble(receiveData, receiveOffset, receiveSize, out receivedBytes);
        }
    }
}
