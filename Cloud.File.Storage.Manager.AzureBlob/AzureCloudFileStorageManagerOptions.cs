using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.AzureBlob
{
    public class AzureCloudFileStorageManagerOptions : FileProviderOptions
    {
        public string ConnectionString { get; set; }
        public string ContainerName { get; set; }
    }
}
