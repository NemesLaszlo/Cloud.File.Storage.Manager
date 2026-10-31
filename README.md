# Cloud File Storage Manager

## Common behaviour

### Requirements

Every package targets .NET 10 (`net10.0`). The versions in the table below are the first .NET 10 releases; the previous versions (Common 3.1.0 and the providers listed with it) stay on .NET 8. The public API is unchanged.

Every provider implements `ICloudFileStorageManager` on top of the shared `CloudFileStorageManager<T>` base class in `Cloud.File.Storage.Manager.Common`, so the rules below apply to all of them unless a provider is named.

### Paths

- Paths are given as one string or as segments. Every segment is split on both `/` and `\`, so `a/b` and `a\b` address the same nested path.
- A segment is rejected with an `ArgumentException` when it is empty or whitespace, is `.` or `..` (or consists only of dots and spaces), contains a control character, or does so after percent-decoding. Percent-encoded separators such as `%2F` are rejected as well.
- Physical and SharePoint also reject `:` in a segment.
- Physical also rejects Windows device names (`CON`, `PRN`, `AUX`, `NUL`, `COM0`-`COM9`, `LPT0`-`LPT9` and their superscript-digit forms, with or without an extension) and names that end in a dot or a space, on every operating system.
- When a `RootDirectoryPath` is configured, every path is resolved under it and cannot leave it.

### The root

- Deleting, moving or updating the root itself is refused with an `InvalidOperationException`.
- Reading the root as a file or asking a download URL for it is refused with an `ArgumentException`. File info of the root reports a missing file.

### Listings and deletes

- `GetDirectoryContentsAsync` lists one level: the files directly in the folder and one directory entry for each subfolder. The returned entries can be passed back to delete, move or list calls.
- Deleting a folder removes everything under it. The object stores (Amazon S3, Minio, Azure Blob Storage, Google Cloud Storage) delete in pages of 1000, so folders with more than 1000 objects are removed completely.

### Download URLs

`SupportsDownloadUrl` tells whether `GetDownloadUrlAsync` can be used. When it is false the call throws `NotSupportedException`.

| Provider | Download URL lifetime |
|----------|-----------------------|
| Azure Blob Storage | Signed with the requested validity, up to 7 days (longer requests are shortened to 7 days) |
| Amazon S3 | Signed with the requested validity, up to 7 days |
| Google Cloud Storage | Signed with the requested validity, up to 7 days; needs a credential that can sign |
| Minio | Signed with the requested validity, up to 7 days (longer requests are shortened to 7 days) |
| Box | About 15 minutes, set by Box; the requested validity is ignored |
| SharePoint | About one hour, set by Microsoft Graph; the requested validity is ignored |
| Physical | Not supported (`SupportsDownloadUrl` is false) |

### Package versions

| Package | Version |
|---------|---------|
| Cloud.File.Storage.Manager.Common | 4.0.0 |
| Cloud.File.Storage.Manager.Physical | 3.0.0 |
| Cloud.File.Storage.Manager.Minio | 3.0.0 |
| Cloud.File.Storage.Manager.AmazonS3 | 3.0.0 |
| Cloud.File.Storage.Manager.AzureBlob | 4.0.0 |
| Cloud.File.Storage.Manager.GoogleCloudStorage | 2.0.0 |
| Cloud.File.Storage.Manager.Box | 3.0.0 |
| Cloud.File.Storage.Manager.SharePoint | 3.0.0 |

## Azure Blob Storage

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `Azure (Azure Blob Storage)` right now.

### Configuration

Let's add a section to the `appsettings.json`

```
"AzureStorageSettings": {
    "Protocol": "https",
    "AccountName": "",
    "AccountKey": "",
    "ContainerName": "",
    // Optional - Leave empty to use azure default url, otherwise provide the hostname (without the protocol and path) of the endpoint
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

## Amazon S3

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `Amazon S3` right now.

Built on the AWS SDK for .NET v4 (`AWSSDK.S3` 4.x). Checksums are computed and validated only where S3 requires them, so S3 compatible servers keep working. The SDK's own `AWS_ENDPOINT_URL_S3` environment variable points the client at another endpoint (for example LocalStack); presigned download URLs then use that endpoint's scheme.

### Configuration

Let's add a section to the `appsettings.json`

```
"AmazonS3StorageSettings": {
    "IsAnonymus": false,
    "AccessKey": "",
    "SecretKey": "",
    "BucketName": "",
    "BucketRegion": ""
}
```
In the application, configure this settings part

```cs
services.AddSingleton<ICloudFileStorageManager, AmazonS3FileStorageManager>(services => new AmazonS3FileStorageManager(new AmazonS3FileStorageManagerOptions()
{
    IsAnonymus = config.GetSection("AmazonS3StorageSettings").GetValue<bool>("IsAnonymus"),
    AccessKey = config.GetSection("AmazonS3StorageSettings").GetValue<string>("AccessKey"),
    SecretKey = config.GetSection("AmazonS3StorageSettings").GetValue<string>("SecretKey"),
    BucketName = config.GetSection("AmazonS3StorageSettings").GetValue<string>("BucketName"),
    BucketRegion = config.GetSection("AmazonS3StorageSettings").GetValue<string>("BucketRegion")
}));  

```

## Minio (Amazon S3 Compatible Cloud Storage)

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `Minio .NET SDK for Amazon S3 Compatible Cloud Storage.` right now.

### Configuration

Let's add a section to the `appsettings.json`

```
"MinioStorageSettings": {
    "HostAndPort": "",
    "UseSSL": false,
    "AccessKey": "",
    "SecretKey": "",
    "BucketName": ""
}
```
In the application, configure this settings part

```cs
services.AddSingleton<ICloudFileStorageManager, MinioCloudFileStorageManager>(services => new MinioCloudFileStorageManager(new MinioCloudFileStorageManagerOptions()
{
    HostAndPort = config.GetSection("MinioStorageSettings").GetValue<string>("HostAndPort"),
    UseSSL = config.GetSection("MinioStorageSettings").GetValue<bool>("UseSSL"),
    AccessKey = config.GetSection("MinioStorageSettings").GetValue<string>("AccessKey"),
    SecretKey = config.GetSection("MinioStorageSettings").GetValue<string>("SecretKey"),
    BucketName = config.GetSection("MinioStorageSettings").GetValue<string>("BucketName")
}));  

```

## Physical

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `Physical` right now.

### Configuration

Let's add a section to the `appsettings.json`

```
"PhysicalStorageSettings": {
    "RootDirectoryPath": "",
}
```
In the application, configure this settings part

```cs
services.AddSingleton<ICloudFileStorageManager, PhysicalFileStorage>(services => new PhysicalFileStorage(new PhysicalFileStorageOptions()
{
    RootDirectoryPath = config.GetSection("PhysicalStorageSettings").GetValue<string>("RootDirectoryPath")
}));  

```

## Box

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `Box` right now.

Built on the Box .NET SDK Gen (`Box.V2.Core` 10.x, namespace `Box.Sdk.Gen`), which renews the access token itself. Box API errors are thrown as `IOException` with the SDK's `BoxSdkException` as the inner exception.

### Configuration

Let's add a section to the `appsettings.json`

```
"BoxStorageSettings": {
    "ClientId": "",
    "ClientSecret": "",
    "EnterpriseId": "",
    "PublicKeyId": "",
    "PrivateKey": "",
    "Passphrase": "",
    "RootFolderId": "0"
}
```
In the application, configure this settings part

```cs
services.AddSingleton<ICloudFileStorageManager, BoxCloudFileStorageManager>(services => new BoxCloudFileStorageManager(new BoxCloudFileStorageManagerOptions()
{
    ClientId = config.GetSection("BoxStorageSettings").GetValue<string>("ClientId"),
    ClientSecret = config.GetSection("BoxStorageSettings").GetValue<string>("ClientSecret"),
    EnterpriseId = config.GetSection("BoxStorageSettings").GetValue<string>("EnterpriseId"),
    PublicKeyId = config.GetSection("BoxStorageSettings").GetValue<string>("PublicKeyId"),
    PrivateKey = config.GetSection("BoxStorageSettings").GetValue<string>("PrivateKey"),
    Passphrase = config.GetSection("BoxStorageSettings").GetValue<string>("Passphrase"),
    RootFolderId = config.GetSection("BoxStorageSettings").GetValue<string>("RootFolderId")
}));

```

## SharePoint

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `SharePoint` right now.

Built on Microsoft Graph SDK 6 (`Microsoft.Graph` 6.x, `Microsoft.Graph.Core` 4.x). An application that also uses `Microsoft.Graph` directly is moved to the 6.x line by this dependency.

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

## Google Cloud Storage

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `Google Cloud Storage (GCS)` right now.

Built on `Google.Cloud.Storage.V1` 5.x. Setting `STORAGE_EMULATOR_HOST` (for example `http://localhost:4443`) points the client at an emulator such as fake-gcs-server; signed download URLs still point at Google.

### Configuration

Let's add a section to the `appsettings.json`

```
"GoogleCloudStorageSettings": {
    "BucketName": "",
    // Service account credentials (client_email + private_key from the key file)
    "ClientEmail": "",
    "PrivateKey": "",
    // Optional
    "ProjectId": "",
    // Optional: the root folder inside the bucket where the files will be stored. Leave empty for the bucket root.
    "RootDirectoryPath": ""
}
```
In the application, configure this settings part

```cs
services.AddSingleton<ICloudFileStorageManager, GoogleCloudStorageFileStorageManager>(services => new GoogleCloudStorageFileStorageManager(new GoogleCloudStorageFileStorageManagerOptions()
{
    BucketName = config.GetSection("GoogleCloudStorageSettings").GetValue<string>("BucketName"),
    ClientEmail = config.GetSection("GoogleCloudStorageSettings").GetValue<string>("ClientEmail"),
    PrivateKey = config.GetSection("GoogleCloudStorageSettings").GetValue<string>("PrivateKey"),
    ProjectId = config.GetSection("GoogleCloudStorageSettings").GetValue<string>("ProjectId"),
    RootDirectoryPath = config.GetSection("GoogleCloudStorageSettings").GetValue<string>("RootDirectoryPath")
}));  

```

Credentials are resolved in this order: (1) `ClientEmail` + `PrivateKey` discrete fields, (2) `CredentialsJson` (the whole service account key JSON as one string), or (3) Application Default Credentials when all are empty — on GKE/GCE this uses Workload Identity / the metadata server, keeping secrets out of the configuration. Grant the service account `Storage Object Admin` on the bucket only. See the `Cloud.File.Storage.Manager.GoogleCloudStorage` project README for the full details (authentication modes, least-privilege setup, and signed URLs).
