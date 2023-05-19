namespace Cloud.File.Storage.Manager.Common
{
    public class LocalFileStream : Stream
    {
        private Stream Stream { get; }
        public ICloudFileStorageManager Provider { get; }
        public string[] TargetFilePathSegments { get; }
        public bool ReadOnly { get; }

        private bool disposeEventCalled = false;

        public LocalFileStream(Stream str, ICloudFileStorageManager provider, bool readOnly, string[] targetFilePathSegments)
        {
            Provider = provider;
            ReadOnly = readOnly;
            TargetFilePathSegments = targetFilePathSegments;
            Stream = str;
        }

        /// <summary>
        /// Gracefully (async) disposes the stream, that will cause the provider to finalize it
        /// </summary>
        /// <returns></returns>
        public async Task DisposeAsync()
        {
            if (!ReadOnly && !disposeEventCalled)
            {
                disposeEventCalled = true;
                await Provider.OnLocalFileStreamDisposingAsync(this);
            }
            Stream.Dispose();
        }

        protected override void Dispose(bool disposing)
        {

            DisposeAsync().Wait();
            Stream.Dispose();
        }

        public override void Flush()
        {
            Stream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return Stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            Stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Stream.Write(buffer, offset, count);
        }

        public override bool CanRead => Stream.CanRead;
        public override bool CanSeek => Stream.CanSeek;
        public override bool CanWrite => Stream.CanWrite && !ReadOnly;
        public override long Length => Stream.Length;
        public override long Position
        {
            get => Stream.Position;
            set => Stream.Position = value;
        }

    }
}
