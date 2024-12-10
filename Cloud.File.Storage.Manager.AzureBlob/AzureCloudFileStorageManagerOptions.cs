using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.AzureBlob
{
    public class AzureCloudFileStorageManagerOptions : FileProviderOptions
    {
        public string AccountName { get; set; }
        public string AccountKey { get; set; }
        public string ContainerName { get; set; }
        public string Protocol { get; set; }
        public string Url { get; set; }
    }
}
