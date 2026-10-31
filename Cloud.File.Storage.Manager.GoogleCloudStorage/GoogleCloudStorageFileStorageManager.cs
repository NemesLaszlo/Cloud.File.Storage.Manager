using Cloud.File.Storage.Manager.Common;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System.Net;

namespace Cloud.File.Storage.Manager.GoogleCloudStorage
{
    /// <summary>
    /// A cloud file storage manager backed by a Google Cloud Storage bucket.
    ///
    /// GCS is a flat object store (object keys may contain '/'). This manager presents the keys as a
    /// virtual folder hierarchy using '/' as the delimiter, mirroring the Amazon S3 and Azure Blob
    /// managers.
    /// </summary>
    public class GoogleCloudStorageFileStorageManager : CloudFileStorageManager<GoogleCloudStorageFileStorageManagerOptions>
    {
        private const string DefaultContentType = "application/octet-stream";
        private const int DeletePageSize = 1000;
        private const int DeleteParallelism = 8;
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);
        private StorageClient Client { get; }
        private UrlSigner UrlSigner { get; }

        /// <summary>
        /// True when no explicit credential is configured and the manager authenticates with Application Default Credentials
        /// (for example Workload Identity or the metadata server).
        /// </summary>
        public bool UsesApplicationDefaultCredentials { get; }

        public GoogleCloudStorageFileStorageManager(GoogleCloudStorageFileStorageManagerOptions options) : base(options)
        {
            if (string.IsNullOrEmpty(Options.BucketName))
                throw new ArgumentException("BucketName is required", nameof(Options.BucketName));

            var credential = createCredential(out var applicationDefault);
            UsesApplicationDefaultCredentials = applicationDefault;
            var builder = new StorageClientBuilder { Credential = credential };
            // STORAGE_EMULATOR_HOST (for example http://localhost:4443) points the client at an emulator; otherwise production as before
            var emulatorHost = Environment.GetEnvironmentVariable("STORAGE_EMULATOR_HOST");
            if (!string.IsNullOrWhiteSpace(emulatorHost) && Uri.TryCreate(emulatorHost.Trim(), UriKind.Absolute, out var emulator))
                builder.BaseUri = emulator.GetLeftPart(UriPartial.Authority) + "/storage/v1/";
            Client = builder.Build();
            // Build the signer up front (this does no network I/O). A service account key signs
            // locally; a Workload Identity / impersonated identity signs via the IAM signBlob API
            // when GetDownloadUrlAsync is called. Only a credential that genuinely cannot sign
            // throws here - in that case leave it null and fail clearly at call time.
            try
            {
                UrlSigner = UrlSigner.FromCredential(credential);
            }
            catch (InvalidOperationException)
            {
                UrlSigner = null;
            }
        }

        private GoogleCredential createCredential(out bool applicationDefault)
        {
            applicationDefault = false;
            // 1. Discrete service account fields (the UI-friendly path).
            if (!string.IsNullOrEmpty(Options.ClientEmail) && !string.IsNullOrEmpty(Options.PrivateKey))
            {
                var initializer = new ServiceAccountCredential.Initializer(Options.ClientEmail)
                {
                    ProjectId = Options.ProjectId
                }.FromPrivateKey(NormalizePem(Options.PrivateKey));
                return GoogleCredential.FromServiceAccountCredential(new ServiceAccountCredential(initializer));
            }
            // 2. Whole service account key JSON as one string (e.g. from a secret store).
            if (!string.IsNullOrEmpty(Options.CredentialsJson))
                return CredentialFactory.FromJson<ServiceAccountCredential>(Options.CredentialsJson).ToGoogleCredential();
            // 3. Application Default Credentials. On GKE/GCE this resolves via Workload Identity /
            // the metadata server, keeping secrets out of the configuration.
            applicationDefault = true;
            return GoogleCredential.GetApplicationDefault();
        }

