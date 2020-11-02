namespace CodeTestsConsole
{
    using System.Net.Sockets;
    using System.Threading;

    public class TcpReceiveBuffer
    {
        public delegate void OnReadCompleteAction();

        public OnReadCompleteAction OnWriteComplete;

        public readonly int MaxPacketSize;
        public readonly int PacketCapacity;

        private int[] _bytedReceived;

        private TcpReceiveBufferState[] _states;

        private int _writeQueueIndex;
        private int _count;

        private readonly byte[] _data;

        public TcpReceiveBuffer(int maxPacketSize, int packetQueueCapacity)
        {
            this.MaxPacketSize = maxPacketSize;
            this.PacketCapacity = packetQueueCapacity;
            this._data = new byte[packetQueueCapacity * maxPacketSize];
            this._bytedReceived = new int[packetQueueCapacity];

            this._states = new TcpReceiveBufferState[packetQueueCapacity];

            this._writeQueueIndex = 0;
            this._count = 0;
        }

        public bool IsWriteQueueFull => this._count == this.PacketCapacity;

        public int Count => _count;

        public bool GetWriteData(out byte[] data, out int offset, out int size)
        {
            if (this._count == this.PacketCapacity)
            {
                data = null;
                offset = -1;
                size = -1;

                return false;
            }
            else
            {
                data = this._data;
                offset = this._writeQueueIndex * this.MaxPacketSize;
                size = this.MaxPacketSize;

                return true;
            }
        }

        public void NextWrite(int bytesReceived, TcpClient tcpClient, ushort transactionId)
        {
            this._bytedReceived[this._writeQueueIndex] = bytesReceived;
            this._states[this._writeQueueIndex].Client = tcpClient;
            this._states[this._writeQueueIndex].TransactionId = transactionId;

            this._writeQueueIndex = (this._writeQueueIndex + 1) % this.PacketCapacity;

            Interlocked.Increment(ref this._count);

            OnWriteComplete?.Invoke();
        }

        public bool GetState(out TcpClient client, out ushort transactionId)
        {
            if (this._count == 0)
            {
                client = default;
                transactionId = default;

                return false;
            }
            else
            {
                var readIndex = GetReadIndex();

                client = this._states[readIndex].Client;
                transactionId = this._states[readIndex].TransactionId;

                return true;
            }
        }

        public bool GetReadData(out byte[] data, out int offset, out int count)
        {
            if (this._count == 0)
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

        public void NextRead(bool closeConnection)
        {
            var readIndex = GetReadIndex();

            if (closeConnection)
            {
                this._states[readIndex].Client.Close();
            }

            this._states[readIndex].Client = null; // For GC

            Interlocked.Decrement(ref this._count);
        }

        private int GetReadIndex() => ((this._writeQueueIndex - this._count + this.PacketCapacity) % this.PacketCapacity);
    }

    public struct TcpReceiveBufferState
    {
        public TcpClient Client;
        public ushort TransactionId;
    }
}
