# Cloud File Storage Manager

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `Physical` right now.

### Configuration

Let's add a section to the `appsettings.json`

```
"PhysicalStorageSettings": {
    "RootDirectoryPath": "",
    "AllowLinks": false
}
```
In the application, configure this settings part

```cs
services.AddSingleton<ICloudFileStorageManager, PhysicalFileStorage>(services => new PhysicalFileStorage(new PhysicalFileStorageOptions()
{
    RootDirectoryPath = config.GetSection("PhysicalStorageSettings").GetValue<string>("RootDirectoryPath"),
    AllowLinks = config.GetSection("PhysicalStorageSettings").GetValue<bool>("AllowLinks")
}));  

```

- `RootDirectoryPath`: the folder that holds the files. A relative path is resolved against the current directory.
- `AllowLinks`: false by default. Symbolic links and junctions inside the root are then refused; the root folder itself may be a link. Set it to true to follow links inside the root.

### Behaviour

- Every path stays inside the root folder; a path that resolves outside it is rejected with an `ArgumentException`.
- On top of the common path rules, segments with `:`, segments ending in a dot or a space, and Windows reserved device names (such as `CON`, `NUL`, `COM1` or `LPT1`, with or without an extension) are rejected on every platform.
- Reading a missing file, or opening a read-only local stream on it, throws a `FileNotFoundException`.
- The root folder cannot be deleted or modified.
- Moving a file creates the target folder when it does not exist.
- Download URLs are not supported: `SupportsDownloadUrl` is false and the download URL methods throw a `NotSupportedException`.
- The physical path of a file and the file name of a `FileNotFoundException` contain the full server path, so do not show them to end users.
