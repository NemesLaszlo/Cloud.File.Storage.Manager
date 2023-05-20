using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.Minio
{
    public class MinioCloudFileStorageManagerOptions : FileProviderOptions
    {
        public string HostAndPort { get; set; }
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
