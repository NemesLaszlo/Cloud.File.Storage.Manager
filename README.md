# Cloud File Storage Manager

## Azure Blob Storage

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `Azure (Azure Blob Storage)` right now.

### Configuration

Let's add a section to the `appsettings.json`

```
"AzureStorageSettings": {
    "AccountName": "",
    "AccountKey": "",
    "ContainerName": ""
}
```
In the application, configure this settings part

```cs
services.AddSingleton<ICloudFileStorageManager, AzureCloudFileStorageManager>(services => new AzureCloudFileStorageManager(new AzureCloudFileStorageManagerOptions()
{
    AccountName = config.GetSection("AzureStorageSettings").GetValue<string>("AccountName"),
    AccountKey = config.GetSection("AzureStorageSettings").GetValue<string>("AccountKey"),
    ContainerName = config.GetSection("AzureStorageSettings").GetValue<string>("ContainerName")
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
