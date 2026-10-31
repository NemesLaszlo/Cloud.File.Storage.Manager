using Box.Sdk.Gen;
using Box.Sdk.Gen.Managers;
using Box.Sdk.Gen.Schemas;
using Cloud.File.Storage.Manager.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Task = System.Threading.Tasks.Task;
using FileInfo = Cloud.File.Storage.Manager.Common.FileInfo;

namespace Cloud.File.Storage.Manager.Box
{
    /// <summary>
    /// A cloud file storage manager over a Box folder, on the Box .NET SDK (Box.Sdk.Gen, shipped as Box.V2.Core 10+).
    /// The app signs in with its JWT credentials as its service account; the SDK renews the token when it expires.
    /// </summary>
    public class BoxCloudFileStorageManager : CloudFileStorageManager<BoxCloudFileStorageManagerOptions>
    {
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);
        private readonly BoxClient Client;
        private readonly string RootFolderId;

        private const long CHUNKED_UPLOAD_THRESHOLD = 20_000_000; // 20MB minimum for Box chunked uploads
        private const int PageSize = 1000;
        private static readonly TimeSpan NotReadyDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan NotReadyTimeout = TimeSpan.FromMinutes(2);

        private static readonly IReadOnlyList<string> ItemFields = new[] { "type", "id", "name", "size", "modified_at", "content_modified_at" };
        private static readonly IReadOnlyList<string> FileFields = new[] { "size", "modified_at", "content_modified_at" };
        private static readonly IReadOnlyList<string> FolderFields = new[] { "modified_at", "content_modified_at" };

        public BoxCloudFileStorageManager(BoxCloudFileStorageManagerOptions options) : base(options)
        {
            if (string.IsNullOrEmpty(options.ClientId)) throw new ArgumentException("ClientId is required");
            if (string.IsNullOrEmpty(options.ClientSecret)) throw new ArgumentException("ClientSecret is required");
            if (string.IsNullOrEmpty(options.EnterpriseId)) throw new ArgumentException("EnterpriseId is required");
            if (string.IsNullOrEmpty(options.PublicKeyId)) throw new ArgumentException("PublicKeyId is required");
            if (string.IsNullOrEmpty(options.PrivateKey)) throw new ArgumentException("PrivateKey is required");

            var config = new JwtConfig(
                clientId: options.ClientId,
                clientSecret: options.ClientSecret,
                jwtKeyId: options.PublicKeyId,
                privateKey: normalizePrivateKey(options.PrivateKey),
                privateKeyPassphrase: options.Passphrase ?? "")
            {
                EnterpriseId = options.EnterpriseId
            };

            RootFolderId = string.IsNullOrEmpty(options.RootFolderId) ? "0" : options.RootFolderId;
            var auth = new BoxJwtAuth(config);
            // Sign in now, as before: wrong credentials fail here instead of on the first call
            auth.RetrieveTokenAsync().GetAwaiter().GetResult();
            Client = new BoxClient(auth);
        }

        internal static string normalizePrivateKey(string pem) => normalizePem(pem);

        // A file or folder found in a listing
        private sealed record Entry(string Id, bool IsFolder, string Name, long? Size, DateTimeOffset? ModifiedAt, DateTimeOffset? ContentModifiedAt)
        {
            public static Entry Root(string id) => new Entry(id, true, "", null, null, null);

            public static Entry From(Item item) => item switch
            {
                { FileFull: { } file } => new Entry(file.Id, false, file.Name ?? "", file.Size, file.ModifiedAt, file.ContentModifiedAt),
                { FolderMini: { } folder } => new Entry(folder.Id, true, folder.Name ?? "", null, null, null),
                _ => null
            };
        }

        private async Task<Items> listPage(string folderId, string marker)
        {
            return await Client.Folders.GetFolderItemsAsync(folderId, new GetFolderItemsQueryParams
            {
                Fields = ItemFields,
                Usemarker = true,
                Marker = marker,
                Limit = PageSize
            });
        }

