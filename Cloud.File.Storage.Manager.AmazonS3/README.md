# Cloud File Storage Manager

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