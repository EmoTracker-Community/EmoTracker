using ConnectorLib;
using EmoTracker.Data.Packages;
using NLua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EmoTracker.Extensions.AutoTracker.MemorySegment;

namespace EmoTracker.Extensions.AutoTracker
{
    internal interface IUpdateWithConnector
    {
        [LuaHide]
        void MarkDirty();

        [LuaHide]
        bool ShouldUpdate(DateTime now);

        [LuaHide]
        MemoryUpdateResult UpdateWithConnector(IAddressableConnector connector, PackageManager.Game game);
    }
}
