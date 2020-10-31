//#define PRINT_DATA

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CodeTestsConsole
{
    public static class TcpTests
    {
        internal static ILogger Logger = new ConsoleLogger();

        internal static ServerProcessor Processor;

        public static void TcpClientServerSendReceive()
        {
            // Start TCP server.

            var serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 27005);

            var server = new TcpSocketListener(Logger, clientCapacity: 2, maxPacketSize: 256, packetQueueCapacity: 4);
            server.Start(serverEndpoint);

            Processor = new ServerProcessor(Logger, capacity: 2);

            server.Clients.OnClientAdded += Clients_OnClientAdded;
            server.Clients.OnClientRemoved += Clients_OnClientRemoved;

            var serverThread = new Thread(Processor.Run);
            serverThread.Start();

            // TCP clients connecto to server.

            const int ClientCount = 10;

            var clients = new TcpSocketClient[ClientCount];

            for (int i= 0; i < clients.Length; i++)
            {
                clients[i] = new TcpSocketClient(Logger);
                clients[i].Connect(serverEndpoint.Address.ToString(), serverEndpoint.Port);
            }

            var serializer = new Serializer();
            var sendStr = $"Client Message: {new string('c', 128)}";

            // Send requests from clients to server.

            byte[] sendData = new byte[256];

            const int TestRequestCount = 10000;

            Logger.Info($"Executing {TestRequestCount:N0} send/receive requests for {ClientCount} clients.");

            using (var sw = new LoggerStopWatch(Logger))
            {
                for (int i = 0; i < TestRequestCount; i++)
                {
                    for (int j = 0; j < clients.Length; j++)
                    {
                        var client = clients[j];

                        serializer.Serialize(sendStr, sendData, out int sendDataCount);

                        client.Send(sendData, 0, sendDataCount);

                        byte[] recvData = new byte[12000];

                        client.Read(recvData, 0, recvData.Length, out int receiveBytes);

                        serializer.Deserialize(recvData, 0, receiveBytes, out string recvText);
#if PRINT_DATA
                        Logger.Info($"Client {j} Receive: {recvText}");
#endif //PRINT_DATA
                    }
                }
            }

            Processor.Stop = true;

            //while (thread.ThreadState != ThreadState.Stopped) { }

#if NEVER


            var bytesSend = Encoding.ASCII.GetBytes("Hello, world");
            client.Send(bytesSend, 0, bytesSend.Length);

            WaitForReceive(serverBuffer, 1, server);

            Debug.Assert(1 == serverBuffer.Count);

            byte[] data = new byte[256];
            int offset, count;
            serverBuffer.GetReadData(out data, out offset, out count);

            var receivedStr = Encoding.ASCII.GetString(data, offset, count);
            Debug.Assert("Hello, world" == receivedStr);

            serverBuffer.GetClient(out TcpClient tcpClient);

            var stream = tcpClient.GetStream();

            var bytesSend2 = Encoding.ASCII.GetBytes("I'm server");
            stream.Write(bytesSend2, 0, bytesSend2.Length);

            {
                var bs = Encoding.ASCII.GetBytes("This is packet # 2");
                stream.Write(bs, 0, bs.Length);
            }

            Thread.Sleep(100);

            client.Read(data, 0, data.Length, out count);

            var receivedStr2 = Encoding.ASCII.GetString(data, offset, count);

            Debug.Assert("I'm server" == receivedStr2);
#endif
            server.Stop();
        }

        private static void Clients_OnClientRemoved(object sender, TcpSocketListener.TcpClientsEventArgs e)
        {
            Processor.Remove(e.ReceiveBuffer);
        }

        private static void Clients_OnClientAdded(object sender, TcpSocketListener.TcpClientsEventArgs e)
        {
            Processor.Add(e.ReceiveBuffer);
        }

        internal class ServerProcessor
        {
            public bool Stop = false;

            private readonly List<TcpReceiveBuffer> Buffers;
            private readonly ILogger _logger;

            private TcpClient _remoteClient;

            private static object _lock = new object();

            public ServerProcessor(ILogger logger, int capacity)
            {
                _logger = logger;

                Buffers = new List<TcpReceiveBuffer>(capacity);
            }

            public void Add(TcpReceiveBuffer buffer)
            {
                lock (_lock)
                {
                    Buffers.Add(buffer);
                }
            }

            public void Remove(TcpReceiveBuffer buffer)
            {
                lock (_lock)
                {
                    Buffers.Remove(buffer);
                }
            }

            public void Run()
            {
                var serializer = new Serializer();

                var sendStr = $"Server Message: {new string('s', 128)}";

                while (!Stop)
                {
                    lock (_lock)
                    {
                        foreach (var buffer in Buffers)
                        {
                            if (buffer.GetReadData(out byte[] data, out int offset, out int count))
                            {
                                serializer.Deserialize(data, 0, count, out string recvText);
#if PRINT_DATA
                        _logger.Info($"Server received: {recvText}");
#endif //PRINT_DATA

                                buffer.GetClient(out _remoteClient);

                                buffer.NextRead(closeConnection: false);

                                var stream = _remoteClient.GetStream();

                                var writeData = new byte[256];

                                serializer.Serialize(sendStr, writeData, out int writeCount);

                                stream.Write(writeData, 0, writeCount);
                            }
                        }
                    }
                }
            }
        }

        private class Serializer
        {
            public void Serialize(string text, byte[] data, out int count)
            {
                count = Encoding.ASCII.GetBytes(text, 0, text.Length, data, 1);

                data[0] = (byte)count;

                count++; // length prefix
            }

            public int Deserialize(byte[] data, int offset, int count, out string text)
            {
                var byteCount = data[0];

                text = Encoding.ASCII.GetString(data, 1, byteCount);
                offset = byteCount;

                return offset + byteCount;
            }
        }
    }
}

