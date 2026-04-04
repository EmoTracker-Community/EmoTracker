using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensibility.AutoTracker
{
    public interface IAutoTrackerPlugin : IPlugin
    {
        /// <summary>
        /// Returns a list of currently available devices provided by this plugin
        /// </summary>
        /// <returns>An enumerable collection of IAutoTrackerDevice instances. Ensure that the returned collection implements INotifyCollectionChanged if the list can dynamically change.</returns>
        IEnumerable<IAutoTrackerDevice> GetAvailableDevices();
    }
}
