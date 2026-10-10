namespace Cloud.File.Storage.Manager.Common
{
    /// <summary>
    /// A local copy of a provider file. Unless read-only, the contents are uploaded to the provider when the stream is disposed,
    /// including a dispose after an exception (partially written content is uploaded).
    /// </summary>
    public class LocalFileStream : Stream
    {
        private Stream Stream { get; }
        private readonly string[] targetFilePathSegments;
        public ICloudFileStorageManager Provider { get; }
        /// <summary>The absolute target segments. Returns a copy; the segments are fixed when the stream is created.</summary>
        public string[] TargetFilePathSegments => (string[])targetFilePathSegments?.Clone();
        public bool ReadOnly { get; }

        private bool disposeEventCalled = false;

        public LocalFileStream(Stream str, ICloudFileStorageManager provider, bool readOnly, string[] targetFilePathSegments)
        {
            Provider = provider;
            ReadOnly = readOnly;
            this.targetFilePathSegments = (string[])targetFilePathSegments?.Clone();
            Stream = str;
        }

        /// <summary>
        /// Gracefully (async) disposes the stream, that will cause the provider to finalize it.
        /// A writeable stream is uploaded also when it is disposed after an exception, so partially written content is uploaded.
        /// </summary>
        /// <returns></returns>
        public async Task DisposeAsync()
        {
            try
            {
                if (!ReadOnly && !disposeEventCalled)
                {
                    disposeEventCalled = true;
                    await Provider.OnLocalFileStreamDisposingAsync(this);
                }
            }
            finally
            {
                Stream.Dispose();
            }
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
