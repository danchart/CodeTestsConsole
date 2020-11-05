namespace Networking.Core
{
    using Common.Core;
    using System;
    using System.Net.Sockets;

    internal sealed class TcpStreamMessageReader
    {
        public delegate void HandleMessageCallback(byte[] data, NetworkStream stream, ushort transactionId);

        public HandleMessageCallback HandleMessage;

        public const int FrameHeaderSizeByteCount = sizeof(ushort) + sizeof(ushort); // Frame size + transaction id

        private bool _started;

        private readonly int MaxPacketSize;
        private readonly int MaxPacketCapacity;

        private readonly ILogger _logger;

        public TcpStreamMessageReader(ILogger logger, int maxMessageSize, int maxMessageCapacity)
        {
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

            this.MaxPacketSize = maxMessageSize;
            this.MaxPacketCapacity = maxMessageCapacity;

            this._started = false;
        }

        public void Start(
            NetworkStream stream, 
            HandleMessageCallback handleMessageCallback)
        {
            if (this._started)
            {
                throw new InvalidOperationException($"{nameof(TcpStreamMessageReader)} has already been started.");
            }

            var state = new TcpStreamMessageReadingState
            {
                Stream = stream,

                AcceptReadBuffer = new byte[this.MaxPacketSize * this.MaxPacketCapacity],
                AcceptReadBufferSize = 0,
                MessageSize = 0,

                ReadCallback = AcceptRead,
                HandleMessageCallback = handleMessageCallback,

                Logger = this._logger,
            };

            stream.BeginRead(
                state.AcceptReadBuffer,
                0,
                state.AcceptReadBuffer.Length,
                state.ReadCallback,
                state);
        }

        private static void AcceptRead(IAsyncResult ar)
        {
            TcpStreamMessageReadingState state = (TcpStreamMessageReadingState)ar.AsyncState;

            NetworkStream stream = state.Stream;

            if (stream == null || !stream.CanRead)
            {
                return;
            }

            int bytesRead;
            try
            {
                bytesRead = stream.EndRead(ar);

                if (bytesRead == 0)
                {
                    // Connection closed.
                    stream.Close();

                    return;
                }
            }
            catch
            {
                // Assume the socket has closed.
                return;
            }

            state.AcceptReadBufferSize += bytesRead;

            // We need at least the frame header data to do anything.
            while (state.AcceptReadBufferSize >= FrameHeaderSizeByteCount)
            {
                if (state.MessageSize == 0)
                {
                    // Starting new message, get message size in bytes.

                    // First two bytes of the buffer is always the message size
                    state.MessageSize = BitConverter.ToUInt16(state.AcceptReadBuffer, 0);
                    state.TransactionId = BitConverter.ToUInt16(state.AcceptReadBuffer, 2);
                }

                if (FrameHeaderSizeByteCount + state.MessageSize <= state.AcceptReadBufferSize)
                {
                    // Complete message data available.

                    if (state.HandleMessageCallback != null)
                    {
                        var data = new byte[state.MessageSize];

                        // Copy data minus frame preamble
                        Array.Copy(state.AcceptReadBuffer, FrameHeaderSizeByteCount, data, 0, state.MessageSize);

                        state.HandleMessageCallback(data, state.Stream, state.TransactionId);
                    }

                    // Shift accept read buffer to the next frame, if any.
                    for (int i = FrameHeaderSizeByteCount + state.MessageSize, j = 0; i < state.AcceptReadBufferSize; i++, j++)
                    {
                        state.AcceptReadBuffer[j] = state.AcceptReadBuffer[i];
                    }

                    state.AcceptReadBufferSize -= state.MessageSize + FrameHeaderSizeByteCount;
                    state.MessageSize = 0;
                }
                else
                {
                    // More message bytes needed to stream.
                    break;
                }
            }

            if (!stream.CanRead)
            {
                return;
            }

            try
            {
                // Begin waiting for more stream data.
                _ = stream.BeginRead(
                    state.AcceptReadBuffer,
                    state.AcceptReadBufferSize,
                    state.AcceptReadBuffer.Length - state.AcceptReadBufferSize,
                    state.ReadCallback,
                    state);
            }
            catch
            {
                // Assume the socket has closed.
            }
        }

        private class TcpStreamMessageReadingState
        {
            public NetworkStream Stream;

            public byte[] AcceptReadBuffer;
            public int AcceptReadBufferSize;
            public int MessageSize;
            public ushort TransactionId;

            // Save delegate in state to avoid allocation per read.
            public AsyncCallback ReadCallback;

            public HandleMessageCallback HandleMessageCallback;

            public ILogger Logger;
        }
    }
}
