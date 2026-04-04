using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensibility.Scripting
{
    public interface IScriptPlugin
    {
        /// <summary>
        /// Perform any necessary scripting related initialization via the provided IScriptHostInterface object
        /// </summary>
        /// <param name="hostInterface"></param>
        void InitializeScriptPlugin(IScriptHostInterface hostInterface);
    }
}
