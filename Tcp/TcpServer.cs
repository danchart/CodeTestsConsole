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

        private static readonly Action<object> ProcessDelegateAsyncCallback = ProcessDelegate;

        private CancellationToken _token;

        private readonly TcpSocketListener _listener;

        private readonly List<TcpReceiveBuffer> _receiveBuffers;
        private readonly ILogger _logger;

        private readonly object _lock = new object();

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

        private static void ProcessDelegate(object state)
        {
            var processState = (ProcessState)state;

            var responseData = processState.CallbackAsync(processState.Data).Result;

            if (responseData != null)
            {
                var stream = processState.RemoteClient.GetStream();

                try
                {
                    stream.WriteFrame(transactionId: processState.TransactionId, data: responseData, offset: 0, count: (ushort)responseData.Length);
                }
                catch (Exception e)
                {
                    processState.Logger.Verbose($"Failed to write to client: e={e}");
                }
            }
            else
            {
                processState.Logger.Warning($"Failed to receive response data.");
            }
        }

        private class ProcessState
        {
            public ProcessAsync CallbackAsync;
            public byte[] Data;
            public TcpClient RemoteClient;
            public ushort TransactionId;
            public ILogger Logger;
        }

        private void Run()
        {
            RunCore(_token);
        }

        private void RunCore(CancellationToken token)
        {
            HashSet<Task> tasks = new HashSet<Task>();

            while (!token.IsCancellationRequested)
            {
                lock (_lock)
                {
                    foreach (var buffer in _receiveBuffers)
                    {
                        while (buffer.GetReadData(out byte[] data, out int offset, out int count))
                        {
                            var dataCopy = new byte[count];
                            Array.Copy(data, offset, dataCopy, 0, count);

                            buffer.GetState(out TcpClient remoteClient, out ushort transactionId);
                            buffer.NextRead(closeConnection: false);

                            tasks.Add(
                                Task.Factory.StartNew(
                                    ProcessDelegateAsyncCallback,
                                    new ProcessState
                                    {
                                        CallbackAsync = ProcessAsyncCallback,
                                        Data = dataCopy,
                                        RemoteClient = remoteClient,
                                        TransactionId = transactionId,
                                        Logger = _logger,
                                    },
                                    token));

                            if (tasks.Count >= MaxConcurrentRequests)
                            {
                                var taskDone = Task.WhenAny(tasks).Result;

                                tasks.Remove(taskDone);
                            }
                        }
                    }
                }
            }
        }

        private void Clients_OnClientRemoved(object sender, TcpSocketListener.TcpClientsEventArgs e)
        {
            lock (_lock)
            {
                _receiveBuffers.Remove(e.ReceiveBuffer);
            }
        }

        private void Clients_OnClientAdded(object sender, TcpSocketListener.TcpClientsEventArgs e)
        {
            lock (_lock)
            {
                _receiveBuffers.Add(e.ReceiveBuffer);
            }
        }
    }
}
