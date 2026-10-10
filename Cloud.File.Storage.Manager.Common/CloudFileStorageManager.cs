using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;

namespace Cloud.File.Storage.Manager.Common
{
    public abstract class CloudFileStorageManager<T> : ICloudFileStorageManager where T : FileProviderOptions, new()
    {
        protected CloudFileStorageManager(T options)
        {
            Options = options;
        }
        public T Options { get; }

        /// <summary>
        /// Serializes the provider options. Warning: the output contains secrets (keys, passwords, tokens) and must never be logged.
        /// </summary>
        public JObject ToJson()
        {
            var ret = JObject.FromObject(Options);
            ret["Type"] = this.GetType().Name;
            return ret;
        }

        private const string RootModificationMessage = "The provider root cannot be deleted or modified.";
        private const string RootReadMessage = "The path must contain at least one segment.";
        private const int MaxDecodeRounds = 4;
        private static readonly char[] SegmentSeparators = { '/', '\\' };

        private string[] getAbsoluteSegments(string[] pathSegments)
        {
            var parts = validateSegments(pathSegments);
            foreach (var part in parts)
                if (!isSegmentAllowed(part) || !decodedForms(part).All(isSegmentAllowed))
                    throw invalidSegment(part);
            return (Options.RootDirectorySegments ?? new string[0]).Concat(parts).ToArray();
        }

        /// <summary>
        /// Splits the caller's segments on '/' and '\' and validates every part. A part is rejected when it is empty or whitespace,
        /// consists only of dots and spaces, contains a control character, or fails the same checks after any round of percent-decoding.
        /// A part whose percent-decoded form contains '/' or '\' is rejected.
        /// </summary>
        /// <param name="segments">The caller's path segments (without the root segments)</param>
        /// <returns>The validated parts</returns>
        /// <exception cref="ArgumentException">A part is not allowed</exception>
        protected static string[] validateSegments(IEnumerable<string> segments)
        {
            var ret = new List<string>();
            if (segments == null) return ret.ToArray();
            foreach (var segment in segments)
            {
                foreach (var part in (segment ?? "").Split(SegmentSeparators))
                {
                    if (!isValidPart(part))
                        throw invalidSegment(part);
                    var decoded = decodedForms(part);
                    if (decoded == null || !decoded.All(d => d.IndexOfAny(SegmentSeparators) < 0 && isValidPart(d)))
                        throw invalidSegment(part);
                    ret.Add(part);
                }
            }
            return ret.ToArray();
        }

        /// <summary>
        /// Provider specific segment rule, applied to every validated part and to each of its percent-decoded forms.
        /// </summary>
        /// <param name="part">A single path segment</param>
        /// <returns>False to reject the segment</returns>
        protected virtual bool isSegmentAllowed(string part)
        {
            return true;
        }

        /// <summary>
        /// Returns true when the absolute segments point to the provider root (no segments after the root segments)
        /// </summary>
        /// <param name="absolutePathSegments">The root segments followed by the relative segments</param>
        protected bool isRootPath(string[] absolutePathSegments)
        {
            return getRelativeSegments(absolutePathSegments ?? new string[0]).Length == 0;
        }

        // The percent-decoded forms of the part, one per round, or null when it still changes after MaxDecodeRounds
        private static List<string> decodedForms(string part)
        {
            var ret = new List<string>();
            var current = part;
            for (var i = 0; i < MaxDecodeRounds; i++)
            {
                var decoded = Uri.UnescapeDataString(current);
                if (decoded == current) return ret;
                ret.Add(decoded);
                current = decoded;
            }
            return Uri.UnescapeDataString(current) == current ? ret : null;
        }

        private static bool isValidPart(string part)
        {
            if (string.IsNullOrWhiteSpace(part)) return false;
            if (part.Trim(' ', '.').Length == 0) return false;
            return !part.Any(char.IsControl);
        }

        private static ArgumentException invalidSegment(string part)
        {
            var name = new string((part ?? "").Select(c => char.IsControl(c) ? '?' : c).ToArray());
            return new ArgumentException($"The path segment '{name}' is not allowed.", "pathSegments");
        }

