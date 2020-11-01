using System;
using System.Net.Sockets;

namespace Networking.Core
{
    public static class NetworkStreamExtensions
    {
        public static void WriteWithSizePreamble(
            this NetworkStream stream,
            byte[] data,
            int offset,
            ushort count)
        {
            var sizePreambleBytes = BitConverter.GetBytes((ushort)count);

            stream.Write(sizePreambleBytes, 0, sizePreambleBytes.Length);
            stream.Write(data, 0, count);

            //var writeData = new byte[count + 2];

            //writeData[0] = sizePreambleBytes[0];
            //writeData[1] = sizePreambleBytes[1];

            //Array.Copy(data, offset, writeData, 2, count);

            //stream.Write(writeData, 0, count + 2);
        }

        public static void ReadWithSizePreamble(
            this NetworkStream stream,
            byte[] receiveData,
            int receiveOffset,
            int receiveSize,
            out int receivedBytes)
        {
            byte[] sizePreambleBytes = new byte[2];

            stream.Read(sizePreambleBytes, 0, 2);

            ushort size = BitConverter.ToUInt16(sizePreambleBytes, 0);

            if (size > receiveSize)
            {
                throw new InvalidOperationException($"Receive buffer smaller than packet size.");
            }

            //receivedBytes = _stream.Read(receiveData, receiveOffset, receiveSize);
            receivedBytes = stream.Read(receiveData, receiveOffset, size);
        }
    }
}
