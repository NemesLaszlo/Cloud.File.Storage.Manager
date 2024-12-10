# Cloud File Storage Manager

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
