using EmoTracker.UI.Controls;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// The item icon keeps its current bitmap only when the same item is
    /// moving to an image that is still decoding. A new item, a cleared
    /// icon, or an already-decoded icon updates immediately.
    /// </summary>
    public class HeldImageTests
    {
        [Theory]
        [InlineData(false, true, true, false, HeldImage.Action.ShowStandIn)]
        [InlineData(true, true, true, false, HeldImage.Action.HoldPrevious)]
        [InlineData(true, false, true, false, HeldImage.Action.ShowStandIn)]
        [InlineData(true, true, true, true, HeldImage.Action.ShowIncoming)]
        [InlineData(false, true, true, true, HeldImage.Action.ShowIncoming)]
        [InlineData(true, true, false, false, HeldImage.Action.Clear)]
        [InlineData(true, true, false, true, HeldImage.Action.Clear)]
        public void Decide(bool sameOwner, bool previousIsReal, bool hasIncoming, bool incomingIsReady, HeldImage.Action expected)
        {
            Assert.Equal(expected, HeldImage.Decide(sameOwner, previousIsReal, hasIncoming, incomingIsReady));
        }
    }
}
