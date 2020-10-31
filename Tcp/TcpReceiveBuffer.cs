namespace CodeTestsConsole
{
    using System.Net.Sockets;
    using System.Threading;

    public class TcpReceiveBuffer
    {
        public readonly int MaxPacketSize;
        public readonly int PacketCapacity;

        private int[] _bytedReceived;

        private TcpClient[] _clients;

        private int _writeQueueIndex;
        private int _count;

        private readonly byte[] _data;

        public TcpReceiveBuffer(int maxPacketSize, int packetQueueCapacity)
        {
            this.MaxPacketSize = maxPacketSize;
            this.PacketCapacity = packetQueueCapacity;
            this._data = new byte[packetQueueCapacity * maxPacketSize];
            this._bytedReceived = new int[packetQueueCapacity];

            this._clients = new TcpClient[packetQueueCapacity];

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

        public void NextWrite(int bytesReceived, TcpClient tcpClient)
        {
            this._bytedReceived[this._writeQueueIndex] = bytesReceived;
            this._clients[this._writeQueueIndex] = tcpClient;

            this._writeQueueIndex = (this._writeQueueIndex + 1) % this.PacketCapacity;

            Interlocked.Increment(ref this._count);
        }

        public bool GetClient(out TcpClient client)
        {
            if (this._count == 0)
            {
                client = default;

                return false;
            }
            else
            {
                var readIndex = GetReadIndex();

                client = this._clients[readIndex];

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
                this._clients[readIndex].Close();
            }

            this._clients[readIndex] = null; // For GC

            Interlocked.Decrement(ref this._count);
        }

        private int GetReadIndex() => ((this._writeQueueIndex - this._count + this.PacketCapacity) % this.PacketCapacity);
    }
}
