namespace Networking.Core
{
    using Common.Core;
    using System;
    using System.Collections.Generic;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    public class TcpSocketClient
    {
        /// <summary>
        /// Timeout for send and receive RTT.
        /// </summary>
        public int TimeoutInMilliseconds = 30000;

        private TcpClient _client;
        private NetworkStream _stream;

        private readonly TcpReceiveBuffer _receiveBuffer;
        private readonly TcpStreamMessageReader _tcpReceiver;

        private ushort _nextTransactionId = 0;

        private object _stateLock = new object();
        private object _streamWriteLock = new object();

        private SendAndReceiveState[] _sendAndReceiveStates;
        private int _sendAndReceiveStateCount;

        private int[] _freeSendAndReceiveStateIndices;
        private int _freeSendAndReceiveStateCount;

        private Dictionary<int, int> _transactionIdToStateIndex;

        private readonly Action<object> OnCancelResponseTcs;

        public TcpSocketClient(ILogger logger, int maxPacketSize, int packetQueueCapacity)
        {
            this._client = new TcpClient();

            this._receiveBuffer = new TcpReceiveBuffer(maxPacketSize, packetQueueCapacity);
            this._tcpReceiver = new TcpStreamMessageReader(logger, maxPacketSize, packetQueueCapacity);

            this._sendAndReceiveStates = new SendAndReceiveState[packetQueueCapacity];
            this._freeSendAndReceiveStateIndices = new int[packetQueueCapacity];
            this._transactionIdToStateIndex = new Dictionary<int, int>(packetQueueCapacity);

            this._sendAndReceiveStateCount = 0;
            this._freeSendAndReceiveStateCount = 0;

            this._receiveBuffer.OnWriteComplete = this.OnWriteComplete;
            this.OnCancelResponseTcs = OnSendAsyncCancellation;
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

        /// <summary>
        /// Sends a TCP message. Returns response.
        /// </summary>
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

            var cancellationTokenSource = new CancellationTokenSource(millisecondsDelay: TimeoutInMilliseconds);

            sendAndReceiveData.CancellationTokenSource = cancellationTokenSource;

            _ = cancellationTokenSource.Token.Register(
                OnCancelResponseTcs, 
                transactionId);

            Post(transactionId, data, offset, count);

            return tcs.Task;
        }

        /// <summary>
        /// Posts a TCP message. This returns immediately with no response.
        /// </summary>
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

        private void OnSendAsyncCancellation(object obj)
        {
            lock (_stateLock)
            {
                ushort transactionId = (ushort)obj;

                if (this._transactionIdToStateIndex.ContainsKey(transactionId))
                {
                    var index = this._transactionIdToStateIndex[transactionId];

                    this._transactionIdToStateIndex.Remove(transactionId);

                    this._freeSendAndReceiveStateIndices[this._freeSendAndReceiveStateCount++] = index;

                    ref var state = ref this._sendAndReceiveStates[index];

                    var tcs = state.Tcs;

                    state.Tcs = null; // for GC

                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.TrySetCanceled();

                        // Dispose CTS
                        state.CancellationTokenSource.Dispose();
                        state.CancellationTokenSource = null; // for GC
                    }
                }
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

                    var responseMessage = new TcpResponseMessage
                    {
                        Data = dataCopy,
                        Offset = 0,
                        Size = size
                    };

                    this._transactionIdToStateIndex.Remove(transactionId);

                    this._freeSendAndReceiveStateIndices[this._freeSendAndReceiveStateCount++] = index;

                    ref var state = ref this._sendAndReceiveStates[index];

                    var tcs = state.Tcs;

                    state.Tcs = null; // for GC

                    if (!tcs.Task.IsCanceled)
                    {
                        // Dispose CTS
                        state.CancellationTokenSource.Dispose();
                        state.CancellationTokenSource = null; // for GC

                        // Successfully received response before cancellation.
                        tcs.SetResult(responseMessage);
                    }

                    return true;
                }
            }

            return false;
        }

        internal struct SendAndReceiveState
        {
            public CancellationTokenSource CancellationTokenSource;
            public TaskCompletionSource<TcpResponseMessage> Tcs;
            public ushort TransactionId;
        }
    }
}
