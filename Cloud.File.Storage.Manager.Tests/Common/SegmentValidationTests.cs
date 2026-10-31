using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Cloud.File.Storage.Manager.Common;
using Cloud.File.Storage.Manager.Physical;
using FileInfo = Cloud.File.Storage.Manager.Common.FileInfo;

namespace Cloud.File.Storage.Manager.Tests.Common
{
    [TestClass]
    public class SegmentValidationTests
    {
        private string root;
        private PhysicalFileStorage provider;

        private class SegmentProvider : PhysicalFileStorage
        {
            public SegmentProvider(string rootPath) : base(rootPath) { }
            public static string[] Validate(params string[] segments) => validateSegments(segments);
        }

        private class MemoryProvider : CloudFileStorageManager<FileProviderOptions>
        {
            public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>();
            public MemoryProvider(string rootPath) : base(new FileProviderOptions { RootDirectoryPath = rootPath }) { }

            protected override Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
            {
                var key = string.Join("/", absolutePathSegments);
                var exists = Files.TryGetValue(key, out var data);
                return Task.FromResult<IFileInfo>(new FileInfo(this, exists, exists ? data.Length : -1, key, absolutePathSegments.LastOrDefault() ?? "",
                    DateTimeOffset.UtcNow, false, getRelativeSegments(absolutePathSegments)));
            }

            protected override Task<Stream> readFileAsync(params string[] absolutePathSegments) =>
                Task.FromResult<Stream>(new MemoryStream(Files[string.Join("/", absolutePathSegments)]));

            protected override async Task updateFileAsync(Stream contents, UpdateFileMode mode, params string[] absolutePathSegments)
            {
                using var m = new MemoryStream();
                await contents.CopyToAsync(m);
                Files[string.Join("/", absolutePathSegments)] = m.ToArray();
            }

