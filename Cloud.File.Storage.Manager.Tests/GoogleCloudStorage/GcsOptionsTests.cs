using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.FileProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Cloud.File.Storage.Manager.GoogleCloudStorage;

namespace Cloud.File.Storage.Manager.Tests.GoogleCloudStorage
{
    [TestClass]
    public class GcsOptionsTests
    {
        private const string ClientEmail = "wfp-test@example-project.iam.gserviceaccount.com";
        private static readonly string pem = createPem();

        private static string createPem()
        {
            using (var rsa = RSA.Create(2048))
                return rsa.ExportPkcs8PrivateKeyPem().Replace("\r\n", "\n");
        }

        private static string escapedLf => pem.Replace("\n", "\\n");
        private static string escapedCrLf => pem.Replace("\n", "\\r\\n");
        private static string escapedCr => pem.Replace("\n", "\\r");

        private static GoogleCloudStorageFileStorageManagerOptions options(string privateKey, string root = null)
        {
            return new GoogleCloudStorageFileStorageManagerOptions("wfp-test-bucket", ClientEmail, privateKey, "example-project", root);
        }

        [TestMethod]
        public void NormalizePem_LeavesEmptyAndValidKeysUnchanged()
        {
            Assert.IsNull(GoogleCloudStorageFileStorageManager.NormalizePem(null));
            Assert.AreEqual("", GoogleCloudStorageFileStorageManager.NormalizePem(""));
            Assert.AreSame(pem, GoogleCloudStorageFileStorageManager.NormalizePem(pem));
            var crlf = pem.Replace("\n", "\r\n");
            Assert.AreEqual(crlf, GoogleCloudStorageFileStorageManager.NormalizePem(crlf));
        }

        [TestMethod]
        public void NormalizePem_TurnsEscapedNewlinesIntoNewlines()
        {
            Assert.AreEqual(pem, GoogleCloudStorageFileStorageManager.NormalizePem(escapedLf));
            Assert.AreEqual(pem, GoogleCloudStorageFileStorageManager.NormalizePem(escapedCrLf));
            Assert.AreEqual(pem, GoogleCloudStorageFileStorageManager.NormalizePem(escapedCr));
        }

        [TestMethod]
        public void NormalizePem_IsIdempotent()
        {
            foreach (var key in new[] { pem, escapedLf, escapedCrLf, escapedCr })
            {
                var once = GoogleCloudStorageFileStorageManager.NormalizePem(key);
                Assert.AreEqual(once, GoogleCloudStorageFileStorageManager.NormalizePem(once));
            }
        }

        [TestMethod]
        public void EscapedKey_IsRejectedByTheCredentialStackWithoutNormalisation()
        {
            Assert.ThrowsExactly<FormatException>(() => new ServiceAccountCredential.Initializer(ClientEmail).FromPrivateKey(escapedLf));
        }

        [TestMethod]
        public void Provider_BuildsWithRealAndEscapedKeys()
        {
            foreach (var key in new[] { pem, pem.Replace("\n", "\r\n"), escapedLf, escapedCrLf, escapedCr })
                Assert.IsNotNull(new GoogleCloudStorageFileStorageManager(options(key)));
        }

        [TestMethod]
        public void Provider_DoesNotModifyThePrivateKeyOption()
        {
            var provider = new GoogleCloudStorageFileStorageManager(options(escapedLf));
            Assert.AreEqual(escapedLf, provider.Options.PrivateKey);
            Assert.AreEqual(escapedLf, (string)provider.ToJson()["PrivateKey"]);

            var restored = new GoogleCloudStorageFileStorageManager(provider.ToJson().ToObject<GoogleCloudStorageFileStorageManagerOptions>());
            Assert.AreEqual(escapedLf, restored.Options.PrivateKey);
        }

        [TestMethod]
        public async Task Delete_ProviderRootIsRefusedWithoutNetwork()
        {
            var provider = new Probe(options(escapedLf));
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAbsolute());
            Assert.AreEqual("The provider root cannot be deleted or modified.", ex.Message);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.DeleteAsync(""));

            var rooted = new Probe(options(escapedLf, "base/inner"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => rooted.DeleteAbsolute("base", "inner"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => rooted.DeleteAsync(""));
        }

        [TestMethod]
        public async Task FileInfo_ZeroSegmentsIsNotFoundWithoutNetwork()
        {
            var info = await new Probe(options(escapedLf)).FileInfoAbsolute();
            Assert.IsFalse(info.Exists);
        }

        private static string serviceAccountJson() => new JObject
        {
            ["type"] = "service_account",
            ["project_id"] = "example-project",
            ["private_key_id"] = "0",
            ["private_key"] = pem,
            ["client_email"] = ClientEmail,
            ["client_id"] = "0",
            ["token_uri"] = "https://oauth2.googleapis.com/token"
        }.ToString();

        [TestMethod]
        public void UsesApplicationDefaultCredentials_FalseForExplicitCredentials()
        {
            Assert.IsFalse(new GoogleCloudStorageFileStorageManager(options(pem)).UsesApplicationDefaultCredentials);
            var json = new GoogleCloudStorageFileStorageManagerOptions { BucketName = "wfp-test-bucket", CredentialsJson = serviceAccountJson() };
            Assert.IsFalse(new GoogleCloudStorageFileStorageManager(json).UsesApplicationDefaultCredentials);
        }

        [TestMethod]
        public void UsesApplicationDefaultCredentials_TrueWithoutCredentials()
        {
            const string variable = "GOOGLE_APPLICATION_CREDENTIALS";
            var previous = Environment.GetEnvironmentVariable(variable);
            var file = Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(file, serviceAccountJson());
                Environment.SetEnvironmentVariable(variable, file);
                var provider = new GoogleCloudStorageFileStorageManager(new GoogleCloudStorageFileStorageManagerOptions { BucketName = "wfp-test-bucket" });
                Assert.IsTrue(provider.UsesApplicationDefaultCredentials);
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, previous);
                System.IO.File.Delete(file);
            }
        }

        private class Probe : GoogleCloudStorageFileStorageManager
        {
            public Probe(GoogleCloudStorageFileStorageManagerOptions options) : base(options) { }
            public Task<bool> DeleteAbsolute(params string[] absolutePathSegments) => deleteAsync(absolutePathSegments);
            public Task<IFileInfo> FileInfoAbsolute(params string[] absolutePathSegments) => getFileInfoAsync(absolutePathSegments);
        }
    }
}
