using System;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Cloud.File.Storage.Manager.AzureBlob;

namespace Cloud.File.Storage.Manager.Tests.AzureBlob
{
    /// <summary>
    /// Connection details of a local Azurite blob emulator:
    /// docker run -d -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0
    /// Tests are inconclusive when it is not running.
    /// </summary>
    internal static class Azurite
    {
        public const string AccountName = "devstoreaccount1";
        public const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
        public const string Url = "127.0.0.1:10000/devstoreaccount1";

        private static readonly Lazy<bool> available = new Lazy<bool>(probe);

        public static string NewContainerName() => $"wfp-test-{Guid.NewGuid():N}";

        public static void RequireRunning()
        {
            if (!available.Value)
                Assert.Inconclusive("Azurite is not running on 127.0.0.1:10000");
        }

        public static AzureCloudFileStorageManager CreateProvider(string containerName, string rootDirectoryPath = null)
        {
            RequireRunning();
            return new AzureCloudFileStorageManager(Options(containerName, rootDirectoryPath));
        }

        public static AzureCloudFileStorageManagerOptions Options(string containerName, string rootDirectoryPath = null)
        {
            return new AzureCloudFileStorageManagerOptions
            {
                AccountName = AccountName,
                AccountKey = AccountKey,
                ContainerName = containerName,
                Protocol = "http",
                Url = Url,
                RootDirectoryPath = rootDirectoryPath
            };
        }

        public static BlobContainerClient Container(string containerName, BlobClientOptions options = null)
        {
            return new BlobContainerClient(new Uri($"http://{Url}/{containerName}"),
                new StorageSharedKeyCredential(AccountName, AccountKey), options);
        }

        public static async Task DeleteContainer(string containerName)
        {
            if (available.Value)
                await Container(containerName).DeleteIfExistsAsync();
        }

        private static bool probe()
        {
            try
            {
                var options = new BlobClientOptions();
                options.Retry.MaxRetries = 0;
                options.Retry.NetworkTimeout = TimeSpan.FromSeconds(3);
                var service = new BlobServiceClient(new Uri($"http://{Url}"), new StorageSharedKeyCredential(AccountName, AccountKey), options);
                service.GetProperties();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
