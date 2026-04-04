using System.Collections.Generic;
using System.IO;

namespace EmoTracker.Data.Packages
{
    public class DirectoryPackageSource : IGamePackageSource
    {
        private string mPath;

        public DirectoryPackageSource(string path)
        {
            mPath = path;
        }

        public string PackPath { get { return mPath; } }

        public Stream Open(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                string resolvedPath = Path.Combine(mPath, path);

                if (!File.Exists(resolvedPath))
                    return null;

                return File.OpenRead(resolvedPath);
            }

            return null;
        }

        public void AcquireStorage()
        {
        }

        public void ReleaseStorage()
        {
        }

        string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            while (path.StartsWith("/"))
            {
                path = path.Substring(1);
            }

            return path;
        }

        void BuildFileList(string root, List<string> files, string prefix = null)
        {
            if (prefix == null)
                prefix = root;

            try
            {
                foreach (string directory in Directory.EnumerateDirectories(root))
                {
                    BuildFileList(directory, files, prefix);
                }

                foreach (string file in Directory.EnumerateFiles(root))
                {
                    files.Add(NormalizePath(file.Replace(prefix, "").Replace(Path.DirectorySeparatorChar, '/')));
                }
            }
            catch
            {
            }
        }

        public IEnumerable<string> Files
        {
            get
            {
                List<string> files = new List<string>();
                BuildFileList(mPath, files);

                return files;
            }            
        }
    }
}
