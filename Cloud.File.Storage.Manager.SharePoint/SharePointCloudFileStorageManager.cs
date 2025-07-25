﻿using Azure.Identity;
using Cloud.File.Storage.Manager.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using System.Text.Json;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using FileInfo = Cloud.File.Storage.Manager.Common.FileInfo;

namespace Cloud.File.Storage.Manager.SharePoint
{
    public class SharePointCloudFileStorageManager : CloudFileStorageManager<SharePointCloudFileStorageManagerOptions>
    {
        private readonly DateTime DefaultDateTime = new DateTime(1970, 1, 1, 0, 0, 0);
        private const long ChunkSize = 320 * 1024 * 10; // 3.2MB chunks (recommended by Microsoft)
        private const long SmallFileThreshold = 20 * 1024 * 1024; // 20MB - simple upload for smaller files
        private GraphServiceClient GraphClient { get; }
        private string DriveId { get; }
        private string WorkFolderId { get; set; }

        public SharePointCloudFileStorageManager(SharePointCloudFileStorageManagerOptions options) : base(options)
        {
            if (string.IsNullOrEmpty(Options.SiteUrl))
                throw new ArgumentException("SiteUrl is required", nameof(Options.SiteUrl));
            if (string.IsNullOrEmpty(Options.TenantId))
                throw new ArgumentException("TenantId is required", nameof(Options.TenantId));
            if (string.IsNullOrEmpty(Options.ClientId))
                throw new ArgumentException("ClientId is required", nameof(Options.ClientId));
            if (string.IsNullOrEmpty(Options.ClientSecret))
                throw new ArgumentException("ClientSecret is required", nameof(Options.ClientSecret));

            GraphClient = createGraphClient();
            DriveId = initializeDriveAsync().GetAwaiter().GetResult();
        }

        private GraphServiceClient createGraphClient()
        {
            var credential = new ClientSecretCredential(
                Options.TenantId,
                Options.ClientId,
                Options.ClientSecret
            );

            var graphClient = new GraphServiceClient(credential);
            return graphClient;
        }

        private string normalizeWorkFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            return path.Replace('\\', '/').Trim('/');
        }

