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
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);
        AmazonS3Client Client { get; }

        public AmazonS3FileStorageManager(AmazonS3FileStorageManagerOptions options) : base(options)
        {
            AWSCredentials credentials = Options.IsAnonymus ? (AWSCredentials)new AnonymousAWSCredentials() : new BasicAWSCredentials(Options.AccessKey, options.SecretKey);
            Client = new AmazonS3Client(credentials, RegionEndpoint.GetBySystemName(Options.BucketRegion));
        }

        private async Task<string> getLatestVersion(string[] absolutePathSegments)
        {
            ListVersionsResponse list;
            try
            {
                list = await Client.ListVersionsAsync(Options.BucketName, path(absolutePathSegments));

            }
            catch (Exception ex)
            {
                list = null;
            }
            return list.Versions?.Find(e => e.IsLatest)?.VersionId;
        }

        private string path(string[] absolutePathSegments)
        {
            return string.Join("/", absolutePathSegments);
        }

        private string dirPath(string[] absolutePathSegments)
        {
            return $"{path(absolutePathSegments)}/";
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

        private async Task<List<IFileInfo>> EnumerateObjects(string[] absolutePathSegments, bool deep)
        {
            var ret = new List<IFileInfo>();
            var dirnames = new List<string>();
            ListObjectsV2Response results;
            var dirPath = this.dirPath(absolutePathSegments);
            string continuationToken = null;
            do
            {
                results = await Client.ListObjectsV2Async(new ListObjectsV2Request()
                {
                    BucketName = Options.BucketName,
                    Prefix = dirPath,
                    ContinuationToken = continuationToken,
                    RequestPayer = RequestPayer.Requester
                });
                continuationToken = results.NextContinuationToken;

                foreach (var r in results.S3Objects)
                {
                    var relativePath = r.Key.Substring(dirPath.Length);
                    // lower level file/dir
                    if (relativePath.Contains("/"))
                    {
                        var dirName = relativePath.Substring(0, relativePath.IndexOf("/"));
                        if (!dirnames.Contains(dirName)) dirnames.Add(dirName);
                        if (!deep) continue;
                    }

                    ret.Add(new Common.FileInfo(this, true, r.Size, r.Key, fileName(r.Key), r.LastModified, false, getRelativeSegments(absolutePathSegments.Concat(new[] { fileName(r.Key) }).ToArray())));
                }
            } while (!string.IsNullOrEmpty(results.NextContinuationToken));
            foreach (var d in dirnames)
                if (!ret.Exists(e => e.PhysicalPath == d && e.IsDirectory))
                    ret.Add(new Common.FileInfo(this, true, -1, dirPath + d, d, DefaultDateTime, true, getRelativeSegments(absolutePathSegments.Concat(new[] { d }).ToArray())));

            return ret;
        }

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            try
            {
                // try to enumerate to see if dir
                var e = await Client.ListObjectsV2Async(new ListObjectsV2Request()
                {
                    BucketName = Options.BucketName,
                    RequestPayer = RequestPayer.Requester,
                    ContinuationToken = null,
                    Prefix = path(absolutePathSegments),
                    MaxKeys = 2
                });
                if (e.S3Objects.Count != 1) // path is directory, return not found
                    return new Common.FileInfo(this, false, -1, path(absolutePathSegments), fileName(path(absolutePathSegments)), DefaultDateTime, false, getRelativeSegments(absolutePathSegments));
                var latestVersion = await getLatestVersion(absolutePathSegments);
                GetObjectResponse file;
                if (!string.IsNullOrEmpty(latestVersion))
                    file = await Client.GetObjectAsync(Options.BucketName, path(absolutePathSegments), latestVersion);
                else file = await Client.GetObjectAsync(Options.BucketName, path(absolutePathSegments));
                return new Common.FileInfo(this, true, file.ContentLength, path(absolutePathSegments), fileName(path(absolutePathSegments)), file.LastModified, false, getRelativeSegments(absolutePathSegments));

            }
            catch (AmazonS3Exception e)
            {
                // file not found
                if (e.ErrorCode == "NoSuchKey")
                    return new Common.FileInfo(this, false, -1, path(absolutePathSegments), fileName(path(absolutePathSegments)), DefaultDateTime, false, getRelativeSegments(absolutePathSegments));
                throw;
            }
        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            var ret = await EnumerateObjects(absolutePathSegments, false);
            return new DirectoryContents(ret.Any(), ret.ToArray());
        }

        protected override Task<long> appendFileAsync(Stream contents, params string[] absolutePathSegments)
        {
            throw new NotSupportedException("appendFile is not supported in AmazonS3FileStorageManager");
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            var info = await getFileInfoAsync(absolutePathSegments);
            if (info.Exists)
                return (await Client.DeleteObjectAsync(Options.BucketName, path(absolutePathSegments))).HttpStatusCode ==
                    HttpStatusCode.OK;

            var listedObjects = await EnumerateObjects(absolutePathSegments, true);
            if (!listedObjects.Any()) return true;
            var ret = await Client.DeleteObjectsAsync(new DeleteObjectsRequest()
            {
                BucketName = Options.BucketName,
                RequestPayer = RequestPayer.Requester,
                Objects = listedObjects.Select(e => new KeyVersion() { Key = e.PhysicalPath }).ToList()
            });
            return !ret.DeleteErrors.Any() && ret.DeletedObjects.Any();
        }


        protected override Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            var request = new GetPreSignedUrlRequest()
            {
                BucketName = Options.BucketName,
                Key = path(absolutePathSegments),
                Expires = DateTime.UtcNow.Add(validity),
                Verb = HttpVerb.GET
            };
            request.ResponseHeaderOverrides.ContentType = "application/octet-stream";
            return Task.FromResult(Client.GetPreSignedURL(request));
        }

        public async Task<Stream> ReadFileAsync(string versionId, params string[] absolutePathSegments)
        {

            if (string.IsNullOrEmpty(versionId))
                return (await Client.GetObjectAsync(Options.BucketName, path(absolutePathSegments))).ResponseStream;
            else
                return (await Client.GetObjectAsync(Options.BucketName, path(absolutePathSegments), versionId)).ResponseStream;
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {

            return await ReadFileAsync(await getLatestVersion(absolutePathSegments), absolutePathSegments);

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
