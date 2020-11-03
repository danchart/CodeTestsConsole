namespace Networking.Core
{
    using Common.Core;
    using System;
    using System.Net.Sockets;

    internal sealed class TcpStreamMessageReader
    {
        public const int FrameHeaderSizeByteCount = sizeof(ushort) + sizeof(ushort); // size + transaction id

        private readonly int MaxPacketSize;
        private readonly int MaxPacketCapacity;

        private readonly ILogger _logger;

        public TcpStreamMessageReader(ILogger logger, int maxPacketSize, int maxPacketCapacity)
        {
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

            this.MaxPacketSize = maxPacketSize;
            this.MaxPacketCapacity = maxPacketCapacity;
        }

        public void Start(NetworkStream stream, TcpClientData clientData)
        {
            var state = new TcpStreamMessageReadingState
            {
                ClientData = clientData,

                AcceptReadBuffer = new byte[this.MaxPacketSize * this.MaxPacketCapacity],
                AcceptReadBufferSize = 0,
                MessageSize = 0,

                Logger = this._logger,
            };

            stream.BeginRead(
                state.AcceptReadBuffer,
                0,
                state.AcceptReadBuffer.Length,
                AcceptRead,
                state);
        }

        private static void AcceptRead(IAsyncResult ar)
        {
            TcpStreamMessageReadingState state = (TcpStreamMessageReadingState)ar.AsyncState;

            try
            {
                state.ClientData.ReceiverCancelToken.Lock();

                if (state.ClientData.ReceiverCancelToken.IsCanceled ||
                    state.ClientData.ReceiverCancelToken.IsCanceling)
                {
                    // Server stopped.
                    return;
                }

                NetworkStream stream = state.ClientData.Stream;
                int bytesRead = stream.EndRead(ar);
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

                        if (state.ClientData.ReceiveBuffer.GetWriteData(out byte[] data, out int offset, out int size))
                        {
                            // Copy data minus frame preamble
                            Array.Copy(state.AcceptReadBuffer, FrameHeaderSizeByteCount, data, offset, state.MessageSize);

                            state.ClientData.ReceiveBuffer.NextWrite(state.MessageSize, state.ClientData.Client, state.TransactionId);
                        }
                        else
                        {
                            state.Logger.Error($"Out of receive buffer space: capacity={state.ClientData.ReceiveBuffer.PacketCapacity}");
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
                        // Message still being streamed.

                        break;
                    }
                }
            }
            finally
            {
                state.ClientData.ReceiverCancelToken.Unlock();
            }

            if (state.ClientData.ReceiverCancelToken.IsCanceled ||
                state.ClientData.ReceiverCancelToken.IsCanceling)
            {
                // Server stopped.
                return;
            }

            var stream2 = state.ClientData.Stream;

            if (stream2 != null)
            {
                // Begin waiting for more stream data.
                stream2.BeginRead(
                    state.AcceptReadBuffer,
                    state.AcceptReadBufferSize,
                    state.AcceptReadBuffer.Length - state.AcceptReadBufferSize,
                    AcceptRead,
                    state);
            }
        }

        private class TcpStreamMessageReadingState
        {
            public TcpClientData ClientData;

            public byte[] AcceptReadBuffer;
            public int AcceptReadBufferSize;
            public int MessageSize;
            public ushort TransactionId;

            public ILogger Logger;
        }
    }
}
