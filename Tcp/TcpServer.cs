using Common.Core;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Networking.Core
{
    public sealed class TcpServer
    {
        public delegate byte[] ProcessRequestDelegate(byte[] requestData);

        private ProcessRequestDelegate ProcessRequestCallback;

        private bool _stop;

        private readonly TcpSocketListener _listener;

        private readonly ILogger _logger;

        private readonly object _streamWriteLock = new object();

        public TcpServer(
            ILogger logger,
            int clientCapacity,
            int maxPacketSize,
            int packetQueueDepth)
        {
            this._logger = logger;

            this._listener = new TcpSocketListener(
                logger,
                clientCapacity: clientCapacity,
                maxMessageSize: maxPacketSize,
                messageQueueCapacity: packetQueueDepth);

            this._stop = true;
        }

        public void Start(IPEndPoint endPoint, ProcessRequestDelegate processRequestCallback)
        {
            if (!this._stop)
            {
                throw new InvalidOperationException($"{nameof(TcpServer)} already started.");
            }

            this._stop = false;

            this.ProcessRequestCallback = processRequestCallback;
            this._listener.Start(endPoint, this.HandleMessageCallback);
        }

        public void Stop()
        {
            this._stop = true;

            this._listener.Stop();
        }

        private async void HandleMessageCallback(byte[] requestData, NetworkStream stream, ushort transactionId)
        {
            var responseData = this.ProcessRequestCallback(requestData);

            try
            {
                lock (this._streamWriteLock)
                {
                    stream.WriteFrame(
                        transactionId: transactionId,
                        data: responseData,
                        offset: 0,
                        count: (ushort)responseData.Length);
                }
            }
            catch (Exception e)
            {
                this._logger.Verbose($"Failed to write to client: e={e}");
            }
        }
    }
}
