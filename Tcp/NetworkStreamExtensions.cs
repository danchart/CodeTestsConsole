using System;
using System.Net.Sockets;

namespace Networking.Core
{
    public static class NetworkStreamExtensions
    {
        public static void WriteFrame(
            this NetworkStream stream,
            ushort transactionId,
            byte[] data,
            int offset,
            ushort count)
        {
            Span<byte> dest = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dest, count);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(2, 2), transactionId);

            stream.Write(dest.ToArray(), 0, dest.Length);

            // Write message frame header.
            //var sizePreambleBytes = BitConverter.GetBytes(count);
            //var transactionIdBytes = BitConverter.GetBytes((ushort)transactionId);

            //stream.Write(sizePreambleBytes, 0, sizePreambleBytes.Length);
            //stream.Write(transactionIdBytes, 0, transactionIdBytes.Length);

            // Write data.
            stream.Write(data, 0, count);
        }

        // ****** DOES NOT READ TRANSACTION ID
        //public static void ReadWithFrameHeader(
        //    this NetworkStream stream,
        //    byte[] receiveData,
        //    int receiveOffset,
        //    int receiveSize,
        //    out int receivedBytes)
        //{
        //    byte[] sizePreambleBytes = new byte[2];

        //    stream.Read(sizePreambleBytes, 0, 2);

        //    ushort size = BitConverter.ToUInt16(sizePreambleBytes, 0);

        //    if (size > receiveSize)
        //    {
        //        throw new InvalidOperationException($"Receive buffer smaller than packet size.");
        //    }

        //    //receivedBytes = _stream.Read(receiveData, receiveOffset, receiveSize);
        //    receivedBytes = stream.Read(receiveData, receiveOffset, size);
        //}
    }
}
