using Common.Core;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Networking.Core
{
    public sealed class TcpServer
    {
        public delegate Task<byte[]> ProcessAsync(byte[] data);

        public ProcessAsync ProcessAsyncCallback;

        private CancellationToken _token;

        private readonly TcpSocketListener _listener;

        private readonly List<TcpReceiveBuffer> _receiveBuffers;
        private readonly ILogger _logger;

        private readonly object _lock = new object();

        private readonly List<TcpReceiveBuffer> _pendingAddBuffers = new List<TcpReceiveBuffer>();
        private readonly List<TcpReceiveBuffer> _pendingRemoveBuffers = new List<TcpReceiveBuffer>();
        private int _lockCount = 0;

        private readonly int MaxConcurrentRequests;

        public TcpServer(
            ILogger logger,
            int clientCapacity,
            int maxPacketSize,
            int packetQueueDepth)
        {
            this._logger = logger;

            this._receiveBuffers = new List<TcpReceiveBuffer>(clientCapacity);

            this._listener = new TcpSocketListener(
                logger,
                clientCapacity: clientCapacity,
                maxPacketSize: maxPacketSize,
                packetQueueCapacity: packetQueueDepth);

            this._listener.Clients.OnClientAdded += Clients_OnClientAdded;
            this._listener.Clients.OnClientRemoved += Clients_OnClientRemoved;

            // Default to the minimum available worker threads (min threads seems to == core count).
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            MaxConcurrentRequests = workerThreads;
        }

        public void Start(IPEndPoint endPoint, CancellationToken token)
        {
            this._token = token;

            this._listener.Start(endPoint);

            var serverThread = new Thread(Run);
            serverThread.Start();
        }

        //public void Stop()
        //{
        //    this._stop = true;
        //}

        private async static Task ProcessDelegateAsync(ProcessState state)
        {
            var responseData = await state.CallbackAsync(state.Data).ConfigureAwait(false);

            if (responseData != null)
            {
                var stream = state.RemoteClient.GetStream();

                try
                {
                    stream.WriteFrame(transactionId: state.TransactionId, data: responseData, offset: 0, count: (ushort)responseData.Length);
                }
                catch (Exception e)
                {
                    state.Logger.Verbose($"Failed to write to client: e={e}");
                }
            }
            else
            {
                state.Logger.Warning($"Failed to receive response data.");
            }
        }

        private void Run()
        {
            RunCore(_token);
        }

        private void RunCore(CancellationToken token)
        {
            Task[] tasks = new Task[MaxConcurrentRequests];
            ProcessState[] states = new ProcessState[MaxConcurrentRequests];
            int taskCount = 0;

            while (!token.IsCancellationRequested)
            {
                Lock();

                try
                {
                    foreach (var buffer in _receiveBuffers)
                    {
                        while (buffer.GetReadData(out byte[] data, out int offset, out int count))
                        {
                            var dataCopy = new byte[count];
                            Array.Copy(data, offset, dataCopy, 0, count);

                            buffer.GetState(out TcpClient remoteClient, out ushort transactionId);
                            buffer.NextRead(closeConnection: false);

                            if (taskCount == tasks.Length)
                            {
                                // Clean up completed tasks

                                Task.WhenAny(tasks).Wait();

                                for (int i = taskCount - 1; i >= 0; i--)
                                {
                                    if (tasks[i].IsCompleted)
                                    {
                                        if (i != taskCount - 1)
                                        {
                                            tasks[i] = tasks[taskCount - 1];
                                        }

                                        taskCount--;
                                    }
                                }
                            }

                            ref var state = ref states[taskCount];
                            state.CallbackAsync = ProcessAsyncCallback;
                            state.Data = dataCopy;
                            state.RemoteClient = remoteClient;
                            state.TransactionId = transactionId;
                            state.Logger = _logger;

                            tasks[taskCount++] = ProcessDelegateAsync(state);
                        }
                    }
                }
                finally
                {
                    Unlock();
                }
            }
        }

        private void Lock() => this._lockCount++;

        private void Unlock()
        {
            if (this._lockCount == 1)
            {
                lock (this._lock)
                {
                    foreach (var receiveBuffer in this._pendingRemoveBuffers)
                    {
                        _receiveBuffers.Remove(receiveBuffer);
                    }

                    this._pendingRemoveBuffers.Clear();

                    foreach (var receiveBuffer in this._pendingAddBuffers)
                    {
                        _receiveBuffers.Add(receiveBuffer);
                    }

                    this._pendingAddBuffers.Clear();
                }
            }

            this._lockCount--;
        }

        private void Clients_OnClientRemoved(object sender, TcpSocketListener.TcpClientsEventArgs e)
        {
            if (this._lockCount > 0)
            {
                lock (this._lock)
                {
                    _pendingRemoveBuffers.Add(e.ReceiveBuffer);
                }
            }
        }

        private void Clients_OnClientAdded(object sender, TcpSocketListener.TcpClientsEventArgs e)
        {
            if (this._lockCount > 0)
            {
                lock (this._lock)
                {
                    _pendingAddBuffers.Add(e.ReceiveBuffer);
                }
            }
        }

        private struct ProcessState
        {
            public ProcessAsync CallbackAsync;
            public byte[] Data;
            public TcpClient RemoteClient;
            public ushort TransactionId;
            public ILogger Logger;
        }
    }
}
