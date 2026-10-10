using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.AzureBlob
{
    public class AzureCloudFileStorageManagerOptions : FileProviderOptions
    {
        public string AccountName { get; set; }
        public string AccountKey { get; set; }
        public string ContainerName { get; set; }
        /// <summary>The endpoint scheme: <c>http</c> or <c>https</c>. Defaults to <c>https</c> when empty.</summary>
        public string Protocol { get; set; }
        /// <summary>
        /// The blob endpoint as host[:port], optionally followed by the account name of a path-style endpoint (for example Azurite).
        /// A scheme prefix (http:// or https://) overrides <see cref="Protocol"/>. Defaults to the account's blob.core.windows.net host when empty.
        /// </summary>
        public string Url { get; set; }
    }
}
