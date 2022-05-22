
namespace Cloud.File.Storage.Manager.Common
{
    public class FileProviderOptions
    {
        private string[] rootDirSegments;

        public virtual string RootDirectoryPath
        {
            get => RootDirectorySegments == null ? "" : string.Join("/", RootDirectorySegments) + "/";
            set => RootDirectorySegments = value?.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public string[] RootDirectorySegments
        {
            get { return rootDirSegments ?? new string[0]; }
            set { rootDirSegments = value; }
        }
    }
}
