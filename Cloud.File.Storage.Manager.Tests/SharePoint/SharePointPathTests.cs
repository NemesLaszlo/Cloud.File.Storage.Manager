using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Cloud.File.Storage.Manager.Common;
using Cloud.File.Storage.Manager.SharePoint;

namespace Cloud.File.Storage.Manager.Tests
{
    [TestClass]
    public class SharePointPathTests
    {
        private const string DriveId = "drive1";
        private const string WorkFolderId = "work1";
        private const string WorkFolderPath = "Archive/Stale";

        [TestMethod]
        public void StripWorkFolder_LeadingWorkFolder_IsRemovedCaseInsensitive()
        {
            CollectionAssert.AreEqual(new[] { "wb", "a.twbx" },
                SharePointPaths.StripWorkFolder(WorkFolderPath, new[] { "archive", "STALE", "wb", "a.twbx" }));
        }

        [TestMethod]
        public void StripWorkFolder_OtherPaths_AreKept()
        {
            CollectionAssert.AreEqual(new[] { "wb", "a.twbx" }, SharePointPaths.StripWorkFolder(WorkFolderPath, new[] { "wb", "a.twbx" }));
            CollectionAssert.AreEqual(new[] { "Archive", "Other" }, SharePointPaths.StripWorkFolder(WorkFolderPath, new[] { "Archive", "Other" }));
            CollectionAssert.AreEqual(new[] { "Archive" }, SharePointPaths.StripWorkFolder(WorkFolderPath, new[] { "Archive" }));
            CollectionAssert.AreEqual(new[] { "Archive", "Stale" }, SharePointPaths.StripWorkFolder(null, new[] { "Archive", "Stale" }));
            CollectionAssert.AreEqual(new[] { "Archive", "Stale" }, SharePointPaths.StripWorkFolder("", new[] { "Archive", "Stale" }));
        }

        [TestMethod]
        public void StripWorkFolder_WorkFolderSlashesAndBackslashes_AreNormalized()
        {
            CollectionAssert.AreEqual(new[] { "x" }, SharePointPaths.StripWorkFolder("\\Archive\\Stale\\", new[] { "Archive", "Stale", "x" }));
            CollectionAssert.AreEqual(new[] { "x" }, SharePointPaths.StripWorkFolder("/Archive/Stale/", new[] { "Archive", "Stale", "x" }));
            Assert.AreEqual("x/y.txt", SharePointPaths.GetRelativePath(WorkFolderPath, new[] { "Archive", "Stale", "x", "y.txt" }));
        }

        [TestMethod]
        public void StripWorkFolder_WorkFolderItself_IsEmpty()
        {
            Assert.AreEqual(0, SharePointPaths.StripWorkFolder(WorkFolderPath, new[] { "Archive", "Stale" }).Length);
            Assert.IsTrue(SharePointPaths.IsProviderRoot(WorkFolderPath, new string[0], new[] { "Archive", "Stale" }));
            Assert.IsTrue(SharePointPaths.IsProviderRoot(null, new string[0], new string[0]));
            Assert.IsTrue(SharePointPaths.IsProviderRoot(null, new[] { "root" }, new[] { "root" }));
            Assert.IsFalse(SharePointPaths.IsProviderRoot(WorkFolderPath, new string[0], new[] { "Archive", "Stale", "a.txt" }));
        }

        [TestMethod]
        public async Task Delete_WorkFolderItself_IsRefused()
        {
            var (provider, handler) = createProvider();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAsync("Archive", "Stale"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAsync("Archive/Stale"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAsync(""));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Move_FromOrToWorkFolderItself_IsRefused()
        {
            var (provider, handler) = createProvider();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.MoveAsync("Archive/Stale", "Archive/Stale/x"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.MoveAsync("Archive/Stale/x", "Archive/Stale"));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Update_WorkFolderItself_IsRefused()
        {
            var (provider, handler) = createProvider();
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.UpdateFileAsync(content, UpdateFileMode.Overwrite, "Archive", "Stale"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.UpdateFileAsync(content, UpdateFileMode.Append, "Archive", "Stale"));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task GetFileInfo_WorkFolderItself_StillWorks()
        {
            var (provider, handler) = createProvider();
            var info = await provider.GetFileInfoAsync("Archive", "Stale");
            Assert.IsTrue(info.Exists);
            Assert.IsTrue(info.IsDirectory);
            Assert.AreEqual($"/v1.0/drives/{DriveId}/items/{WorkFolderId}", handler.Requests.Single().AbsolutePath);
        }

        [TestMethod]
        public async Task Segment_WithColon_IsRejected()
        {
            var (provider, handler) = createProvider();
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GetFileInfoAsync("a:b"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.DeleteAsync("dir", "a%3Ab"));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Listing_ReturnsEachItemOnce_AcrossPages()
        {
            var (provider, handler) = createProvider();
            var contents = await provider.GetDirectoryContentsAsync("Archive", "Stale", "wb");

            var names = contents.Select(e => e.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "a.txt", "b.txt", "sub" }, names);
            Assert.IsTrue(contents.All(e => e.Exists));
            Assert.IsTrue(contents.Single(e => e.Name == "sub").IsDirectory);
            Assert.AreEqual(2, handler.Requests.Count);
            Assert.IsTrue(handler.Requests[0].AbsolutePath.EndsWith($"/items/{WorkFolderId}:/wb:/children"), handler.Requests[0].AbsolutePath);
        }

        [TestMethod]
        public async Task Listing_WithoutWorkFolderPrefix_UsesSameFolder()
        {
            var (provider, handler) = createProvider();
            await provider.GetDirectoryContentsAsync("wb");
            Assert.IsTrue(handler.Requests[0].AbsolutePath.EndsWith($"/items/{WorkFolderId}:/wb:/children"), handler.Requests[0].AbsolutePath);
        }

        private static (SharePointCloudFileStorageManager provider, FakeGraphHandler handler) createProvider()
        {
            var handler = new FakeGraphHandler();
            var client = new GraphServiceClient(new HttpClient(handler), new AnonymousAuthenticationProvider());
            var options = new SharePointCloudFileStorageManagerOptions("https://contoso.sharepoint.com/sites/test", "Documents", "tenant", "client", "secret", WorkFolderPath);
            return (new SharePointCloudFileStorageManager(options, client, DriveId, WorkFolderId), handler);
        }

        private sealed class FakeGraphHandler : HttpMessageHandler
        {
            public List<Uri> Requests { get; } = new List<Uri>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request.RequestUri);
                var url = request.RequestUri.ToString();
                string json;
                if (url.Contains("skiptoken"))
                    json = "{\"value\":[" + file("2", "b.txt") + "]}";
                else if (request.RequestUri.AbsolutePath.EndsWith("/children"))
                    json = "{\"value\":[" + file("1", "a.txt") + "," + folder("3", "sub") + "],"
                        + "\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/drives/" + DriveId + "/items/" + WorkFolderId + "/children?$skiptoken=page2\"}";
                else
                    json = folder(WorkFolderId, "Stale");

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            private static string file(string id, string name) =>
                "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"size\":3,\"lastModifiedDateTime\":\"2026-01-02T03:04:05Z\",\"file\":{}}";

            private static string folder(string id, string name) =>
                "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"lastModifiedDateTime\":\"2026-01-02T03:04:05Z\",\"folder\":{\"childCount\":0}}";
        }
    }
}
