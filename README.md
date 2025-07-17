# Cloud File Storage Manager

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

### Configuration

Let's add a section to the `appsettings.json`

```
"AmazonS3torageSettings": {
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
    IsAnonymus = config.GetSection("AmazonS3torageSettings").GetValue<bool>("IsAnonymus"),
    AccessKey = config.GetSection("AmazonS3torageSettings").GetValue<string>("AccessKey"),
    SecretKey = config.GetSection("AmazonS3torageSettings").GetValue<string>("SecretKey"),
    BucketName = config.GetSection("AmazonS3torageSettings").GetValue<string>("BucketName"),
    BucketRegion = config.GetSection("AmazonS3torageSettings").GetValue<string>("BucketRegion")
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
services.AddSingleton<ICloudFileStorageManager, BoxFileStorageManager>(services => new BoxFileStorageManager(new BoxFileStorageManagerOptions()
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
    ClientSecret = config.GetSection("SharePointStorageSettings").GetValue<string>("ClientSecret")
    WorkFolderPath = config.GetSection("SharePointStorageSettings").GetValue<string>("WorkFolderPath")
}));  

```
