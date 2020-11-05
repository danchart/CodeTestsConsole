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

        internal static Random Random = new Random();

        public static void TcpClientServerSendReceive()
        {
            RunAsync().Wait();
        }

        private static async Task<byte[]> ProcessAsync(byte[] data, CancellationToken token)
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
            const int ClientCount = 5;
            const int ConcurrentRequestCount = 100;

            // Start TCP server.

            //IPAddress.Parse("127.0.0.1")
            var addressList = Dns.GetHostEntry("localhost").AddressList;
            var ipAddress = addressList.First();
            var serverEndpoint = new IPEndPoint(ipAddress, 27005);

everted             var server = new TcpServer(Logger, clientCapacity: 2, maxPacketSize: MaxPacketSize, packetQueueDepth: PacketQueueCapacity);

            server.Start(serverEndpoint, ProcessAsync);

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

                    tasks.Add(client.SendAsync(sendData, 0, (ushort)sendData.Length));
                    clientIndices.Add(clientIndex);

                    if (tasks.Count == ConcurrentRequestCount)
                    {
                        var taskSendAll = Task.WhenAll(tasks);

                        await Task.WhenAny(
                            taskSendAll,
                            Task.Delay(250)).ConfigureAwait(false);

                        if (!taskSendAll.IsCompleted)
                        {
                            Logger.Error($"Failed to complete all sends: count={tasks.Count()}, failedCount={tasks.Where(x => !x.IsCompleted).Count()}");

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


            server.Stop();

        }

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

