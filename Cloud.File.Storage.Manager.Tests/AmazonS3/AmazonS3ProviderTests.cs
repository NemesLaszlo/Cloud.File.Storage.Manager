using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.FileProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Cloud.File.Storage.Manager.AmazonS3;
using Cloud.File.Storage.Manager.Common;
using FileInfo = Cloud.File.Storage.Manager.Common.FileInfo;

namespace Cloud.File.Storage.Manager.Tests
{
    /// <summary>
    /// S3 test target. Runs only when AWS_ENDPOINT_URL_S3 points to an S3 compatible endpoint (for example LocalStack)
    /// that has the bucket WFP_S3_TEST_BUCKET (default wfp-test) and accepts the access key and secret "test".
    /// </summary>
    internal static class LocalStackS3
    {
        public const string Region = "us-east-1";
        public const string Key = "test";

        public static string BucketName =>
            Environment.GetEnvironmentVariable("WFP_S3_TEST_BUCKET") is { Length: > 0 } b ? b : "wfp-test";

        public static void EnsureConfigured()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL_S3")))
                Assert.Inconclusive("AWS_ENDPOINT_URL_S3 is not set; the S3 tests need a LocalStack (or other S3 compatible) endpoint.");
        }

        public static AmazonS3FileStorageManagerOptions Options(string rootDirectoryPath) => new AmazonS3FileStorageManagerOptions()
        {
            AccessKey = Key,
            SecretKey = Key,
            BucketName = BucketName,
            BucketRegion = Region,
            RootDirectoryPath = rootDirectoryPath
        };

        public static AmazonS3FileStorageManager Provider(string rootDirectoryPath = null)
        {
            EnsureConfigured();
            return new AmazonS3FileStorageManager(Options(rootDirectoryPath));
        }

