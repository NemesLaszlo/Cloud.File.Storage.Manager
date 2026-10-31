using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Cloud.File.Storage.Manager.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System.Net;

namespace Cloud.File.Storage.Manager.AmazonS3
{
    public class AmazonS3FileStorageManager : CloudFileStorageManager<AmazonS3FileStorageManagerOptions>
    {
        private const int MaxDeleteBatchSize = 1000;
        private const string RootReadMessage = "The path must contain at least one segment.";
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);
        AmazonS3Client Client { get; }

        public AmazonS3FileStorageManager(AmazonS3FileStorageManagerOptions options) : base(options)
        {
            AWSCredentials credentials = Options.IsAnonymus ? (AWSCredentials)new AnonymousAWSCredentials() : new BasicAWSCredentials(Options.AccessKey, Options.SecretKey);
            Client = new AmazonS3Client(credentials, new AmazonS3Config()
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(Options.BucketRegion),
                // SDK v4 would otherwise add CRC32 checksums (aws-chunked uploads) that S3 compatible servers may reject
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });
        }

        private async Task<S3Object> firstObject(string prefix)
        {
            var response = await Client.ListObjectsV2Async(new ListObjectsV2Request()
            {
                BucketName = Options.BucketName,
                RequestPayer = RequestPayer.Requester,
                Prefix = prefix,
                MaxKeys = 1
            });
            return response.S3Objects?.FirstOrDefault();
        }

        // Absolute segments given by a caller: they must start with the root segments, the rest is validated
        private string[] underRoot(string[] absolutePathSegments)
        {
            var root = Options.RootDirectorySegments ?? new string[0];
            var segments = absolutePathSegments ?? new string[0];
            if (segments.Length < root.Length || !segments.Take(root.Length).SequenceEqual(root, StringComparer.Ordinal))
                throw new ArgumentException("The path must start with the provider root directory segments.", nameof(absolutePathSegments));
            var parts = validateSegments(segments.Skip(root.Length));
            foreach (var part in parts)
                if (!isSegmentAllowed(part))
                    throw new ArgumentException($"The path segment '{part}' is not allowed.", "pathSegments");
            return root.Concat(parts).ToArray();
        }

        private string path(string[] absolutePathSegments)
        {
            return string.Join("/", absolutePathSegments);
        }

        private string dirPath(string[] absolutePathSegments)
        {
            var p = path(absolutePathSegments);
            return p.Length == 0 ? p : $"{p}/";
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

        /// <summary>
        /// Lists the objects under a folder. The segments are absolute: they must start with the root directory segments,
        /// and the provider root itself may be listed.
        /// </summary>
        /// <exception cref="ArgumentException">The segments are outside the provider root or contain a segment that is not allowed</exception>
        public async Task<List<IFileInfo>> EnumerateObjects(string[] absolutePathSegments, bool deep)
        {
            return await enumerateObjects(underRoot(absolutePathSegments), deep);
        }

        private async Task<List<IFileInfo>> enumerateObjects(string[] absolutePathSegments, bool deep)
        {
            var ret = new List<IFileInfo>();
            var dirnames = new List<string>();
            var seenDirnames = new HashSet<string>();
            ListObjectsV2Response results;
            var dirPath = this.dirPath(absolutePathSegments);
            string continuationToken = null;
            do
            {
                results = await Client.ListObjectsV2Async(new ListObjectsV2Request()
                {
                    BucketName = Options.BucketName,
                    Prefix = dirPath,
                    Delimiter = deep ? null : "/",
                    ContinuationToken = continuationToken,
                    RequestPayer = RequestPayer.Requester
                });
                continuationToken = results.NextContinuationToken;

                foreach (var r in results.S3Objects ?? [])
                {
                    var relativeParts = r.Key.Substring(dirPath.Length).Split('/');
                    if (relativeParts.Length > 1 && relativeParts[0].Length > 0 && seenDirnames.Add(relativeParts[0]))
                        dirnames.Add(relativeParts[0]);
                    // skip folder markers and keys containing empty segments
                    if (relativeParts.Any(string.IsNullOrEmpty)) continue;

                    ret.Add(new Common.FileInfo(this, true, r.Size ?? 0, r.Key, fileName(r.Key), r.LastModified ?? DefaultDateTime, false, getRelativeSegments(absolutePathSegments.Concat(relativeParts).ToArray())));
                }

                foreach (var p in results.CommonPrefixes ?? [])
                {
                    var d = p.Substring(dirPath.Length, p.Length - dirPath.Length - 1);
                    if (d.Length > 0 && seenDirnames.Add(d))
                        dirnames.Add(d);
                }
            } while (!string.IsNullOrEmpty(continuationToken));
            foreach (var d in dirnames)
                ret.Add(new Common.FileInfo(this, true, -1, dirPath + d, d, DefaultDateTime, true, getRelativeSegments(absolutePathSegments.Concat(new[] { d }).ToArray())));

            return ret;
        }

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            var key = path(absolutePathSegments);
            var relativeSegments = getRelativeSegments(absolutePathSegments);
            if (string.IsNullOrEmpty(key))
                return new Common.FileInfo(this, false, -1, key, fileName(key), DefaultDateTime, false, relativeSegments);

            // an exact key is always listed first under its own prefix
            var first = await firstObject(key);
            if (first != null && first.Key == key)
                return new Common.FileInfo(this, true, first.Size ?? 0, key, fileName(key), first.LastModified ?? DefaultDateTime, false, relativeSegments);

            var isDirectory = first != null
                              && (first.Key.StartsWith(key + "/", StringComparison.Ordinal) || await firstObject(key + "/") != null);
            return new Common.FileInfo(this, false, -1, key, fileName(key), DefaultDateTime, isDirectory, relativeSegments);
        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            var ret = await enumerateObjects(absolutePathSegments, false);
            return new DirectoryContents(ret.Any(), ret.ToArray());
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            if (getRelativeSegments(absolutePathSegments).Length == 0)
                throw new InvalidOperationException("The provider root cannot be deleted or modified.");

            var key = path(absolutePathSegments);
            var first = await firstObject(key);
            if (first == null) return false;
            if (first.Key == key)
            {
                var status = (await Client.DeleteObjectAsync(Options.BucketName, key)).HttpStatusCode;
                return status == HttpStatusCode.OK || status == HttpStatusCode.NoContent;
            }

            var deleted = false;
            var prefix = dirPath(absolutePathSegments);
            string continuationToken = null;
            do
            {
                var page = await Client.ListObjectsV2Async(new ListObjectsV2Request()
                {
                    BucketName = Options.BucketName,
                    Prefix = prefix,
                    MaxKeys = MaxDeleteBatchSize,
                    ContinuationToken = continuationToken,
                    RequestPayer = RequestPayer.Requester
                });
                continuationToken = page.NextContinuationToken;
                if (page.S3Objects is not { Count: > 0 } objects) continue;

                List<DeleteError> errors;
                try
                {
                    var ret = await Client.DeleteObjectsAsync(new DeleteObjectsRequest()
                    {
                        BucketName = Options.BucketName,
                        RequestPayer = RequestPayer.Requester,
                        Quiet = true,
                        Objects = objects.Select(e => new KeyVersion() { Key = e.Key }).ToList()
                    });
                    errors = ret.DeleteErrors;
                }
                catch (DeleteObjectsException ex) when (ex.Response?.DeleteErrors is { Count: > 0 })
                {
                    errors = ex.Response.DeleteErrors;
                }
                if (errors is { Count: > 0 })
                    throw new IOException(
                        $"Failed to delete {errors.Count} object(s) under '{prefix}'. First error: '{errors[0].Key}' {errors[0].Code} {errors[0].Message}");
                deleted = true;
            } while (!string.IsNullOrEmpty(continuationToken));
            return deleted;
        }

        private bool usesHttp()
        {
            var url = Client.Config.ServiceURL;
            return string.IsNullOrEmpty(url) ? Client.Config.UseHttp : url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        protected override Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            var request = new GetPreSignedUrlRequest()
            {
                BucketName = Options.BucketName,
                Key = path(absolutePathSegments),
                Expires = DateTime.UtcNow.Add(validity),
                Verb = HttpVerb.GET,
                // SDK v4 signs https unless told otherwise, whatever the endpoint's scheme
                Protocol = usesHttp() ? Protocol.HTTP : Protocol.HTTPS
            };
            request.ResponseHeaderOverrides.ContentType = "application/octet-stream";
            return Task.FromResult(Client.GetPreSignedURL(request));
        }

        /// <summary>
        /// Reads a version of a file. The segments are absolute: they must start with the root directory segments.
        /// On this type a call like ReadFileAsync("dir", "file.txt") binds here, with "dir" as the version id.
        /// </summary>
        /// <exception cref="ArgumentException">The segments are outside the provider root, point to the root, or contain a segment that is not allowed</exception>
        [Obsolete("Use ReadFileVersionAsync with relative segments.")]
        public async Task<Stream> ReadFileAsync(string versionId, params string[] absolutePathSegments)
        {
            return await readVersionAsync(versionId, absolutePathSegments);
        }

        /// <summary>
        /// Reads a version of a file; a null or empty <paramref name="versionId"/> reads the current version.
        /// </summary>
        /// <param name="versionId">The object version id</param>
        /// <param name="pathSegments">The path segments, relative to the root directory</param>
        /// <exception cref="ArgumentException">The path points to the root or contains a segment that is not allowed</exception>
        public async Task<Stream> ReadFileVersionAsync(string versionId, params string[] pathSegments)
        {
            return await readVersionAsync(versionId, (Options.RootDirectorySegments ?? new string[0]).Concat(pathSegments ?? new string[0]).ToArray());
        }

        private async Task<Stream> readVersionAsync(string versionId, string[] absolutePathSegments)
        {
            var segments = underRoot(absolutePathSegments);
            if (isRootPath(segments))
                throw new ArgumentException(RootReadMessage);
            return await getObjectAsync(versionId, segments);
        }

        private async Task<Stream> getObjectAsync(string versionId, string[] absolutePathSegments)
        {
            if (string.IsNullOrEmpty(versionId))
                return (await Client.GetObjectAsync(Options.BucketName, path(absolutePathSegments))).ResponseStream;
            else
                return (await Client.GetObjectAsync(Options.BucketName, path(absolutePathSegments), versionId)).ResponseStream;
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {
            return await getObjectAsync(null, absolutePathSegments);
        }

        protected override async Task updateFileAsync(Stream contents, UpdateFileMode mode, params string[] absolutePathSegments)
        {

            if (mode == UpdateFileMode.Append)
            {
                await using var str = await createLocalFileStreamAsync(false, absolutePathSegments);
                str.Position = str.Length;
                await using (contents)
                    await contents.CopyToAsync(str);
            }
            else
                await using (contents)
                    await Client.PutObjectAsync(new PutObjectRequest()
                    {
                        InputStream = contents,
                        BucketName = Options.BucketName,
                        AutoCloseStream = true,
                        AutoResetStreamPosition = false,
                        Key = path(absolutePathSegments),
                        RequestPayer = RequestPayer.Requester,
                        StorageClass = Options.StorageClassForNewFiles
                    });

        }

        protected override Task<IChangeToken> watchAsync(string filter)
        {
            throw new NotSupportedException("Watch is not supported in AmazonS3FileStorageManager");
        }

        /// <summary>
        /// Copies the object to the new key with a single CopyObject request, then deletes the original.
        /// S3 limits a single copy to objects of at most 5 GB, so larger objects cannot be moved.
        /// </summary>
        protected override async Task move(string[] oldSegments, string[] newSegments)
        {
            var copyObjectRequest = new CopyObjectRequest
            {
                SourceBucket = Options.BucketName,
                DestinationBucket = Options.BucketName,
                SourceKey = path(oldSegments),
                DestinationKey = path(newSegments)
            };

            await Client.CopyObjectAsync(copyObjectRequest);

            await deleteAsync(oldSegments);
        }
    }
}
