using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IzbanKiosk.LegacyHardware.Contracts
{
    public static class NamedPipeFraming
    {
        private const int HEADER_SIZE = 4;
        private const int MAX_MESSAGE_SIZE = 64 * 1024; // 64 KB

        /// <summary>
        /// .NET Framework 4.0 compatible synchronous reader. The legacy kiosk
        /// executes this method only on background worker threads.
        /// </summary>
        public static string ReadMessage(Stream stream)
        {
            byte[] headerBuffer = new byte[HEADER_SIZE];
            ReadExact(stream, headerBuffer, 0, HEADER_SIZE);

            int payloadLength = BitConverter.ToInt32(headerBuffer, 0);
            if (payloadLength < 0 || payloadLength > MAX_MESSAGE_SIZE)
            {
                throw new InvalidDataException(string.Format(
                    "Invalid packet size: {0} bytes. Limit is {1} bytes.",
                    payloadLength,
                    MAX_MESSAGE_SIZE));
            }

            if (payloadLength == 0)
            {
                return string.Empty;
            }

            byte[] payloadBuffer = new byte[payloadLength];
            ReadExact(stream, payloadBuffer, 0, payloadLength);
            return Encoding.UTF8.GetString(payloadBuffer);
        }

        /// <summary>
        /// .NET Framework 4.0 compatible synchronous writer.
        /// </summary>
        public static void WriteMessage(Stream stream, string message)
        {
            byte[] payloadBytes = Encoding.UTF8.GetBytes(message ?? string.Empty);
            if (payloadBytes.Length > MAX_MESSAGE_SIZE)
            {
                throw new InvalidOperationException(string.Format(
                    "Message size {0} exceeds maximum limit of {1} bytes.",
                    payloadBytes.Length,
                    MAX_MESSAGE_SIZE));
            }

            byte[] headerBytes = BitConverter.GetBytes(payloadBytes.Length);
            stream.Write(headerBytes, 0, HEADER_SIZE);
            if (payloadBytes.Length > 0)
            {
                stream.Write(payloadBytes, 0, payloadBytes.Length);
            }
            stream.Flush();
        }

        private static void ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                int bytesRead = stream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                if (bytesRead <= 0)
                {
                    throw new EndOfStreamException(string.Format(
                        "Reached end of stream. Read {0} of {1} requested bytes.",
                        totalBytesRead,
                        count));
                }
                totalBytesRead += bytesRead;
            }
        }

#if !NET40
        /// <summary>
        /// Reads a length-prefixed packet from the stream with chunked buffer accumulation.
        /// </summary>
        public static async Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
        {
            // 1. Read header (4 bytes payload length)
            byte[] headerBuffer = new byte[HEADER_SIZE];
            await ReadExactAsync(stream, headerBuffer, 0, HEADER_SIZE, cancellationToken);

            int payloadLength = BitConverter.ToInt32(headerBuffer, 0);
            if (payloadLength < 0 || payloadLength > MAX_MESSAGE_SIZE)
            {
                throw new InvalidDataException($"Invalid packet size: {payloadLength} bytes. Limit is {MAX_MESSAGE_SIZE} bytes.");
            }

            if (payloadLength == 0)
            {
                return string.Empty;
            }

            // 2. Read full payload body
            byte[] payloadBuffer = new byte[payloadLength];
            await ReadExactAsync(stream, payloadBuffer, 0, payloadLength, cancellationToken);

            return Encoding.UTF8.GetString(payloadBuffer);
        }

        /// <summary>
        /// Writes a length-prefixed packet to the stream.
        /// </summary>
        public static async Task WriteMessageAsync(Stream stream, string message, CancellationToken cancellationToken)
        {
            byte[] payloadBytes = Encoding.UTF8.GetBytes(message ?? string.Empty);
            if (payloadBytes.Length > MAX_MESSAGE_SIZE)
            {
                throw new InvalidOperationException($"Message size {payloadBytes.Length} exceeds maximum limit of {MAX_MESSAGE_SIZE} bytes.");
            }

            byte[] headerBytes = BitConverter.GetBytes(payloadBytes.Length);

            // Write length followed by content
            await stream.WriteAsync(headerBytes, 0, HEADER_SIZE, cancellationToken);
            if (payloadBytes.Length > 0)
            {
                await stream.WriteAsync(payloadBytes, 0, payloadBytes.Length, cancellationToken);
            }
            await stream.FlushAsync(cancellationToken);
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                int bytesRead = await stream.ReadAsync(
                    buffer, 
                    offset + totalBytesRead, 
                    count - totalBytesRead, 
                    cancellationToken);

                if (bytesRead <= 0)
                {
                    throw new EndOfStreamException($"Reached end of stream. Read {totalBytesRead} of {count} requested bytes.");
                }

                totalBytesRead += bytesRead;
            }
        }
#endif
    }
}
