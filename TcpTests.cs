//#define PRINT_DATA

using MessagePack;
using Networking.Core;
using System;
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

        internal static Random Random = new Random();

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
            //var sendStr = $"Client Message: {new string('c', 128)}";

            // Send requests from clients to server.

            //byte[] sendData = new byte[256];

            const int TestRequestCount = 10000;

            Logger.Info($"Executing {TestRequestCount:N0} send/receive requests for {ClientCount} clients.");

            using (var sw = new LoggerStopWatch(Logger))
            {
                for (int i = 0; i < TestRequestCount; i++)
                {
                    var clientIndex = Random.Next() % clients.Length;
                    var client = clients[clientIndex];

                    var sendData = MessagePackSerializer.Serialize(
                        new SampleDataMsgPack
                        {
                            ClientId = clientIndex,
                            MyFloat = 456.0f,
                            MyString = "abcdefghijklmnopqrstuvwxyz.",
                        });

                    client.Send(sendData, 0, (ushort)sendData.Length);

                    byte[] recvData = new byte[12000];

                    client.Read(recvData, 0, recvData.Length, out int receiveBytes);

                    var receivedObj = MessagePackSerializer.Deserialize<SampleDataMsgPack>(recvData);

                    //serializer.Deserialize(recvData, 0, receiveBytes, out string recvText);
#if PRINT_DATA
                        Logger.Info($"Client {j} Receive: {receivedObj}");
#endif //PRINT_DATA

#if MOTHBALL
                    for (int j = 0; j < clients.Length; j++)
                    {
                        var client = clients[j];

                        //serializer.Serialize(sendStr, sendData, out int sendDataCount);
                        //client.Send(sendData, 0, sendDataCount);

                        var sendData = MessagePackSerializer.Serialize(msgPackData);

                        client.Send(sendData, 0, (ushort) sendData.Length);

                        byte[] recvData = new byte[12000];

                        client.Read(recvData, 0, recvData.Length, out int receiveBytes);

                        var receivedObj = MessagePackSerializer.Deserialize<SampleDataMsgPack>(recvData);

                        //serializer.Deserialize(recvData, 0, receiveBytes, out string recvText);
#if PRINT_DATA
                        Logger.Info($"Client {j} Receive: {receivedObj}");
#endif //PRINT_DATA
                }
#endif
                }
            }

            Logger.Info("Client counts:");

            for (int i = 0; i < clients.Length; i++)
            {
                Logger.Info($"Client {i}: {(Processor.ClientCounts.ContainsKey(i) ? Processor.ClientCounts[i] : 0)}");
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

            public readonly Dictionary<int, int> ClientCounts = new Dictionary<int, int>();

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
                                //serializer.Deserialize(data, 0, count, out string recvText);

                                var dataObj = MessagePackSerializer.Deserialize<SampleDataMsgPack>(data);

                                if (!ClientCounts.ContainsKey(dataObj.ClientId))
                                {
                                    ClientCounts[dataObj.ClientId] = 0;
                                }

                                ClientCounts[dataObj.ClientId] = ClientCounts[dataObj.ClientId] + 1;

#if PRINT_DATA
                        _logger.Info($"Server received: {dataObj}");
#endif //PRINT_DATA

                                buffer.GetClient(out _remoteClient);

                                buffer.NextRead(closeConnection: false);

                                var stream = _remoteClient.GetStream();

                                //var writeData = new byte[256];

                                //serializer.Serialize(sendStr, writeData, out int writeCount);

                                var writeData = MessagePackSerializer.Serialize(new SampleDataMsgPack
                                {
                                    ClientId = 321,
                                    MyFloat = 654,
                                    MyString = "The quick brown fox jumped over the lazy dogs.",
                                });

                                stream.WriteWithSizePreamble(writeData, 0, (ushort)writeData.Length);
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

        [MessagePackObject]
        public class SampleDataMsgPack
        {
            [Key(0)]
            public int ClientId;

            [Key(1)]
            public float MyFloat;

            [Key(2)]
            public string MyString;

            public override string ToString()
            {
                return $"ClientId={ClientId}, MyFloat={MyFloat}, MyString={MyString}";
            }
        }
    }
}

