using Cloud.File.Storage.Manager.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Cloud.File.Storage.Manager.AzureBlob
{
    public class AzureCloudFileStorageManager : CloudFileStorageManager<AzureCloudFileStorageManagerOptions>
    {
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);
        private CloudBlobContainer Container { get; }

        public AzureCloudFileStorageManager(AzureCloudFileStorageManagerOptions options) : base(options)
        {
            if (string.IsNullOrEmpty(Options.ConnectionString))
                throw new ArgumentException("ConnectionString is required", nameof(Options.ConnectionString));

            if (string.IsNullOrEmpty(Options.ContainerName))
                throw new ArgumentException("ContainerName is required", nameof(Options.ContainerName));

            var storageAcc = CloudStorageAccount.Parse(Options.ConnectionString);
            Container = storageAcc.CreateCloudBlobClient().GetContainerReference(Options.ContainerName);
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

        private async Task<(CloudBlob[] files, CloudBlobDirectory[] directories)> enumerateFiles(string[] absolutePathSegments, bool deep)
        {
            var ret = new List<CloudBlob>();
            var retDirs = new List<CloudBlobDirectory>();
            var contToken = new BlobContinuationToken();

            while (contToken != null)
            {
                var req = await Container.ListBlobsSegmentedAsync($"{getPath(absolutePathSegments)}/", deep, BlobListingDetails.All, null,
                    contToken, new BlobRequestOptions(), new OperationContext());

                contToken = req.ContinuationToken;
                ret.AddRange(req.Results.OfType<CloudBlob>());
                retDirs.AddRange(req.Results.OfType<CloudBlobDirectory>());
            }

            return (ret.ToArray(), retDirs.ToArray());
        }

        private async Task Create(string[] absolutePathSegments)
        {
            var file = Container.GetAppendBlobReference(getPath(absolutePathSegments));
            await file.CreateOrReplaceAsync();
        }

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            await Container.CreateIfNotExistsAsync();
            var file = Container.GetAppendBlobReference(getPath(absolutePathSegments));
            var exists = await file.ExistsAsync();
            if (exists)
                await file.FetchAttributesAsync();
            return new Common.FileInfo(this, exists, file.Properties.Length,
                getPath(absolutePathSegments), absolutePathSegments.Last(), file.Properties.LastModified ?? DefaultDateTime, false, getRelativeSegments(absolutePathSegments));

        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            var e = await enumerateFiles(absolutePathSegments, false);
            var ret = new List<IFileInfo>();
            var metadataJobs = new List<Task>();
            foreach (var file in e.files)
            {
                var t = new Task(async () =>
                {
                    await file.FetchAttributesAsync();
                });
                metadataJobs.Add(t);
                t.Start();
            }
            Task.WaitAll(metadataJobs.ToArray());

            foreach (var file in e.files)
                ret.Add(new Common.FileInfo(this, true, file.Properties.Length, file.Name, getFileName(file.Name), file.Properties.LastModified ?? DefaultDateTime, false, getRelativeSegments(absolutePathSegments.Concat(new[] { file.Name }).ToArray())));
            foreach (var dir in e.directories)
                ret.Add(new Common.FileInfo(this, true, -1, dir.Prefix, getFileName(dir.Prefix), DefaultDateTime, true, getRelativeSegments(absolutePathSegments.Concat(new[] { getFileName(dir.Prefix) }).ToArray())));
            return new DirectoryContents(ret.Any(), ret.ToArray());
        }

        protected override Task<IChangeToken> watchAsync(string filter)
        {
            throw new NotImplementedException("Watch is not supported in AzureCloudFileStorageManager");
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {
            var file = Container.GetBlobReference(getPath(absolutePathSegments));
            return await file.OpenReadAsync();
        }

        protected override Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            var file = getPath(absolutePathSegments);
            var blob = Container.GetBlobReference(file);

            string signature = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy()
            {
                SharedAccessStartTime = DateTimeOffset.UtcNow,
                SharedAccessExpiryTime = DateTimeOffset.UtcNow.Add(validity),
                Permissions = SharedAccessBlobPermissions.Read
            });

            var urlString = blob.StorageUri.PrimaryUri.ToString() + signature;
            return Task.FromResult(urlString);
        }

        protected override async Task updateFileAsync(Stream contents, params string[] absolutePathSegments)
        {
            var file = Container.GetAppendBlobReference(getPath(absolutePathSegments));
            await file.UploadFromStreamAsync(contents);

        }

        protected override async Task<long> appendFileAsync(Stream contents, params string[] absolutePathSegments)
        {
            var file = Container.GetAppendBlobReference(getPath(absolutePathSegments));

            if (!await file.ExistsAsync())
                await Create(absolutePathSegments);

            await file.AppendFromStreamAsync(contents);
            await file.FetchAttributesAsync();

            return file.Properties.Length;
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            var file = Container.GetBlobReference(getPath(absolutePathSegments));
            var ret = await file.DeleteIfExistsAsync();

            foreach (var f in (await enumerateFiles(absolutePathSegments, true)).files)
            {
                file = Container.GetAppendBlobReference(f.Name);
                await file.DeleteAsync();
                ret = true;
            }

            return ret;
        }
    }
}
