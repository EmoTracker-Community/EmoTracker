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
        private static String sRegistryLock = "Registry";
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

            var types =
                from a in AppDomain.CurrentDomain.GetAssemblies()
                from t in a.GetTypes()
                where typeof(T).IsInterface ? t.GetInterfaces().Contains(typeof(T)) : t.IsSubclassOf(typeof(T))
                select t;

            foreach (Type t in types)
            {
                if (!t.IsAbstract && !t.ContainsGenericParameters)
                    sSupportRegistry.Add(t);
            }
        }
    }
}