        private string getRelativePathFromAbsolute(string[] absolutePathSegments)
        {
            if (string.IsNullOrEmpty(Options.WorkFolderPath))
            {
                // No work folder, use full path
                return string.Join("/", absolutePathSegments);
            }

            // Strip work folder segments from the beginning
            var workFolderSegments = normalizeWorkFolderPath(Options.WorkFolderPath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (absolutePathSegments.Length >= workFolderSegments.Length)
            {
                // Check if path starts with work folder
                bool startsWithWorkFolder = true;
                for (int i = 0; i < workFolderSegments.Length; i++)
                {
                    if (!absolutePathSegments[i].Equals(workFolderSegments[i], StringComparison.OrdinalIgnoreCase))
                    {
                        startsWithWorkFolder = false;
                        break;
                    }
                }

                if (startsWithWorkFolder)
                {
                    // Remove work folder segments, return the rest
                    var relativeSegments = absolutePathSegments.Skip(workFolderSegments.Length).ToArray();
                    return string.Join("/", relativeSegments);
                }
            }

            // Path doesn't start with work folder, use as-is
            return string.Join("/", absolutePathSegments);
        }

        private string[] getRelativeSegmentsFromAbsolute(string[] absolutePathSegments)
        {
            if (string.IsNullOrEmpty(Options.WorkFolderPath))
            {
                return absolutePathSegments;
            }

            var workFolderSegments = normalizeWorkFolderPath(Options.WorkFolderPath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (absolutePathSegments.Length >= workFolderSegments.Length)
            {
                bool startsWithWorkFolder = true;
                for (int i = 0; i < workFolderSegments.Length; i++)
                {
                    if (!absolutePathSegments[i].Equals(workFolderSegments[i], StringComparison.OrdinalIgnoreCase))
                    {
                        startsWithWorkFolder = false;
                        break;
                    }
                }

                if (startsWithWorkFolder)
                {
                    return absolutePathSegments.Skip(workFolderSegments.Length).ToArray();
                }
            }

            return absolutePathSegments;
        }

        private async Task<string> initializeDriveAsync()
        {
            try
            {
                var uri = new Uri(Options.SiteUrl);
                var hostname = uri.Host;
                var sitePath = uri.AbsolutePath;

                var site = await GraphClient.Sites[$"{hostname}:{sitePath}"].GetAsync();
                var siteId = site.Id;

                var drives = await GraphClient.Sites[siteId].Drives.GetAsync();
                var targetDrive = drives.Value.FirstOrDefault(d =>
                    string.IsNullOrEmpty(Options.DocumentLibraryName) ||
                    d.Name.Equals(Options.DocumentLibraryName, StringComparison.OrdinalIgnoreCase));

                if (targetDrive == null)
                    throw new ArgumentException($"Document library '{Options.DocumentLibraryName}' not found");

                if (!string.IsNullOrEmpty(Options.WorkFolderPath))
                {
                    try
                    {
                        // Normalize the path to use forward slashes
                        var normalizedWorkFolderPath = normalizeWorkFolderPath(Options.WorkFolderPath);
                        // Check if the work folder exists
                        var workFolderItem = await GraphClient.Drives[targetDrive.Id].Items["root"]
                            .ItemWithPath(normalizedWorkFolderPath)
                            .GetAsync();

                        if (workFolderItem == null || workFolderItem.Folder == null)
                        {
                            // If it doesn't exist, create the folder hierarchy
                            await createWorkFolderHierarchyFromPath(targetDrive.Id, normalizedWorkFolderPath);
                            // Get the created folder
                            workFolderItem = await GraphClient.Drives[targetDrive.Id].Items["root"]
                                .ItemWithPath(normalizedWorkFolderPath)
                                .GetAsync();
                        }
                        WorkFolderId = workFolderItem.Id;
                    }
                    catch (ODataError ex) when (ex.Error?.Code == "itemNotFound")
                    {
                        // Folder not found, create the entire hierarchy
                        var normalizedWorkFolderPath = normalizeWorkFolderPath(Options.WorkFolderPath);
                        await createWorkFolderHierarchyFromPath(targetDrive.Id, normalizedWorkFolderPath);
                        // Get the created folder
                        var workFolderItem = await GraphClient.Drives[targetDrive.Id].Items["root"]
                            .ItemWithPath(normalizedWorkFolderPath)
                            .GetAsync();
                        WorkFolderId = workFolderItem.Id;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Failed to initialize work folder '{Options.WorkFolderPath}'", ex);
                    }
                }

                return targetDrive.Id;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to initialize SharePoint connection", ex);
            }
        }

        private async Task createWorkFolderHierarchyFromPath(string driveId, string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return;
            var pathSegments = folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            await createWorkFolderHierarchyRecursive(driveId, pathSegments, 0, "");
        }

        private async Task createWorkFolderHierarchyRecursive(string driveId, string[] pathSegments, int currentIndex, string currentPath)
        {
            if (currentIndex >= pathSegments.Length)
                return;

            var segment = pathSegments[currentIndex];
            var newPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}/{segment}";

            try
            {
                // Check if this level already exists
                var existingItem = await GraphClient.Drives[driveId].Items["root"]
                    .ItemWithPath(newPath)
                    .GetAsync();

                if (existingItem != null && existingItem.Folder != null)
                {
                    // Folder exists, continue to next level
                    await createWorkFolderHierarchyRecursive(driveId, pathSegments, currentIndex + 1, newPath);
                    return;
                }
            }
            catch (ODataError ex) when (ex.Error?.Code == "itemNotFound")
            {
                // Folder doesn't exist, we will create it
            }

            try
            {
                // Create the folder at this level
                var driveItem = new DriveItem
                {
                    Name = segment,
                    Folder = new Folder()
                };

                if (string.IsNullOrEmpty(currentPath))
                {
                    // Create in root
                    await GraphClient.Drives[driveId].Items["root"].Children.PostAsync(driveItem);
                }
                else
                {
                    // Create in parent folder
                    await GraphClient.Drives[driveId].Items["root"]
                        .ItemWithPath(currentPath)
                        .Children.PostAsync(driveItem);
                }

                // Continue to next level
                await createWorkFolderHierarchyRecursive(driveId, pathSegments, currentIndex + 1, newPath);
            }
            catch (ODataError ex)
            {
                // If folder already exists, that's fine
                if (ex.Error?.Code == "nameAlreadyExists")
                {
                    await createWorkFolderHierarchyRecursive(driveId, pathSegments, currentIndex + 1, newPath);
                    return;
                }
                throw;
            }
        }

        private Microsoft.Graph.Drives.Item.Items.Item.DriveItemItemRequestBuilder getParentItemBuilder()
        {
            if (!string.IsNullOrEmpty(WorkFolderId))
                return GraphClient.Drives[DriveId].Items[WorkFolderId];
            return GraphClient.Drives[DriveId].Items["root"];
        }

        private string getPath(string[] absolutePathSegments) => string.Join("/", absolutePathSegments);

        private async Task<(DriveItem[] files, DriveItem[] directories)> enumerateFiles(string[] absolutePathSegments, bool deep)
        {
            try
            {
                var relativePath = getRelativePathFromAbsolute(absolutePathSegments);
                var items = new List<DriveItem>();
                var allItems = await getAllItemsAsync(relativePath);
                items.AddRange(allItems);

                if (deep)
                {
                    var subDirectories = items.Where(i => i.Folder != null).ToArray();
                    foreach (var subDir in subDirectories)
                    {
                        var subPath = absolutePathSegments.Concat(new[] { subDir.Name }).ToArray();
                        var subItems = await enumerateFiles(subPath, true);
                        items.AddRange(subItems.files);
                        items.AddRange(subItems.directories);
                    }
                }

                var files = items.Where(i => i.File != null).ToArray();
                var directories = items.Where(i => i.Folder != null).ToArray();

                return (files, directories);
            }
            catch (ODataError ex)
            {
                if (ex.Error?.Code == "itemNotFound")
                {
                    return (Array.Empty<DriveItem>(), Array.Empty<DriveItem>());
                }
                throw;
            }
        }

        private async Task<IEnumerable<DriveItem>> getAllItemsAsync(string relativePath)
        {
            var items = new List<DriveItem>();
            DriveItemCollectionResponse response;

            if (string.IsNullOrEmpty(relativePath))
                response = await getParentItemBuilder().Children.GetAsync();
            else
                response = await getParentItemBuilder().ItemWithPath(relativePath).Children.GetAsync();

            if (response?.Value != null)
                items.AddRange(response.Value);

            var pageIterator = PageIterator<DriveItem, DriveItemCollectionResponse>
                .CreatePageIterator(GraphClient, response, (item) => {
                    items.Add(item);
                    return true;
                });

            await pageIterator.IterateAsync();
            return items;
        }

        protected override async Task<IFileInfo> getFileInfoAsync(params string[] absolutePathSegments)
        {
            try
            {
                var relativePath = getRelativePathFromAbsolute(absolutePathSegments);
                DriveItem item;

                if (string.IsNullOrEmpty(relativePath))
                    item = await getParentItemBuilder().GetAsync();
                else
                    item = await getParentItemBuilder().ItemWithPath(relativePath).GetAsync();

                return new FileInfo(
                    this,
                    true,
                    item.Size ?? 0,
                    getPath(absolutePathSegments),
                    absolutePathSegments.Last(),
                    item.LastModifiedDateTime ?? DefaultDateTime,
                    item.Folder != null,
                    getRelativeSegments(absolutePathSegments)
                );
            }
            catch (ODataError ex)
            {
                if (ex.Error?.Code == "itemNotFound")
                {
                    return new FileInfo(
                        this,
                        false,
                        0,
                        getPath(absolutePathSegments),
                        absolutePathSegments.Last(),
                        DefaultDateTime,
                        false,
                        getRelativeSegments(absolutePathSegments)
                    );
                }
                throw new FileNotFoundException($"SharePoint error retrieving file info for {getPath(absolutePathSegments)}: {ex.Message}", ex);
            }
            catch (Exception)
            {
                throw;
            }
        }

        protected override async Task<IDirectoryContents> getDirectoryContentsAsync(params string[] absolutePathSegments)
        {
            var e = await enumerateFiles(absolutePathSegments, false);
            var ret = new List<IFileInfo>();

            foreach (var file in e.files)
            {
                ret.Add(new FileInfo(
                    this,
                    true,
                    file.Size ?? 0,
                    file.Name,
                    file.Name,
                    file.LastModifiedDateTime ?? DefaultDateTime,
                    false,
                    getRelativeSegments(absolutePathSegments.Concat(new[] { file.Name }).ToArray())
                ));
            }

            foreach (var dir in e.directories)
            {
                ret.Add(new FileInfo(
                    this,
                    false,
                    -1,
                    dir.Name,
                    dir.Name,
                    dir.LastModifiedDateTime ?? DefaultDateTime,
                    true,
                    getRelativeSegments(absolutePathSegments.Concat(new[] { dir.Name }).ToArray())
                ));
            }

            return new DirectoryContents(ret.Any(), ret.ToArray());
        }

        protected override async Task<Stream> readFileAsync(params string[] absolutePathSegments)
        {
            try
            {
                var relativePath = getRelativePathFromAbsolute(absolutePathSegments);
                var contentStream = await getParentItemBuilder().ItemWithPath(relativePath).Content.GetAsync();
                if (contentStream == null)
                    throw new FileNotFoundException($"File content not available for {getPath(absolutePathSegments)}");

                var tempFilePath = Path.GetTempFileName();
                var seekableFileStream = new FileStream(
                    tempFilePath,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    4096,
                    FileOptions.DeleteOnClose
                );

                using (contentStream)
                {
                    await contentStream.CopyToAsync(seekableFileStream);
                }
                seekableFileStream.Position = 0;
                return seekableFileStream;
            }
            catch (ODataError ex)
            {
                if (ex.Error?.Code == "itemNotFound")
                {
                    throw new FileNotFoundException($"File not found: {getPath(absolutePathSegments)}");
                }
                throw;
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async Task<bool> ensureFolderPathExistsAsync(params string[] absolutePathSegments)
        {
            if (absolutePathSegments == null || absolutePathSegments.Length == 0)
            {
                try
                {
                    var rootItem = await getParentItemBuilder().GetAsync();
                    return rootItem != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            try
            {
                // Check if the full path already exists in SharePoint
                var relativePath = getRelativePathFromAbsolute(absolutePathSegments);
                var existingItem = await getFolderItemAsync(relativePath);
                if (existingItem != null && existingItem.Folder != null)
                    return true;

                // Path doesn't exist, create the folder hierarchy step by step
                var relativeSegments = getRelativeSegmentsFromAbsolute(absolutePathSegments);
                await createFolderHierarchyAsync(relativeSegments);

                // Verify the path was created successfully
                var verificationItem = await getFolderItemAsync(relativePath);
                return verificationItem != null && verificationItem.Folder != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<DriveItem> getFolderItemAsync(string path)
        {
            try
            {
                DriveItem item;
                if (string.IsNullOrEmpty(path))
                    item = await getParentItemBuilder().GetAsync();
                else
                    item = await getParentItemBuilder().ItemWithPath(path).GetAsync();

                return item?.Folder != null ? item : null;
            }
            catch (ODataError ex)
            {
                if (ex.Error?.Code == "itemNotFound")
                    return null;
                throw;
            }
        }

        private async Task createFolderHierarchyAsync(string[] pathSegments)
        {
            if (pathSegments == null || pathSegments.Length == 0)
                return;

            var currentPath = "";

            for (int i = 0; i < pathSegments.Length; i++)
            {
                var segment = pathSegments[i];
                if (string.IsNullOrWhiteSpace(segment))
                    continue;

                var newPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}/{segment}";

                try
                {
                    // Check if this level already exists
                    var existingItem = await getFolderItemAsync(newPath);
                    if (existingItem != null)
                    {
                        currentPath = newPath;
                        continue;
                    }

                    // Create the folder at this level
                    var driveItem = new DriveItem
                    {
                        Name = segment,
                        Folder = new Folder()
                    };

                    if (string.IsNullOrEmpty(currentPath))
                    {
                        // Create in root
                        await getParentItemBuilder().Children.PostAsync(driveItem);
                    }
                    else
                    {
                        // Create in parent folder
                        await getParentItemBuilder().ItemWithPath(currentPath).Children.PostAsync(driveItem);
                    }

                    currentPath = newPath;
                }
                catch (ODataError ex)
                {
                    // If folder already exists, that's fine
                    if (ex.Error?.Code == "nameAlreadyExists")
                    {
                        currentPath = newPath;
                        continue;
                    }
                    throw;
                }
            }
        }

        protected override async Task updateFileAsync(Stream contents, UpdateFileMode mode, params string[] absolutePathSegments)
        {
            var ownsBufferedStream = false;
            Stream bufferedIncomingStream = contents;
            try
            {
                if (!contents.CanSeek)
                {
                    var tempStream = new FileStream(
                        Path.GetTempFileName(),
                        FileMode.Create,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        4096,
                        FileOptions.Asynchronous | FileOptions.DeleteOnClose);

                    await contents.CopyToAsync(tempStream);
                    tempStream.Position = 0;

                    bufferedIncomingStream = tempStream;
                    ownsBufferedStream = true;
                }

                if (absolutePathSegments.Length > 1)
                {
                    var folderPath = absolutePathSegments.Take(absolutePathSegments.Length - 1).ToArray();
                    var folderExists = await ensureFolderPathExistsAsync(folderPath);
                    if (!folderExists)
                    {
                        throw new InvalidOperationException($"Failed to create folder path: {getPath(folderPath)}");
                    }
                }

                if (mode == UpdateFileMode.Append)
                {
                    Stream finalContentsStream = bufferedIncomingStream;
                    var ownsFinalStream = false;
                    try
                    {
                        try
                        {
                            await using var existingContent = await readFileAsync(absolutePathSegments);

                            var existingSize = existingContent.Length;
                            var newContentSize = bufferedIncomingStream.Length;
                            var totalSize = existingSize + newContentSize;

                            if (totalSize > SmallFileThreshold)
                            {
                                var tempFileStream = new FileStream(
                                    Path.GetTempFileName(),
                                    FileMode.OpenOrCreate,
                                    FileAccess.ReadWrite,
                                    FileShare.None,
                                    4096,
                                    FileOptions.DeleteOnClose | FileOptions.SequentialScan);

                                await existingContent.CopyToAsync(tempFileStream);
                                await bufferedIncomingStream.CopyToAsync(tempFileStream);
                                tempFileStream.Position = 0;

                                finalContentsStream = tempFileStream;
                                ownsFinalStream = true;
                            }
                            else
                            {
                                var combinedStream = new MemoryStream();
                                await existingContent.CopyToAsync(combinedStream);
                                await bufferedIncomingStream.CopyToAsync(combinedStream);
                                combinedStream.Position = 0;

                                finalContentsStream = combinedStream;
                                ownsFinalStream = true;
                            }
                        }
                        catch (FileNotFoundException)
                        {
                            // File doesn't exist yet — treat it as new
                        }

                        if (finalContentsStream.CanSeek)
                            finalContentsStream.Position = 0;

                        if (finalContentsStream.Length <= SmallFileThreshold)
                            await uploadSmallFileAsync(finalContentsStream, absolutePathSegments);
                        else
                            await uploadLargeFileAsync(finalContentsStream, absolutePathSegments);
                    }
                    finally
                    {
                        if (ownsFinalStream)
                            await finalContentsStream.DisposeAsync();
                    }
                }
                else
                {
                    if (bufferedIncomingStream.CanSeek)
                        bufferedIncomingStream.Position = 0;

                    if (bufferedIncomingStream.Length <= SmallFileThreshold)
                        await uploadSmallFileAsync(bufferedIncomingStream, absolutePathSegments);
                    else
                        await uploadLargeFileAsync(bufferedIncomingStream, absolutePathSegments);
                }
            }
            catch (ODataError ex)
            {
                if (ex.Error?.Code == "itemNotFound")
                {
                    await createFolderHierarchyAsync(absolutePathSegments.Take(absolutePathSegments.Length - 1).ToArray());
                    if (bufferedIncomingStream.CanSeek)
                        bufferedIncomingStream.Position = 0;
                    await updateFileAsync(bufferedIncomingStream, mode, absolutePathSegments);
                    return;
                }
                throw;
            }
            finally
            {
                if (ownsBufferedStream)
                    await bufferedIncomingStream.DisposeAsync();
            }
        }

        private async Task uploadSmallFileAsync(Stream contents, string[] absolutePathSegments)
        {
            var relativePath = getRelativePathFromAbsolute(absolutePathSegments);
            await getParentItemBuilder().ItemWithPath(relativePath).Content.PutAsync(contents);
        }

        private async Task uploadLargeFileAsync(Stream contents, string[] absolutePathSegments)
        {
            var fileName = absolutePathSegments.Last();
            var fileSize = contents.Length;

            var uploadSession = await createUploadSessionAsync(absolutePathSegments);
            var remainingBytes = fileSize;
            var nextExpectedRangeStart = 0L;
            var buffer = new byte[ChunkSize];

            while (remainingBytes > 0)
            {
                var bytesToRead = (int)Math.Min(ChunkSize, remainingBytes);
                var bytesRead = await contents.ReadAsync(buffer, 0, bytesToRead);

                if (bytesRead == 0)
                    break;


                var chunkStream = new MemoryStream(buffer, 0, bytesRead);
                var rangeHeader = $"bytes {nextExpectedRangeStart}-{nextExpectedRangeStart + bytesRead - 1}/{fileSize}";
                var uploadResult = await uploadChunkAsync(uploadSession.UploadUrl, chunkStream, rangeHeader);

                if (uploadResult.Item != null)
                {
                    // Upload completed
                    break;
                }

                nextExpectedRangeStart += bytesRead;
                remainingBytes -= bytesRead;
            }
        }

        private async Task<UploadSession> createUploadSessionAsync(string[] absolutePathSegments)
        {
            var createUploadSessionPostRequestBody = new CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        {"@microsoft.graph.conflictBehavior", "replace"}
                    }
                }
            };

            var relativePath = getRelativePathFromAbsolute(absolutePathSegments);
            return await getParentItemBuilder().ItemWithPath(relativePath).CreateUploadSession.PostAsync(createUploadSessionPostRequestBody);
        }

        private async Task<UploadResult<DriveItem>> uploadChunkAsync(string uploadUrl, Stream chunkStream, string rangeHeader)
        {
            var requestAdapter = GraphClient.RequestAdapter;
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.PUT,
                UrlTemplate = uploadUrl,
                Content = chunkStream
            };

            requestInfo.Headers.Add("Content-Range", rangeHeader);
            requestInfo.Headers.Add("Content-Length", chunkStream.Length.ToString());

            try
            {
                var response = await requestAdapter.SendPrimitiveAsync<Stream>(requestInfo);

                if (response != null)
                {
                    var content = await new StreamReader(response).ReadToEndAsync();

                    // Check if upload is complete by looking for the DriveItem in response
                    if (content.Contains("\"id\"") && content.Contains("\"name\""))
                    {
                        var driveItem = JsonSerializer.Deserialize<DriveItem>(content, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        return new UploadResult<DriveItem> { Item = driveItem };
                    }
                }

                return new UploadResult<DriveItem> { Item = null };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Chunk upload failed: {ex.Message}", ex);
            }
        }

        protected override async Task<bool> deleteAsync(params string[] absolutePathSegments)
        {
            try
            {
                var relativePath = getRelativePathFromAbsolute(absolutePathSegments);
                await getParentItemBuilder().ItemWithPath(relativePath).DeleteAsync();
                return true;
            }
            catch (ODataError ex)
            {
                if (ex.Error?.Code == "itemNotFound")
                {
                    return false;
                }
                throw;
            }
        }

        protected override async Task move(string[] oldSegments, string[] newSegments)
        {
            try
            {
                if (oldSegments == null || oldSegments.Length == 0)
                    throw new ArgumentException("Source path cannot be null or empty", nameof(oldSegments));
                if (newSegments == null || newSegments.Length == 0)
                    throw new ArgumentException("Destination path cannot be null or empty", nameof(newSegments));

                var oldRelativePath = getRelativePathFromAbsolute(oldSegments);
                var newFileName = newSegments.Last();
                var newParentSegments = newSegments.Length > 1 ? newSegments.Take(newSegments.Length - 1).ToArray() : new string[0];
                var newParentRelativePath = getRelativePathFromAbsolute(newParentSegments);

                try
                {
                    var sourceItem = await getParentItemBuilder().ItemWithPath(oldRelativePath).GetAsync();
                    if (sourceItem == null)
                    {
                        throw new FileNotFoundException($"Source item not found: {getPath(oldSegments)}");
                    }
                }
                catch (ODataError ex)
                {
                    if (ex.Error?.Code == "itemNotFound")
                    {
                        throw new FileNotFoundException($"Source item not found: {getPath(oldSegments)}");
                    }
                    throw;
                }

                if (newSegments.Length > 1)
                {
                    var destinationFolderPath = newSegments.Take(newSegments.Length - 1).ToArray();
                    var folderExists = await ensureFolderPathExistsAsync(destinationFolderPath);
                    if (!folderExists)
                    {
                        throw new InvalidOperationException($"Failed to create destination folder path: {getPath(destinationFolderPath)}");
                    }
                }

                var moveRequest = new DriveItem
                {
                    Name = newFileName
                };

                if (!string.IsNullOrEmpty(newParentRelativePath))
                {
                    var parentItem = await getParentItemBuilder().ItemWithPath(newParentRelativePath).GetAsync();
                    moveRequest.ParentReference = new ItemReference
                    {
                        Id = parentItem.Id
                    };
                }
                else
                {
                    var rootItem = await getParentItemBuilder().GetAsync();
                    moveRequest.ParentReference = new ItemReference
                    {
                        Id = rootItem.Id
                    };
                }

                await getParentItemBuilder().ItemWithPath(oldRelativePath).PatchAsync(moveRequest);
            }
            catch (ODataError ex)
            {
                if (ex.Error?.Code == "itemNotFound")
                {
                    throw new FileNotFoundException($"Source file not found: {getPath(oldSegments)}");
                }
                throw;
            }
        }

        protected override async Task<string> getDownloadUrl(string[] absolutePathSegments, TimeSpan validity)
        {
            try
            {
                var relativePath = getRelativePathFromAbsolute(absolutePathSegments);
                var item = await getParentItemBuilder().ItemWithPath(relativePath).GetAsync();
                return item.AdditionalData?.ContainsKey("@microsoft.graph.downloadUrl") == true
                    ? item.AdditionalData["@microsoft.graph.downloadUrl"].ToString()
                    : throw new NotSupportedException("Download URL not available for this item");
            }
            catch (ODataError ex)
            {
                if (ex.Error?.Code == "itemNotFound")
                {
                    throw new FileNotFoundException($"File not found: {getPath(absolutePathSegments)}");
                }
                throw;
            }
        }

        protected override Task<IChangeToken> watchAsync(string filter)
        {
            throw new PlatformNotSupportedException("Watch is not supported in WriteableSharePointFileProvider");
        }
    }

    public class UploadResult<T>
    {
        public T Item { get; set; }
    }
}
