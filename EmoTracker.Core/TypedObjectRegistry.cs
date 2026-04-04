using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EmoTracker.Core
{
    public class TypedObjectRegistry<T> where T : class
    {
        private static String sRegistryLock = "Registry";
        private static List<T> sSupportRegistry;
        public static IEnumerable<T> SupportRegistry
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
            sSupportRegistry = new List<T>();

            List<Type> types = new List<Type>();
            
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type[] aTypes = a.GetTypes();

                    foreach (Type t in aTypes)
                    {
                        if (typeof(T).IsInterface ? t.GetInterfaces().Contains(typeof(T)) : t.IsSubclassOf(typeof(T)))
                            types.Add(t);
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    var loaderExceptions = e.LoaderExceptions;
                    foreach (var item in loaderExceptions)
                    {
                        System.Diagnostics.Debug.Print("Assembly loading error: " + item.Message);
                    }
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.Print("Failed to load plugin assembly with error " + e.ToString());
                }
            }

            foreach (Type t in types)
            {
                if (!t.IsAbstract && !t.IsInterface && !t.ContainsGenericParameters)
                {
                    T instance = Activator.CreateInstance(t) as T;
                    if (instance != null)
                        sSupportRegistry.Add(instance);
                }
            }
        }

        public static T GetObjectOfConcreteType<V>()
        {
            foreach (T item in SupportRegistry)
            {
                if (item.GetType().IsSubclassOf(typeof(V)))
                    return item;
            }

            return null;
        }
    }
}
