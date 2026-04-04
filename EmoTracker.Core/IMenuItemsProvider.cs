using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public interface IMenuItemsProvider
    {
        IEnumerable<object> Items { get; }
    }
}