            protected override Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments) => throw new NotSupportedException();
            protected override Task<IChangeToken> watchAsync(string filter) => throw new NotSupportedException();
            protected override Task<bool> deleteAsync(params string[] absolutePathSegments) => throw new NotSupportedException();
            protected override Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity) => throw new NotSupportedException();
            protected override Task move(string[] oldSegments, string[] newSegments) => throw new NotSupportedException();
        }

        [TestInitialize]
        public void Init()
        {
            root = Path.Combine(Path.GetTempPath(), "wfp-segments-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            provider = new PhysicalFileStorage(root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private static MemoryStream Content() => new MemoryStream(new[] { (byte)1 });

        [TestMethod]
        public void SeparatorsInsideSegmentsAreSplit()
        {
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, SegmentProvider.Validate("a/b", "c"));
            CollectionAssert.AreEqual(new[] { "a", "b" }, SegmentProvider.Validate(@"a\b"));
            CollectionAssert.AreEqual(new string[0], SegmentProvider.Validate());
        }

        [TestMethod]
        public async Task SplitSegmentIsWrittenAsNestedPath()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "a/b", "c.txt");
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "a", "b", "c.txt")));
            var info = (FileInfo)await provider.GetFileInfoAsync("a/b", "c.txt");
            Assert.IsTrue(info.Exists);
            CollectionAssert.AreEqual(new[] { "a", "b", "c.txt" }, info.PathSegments);
        }

        [TestMethod]
        [DataRow("my file.txt")]
        [DataRow("árvíztűrő tükörfúrógép.txt")]
        [DataRow("日本語.txt")]
        [DataRow("a..b.txt")]
        [DataRow(".hidden")]
        public async Task AcceptedNames(string name)
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "dir", name);
            Assert.IsTrue((await provider.GetFileInfoAsync("dir", name)).Exists);
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "dir", name)));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(".")]
        [DataRow("..")]
        [DataRow("...")]
        [DataRow(" ")]
        [DataRow(". .")]
        [DataRow("a/../../x")]
        [DataRow("%2e%2e")]
        [DataRow("%252e%252e")]
        [DataRow("a%2F..%2Fb")]
        [DataRow("a%2Fb")]
        [DataRow("a%5Cb")]
        [DataRow("a%252Fb")]
        [DataRow("a\u0001b")]
        [DataRow("a:b")]
        [DataRow("a/")]
        public async Task RejectedSegments(string segment)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GetFileInfoAsync(new[] { segment }));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ReadFileAsync(new[] { "x", segment }));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GetDirectoryContentsAsync(new[] { segment }));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "x", segment));
            Assert.ThrowsExactly<ArgumentException>(() => provider.Delete(new[] { "x", segment }));
            Assert.ThrowsExactly<ArgumentException>(() => provider.Move(new[] { "x", segment }, new[] { "y" }));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "x")));
        }

        [TestMethod]
        [DataRow("..")]
        [DataRow("a/../../x")]
        [DataRow("a/./b")]
        [DataRow(@"a\..\b")]
        [DataRow("%2e%2e/x")]
        public async Task RejectedSubpaths(string subpath)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GetFileInfoAsync(subpath));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.DeleteAsync(subpath));
            Assert.ThrowsExactly<ArgumentException>(() => provider.ReadFile(subpath));
        }

        [TestMethod]
        public void RejectionMessageNamesOnlyTheSegment()
        {
            var e = Assert.ThrowsExactly<ArgumentException>(() => provider.GetFileInfo(new[] { "secret-parent", ".." }));
            StringAssert.Contains(e.Message, "'..'");
            Assert.IsFalse(e.Message.Contains("secret-parent"));
            Assert.IsFalse(e.Message.Contains(root));
        }

        [TestMethod]
        public async Task EmptySegmentDoesNotDeleteParent()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "keep", "f.txt");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.DeleteAsync(new[] { "keep", "" }));
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "keep", "f.txt")));
        }

        [TestMethod]
        public async Task RootDeleteIsRefused()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "f.txt");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAsync(""));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAsync("/"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAsync(new string[0]));
            Assert.ThrowsExactly<InvalidOperationException>(() => provider.Delete(""));
            Assert.ThrowsExactly<InvalidOperationException>(() => provider.Delete(new string[0]));
            var rootInfo = await provider.GetFileInfoAsync("");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAsync(rootInfo));
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "f.txt")));
        }

        [TestMethod]
        public async Task RootMoveAndUpdateAreRefused()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "f.txt");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.MoveAsync("", "moved"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.MoveAsync("f.txt", ""));
            Assert.ThrowsExactly<InvalidOperationException>(() => provider.Move("", "moved"));
            var info = await provider.GetFileInfoAsync("f.txt");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.MoveAsync(info, new string[0]));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.UpdateFileAsync("", UpdateFileMode.Overwrite, Content()));
            Assert.ThrowsExactly<InvalidOperationException>(() => provider.UpdateFile(Content(), UpdateFileMode.Append, new string[0]));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.CreateLocalFileStreamAsync(false, ""));
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "f.txt")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "moved")));
        }

        [TestMethod]
        public async Task ZeroSegmentReadAndDownloadUrlAreRefused()
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ReadFileAsync(""));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ReadFileAsync(new string[0]));
            Assert.ThrowsExactly<ArgumentException>(() => provider.ReadFile("/"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.CreateLocalFileStreamAsync(true, ""));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GetDownloadUrlAsync("", TimeSpan.FromMinutes(1)));
            var rootInfo = await provider.GetFileInfoAsync("");
            Assert.ThrowsExactly<ArgumentException>(() => provider.GetDownloadUrl(rootInfo, TimeSpan.FromMinutes(1)));
        }

        [TestMethod]
        public async Task MoveToItselfKeepsTheFile()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "same", "f.txt");
            await provider.MoveAsync("same/f.txt", "same/f.txt");
            await provider.MoveAsync(new[] { "same", "f.txt" }, new[] { "same/f.txt" });
            provider.Move(await provider.GetFileInfoAsync("same", "f.txt"), new[] { "same", "f.txt" });
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "same", "f.txt")));
        }

        [TestMethod]
        public async Task ZeroSegmentFileInfoIsNotFound()
        {
            foreach (var info in new[] { await provider.GetFileInfoAsync(""), await provider.GetFileInfoAsync(new string[0]), provider.GetFileInfo("/") })
            {
                Assert.IsFalse(info.Exists);
                Assert.IsFalse(info.IsDirectory);
            }
            Assert.IsTrue((await provider.GetDirectoryContentsAsync("")).Exists);
        }

        [TestMethod]
        public async Task MoveByFileInfoLandsUnderTheRoot()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "src", "a.txt");
            var info = await provider.GetFileInfoAsync("src", "a.txt");
            await provider.MoveAsync(info, new[] { "dst", "b.txt" });
            Assert.IsFalse(System.IO.File.Exists(Path.Combine(root, "src", "a.txt")));
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "dst", "b.txt")));
            Assert.IsTrue((await provider.GetFileInfoAsync("dst/b.txt")).Exists);
        }

        [TestMethod]
        public async Task LocalStreamUploadsToTheTargetFixedAtCreation()
        {
            var memory = new MemoryProvider("base");
            using (var created = await memory.CreateLocalFileStreamAsync(false, "dir", "created.txt"))
            {
                created.TargetFilePathSegments[1] = "other";
                created.Write(new[] { (byte)2 }, 0, 1);
            }
            CollectionAssert.AreEqual(new[] { (byte)2 }, memory.Files["base/dir/created.txt"]);

            var target = new[] { "base", "dir", "forged.txt" };
            var stream = new LocalFileStream(Content(), memory, false, target);
            target[1] = "..";
            target[2] = "escaped.txt";
            await stream.DisposeAsync();
            CollectionAssert.AreEqual(new[] { (byte)1 }, memory.Files["base/dir/forged.txt"]);
            CollectionAssert.AreEquivalent(new[] { "base/dir/created.txt", "base/dir/forged.txt" }, memory.Files.Keys.ToArray());
        }

        [TestMethod]
        public async Task LocalStreamOfAnotherProviderIsRefused()
        {
            var owner = new MemoryProvider("base");
            var other = new MemoryProvider("base");
            var stream = await owner.CreateLocalFileStreamAsync(false, "f.txt");
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => other.OnLocalFileStreamDisposingAsync(stream));
            Assert.AreEqual("The local file stream belongs to another provider.", ex.Message);
            Assert.AreEqual(0, other.Files.Count);
            await stream.DisposeAsync();
            Assert.IsTrue(owner.Files.ContainsKey("base/f.txt"));
        }

        [TestMethod]
        public async Task ForgedLocalStreamOutsideTheRootIsRefused()
        {
            var memory = new MemoryProvider("base");
            foreach (var target in new[] { new[] { "other", "f.txt" }, new[] { "f.txt" }, new string[0], new[] { "BASE", "f.txt" }, new[] { "base", "..", "f.txt" }, new[] { "base", "a%2F..%2Fb" } })
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => new LocalFileStream(Content(), memory, false, target).DisposeAsync(), string.Join("/", target));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new LocalFileStream(Content(), memory, false, new[] { "base" }).DisposeAsync());
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => new LocalFileStream(Content(), memory, false, null).DisposeAsync());
            await new LocalFileStream(Content(), memory, true, new[] { "other", "f.txt" }).DisposeAsync();
            Assert.AreEqual(0, memory.Files.Count);
        }

        [TestMethod]
        public async Task ListingEntriesCanBeReused()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "list", "sub", "f.txt");
            var entries = (await provider.GetDirectoryContentsAsync("list")).ToList();
            var dir = entries.Single(e => e.Name == "sub");
            Assert.IsTrue(dir.IsDirectory);
            Assert.IsTrue(await provider.DeleteAsync(dir));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "list", "sub")));
            Assert.IsTrue(Directory.Exists(Path.Combine(root, "list")));
        }
    }
}