        public static AmazonS3Client Client()
        {
            EnsureConfigured();
            return new AmazonS3Client(new BasicAWSCredentials(Key, Key), RegionEndpoint.GetBySystemName(Region));
        }
    }

    [TestClass]
    public class AmazonS3ProviderTests
    {
        private class RootGuardProbe : AmazonS3FileStorageManager
        {
            public RootGuardProbe(AmazonS3FileStorageManagerOptions options) : base(options) { }
            public Task<bool> DeleteAbsolute(params string[] absolutePathSegments) => deleteAsync(absolutePathSegments);
        }

        private static string NewName(string prefix) => $"{prefix}{Guid.NewGuid():N}";

        private static MemoryStream Content(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

        private static async Task PutRaw(AmazonS3Client client, string key, string text = "")
        {
            await client.PutObjectAsync(new PutObjectRequest() { BucketName = LocalStackS3.BucketName, Key = key, ContentBody = text });
        }

        private static async Task<int> CountKeys(AmazonS3Client client, string prefix)
        {
            var count = 0;
            string token = null;
            do
            {
                var page = await client.ListObjectsV2Async(new ListObjectsV2Request()
                {
                    BucketName = LocalStackS3.BucketName,
                    Prefix = prefix,
                    ContinuationToken = token
                });
                count += page.S3Objects.Count;
                token = page.NextContinuationToken;
            } while (!string.IsNullOrEmpty(token));
            return count;
        }

        private static async Task DeleteRaw(AmazonS3Client client, string prefix)
        {
            string token = null;
            do
            {
                var page = await client.ListObjectsV2Async(new ListObjectsV2Request()
                {
                    BucketName = LocalStackS3.BucketName,
                    Prefix = prefix,
                    ContinuationToken = token
                });
                token = page.NextContinuationToken;
                if (page.S3Objects.Count > 0)
                    await client.DeleteObjectsAsync(new DeleteObjectsRequest()
                    {
                        BucketName = LocalStackS3.BucketName,
                        Objects = page.S3Objects.Select(o => new KeyVersion() { Key = o.Key }).ToList()
                    });
            } while (!string.IsNullOrEmpty(token));
        }

        private static async Task AssertNestedListing(AmazonS3FileStorageManager provider, string keyPrefix, string baseDir)
        {
            var contents = await provider.GetDirectoryContentsAsync(baseDir);
            Assert.IsTrue(contents.Exists, "Listing does not exist");
            var entries = contents.ToList();
            CollectionAssert.AreEquivalent(new[] { "a", "m", "x.txt" }, entries.Select(e => e.Name).ToArray(),
                "Listing must contain exactly the direct children, without the folder marker or empty names");

            foreach (var dir in entries.Where(e => e.Name != "x.txt").Cast<FileInfo>())
            {
                Assert.IsTrue(dir.IsDirectory && dir.Exists, $"{dir.Name} is not an existing directory");
                Assert.AreEqual(-1, dir.Length);
                Assert.AreEqual($"{keyPrefix}{baseDir}/{dir.Name}", dir.PhysicalPath);
                CollectionAssert.AreEqual(new[] { baseDir, dir.Name }, dir.PathSegments);
            }

            var file = (FileInfo)entries.Single(e => e.Name == "x.txt");
            Assert.IsTrue(file.Exists && !file.IsDirectory);
            Assert.AreEqual(5, file.Length);
            Assert.IsTrue(file.LastModified > new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), "File LastModified is not the stored value");
            Assert.AreEqual($"{keyPrefix}{baseDir}/x.txt", file.PhysicalPath);
            CollectionAssert.AreEqual(new[] { baseDir, "x.txt" }, file.PathSegments);
            Assert.IsTrue((await provider.GetFileInfoAsync(file.PathSegments)).Exists, "PathSegments do not round-trip");
            using (var reader = new StreamReader(await provider.ReadFileAsync(file)))
                Assert.AreEqual("hello", await reader.ReadToEndAsync());

            var nested = (await provider.GetDirectoryContentsAsync(baseDir, "a")).ToList();
            Assert.AreEqual(1, nested.Count);
            Assert.AreEqual("b", nested[0].Name);
            Assert.IsTrue(nested[0].IsDirectory);
            CollectionAssert.AreEqual(new[] { baseDir, "a", "b" }, ((FileInfo)nested[0]).PathSegments);

            var missing = await provider.GetDirectoryContentsAsync(baseDir, "missing");
            Assert.IsFalse(missing.Exists);
            Assert.AreEqual(0, missing.Count());
        }

        private static async Task CreateNestedTree(AmazonS3Client client, string keyPrefix, string baseDir)
        {
            await PutRaw(client, $"{keyPrefix}{baseDir}/");
            await PutRaw(client, $"{keyPrefix}{baseDir}/a/b/c.txt", "c");
            await PutRaw(client, $"{keyPrefix}{baseDir}/x.txt", "hello");
            await PutRaw(client, $"{keyPrefix}{baseDir}/m/");
            await PutRaw(client, $"{keyPrefix}{baseDir}//empty-segment.txt", "e");
        }

        [TestMethod]
        public async Task Listing_NestedFolders_WithoutRootFolder()
        {
            var provider = LocalStackS3.Provider();
            using var client = LocalStackS3.Client();
            var baseDir = NewName("s3list");
            try
            {
                await CreateNestedTree(client, "", baseDir);
                await AssertNestedListing(provider, "", baseDir);

                var root = await provider.GetDirectoryContentsAsync("");
                Assert.IsTrue(root.Exists, "Bucket root listing is empty");
                var dir = root.SingleOrDefault(e => e.Name == baseDir);
                Assert.IsNotNull(dir, "Bucket root listing does not contain the test folder");
                Assert.IsTrue(dir.IsDirectory && dir.Exists);
                CollectionAssert.AreEqual(new[] { baseDir }, ((FileInfo)dir).PathSegments);
            }
            finally
            {
                await DeleteRaw(client, $"{baseDir}/");
            }
        }

        [TestMethod]
        public async Task Listing_NestedFolders_WithRootFolder()
        {
            var rootDir = NewName("s3root");
            var provider = LocalStackS3.Provider(rootDir);
            using var client = LocalStackS3.Client();
            const string baseDir = "base";
            try
            {
                await CreateNestedTree(client, $"{rootDir}/", baseDir);
                await AssertNestedListing(provider, $"{rootDir}/", baseDir);

                var root = (await provider.GetDirectoryContentsAsync("")).ToList();
                Assert.AreEqual(1, root.Count);
                Assert.AreEqual(baseDir, root[0].Name);
                Assert.IsTrue(root[0].IsDirectory && root[0].Exists);
                CollectionAssert.AreEqual(new[] { baseDir }, ((FileInfo)root[0]).PathSegments);
            }
            finally
            {
                await DeleteRaw(client, $"{rootDir}/");
            }
        }

        [TestMethod]
        public async Task FileInfo_ExactKeyIsNotConfusedWithLongerKey()
        {
            var provider = LocalStackS3.Provider();
            using var client = LocalStackS3.Client();
            var baseDir = NewName("s3info");
            try
            {
                await provider.UpdateFileAsync(Content("backup"), UpdateFileMode.Overwrite, baseDir, "a.txt.bak");
                var info = await provider.GetFileInfoAsync(baseDir, "a.txt");
                Assert.IsFalse(info.Exists, "a.txt must not exist when only a.txt.bak exists");
                Assert.IsFalse(info.IsDirectory);

                await provider.UpdateFileAsync(Content("abc"), UpdateFileMode.Overwrite, baseDir, "a.txt");
                info = await provider.GetFileInfoAsync(baseDir, "a.txt");
                Assert.IsTrue(info.Exists, "a.txt is not found next to a.txt.bak");
                Assert.IsFalse(info.IsDirectory);
                Assert.AreEqual(3, info.Length);
                Assert.AreEqual("a.txt", info.Name);

                var bak = await provider.GetFileInfoAsync(baseDir, "a.txt.bak");
                Assert.IsTrue(bak.Exists);
                Assert.AreEqual(6, bak.Length);
            }
            finally
            {
                await DeleteRaw(client, $"{baseDir}/");
            }
        }

        [TestMethod]
        public async Task FileInfo_KeyWithOnlyChildren_IsDirectory()
        {
            var provider = LocalStackS3.Provider();
            using var client = LocalStackS3.Client();
            var baseDir = NewName("s3dirinfo");
            try
            {
                //dir.txt sorts before dir/child.txt, so the directory check cannot rely on the first key under "dir"
                await provider.UpdateFileAsync(Content("1"), UpdateFileMode.Overwrite, baseDir, "dir.txt");
                await provider.UpdateFileAsync(Content("2"), UpdateFileMode.Overwrite, baseDir, "dir", "child.txt");

                var info = await provider.GetFileInfoAsync(baseDir, "dir");
                Assert.IsTrue(info.IsDirectory, "A key with only children is not reported as a directory");
                Assert.IsFalse(info.Exists, "A directory must not be reported as an existing file");

                info = await provider.GetFileInfoAsync(baseDir);
                Assert.IsTrue(info.IsDirectory);

                info = await provider.GetFileInfoAsync(baseDir, "missing");
                Assert.IsFalse(info.Exists || info.IsDirectory);
            }
            finally
            {
                await DeleteRaw(client, $"{baseDir}/");
            }
        }

        [TestMethod]
        public async Task Delete_ReturnsTrueOnSuccessAndFalseWhenMissing()
        {
            var provider = LocalStackS3.Provider();
            using var client = LocalStackS3.Client();
            var baseDir = NewName("s3del");
            try
            {
                await provider.UpdateFileAsync(Content("1"), UpdateFileMode.Overwrite, baseDir, "file.txt");
                await provider.UpdateFileAsync(Content("2"), UpdateFileMode.Overwrite, baseDir, "sub", "inner.txt");

                Assert.IsTrue(await provider.DeleteAsync(baseDir, "file.txt"), "Deleting an existing file returned false");
                Assert.IsFalse((await provider.GetFileInfoAsync(baseDir, "file.txt")).Exists);
                Assert.IsFalse(await provider.DeleteAsync(baseDir, "file.txt"), "Deleting a missing file returned true");
                Assert.IsFalse(await provider.DeleteAsync(baseDir, "missing-dir"), "Deleting a missing folder returned true");

                Assert.IsTrue(await provider.DeleteAsync(baseDir), "Deleting an existing folder returned false");
                Assert.AreEqual(0, await CountKeys(client, $"{baseDir}/"));
                Assert.IsFalse(await provider.DeleteAsync(baseDir), "Deleting a deleted folder returned true");
            }
            finally
            {
                await DeleteRaw(client, $"{baseDir}/");
            }
        }

        [TestMethod]
        public async Task Delete_FolderWithMoreThan1000Keys()
        {
            const int count = 1100;
            var provider = LocalStackS3.Provider();
            using var client = LocalStackS3.Client();
            var baseDir = NewName("s3bulk");
            try
            {
                using (var gate = new SemaphoreSlim(32))
                    await Task.WhenAll(Enumerable.Range(0, count).Select(async i =>
                    {
                        await gate.WaitAsync();
                        try
                        {
                            await PutRaw(client, $"{baseDir}/sub{i % 3}/f{i:D4}.txt", "x");
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }));
                await PutRaw(client, $"{baseDir}x/sibling.txt", "keep");
                await PutRaw(client, $"{baseDir}.txt", "keep");
                Assert.AreEqual(count, await CountKeys(client, $"{baseDir}/"));

                Assert.IsTrue(await provider.DeleteAsync(baseDir));
                Assert.AreEqual(0, await CountKeys(client, $"{baseDir}/"), "Keys are left after the folder delete");
                Assert.IsFalse((await provider.GetDirectoryContentsAsync(baseDir)).Exists);
                Assert.IsTrue((await provider.GetFileInfoAsync($"{baseDir}x", "sibling.txt")).Exists, "A sibling folder sharing the name prefix was deleted");
                Assert.IsTrue((await provider.GetFileInfoAsync($"{baseDir}.txt")).Exists, "A sibling file sharing the name prefix was deleted");
            }
            finally
            {
                await DeleteRaw(client, $"{baseDir}/");
                await DeleteRaw(client, $"{baseDir}x/");
                await DeleteRaw(client, $"{baseDir}.txt");
            }
        }

        [TestMethod]
        public async Task AbsoluteReadAndListingStayUnderTheRoot()
        {
            var rootDir = NewName("s3abs");
            var outside = NewName("s3outside") + ".txt";
            var provider = LocalStackS3.Provider(rootDir);
            using var client = LocalStackS3.Client();
            try
            {
                await PutRaw(client, outside, "outside");
                await provider.UpdateFileAsync(Content("inside"), UpdateFileMode.Overwrite, "dir", "file.txt");

#pragma warning disable CS0618
                // Binds to ReadFileAsync(versionId, absolutePathSegments); "file.txt" is not under the root, so it throws
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => provider.ReadFileAsync("dir", "file.txt"));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => provider.ReadFileAsync(null, outside));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => provider.ReadFileAsync(null, rootDir, "..", outside));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => provider.ReadFileAsync(null, rootDir));
                using (var reader = new StreamReader(await provider.ReadFileAsync(null, rootDir, "dir", "file.txt")))
                    Assert.AreEqual("inside", await reader.ReadToEndAsync());
