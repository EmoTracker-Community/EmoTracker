using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensibility
{
    public interface IPlugin
    {
        /// <summary>
        /// A category name for the plugin, suitable for display (single-line)
        /// </summary>
        string Category { get; }

        /// <summary>
        /// A name for the plugin, suitable for display (single-line)
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A longer description for the plugin, suitable for display (multi-line)
        /// </summary>
        string Description { get; }

        /// <summary>
        /// A globally unique ID that packages can use to activate this plugin
        /// </summary>
        string UID { get; }

        /// <summary>
        /// A signed priority value, which is used to determine in which order plugins
        /// are returned to call-sites which use them
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Called in priority order, after all plugins have been constructed
        /// </summary>
        void InitlalizePlugin(IApplicationInterface appInterface);

        /// <summary>
        /// Called in inverse-priority order, just prior to application shutdown
        /// </summary>
        void ShutdownPlugin(IApplicationInterface appInterface);
    }
}
