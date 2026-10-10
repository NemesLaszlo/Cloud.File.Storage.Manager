using Cloud.File.Storage.Manager.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using System.Net;

namespace Cloud.File.Storage.Manager.Minio
{
    /// <summary>
    /// Cloud file storage manager for Minio and other Amazon S3 compatible object storages.
    /// Folders are key prefixes: listings return one level, folder deletes remove every object under the prefix,
    /// and the bucket is created on the first write.
    /// </summary>
    public class MinioCloudFileStorageManager : CloudFileStorageManager<MinioCloudFileStorageManagerOptions>
    {
        private const int DeleteBatchSize = 1000;
        private const int MaxPresignedUrlSeconds = 7 * 24 * 60 * 60;
        private const string RootModificationMessage = "The provider root cannot be deleted or modified.";
        // Shared by all instances. Attached after Build(), which changes the client's timeout and headers.
        private static readonly HttpClient SharedHttpClient =
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TimeSpan.FromMinutes(30) };
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);
        private volatile bool bucketReady;
        private IMinioClient Client { get; }

        public MinioCloudFileStorageManager(MinioCloudFileStorageManagerOptions options) : base(options)
        {
            var useSsl = options.UseSSL;
            var client = new MinioClient()
                .WithEndpoint(endpoint(options.HostAndPort, ref useSsl))
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithSSL(useSsl)
                .Build();
            client.Config.HttpClient?.Dispose();
            Client = client.WithHttpClient(SharedHttpClient);
        }

        /// <summary>True when the client connects over HTTPS.</summary>
        protected bool UsesSsl => Client.Config.Secure;

        // Removes an http:// or https:// scheme, which then decides useSsl
        private static string endpoint(string hostAndPort, ref bool useSsl)
        {
            if (string.IsNullOrEmpty(hostAndPort))
                return hostAndPort;
            foreach (var (scheme, ssl) in new[] { ("https://", true), ("http://", false) })
                if (hostAndPort.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                {
                    useSsl = ssl;
                    return hostAndPort.Substring(scheme.Length).TrimEnd('/');
                }
            return hostAndPort;
        }

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            if (isRoot(absolutePathSegments))
                return notFound(absolutePathSegments);
            var key = path(absolutePathSegments);
            try
            {
                var stat = await Client.StatObjectAsync(new StatObjectArgs().WithBucket(Options.BucketName).WithObject(key));
                return new Common.FileInfo(this, true, stat.Size, key, fileName(key), stat.LastModified, false,
                    getRelativeSegments(absolutePathSegments));
            }
            catch (ObjectNotFoundException)
            {
            }
            catch (BucketNotFoundException)
            {
                return notFound(absolutePathSegments);
            }

            // Not an object: a directory when at least one key starts with the prefix
            await foreach (var _ in Client.ListObjectsEnumAsync(listArgs(dirPath(absolutePathSegments), false)))
                return new Common.FileInfo(this, true, -1, key, fileName(key), DefaultDateTime, true,
                    getRelativeSegments(absolutePathSegments));
            return notFound(absolutePathSegments);
        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            var prefix = dirPath(absolutePathSegments);
            var exists = false;
            var res = new List<IFileInfo>();
            try
            {
                await foreach (var item in Client.ListObjectsEnumAsync(listArgs(prefix, false)))
                {
                    exists = true;
                    // The client decodes object keys but not common prefixes.
                    var key = item.IsDir ? WebUtility.UrlDecode(item.Key) : item.Key;
                    if (key == null || !key.StartsWith(prefix, StringComparison.Ordinal))
                        continue;
                    var name = key.Substring(prefix.Length).TrimEnd('/');
                    if (name.Length == 0 || name.Contains('/'))
                        continue;
                    var segments = getRelativeSegments(absolutePathSegments.Concat(new[] { name }).ToArray());
                    res.Add(item.IsDir
                        ? new Common.FileInfo(this, true, -1, prefix + name, name, DefaultDateTime, true, segments)
                        : new Common.FileInfo(this, true, (long)item.Size, key, name,
                            item.LastModifiedDateTime ?? DefaultDateTime, false, segments));
                }
            }
            catch (Exception e)
            {
                // Some S3-compatible servers omit the bucket name in NoSuchBucket, which the client then fails to map.
                if (e is BucketNotFoundException || await bucketMissingAsync())
                    return new DirectoryContents(false, new IFileInfo[0]);
                throw;
            }

            return new DirectoryContents(exists, res.ToArray());
        }

        protected override async Task<IChangeToken> watchAsync(string filter)
        {
            throw new NotSupportedException("Watch is not supported in MinioCloudFileStorageManager");
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {
            // Buffered in a temp file, not in memory, so large objects do not fill the heap
            var tmp = new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096,
                FileOptions.DeleteOnClose);
            try
            {
                await Client.GetObjectAsync(new GetObjectArgs().WithBucket(Options.BucketName)
                    .WithObject(path(absolutePathSegments))
                    .WithCallbackStream(async (s, ct) => await s.CopyToAsync(tmp, ct)));
                tmp.Position = 0;
                return tmp;
            }
            catch
            {
                await tmp.DisposeAsync();
                throw;
            }
        }

        protected override async Task updateFileAsync(Stream contents, UpdateFileMode mode, params string[] absolutePathSegments)
        {
            ensureNotRoot(absolutePathSegments);
            await ensureBucketAsync();

            await using var tmp = new FileStream(Path.GetTempFileName(), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 4096, FileOptions.DeleteOnClose);
            if (mode == UpdateFileMode.Append)
            {
                var info = await getFileInfoAsync(absolutePathSegments);
                if (info.Exists && !info.IsDirectory)
                    await using (var s = await readFileAsync(absolutePathSegments))
                        await s.CopyToAsync(tmp);
            }
            await contents.CopyToAsync(tmp);
            await tmp.FlushAsync();
            tmp.Position = 0;
            await Client.PutObjectAsync(new PutObjectArgs().WithBucket(Options.BucketName)
                .WithObject(path(absolutePathSegments))
                .WithStreamData(tmp)
                .WithObjectSize(tmp.Length)
                .WithContentType("application/octet-stream"));
        }

        protected override async Task move(string[] oldSegments, string[] newSegments)
        {
            ensureNotRoot(oldSegments);
            ensureNotRoot(newSegments);
            await ensureBucketAsync();
            await Client.CopyObjectAsync(new CopyObjectArgs().WithBucket(Options.BucketName).WithObject(path(newSegments))
                .WithCopyObjectSource(new CopySourceObjectArgs().WithBucket(Options.BucketName).WithObject(path(oldSegments))));
            await Client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(Options.BucketName).WithObject(path(oldSegments)));
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            ensureNotRoot(absolutePathSegments);
            var info = await getFileInfoAsync(absolutePathSegments);
            if (!info.Exists)
                return false;
            if (!info.IsDirectory)
            {
                await Client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(Options.BucketName)
                    .WithObject(path(absolutePathSegments)));
                return true;
            }

            // Every batch comes from a fresh listing, so no continuation token is reused after a delete.
            var prefix = dirPath(absolutePathSegments);
            var deleted = false;
            string previousFirstKey = null;
            while (true)
            {
                var keys = new List<string>(DeleteBatchSize);
                await foreach (var item in Client.ListObjectsEnumAsync(listArgs(prefix, true)))
                {
                    keys.Add(item.Key);
                    if (keys.Count == DeleteBatchSize)
                        break;
                }
                if (keys.Count == 0)
                    return deleted;
                if (keys[0] == previousFirstKey)
                    throw new IOException($"The objects under '{prefix}' could not be deleted.");
                previousFirstKey = keys[0];

                var errors = await Client.RemoveObjectsAsync(new RemoveObjectsArgs().WithBucket(Options.BucketName)
                    .WithObjects(keys));
                if (errors != null && errors.Count > 0)
                    throw new IOException(
                        $"Failed to delete {errors.Count} object(s) under '{prefix}'. First error: '{errors[0].Key}' {errors[0].Code} {errors[0].Message}");
                deleted = true;
            }
        }

        /// <summary>
        /// Returns a presigned GET URL. The validity is clamped to between 1 second and 7 days (the S3 maximum),
        /// and the link points at <see cref="MinioCloudFileStorageManagerOptions.HostAndPort"/>.
        /// </summary>
        protected override Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            var seconds = (int)Math.Clamp(validity.TotalSeconds, 1, MaxPresignedUrlSeconds);
            return Client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                .WithBucket(Options.BucketName)
                .WithObject(path(absolutePathSegments))
                .WithExpiry(seconds));
        }

        private ListObjectsArgs listArgs(string prefix, bool recursive)
        {
            return new ListObjectsArgs().WithBucket(Options.BucketName).WithPrefix(prefix).WithRecursive(recursive);
        }

        private IFileInfo notFound(string[] absolutePathSegments)
        {
            return new Common.FileInfo(this, false, -1, path(absolutePathSegments), fileName(path(absolutePathSegments)),
                DefaultDateTime, false, getRelativeSegments(absolutePathSegments));
        }

        private bool isRoot(string[] absolutePathSegments)
        {
            return getRelativeSegments(absolutePathSegments ?? new string[0]).Length == 0;
        }

        private void ensureNotRoot(string[] absolutePathSegments)
        {
            if (isRoot(absolutePathSegments))
                throw new InvalidOperationException(RootModificationMessage);
        }

        private string path(string[] absolutePathSegments)
        {
            return string.Join("/", absolutePathSegments);
        }

        private string dirPath(string[] absolutePathSegments)
        {
            return absolutePathSegments.Length == 0 ? "" : $"{path(absolutePathSegments)}/";
        }

        private string fileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var ret = path;
            if (ret.EndsWith("/")) ret = ret.Substring(0, ret.Length - 1);
            if (ret.Contains("/"))
                ret = ret.Substring(ret.LastIndexOf("/") + 1);
            return ret;
        }

        private async Task<bool> bucketMissingAsync()
        {
            try
            {
                return !await Client.BucketExistsAsync(new BucketExistsArgs().WithBucket(Options.BucketName));
            }
            catch
            {
                return false;
            }
        }

        // The bucket is created only by writes; reads and listings report a missing bucket as not found
        private async Task ensureBucketAsync()
        {
            if (bucketReady) return;
            if (!(await Client.BucketExistsAsync(new BucketExistsArgs().WithBucket(Options.BucketName))))
                await Client.MakeBucketAsync(new MakeBucketArgs().WithBucket(Options.BucketName));
            bucketReady = true;
        }
    }
}
