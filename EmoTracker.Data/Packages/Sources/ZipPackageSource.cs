using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace EmoTracker.Data.Packages
{
    public class ZipPackageSource : IGamePackageSource
    {
        private ZipArchive mArchive;
        private string mPath;

        public string ArchivePath
        {
            get { return mPath; }
        }

        public IEnumerable<string> Files
        {
            get
            {
                List<string> files = new List<string>();

                if (mArchive != null)
                {
                    foreach (ZipArchiveEntry entry in mArchive.Entries)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.FullName) && !entry.FullName.EndsWith("/"))
                            files.Add(entry.FullName);
                    }

                    files.Sort(FileListSort);
                }

                return files;
            }
        }

        public string PackPath { get { return mPath; } }

        private int FileListSort(string x, string y)
        {
            

            if (Path.GetDirectoryName(x) == null && Path.GetDirectoryName(y) != null)
                return 1;
            else if (Path.GetDirectoryName(x) != null && Path.GetDirectoryName(y) == null)
                return -1;

            return x.CompareTo(y);
        }

        public ZipPackageSource(string path)
        {
            mPath = path;
            AcquireStorage();
        }

       

        public Stream Open(string path)
        {
            if (mArchive != null && !string.IsNullOrWhiteSpace(path))
            {
                ZipArchiveEntry entry = mArchive.GetEntry(path);
                if (entry != null)
                {
                    using (Stream src = entry.Open())
                    {
                        MemoryStream stream = new MemoryStream();
                        src.CopyTo(stream);
                        stream.Seek(0, SeekOrigin.Begin);
                        return stream;
                    }
                }
            }

            return null;
        }

        public void AcquireStorage()
        {
            ReleaseStorage();

            try
            {
                using (Stream src = File.OpenRead(mPath))
                {
                    MemoryStream stream = new MemoryStream();
                    src.CopyTo(stream);
                    stream.Seek(0, SeekOrigin.Begin);

                    mArchive = new ZipArchive(stream, ZipArchiveMode.Read, false);

                    src.Close();
                }
            }
            catch
            {
            }
        }

        public void ReleaseStorage()
        {
            if (mArchive != null)
            {
                mArchive.Dispose();
                mArchive = null;
            }
        }
    }
}
