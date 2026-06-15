# Cloud File Storage Manager

Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete. Primarily for `Google Cloud Storage (GCS)` right now.

Google Cloud Storage is a flat object store (there are no real folders, only object keys that contain `/`). This manager presents the keys as a virtual folder hierarchy, the same way the Azure Blob and Amazon S3 managers do.

### Configuration

Let's add a section to the `appsettings.json`

```
"GoogleCloudStorageSettings": {
    "BucketName": "",
    // Service account credentials (client_email + private_key from the key file).
    "ClientEmail": "",
    "PrivateKey": "",
    // Optional
    "ProjectId": "",
    // Optional - the root folder inside the bucket where files are stored. Leave empty for the bucket root.
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

### Authentication

Credentials are resolved in this order, so the same manager works for UI-driven setup, `appsettings.json`, VMs, and Kubernetes:

1. **Discrete service account fields** — set `ClientEmail` + `PrivateKey` (optionally `ProjectId`). These come straight from the `client_email` and `private_key` fields of a service account key. This is the recommended way to wire up a UI form (no JSON blob to paste).
2. **Whole key JSON** — set `CredentialsJson` to the entire service account key JSON as one string. Useful when the key is pulled as a single blob from a secret store (Secret Manager / Key Vault).
3. **Application Default Credentials (ADC)** — leave every credential field empty. On GKE/GCE this resolves via **Workload Identity** / the metadata server, which keeps secrets out of the configuration entirely. This is the recommended enterprise setup.

```cs
// Whole-JSON alternative
new GoogleCloudStorageFileStorageManagerOptions()
{
    BucketName = "my-bucket",
    CredentialsJson = config.GetSection("GoogleCloudStorageSettings").GetValue<string>("CredentialsJson")
};

// Workload Identity / ADC - no secret in config
new GoogleCloudStorageFileStorageManagerOptions()
{
    BucketName = "my-bucket"
};
```

### Setting up a service account (least privilege)

1. Create a dedicated service account (IAM & Admin → Service Accounts).
2. Grant it **Storage Object Admin** (`roles/storage.objectAdmin`) **on the bucket only** (Cloud Storage → bucket → Permissions → Grant access). This is all the manager needs — it never creates buckets, so it requires nothing at project level.
3. For non-Workload-Identity setups, create a JSON key (Keys → Add key → Create new key → JSON) and read `client_email` / `private_key` from it into the options.

### Signed download URLs

`GetDownloadUrl` / `GetDownloadUrlAsync` produces a time-limited signed URL. Signing requires a credential that can sign:

- a service account key (options 1/2 above) — signs locally, no extra permission, or
- a Workload Identity / impersonated identity holding `iam.serviceAccounts.signBlob` — signs via the IAM API.

```cs
var url = await manager.GetDownloadUrlAsync("folder/file.txt", TimeSpan.FromHours(1));
```

### Notes

- **Append** is emulated (GCS has no native append): the existing object is downloaded, appended to, and re-uploaded.
- **Move** is a server-side copy followed by a delete.
- **Watch** is not supported (throws `NotSupportedException`), consistent with the Amazon S3 and Azure Blob managers.
- Large reads are streamed through a temporary file (deleted on close) to keep memory usage flat.
