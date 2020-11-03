namespace CodeTestsConsole
{
    using Networking.Core;
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class TcpSocketListener
    {
        public readonly TcpClients Clients;

        private bool _isRunning;

        private TcpListener _listener;

        private readonly TcpReceive _tcpReceiver;

        private readonly ILogger _logger;

        private readonly int MaxPacketSize, PacketQueueCapacity;

        public TcpSocketListener(ILogger logger, int clientCapacity, int maxPacketSize, int packetQueueCapacity)
        {
            this._isRunning = false;

            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

            this.Clients = new TcpClients(clientCapacity);
            this._tcpReceiver = new TcpReceive(logger, maxPacketSize, packetQueueCapacity);

            this.MaxPacketSize = maxPacketSize;
            this.PacketQueueCapacity = packetQueueCapacity;
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

            this._tcpReceiver.Start(stream, clientData);

            this._logger.Info($"Connected. RemoteEp={client.Client.RemoteEndPoint}");

            // Begin waiting for the next request.
            listener.BeginAcceptTcpClient(AcceptClient, listener);
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
    }

    public sealed class TcpClientData
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

    internal sealed class TcpReceive
    {
        public const int FrameHeaderSizeByteCount = sizeof(ushort) + sizeof(ushort); // size + transaction id

        private bool _isRunning;

        private readonly int MaxPacketSize;
        private readonly int MaxPacketCapacity;

        private readonly ILogger _logger;

        public bool IsClient = false;



        public TcpReceive(ILogger logger, int maxPacketSize, int maxPacketCapacity)
        {
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

            this.MaxPacketSize = maxPacketSize;
            this.MaxPacketCapacity = maxPacketCapacity;
        }

        public void Start(NetworkStream stream, TcpClientData clientData)
        {
            this._isRunning = true;

            var receiverData = new ReceiverData
            {
                ClientData = clientData,
                AcceptReadBuffer = new byte[this.MaxPacketSize * this.MaxPacketCapacity],
                AcceptReadBufferSize = 0,
                MessageSize = 0,
            };

            stream.BeginRead(
                receiverData.AcceptReadBuffer, 
                0, 
                receiverData.AcceptReadBuffer.Length, 
                AcceptRead, 
                receiverData);
        }

        public void Stop()
        {
            this._isRunning = false;
        }

        private void AcceptRead(IAsyncResult ar)
        {
            if (!_isRunning)
            {
                // Server stopped.
                return;
            }

            ReceiverData receiverData = (ReceiverData)ar.AsyncState;

            NetworkStream stream = receiverData.ClientData.Stream;

            int bytesRead = stream.EndRead(ar);
            receiverData.AcceptReadBufferSize += bytesRead;

            // We need at least the frame header data to do anything.
            while (receiverData.AcceptReadBufferSize >= FrameHeaderSizeByteCount) 
            {
                if (receiverData.MessageSize == 0)
                {
                    // Starting new message, get message size in bytes.

                    // First two bytes of the buffer is always the message size
                    receiverData.MessageSize = BitConverter.ToUInt16(receiverData.AcceptReadBuffer, 0);
                    receiverData.TransactionId = BitConverter.ToUInt16(receiverData.AcceptReadBuffer, 2);
                }

                if (FrameHeaderSizeByteCount + receiverData.MessageSize <= receiverData.AcceptReadBufferSize)
                {
                    // Complete message data available.

                    if (receiverData.ClientData.ReceiveBuffer.GetWriteData(out byte[] data, out int offset, out int size))
                    {
                        // Copy data minus frame preamble
                        Array.Copy(receiverData.AcceptReadBuffer, FrameHeaderSizeByteCount, data, offset, receiverData.MessageSize);

                        receiverData.ClientData.ReceiveBuffer.NextWrite(receiverData.MessageSize, receiverData.ClientData.Client, receiverData.TransactionId);
                    }
                    else
                    {
                        _logger.Error($"Out of receive buffer space: capacity={this.MaxPacketCapacity}");
                    }

                    // Shift accept read buffer to the next frame, if any.
                    for (int i = FrameHeaderSizeByteCount + receiverData.MessageSize, j = 0; i < receiverData.AcceptReadBufferSize; i++, j++)
                    {
                        receiverData.AcceptReadBuffer[j] = receiverData.AcceptReadBuffer[i];
                    }

                    receiverData.AcceptReadBufferSize -= receiverData.MessageSize + FrameHeaderSizeByteCount;
                    receiverData.MessageSize = 0;
                }
                else
                {
                    // Message still being streamed.

                    break;
                }
            }

            // Begin waiting for more stream data.
            stream.BeginRead(
                receiverData.AcceptReadBuffer,
                receiverData.AcceptReadBufferSize,
                receiverData.AcceptReadBuffer.Length - receiverData.AcceptReadBufferSize,
                AcceptRead,
                receiverData);
        }

        private class ReceiverData
        {
            public TcpClientData ClientData;

            public byte[] AcceptReadBuffer;
            public int AcceptReadBufferSize;
            public int MessageSize;
            public ushort TransactionId;
        }
    }

    public class TcpSocketClient
    {
        private TcpClient _client;
        private NetworkStream _stream;

        private readonly TcpReceiveBuffer _receiveBuffer;
        private readonly TcpReceive _tcpReceiver;

        private ushort _nextTransactionId = 0;

        private object _stateLock = new object();
        private object _streamWriteLock = new object();

        private SendAndReceiveState[] _sendAndReceiveStates = new SendAndReceiveState[16];
        private int _sendAndReceiveStateCount = 0;

        private int[] _freeSendAndReceiveStateIndices = new int[16];
        private int _freeSendAndReceiveStateCount = 0;

        private Dictionary<int, int> _transactionIdToStateIndex = new Dictionary<int, int>(16);

        public TcpSocketClient(ILogger logger, int maxPacketSize, int packetQueueCapacity)
        {
            this._client = new TcpClient();

            this._receiveBuffer = new TcpReceiveBuffer(maxPacketSize, packetQueueCapacity);
            this._tcpReceiver = new TcpReceive(logger, maxPacketSize, packetQueueCapacity);



            this._tcpReceiver.IsClient = true;




            this._receiveBuffer.OnWriteComplete = this.OnWriteComplete;
        }

        public void Connect(string server, int port)
        {
            _client.Connect(server, port);
            _stream = _client.GetStream();

            this._tcpReceiver.Start(
                this._stream, 
                new TcpClientData
                {
                    Client = this._client,
                    Stream = this._stream,
                    ReceiveBuffer = this._receiveBuffer,
                });
        }

        public void Disconnect()
        {
            _stream.Close();
            _client.Close();
        }

        public Task<TcpPacket> SendAsync(
            byte[] data,
            int offset,
            ushort count)
        {
            int index;
            ushort transactionId;

            lock (_stateLock)
            {
                if (this._freeSendAndReceiveStateCount > 0)
                {
                    index = this._freeSendAndReceiveStateIndices[--this._freeSendAndReceiveStateCount];
                }
                else
                {
                    if (this._sendAndReceiveStateCount == this._sendAndReceiveStates.Length)
                    {
                        Array.Resize(ref this._sendAndReceiveStates, 2 * this._sendAndReceiveStateCount);
                        Array.Resize(ref this._freeSendAndReceiveStateIndices, 2 * this._sendAndReceiveStateCount);
                    }

                    index = this._sendAndReceiveStateCount++;
                }

                transactionId = this._nextTransactionId++;

                this._transactionIdToStateIndex[transactionId] = index;
            }

            ref var sendAndReceiveData = ref this._sendAndReceiveStates[index];

            // Create and use a copy of the TCS as it can get deferenced in the sendAndReceive pool 
            // before we return.
            var tcs = new TaskCompletionSource<TcpPacket>();

            sendAndReceiveData.Tcs = tcs;
            sendAndReceiveData.TransactionId = transactionId;

            Post(transactionId, data, offset, count);

            return tcs.Task;
        }

        public void Post(
            ushort transactionId,
            byte[] data,
            int offset,
            ushort count)
        {
            lock (_streamWriteLock)
            {
                _stream.WriteFrame(transactionId, data, offset, count);
            }
        }

        //public void Read(
        //    byte[] receiveData,
        //    int receiveOffset,
        //    int receiveSize,
        //    out ushort transactionId,
        //    out int receivedBytes)
        //{
        //    //_stream.ReadWithSizePreamble(receiveData, receiveOffset, receiveSize, out receivedBytes);

        //    byte[] data;
        //    int offset;
        //    while (!this._receiveBuffer.GetReadData(out data, out offset, out receivedBytes))
        //    {
        //    }

        //    Array.Copy(data, offset, receiveData, receiveOffset, receivedBytes);

        //    this._receiveBuffer.NextRead(closeConnection: false);
        //}

        private bool OnWriteComplete(
            byte[] data, 
            int offset, 
            int size, 
            TcpClient tcpClient, 
            ushort transactionId)
        {
            lock (_stateLock)
            {
                if (this._transactionIdToStateIndex.ContainsKey(transactionId))
                {
                    var index = this._transactionIdToStateIndex[transactionId];

                    var dataCopy = new byte[size];
                    Array.Copy(data, offset, dataCopy, 0, size);

                    var packet = new TcpPacket
                    {
                        Data = dataCopy,
                        Offset = 0,
                        Size = size
                    };

                    this._transactionIdToStateIndex.Remove(transactionId);

                    this._freeSendAndReceiveStateIndices[this._freeSendAndReceiveStateCount++] = index;

                    var tcs = this._sendAndReceiveStates[index].Tcs;
                    this._sendAndReceiveStates[index].Tcs = null; // for GC

                    tcs.SetResult(packet);

                    return true;
                }
            }

            return false;
        }

        internal struct SendAndReceiveState
        {
            public TaskCompletionSource<TcpPacket> Tcs;
            public ushort TransactionId;
        }
    }

    public class TcpPacket
    {
        public byte[] Data;
        public int Offset;
        public int Size;
    }
}
