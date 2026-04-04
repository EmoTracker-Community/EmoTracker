using EmoTracker.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.NDI
{
    public class NDIExtension : ObservableObject, Extension
    {
        public string Name
        {
            get { return "NewTek NDI®"; }
        }

        public string UID
        {
            get { return "newtek_ndi_support"; }
        }

        public int Priority { get { return -20000; } }

        bool mbActive = false;
        public bool Active
        {
            get { return mbActive; }
            set { SetProperty(ref mbActive, value); }
        }

        public object StatusBarControl
        {
            get; set;
        }

        public NDIExtension()
        {
            StatusBarControl = new NDIStatusIndicator() { DataContext = this };
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
