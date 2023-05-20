using Cloud.File.Storage.Manager.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Cloud.File.Storage.Manager.Physical
{
    public class PhysicalFileStorage : CloudFileStorageManager<PhysicalFileStorageOptions>
    {
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);

        private readonly PhysicalFileProvider _provider;
        private PhysicalFileStorageOptions O { get; }

        public PhysicalFileStorage(string rootPath) : this(new PhysicalFileStorageOptions() { RootDirectoryPath = rootPath })
        {

        }

        public PhysicalFileStorage(PhysicalFileStorageOptions options) : base(options)
        {
            O = options;
            if (string.IsNullOrEmpty(options.RootDirectoryPath))
                throw new ArgumentException("RootPath cannot be empty", nameof(options.RootDirectoryPath));

            var rootPrefix = "root:";
            if (options.RootDirectoryPath.StartsWith(rootPrefix) || !Path.IsPathRooted(options.RootDirectoryPath))
                options.RootDirectoryPath = Path.GetFullPath(options.RootDirectoryPath.Replace(rootPrefix, ""));
        }

        internal static string[] getPathSegments(string p)
        {
            var ret = new List<string>();
            var val = p.Replace("/", @"\");
            if (p.StartsWith(@"\\"))
                ret.Add("\\");
            else if (p.StartsWith(@"/"))
                ret.Add(@"root:");
            ret.AddRange(val.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries));
            return ret.ToArray();
        }

        private string path(string[] p)
        {
            return string.Join(Path.DirectorySeparatorChar.ToString(), p).Replace("root:", "");
        }

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            var f = new System.IO.FileInfo(path(absolutePathSegments));
            return new Common.FileInfo(this, f.Exists, f.Exists ? f.Length : -1, f.FullName, 
                f.Name, f.Exists ? f.LastWriteTimeUtc : DefaultDateTime, false, getRelativeSegments(absolutePathSegments));
        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            var dir = new DirectoryInfo(path(absolutePathSegments));
            if (!dir.Exists)
                return new NotFoundDirectoryContents();
            var ret = new List<IFileInfo>();
            foreach (var d in dir.GetDirectories())
                ret.Add(new Common.FileInfo(this, true, -1, d.FullName, d.Name, d.LastWriteTimeUtc, true, getRelativeSegments(absolutePathSegments.Concat(new[] { d.Name }).ToArray())));
            foreach (var f in dir.GetFiles())
                ret.Add(new Common.FileInfo(this, true, f.Length, f.FullName, f.Name, f.LastWriteTimeUtc, false, getRelativeSegments(absolutePathSegments.Concat(new[] { f.Name }).ToArray())));
            return new DirectoryContents(true, ret.ToArray());

        }

        protected override async Task<IChangeToken> watchAsync(string filter)
        {
            throw new NotSupportedException("Watch is not supported in PhysicalFileStorage");
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {
            string p = path(absolutePathSegments);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            return new FileStream(p, FileMode.OpenOrCreate, FileAccess.Read);
        }

        protected override async Task updateFileAsync(Stream contents, UpdateFileMode mode, params string[] absolutePathSegments)
        {
            string p = path(absolutePathSegments);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            await using var fs = new FileStream(p, mode == UpdateFileMode.Overwrite ? FileMode.Create : FileMode.Append, FileAccess.Write);
            await contents.CopyToAsync(fs);
        }

        protected override Task move(string[] oldSegments, string[] newSegments)
        {
            System.IO.File.Move(path(oldSegments), path(newSegments));
            return Task.CompletedTask;
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            var p = path(absolutePathSegments);
            var ret = false;
            if (System.IO.File.Exists(p))
            {
                System.IO.File.Delete(p);
                ret = true;
            }
            else if (Directory.Exists(p))
            {
                Directory.Delete(p, true);
                ret = true;
            }
            return ret;
        }

        protected override async Task<LocalFileStream> createLocalFileStreamAsync(bool readOnly, params string[] absolutePathSegments)
        {
            var p = path(absolutePathSegments);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            return new LocalFileStream(new FileStream(p, FileMode.OpenOrCreate, readOnly ? FileAccess.Read : FileAccess.ReadWrite, readOnly ? FileShare.ReadWrite : FileShare.None),
                this, readOnly, absolutePathSegments);
        }

        protected override Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            throw new NotSupportedException("DownloadUrl is not supported in PhysicalFileStorage");
        }
    }
}
