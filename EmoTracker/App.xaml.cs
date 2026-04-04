using EmoTracker.Core;
using EmoTracker.Services;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace EmoTracker
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ForceLoadLuaDLL();
            ConfigurePlatformDllPaths();

            Data.Core.Transactions.TransactionProcessor.SetTransactionProcessor(new Data.Core.Transactions.Processors.LocalTransactionProcessorWithUndo());

            Core.Services.Backends.LogService.SetServiceBackend(new Services.LogService());
            Core.Services.Backends.DispatchService.SetServiceBackend(new Services.DispatchService());

            try
            {
                //  Create log directory
                string logDirectory = Path.Combine(UserDirectory.Path, "logs");

                //  Configure Serilog logging
                {
                    Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Verbose()
                        .Enrich.FromLogContext()
                        .WriteTo.File(Path.Combine(logDirectory, "emotracker_log.txt"), rollingInterval: RollingInterval.Day, buffered: true, flushToDiskInterval: TimeSpan.FromSeconds(5))
                        .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
                        .WriteTo.DeveloperConsole()
                        .CreateLogger();
                }
            }
            catch (Exception)
            {
            }

            //  Windows 7 requires the following in order to connect via https
            OperatingSystem os = System.Environment.OSVersion;
            if (os.Version.Major < 10)
            {
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }

            foreach (FontFamily fontFamily in Fonts.GetFontFamilies(new Uri("pack://application:,,,/"), "./Resources/Fonts/"))
            {
                string name = System.IO.Path.GetFileName(fontFamily.ToString());
                name = name.Replace("#", "");
                name = name.Replace(" ", "");

                this.Resources.Add(name, fontFamily);

                // Perform action.
            }

            //  Load application settings
            Data.ApplicationSettings.CreateInstance();

            MainWindow window = new EmoTracker.MainWindow();
            MainWindow = window;
            window.Show();

            base.OnStartup(e);
        }

        #region -- Platform Redirection for Native Assemblies --

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libname);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetDllDirectory(string lpPathName);

        private void ForceLoadLuaDLL()
        {
            string processorAssemblyPath = "x64";
            if (!Environment.Is64BitProcess)
                processorAssemblyPath = "x86";

            string privateBinPath = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, string.Format("{0}\\lua53.dll", processorAssemblyPath));
            try
            {
                IntPtr handle = LoadLibrary(privateBinPath);
                if (handle == IntPtr.Zero)
                {
                    MessageBox.Show("EmoTracker was unable to load the Lua interpreter DLL, and will now close.\n\nSee the EmoTracker Discord for more help.", "Critical Error");
                    Shutdown(-20);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show("EmoTracker was unable to load the Lua interpreter DLL, and will now close.\n\nSee the EmoTracker Discord for more help.", "Critical Error");
                throw new InvalidOperationException(string.Format("Failed to force-load Lua interpreter DLL at path: {0}", privateBinPath), e);
            }
        }

        private void ConfigurePlatformDllPaths()
        {
            try
            {
                string processorAssemblyPath = "x64";
                if (!Environment.Is64BitProcess)
                    processorAssemblyPath = "x86";

                string privateBinPath = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, string.Format("{0}\\", processorAssemblyPath));
                SetDllDirectory(privateBinPath);
            }
            catch
            {
                throw new InvalidOperationException("Failed to set platform DLL search directory.");
            }
        }

#endregion

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                //  Shutdown extensions
                if (e.ApplicationExitCode == 0)
                    Extensions.ExtensionManager.Instance.OnApplicationClosing();

                if (Data.ApplicationSettings.Instance.EnableDiscordRichPresence)
                {
                    try
                    {
                        DiscordRpc.ClearPresence();
                        DiscordRpc.Shutdown();
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                base.OnExit(e);
            }
        }
    }
}
