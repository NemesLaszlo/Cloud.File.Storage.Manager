using Azure.Storage.Blobs;
using Azure.Storage;
using Cloud.File.Storage.Manager.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Azure.Storage.Blobs.Models;
using Azure;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;

namespace Cloud.File.Storage.Manager.AzureBlob
{
    public class AzureCloudFileStorageManager : CloudFileStorageManager<AzureCloudFileStorageManagerOptions>
    {
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);
        private BlobServiceClient ServiceClient { get; }
        private BlobContainerClient Container { get; }
        private StorageSharedKeyCredential Credential { get; }

        public AzureCloudFileStorageManager(AzureCloudFileStorageManagerOptions options) : base(options)
        {
            if (string.IsNullOrEmpty(Options.AccountName))
                throw new ArgumentException("AccountName is required", nameof(Options.AccountName));
            if (string.IsNullOrEmpty(Options.AccountKey))
                throw new ArgumentException("AccountKey is required", nameof(Options.AccountKey));
            if (string.IsNullOrEmpty(Options.ContainerName))
                throw new ArgumentException("ContainerName is required", nameof(Options.ContainerName));
            if (string.IsNullOrEmpty(Options.Protocol))
                throw new ArgumentException("Protocol is required", nameof(Options.Protocol));

            var sanitizedUrl = $"{Options.AccountName}.blob.core.windows.net";
            if (!string.IsNullOrEmpty(Options.Url))
                sanitizedUrl = Options.Url;
            var blobEndpoint = $"{Options.Protocol}://{sanitizedUrl}";
            Credential = new StorageSharedKeyCredential(Options.AccountName, Options.AccountKey);
            ServiceClient = new BlobServiceClient(new Uri(blobEndpoint), Credential);
            Container = ServiceClient.GetBlobContainerClient(Options.ContainerName);
        }

        private string getPath(string[] absolutePathSegments) => string.Join("/", absolutePathSegments);

