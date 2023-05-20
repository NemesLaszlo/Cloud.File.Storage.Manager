using Cloud.File.Storage.Manager.Common;

namespace Cloud.File.Storage.Manager.Physical
{
    public class PhysicalFileStorageOptions : FileProviderOptions
    {
        public override string RootDirectoryPath
        {
            get => string.Join(Path.DirectorySeparatorChar.ToString(), base.RootDirectorySegments ?? new string[0]);
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    this.RootDirectorySegments = new string[0];
                    return;
                }
                else
                {
                    this.RootDirectorySegments = PhysicalFileStorage.getPathSegments(value);
                }

            }
        }
    }
}
