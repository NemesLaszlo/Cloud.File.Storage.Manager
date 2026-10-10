namespace Cloud.File.Storage.Manager.Box
{
    internal static class BoxUploadBuffer
    {
        internal static FileStream CreateTempFile()
        {
            return new FileStream(
                Path.GetTempFileName(),
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        }

        // Copies the remaining content of a (possibly non-seekable) stream into a seekable temp file.
        internal static async Task<Stream> SpoolAsync(Stream source)
        {
            var temp = CreateTempFile();
            try
            {
                await source.CopyToAsync(temp);
                temp.Position = 0;
                return temp;
            }
            catch
            {
                await temp.DisposeAsync();
                throw;
            }
        }

        internal static async Task<Stream> CombineAsync(Stream existingContent, Stream appendedContent)
        {
            var temp = CreateTempFile();
            try
            {
                await existingContent.CopyToAsync(temp);
                await appendedContent.CopyToAsync(temp);
                temp.Position = 0;
                return temp;
            }
            catch
            {
                await temp.DisposeAsync();
                throw;
            }
        }
    }
}
