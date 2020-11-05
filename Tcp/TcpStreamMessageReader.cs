namespace Networking.Core
{
    using Common.Core;
    using System;
    using System.Diagnostics;
    using System.Net.Sockets;

    internal sealed class TcpStreamMessageReader
    {
        public const int FrameHeaderSizeByteCount = sizeof(ushort) + sizeof(ushort); // Frame size + transaction id

        private readonly int _maxMessageSize;
        private readonly int _maxMessageQueueSize;

        private readonly ILogger _logger;

        public TcpStreamMessageReader(ILogger logger, int maxMessageSize, int maxMessageQueueSize)
        {
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

            this._maxMessageSize = maxMessageSize;
            this._maxMessageQueueSize = maxMessageQueueSize;
        }

        public void Start(NetworkStream stream, TcpReceiveBuffer receiveBuffer)
        {
            var state = new TcpStreamMessageReadingState
            {
                Stream = stream,
                ReceiveBuffer = receiveBuffer,

                AcceptReadBuffer = new byte[this._maxMessageSize * this._maxMessageQueueSize],
                AcceptReadBufferSize = 0,
                MessageSize = 0,

                ReadCallback = AcceptRead,

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
            }
            catch
            {
                // Assume the socket has closed.
                return;
            }

            if (bytesRead == 0)
            {
                // Socket has closed.
                stream.Close(); // Close the stream for immediate disconnection.

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

                    if (state.ReceiveBuffer.GetWriteData(out byte[] data, out int offset, out int size))
                    {
                        Debug.Assert(state.MessageSize <= size);
                        
                        // Copy data minus frame preamble
                        Array.Copy(state.AcceptReadBuffer, FrameHeaderSizeByteCount, data, offset, state.MessageSize);

                        state.ReceiveBuffer.NextWrite(state.MessageSize, state.Stream, state.TransactionId);
                    }
                    else
                    {
                        state.Logger.Error($"Out of receive buffer space: capacity={state.ReceiveBuffer.PacketCapacity}");
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
            public TcpReceiveBuffer ReceiveBuffer;

            public byte[] AcceptReadBuffer;
            public int AcceptReadBufferSize;
            public int MessageSize;
            public ushort TransactionId;

            // Save delegate in state to avoid allocation per read.
            public AsyncCallback ReadCallback;

            public ILogger Logger;
        }
    }
}
