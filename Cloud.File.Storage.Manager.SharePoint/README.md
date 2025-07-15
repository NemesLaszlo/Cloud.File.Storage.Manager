# Cloud File Storage Manager

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
    // Optional: The folder name where the files will be stored
    "WorkFolderName": ""
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
    WorkFolderName = config.GetSection("SharePointStorageSettings").GetValue<string>("WorkFolderName")
}));  

```

## Azure App Registration configuration

Create an Azure AD app registration:
1. Go to Azure Portal > App registrations
2. Create a new registration
3. Add API permissions: `Sites.ReadWrite.All` - Microsoft Graph (Manage / API permissions section)
4. Create a client secret

Then, you have all the necessary values to connect your app to SharePoint and perform actions there.