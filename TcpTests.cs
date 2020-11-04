//#define PRINT_DATA

using Common.Core;
using MessagePack;
using Networking.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CodeTestsConsole
{
    public static class TcpTests
    {
        internal static ILogger Logger = new ConsoleLogger(maxLevel: 5);

        //internal static ServerProcessor Processor;

        internal static Random Random = new Random();

        public static void TcpClientServerSendReceive()
        {
            RunAsync().Wait();
        }

        private static async Task<byte[]> ProcessAsync(byte[] data)
        {
            var requestMessage = MessagePackSerializer.Deserialize<SampleDataMsgPack>(
                new ReadOnlyMemory<byte>(data, 0, data.Length));

            var responseMessage = new SampleDataMsgPack
            {
                ClientId = requestMessage.ClientId,
                MyInt = requestMessage.MyInt,
                MyString = "The quick brown fox jumped over the lazy dogs.",
            };

            return MessagePackSerializer.Serialize(responseMessage);
        }

        private static async Task RunAsync()
        {
            // Testing constants:

            const int MaxPacketSize = 256;
            const int PacketQueueCapacity = 256;

            const int TestRequestCount = 100000;
            const int ClientCount = 10;
            const int ConcurrentRequestCount = 100;

            // Start TCP server.

            //IPAddress.Parse("127.0.0.1")
            var addressList = Dns.GetHostEntry("localhost").AddressList;
            var ipAddress = addressList.First();
            var serverEndpoint = new IPEndPoint(ipAddress, 27005);

            //Processor = new ServerProcessor(Logger, capacity: 2);

            var server = new TcpServer(Logger, clientCapacity: 2, maxPacketSize: MaxPacketSize, packetQueueDepth: PacketQueueCapacity);

            var cts = new CancellationTokenSource();

            server.ProcessAsyncCallback = ProcessAsync;

            server.Start(serverEndpoint, cts.Token);


            // TCP clients connecto to server.

            var clients = new TcpSocketClient[ClientCount];

            for (int i= 0; i < clients.Length; i++)
            {
                clients[i] = new TcpSocketClient(Logger, maxPacketSize: MaxPacketSize, packetQueueCapacity: PacketQueueCapacity);
                clients[i].Connect(new IPAddress[] { serverEndpoint.Address }, serverEndpoint.Port);
            }

            // Send requests from clients to server.

            Logger.Info($"Executing {TestRequestCount:N0} send/receive requests: clientCount={ClientCount}, concurrentRequests={ConcurrentRequestCount}");

            using (var sw = new LoggerStopWatch(Logger))
            {
                var tasks = new List<Task<TcpResponseMessage>>();
                var clientIndices = new List<int>();

                for (int i = 0; i < TestRequestCount; i++)
                {
                    var clientIndex = Random.Next() % clients.Length;

                    //if (i % 5000 == 0)
                    if (false && (i % 10000) == (Math.Abs((Random.Next()) % 10000)))
                    {
                        clients[clientIndex].Disconnect();

                        clients[clientIndex] = new TcpSocketClient(Logger, maxPacketSize: MaxPacketSize, packetQueueCapacity: PacketQueueCapacity);
                        clients[clientIndex].Connect(new IPAddress[] { serverEndpoint.Address }, serverEndpoint.Port);
                    }

                    var client = clients[clientIndex];

                     var sendMessage = new SampleDataMsgPack
                    {
                        ClientId = clientIndex,
                        MyInt = i,
                        TransactionId = 1337,
                        MyString = "abcdefghijklmnopqrstuvwxyz.",
                    };

                    var sendData = MessagePackSerializer.Serialize(sendMessage);

#if PRINT_DATA
                    Logger.Info($"Client {clientIndex} Sending: {sendMessage}");
#endif //PRINT_DATA

                    //var tcpPacket = await client.SendAsync(sendData, 0, (ushort)sendData.Length);

                    //var receivedObj = MessagePackSerializer.Deserialize<SampleDataMsgPack>(tcpPacket.Data);


                    tasks.Add(client.SendAsync(sendData, 0, (ushort)sendData.Length));
                    clientIndices.Add(clientIndex);

                    if (tasks.Count == ConcurrentRequestCount)
                    {
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
                            var receivedMessage = MessagePackSerializer.Deserialize<SampleDataMsgPack>(
                                new ReadOnlyMemory<byte>(
                                    task.Result.Data, 
                                    task.Result.Offset, 
                                    task.Result.Size));
#if PRINT_DATA
                            Logger.Info($"Client {receivedMessage.ClientId} Receive: {receivedMessage}");
#endif //PRINT_DATA
                        }

                        tasks.Clear();
                        clientIndices.Clear();
                    }

#if PRINT_DATA
                        //Logger.Info($"Client {clientIndex} Receive: {receivedObj}");
#endif //PRINT_DATA
                }
            }

            Logger.Info("Client counts:");

            //for (int i = 0; i < clients.Length; i++)
            //{
            //    Logger.Info($"Client {i}: {(Processor.ClientCounts.ContainsKey(i) ? Processor.ClientCounts[i] : 0)}");
            //}


            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].Disconnect();
            }


            cts.Cancel();
            //Processor.Stop = true;

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
            //server.Stop();

            //Thread.Sleep(250);

            //int iii = 0;
        }
#if MOTHBALL
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

            private readonly object _lock = new object();

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
                while (!Stop)
                {
                    lock (_lock)
                    {
                        foreach (var buffer in Buffers)
                        {
                            if (buffer.GetReadData(out byte[] data, out int offset, out int count))
                            {
                                var receivedMessage = MessagePackSerializer.Deserialize<SampleDataMsgPack>(new ReadOnlyMemory<byte>(data, offset, count));

                                if (!ClientCounts.ContainsKey(receivedMessage.ClientId))
                                {
                                    ClientCounts[receivedMessage.ClientId] = 0;
                                }

                                ClientCounts[receivedMessage.ClientId] = ClientCounts[receivedMessage.ClientId] + 1;

                                buffer.GetState(out TcpClient remoteClient, out ushort transactionId);

                                buffer.NextRead(closeConnection: false);

                                if (!remoteClient.Connected)
                                {
                                    continue;
                                }


                                receivedMessage.TransactionId = transactionId;

#if PRINT_DATA
                                _logger.Info($"Server received: {receivedMessage}");
#endif //PRINT_DATA


                                var stream = remoteClient.GetStream();

                                var sendMessage = new SampleDataMsgPack
                                {
                                    ClientId = receivedMessage.ClientId,
                                    TransactionId = transactionId,
                                    MyInt = receivedMessage.MyInt,
                                    MyString = "The quick brown fox jumped over the lazy dogs.",
                                };

                                var sendMessageBytes = MessagePackSerializer.Serialize(sendMessage);

                                try
                                {
                                    stream.WriteFrame(transactionId: transactionId, data: sendMessageBytes, offset: 0, count: (ushort)sendMessageBytes.Length);
                                }
                                catch (Exception e)
                                {
                                    Logger.Verbose($"Failed to write to client: e={e}");
                                }
                            }
                        }
                    }
                }
            }
        }
#endif


        [MessagePackObject]
        public class SampleDataMsgPack
        {
            [Key(0)]
            public int ClientId;

            [Key(1)]
            public ushort TransactionId;

            [Key(2)]
            public int MyInt;

            [Key(3)]
            public string MyString;

            public override string ToString()
            {
                return $"ClientId={ClientId}, TransactionId={TransactionId}, MyInt={MyInt}, MyString={MyString}";
            }
        }
    }
}

