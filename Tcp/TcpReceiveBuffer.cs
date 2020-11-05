namespace Networking.Core
{
    using System.Net.Sockets;

    public class TcpReceiveBuffer
    {
        public delegate bool OnBeforeWriteCompleteAction(
            byte[] data, 
            int offset, 
            int size, 
            NetworkStream stream, 
            ushort transactionId);

        public OnBeforeWriteCompleteAction OnWriteComplete;

        public readonly int MaxPacketSize;
        public readonly int PacketCapacity;

        private readonly int[] _bytedReceived;
        private readonly TcpReceiveBufferState[] _states;

        private int _unwrappedTailIndex; // current read index - range is [0, 2 * Capacity]
        private int _unwrappedHeadIndex; // current write index - range is [0, 2 * Capacity]

        private readonly byte[] _data;

        public TcpReceiveBuffer(int maxPacketSize, int packetQueueCapacity)
        {
            this.MaxPacketSize = maxPacketSize;
            this.PacketCapacity = packetQueueCapacity;

            this._data = new byte[packetQueueCapacity * maxPacketSize];
            this._bytedReceived = new int[packetQueueCapacity];

            this._states = new TcpReceiveBufferState[packetQueueCapacity];

            this._unwrappedTailIndex = 0;
            this._unwrappedHeadIndex = 0;
        }

        public bool IsFull => this.Count == this.PacketCapacity;

        public int Count 
        {
            get
            {
                var count = (this._unwrappedHeadIndex + 2 * this.PacketCapacity - this._unwrappedTailIndex) % this.PacketCapacity;

                if (count == 0)
                {
                    if (this._unwrappedHeadIndex == this._unwrappedTailIndex)
                    {
                        return 0;
                    }
                    else
                    {
                        return this.PacketCapacity;
                    }
                }

                return count;
            }
        }

        public bool GetWriteData(out byte[] data, out int offset, out int size)
        {
            if (this.Count == this.PacketCapacity)
            {
                data = null;
                offset = -1;
                size = -1;

                return false;
            }
            else
            {
                var writeIndex = GetWriteIndex();

                data = this._data;
                offset = writeIndex * this.MaxPacketSize;
                size = this.MaxPacketSize;

                return true;
            }
        }

        public void NextWrite(int bytesReceived, NetworkStream stream, ushort transactionId)
        {
            var writeIndex = GetWriteIndex();

            if (OnWriteComplete != null) 
            {
                if (OnWriteComplete(this._data, writeIndex * this.MaxPacketSize, bytesReceived, stream, transactionId))
                {
                    // Write processed by callback, don't update head position.
                    return;
                }
            }

            this._bytedReceived[writeIndex] = bytesReceived;
            this._states[writeIndex].Stream = stream;
            this._states[writeIndex].TransactionId = transactionId;

            this._unwrappedHeadIndex = (this._unwrappedHeadIndex + 1) % (2 * this.PacketCapacity);
        }

        public bool GetState(out NetworkStream stream, out ushort transactionId)
        {
            if (this.Count == 0)
            {
                stream = default;
                transactionId = default;

                return false;
            }
            else
            {
                var readIndex = GetReadIndex();

                stream = this._states[readIndex].Stream;
                transactionId = this._states[readIndex].TransactionId;

                return true;
            }
        }

        public bool GetReadData(out byte[] data, out int offset, out int count)
        {
            if (this.Count == 0)
            {
                data = null;
                offset = -1;
                count = -1;

                return false;
            }
            else
            {
                var readIndex = GetReadIndex();

                data = this._data;
                offset = readIndex * this.MaxPacketSize;
                count = this._bytedReceived[readIndex];

                return true;
            }
        }

        public void NextRead()
        {
            var readIndex = GetReadIndex();

            this._states[readIndex].Stream = null; // For GC

            this._unwrappedTailIndex = (this._unwrappedTailIndex + 1) % (2 * this.PacketCapacity);
        }

        private int GetReadIndex() => this._unwrappedTailIndex % this.PacketCapacity;
        private int GetWriteIndex() => this._unwrappedHeadIndex % this.PacketCapacity;

        private struct TcpReceiveBufferState
        {
            public NetworkStream Stream;
            public ushort TransactionId;
        }
    }
}
