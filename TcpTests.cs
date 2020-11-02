﻿//#define PRINT_DATA

using MessagePack;
using Networking.Core;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeTestsConsole
{
    public static class TcpTests
    {
        internal static ILogger Logger = new ConsoleLogger();

        internal static ServerProcessor Processor;

        internal static Random Random = new Random();

        public static void TcpClientServerSendReceive()
        {
            RunAsync().Wait();
        }

        private static async Task RunAsync()
        {
            // Testing constants:

            const int MaxPacketSize = 256;
            const int PacketQueueCapacity = 16;

            // Start TCP server.

            var serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 27005);

            var server = new TcpSocketListener(Logger, clientCapacity: 2, maxPacketSize: MaxPacketSize, packetQueueCapacity: PacketQueueCapacity);
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
                clients[i] = new TcpSocketClient(Logger, maxPacketSize: MaxPacketSize, packetQueueCapacity: PacketQueueCapacity);
                clients[i].Connect(serverEndpoint.Address.ToString(), serverEndpoint.Port);
            }

            // Send requests from clients to server.

            const int TestRequestCount = 100000;

            Logger.Info($"Executing {TestRequestCount:N0} send/receive requests for {ClientCount} clients.");

            using (var sw = new LoggerStopWatch(Logger))
            {
                var tasks = new List<Task<TcpPacket>>();
                var clientIndices = new List<int>();

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

                    //var tcpPacket = await client.SendAsync(sendData, 0, (ushort)sendData.Length);

                    //var receivedObj = MessagePackSerializer.Deserialize<SampleDataMsgPack>(tcpPacket.Data);


                    tasks.Add(client.SendAsync(sendData, 0, (ushort)sendData.Length));
                    clientIndices.Add(clientIndex);

                    if (tasks.Count == 10)
                    {
                        //Logger.Info($"Before {i} frames.");

                        var taskSendAll = Task.WhenAll(tasks);

                        await Task.WhenAny(
                            taskSendAll,
                            Task.Delay(250));

                        if (!taskSendAll.IsCompleted)
                        {
                            Logger.Error($"Failed to complete all sends.");

                            tasks.Clear();
                            clientIndices.Clear();

                            continue;
                        }

                        foreach (var task in tasks)
                        {
                            var receivedObj = MessagePackSerializer.Deserialize<SampleDataMsgPack>(task.Result.Data);
#if PRINT_DATA
                            Logger.Info($"Client {receivedObj.ClientId} Receive: {receivedObj}");
#endif //PRINT_DATA
                        }

                        tasks.Clear();
                        clientIndices.Clear();

                        //Logger.Info($"After {i} frames.");
                    }

#if PRINT_DATA
                        //Logger.Info($"Client {clientIndex} Receive: {receivedObj}");
#endif //PRINT_DATA
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
                                var receivedMessage = MessagePackSerializer.Deserialize<SampleDataMsgPack>(data);

                                if (!ClientCounts.ContainsKey(receivedMessage.ClientId))
                                {
                                    ClientCounts[receivedMessage.ClientId] = 0;
                                }

                                ClientCounts[receivedMessage.ClientId] = ClientCounts[receivedMessage.ClientId] + 1;

#if PRINT_DATA
                        _logger.Info($"Server received: {dataObj}");
#endif //PRINT_DATA

                                buffer.GetState(out TcpClient remoteClient, out ushort transactionId);

                                buffer.NextRead(closeConnection: false);

                                var stream = remoteClient.GetStream();

                                var sendMessage = MessagePackSerializer.Serialize(
                                    new SampleDataMsgPack
                                    {
                                        ClientId = receivedMessage.ClientId,
                                        MyFloat = 654,
                                        MyString = "The quick brown fox jumped over the lazy dogs.",
                                    });

                                stream.WriteFrame(transactionId: transactionId, data: sendMessage, offset: 0, count: (ushort)sendMessage.Length);
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