        private string getFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (!path.Contains("/")) return path;
            var ret = path;
            if (ret.EndsWith("/")) ret = ret.Substring(0, ret.Length - 1);
            return ret.Substring(ret.LastIndexOf("/") + 1);
        }

        private async Task<(BlobItem[] files, string[] directories)> enumerateFiles(string[] absolutePathSegments, bool deep)
        {
            try
            {
                var files = new List<BlobItem>();
                var dirPath = $"{getPath(absolutePathSegments)}/";
                var items = Container.GetBlobsAsync(prefix: dirPath);
                await foreach (var i in items)
                    files.Add(i);
                return (files.Where(f =>
                {
                    var relativePath = f.Name.Substring(dirPath.Length);
                    return deep || !relativePath.Contains("/");
                }).ToArray(), files.Select(e => e.Name).Select(e => e.Substring(0, e.LastIndexOf("/") + 1)).Where(e => e.EndsWith("/")).Where(d =>
                {
                    var relativePath = d.Substring(dirPath.Length);
                    if (string.IsNullOrEmpty(relativePath))
                        return false;
                    relativePath = relativePath.Substring(0, relativePath.Length - 1);
                    return deep || !relativePath.Contains("/");
                }).Distinct().ToArray());
            }
            catch (RequestFailedException ex)
            {
                if (ex.ErrorCode == BlobErrorCode.ContainerNotFound)
                {
                    await Container.CreateIfNotExistsAsync();
                    return await enumerateFiles(absolutePathSegments, deep);
                }
                throw;
            }
        }

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            try
            {
                var file = Container.GetAppendBlobClient(getPath(absolutePathSegments));
                var properties = await file.GetPropertiesAsync();
                return new Common.FileInfo(this, true, properties.Value.ContentLength, getPath(absolutePathSegments), absolutePathSegments.Last(), properties.Value.LastModified, false, getRelativeSegments(absolutePathSegments));
            }
            catch (RequestFailedException ex)
            {
                if (ex.ErrorCode == BlobErrorCode.BlobNotFound)
                    return new Common.FileInfo(this, false, 0, getPath(absolutePathSegments), absolutePathSegments.Last(), DefaultDateTime, false, getRelativeSegments(absolutePathSegments));
                if (ex.ErrorCode == BlobErrorCode.ContainerNotFound)
                {
                    await Container.CreateIfNotExistsAsync();
                    return await getFileInfoAsync(absolutePathSegments);
                }
                throw;
            }
        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            var e = await enumerateFiles(absolutePathSegments, false);
            var ret = new List<IFileInfo>();
            foreach (var file in e.files)
                ret.Add(new Common.FileInfo(this, true, file.Properties.ContentLength ?? 0, file.Name, getFileName(file.Name), file.Properties.LastModified ?? DefaultDateTime, false, getRelativeSegments(absolutePathSegments.Concat(new[] { file.Name }).ToArray())));
            foreach (var dir in e.directories)
                ret.Add(new Common.FileInfo(this, false, -1, dir, getFileName(dir), DefaultDateTime, true, getRelativeSegments(absolutePathSegments.Concat(new[] { dir }).ToArray())));
            return new DirectoryContents(ret.Any(), ret.ToArray());
        }

        protected override Task<IChangeToken> watchAsync(string filter)
        {
            throw new NotSupportedException("Watch is not supported in AzureCloudFileStorageManager");
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {
            try
            {
                var file = Container.GetAppendBlobClient(getPath(absolutePathSegments));
                var download = await file.DownloadAsync();
                return new AzureFileStream(download.Value.Content, download.Value.ContentLength);
            }
            catch (RequestFailedException ex)
            {
                if (ex.ErrorCode == BlobErrorCode.ContainerNotFound)
                {
                    await Container.CreateIfNotExistsAsync();
                    return await readFileAsync(absolutePathSegments);
                }
                throw;
            }
        }

        protected override async Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            try
            {
                var file = Container.GetBlobClient(getPath(absolutePathSegments));
                var builder = new BlobSasBuilder()
                {
                    BlobContainerName = Container.Name,
                    BlobName = getPath(absolutePathSegments),
                    StartsOn = DateTimeOffset.UtcNow,
                    ExpiresOn = DateTimeOffset.UtcNow.Add(validity)
                };
                builder.SetPermissions(BlobAccountSasPermissions.Read);
                return new BlobUriBuilder(file.Uri)
                {
                    Sas = builder.ToSasQueryParameters(Credential)
                }.ToUri().ToString();
            }
            catch (RequestFailedException ex)
            {
                if (ex.ErrorCode == BlobErrorCode.ContainerNotFound)
                {
                    await Container.CreateIfNotExistsAsync();
                    return await getDownloadUrl(absolutePathSegments, validity);
                }
                throw;
            }
        }

        protected override async Task updateFileAsync(Stream contents, UpdateFileMode mode, params string[] absolutePathSegments)
        {
            var file = Container.GetAppendBlobClient(getPath(absolutePathSegments));
            Stream str;
            try
            {
                await file.CreateIfNotExistsAsync();
                str = await file.OpenWriteAsync(mode == UpdateFileMode.Overwrite);
            }
            catch (RequestFailedException ex)
            {
                if (ex.ErrorCode == BlobErrorCode.ContainerNotFound)
                {
                    await Container.CreateIfNotExistsAsync();
                    await updateFileAsync(contents, mode, absolutePathSegments);
                    return;
                }
                throw;
            }

            await using (str)
                await contents.CopyToAsync(str);
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            try
            {
                var file = Container.GetAppendBlobClient(getPath(absolutePathSegments));
                if (await file.DeleteIfExistsAsync())
                    return true;
                var ret = false;
                foreach (var f in (await enumerateFiles(absolutePathSegments, true)).files)
                    ret = await Container.GetBlobClient(f.Name).DeleteIfExistsAsync();
                return ret;
            }
            catch (RequestFailedException ex)
            {
                if (ex.ErrorCode == BlobErrorCode.ContainerNotFound)
                {
                    await Container.CreateIfNotExistsAsync();
                    return await deleteAsync(absolutePathSegments);
                }
                throw;
            }
        }

        protected override async Task move(string[] oldSegments, string[] newSegments)
        {
            var oldFile = Container.GetAppendBlobClient(getPath(oldSegments));
            var newFile = Container.GetAppendBlobClient(getPath(newSegments));
            var lease = oldFile.GetBlobLeaseClient();
            try
            {
                await lease.AcquireAsync(TimeSpan.FromSeconds(-1));
                await newFile.StartCopyFromUriAsync(oldFile.Uri);
                BlobProperties properties;
                while (true)
                {
                    properties = await newFile.GetPropertiesAsync();
                    if (properties.BlobCopyStatus == CopyStatus.Pending)
                        await Task.Delay(1000);
                    else break;
                }
                switch (properties.BlobCopyStatus)
                {
                    case CopyStatus.Success:
                        await lease.BreakAsync();
                        await deleteAsync(oldSegments);
                        return;
                    case CopyStatus.Failed:
                    case CopyStatus.Aborted:
                        throw new ApplicationException($"Failed to copy file: {properties.CopyStatusDescription}");
                    default:
                        throw new ApplicationException($"Failed to copy file: Unknown status {properties.BlobCopyStatus}");
                }
            }
            catch (RequestFailedException ex)
            {
                if (ex.ErrorCode == BlobErrorCode.ContainerNotFound)
                {
                    await Container.CreateIfNotExistsAsync();
                    await move(oldSegments, newSegments);
                    return;
                }
                throw;
            }
            finally
            {
                try
                {
                    await lease.BreakAsync();
                }
                catch (Exception) { }
            }
        }
    }
}
