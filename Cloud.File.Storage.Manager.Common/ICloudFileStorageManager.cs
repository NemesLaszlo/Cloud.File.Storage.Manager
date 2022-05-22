using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;

namespace Cloud.File.Storage.Manager.Common
{
    public interface ICloudFileStorageManager : IFileProvider
    {
        /// <summary>Locate a file at the given path.</summary>
        /// <param name="subpath">Relative path that identifies the file.</param>
        /// <returns>The file information. Caller must check Exists property.</returns>
        Task<IFileInfo> GetFileInfoAsync(string subpath);

        /// <summary>Locate a file at the given path.</summary>
        /// <param name="subpath">Relative path that identifies the file.</param>
        /// <returns>The file information. Caller must check Exists property.</returns>
        IFileInfo GetFileInfo(params string[] pathSegments);

        /// <summary>Locate a file at the given path.</summary>
        /// <param name="subpath">Relative path that identifies the file.</param>
        /// <returns>The file information. Caller must check Exists property.</returns>
        Task<IFileInfo> GetFileInfoAsync(params string[] pathSegments);

        /// <summary>Enumerate a directory at the given path, if any.</summary>
        /// <param name="subpath">Relative path that identifies the directory.</param>
        /// <returns>Returns the contents of the directory.</returns>
        Task<IDirectoryContents> GetDirectoryContentsAsync(string subpath);

        /// <summary>Enumerate a directory at the given path, if any.</summary>
        /// <param name="subpath">Relative path that identifies the directory.</param>
        /// <returns>Returns the contents of the directory.</returns>
        IDirectoryContents GetDirectoryContents(params string[] pathSegments);

        /// <summary>Enumerate a directory at the given path, if any.</summary>
        /// <param name="subpath">Relative path that identifies the directory.</param>
        /// <returns>Returns the contents of the directory.</returns>
        Task<IDirectoryContents> GetDirectoryContentsAsync(params string[] pathSegments);

        /// <summary>
        /// Creates a <see cref="T:Microsoft.Extensions.Primitives.IChangeToken" /> for the specified <paramref name="filter" />.
        /// </summary>
        /// <param name="filter">Filter string used to determine what files or folders to monitor. Example: **/*.cs, *.*, subFolder/**/*.cshtml.</param>
        /// <returns>An <see cref="T:Microsoft.Extensions.Primitives.IChangeToken" /> that is notified when a file matching <paramref name="filter" /> is added, modified or deleted.</returns>
        Task<IChangeToken> WatchAsync(string filter);

        /// <summary>
        /// Reads the contents of a specified file. This is a read-only stream and can be a network stream.
        /// Its recommended to write the contents to a temporary file to start using advanced stream operations
        /// </summary>
        /// <param name="subPath"></param>
        /// <returns></returns>
        Task<Stream> ReadFileAsync(string subpath);

        /// <summary>
        /// Reads the contents of a specified file. This a read-only stream and can be a network stream
        ///  Its recommended to write the contents to a temporary file to start using advanced stream operations
        /// </summary>
        /// <param name="subPath"></param>
        /// <returns></returns>
        Stream ReadFile(string subpath);

        /// <summary>
        /// Reads the contents of a specified file. This a read-only stream and can be a network stream
        ///  Its recommended to write the contents to a temporary file to start using advanced stream operations
        /// </summary>
        /// <param name="subPath"></param>
        /// <returns></returns>
        Task<Stream> ReadFileAsync(params string[] pathSegments);

        /// <summary>
        /// Reads the contents of a specified file. This a read-only stream and can be a network stream
        ///  Its recommended to write the contents to a temporary file to start using advanced stream operations
        /// </summary>
        /// <param name="subPath"></param>
        /// <returns></returns>
        Stream ReadFile(params string[] pathSegments);

        /// <summary>
        /// Reads the contents of a specified file. This a read-only stream and can be a network stream
        ///  Its recommended to write the contents to a temporary file to start using advanced stream operations
        /// </summary>
        /// <returns></returns>
        Task<Stream> ReadFileAsync(IFileInfo file);

        /// <summary>
        /// Reads the contents of a specified file. This a read-only stream and can be a network stream
        ///  Its recommended to write the contents to a temporary file to start using advanced stream operations
        /// </summary>
        /// <returns></returns>
        Stream ReadFile(IFileInfo file);

