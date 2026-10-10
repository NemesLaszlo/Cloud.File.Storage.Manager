# Cloud File Storage Manager

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

### Behaviour

- Folders are key prefixes. A listing returns one level: the files and the subfolders directly under the folder.
- Deleting a folder removes every object under its prefix, in batches; a failed batch raises an `IOException`. The storage root cannot be deleted, moved or overwritten.
- The bucket is created on the first write; reads and listings report a missing bucket as not found.
- `GetDownloadUrl` returns a presigned GET URL. The validity is capped at 7 days (the S3 maximum), and the link points at `HostAndPort`, so use an address the link's recipients can reach.
- `HostAndPort` may carry a scheme: `https://host:9000` connects over HTTPS and `http://host:9000` over HTTP, whatever `UseSSL` says. Without a scheme `UseSSL` decides.
- `UseSSL` defaults to `false` for compatibility; set it to `true` (or use an `https://` address) in production.