        private async Task<Entry> findChild(string parentId, string name)
        {
            string marker = null;
            try
            {
                do
                {
                    var page = await listPage(parentId, marker);
                    foreach (var item in page.Entries ?? [])
                    {
                        var entry = Entry.From(item);
                        if (entry != null && entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return entry;
                    }
                    marker = page.NextMarker;
                } while (!string.IsNullOrEmpty(marker));
                return null;
            }
            catch (BoxSdkException ex)
            {
                throw new IOException($"Box API error while resolving path segment '{name}': {ex.Message}", ex);
            }
        }

        // Zero segments resolve to the provider root folder.
        private async Task<(Entry item, bool isFolder)> resolvePath(string[] pathSegments, bool expectFolder = false)
        {
            var current = Entry.Root(RootFolderId);

            foreach (var segment in pathSegments)
            {
                if (!current.IsFolder)
                    return (null, false);
                current = await findChild(current.Id, segment);
                if (current == null)
                    return (null, false);
            }
            if (expectFolder && !current.IsFolder)
                return (null, false);
            return (current, current.IsFolder);
        }

        private bool isProviderRoot(string[] absolutePathSegments, Entry item = null)
        {
            return absolutePathSegments.Length <= Options.RootDirectorySegments.Length || item?.Id == RootFolderId;
        }

        private static void refuseProviderRoot()
        {
            throw new InvalidOperationException("The provider root cannot be deleted or modified.");
        }

        private DateTimeOffset lastModified(Entry item)
        {
            return item.ModifiedAt ?? item.ContentModifiedAt ?? DefaultDateTime;
        }

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            try
            {
                var (item, isFolder) = await resolvePath(absolutePathSegments);
                var name = absolutePathSegments.LastOrDefault() ?? "";
                if (item == null)
                    return new FileInfo(this, false, 0, string.Join("/", absolutePathSegments), name,
                        DefaultDateTime, false, getRelativeSegments(absolutePathSegments));

                long fileSize = -1;
                var modifiedAt = lastModified(item);
                if (!isFolder)
                {
                    fileSize = item.Size ?? 0;
                    if (item.Size == null || item.ModifiedAt == null)
                    {
                        try
                        {
                            var completeFileInfo = await Client.Files.GetFileByIdAsync(item.Id, new GetFileByIdQueryParams { Fields = FileFields });
                            fileSize = completeFileInfo.Size ?? fileSize;
                            modifiedAt = completeFileInfo.ModifiedAt ?? completeFileInfo.ContentModifiedAt ?? modifiedAt;
                        }
                        catch
                        {
                            // Keep the values from the folder listing
                        }
                    }
                }
                else if (item.Id != RootFolderId)
                {
                    // Folder listings carry no timestamps: ask the folder itself
                    try
                    {
                        var folder = await Client.Folders.GetFolderByIdAsync(item.Id, new GetFolderByIdQueryParams { Fields = FolderFields });
                        modifiedAt = folder.ModifiedAt ?? folder.ContentModifiedAt ?? modifiedAt;
                    }
                    catch
                    {
                        // Keep the default
                    }
                }
                return new FileInfo(this,
                    !isFolder,
                    isFolder ? -1 : fileSize,
                    string.Join("/", absolutePathSegments),
                    name,
                    modifiedAt,
                    isFolder,
                    getRelativeSegments(absolutePathSegments));
            }
            catch (BoxSdkException ex)
            {
                throw new IOException($"Box API error retrieving file info for {string.Join("/", absolutePathSegments)}: {ex.Message}", ex);
            }
        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            try
            {
                var (folder, isFolder) = await resolvePath(absolutePathSegments, expectFolder: true);
                if (folder == null || !isFolder)
                    return new DirectoryContents(false, Array.Empty<IFileInfo>());

                var results = new List<IFileInfo>();
                string marker = null;
                do
                {
                    var page = await listPage(folder.Id, marker);
                    foreach (var item in page.Entries ?? [])
                    {
                        var entry = Entry.From(item);
                        if (entry == null) continue; // web links are neither files nor folders
                        results.Add(new FileInfo(this,
                            true,
                            entry.IsFolder ? -1 : entry.Size ?? 0,
                            entry.Name,
                            entry.Name,
                            lastModified(entry),
                            entry.IsFolder,
                            getRelativeSegments(absolutePathSegments.Concat(new[] { entry.Name }).ToArray())));
                    }
                    marker = page.NextMarker;
                } while (!string.IsNullOrEmpty(marker));
                return new DirectoryContents(true, results.ToArray());
            }
            catch (BoxSdkException ex)
            {
                throw new IOException($"Box API error reading directory contents: {string.Join("/", absolutePathSegments)}: {ex.Message}", ex);
            }
        }

