﻿namespace Networking.Core
{
    using Common.Core;
    using System;
    using System.Net;
    using System.Net.Sockets;

    internal sealed class TcpSocketListener
    {
        public readonly TcpClients Clients;

        private TcpListener _listener;

        private AsyncCancelToken _cancelToken;

        private readonly TcpStreamMessageReader _tcpReceiver;

        private readonly ILogger _logger;

        private readonly int MaxPacketSize, PacketQueueCapacity;

        public TcpSocketListener(ILogger logger, int clientCapacity, int maxPacketSize, int packetQueueCapacity)
        {
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

            this.Clients = new TcpClients(clientCapacity);
            this._tcpReceiver = new TcpStreamMessageReader(logger, maxPacketSize, packetQueueCapacity);

            this.MaxPacketSize = maxPacketSize;
            this.PacketQueueCapacity = packetQueueCapacity;
        }

        public void Start(IPEndPoint ipEndPoint)
        {
            // From: https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.tcplistener?view=netcore-3.1

            this._listener = new TcpListener(ipEndPoint);
            this._cancelToken = new AsyncCancelToken();

            // Start listening for client requests.
            this._listener.Start();

            this._logger.Info("Waiting for a connection...");

            this._listener.BeginAcceptTcpClient(
                AcceptClient,
                new AcceptClientState
                {
                    Listener = this._listener,
                    StreamMessageReader = this._tcpReceiver,
                    Clients = this.Clients,

                    MaxPacketSize = this.MaxPacketSize,
                    PacketQueueCapacity = this.PacketQueueCapacity,

                    Logger = this._logger,

                    CancelToken = this._cancelToken,
                });
        }

        public void Stop()
        {
            // We need to flag the stop condition because the listener continues to dispatch the Accept callback
            // after TcpListener.Stop().
            this._cancelToken.Cancel();

            while (!this._cancelToken.IsCanceled)
            { }

            this.Clients.Stop();
            this._listener.Stop();
        }

        private static void AcceptClient(IAsyncResult ar)
        {
            var state = (AcceptClientState)ar.AsyncState;

            if (state.CancelToken.IsCanceled)
            {
                return;
            }

            TcpClient client = state.Listener.EndAcceptTcpClient(ar);
            NetworkStream stream = client.GetStream();
            TcpReceiveBuffer buffer = 
                new TcpReceiveBuffer(
                    maxPacketSize: state.MaxPacketSize, 
                    packetQueueCapacity: state.PacketQueueCapacity);

            var clientData = new TcpClientData
            {
                Client = client,
                Stream = stream,
                ReceiveBuffer = buffer,
            };
            state.Clients.Add(clientData);

            state.StreamMessageReader.Start(stream, clientData);

            state.Logger.Info($"Connected. RemoteEp={client.Client.RemoteEndPoint}");

            // Begin waiting for the next request.
            state.Listener.BeginAcceptTcpClient(AcceptClient, state);
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

            public void Stop()
            {
                Lock();

                try
                {
                    for (int i = this._count - 1; i >= 0; i--)
                    {
                        RemoveAndCloseLockFree(i);
                    }
                }
                finally
                {
                    Unlock();
                }
            }

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
                            RemoveAndCloseLockFree(this._pendingRemoveIndices[i]);
                        }

                        this._pendingRemoveCount = 0;

                        // Process add list

                        for (int i = 0; i < this._pendingAddCount; i++)
                        {
                            AddLockFree(this._pendingAddClients[i]);

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

                AddLockFree(clientData);
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

                RemoveAndCloseLockFree(index);
            }

            private void AddLockFree(TcpClientData clientData)
            {
                if (this._count == this._clients.Length)
                {
                    Array.Resize(ref this._clients, 2 * _count);
                }

                this._clients[this._count++] = clientData;

                // Raise add event
                this.OnClientAdded?.Invoke(this, new TcpClientsEventArgs(clientData.ReceiveBuffer));
            }

            private void RemoveAndCloseLockFree(int index)
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

        private sealed class AcceptClientState
        {
            public TcpListener Listener;
            public TcpStreamMessageReader StreamMessageReader;
            public TcpClients Clients;

            public AsyncCancelToken CancelToken;

            public ILogger Logger;

            public int MaxPacketSize;
            public int PacketQueueCapacity;
        }
    }
}
