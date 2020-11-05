﻿#if MOTHBALL

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
        public delegate Task<byte[]> ProcessAsync(byte[] data, CancellationToken token);

        public ProcessAsync ProcessRequestAsyncCallback;

        private bool _stop;
        private CancellationTokenSource _cts;

        private readonly TcpSocketListener _listener;

        private readonly List<TcpReceiveBuffer> _receiveBuffers;
        private readonly ILogger _logger;

        private readonly object _receiveBufferLock = new object();
        private readonly object _streamWriteLock = new object();

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

            this._stop = true;

            // Default to the minimum available worker threads (min threads seems to == core count).
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            MaxConcurrentRequests = workerThreads;
        }

        public void Start(IPEndPoint endPoint)
        {
            if (!this._stop)
            {
                throw new InvalidOperationException($"{nameof(TcpServer)} already started.");
            }

            this._stop = false;

            this._cts = new CancellationTokenSource();

            this._listener.Start(endPoint);

            var serverThread = new Thread(Run);
            serverThread.Start();
        }

        public void Stop()
        {
            this._stop = true;
            this._cts.Cancel();
        }

        private async static Task HandleRequestAsync(ProcessState state, CancellationToken token)
        {
            var responseData = await state.RequestCallbackAsync(state.Data, token).ConfigureAwait(false);

            if (responseData != null)
            {
                try
                {
                    lock (state.StreamWriteLock)
                    {
                        state.Stream.WriteFrame(
                            transactionId: state.TransactionId,
                            data: responseData,
                            offset: 0,
                            count: (ushort)responseData.Length);
                    }
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
            RunCore(this._cts.Token);
        }

        private void RunCore(CancellationToken token)
        {
            Task[] tasks = new Task[MaxConcurrentRequests];
            ProcessState[] states = new ProcessState[MaxConcurrentRequests];
            int taskCount = 0;

            while (!this._stop)
            {
                Lock();

                try
                {
                    foreach (var buffer in this._receiveBuffers)
                    {
                        int readCount = 0;
                        do
                        {
                            if (this._stop || token.IsCancellationRequested)
                            {
                                return;
                            }

                            if (buffer.GetReadData(out byte[] data, out int offset, out int count))
                            {
                                var dataCopy = new byte[count];
                                Array.Copy(data, offset, dataCopy, 0, count);

                                buffer.GetState(out NetworkStream stream, out ushort transactionId);
                                buffer.NextRead();

                                if (taskCount == tasks.Length)
                                {
                                    // Clean up completed tasks

                                    Task.WaitAny(tasks);

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
                                state.StreamWriteLock = this._streamWriteLock;
                                state.RequestCallbackAsync = ProcessRequestAsyncCallback;
                                state.Data = dataCopy;
                                state.Stream = stream;
                                state.TransactionId = transactionId;
                                state.Logger = _logger;

                                tasks[taskCount++] = HandleRequestAsync(state, token);
                            }
                        } while (++readCount < MaxConcurrentRequests); // Limit request throughput per client 
                    }
                }
                finally
                {
                    Unlock();
                }
            }
        }

        private void Lock()
        {
            lock (this._receiveBufferLock)
            {
                this._lockCount++;
            }
        }

        private void Unlock()
        {
            lock (this._receiveBufferLock)
            {
                if (this._lockCount == 1)
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

                this._lockCount--;
            }
        }

        private void Clients_OnClientRemoved(object sender, TcpSocketListener.TcpClientsEventArgs e)
        {
            lock (this._receiveBufferLock)
            {
                if (this._lockCount > 0)
                {
                    _pendingRemoveBuffers.Add(e.ReceiveBuffer);
                }
                else
                {
                    _receiveBuffers.Remove(e.ReceiveBuffer);
                }
            }
        }

        private void Clients_OnClientAdded(object sender, TcpSocketListener.TcpClientsEventArgs e)
        {
            lock (this._receiveBufferLock)
            {
                if (this._lockCount > 0)
                {
                    _pendingAddBuffers.Add(e.ReceiveBuffer);
                }
                else
                {
                    _receiveBuffers.Add(e.ReceiveBuffer);
                }
            }
        }

        private struct ProcessState
        {
            public object StreamWriteLock;

            public ProcessAsync RequestCallbackAsync;
            public byte[] Data;
            public NetworkStream Stream;
            public ushort TransactionId;
            public ILogger Logger;
        }
    }
}
#endif