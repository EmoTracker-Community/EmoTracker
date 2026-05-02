// Polyfills required so this netstandard2.0 generator project can use C# 9+
// language features (records / init-only setters) that the BCL on netstandard2.0
// does not provide. These are recognized by the compiler purely by full name.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
