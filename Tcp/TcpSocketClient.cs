namespace Networking.Core
{
    using Common.Core;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    public class TcpSocketClient
    {
        /// <summary>
        /// Timeout for send and receive RTT.
        /// </summary>
        public int TimeoutInMilliseconds = 30000;

        private ushort _nextTransactionId = 0;

        private NetworkStream _stream;

        private RequestAsyncState[] _requestAsyncStates;
        private int _requestAsyncStateCount;

        private int[] _freeRequestAsyncStateIndices;
        private int _freeRequestAsyncStateCount;

        private readonly Dictionary<int, int> _idToRequestAsyncStateIndex;

        private readonly TcpClient _client;

        private readonly TcpReceiveBuffer _receiveBuffer;
        private readonly TcpStreamMessageReader _tcpReceiver;

        private readonly Action<object> OnCancelResponseTcs;

        private readonly object _stateLock;
        private readonly object _streamWriteLock;

        public TcpSocketClient(ILogger logger, int maxPacketSize, int packetQueueCapacity)
        {
            this._client = new TcpClient(AddressFamily.InterNetworkV6);

            this._receiveBuffer = new TcpReceiveBuffer(maxPacketSize, packetQueueCapacity);
            this._tcpReceiver = new TcpStreamMessageReader(logger, maxPacketSize, packetQueueCapacity);

            this._requestAsyncStates = new RequestAsyncState[packetQueueCapacity];
            this._freeRequestAsyncStateIndices = new int[packetQueueCapacity];
            this._idToRequestAsyncStateIndex = new Dictionary<int, int>(packetQueueCapacity);

            this._requestAsyncStateCount = 0;
            this._freeRequestAsyncStateCount = 0;

            this._receiveBuffer.OnWriteComplete = this.OnWriteComplete;
            this.OnCancelResponseTcs = OnSendAsyncCancellation;

            this._stateLock = new object();
            this._streamWriteLock = new object();
        }

        public void Connect(IPAddress[] ipAddresses, int port)
        {
            _client.Connect(ipAddresses, port);
            _stream = _client.GetStream();

            this._tcpReceiver.Start(this._stream, this._receiveBuffer);
        }

        public void Disconnect()
        {
            this._stream.Close();
            this._stream = null;
            this._client.Close();
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
                if (this._freeRequestAsyncStateCount > 0)
                {
                    index = this._freeRequestAsyncStateIndices[--this._freeRequestAsyncStateCount];
                }
                else
                {
                    if (this._requestAsyncStateCount == this._requestAsyncStates.Length)
                    {
                        Array.Resize(ref this._requestAsyncStates, 2 * this._requestAsyncStateCount);
                        Array.Resize(ref this._freeRequestAsyncStateIndices, 2 * this._requestAsyncStateCount);
                    }

                    index = this._requestAsyncStateCount++;
                }

                transactionId = this._nextTransactionId++;

                Debug.Assert(!this._idToRequestAsyncStateIndex.ContainsKey(transactionId));

                this._idToRequestAsyncStateIndex[transactionId] = index;
            }

            ref var sendAndReceiveData = ref this._requestAsyncStates[index];

            // Create and use a copy of the TCS as it can get deferenced in the sendAndReceive pool 
            // before we return.
            var tcs = new TaskCompletionSource<TcpResponseMessage>();

            sendAndReceiveData.Tcs = tcs;
            sendAndReceiveData.TransactionId = transactionId;

            var cts = new CancellationTokenSource(millisecondsDelay: TimeoutInMilliseconds);

            sendAndReceiveData.CancellationTokenSource = cts;

            _ = cts.Token.Register(
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
            lock (this._streamWriteLock)
            {
                this._stream.WriteFrame(transactionId, data, offset, count);
            }
        }

        private void OnSendAsyncCancellation(object obj)
        {
            lock (_stateLock)
            {
                ushort transactionId = (ushort)obj;

                if (this._idToRequestAsyncStateIndex.ContainsKey(transactionId))
                {
                    var index = this._idToRequestAsyncStateIndex[transactionId];

                    this._idToRequestAsyncStateIndex.Remove(transactionId);

                    this._freeRequestAsyncStateIndices[this._freeRequestAsyncStateCount++] = index;

                    ref var state = ref this._requestAsyncStates[index];

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
            NetworkStream stream,
            ushort transactionId)
        {
            lock (_stateLock)
            {
                if (this._idToRequestAsyncStateIndex.ContainsKey(transactionId))
                {
                    var index = this._idToRequestAsyncStateIndex[transactionId];

                    this._idToRequestAsyncStateIndex.Remove(transactionId);

                    this._freeRequestAsyncStateIndices[this._freeRequestAsyncStateCount++] = index;

                    ref var state = ref this._requestAsyncStates[index];

                    var tcs = state.Tcs;

                    state.Tcs = null; // for GC

                    if (!tcs.Task.IsCanceled)
                    {
                        // Dispose CTS
                        state.CancellationTokenSource.Dispose();
                        state.CancellationTokenSource = null; // for GC

                        var dataCopy = new byte[size];
                        Array.Copy(data, offset, dataCopy, 0, size);

                        // Successfully received response before cancellation.
                        tcs.SetResult(
                            new TcpResponseMessage
                            {
                                Data = dataCopy,
                                Offset = 0,
                                Size = size
                            });
                    }

                    return true;
                }
                else
                {
                    Debug.Assert(false);
                }
            }

            return false;
        }

        internal struct RequestAsyncState
        {
            public CancellationTokenSource CancellationTokenSource;
            public TaskCompletionSource<TcpResponseMessage> Tcs;
            public ushort TransactionId;
        }
    }
}
