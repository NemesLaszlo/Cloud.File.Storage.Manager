using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.Tests.AzureBlob
{
    /// <summary>
    /// Runs the shared provider suite against Azurite, with a root folder.
    /// </summary>
    [TestClass]
    public class AzuriteFileProvider : TestBase
    {
        private static TestContext context;
        private static readonly string containerName = Azurite.NewContainerName();

        [ClassInitialize]
        public static void Init(TestContext _context)
        {
            context = _context;
        }

        [ClassCleanup]
        public static async Task Cleanup()
        {
            await Azurite.DeleteContainer(containerName);
        }

        public AzuriteFileProvider() : base(context) { }

        protected override ICloudFileStorageManager GetProvider()
        {
            return Azurite.CreateProvider(containerName, "suite/root");
        }
    }
}
