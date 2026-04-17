namespace EmoTracker.Core
{
    public class DotNetFrameworkVersion
    {
        // .NET Framework version checking is only relevant on Windows with .NET Framework.
        // On .NET 8+ this class is a no-op.
        public static void CheckVersion() { }
    }
}
