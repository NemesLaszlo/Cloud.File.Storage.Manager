using Box.V2.Config;
using Box.V2.Exceptions;
using Box.V2.JWTAuth;
using Box.V2.Models;
using Box.V2;
using Cloud.File.Storage.Manager.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using FileInfo = Cloud.File.Storage.Manager.Common.FileInfo;

namespace Cloud.File.Storage.Manager.Box
{
    public class BoxCloudFileStorageManager : CloudFileStorageManager<BoxCloudFileStorageManagerOptions>
    {
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);
        private readonly BoxJWTAuth jwtAuth;
        private string accessToken;
        private DateTime tokenExpirationTime;
        private BoxClient Client;
        private readonly string RootFolderId;

        private readonly SemaphoreSlim tokenRefreshSemaphore = new SemaphoreSlim(1, 1);

        private const long CHUNKED_UPLOAD_THRESHOLD = 20_000_000; // 20MB minimum for Box chunked uploads

        private static readonly string[] ItemFields = { "type", "id", "name", "size", "modified_at", "content_modified_at" };

        public BoxCloudFileStorageManager(BoxCloudFileStorageManagerOptions options) : base(options)
        {
            if (string.IsNullOrEmpty(options.ClientId)) throw new ArgumentException("ClientId is required");
            if (string.IsNullOrEmpty(options.ClientSecret)) throw new ArgumentException("ClientSecret is required");
            if (string.IsNullOrEmpty(options.EnterpriseId)) throw new ArgumentException("EnterpriseId is required");
            if (string.IsNullOrEmpty(options.PublicKeyId)) throw new ArgumentException("PublicKeyId is required");
            if (string.IsNullOrEmpty(options.PrivateKey)) throw new ArgumentException("PrivateKey is required");

            var boxConfig = new BoxConfig(
                options.ClientId,
                options.ClientSecret,
                options.EnterpriseId,
                normalizePrivateKey(options.PrivateKey),
                options.Passphrase,
                options.PublicKeyId);

            RootFolderId = options.RootFolderId ?? "0";
            jwtAuth = new BoxJWTAuth(boxConfig);
            initializeOAuthSessionAsync().Wait();
            Client = jwtAuth.AdminClient(accessToken);
        }

        internal static string normalizePrivateKey(string pem) => normalizePem(pem);

        private async Task initializeOAuthSessionAsync()
        {
            accessToken = await jwtAuth.AdminTokenAsync();
            tokenExpirationTime = DateTime.UtcNow.AddSeconds(3600);
        }

        private async Task ensureValidSessionAsync()
        {
            if (string.IsNullOrEmpty(accessToken) || tokenExpirationTime <= DateTime.UtcNow.AddMinutes(5))
            {
                await tokenRefreshSemaphore.WaitAsync();
                try
                {
                    // Double-check inside the lock, because another thread might have refreshed the token already
                    if (string.IsNullOrEmpty(accessToken) || tokenExpirationTime <= DateTime.UtcNow.AddMinutes(5))
                    {
                        await initializeOAuthSessionAsync();
                        Client = jwtAuth.AdminClient(accessToken);
                    }
                }
                finally
                {
                    tokenRefreshSemaphore.Release();
                }
            }
        }

        private sealed class BoxRootFolder : BoxFolder
        {
            public BoxRootFolder(string id)
            {
                Id = id;
                Type = "folder";
            }
        }

        private async Task<BoxItem> findChild(string parentId, string name)
        {
            int offset = 0;
            const int limit = 500;

            while (true)
            {
                try
                {
                    var items = await Client.FoldersManager.GetFolderItemsAsync(parentId, limit, offset, ItemFields);
                    var match = items.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        return match;

                    offset += limit;
                    if (offset >= items.TotalCount)
                        return null;
                }
                catch (BoxException ex)
                {
                    throw new BoxException($"Box API error while resolving path segment '{name}': {ex.Message}", ex);
                }
            }
        }

        // Zero segments resolve to the provider root folder.
        private async Task<(BoxItem item, bool isFolder)> resolvePath(string[] pathSegments, bool expectFolder = false)
        {
            BoxItem current = new BoxRootFolder(RootFolderId);

            foreach (var segment in pathSegments)
            {
                if (current.Type != "folder")
                    return (null, false);
                current = await findChild(current.Id, segment);
                if (current == null)
                    return (null, false);
            }
            bool isFolder = current.Type == "folder";
            if (expectFolder && !isFolder)
                return (null, false);
            return (current, isFolder);
        }

