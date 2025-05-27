using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.Box
{
    public class BoxCloudFileStorageManagerOptions : FileProviderOptions
    {
        public BoxCloudFileStorageManagerOptions(string clientId, string clientSecret, string enterpriseId, string publicKeyId,
            string privateKey, string passphrase, string rootFolderId)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            EnterpriseId = enterpriseId;
            PublicKeyId = publicKeyId;
            PrivateKey = privateKey;
            Passphrase = passphrase;
            RootFolderId = rootFolderId;
        }

        public BoxCloudFileStorageManagerOptions() { }

        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string EnterpriseId { get; set; }
        public string PublicKeyId { get; set; }
        public string PrivateKey { get; set; }
        public string Passphrase { get; set; }
        public string RootFolderId { get; set; }
    }
}
