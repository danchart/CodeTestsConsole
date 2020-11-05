namespace Networking.Core
{
    using Common.Core;
    using System;
    using System.Collections.Generic;
    using System.Net.Sockets;

    internal sealed class TcpStreamMessageReader
    {
        public delegate void HandleMessageCallback(byte[] data, NetworkStream stream, ushort transactionId);

        public const int FrameHeaderSizeByteCount = sizeof(ushort) + sizeof(ushort); // Frame size + transaction id

        private bool _started;

        private readonly int _maxMessageSize;
        private readonly int _maxMessageQueueSize;

        private readonly ILogger _logger;

        public TcpStreamMessageReader(ILogger logger, int maxMessageSize, int maxMessageQueueSize)
        {
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

            this._maxMessageSize = maxMessageSize;
            this._maxMessageQueueSize = maxMessageQueueSize;

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

                AcceptReadBuffer = new byte[this._maxMessageSize * this._maxMessageQueueSize],
                AcceptReadBufferSize = 0,
                CurrentMessageSize = 0,

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
            }
            catch
            {
                // Assume the socket has closed.
                return;
            }

            if (bytesRead == 0)
            {
                // Connection closed.
                stream.Close();

                return;
            }

            state.AcceptReadBufferSize += bytesRead;

            List<MessageCallbackData> callbackDataItems = new List<MessageCallbackData>();

            // We need at least the frame header data to do anything.
            while (state.AcceptReadBufferSize >= FrameHeaderSizeByteCount)
            {
                if (state.CurrentMessageSize == 0)
                {
                    // Starting new message, get message size in bytes.

                    // First two bytes of the buffer is always the message size
                    state.CurrentMessageSize = BitConverter.ToUInt16(state.AcceptReadBuffer, 0);
                    state.CurrentTransactionId = BitConverter.ToUInt16(state.AcceptReadBuffer, 2);
                }

                if (FrameHeaderSizeByteCount + state.CurrentMessageSize <= state.AcceptReadBufferSize)
                {
                    // Complete message data available.

                    var callbackData = new MessageCallbackData
                    {
                        Data = new byte[state.CurrentMessageSize],
                        TransactionId = state.CurrentTransactionId,
                    };

                    // Copy data minus frame preamble
                    Array.Copy(state.AcceptReadBuffer, FrameHeaderSizeByteCount, callbackData.Data, 0, state.CurrentMessageSize);

                    // Shift accept read buffer to the next frame, if any.
                    for (int i = FrameHeaderSizeByteCount + state.CurrentMessageSize, j = 0; i < state.AcceptReadBufferSize; i++, j++)
                    {
                        state.AcceptReadBuffer[j] = state.AcceptReadBuffer[i];
                    }

                    state.AcceptReadBufferSize -= state.CurrentMessageSize + FrameHeaderSizeByteCount;
                    state.CurrentMessageSize = 0;

                    callbackDataItems.Add(callbackData);
                }
                else
                {
                    // More message bytes needed to stream.
                    break;
                }
            }

            try
            {
                // Begin asynchronous read of more stream data.
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
                return;
            }

            // Execute the message handler callbacks.

            foreach (var callbackData in callbackDataItems)
            {
                try
                {
                    state.HandleMessageCallback(
                        callbackData.Data,
                        state.Stream,
                        callbackData.TransactionId);
                }
                catch (Exception e)
                {
                    state.Logger.Warning($"Exception thrown in TCP message callback: e={e}");
                }
            }

            callbackDataItems.Clear();
        }

        private class TcpStreamMessageReadingState
        {
            public NetworkStream Stream;

            public byte[] AcceptReadBuffer;
            public int AcceptReadBufferSize;

            public int CurrentMessageSize;
            public ushort CurrentTransactionId;

            // Save delegates in state to avoid allocation per read.
            public AsyncCallback ReadCallback;
            public HandleMessageCallback HandleMessageCallback;

            public ILogger Logger;
        }

        private struct MessageCallbackData
        {
            public byte[] Data;
            public ushort TransactionId;
        }
    }
}