        private string[] getModifiableSegments(string[] pathSegments)
        {
            var ret = getAbsoluteSegments(pathSegments);
            if (isRootPath(ret))
                throw new InvalidOperationException(RootModificationMessage);
            return ret;
        }

        private string[] getReadableSegments(string[] pathSegments)
        {
            var ret = getAbsoluteSegments(pathSegments);
            if (isRootPath(ret))
                throw new ArgumentException(RootReadMessage);
            return ret;
        }

        protected abstract Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments);

        public async Task<IFileInfo> GetFileInfoAsync(params string[] pathSegments)
        {
            var absolutePathSegments = getAbsoluteSegments(pathSegments);
            if (isRootPath(absolutePathSegments))
                return new FileInfo(this, false, -1, "", "", new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), false, new string[0]);
            return await getFileInfoAsync(absolutePathSegments);
        }

        public async Task<IFileInfo> GetFileInfoAsync(string subpath)
        {
            var normalizedPath = normalizePath(subpath);
            return await GetFileInfoAsync(normalizedPath);
        }

        public IFileInfo GetFileInfo(params string[] pathSegments)
        {
            return RunTaskSerial(GetFileInfoAsync(pathSegments));
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            var normalizedPath = normalizePath(subpath);
            return GetFileInfo(normalizedPath);
        }


        protected abstract Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments);

        public async Task<IDirectoryContents> GetDirectoryContentsAsync(params string[] pathSegments)
        {
            return await getDirectoryContentsAsync(getAbsoluteSegments(pathSegments));
        }

        public async Task<IDirectoryContents> GetDirectoryContentsAsync(string subpath)
        {
            var normalizedPath = normalizePath(subpath);
            return await GetDirectoryContentsAsync(normalizedPath);
        }

        public IDirectoryContents GetDirectoryContents(params string[] pathSegments)
        {
            return RunTaskSerial(GetDirectoryContentsAsync(pathSegments));
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            var normalizedPath = normalizePath(subpath);
            return GetDirectoryContents(normalizedPath);
        }


        protected abstract Task<IChangeToken> watchAsync(string filter);
        public IChangeToken Watch(string filter)
        {
            return RunTaskSerial(WatchAsync(filter));
        }

        public async Task<IChangeToken> WatchAsync(string filter)
        {
            return await watchAsync(filter);
        }


        protected abstract Task<Stream> readFileAsync(params string[] absolutePathSegments);

        public async Task<Stream> ReadFileAsync(params string[] pathSegments)
        {
            return await readFileAsync(getReadableSegments(pathSegments));
        }

        public async Task<Stream> ReadFileAsync(string subpath)
        {
            var normalizedPath = normalizePath(subpath);
            return await ReadFileAsync(normalizedPath);
        }
        public Stream ReadFile(string subpath)
        {
            var normalizedPath = normalizePath(subpath);
            return ReadFile(normalizedPath);
        }
        public Stream ReadFile(params string[] pathSegments)
        {
            return RunTaskSerial(ReadFileAsync(pathSegments));
        }

        public async Task<Stream> ReadFileAsync(IFileInfo file)
        {
            return await ReadFileAsync(fileSegments(file));
        }

        public Stream ReadFile(IFileInfo file)
        {
            return ReadFile(fileSegments(file));
        }


        public virtual bool SupportsDownloadUrl => true;

        protected abstract Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity);

        public string GetDownloadUrl(string[] pathSegments, TimeSpan validity) => GetDownloadUrlAsync(pathSegments, validity).Result;

        public string GetDownloadUrl(string subpath, TimeSpan validity) => GetDownloadUrlAsync(subpath, validity).Result;

        public string GetDownloadUrl(IFileInfo file, TimeSpan validity) => GetDownloadUrlAsync(file, validity).Result;

        public Task<string> GetDownloadUrlAsync(string[] pathSegments, TimeSpan validity)
            => getDownloadUrl(getReadableSegments(pathSegments), validity);

        public Task<string> GetDownloadUrlAsync(string subpath, TimeSpan validity)
            => GetDownloadUrlAsync(normalizePath(subpath), validity);

        public Task<string> GetDownloadUrlAsync(IFileInfo file, TimeSpan validity)
            => getDownloadUrl(getReadableSegments(fileSegments(file)), validity);



        protected abstract Task updateFileAsync(Stream contents, UpdateFileMode mode, params string[] absolutePathSegments);

        public async Task UpdateFileAsync(Stream contents, UpdateFileMode mode, params string[] pathSegments)
        {
            await updateFileAsync(contents, mode, getModifiableSegments(pathSegments));
        }

        public void UpdateFile(Stream contents, UpdateFileMode mode, IFileInfo file)
        {
            UpdateFile(contents, mode, fileSegments(file));
        }

        public async Task UpdateFileAsync(Stream contents, UpdateFileMode mode, IFileInfo file)
        {
            await UpdateFileAsync(contents, mode, fileSegments(file));
        }

        public async Task UpdateFileAsync(string subpath, UpdateFileMode mode, Stream contents)
        {
            var normalizedPath = normalizePath(subpath);
            await UpdateFileAsync(contents, mode, normalizedPath);
        }

        public void UpdateFile(string subpath, UpdateFileMode mode, Stream contents)
        {
            var normalizedPath = normalizePath(subpath);
            UpdateFile(contents, mode, normalizedPath);
        }

        public void UpdateFile(Stream contents, UpdateFileMode mode, params string[] pathSegments)
        {
            RunTaskSerial(UpdateFileAsync(contents, mode, pathSegments));
        }

        protected abstract Task<bool> deleteAsync(params string[] absolutePathSegments);

        public async Task<bool> DeleteAsync(params string[] pathSegments)
        {
            return await deleteAsync(getModifiableSegments(pathSegments));
        }

        public bool Delete(IFileInfo fileOrDir)
        {
            return Delete(fileSegments(fileOrDir));
        }

        public async Task<bool> DeleteAsync(IFileInfo fileOrDir)
        {
            return await DeleteAsync(fileSegments(fileOrDir));
        }

        public bool Delete(string subpath)
        {
            var normalizedPath = normalizePath(subpath);
            return Delete(normalizedPath);
        }

        public async Task<bool> DeleteAsync(string subpath)
        {
            var normalizedPath = normalizePath(subpath);
            return await DeleteAsync(normalizedPath);
        }

        public bool Delete(params string[] pathSegments)
        {
            return RunTaskSerial(DeleteAsync(pathSegments));
        }


        public LocalFileStream CreateLocalFileStream(bool readOnly, string subpath)
        {
            var normalizedPath = normalizePath(subpath);
            return CreateLocalFileStream(readOnly, normalizedPath);
        }
        public async Task<LocalFileStream> CreateLocalFileStreamAsync(bool readOnly, string subpath)
        {
            var normalizedPath = normalizePath(subpath);
            return await CreateLocalFileStreamAsync(readOnly, normalizedPath);
        }
        public LocalFileStream CreateLocalFileStream(bool readOnly, params string[] pathSegments)
        {
            return RunTaskSerial(CreateLocalFileStreamAsync(readOnly, pathSegments));
        }

        public async Task<LocalFileStream> CreateLocalFileStreamAsync(bool readOnly, IFileInfo file)
        {
            return await CreateLocalFileStreamAsync(readOnly, fileSegments(file));
        }

        public LocalFileStream CreateLocalFileStream(bool readOnly, IFileInfo file)
        {
            return CreateLocalFileStream(readOnly, fileSegments(file));
        }

        /// <summary>
        /// Returns the file contents as a local stream. A writeable stream uploads its contents when disposed,
        /// also when it is disposed after an exception, so partially written content is uploaded.
        /// </summary>
        public virtual async Task<LocalFileStream> CreateLocalFileStreamAsync(bool readOnly, params string[] pathSegments)
        {
            return await createLocalFileStreamAsync(readOnly, readOnly ? getReadableSegments(pathSegments) : getModifiableSegments(pathSegments));

        }

        protected virtual async Task<LocalFileStream> createLocalFileStreamAsync(bool readOnly, params string[] absolutePathSegments)
        {
            var tempFileName = Path.GetTempFileName();
            var tempStream = new FileStream(tempFileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
            try
            {
                var f = await getFileInfoAsync(absolutePathSegments);
                if (f.Exists)
                    using (var fs = await readFileAsync(absolutePathSegments))
                        fs.CopyTo(tempStream);
                tempStream.Position = 0;
                return new LocalFileStream(tempStream, this, readOnly, absolutePathSegments);
            }
            catch
            {
                tempStream?.Dispose();
                throw;
            }

        }

        protected abstract Task move(string[] oldSegments, string[] newSegments);

        public void Move(string[] oldFileSegments, string[] newFileSegments)
            => MoveAsync(oldFileSegments, newFileSegments).Wait();

        public void Move(string oldPath, string newPath) =>
            MoveAsync(oldPath, newPath).Wait();

        public void Move(IFileInfo file, string[] newFileSegments) =>
            MoveAsync(file, newFileSegments).Wait();

        public Task MoveAsync(string oldPath, string newPath) =>
            MoveAsync(normalizePath(oldPath), normalizePath(newPath));

        public Task MoveAsync(IFileInfo file, string[] newFileSegments) =>
            moveSegments(getModifiableSegments(fileSegments(file)), getModifiableSegments(newFileSegments));

        public Task MoveAsync(string[] oldFileSegments, string[] newFileSegments) =>
            moveSegments(getModifiableSegments(oldFileSegments), getModifiableSegments(newFileSegments));

        private Task moveSegments(string[] oldSegments, string[] newSegments) =>
            oldSegments.SequenceEqual(newSegments, StringComparer.Ordinal) ? Task.CompletedTask : move(oldSegments, newSegments);


        /// <summary>
        /// Uploads a writeable local stream to its target. The stream must have been created by this provider instance, and its target
        /// must be a non-root path under the provider root that passes the same validation as <see cref="UpdateFileAsync(Stream, UpdateFileMode, string[])"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The stream belongs to another provider, or the target is the provider root</exception>
        /// <exception cref="ArgumentException">The target is outside the provider root or contains a segment that is not allowed</exception>
        public virtual async Task OnLocalFileStreamDisposingAsync(LocalFileStream stream)
        {
            if (stream.ReadOnly) return;
            if (!ReferenceEquals(stream.Provider, this))
                throw new InvalidOperationException("The local file stream belongs to another provider.");
            var target = stream.TargetFilePathSegments ?? new string[0];
            var root = Options.RootDirectorySegments ?? new string[0];
            if (target.Length < root.Length || !target.Take(root.Length).SequenceEqual(root, StringComparer.Ordinal))
                throw new ArgumentException("The local file stream target is outside the provider root.");
            var absolutePathSegments = getModifiableSegments(getRelativeSegments(target));
            stream.Position = 0;
            await updateFileAsync(stream, UpdateFileMode.Overwrite, absolutePathSegments);
        }

        protected string[] normalizePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return new string[0];
            var ret = p.Replace("\\", "/");
            if (ret.StartsWith("/")) ret = ret.Substring(1);
            return ret.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        }

        /// <summary>
        /// Turns escaped newline sequences (a key pasted into an environment variable or a secret) into real newlines.
        /// A PEM body never contains a backslash, so a valid key is returned unchanged.
        /// </summary>
        protected static string normalizePem(string pem) =>
            string.IsNullOrEmpty(pem) || pem.IndexOf('\\') < 0
                ? pem
                : pem.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");

        protected string[] fileSegments(IFileInfo f)
        {
            if (f is FileInfo) return ((FileInfo)f).PathSegments;
            return normalizePath(f.PhysicalPath);
        }

        protected string[] getRelativeSegments(string[] segments)
        {
            return segments.Skip(Options.RootDirectorySegments?.Length ?? 0).ToArray();
        }

        protected T RunTaskSerial<T>(Task<T> t)
        {
            try
            {
                return t.Result;
            }
            catch (AggregateException e)
            {
                throw e.InnerExceptions.FirstOrDefault() ?? e;
            }
        }

        protected void RunTaskSerial(Task t)
        {
            try
            {
                t.Wait();
            }
            catch (AggregateException e)
            {
                throw e.InnerExceptions.FirstOrDefault() ?? e;
            }
        }
    }
}
