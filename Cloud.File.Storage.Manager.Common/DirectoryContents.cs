using Microsoft.Extensions.FileProviders;
using System.Collections;

namespace Cloud.File.Storage.Manager.Common
{
    public class DirectoryContents : IDirectoryContents
    {
        public bool Exists { get; }
        private IFileInfo[] _files { get; }

        public DirectoryContents(bool exists, IFileInfo[] infos)
        {
            Exists = exists;
            _files = infos;
        }

        public IEnumerator<IFileInfo> GetEnumerator()
        {
            return _files.AsEnumerable().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _files.GetEnumerator();
        }
    }
}
