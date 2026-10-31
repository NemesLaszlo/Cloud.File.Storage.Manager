using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Cloud.File.Storage.Manager.Common;
using Cloud.File.Storage.Manager.Minio;
using FileInfo = Cloud.File.Storage.Manager.Common.FileInfo;

namespace Cloud.File.Storage.Manager.Tests.Minio
{
    /// <summary>
    /// Endpoint for the Minio provider tests, read from WFP_MINIO_ENDPOINT, WFP_MINIO_ACCESS_KEY, WFP_MINIO_SECRET_KEY
    /// and WFP_MINIO_BUCKET. The defaults match a local adobe/s3mock container
    /// (docker run -d -p 9090:9090 -e COM_ADOBE_TESTING_S3MOCK_STORE_INITIAL_BUCKETS=wfp-test adobe/s3mock).
    /// Tests are inconclusive when the endpoint is not reachable.
    /// </summary>
    internal static class MinioTestEndpoint
    {
        public static string HostAndPort => env("WFP_MINIO_ENDPOINT", "127.0.0.1:9090");
        public static string AccessKey => env("WFP_MINIO_ACCESS_KEY", "foo");
        public static string SecretKey => env("WFP_MINIO_SECRET_KEY", "bar");
        public static string BucketName => env("WFP_MINIO_BUCKET", "wfp-test");

        private static bool? reachable;

        public static void EnsureReachable()
        {
            if (reachable == null)
            {
                var parts = HostAndPort.Split(':');
                try
                {
                    using var client = new TcpClient();
                    reachable = client.ConnectAsync(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 80).Wait(2000);
                }
                catch
                {
                    reachable = false;
                }
            }
            if (reachable != true)
                Assert.Inconclusive($"Minio endpoint {HostAndPort} is not reachable");
        }

        public static MinioCloudFileStorageManagerOptions Options(string rootDirectory, string bucketName = null)
        {
            return new MinioCloudFileStorageManagerOptions(HostAndPort, false, AccessKey, SecretKey, bucketName ?? BucketName) { RootDirectoryPath = rootDirectory };
        }

        public static IMinioClient CreateClient()
        {
            return new MinioClient().WithEndpoint(HostAndPort).WithCredentials(AccessKey, SecretKey).Build();
        }

        private static string env(string name, string defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }
    }

    /// <summary>
    /// Runs the shared provider suite against the endpoint of <see cref="MinioTestEndpoint"/>.
    /// </summary>
    [TestClass]
    [TestCategory("Minio")]
    public class MinioEndpointFileProvider : TestBase
    {
        private static TestContext context;
        [ClassInitialize]
        public static void Init(TestContext _context)
        {
            context = _context;
        }
        public MinioEndpointFileProvider() : base(context) { }

        [TestInitialize]
        public void EnsureEndpoint() => MinioTestEndpoint.EnsureReachable();

        protected override ICloudFileStorageManager GetProvider()
        {
            return new MinioCloudFileStorageManager(MinioTestEndpoint.Options("wfp-testbase"));
        }
    }

    [TestClass]
    [TestCategory("Minio")]
    public class MinioProviderTests
    {
        private string root;

        [TestInitialize]
        public void Setup()
        {
            MinioTestEndpoint.EnsureReachable();
            root = $"minio-tests/{Guid.NewGuid():N}";
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (root == null) return;
            var parent = new MinioCloudFileStorageManager(MinioTestEndpoint.Options("minio-tests"));
            await parent.DeleteAsync(root.Substring("minio-tests/".Length));
        }

        private MinioCloudFileStorageManager provider() => new MinioCloudFileStorageManager(MinioTestEndpoint.Options(root));

        private static async Task write(ICloudFileStorageManager p, string path, string content = "x")
        {
            await p.UpdateFileAsync(path, UpdateFileMode.Overwrite, new MemoryStream(Encoding.UTF8.GetBytes(content)));
        }

        private static async Task<string> read(ICloudFileStorageManager p, IFileInfo file)
        {
            await using var s = await p.ReadFileAsync(file);
            using var reader = new StreamReader(s);
            return await reader.ReadToEndAsync();
        }

