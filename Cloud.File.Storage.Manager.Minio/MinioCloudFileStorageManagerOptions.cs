using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.Minio
{
    public class MinioCloudFileStorageManagerOptions : FileProviderOptions
    {
        /// <summary>
        /// The endpoint as host[:port], optionally prefixed with a scheme: https://host[:port] connects over HTTPS and
        /// http://host[:port] over HTTP, overriding <see cref="UseSSL"/>; without a scheme <see cref="UseSSL"/> decides.
        /// </summary>
        public string HostAndPort { get; set; }
        /// <summary>
        /// Connects over HTTPS when true. Defaults to false for compatibility; set it to true in production.
        /// Ignored when <see cref="HostAndPort"/> carries a scheme.
        /// </summary>
        public bool UseSSL { get; set; }
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }
        public string BucketName { get; set; }

        public MinioCloudFileStorageManagerOptions() { }

        public MinioCloudFileStorageManagerOptions(string hostAndPort, bool useSSL, string accessKey, string secretKey, string bucketName)
        {
            HostAndPort = hostAndPort;
            UseSSL = useSSL;
            AccessKey = accessKey;
            SecretKey = secretKey;
            BucketName = bucketName;
        }
    }
}
