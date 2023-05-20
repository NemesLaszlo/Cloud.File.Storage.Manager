# Cloud File Storage Manager

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