        [TestMethod]
        public async Task ListingReturnsOneLevelWithAddressableEntries()
        {
            var p = provider();
            await write(p, "d/f1.txt", "one");
            await write(p, "d/sub1/a.txt");
            await write(p, "d/sub1/deeper/b.txt");
            await write(p, "d/sub2/c.txt");
            await write(p, "dx/z.txt");

            var contents = await p.GetDirectoryContentsAsync("d");
            Assert.IsTrue(contents.Exists);
            var entries = contents.ToList();
            CollectionAssert.AreEquivalent(new[] { "f1.txt", "sub1", "sub2" }, entries.Select(e => e.Name).ToArray());

            var file = entries.Single(e => e.Name == "f1.txt");
            Assert.IsTrue(file.Exists);
            Assert.IsFalse(file.IsDirectory);
            Assert.AreEqual(3, file.Length);
            Assert.IsTrue(file.LastModified.Year > 1970);
            CollectionAssert.AreEqual(new[] { "d", "f1.txt" }, ((FileInfo) file).PathSegments);
            Assert.AreEqual("one", await read(p, file));

            foreach (var dir in entries.Where(e => e.Name.StartsWith("sub")))
            {
                Assert.IsTrue(dir.Exists);
                Assert.IsTrue(dir.IsDirectory);
                Assert.AreEqual(1970, dir.LastModified.Year);
                CollectionAssert.AreEqual(new[] { "d", dir.Name }, ((FileInfo) dir).PathSegments);
            }

            var sub1Entry = (FileInfo) entries.Single(e => e.Name == "sub1");
            var sub1 = (await p.GetDirectoryContentsAsync(sub1Entry.PathSegments)).ToList();
            CollectionAssert.AreEquivalent(new[] { "a.txt", "deeper" }, sub1.Select(e => e.Name).ToArray());
            Assert.IsTrue(sub1.Single(e => e.Name == "deeper").IsDirectory);

            var top = (await p.GetDirectoryContentsAsync()).ToList();
            CollectionAssert.AreEquivalent(new[] { "d", "dx" }, top.Select(e => e.Name).ToArray());
            Assert.IsTrue(top.All(e => e.IsDirectory));
        }

        [TestMethod]
        public async Task ListingSkipsEmptyNamesFromDoubleSlashKeys()
        {
            var p = provider();
            await write(p, "d/ok.txt");
            using (var client = MinioTestEndpoint.CreateClient())
            {
                var data = new MemoryStream(new byte[] { 1 });
                await client.PutObjectAsync(new PutObjectArgs().WithBucket(MinioTestEndpoint.BucketName)
                    .WithObject($"{root}/d//odd.txt").WithStreamData(data).WithObjectSize(1));
            }

            var entries = (await p.GetDirectoryContentsAsync("d")).ToList();
            Assert.IsFalse(entries.Any(e => string.IsNullOrEmpty(e.Name)));
            Assert.IsTrue(entries.Any(e => e.Name == "ok.txt"));
        }

        [TestMethod]
        public async Task FolderWithSingleObjectExists()
        {
            var p = provider();
            await write(p, "single/only.txt");

            var contents = await p.GetDirectoryContentsAsync("single");
            Assert.IsTrue(contents.Exists);
            Assert.AreEqual("only.txt", contents.Single().Name);

            var info = await p.GetFileInfoAsync("single");
            Assert.IsTrue(info.Exists);
            Assert.IsTrue(info.IsDirectory);

            Assert.IsFalse((await p.GetDirectoryContentsAsync("missing")).Exists);
        }

        [TestMethod]
        public async Task FileInfoMatchesExactKeyOnly()
        {
            var p = provider();
            await write(p, "k/a.txt.bak", "backup");

            var info = await p.GetFileInfoAsync("k/a.txt");
            Assert.IsFalse(info.Exists);

            await write(p, "k/a.txt", "abc");
            info = await p.GetFileInfoAsync("k/a.txt");
            Assert.IsTrue(info.Exists);
            Assert.IsFalse(info.IsDirectory);
            Assert.AreEqual(3, info.Length);
            Assert.AreEqual("a.txt", info.Name);
        }

        [TestMethod]
        public async Task DeleteFolderWith1100Objects()
        {
            var p = provider();
            var names = Enumerable.Range(0, 1100)
                .Select(i => i % 10 == 0 ? $"big/nested/{i:D4}.txt" : $"big/{i:D4}.txt").ToList();
            for (var i = 0; i < names.Count; i += 50)
                await Task.WhenAll(names.Skip(i).Take(50).Select(n => write(p, n)));
            await write(p, "keep.txt");

            Assert.IsTrue(await p.DeleteAsync("big"));

            Assert.IsFalse((await p.GetDirectoryContentsAsync("big")).Exists);
            Assert.IsFalse((await p.GetFileInfoAsync("big")).Exists);
            Assert.IsTrue((await p.GetFileInfoAsync("keep.txt")).Exists);
        }

        [TestMethod]
        public async Task DeleteMissingPathReturnsFalse()
        {
            var p = provider();
            await write(p, "present.txt");
            Assert.IsFalse(await p.DeleteAsync("absent.txt"));
            Assert.IsFalse(await p.DeleteAsync("absent-dir/inner"));
            Assert.IsTrue(await p.DeleteAsync("present.txt"));
            Assert.IsFalse(await p.DeleteAsync("present.txt"));
        }

        [TestMethod]
        public async Task RootDeleteIsRefused()
        {
            var p = provider();
            await write(p, "survivor.txt");

            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => p.DeleteAsync(""));
            Assert.AreEqual("The provider root cannot be deleted or modified.", ex.Message);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => p.DeleteAsync(new string[0]));

