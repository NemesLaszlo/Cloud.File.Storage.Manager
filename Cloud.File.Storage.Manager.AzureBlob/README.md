# Cloud File Storage Manager

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `Azure (Azure Blob Storage)` right now.

### Configuration

Let's add a section to the `appsettings.json`

```
"AzureStorageSettings": {
    // Optional - http or https, defaults to https when empty
    "Protocol": "https",
    "AccountName": "",
    "AccountKey": "",
    "ContainerName": "",
    // Optional - Leave empty to use the azure default url (<AccountName>.blob.core.windows.net), see the Url rules below
    "Url": ""
}
```
In the application, configure this settings part

```cs
services.AddSingleton<ICloudFileStorageManager, AzureCloudFileStorageManager>(services => new AzureCloudFileStorageManager(new AzureCloudFileStorageManagerOptions()
{
    AccountName = config.GetSection("AzureStorageSettings").GetValue<string>("AccountName"),
    AccountKey = config.GetSection("AzureStorageSettings").GetValue<string>("AccountKey"),
    ContainerName = config.GetSection("AzureStorageSettings").GetValue<string>("ContainerName"),
    Protocol = config.GetSection("AzureStorageSettings").GetValue<string>("Protocol"),
    Url = config.GetSection("AzureStorageSettings").GetValue<string>("Url")
}));  

```

### Endpoint (`Url` and `Protocol`)

- `Protocol` is `http` or `https` (case-insensitive). When empty it defaults to `https`; any other value is refused with an `ArgumentException`.
- `Url` is the blob endpoint as `host[:port]`, for example `myaccount.blob.core.windows.net` or `blob.example.com:8443`.
- `Url` may start with `http://` or `https://`; the scheme then overrides `Protocol`. Other schemes are refused.
- `Url` may carry at most one path segment, the account name of a path-style endpoint such as Azurite: `127.0.0.1:10000/devstoreaccount1`.
- A query string (`?`), a fragment (`#`), user info (`@`), a backslash or more than one path segment is refused with an `ArgumentException`.

### Download links

`GetDownloadUrl` / `GetDownloadUrlAsync` returns a SAS link signed with the account key:

- the link is read-only,
- the validity is capped at 7 days (a longer requested validity is shortened to 7 days),
- on an `https` endpoint the link only works over https.

### Listings and deletes

- Listings return one level of the folder hierarchy: the files directly in the folder and its sub-folders, with paths relative to the root directory. Folders are reported as existing.
- Reads, listings and deletes never create the container; it is created on the first write. A missing container lists as empty and deletes nothing.
- Deleting a folder removes every blob under it page by page (1000 blobs per page); `Delete` returns `false` when nothing was found.
- The root directory (the container itself or the configured `RootDirectoryPath`) cannot be deleted.
- Files are stored as append blobs. Writes are not atomic: concurrent readers can observe partial content.
- Move copies the blob server-side under a 60 second lease that is renewed while the copy runs, and gives up after 10 minutes.
- Watch is not supported (throws `NotSupportedException`).

### Path rules

Path segments are validated by Cloud.File.Storage.Manager.Common: empty or whitespace segments, segments made only of dots and spaces, control characters and percent-encoded separators are refused with an `ArgumentException`.
