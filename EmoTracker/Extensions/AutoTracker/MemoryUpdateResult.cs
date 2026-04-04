using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.AutoTracker
{
    public enum MemoryUpdateResult
    {
        Success,
        Error,
        MissingGameData,
        InvalidAccess
    };
}