#pragma warning restore CS0618

                using (var reader = new StreamReader(await provider.ReadFileVersionAsync(null, "dir", "file.txt")))
                    Assert.AreEqual("inside", await reader.ReadToEndAsync());
                using (var reader = new StreamReader(await provider.ReadFileVersionAsync("", "dir/file.txt")))
                    Assert.AreEqual("inside", await reader.ReadToEndAsync());
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => provider.ReadFileVersionAsync(null));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => provider.ReadFileVersionAsync(null, "..", outside));

                await Assert.ThrowsExceptionAsync<ArgumentException>(() => provider.EnumerateObjects(new string[0], true));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => provider.EnumerateObjects(new[] { outside }, false));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => provider.EnumerateObjects(new[] { rootDir, ".." }, true));
                var listed = await provider.EnumerateObjects(new[] { rootDir }, true);
                CollectionAssert.AreEqual(new[] { "dir", "file.txt" }, ((FileInfo)listed.Single(e => !e.IsDirectory)).PathSegments);
            }
            finally
            {
                await DeleteRaw(client, $"{rootDir}/");
                await DeleteRaw(client, outside);
            }
        }

        [TestMethod]
        public async Task Delete_ProviderRootIsRefused()
        {
            var rootDir = NewName("s3guard");
            using var client = LocalStackS3.Client();
            var noRoot = LocalStackS3.Provider();
            var withRoot = LocalStackS3.Provider(rootDir);
            try
            {
                await withRoot.UpdateFileAsync(Content("1"), UpdateFileMode.Overwrite, "keep.txt");

                foreach (var provider in new[] { noRoot, withRoot })
                {
                    await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => provider.DeleteAsync(""));
                    await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => provider.DeleteAsync(new string[0]));
                }

                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new RootGuardProbe(LocalStackS3.Options(null)).DeleteAbsolute());
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new RootGuardProbe(LocalStackS3.Options(rootDir)).DeleteAbsolute(rootDir));

                Assert.IsTrue((await withRoot.GetFileInfoAsync("keep.txt")).Exists, "Content under the root was deleted");
            }
            finally
            {
                await DeleteRaw(client, $"{rootDir}/");
            }
        }
    }
}