        private bool isProviderRoot(string[] absolutePathSegments, BoxItem item = null)
        {
            return absolutePathSegments.Length <= Options.RootDirectorySegments.Length || item?.Id == RootFolderId;
        }

        private static void refuseProviderRoot()
        {
            throw new InvalidOperationException("The provider root cannot be deleted or modified.");
        }

        private DateTimeOffset lastModified(BoxItem item)
        {
            return item.ModifiedAt
                ?? (item as BoxFile)?.ContentModifiedAt
                ?? (item as BoxFolder)?.ContentModifiedAt
                ?? DefaultDateTime;
        }

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            try
            {
                await ensureValidSessionAsync();
                var (file, isFolder) = await resolvePath(absolutePathSegments);
                var name = absolutePathSegments.LastOrDefault() ?? "";
                if (file == null)
                    return new FileInfo(this, false, 0, string.Join("/", absolutePathSegments), name,
                        DefaultDateTime, false, getRelativeSegments(absolutePathSegments));

                long fileSize = -1;
                var modifiedAt = lastModified(file);
                if (!isFolder && file.Type == "file")
                {
                    fileSize = file.Size ?? 0;
                    if (file.Size == null || file.ModifiedAt == null)
                    {
                        try
                        {
                            var completeFileInfo = await Client.FilesManager.GetInformationAsync(file.Id);
                            fileSize = completeFileInfo.Size ?? fileSize;
                            modifiedAt = completeFileInfo.ModifiedAt ?? modifiedAt;
                        }
                        catch
                        {
                            // Keep the values from the folder listing
                        }
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
            catch (BoxException ex)
            {
                throw new BoxException($"Box API error retrieving file info for {string.Join("/", absolutePathSegments)}: {ex.Message}", ex);
            }
            catch (Exception)
            {
                throw;
            }
        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            try
            {
                await ensureValidSessionAsync();
                var (folder, isFolder) = await resolvePath(absolutePathSegments, expectFolder: true);
                if (folder == null || !isFolder)
                    return new DirectoryContents(false, Array.Empty<IFileInfo>());

                var results = new List<IFileInfo>();
                int offset = 0;
                const int limit = 500;

                while (true)
                {
                    var items = await Client.FoldersManager.GetFolderItemsAsync(folder.Id, limit, offset, ItemFields);

                    foreach (var item in items.Entries)
                    {
                        results.Add(new FileInfo(this,
                            item.Type == "file" || item.Type == "folder",
                            item is BoxFile f ? f.Size ?? 0 : -1,
                            item.Name,
                            item.Name,
                            lastModified(item),
                            item.Type == "folder",
                            getRelativeSegments(absolutePathSegments.Concat(new[] { item.Name }).ToArray())));
                    }

                    offset += limit;
                    if (offset >= items.TotalCount)
                        break;
                }
                return new DirectoryContents(true, results.ToArray());
            }
            catch (BoxException ex)
            {
                throw new BoxException($"Box API error reading directory contents: {string.Join("/", absolutePathSegments)}: {ex.Message}", ex);
            }
            catch (Exception)
            {
                throw;
            }
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {
            try
            {
                await ensureValidSessionAsync();
                var (file, _) = await resolvePath(absolutePathSegments);
                if (file is not BoxFile boxFile)
                    throw new FileNotFoundException($"File not found: {string.Join("/", absolutePathSegments)}");
                using var downloadStream = await Client.FilesManager.DownloadAsync(boxFile.Id);
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
            catch (BoxException ex)
            {
                throw new BoxException($"Box API error reading file: {string.Join("/", absolutePathSegments)}: {ex.Message}", ex);
            }
            catch (Exception)
            {
                throw;
            }
        }

        protected override async Task updateFileAsync(Stream newContent, UpdateFileMode mode, params string[] absolutePathSegments)
        {
            try
            {
                if (isProviderRoot(absolutePathSegments))
                    refuseProviderRoot();

                await ensureValidSessionAsync();
                var parentId = await ensureParentDirectoriesExist(absolutePathSegments[..^1]);
                var file = await findChild(parentId, absolutePathSegments[^1]);

                if (file is BoxFile existingFile)
                {
                    if (mode == UpdateFileMode.Overwrite)
                        await uploadNewVersion(existingFile.Id, existingFile.Name, newContent);
                    else if (mode == UpdateFileMode.Append)
                        await appendToExistingFile(existingFile, newContent);
                }
                else
                {
                    var fileName = absolutePathSegments.Last();
                    await uploadNewFile(fileName, parentId, newContent);
                }
            }
            catch (BoxException ex)
            {
                throw new BoxException($"Box API error updating file {string.Join("/", absolutePathSegments)}: {ex.Message}", ex);
            }
            catch (Exception)
            {
                throw;
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
                    if (existingItem.Type != "folder")
                        throw new InvalidOperationException($"Path segment '{segmentName}' exists but is a file, not a folder");
                    currentParentId = existingItem.Id;
                }
                else
                {
                    var folderRequest = new BoxFolderRequest
                    {
                        Name = segmentName,
                        Parent = new BoxRequestEntity { Id = currentParentId }
                    };
                    var createdFolder = await Client.FoldersManager.CreateAsync(folderRequest);
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
                var uploadRequest = new BoxFileRequest
                {
                    Name = fileName,
                    Parent = new BoxRequestEntity { Id = parentId }
                };
                await Client.FilesManager.UploadAsync(uploadRequest, fileStream);
            }
            else
            {
                await Client.FilesManager.UploadUsingSessionAsync(
                    fileStream,
                    fileName,
                    parentId,
                    timeout: TimeSpan.FromMinutes(5));
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
                await Client.FilesManager.UploadNewVersionAsync(fileName, fileId, fileStream);
            else
                await Client.FilesManager.UploadNewVersionUsingSessionAsync(
                    fileStream,
                    fileId,
                    fileName,
                    timeout: TimeSpan.FromMinutes(5));
        }

        private async Task appendToExistingFile(BoxFile existingFile, Stream newContent)
        {
            Stream combined;
            using (var existingContent = await Client.FilesManager.DownloadAsync(existingFile.Id))
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

                await ensureValidSessionAsync();
                var (item, isFolder) = await resolvePath(absolutePathSegments);
                if (item == null) return false;
                if (isProviderRoot(absolutePathSegments, item))
                    refuseProviderRoot();

                if (isFolder)
                    await Client.FoldersManager.DeleteAsync(item.Id, recursive: true);
                else
                    await Client.FilesManager.DeleteAsync(item.Id);

                return true;
            }
            catch (BoxException ex)
            {
                throw new BoxException($"Box API error deleting item: {string.Join("/", absolutePathSegments)}: {ex.Message}", ex);
            }
            catch (Exception)
            {
                throw;
            }
        }

        protected override async Task move(string[] oldSegments, string[] newSegments)
        {
            try
            {
                if (isProviderRoot(oldSegments) || isProviderRoot(newSegments))
                    refuseProviderRoot();

                await ensureValidSessionAsync();
                var (item, _) = await resolvePath(oldSegments);
                if (item == null)
                    throw new FileNotFoundException($"Source item not found: {string.Join("/", oldSegments)}");
                if (isProviderRoot(oldSegments, item))
                    refuseProviderRoot();

                var (newParent, isFolder) = await resolvePath(newSegments[..^1], expectFolder: true);
                if (!isFolder)
                    throw new DirectoryNotFoundException($"Destination folder not found: {string.Join("/", newSegments[..^1])}");

                var newName = newSegments.Last();

                if (item is BoxFile file)
                {
                    await Client.FilesManager.UpdateInformationAsync(new BoxFileRequest
                    {
                        Id = file.Id,
                        Name = newName,
                        Parent = new BoxRequestEntity { Id = newParent.Id }
                    });
                }
                else if (item is BoxFolder folder)
                {
                    await Client.FoldersManager.UpdateInformationAsync(new BoxFolderRequest
                    {
                        Id = folder.Id,
                        Name = newName,
                        Parent = new BoxRequestEntity { Id = newParent.Id }
                    });
                }
            }
            catch (BoxException ex)
            {
                throw new BoxException($"Box API error moving item from {string.Join("/", oldSegments)} to {string.Join("/", newSegments)}: {ex.Message}", ex);
            }
            catch (Exception)
            {
                throw;
            }
        }

        protected override async Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            try
            {
                await ensureValidSessionAsync();
                var (item, isFolder) = await resolvePath(absolutePathSegments);
                if (item is not BoxFile file)
                    throw new FileNotFoundException($"File not found or is a folder: {string.Join("/", absolutePathSegments)}");

                // Retrieves the temporary direct Uri to a file (valid for 15 minutes)
                var downloadUri = await Client.FilesManager.GetDownloadUriAsync(file.Id);
                return downloadUri.ToString();
            }
            catch (BoxException ex)
            {
                throw new BoxException($"Box API error while retrieving download URL: {ex.Message}", ex);
            }
            catch (Exception)
            {
                throw;
            }
        }

        protected override Task<IChangeToken> watchAsync(string filter)
        {
            throw new NotSupportedException("Watch is not supported in BoxCloudFileStorageManager");
        }
    }
}
