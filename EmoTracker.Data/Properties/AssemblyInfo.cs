using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("EmoTracker.Data")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("EmoTracker.Data")]
[assembly: AssemblyCopyright("Copyright ©  2019")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("8b95a4e0-8f5b-4894-b1a4-945e20c989e8")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("3.0.1.11")]
[assembly: AssemblyVersion("3.0.3.1")]
[assembly: AssemblyFileVersion("3.0.3.1")]

// Expose internals to the source-generator test project so unit tests can
// drive otherwise-internal seeding helpers (e.g. TabPanel.Tab.SeedDefinition)
// that are part of the parse-time setup path. Production callers don't need
// them — they go through the JSON parse entry point.
[assembly: InternalsVisibleTo("EmoTracker.SourceGenerators.Tests")]
// Expose internals to the main app so per-state extensions
// (AutoTrackerExtension's fork-replay path) can read ScriptManager's
// fork-time hooks (ForkSource, ForkCloner, MemoryWatchRegistrations)
// without forcing those onto the public API surface.
[assembly: InternalsVisibleTo("EmoTracker")]
