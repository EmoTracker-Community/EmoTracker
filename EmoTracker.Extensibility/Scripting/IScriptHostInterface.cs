using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensibility.Scripting
{
    public interface IScriptHostInterface
    {
        /// <summary>
        /// Registers a named object in the global scripting state.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="globalObject"></param>
        void RegisterGlobal(string name, object globalObject);
    }
}
