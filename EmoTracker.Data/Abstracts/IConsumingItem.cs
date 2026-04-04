using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data
{
    public interface IConsumingItem
    {
        bool GetPotentialConsumedItem(out string code, out uint count);
    }
}
