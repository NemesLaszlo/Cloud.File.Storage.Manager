# Cloud File Storage Manager

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `SharePoint` right now.

### Configuration

Let's add a section to the `appsettings.json`

```
"SharePointStorageSettings": {
    // The SharePoint site URL (e.g., https://tenant.sharepoint.com/sites/sitename)
    "SiteUrl": "",
    "DocumentLibraryName": "",
    "TenantId": "",
    "ClientId": "",
    "ClientSecret": "",
    // Optional: The folder name where the files will be stored, like: "MainFolder/WorkFolder/Test"
    "WorkFolderPath": ""
}
```
In the application, configure this settings part

```cs
services.AddSingleton<ICloudFileStorageManager, SharePointCloudFileStorageManager>(services => new SharePointCloudFileStorageManager(new SharePointCloudFileStorageManagerOptions()
{
    SiteUrl = config.GetSection("SharePointStorageSettings").GetValue<string>("SiteUrl"),
    DocumentLibraryName = config.GetSection("SharePointStorageSettings").GetValue<string>("DocumentLibraryName"),
    TenantId = config.GetSection("SharePointStorageSettings").GetValue<string>("TenantId"),
    ClientId = config.GetSection("SharePointStorageSettings").GetValue<string>("ClientId"),
    ClientSecret = config.GetSection("SharePointStorageSettings").GetValue<string>("ClientSecret"),
    WorkFolderPath = config.GetSection("SharePointStorageSettings").GetValue<string>("WorkFolderPath")
}));

```

## Azure App Registration configuration

Create an Azure AD app registration:
1. Go to Azure Portal > App registrations
2. Create a new registration
3. Add API permissions: `Sites.ReadWrite.All` - Microsoft Graph (Manage / API permissions section)
4. Create a client secret

Then, you have all the necessary values to connect your app to SharePoint and perform actions there.

## Behaviour

### Work folder

When `WorkFolderPath` is set, every path is relative to that folder of the document library. A path that already starts with the work folder segments (compared case-insensitively) is made relative to it, so `MainFolder/WorkFolder/Test/report.csv` and `report.csv` address the same file when `WorkFolderPath` is `MainFolder/WorkFolder/Test`. Any other path is used as-is below the work folder. Without `WorkFolderPath` the paths are relative to the root of the document library.

### Paths

Both `/` and `\` separate segments. A segment is rejected with an `ArgumentException` when it is empty or whitespace, consists only of dots and spaces, contains a control character or a colon (`:`), or does so after percent-decoding. A segment whose percent-decoded form contains `/` or `\` is rejected as well. The colon is reserved by the Microsoft Graph path syntax.

### Root protection

The work folder itself (or the library root when no work folder is set) and the `RootDirectoryPath` root cannot be deleted, moved, renamed or overwritten. These calls throw an `InvalidOperationException`, also when the path only consists of the work folder prefix.

### Uploads

- Files below 20 MB are uploaded in a single request, larger files with a Graph upload session in 3.2 MB chunks.
- Non-seekable streams are first buffered to a temporary file, which is deleted when the upload finishes or fails.
- Missing parent folders are created on upload and move. When a folder cannot be created, the `InvalidOperationException` keeps the original error as its inner exception.

### Listing and file information

`GetDirectoryContents` follows the Graph paging links and returns every file and folder once. Folders are reported with `Exists = true` and `IsDirectory = true`.

### Reading

`ReadFile` downloads the file into a temporary file that is deleted when the returned stream is disposed. When the download fails, the temporary file is removed right away.

### Download links

`GetDownloadUrl` returns the pre-authenticated download link Microsoft Graph provides for the file (`@microsoft.graph.downloadUrl`). Graph keeps the link valid for about an hour; the validity argument is ignored. Anyone holding the link can download the file without signing in.

### Not supported

`Watch` is not supported and throws a `PlatformNotSupportedException`.
