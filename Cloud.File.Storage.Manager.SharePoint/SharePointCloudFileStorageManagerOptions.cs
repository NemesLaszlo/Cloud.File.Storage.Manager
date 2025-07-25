﻿using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.SharePoint
{
    public class SharePointCloudFileStorageManagerOptions : FileProviderOptions
    {
        public SharePointCloudFileStorageManagerOptions(string siteUrl, string documentLibraryName, string tenantId, string clientId, 
            string clientSecret, string workFolderPath)
        {
            SiteUrl = siteUrl;
            DocumentLibraryName = documentLibraryName;
            TenantId = tenantId;
            ClientId = clientId;
            ClientSecret = clientSecret;
            WorkFolderPath = workFolderPath;
        }

        public SharePointCloudFileStorageManagerOptions() { }

        public string SiteUrl { get; set; } // The SharePoint site URL (e.g., https://tenant.sharepoint.com/sites/sitename)
        public string DocumentLibraryName { get; set; }
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        // Optional path, like: "MainFolder/WorkFolder/Test"
        public string WorkFolderPath { get; set; }
    }
}