        internal static string NormalizePem(string pem) => normalizePem(pem);

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            var key = getPath(absolutePathSegments);
            if (absolutePathSegments.Length == 0)
                return new Common.FileInfo(this, false, -1, key, "", DefaultDateTime, false,
                    getRelativeSegments(absolutePathSegments));
            try
            {
                var obj = await Client.GetObjectAsync(Options.BucketName, key);
                return new Common.FileInfo(this, true, (long)(obj.Size ?? 0), key, getFileName(key),
                    updated(obj), false, getRelativeSegments(absolutePathSegments));
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return new Common.FileInfo(this, false, -1, key, getFileName(key), DefaultDateTime, false,
                    getRelativeSegments(absolutePathSegments));
            }
        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            var prefix = getDirPrefix(absolutePathSegments);
            var ret = new List<IFileInfo>();
            var options = new ListObjectsOptions { Delimiter = "/" };
            await foreach (var page in Client.ListObjectsAsync(Options.BucketName, prefix, options).AsRawResponses())
            {
                if (page.Items != null)
                    foreach (var obj in page.Items)
                    {
                        // Skip a directory placeholder object (a key equal to the prefix itself).
                        if (obj.Name == prefix) continue;
                        var name = getFileName(obj.Name);
                        if (string.IsNullOrEmpty(name)) continue;
                        ret.Add(new Common.FileInfo(this, true, (long)(obj.Size ?? 0), obj.Name, name,
                            updated(obj), false,
                            getRelativeSegments(absolutePathSegments.Concat(new[] { name }).ToArray())));
                    }
                if (page.Prefixes != null)
                    foreach (var dir in page.Prefixes)
                    {
                        var name = getFileName(dir);
                        if (string.IsNullOrEmpty(name)) continue;
                        ret.Add(new Common.FileInfo(this, true, -1, dir.TrimEnd('/'), name, DefaultDateTime, true,
                            getRelativeSegments(absolutePathSegments.Concat(new[] { name }).ToArray())));
                    }
            }
            return new DirectoryContents(ret.Any(), ret.ToArray());
        }

        protected override Task<IChangeToken> watchAsync(string filter)
        {
            throw new NotSupportedException("Watch is not supported in GoogleCloudStorageFileStorageManager");
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {
            // GCS only exposes object downloads into a caller-provided stream, so stream the
            // contents into a temp file (deleted on close) and hand that back. This keeps memory
            // usage flat even for very large objects.
            var tempFileName = Path.GetTempFileName();
            var tempStream = new FileStream(tempFileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096,
                FileOptions.DeleteOnClose);
            try
            {
                await Client.DownloadObjectAsync(Options.BucketName, getPath(absolutePathSegments), tempStream);
                tempStream.Position = 0;
                return tempStream;
            }
            catch
            {
                tempStream.Dispose();
                throw;
            }
        }

        protected override async Task updateFileAsync(Stream contents, UpdateFileMode mode, params string[] absolutePathSegments)
        {
            if (mode == UpdateFileMode.Append)
            {
                // GCS has no native append; download the existing object, append, and re-upload.
                // Disposing the local stream commits it back via OnLocalFileStreamDisposingAsync.
                await using var str = await createLocalFileStreamAsync(false, absolutePathSegments);
                str.Position = str.Length;
                await using (contents)
                    await contents.CopyToAsync(str);
            }
            else
            {
                await using (contents)
                    await Client.UploadObjectAsync(Options.BucketName, getPath(absolutePathSegments), DefaultContentType,
                        contents);
            }
        }

        protected override async Task move(string[] oldSegments, string[] newSegments)
        {
            await Client.CopyObjectAsync(Options.BucketName, getPath(oldSegments), Options.BucketName,
                getPath(newSegments));
            await deleteAsync(oldSegments);
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            if (absolutePathSegments.Length == 0 || absolutePathSegments.SequenceEqual(Options.RootDirectorySegments))
                throw new InvalidOperationException("The provider root cannot be deleted or modified.");
            var info = await getFileInfoAsync(absolutePathSegments);
            if (info.Exists)
            {
                await Client.DeleteObjectAsync(Options.BucketName, getPath(absolutePathSegments));
                return true;
            }

            // Not a single file - treat as a directory and delete everything under it, one page at a time.
            var prefix = getDirPrefix(absolutePathSegments);
            var deleted = false;
            var options = new ListObjectsOptions { PageSize = DeletePageSize };
            await foreach (var page in Client.ListObjectsAsync(Options.BucketName, prefix, options).AsRawResponses())
            {
                if (page.Items == null || page.Items.Count == 0)
                    continue;
                await Parallel.ForEachAsync(page.Items,
                    new ParallelOptions { MaxDegreeOfParallelism = DeleteParallelism },
                    async (obj, ct) =>
                    {
                        try
                        {
                            await Client.DeleteObjectAsync(Options.BucketName, obj.Name, cancellationToken: ct);
                        }
                        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
                        {
                        }
                    });
                deleted = true;
            }
            return deleted;
        }

        protected override async Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            if (UrlSigner == null)
                throw new NotSupportedException(
                    "Signed download URLs require a credential that can sign: either an inline service " +
                    "account key (ClientEmail + PrivateKey or CredentialsJson) or a Workload Identity / " +
                    "impersonated identity that holds the iam.serviceAccounts.signBlob permission.");
            // Use the async signer. For non-service-account credentials (Workload Identity) signing
            // performs an IAM signBlob call; the synchronous Sign(...) overload can deadlock there.
            return await UrlSigner.SignAsync(Options.BucketName, getPath(absolutePathSegments), validity,
                HttpMethod.Get);
        }

        // The SDK parses the timestamp strictly; an emulator may send a format it refuses, so fall back to a lenient parse
        private DateTimeOffset updated(Google.Apis.Storage.v1.Data.Object obj)
        {
            try
            {
                return obj.UpdatedDateTimeOffset ?? DefaultDateTime;
            }
            catch (FormatException)
            {
                return DateTimeOffset.TryParse(obj.UpdatedRaw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : DefaultDateTime;
            }
        }

        private string getPath(string[] absolutePathSegments)
        {
            return string.Join("/", absolutePathSegments);
        }

        private string getDirPrefix(string[] absolutePathSegments)
        {
            // The root directory lists with an empty prefix; otherwise list inside the folder.
            return absolutePathSegments.Length == 0 ? "" : getPath(absolutePathSegments) + "/";
        }

        private string getFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var ret = path;
            if (ret.EndsWith("/")) ret = ret.Substring(0, ret.Length - 1);
            if (ret.Contains("/"))
                ret = ret.Substring(ret.LastIndexOf("/") + 1);
            return ret;
        }
    }
}
