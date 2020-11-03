namespace Networking.Core
{
    using Common.Core;
    using System;
    using System.Collections.Generic;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    public class TcpSocketClient
    {
        private TcpClient _client;
        private NetworkStream _stream;

        private readonly TcpReceiveBuffer _receiveBuffer;
        private readonly TcpStreamMessageReader _tcpReceiver;

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
            this._tcpReceiver = new TcpStreamMessageReader(logger, maxPacketSize, packetQueueCapacity);

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

        public Task<TcpResponseMessage> SendAsync(
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
            var tcs = new TaskCompletionSource<TcpResponseMessage>();

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

                    var packet = new TcpResponseMessage
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
            public TaskCompletionSource<TcpResponseMessage> Tcs;
            public ushort TransactionId;
        }
    }
}
