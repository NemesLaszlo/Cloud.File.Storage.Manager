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

        public JObject ToJson()
        {
            var ret = JObject.FromObject(Options);
            ret["Type"] = this.GetType().Name;
            return ret;
        }


        private string[] getAbsoluteSegments(string[] pathSegments)
        {
            return (Options.RootDirectorySegments ?? new string[0]).Concat(pathSegments ?? new string[0]).ToArray();
        }

        protected abstract Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments);

        public async Task<IFileInfo> GetFileInfoAsync(params string[] pathSegments)
        {
            return await getFileInfoAsync(getAbsoluteSegments(pathSegments));
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
            return await readFileAsync(getAbsoluteSegments(pathSegments));
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


        protected abstract Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity);

        public string GetDownloadUrl(string[] pathSegments, TimeSpan validity) => GetDownloadUrlAsync(pathSegments, validity).Result;

        public string GetDownloadUrl(string subpath, TimeSpan validity) => GetDownloadUrlAsync(subpath, validity).Result;

        public string GetDownloadUrl(IFileInfo file, TimeSpan validity) => GetDownloadUrlAsync(file, validity).Result;

        public Task<string> GetDownloadUrlAsync(string[] pathSegments, TimeSpan validity)
            => getDownloadUrl(getAbsoluteSegments(pathSegments), validity);

        public Task<string> GetDownloadUrlAsync(string subpath, TimeSpan validity)
            => getDownloadUrl(normalizePath(subpath), validity);

        public Task<string> GetDownloadUrlAsync(IFileInfo file, TimeSpan validity)
            => getDownloadUrl(fileSegments(file), validity);



        protected abstract Task updateFileAsync(Stream contents, UpdateFileMode mode, params string[] absolutePathSegments);

        public async Task UpdateFileAsync(Stream contents, UpdateFileMode mode, params string[] pathSegments)
        {
            await updateFileAsync(contents, mode, getAbsoluteSegments(pathSegments));
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
            return await deleteAsync(getAbsoluteSegments(pathSegments));
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

        public virtual async Task<LocalFileStream> CreateLocalFileStreamAsync(bool readOnly, params string[] pathSegments)
        {
            return await createLocalFileStreamAsync(readOnly, getAbsoluteSegments(pathSegments));

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
            move(fileSegments(file), getAbsoluteSegments(newFileSegments));

        public Task MoveAsync(string[] oldFileSegments, string[] newFileSegments) =>
            move(getAbsoluteSegments(oldFileSegments), getAbsoluteSegments(newFileSegments));


        public virtual async Task OnLocalFileStreamDisposingAsync(LocalFileStream stream)
        {
            if (stream.ReadOnly) return;
            stream.Position = 0;
            await updateFileAsync(stream, UpdateFileMode.Overwrite, stream.TargetFilePathSegments);
        }

        protected string[] normalizePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return new string[0];
            var ret = p.Replace("\\", "/");
            if (ret.StartsWith("/")) ret = ret.Substring(1);
            return ret.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        }

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
