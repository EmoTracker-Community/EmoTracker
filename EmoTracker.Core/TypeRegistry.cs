using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public class TypeRegistry<T> where T : class
    {
        private static readonly object sRegistryLock = new();
        private static List<Type> sSupportRegistry;
        public static IEnumerable<Type> SupportRegistry
        {
            get
            {
                lock (sRegistryLock)
                {
                    if (sSupportRegistry == null)
                        BuildRegistry();

                    return sSupportRegistry;
                }
            }
        }

        static void BuildRegistry()
        {
            sSupportRegistry = new List<Type>();

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type t in a.GetTypes())
                    {
                        if ((typeof(T).IsInterface ? t.GetInterfaces().Contains(typeof(T)) : t.IsSubclassOf(typeof(T)))
                            && !t.IsAbstract && !t.ContainsGenericParameters)
                        {
                            sSupportRegistry.Add(t);
                        }
                    }
                }
                catch (ReflectionTypeLoadException e) when (e.LoaderExceptions != null)
                {
                    foreach (var item in e.LoaderExceptions)
                    {
                        System.Diagnostics.Debug.Print("Assembly loading error: " + item.Message);
                    }
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.Print("Failed to load assembly with error: " + e);
                }
            }
        }
    }
}
