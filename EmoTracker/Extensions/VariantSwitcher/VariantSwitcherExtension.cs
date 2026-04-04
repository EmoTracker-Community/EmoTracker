using EmoTracker.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace EmoTracker.Extensions.VariantSwitcher
{
    public class VariantSwitcherExtension : ObservableObject, Extension
    {
        public string Name
        {
            get { return "Variant Switcher"; }
        }

        public string UID
        {
            get { return "emotracker_variant_switcher"; }
        }

        public int Priority { get { return -200; } }

        public FrameworkElement StatusBarControl
        {
            get; set;
        }

        public VariantSwitcherExtension()
        {
            StatusBarControl = new VariantSwitcherControl() { DataContext = this };
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }
        public void OnPackageUnloaded()
        {
        }
        public void OnPackageLoaded()
        {
        }

        public JToken SerializeToJson()
        {
            return null;
        }

        public bool DeserializeFromJson(JToken token)
        {
            return true;
        }
    }
}
