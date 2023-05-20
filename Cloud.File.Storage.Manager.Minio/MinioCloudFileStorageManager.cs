using Cloud.File.Storage.Manager.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Minio;
using Minio.Exceptions;
using System.Reactive.Linq;

namespace Cloud.File.Storage.Manager.Minio
{
    public class MinioCloudFileStorageManager : CloudFileStorageManager<MinioCloudFileStorageManagerOptions>
    {
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);
        private MinioClient Client { get; }

        public MinioCloudFileStorageManager(MinioCloudFileStorageManagerOptions options) : base(options)
        {
            Client = new MinioClient(options.HostAndPort, options.AccessKey, options.SecretKey);
            if (Options.UseSSL)
                Client = Client.WithSSL();
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

        private async Task ensureBucketAsync()
        {
            if (!(await Client.BucketExistsAsync(Options.BucketName)))
                await Client.MakeBucketAsync(Options.BucketName);
        }

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            try
            {
                await ensureBucketAsync();
                // try to enumerate to see if dir
                var e = Client.ListObjectsAsync(Options.BucketName, path(absolutePathSegments));
                var items = await e.ToArray();
                if (items.Length == 0)
                    return new Common.FileInfo(this, false, -1, path(absolutePathSegments),
                        fileName(path(absolutePathSegments)), DefaultDateTime, false,
                        getRelativeSegments(absolutePathSegments));

                if (items.Length != 1 || items[0].Key.EndsWith("/")) // path is directory, return not found
                    return new Common.FileInfo(this, true, -1, path(absolutePathSegments),
                        fileName(path(absolutePathSegments)), DefaultDateTime, true,
                        getRelativeSegments(absolutePathSegments));

                return new Common.FileInfo(this, true, (long)items[0].Size, items[0].Key, fileName(items[0].Key),
                    items[0].LastModifiedDateTime ?? DefaultDateTime, false, getRelativeSegments(absolutePathSegments));

            }
            catch (ObjectNotFoundException)
            {
                return new Common.FileInfo(this, false, -1, path(absolutePathSegments),
                    fileName(path(absolutePathSegments)), DefaultDateTime, false,
                    getRelativeSegments(absolutePathSegments));
            }
        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            await ensureBucketAsync();
            var c = Client.ListObjectsAsync(Options.BucketName, path(absolutePathSegments), true);
            var items = await c.ToArray();
            if (items.Length == 0 || (items.Length == 1 && !items[0].Key.EndsWith("/")))
                return new DirectoryContents(false, new IFileInfo[0]);

            var res = items.Select(e => (IFileInfo)new Common.FileInfo(this, true, (long)e.Size, e.Key, fileName(e.Key),
                e.LastModifiedDateTime ?? DefaultDateTime, e.Key.EndsWith("/"),
                getRelativeSegments(absolutePathSegments.Concat(new[] { fileName(e.Key) }).ToArray()))).ToList();

            var allDirs = res.Where(e => !e.IsDirectory).Select(e => e.PhysicalPath).Select(e =>
            {
                var segments = normalizePath(e);
                return segments.Take(segments.Length - 1).ToArray();
            }).ToArray();
            foreach (var d in allDirs)
            {
                if (!res.Any(r => normalizePath(r.PhysicalPath).SequenceEqual(d)))
                    res.Add(new Common.FileInfo(this, true, -1, path(d), fileName(path(d)), DefaultDateTime, true, d));
            }
            return new DirectoryContents(true, res.ToArray());
        }

        protected override async Task<IChangeToken> watchAsync(string filter)
        {
            throw new NotSupportedException("Watch is not supported in MinioCloudFileStorageManager");
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {
            await ensureBucketAsync();
            Stream s = new MemoryStream();

            await Client.GetObjectAsync(Options.BucketName, path(absolutePathSegments), e => e.CopyTo(s));
            s.Position = 0;
            return s;
        }

        protected override async Task updateFileAsync(Stream contents, UpdateFileMode mode, params string[] absolutePathSegments)
        {
            await ensureBucketAsync();

            await using var tmp = new FileStream(Path.GetTempFileName(), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 4096, FileOptions.DeleteOnClose);
            if (mode == UpdateFileMode.Append)
            {
                var info = await getFileInfoAsync(absolutePathSegments);
                if (info.Exists)
                    await using (var s = await readFileAsync(absolutePathSegments))
                        await s.CopyToAsync(tmp);
            }
            await contents.CopyToAsync(tmp);
            await tmp.FlushAsync();
            tmp.Position = 0;
            await Client.PutObjectAsync(Options.BucketName, path(absolutePathSegments), tmp, tmp.Length);
        }

        protected override async Task move(string[] oldSegments, string[] newSegments)
        {
            await Client.CopyObjectAsync(Options.BucketName, path(oldSegments), Options.BucketName, path(newSegments));
            await deleteAsync(oldSegments);
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            await ensureBucketAsync();
            var info = await getFileInfoAsync(absolutePathSegments);
            if (info.Exists)
            {
                if (!info.IsDirectory)
                    await Client.RemoveObjectAsync(Options.BucketName, path(absolutePathSegments));
                else
                {
                    var files = await getDirectoryContentsAsync(absolutePathSegments);
                    if (files.Any(e => !e.IsDirectory))
                    {
                        var filePaths = files.Where(e => !e.IsDirectory).Select(e => e.PhysicalPath).ToArray();
                        await Client.RemoveObjectAsync(Options.BucketName, filePaths);
                    }
                }
                return true;
            }
            return false;
        }

        protected override Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            throw new NotSupportedException("DownloadUrl is not supported in MinioCloudFileStorageManager");
        }

    }
}
