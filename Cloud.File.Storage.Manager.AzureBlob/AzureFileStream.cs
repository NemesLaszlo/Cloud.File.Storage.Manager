namespace Cloud.File.Storage.Manager.AzureBlob
{
    public class AzureFileStream : Stream
    {
        private readonly Stream baseStream;
        private readonly long length;

        public AzureFileStream(Stream baseStream, long length)
        {
            this.baseStream = baseStream;
            this.length = length;
        }

        public override bool CanRead => baseStream.CanRead;

        public override bool CanSeek => baseStream.CanSeek;

        public override bool CanWrite => baseStream.CanWrite;

        public override long Length => length;

        public override long Position { get => baseStream.Position; set => baseStream.Position = value; }

        public override void Flush() => baseStream.Flush();

        public override int Read(byte[] buffer, int offset, int count) => baseStream.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => baseStream.Seek(offset, origin);

        public override void SetLength(long value) => baseStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => baseStream.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            baseStream.Dispose();
        }
        public override ValueTask DisposeAsync()
        {
            return baseStream.DisposeAsync();
        }
    }
}