        /// <summary>
        /// Generate a presigned URL that you can give to others so that they can retrieve an object.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="validity"></param>
        /// <returns></returns>
        string GetDownloadUrl(string[] pathSegments, TimeSpan validity);

        /// <summary>
        /// Generate a presigned URL that you can give to others so that they can retrieve an object.
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="validity"></param>
        /// <returns></returns>
        string GetDownloadUrl(string subpath, TimeSpan validity);

        /// <summary>
        /// Generate a presigned URL that you can give to others so that they can retrieve an object.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="validity"></param>
        /// <returns></returns>
        string GetDownloadUrl(IFileInfo file, TimeSpan validity);

        /// <summary>
        /// Generate a presigned URL that you can give to others so that they can retrieve an object.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="validity"></param>
        /// <returns></returns>
        Task<string> GetDownloadUrlAsync(string[] pathSegments, TimeSpan validity);

        /// <summary>
        /// Generate a presigned URL that you can give to others so that they can retrieve an object.
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="validity"></param>
        /// <returns></returns>
        Task<string> GetDownloadUrlAsync(string subpath, TimeSpan validity);

        /// <summary>
        /// Generate a presigned URL that you can give to others so that they can retrieve an object.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="validity"></param>
        /// <returns></returns>
        Task<string> GetDownloadUrlAsync(IFileInfo file, TimeSpan validity);

        /// <summary>
        /// Updates a file with the specified contents
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        Task UpdateFileAsync(string subpath, Stream contents);

        /// <summary>
        /// Updates a file with the specified contents
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        void UpdateFile(string subpath, Stream contents);

        /// <summary>
        /// Updates a file with the specified contents
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        void UpdateFile(Stream contents, params string[] pathSegments);

        /// <summary>
        /// Updates a file with the specified contents
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        Task UpdateFileAsync(Stream contents, params string[] pathSegments);

        /// <summary>
        /// Updates a file with the specified contents
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        void UpdateFile(Stream contents, IFileInfo file);

        /// <summary>
        /// Updates a file with the specified contents
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        Task UpdateFileAsync(Stream contents, IFileInfo file);

        /// <summary>
        /// Upload the file in chunks - appends to an existing file upload with the specified contents
        /// If the file does not exist, it creates one
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        Task AppendFileAsync(string subpath, Stream contents);

        /// <summary>
        /// Upload the file in chunks - appends to an existing file upload with the specified contents
        /// If the file does not exist, it creates one
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        void AppendFile(string subpath, Stream contents);

        /// <summary>
        /// Upload the file in chunks - appends to an existing file upload with the specified contents
        /// If the file does not exist, it creates one
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        void AppendFile(Stream contents, params string[] pathSegments);

        /// <summary>
        /// Upload the file in chunks - appends to an existing file upload with the specified contents
        /// If the file does not exist, it creates one
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        Task AppendFileAsync(Stream contents, params string[] pathSegments);

        /// <summary>
        /// Upload the file in chunks - appends to an existing file upload with the specified contents
        /// If the file does not exist, it creates one
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        void AppendFile(Stream contents, IFileInfo file);

        /// <summary>
        /// Upload the file in chunks - appends to an existing file upload with the specified contents
        /// If the file does not exist, it creates one
        /// </summary>
        /// <param name="subpath"></param>
        /// <param name="contents"></param>
        /// <returns></returns>
        Task AppendFileAsync(Stream contents, IFileInfo file);

        /// <summary>
        /// Deletes a file or directory. The delete is recursive for directories
        /// </summary>
        /// <param name="subpath"></param>
        /// <returns></returns>
        bool Delete(string subpath);

        /// <summary>
        /// Deletes a file or directory. The delete is recursive for directories
        /// </summary>
        /// <param name="subpath"></param>
        /// <returns></returns>
        Task<bool> DeleteAsync(string subpath);

        /// <summary>
        /// Deletes a file or directory. The delete is recursive for directories
        /// </summary>
        /// <param name="subpath"></param>
        /// <returns></returns>
        bool Delete(params string[] pathSegments);

