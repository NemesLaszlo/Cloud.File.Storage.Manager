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

### Behaviour

- Directory listings return one level: the files directly in the folder plus its sub-folders (from the S3 common prefixes). Folder marker keys ending with `/` are not listed as files.
- File info is resolved with a single `ListObjectsV2` request for the exact key, so only `s3:ListBucket` is needed; `s3:GetObject` and `s3:ListBucketVersions` are not required for it.
- Deleting a folder removes every key under its prefix in pages of up to 1000 keys. If S3 reports any failed key, an `IOException` is thrown naming the first failure.
- Delete returns `true` when something was deleted and `false` when nothing existed under the path.
- Moving copies the object with a single `CopyObject` request and then deletes the original; S3 limits this to objects of at most 5 GB.
- Use `ReadFileVersionAsync(versionId, pathSegments)` to read a specific object version with root-relative path segments; a null or empty version id reads the current version. The older `ReadFileAsync(versionId, absolutePathSegments)` overload is obsolete and only accepts paths under the configured root. On a variable typed as `AmazonS3FileStorageManager`, a call like `ReadFileAsync("dir", "file.txt")` binds to that overload with `"dir"` as the version id; use the `ICloudFileStorageManager` interface or `ReadFileVersionAsync` for plain reads.
- Request presigned download URLs for at most 7 days, the S3 limit for signature version 4. The validity is not capped by the library: for a longer validity the AWS SDK falls back to a legacy signature version 2 URL, which buckets and regions without signature version 2 support reject.
- Path segments `.` and `..` are rejected.
- The configured root directory itself cannot be deleted, moved or overwritten.
