namespace Networking.Core
{
    using Common.Core;
    using System.Net.Sockets;

    internal sealed class TcpClientData
    {
        public TcpClient Client;
        public NetworkStream Stream;
        public TcpReceiveBuffer ReceiveBuffer;

        public AsyncCancelToken ReceiverCancelToken;

        public void ClearAndClose()
        {
            ReceiverCancelToken.Cancel();

            while (!ReceiverCancelToken.IsCanceled)
            { 
                break;  
            }

            Stream.Close();
            Stream = null;
            Client.Close();
            Client = null;
            ReceiveBuffer = null;
        }
    }
}
