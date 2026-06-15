using Cloud.File.Storage.Manager.Common;
using Cloud.File.Storage.Manager.GoogleCloudStorage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Cloud.File.Storage.Manager.Tests
{
    [TestClass]
    public class GoogleCloudStorageFileStorageManagerTests : TestBase
    {
        private static TestContext context;
        [ClassInitialize]
        public static void Init(TestContext _context)
        {
            context = _context;
        }
        public GoogleCloudStorageFileStorageManagerTests() : base(context) { }
        protected override ICloudFileStorageManager GetProvider()
        {
            return new GoogleCloudStorageFileStorageManager(new GoogleCloudStorageFileStorageManagerOptions()
            {
                BucketName = Settings.GoogleCloudStorageManager_BucketName,
                ClientEmail = Settings.GoogleCloudStorageManager_ClientEmail,
                PrivateKey = Settings.GoogleCloudStorageManager_PrivateKey,
                ProjectId = Settings.GoogleCloudStorageManager_ProjectId,
                RootDirectoryPath = Settings.GoogleCloudStorageManager_RootPath
            });
        }
    }
}