        // Box answers 202 (a null stream) while a file just uploaded is not ready to download yet
        private async Task<Stream> download(string fileId, string path)
        {
            var started = DateTime.UtcNow;
            while (true)
            {
                var stream = await Client.Downloads.DownloadFileAsync(fileId);
                if (stream != null)
                    return stream;
                if (DateTime.UtcNow - started + NotReadyDelay > NotReadyTimeout)
                    throw new IOException($"Box did not make '{path}' ready to download within {NotReadyTimeout}.");
                await Task.Delay(NotReadyDelay);
            }
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {
            try
            {
                var (file, isFolder) = await resolvePath(absolutePathSegments);
                if (file == null || isFolder)
                    throw new FileNotFoundException($"File not found: {string.Join("/", absolutePathSegments)}");
                using var downloadStream = await download(file.Id, string.Join("/", absolutePathSegments));
                var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
                try
                {
                    await downloadStream.CopyToAsync(fileStream);
                }
                catch
                {
                    await fileStream.DisposeAsync();
                    throw;
                }
                fileStream.Position = 0;
                return fileStream;
            }
            catch (BoxSdkException ex)
            {
                throw new IOException($"Box API error reading file: {string.Join("/", absolutePathSegments)}: {ex.Message}", ex);
            }
        }

        protected override async Task updateFileAsync(Stream newContent, UpdateFileMode mode, params string[] absolutePathSegments)
        {
            try
            {
                if (isProviderRoot(absolutePathSegments))
                    refuseProviderRoot();

                var parentId = await ensureParentDirectoriesExist(absolutePathSegments[..^1]);
                var file = await findChild(parentId, absolutePathSegments[^1]);

                if (file != null && !file.IsFolder)
                {
                    if (mode == UpdateFileMode.Overwrite)
                        await uploadNewVersion(file.Id, file.Name, newContent);
                    else if (mode == UpdateFileMode.Append)
                        await appendToExistingFile(file, newContent);
                }
                else
                {
                    var fileName = absolutePathSegments.Last();
                    await uploadNewFile(fileName, parentId, newContent);
                }
            }
            catch (BoxSdkException ex)
            {
                throw new IOException($"Box API error updating file {string.Join("/", absolutePathSegments)}: {ex.Message}", ex);
            }
        }

        private async Task<string> ensureParentDirectoriesExist(string[] pathSegments)
        {
            var currentParentId = RootFolderId;
            foreach (var segmentName in pathSegments)
            {
                var existingItem = await findChild(currentParentId, segmentName);

                if (existingItem != null)
                {
                    if (!existingItem.IsFolder)
                        throw new InvalidOperationException($"Path segment '{segmentName}' exists but is a file, not a folder");
                    currentParentId = existingItem.Id;
                }
                else
                {
                    var createdFolder = await Client.Folders.CreateFolderAsync(
                        new CreateFolderRequestBody(segmentName, new CreateFolderRequestBodyParentField(currentParentId)));
                    currentParentId = createdFolder.Id;
                }
            }
            return currentParentId;
        }

        private async Task uploadNewFile(string fileName, string parentId, Stream fileStream)
        {
            if (!fileStream.CanSeek)
            {
                await using var spooled = await BoxUploadBuffer.SpoolAsync(fileStream);
                await uploadNewFileInternal(fileName, parentId, spooled);
            }
            else
            {
                await uploadNewFileInternal(fileName, parentId, fileStream);
            }
        }

        private async Task uploadNewFileInternal(string fileName, string parentId, Stream fileStream)
        {
            long fileSize = fileStream.Length;
            fileStream.Position = 0;

            if (fileSize < CHUNKED_UPLOAD_THRESHOLD)
            {
                await Client.Uploads.UploadFileAsync(new UploadFileRequestBody(
                    new UploadFileRequestBodyAttributesField(fileName, new UploadFileRequestBodyAttributesParentField(parentId)),
                    fileStream));
            }
            else
            {
                await Client.ChunkedUploads.UploadBigFileAsync(fileStream, fileName, fileSize, parentId);
            }
        }

        private async Task uploadNewVersion(string fileId, string fileName, Stream fileStream)
        {
            if (!fileStream.CanSeek)
            {
                await using var spooled = await BoxUploadBuffer.SpoolAsync(fileStream);
                await uploadNewVersionInternal(fileId, fileName, spooled);
            }
            else
            {
                await uploadNewVersionInternal(fileId, fileName, fileStream);
            }
        }

        private async Task uploadNewVersionInternal(string fileId, string fileName, Stream fileStream)
        {
            long fileSize = fileStream.Length;
            fileStream.Position = 0;
            if (fileSize < CHUNKED_UPLOAD_THRESHOLD)
                await Client.Uploads.UploadFileVersionAsync(fileId, new UploadFileVersionRequestBody(
                    new UploadFileVersionRequestBodyAttributesField(fileName), fileStream));
            else
                await Client.ChunkedUploads.UploadBigFileVersionAsync(fileId, fileStream, fileSize, fileName);
        }

        private async Task appendToExistingFile(Entry existingFile, Stream newContent)
        {
            Stream combined;
            using (var existingContent = await download(existingFile.Id, existingFile.Name))
                combined = await BoxUploadBuffer.CombineAsync(existingContent, newContent);

            await using (combined)
                await uploadNewVersionInternal(existingFile.Id, existingFile.Name, combined);
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            try
            {
                if (isProviderRoot(absolutePathSegments))
                    refuseProviderRoot();

                var (item, isFolder) = await resolvePath(absolutePathSegments);
                if (item == null) return false;
                if (isProviderRoot(absolutePathSegments, item))
                    refuseProviderRoot();

                if (isFolder)
                    await Client.Folders.DeleteFolderByIdAsync(item.Id, new DeleteFolderByIdQueryParams { Recursive = true });
                else
                    await Client.Files.DeleteFileByIdAsync(item.Id);

                return true;
            }
            catch (BoxSdkException ex)
            {
                throw new IOException($"Box API error deleting item: {string.Join("/", absolutePathSegments)}: {ex.Message}", ex);
            }
        }

        protected override async Task move(string[] oldSegments, string[] newSegments)
        {
            try
            {
                if (isProviderRoot(oldSegments) || isProviderRoot(newSegments))
                    refuseProviderRoot();

                var (item, _) = await resolvePath(oldSegments);
                if (item == null)
                    throw new FileNotFoundException($"Source item not found: {string.Join("/", oldSegments)}");
                if (isProviderRoot(oldSegments, item))
                    refuseProviderRoot();

                var (newParent, isFolder) = await resolvePath(newSegments[..^1], expectFolder: true);
                if (!isFolder)
                    throw new DirectoryNotFoundException($"Destination folder not found: {string.Join("/", newSegments[..^1])}");

                var newName = newSegments.Last();

                if (!item.IsFolder)
                {
                    await Client.Files.UpdateFileByIdAsync(item.Id, new UpdateFileByIdRequestBody
                    {
                        Name = newName,
                        Parent = new UpdateFileByIdRequestBodyParentField { Id = newParent.Id }
                    });
                }
                else
                {
                    await Client.Folders.UpdateFolderByIdAsync(item.Id, new UpdateFolderByIdRequestBody
                    {
                        Name = newName,
                        Parent = new UpdateFolderByIdRequestBodyParentField { Id = newParent.Id }
                    });
                }
            }
            catch (BoxSdkException ex)
            {
                throw new IOException($"Box API error moving item from {string.Join("/", oldSegments)} to {string.Join("/", newSegments)}: {ex.Message}", ex);
            }
        }

        protected override async Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            try
            {
                var (item, isFolder) = await resolvePath(absolutePathSegments);
                if (item == null || isFolder)
                    throw new FileNotFoundException($"File not found or is a folder: {string.Join("/", absolutePathSegments)}");

                // Retrieves the temporary direct Uri to a file (valid for 15 minutes)
                return await Client.Downloads.GetDownloadFileUrlAsync(item.Id);
            }
            catch (BoxSdkException ex)
            {
                throw new IOException($"Box API error while retrieving download URL: {ex.Message}", ex);
            }
        }

        protected override Task<IChangeToken> watchAsync(string filter)
        {
            throw new NotSupportedException("Watch is not supported in BoxCloudFileStorageManager");
        }
    }
}