        /// <summary>
        /// Deletes a file or directory. The delete is recursive for directories
        /// </summary>
        /// <param name="subpath"></param>
        /// <returns></returns>
        Task<bool> DeleteAsync(params string[] pathSegments);

        /// <summary>
        /// Deletes a file or directory. The delete is recursive for directories
        /// </summary>
        /// <param name="subpath"></param>
        /// <returns></returns>
        bool Delete(IFileInfo fileOrDir);

        /// <summary>
        /// Deletes a file or directory. The delete is recursive for directories
        /// </summary>
        /// <param name="subpath"></param>
        /// <returns></returns>
        Task<bool> DeleteAsync(IFileInfo fileOrDir);

        /// <summary>
        /// Return file contents as a local stream that can be written to. This will ensure the file is placed on the local filesystem (at least temporarily). Caller should dispose stream when complete, only then will the file be committed.
        /// </summary>
        /// <param name="readOnly">True to open a read only stream (no commit is done to the provider after file is closed)</param>
        /// <param name="subpath">The path to the file</param>
        /// <returns>The file stream</returns>
        /// <exception cref="System.IO.FileNotFoundException"></exception>
        Task<LocalFileStream> CreateLocalFileStreamAsync(bool readOnly, string subpath);

        /// <summary>
        /// Return file contents as a local stream that can be written to. This will ensure the file is placed on the local filesystem (at least temporarily). Caller should dispose stream when complete, only then will the file be committed.
        /// </summary>
        /// <param name="subpath">The path to the file</param>
        ///  /// <param name="readOnly">True to open a read only stream (no commit is done to the provider after file is closed)</param>
        /// <returns>The file stream</returns>
        /// <exception cref="System.IO.FileNotFoundException"></exception>
        LocalFileStream CreateLocalFileStream(bool readOnly, string subpath);

        /// <summary>
        /// Return file contents as a local stream that can be written to. This will ensure the file is placed on the local filesystem (at least temporarily). Caller should dispose stream when complete, only then will the file be committed.
        /// </summary>
        /// <param name="subpath">The path to the file</param>
        ///  /// <param name="readOnly">True to open a read only stream (no commit is done to the provider after file is closed)</param>
        /// <returns>The file stream</returns>
        /// <exception cref="System.IO.FileNotFoundException"></exception>
        Task<LocalFileStream> CreateLocalFileStreamAsync(bool readOnly, params string[] pathSegments);

        /// <summary>
        /// Return file contents as a local stream that can be written to. This will ensure the file is placed on the local filesystem (at least temporarily). Caller should dispose stream when complete, only then will the file be committed.
        /// </summary>
        /// <param name="subpath">The path to the file</param>
        ///  /// <param name="readOnly">True to open a read only stream (no commit is done to the provider after file is closed)</param>
        /// <returns>The file stream</returns>
        /// <exception cref="System.IO.FileNotFoundException"></exception>
        LocalFileStream CreateLocalFileStream(bool readOnly, params string[] pathSegments);

        /// <summary>
        /// Return file contents as a local stream that can be written to. This will ensure the file is placed on the local filesystem (at least temporarily). Caller should dispose stream when complete, only then will the file be committed.
        /// </summary>
        /// <param name="subpath">The path to the file</param>
        ///  /// <param name="readOnly">True to open a read only stream (no commit is done to the provider after file is closed)</param>
        /// <returns>The file stream</returns>
        /// <exception cref="System.IO.FileNotFoundException"></exception>
        Task<LocalFileStream> CreateLocalFileStreamAsync(bool readOnly, IFileInfo file);

        /// <summary>
        /// Return file contents as a local stream that can be written to. This will ensure the file is placed on the local filesystem (at least temporarily). Caller should dispose stream when complete, only then will the file be committed.
        /// </summary>
        /// <param name="subpath">The path to the file</param>
        ///  /// <param name="readOnly">True to open a read only stream (no commit is done to the provider after file is closed)</param>
        /// <returns>The file stream</returns>
        /// <exception cref="System.IO.FileNotFoundException"></exception>
        LocalFileStream CreateLocalFileStream(bool readOnly, IFileInfo file);

        /// <summary>
        /// The callback called when a local file stream is closed. Used to commit the contents of a local stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        Task OnLocalFileStreamDisposingAsync(LocalFileStream stream);

        JObject ToJson();
    }
}
