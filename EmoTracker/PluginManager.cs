using EmoTracker.Core;
using EmoTracker.Extensibility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker
{
    public static class PluginManager
    {
        static List<IPlugin> mPlugins = new List<IPlugin>();

        public static void Initialize(string assemblyFilter = null)
        {
            EnsureReferencedAssembliesAreLoaded();
            LoadPluginAssemblies(assemblyFilter);
        }

        public static IEnumerable<IPlugin> GetPlugins()
        {
            return mPlugins;
        }
         
        public static IEnumerable<PluginType> GetPlugins<PluginType>() where PluginType : class
        {
            return from plugin in mPlugins
                   where plugin as PluginType != null
                   select plugin as PluginType;
        }

        private static void EnsureReferencedAssembliesAreLoaded()
        {
            LoadReferencedAssemblies(Assembly.GetEntryAssembly());
        }

        private static void LoadReferencedAssemblies(Assembly assembly)
        {
            foreach (AssemblyName name in assembly.GetReferencedAssemblies())
            {
                if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName == name.FullName))
                {
                    try
                    {
                        System.Diagnostics.Debug.Print("Force loading assembly: {0}", name);
                        Assembly newAssembly = Assembly.Load(name);
                        if (newAssembly != null)
                            LoadReferencedAssemblies(newAssembly);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void LoadPluginAssemblies(string assemblyFilter = null)
        {
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.AssemblyResolve += new ResolveEventHandler(ResolveLoadedPluginsHandler);

            try
            {
                string hostExePath = System.Reflection.Assembly.GetEntryAssembly().CodeBase;
                hostExePath = hostExePath.Replace(@"file:", "");
                hostExePath = hostExePath.TrimStart('/');
                string pluginsPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(hostExePath), "Plugins"));

                if (Directory.Exists(pluginsPath))
                {
                    foreach (string file in Directory.EnumerateFiles(pluginsPath, "*.dll", SearchOption.AllDirectories))
                    {
                        if (assemblyFilter != null && Path.GetFileNameWithoutExtension(file).ToLower().Contains(assemblyFilter.ToLower()))
                            continue;

                        try
                        {
                            System.Console.WriteLine("Loading plugin assembly: {0}", System.IO.Path.GetFileName(file));
                            LoadReferencedAssemblies(Assembly.LoadFile(System.IO.Path.GetFullPath(file)));
                        }
                        catch
                        {
                            System.Console.WriteLine("Failed to load plugin from {0}", System.IO.Path.GetFileName(file));
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static int PluginPriorityComparer(IPlugin x, IPlugin y)
        {
            return x.Priority.CompareTo(y.Priority);
        }

        private static Assembly ResolveLoadedPluginsHandler(object sender, ResolveEventArgs args)
        {
            //This handler is called only when the common language runtime tries to bind to the assembly and fails.
            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                string loadedName = loaded.GetName().FullName;
                if (loadedName.Equals(args.Name, StringComparison.OrdinalIgnoreCase))
                    return loaded;
            }

            return null;
        }
    }
}
