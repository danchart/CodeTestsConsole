﻿namespace Networking.Core
{
    using System.Net.Sockets;

    internal sealed class TcpClientData
    {
        public TcpClient Client;
        public NetworkStream Stream;

        public void ClearAndClose()
        {
            Stream.Close();
            Stream = null;
            Client.Close();
            Client = null;
        }
    }
}
