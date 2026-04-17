using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        // Typed as object so this interface has no UI framework dependency.
        // Implementations return a platform-specific control (WPF FrameworkElement or Avalonia Control).
        object StatusBarControl { get; }

        JToken SerializeToJson();

        bool DeserializeFromJson(JToken token);
    }
}
