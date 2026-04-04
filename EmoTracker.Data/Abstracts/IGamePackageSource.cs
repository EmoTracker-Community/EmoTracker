using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data
{
    public interface IGamePackageSource
    {
        void AcquireStorage();
        void ReleaseStorage();

        string PackPath { get; }

        System.IO.Stream Open(string path);

        IEnumerable<string> Files { get; }
    }
}
