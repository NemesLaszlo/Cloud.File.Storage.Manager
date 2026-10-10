using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.Tests
{
    /// <summary>
    /// Runs the shared provider suite against the S3 endpoint described in <see cref="LocalStackS3"/>, without a root folder.
    /// </summary>
    [TestClass]
    public class AmazonS3LocalStackSuite : TestBase
    {
        public AmazonS3LocalStackSuite() : base(null) { }

        protected override ICloudFileStorageManager GetProvider() => LocalStackS3.Provider();
    }

    /// <summary>
    /// Runs the shared provider suite against the S3 endpoint described in <see cref="LocalStackS3"/>, with a root folder.
    /// </summary>
    [TestClass]
    public class AmazonS3LocalStackRootSuite : TestBase
    {
        private static readonly string RootDirectoryPath = $"wfp-suite-root-{Guid.NewGuid():N}";

        public AmazonS3LocalStackRootSuite() : base(null) { }

        protected override ICloudFileStorageManager GetProvider() => LocalStackS3.Provider(RootDirectoryPath);
    }
}
