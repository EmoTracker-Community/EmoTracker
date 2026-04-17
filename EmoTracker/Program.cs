using Avalonia;
using System;

namespace EmoTracker
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Register the WPF pack:// URI scheme so that pack://application:,,,/... URIs
            // constructed in EmoTracker.Data (PackageManager, LocationDatabase) don't throw
            // UriFormatException on .NET 8 where this scheme is not registered by default.
            if (!UriParser.IsKnownScheme("pack"))
                UriParser.Register(new GenericUriParser(GenericUriParserOptions.GenericAuthority), "pack", -1);

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}
