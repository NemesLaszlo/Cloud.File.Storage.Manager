using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Cloud.File.Storage.Manager.Common;
using Cloud.File.Storage.Manager.Physical;

namespace Cloud.File.Storage.Manager.Tests.Physical
{
    [TestClass]
    public class PhysicalReadTests
    {
        private string root;
        private TestProvider provider;

        private class TestProvider : PhysicalFileStorage
        {
            public TestProvider(string rootPath) : base(rootPath) { }
            public string[] Absolute(params string[] relative) => Options.RootDirectorySegments.Concat(relative).ToArray();
            public Task<Stream> ReadAbsoluteAsync(params string[] absolute) => readFileAsync(absolute);
            public Task<bool> DeleteAbsoluteAsync(params string[] absolute) => deleteAsync(absolute);
            public Task UpdateAbsoluteAsync(Stream contents, params string[] absolute) => updateFileAsync(contents, UpdateFileMode.Overwrite, absolute);
            public Task<IDirectoryContents> ListAbsoluteAsync(params string[] absolute) => getDirectoryContentsAsync(absolute);
        }

        [TestInitialize]
        public void Init()
        {
            root = Path.Combine(Path.GetTempPath(), "wfp-physical-" + Guid.NewGuid().ToString("N"), "root");
            Directory.CreateDirectory(root);
            provider = new TestProvider(root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            var parent = Path.GetDirectoryName(root);
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }

        private static MemoryStream Content() => new MemoryStream(new[] { (byte)7, (byte)8 });

        [TestMethod]
        public async Task MissingReadThrowsAndCreatesNothing()
        {
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => provider.ReadFileAsync("missing", "file.txt"));
            Assert.ThrowsExactly<FileNotFoundException>(() => provider.ReadFile("missing/file.txt"));
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => provider.ReadFileAsync("file.txt"));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "missing")));
            Assert.IsFalse(System.IO.File.Exists(Path.Combine(root, "file.txt")));
        }

        [TestMethod]
        public async Task MissingReadOnlyLocalStreamThrowsAndCreatesNothing()
        {
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => provider.CreateLocalFileStreamAsync(true, "missing/file.txt"));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "missing")));
        }

        [TestMethod]
        public async Task DirectoryReadThrows()
        {
            Directory.CreateDirectory(Path.Combine(root, "dir"));
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => provider.ReadFileAsync("dir"));
        }

        [TestMethod]
        public async Task PresentReadWorks()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "present", "file.txt");
            using (var s = await provider.ReadFileAsync("present/file.txt"))
            using (var m = new MemoryStream())
            {
                await s.CopyToAsync(m);
                CollectionAssert.AreEqual(new[] { (byte)7, (byte)8 }, m.ToArray());
            }
            using (var local = await provider.CreateLocalFileStreamAsync(true, "present/file.txt"))
                Assert.AreEqual(2, local.Length);
        }

        [TestMethod]
        public async Task WriteCreatesFolders()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "w1", "w2", "file.txt");
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "w1", "w2", "file.txt")));
            using (var local = await provider.CreateLocalFileStreamAsync(false, "l1/l2/file.txt"))
                local.Write(new[] { (byte)1 }, 0, 1);
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "l1", "l2", "file.txt")));
        }

        [TestMethod]
        public async Task MoveCreatesTargetFolder()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "file.txt");
            await provider.MoveAsync("file.txt", "new/folder/file.txt");
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "new", "folder", "file.txt")));
            Assert.IsFalse(System.IO.File.Exists(Path.Combine(root, "file.txt")));
        }

        [TestMethod]
        public async Task DeleteOfRootIsRefused()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "file.txt");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAsync(""));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAbsoluteAsync(provider.Absolute()));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAbsoluteAsync(provider.Absolute("sub", "..")));
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "file.txt")));
        }

        [TestMethod]
        public async Task DownloadUrlIsNotSupported()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "file.txt");
            Assert.IsFalse(provider.SupportsDownloadUrl);
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => provider.GetDownloadUrlAsync("file.txt", TimeSpan.FromHours(1)));
        }

        [TestMethod]
        public async Task ContainmentEscapeIsRefused()
        {
            var outside = Path.Combine(Path.GetDirectoryName(root), "outside.txt");
            System.IO.File.WriteAllBytes(outside, new byte[] { 1 });
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ReadAbsoluteAsync(provider.Absolute("..", "outside.txt")));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.DeleteAbsoluteAsync(provider.Absolute("..", "outside.txt")));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.DeleteAbsoluteAsync(provider.Absolute("..")));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.UpdateAbsoluteAsync(Content(), provider.Absolute("..", "escaped.txt")));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ListAbsoluteAsync(provider.Absolute("..")));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ReadAbsoluteAsync(provider.Absolute("..", Path.GetFileName(root) + "-sibling", "f.txt")));
            Assert.IsTrue(System.IO.File.Exists(outside));
            Assert.IsFalse(System.IO.File.Exists(Path.Combine(Path.GetDirectoryName(root), "escaped.txt")));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ReadFileAsync("..", "outside.txt"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ReadFileAsync("c:", "outside.txt"));
        }

        [TestMethod]
        [DataRow("file.")]
        [DataRow("file ")]
        [DataRow("file%2E")]
        [DataRow("CON")]
        [DataRow("con")]
        [DataRow("nul.txt")]
        [DataRow("Aux.tar.gz")]
        [DataRow("prn .txt")]
        [DataRow("COM0")]
        [DataRow("com9")]
        [DataRow("LPT1.log")]
        [DataRow("COM\u00B9")]
        [DataRow("lpt\u00B3.txt")]
        public async Task ReservedAndTrailingNamesAreRejected(string name)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "dir", name));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GetFileInfoAsync(name));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ReadFileAsync(name, "f.txt"));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "dir")));
        }

        [TestMethod]
        [DataRow("CONSOLE.txt")]
        [DataRow("com10")]
        [DataRow("nullable.txt")]
        [DataRow("LPT")]
        [DataRow("my.con")]
        [DataRow("con-file.txt")]
        public async Task NamesResemblingReservedNamesAreAccepted(string name)
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "dir", name);
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(root, "dir", name)));
        }

        [TestMethod]
        public async Task LinksInsideTheRootAreRefusedUnlessAllowed()
        {
            var target = Path.Combine(Path.GetDirectoryName(root), "link-target");
            Directory.CreateDirectory(target);
            System.IO.File.WriteAllBytes(Path.Combine(target, "f.txt"), new byte[] { 3 });
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            var link = Path.Combine(root, "sub", "link");
            var fileLink = Path.Combine(root, "file-link.txt");
            try
            {
                Directory.CreateSymbolicLink(link, target);
                System.IO.File.CreateSymbolicLink(fileLink, Path.Combine(target, "f.txt"));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException)
            {
                Assert.Inconclusive($"Symbolic links cannot be created: {e.Message}");
            }

            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ReadFileAsync("sub", "link", "f.txt"));
            Assert.AreEqual("Links inside the provider root are not allowed.", ex.Message);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.ReadFileAsync("file-link.txt"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "sub", "link", "new.txt"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GetDirectoryContentsAsync("sub", "link"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.DeleteAsync("sub", "link"));
            Assert.IsFalse(System.IO.File.Exists(Path.Combine(target, "new.txt")));
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(target, "f.txt")));
            Assert.IsTrue((await provider.GetDirectoryContentsAsync("sub")).Exists);

            var allowing = new PhysicalFileStorage(new PhysicalFileStorageOptions { RootDirectoryPath = root, AllowLinks = true });
            using (var s = await allowing.ReadFileAsync("sub", "link", "f.txt"))
                Assert.AreEqual(3, s.ReadByte());
            using (var s = await allowing.ReadFileAsync("file-link.txt"))
                Assert.AreEqual(3, s.ReadByte());

            var linkedRoot = new PhysicalFileStorage(link);
            using (var s = await linkedRoot.ReadFileAsync("f.txt"))
                Assert.AreEqual(3, s.ReadByte());
        }

        [TestMethod]
        public async Task DirectoryEntriesKeepFileSystemTimestamps()
        {
            await provider.UpdateFileAsync(Content(), UpdateFileMode.Overwrite, "ts", "sub", "file.txt");
            var dir = (await provider.GetDirectoryContentsAsync("ts")).Single(e => e.Name == "sub");
            Assert.IsTrue(dir.IsDirectory);
            var expected = new DateTimeOffset(Directory.GetLastWriteTimeUtc(Path.Combine(root, "ts", "sub")));
            Assert.IsTrue((dir.LastModified - expected).Duration() < TimeSpan.FromSeconds(2));
            Assert.IsTrue(dir.LastModified.Year > 1970);
        }
    }
}
