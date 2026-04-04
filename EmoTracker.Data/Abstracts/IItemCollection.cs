using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data
{
    public interface IItemCollection
    {
        IEnumerable<ITrackableItem> Items { get; }

        bool AddItem(ITrackableItem item);
        bool RemoveItem(ITrackableItem item);
    }
}
