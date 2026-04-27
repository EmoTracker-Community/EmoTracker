using EmoTracker.Core.DataModel;
using EmoTracker.Data.Sessions;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Test helpers for the Fork pipeline. Every <see cref="ModelTypeBase.Fork(ITrackerStateContext)"/>
    /// requires an explicit destination state — there is no parameterless
    /// overload — so tests that exercise fork mechanics in isolation
    /// allocate a fresh <see cref="TrackerState"/> via <see cref="NewDestState"/>.
    /// </summary>
    internal static class ForkTestHelpers
    {
        public static TrackerState NewDestState(string name = "test-fork-dest")
            => new TrackerState(name);
    }
}
