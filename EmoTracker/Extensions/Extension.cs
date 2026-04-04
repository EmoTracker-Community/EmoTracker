using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace EmoTracker.Extensions
{
    public interface Extension
    {
        string Name { get; }

        string UID { get; }

        int Priority { get; }

        void Start();

        void Stop();

        void OnPackageUnloaded();

        void OnPackageLoaded();

        FrameworkElement StatusBarControl { get; }

        JToken SerializeToJson();

        bool DeserializeFromJson(JToken token);
    }
}