            var unrooted = new MinioCloudFileStorageManager(MinioTestEndpoint.Options(null));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => unrooted.DeleteAsync(""));

            var probe = new RootProbe(MinioTestEndpoint.Options(root));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => probe.DeleteAbsolute(root.Split('/')));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new RootProbe(MinioTestEndpoint.Options(null)).DeleteAbsolute());

            Assert.IsTrue((await p.GetFileInfoAsync("survivor.txt")).Exists);
        }

        /// <summary>
        /// The client reports a missing object as <see cref="ObjectNotFoundException"/>.
        /// </summary>
        [TestMethod]
        public async Task ReadOfMissingObjectThrowsObjectNotFound()
        {
            var p = provider();
            await write(p, "exists.txt");
            await Assert.ThrowsExactlyAsync<ObjectNotFoundException>(() => p.ReadFileAsync("missing.txt"));
        }

        [TestMethod]
        public async Task ReadReturnsTempFileStream()
        {
            var p = provider();
            await write(p, "stream.txt", "content");
            await using var s = await p.ReadFileAsync("stream.txt");
            Assert.IsInstanceOfType(s, typeof(FileStream));
            Assert.AreEqual(0, s.Position);
            Assert.AreEqual(7, s.Length);
        }

        [TestMethod]
        public async Task DownloadUrlServesTheObjectAndIsCappedAtSevenDays()
        {
            var p = provider();
            Assert.IsTrue(p.SupportsDownloadUrl);
            await write(p, "link.txt", "linked");
            var url = await p.GetDownloadUrlAsync("link.txt", TimeSpan.FromDays(30));
            using var http = new System.Net.Http.HttpClient();
            Assert.AreEqual("linked", await http.GetStringAsync(url));
            StringAssert.Contains(url, "X-Amz-Expires=604800");
        }

        [TestMethod]
        public async Task ReadsDoNotCreateBucket()
        {
            var bucket = $"wfp-nobucket-{Guid.NewGuid():N}".Substring(0, 30);
            var p = new MinioCloudFileStorageManager(MinioTestEndpoint.Options("r", bucket));
            using var client = MinioTestEndpoint.CreateClient();

            Assert.IsFalse((await p.GetFileInfoAsync("a.txt")).Exists);
            Assert.IsFalse((await p.GetDirectoryContentsAsync("")).Exists);
            Assert.IsFalse((await p.GetDirectoryContentsAsync("d")).Exists);
            await Assert.ThrowsExactlyAsync<BucketNotFoundException>(() => p.ReadFileAsync("a.txt"));
            Assert.IsFalse(await p.DeleteAsync("a.txt"));
            Assert.IsFalse(await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket)));

            try
            {
                await write(p, "a.txt");
                Assert.IsTrue(await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket)));
            }
            finally
            {
                await p.DeleteAsync("a.txt");
                await client.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(bucket));
            }
            root = null;
        }

        [TestMethod]
        public async Task MoveRelocatesObject()
        {
            var p = provider();
            await write(p, "from/m.txt", "moved");
            await p.MoveAsync("from/m.txt", "to/m.txt");

            Assert.IsFalse((await p.GetFileInfoAsync("from/m.txt")).Exists);
            var info = await p.GetFileInfoAsync("to/m.txt");
            Assert.IsTrue(info.Exists);
            Assert.AreEqual("moved", await read(p, info));
        }

        [TestMethod]
        public async Task InstancesShareHttpClientAfterRequests()
        {
            var first = provider();
            await write(first, "shared/1.txt");
            var second = provider();
            await write(second, "shared/2.txt");
            Assert.AreEqual(2, (await new MinioCloudFileStorageManager(MinioTestEndpoint.Options(root)).GetDirectoryContentsAsync("shared")).Count());
        }

        [TestMethod]
        public async Task HttpSchemeInHostAndPortConnects()
        {
            var options = MinioTestEndpoint.Options(root);
            options.HostAndPort = "http://" + MinioTestEndpoint.HostAndPort;
            options.UseSSL = true;
            var p = new SslProbe(options);
            Assert.IsFalse(p.Ssl);
            await write(p, "scheme/a.txt", "via-scheme");
            Assert.AreEqual("via-scheme", await read(p, await p.GetFileInfoAsync("scheme/a.txt")));
        }

        private class RootProbe : MinioCloudFileStorageManager
        {
            public RootProbe(MinioCloudFileStorageManagerOptions options) : base(options) { }

            public Task<bool> DeleteAbsolute(params string[] absolutePathSegments) => deleteAsync(absolutePathSegments);
        }
    }

    internal class SslProbe : MinioCloudFileStorageManager
    {
        public SslProbe(MinioCloudFileStorageManagerOptions options) : base(options) { }

        public bool Ssl => UsesSsl;
    }

    [TestClass]
    public class MinioEndpointSchemeTests
    {
        [TestMethod]
        [DataRow("https://minio.example:9000", false, true)]
        [DataRow("HTTPS://minio.example:9000/", false, true)]
        [DataRow("http://minio.example", true, false)]
        [DataRow("minio.example:9000", true, true)]
        [DataRow("minio.example:9000", false, false)]
        public void SchemeInHostAndPortDecidesSsl(string hostAndPort, bool useSsl, bool expected)
        {
            var options = new MinioCloudFileStorageManagerOptions(hostAndPort, useSsl, "key", "secret", "bucket") { RootDirectoryPath = "root" };
            Assert.AreEqual(expected, new SslProbe(options).Ssl);
            Assert.AreEqual(hostAndPort, options.HostAndPort);
        }
    }
}
