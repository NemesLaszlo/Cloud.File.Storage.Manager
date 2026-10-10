using Cloud.File.Storage.Manager.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Cloud.File.Storage.Manager.Physical
{
    /// <summary>
    /// Root containment is path based. Symbolic links and junctions inside the root are refused unless
    /// <see cref="PhysicalFileStorageOptions.AllowLinks"/> is set, in which case they are followed.
    /// <see cref="IFileInfo.PhysicalPath"/> and the <see cref="FileNotFoundException.FileName"/> of a missing file carry the full server path.
    /// </summary>
    public class PhysicalFileStorage : CloudFileStorageManager<PhysicalFileStorageOptions>
    {
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"
        };

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
            var full = fullPath(p);
            var root = fullPath(O.RootDirectorySegments);
            if (string.Equals(full, root, PathComparison))
                return full;
            var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootPrefix, PathComparison))
                throw new ArgumentException("The path resolves outside the provider root.");
            if (!O.AllowLinks)
                ensureNoLinks(rootPrefix, full);
            return full;
        }

        // Walks the existing components strictly below the root. Only symbolic links and junctions count;
        // other reparse points (deduplicated files, cloud placeholders) have no link target.
        private static void ensureNoLinks(string rootPrefix, string full)
        {
            var current = rootPrefix;
            foreach (var part in full.Substring(rootPrefix.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new System.IO.FileInfo(current);
                if (info.LinkTarget != null)
                    throw new ArgumentException("Links inside the provider root are not allowed.");
                if (!info.Exists)
                    return;
            }
        }

        private static string fullPath(string[] p)
        {
            var joined = string.Join(Path.DirectorySeparatorChar.ToString(), p).Replace("root:", "");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(joined + Path.DirectorySeparatorChar));
        }

        private bool isRoot(string p)
        {
            return string.Equals(p, fullPath(O.RootDirectorySegments), PathComparison);
        }

        private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>
        /// Rejects ':', names ending in '.' or ' ', and Windows reserved device names with or without an extension, on every platform.
        /// </summary>
        protected override bool isSegmentAllowed(string part)
        {
            if (part.IndexOf(':') >= 0 || part.EndsWith(".") || part.EndsWith(" "))
                return false;
            var dot = part.IndexOf('.');
            var stem = (dot < 0 ? part : part.Substring(0, dot)).TrimEnd(' ');
            return !ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
        }

        public override async Task OnLocalFileStreamDisposingAsync(LocalFileStream stream)
        {
            return;
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
            if (!System.IO.File.Exists(p))
                throw new FileNotFoundException("The file does not exist.", p);
            return new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
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
            var newPath = path(newSegments);
            var oldPath = path(oldSegments);
            Directory.CreateDirectory(Path.GetDirectoryName(newPath));
            System.IO.File.Move(oldPath, newPath);
            return Task.CompletedTask;
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            var p = path(absolutePathSegments);
            if (isRoot(p))
                throw new InvalidOperationException("The provider root cannot be deleted or modified.");
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
            if (readOnly)
            {
                if (!System.IO.File.Exists(p))
                    throw new FileNotFoundException("The file does not exist.", p);
                return new LocalFileStream(new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), this, true, absolutePathSegments);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            return new LocalFileStream(new FileStream(p, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None),
                this, false, absolutePathSegments);
        }

        public override bool SupportsDownloadUrl => false;

        protected override Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            throw new NotSupportedException("DownloadUrl is not supported in PhysicalFileStorage");
        }
    }
}
