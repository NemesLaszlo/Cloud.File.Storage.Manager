using Amazon.S3;
using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.AmazonS3
{
    public sealed class AmazonS3FileStorageManagerOptions : FileProviderOptions
    {
        public bool IsAnonymus { get; set; }
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }

        public string BucketName { get; set; }
        public string BucketRegion { get; set; }
        public S3StorageClass StorageClassForNewFiles { get; set; } = S3StorageClass.Standard;

        public AmazonS3FileStorageManagerOptions() { }

        public AmazonS3FileStorageManagerOptions(string rootDirectory, string accessKey, string secretKey, string bucketName, 
            string bucketRegion, S3StorageClass storageClassForNewFiles = null)
        {
            RootDirectoryPath = rootDirectory;
            IsAnonymus = string.IsNullOrEmpty(accessKey);
            AccessKey = accessKey;
            SecretKey = secretKey;
            BucketName = bucketName;
            BucketRegion = bucketRegion;
            StorageClassForNewFiles = storageClassForNewFiles ?? S3StorageClass.Standard;
        }
    }
}
