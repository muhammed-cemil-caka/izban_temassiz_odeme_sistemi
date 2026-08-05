using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.LegacyHardware.Contracts;
using Xunit;

namespace IzbanKiosk.Tests
{
    public class NamedPipeFramingTests
    {
        [Fact]
        public void SynchronousFraming_ReassemblesFragmentedPackets_ForNet40Bridge()
        {
            const string expected = "{\"command\":\"ReadCardSnapshot\",\"requestId\":\"net40\"}";
            using var encoded = new MemoryStream();
            NamedPipeFraming.WriteMessage(encoded, expected);
            encoded.Position = 0;

            using var fragmented = new FragmentedReadStream(encoded, 1);
            string actual = NamedPipeFraming.ReadMessage(fragmented);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SynchronousFraming_RejectsPayloadOverMaximumSize()
        {
            byte[] header = BitConverter.GetBytes((64 * 1024) + 1);
            using var stream = new MemoryStream(header);

            Assert.Throws<InvalidDataException>(() => NamedPipeFraming.ReadMessage(stream));
        }

        [Fact]
        public async Task ReadMessageAsync_ReassemblesFragmentedHeaderAndPayload()
        {
            const string expected = "{\"command\":\"ReadCardSnapshot\",\"requestId\":\"abc\"}";
            using var encoded = new MemoryStream();
            await NamedPipeFraming.WriteMessageAsync(encoded, expected, CancellationToken.None);
            encoded.Position = 0;

            using var fragmented = new FragmentedReadStream(encoded, 1);
            string actual = await NamedPipeFraming.ReadMessageAsync(fragmented, CancellationToken.None);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task ReadMessageAsync_RejectsPayloadOverMaximumSize()
        {
            byte[] header = BitConverter.GetBytes((64 * 1024) + 1);
            using var stream = new MemoryStream(header);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                NamedPipeFraming.ReadMessageAsync(stream, CancellationToken.None));
        }

        private sealed class FragmentedReadStream : Stream
        {
            private readonly Stream _inner;
            private readonly int _maxChunkSize;

            public FragmentedReadStream(Stream inner, int maxChunkSize)
            {
                _inner = inner;
                _maxChunkSize = maxChunkSize;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => false;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() => _inner.Flush();
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count) =>
                _inner.Read(buffer, offset, Math.Min(count, _maxChunkSize));

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken) =>
                _inner.ReadAsync(buffer, offset, Math.Min(count, _maxChunkSize), cancellationToken);
        }
    }
}
