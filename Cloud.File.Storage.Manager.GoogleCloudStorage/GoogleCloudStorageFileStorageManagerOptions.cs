using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.GoogleCloudStorage
{
    /// <summary>
    /// Configuration for <see cref="GoogleCloudStorageFileStorageManager"/>.
    ///
    /// Google Cloud Storage is a flat object store: there are no real folders, only object keys
    /// that contain '/' characters. The manager presents these as a virtual folder structure,
    /// the same way the Azure Blob and Amazon S3 managers do.
    ///
    /// Authentication is expressed as plain configuration values (no host file paths), so it maps
    /// onto a simple UI form and works from appsettings.json on a VM or in Kubernetes. Credentials
    /// are resolved in this order:
    ///   1. <see cref="ClientEmail"/> + <see cref="PrivateKey"/> set - a service account built from
    ///      these two discrete fields (straight from the 'client_email' and 'private_key' fields of
    ///      a service account key). <see cref="ProjectId"/> is optional.
    ///   2. <see cref="CredentialsJson"/> set - the entire service account key JSON as a single
    ///      string. Useful when the key is pulled as one blob from a secret store.
    ///   3. None of the above - Application Default Credentials (ADC). On GKE/GCE this is Workload
    ///      Identity / the metadata server, which keeps secrets out of the configuration entirely.
    ///
    /// Note: generating signed download URLs requires a credential that can sign - a service account
    /// key (options 1/2) or a Workload Identity / impersonated identity holding the
    /// iam.serviceAccounts.signBlob permission.
    /// </summary>
    public sealed class GoogleCloudStorageFileStorageManagerOptions : FileProviderOptions
    {
        public GoogleCloudStorageFileStorageManagerOptions() { }

        public GoogleCloudStorageFileStorageManagerOptions(string bucketName, string clientEmail, string privateKey,
            string projectId = null, string rootDirectory = null)
        {
            BucketName = bucketName;
            ClientEmail = clientEmail;
            PrivateKey = privateKey;
            ProjectId = projectId;
            RootDirectoryPath = rootDirectory;
        }

        /// <summary>The name of the Google Cloud Storage bucket. Required.</summary>
        public string BucketName { get; set; }

        /// <summary>
        /// The service account email (the 'client_email' field of a service account key).
        /// Provide together with <see cref="PrivateKey"/> to authenticate with a service account.
        /// </summary>
        public string ClientEmail { get; set; }

        /// <summary>
        /// The service account private key in PEM form (the 'private_key' field of a service account
        /// key). Treated as a secret. Provide together with <see cref="ClientEmail"/>.
        /// </summary>
        public string PrivateKey { get; set; }

        /// <summary>The GCP project id ('project_id'). Optional.</summary>
        public string ProjectId { get; set; }

        /// <summary>
        /// Advanced / alternative: the entire service account key JSON as a single string. Only used
        /// when <see cref="ClientEmail"/> and <see cref="PrivateKey"/> are not both set. Leave every
        /// credential field empty to use Application Default Credentials (Workload Identity).
        /// </summary>
        public string CredentialsJson { get; set; }
    }
}
