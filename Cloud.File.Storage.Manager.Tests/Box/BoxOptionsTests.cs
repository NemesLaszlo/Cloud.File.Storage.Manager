using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Cloud.File.Storage.Manager.Box;

namespace Cloud.File.Storage.Manager.Tests
{
    [TestClass]
    public class BoxOptionsTests
    {
        private const string Pem = "-----BEGIN ENCRYPTED PRIVATE KEY-----\nMIIFHDBOBgkqhkiG9w0BBQ0wQTApBgkqhkiG9w0BBQwwHAQI\n-----END ENCRYPTED PRIVATE KEY-----\n";

        [TestMethod]
        public void PrivateKey_EscapedNewlines_AreConverted()
        {
            var escaped = Pem.Replace("\n", "\\n");
            Assert.AreEqual(Pem, BoxCloudFileStorageManager.normalizePrivateKey(escaped));
        }

        [TestMethod]
        public void PrivateKey_EscapedCrLfAndCr_AreConverted()
        {
            var escaped = Pem.Replace("\n", "\\r\\n");
            Assert.AreEqual(Pem, BoxCloudFileStorageManager.normalizePrivateKey(escaped));
            Assert.AreEqual("a\nb", BoxCloudFileStorageManager.normalizePrivateKey("a\\rb"));
        }

        [TestMethod]
        public void PrivateKey_Normalization_IsIdempotent()
        {
            var once = BoxCloudFileStorageManager.normalizePrivateKey(Pem.Replace("\n", "\\n"));
            Assert.AreEqual(once, BoxCloudFileStorageManager.normalizePrivateKey(once));
            Assert.AreSame(Pem, BoxCloudFileStorageManager.normalizePrivateKey(Pem));
        }

        [TestMethod]
        public void PrivateKey_NullOrEmpty_IsReturnedUnchanged()
        {
            Assert.IsNull(BoxCloudFileStorageManager.normalizePrivateKey(null));
            Assert.AreEqual("", BoxCloudFileStorageManager.normalizePrivateKey(""));
        }

        [TestMethod]
        public async Task Spool_NonSeekableStream_KeepsEveryChunk()
        {
            var content = new byte[100_000];
            new Random(1).NextBytes(content);

            string tempPath;
            await using (var spooled = await BoxUploadBuffer.SpoolAsync(new ChunkedReadStream(content, 1000)))
            {
                Assert.IsTrue(spooled.CanSeek);
                Assert.AreEqual(0, spooled.Position);
                CollectionAssert.AreEqual(content, readAll(spooled));
                tempPath = ((FileStream)spooled).Name;
            }
            Assert.IsFalse(System.IO.File.Exists(tempPath));
        }

        [TestMethod]
        public async Task Append_NonSeekableStream_KeepsExistingAndNewContent()
        {
            var existing = new MemoryStream(Encoding.UTF8.GetBytes("existing-"));
            var appended = new ChunkedReadStream(Encoding.UTF8.GetBytes("chunk1-chunk2-chunk3"), 3);

            await using var combined = await BoxUploadBuffer.CombineAsync(existing, appended);

            Assert.IsTrue(combined.CanSeek);
            Assert.AreEqual(0, combined.Position);
            Assert.AreEqual("existing-chunk1-chunk2-chunk3", Encoding.UTF8.GetString(readAll(combined)));
        }

        [TestMethod]
        public async Task Append_Repeated_AccumulatesAllAppends()
        {
            Stream current = new MemoryStream();
            foreach (var part in new[] { "one;", "two;", "three;" })
            {
                var next = await BoxUploadBuffer.CombineAsync(current, new ChunkedReadStream(Encoding.UTF8.GetBytes(part), 2));
                await current.DisposeAsync();
                current = next;
            }
            await using (current)
                Assert.AreEqual("one;two;three;", Encoding.UTF8.GetString(readAll(current)));
        }

        private static byte[] readAll(Stream s)
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        // Non-seekable stream that returns at most chunkSize bytes per read, like a request body.
        private sealed class ChunkedReadStream : Stream
        {
            private readonly byte[] data;
            private readonly int chunkSize;
            private int position;

            public ChunkedReadStream(byte[] data, int chunkSize)
            {
                this.data = data;
                this.chunkSize = chunkSize;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                var n = Math.Min(Math.Min(count, chunkSize), data.Length - position);
                Array.Copy(data, position, buffer, offset, n);
                position += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
