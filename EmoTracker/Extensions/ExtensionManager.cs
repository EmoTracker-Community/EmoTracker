using EmoTracker.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensions
{
    public class ExtensionManager : ObservableSingleton<ExtensionManager>
    {
        ObservableCollection<Extension> mExtensions = new ObservableCollection<Extension>();

        public IEnumerable<Extension> Extensions
        {
            get { return mExtensions; }
        }

        public ExtensionManager()
        {
            LoadExtensionModules();

            Type interfaceType = typeof(Extension);

            List<Type> types = new List<Type>();
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type t in a.GetTypes())
                    {
                        try
                        {
                            if (!t.IsAbstract && interfaceType.IsAssignableFrom(t) && !types.Contains(t))
                                types.Add(t);
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }

            foreach (Type t in types)
            {
                try
                {
                    Extension instance = Activator.CreateInstance(t) as Extension;
                    if (instance != null)
                        mExtensions.Add(instance);
                }
                catch
                {
                }
            }

            mExtensions.Sort(ExtensionSortKeyFunc);

        }

        public void Start()
        {
            foreach (Extension ext in Extensions)
            {
                ext.Start();
            }
        }

        private object ExtensionSortKeyFunc(Extension arg)
        {
            return arg.Priority;
        } 

        public T FindExtension<T>() where T : class, Extension
        {
            foreach (Extension ext in Extensions)
            {
                if (ext.GetType() == typeof(T))
                    return ext as T;
            }

            return null;
        }

        public Extension FindExtensionByUID(string uid)
        {
            foreach (Extension ext in Extensions)
            {
                if (ext.UID.Equals(uid, StringComparison.OrdinalIgnoreCase))
                    return ext;
            }

            return null;
        }

        public static string GetExtensionPath(Extension instance)
        {
            return Path.Combine(Core.UserDirectory.Path, "extensions", instance.UID);
        }

        private void LoadExtensionModules()
        {
        }

        public void OnApplicationClosing()
        {
            foreach (Extension ext in Extensions)
            {
                ext.Stop();
            }
        }

        public void OnPackageUnloaded()
        {
            foreach (Extension ext in Extensions)
            {
                ext.OnPackageUnloaded();
            }
        }

        public void OnPackageLoaded()
        {
            foreach (Extension ext in Extensions)
            {
                ext.OnPackageLoaded();
            }
        }
    }
}
