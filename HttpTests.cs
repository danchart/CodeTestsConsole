using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CodeTestsConsole
{
    public static class HttpTests
    {
        public static void HttpClientServerSendReceive()
        {
            var logger = new ConsoleLogger();

            var endpoint = "http://localhost:27007/";

            var server = new HttpServer(
                new string[] { endpoint }, 
                logger);

            server.Start();

            WorkerAsync(endpoint, logger).Wait();
        }

        private static async Task WorkerAsync(string endpoint, ILogger logger)
        {
            const int RoundTripCount = 10000;

            logger.Info($"Executing {RoundTripCount:N0} send/receive requests.");

            using (var httpClient = new HttpClient())
            {
                using (var sw = new LoggerStopWatch(logger))
                {
                    for (int i = 0; i < RoundTripCount; i++)
                    {
                        var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, endpoint)
                        {
                            Content = new StringContent("Hello, world")
                        });

                        //logger.Info($"Received {response.Content.ReadAsStringAsync().Result}");
                    }
                }
            }
        }

        private class HttpServer
        {
            private readonly HttpListener _listener;

            private readonly ILogger _logger;

            public HttpServer(string[] prefixes, ILogger logger)
            {
                _logger = logger;

                _listener = new HttpListener();

                foreach (string s in prefixes)
                {
                    _listener.Prefixes.Add(s);
                }
            }

            public void Start()
            {
                _listener.Start();

                _logger.Info("HTTP listener started");

                _listener.BeginGetContext(new AsyncCallback(ListenerCallback), _listener);
            }

            public static void ListenerCallback(IAsyncResult result)
            {
                HttpListener listener = (HttpListener)result.AsyncState;
                // Call EndGetContext to complete the asynchronous operation.
                HttpListenerContext context = listener.EndGetContext(result);
                HttpListenerRequest request = context.Request;
                // Obtain a response object.
                HttpListenerResponse response = context.Response;

                // Construct a response.
                string responseString = "<HTML><BODY> Hello world!</BODY></HTML>";
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                // Get a response stream and write the response to it.
                response.ContentLength64 = buffer.Length;
                Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                // You must close the output stream.
                output.Close();

                listener.BeginGetContext(new AsyncCallback(ListenerCallback), listener);
            }
        }
    }
}
