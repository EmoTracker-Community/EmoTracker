using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public static class ApplicationVersion
    {
        public static Version Current
        {
            get
            {
                return System.Reflection.Assembly.GetEntryAssembly().GetName().Version;
            }
        }
    }
}
