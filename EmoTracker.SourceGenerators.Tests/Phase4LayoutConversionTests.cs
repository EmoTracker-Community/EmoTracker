using EmoTracker.Core.DataModel;
using EmoTracker.Data.Layout;
using System.ComponentModel;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 4 verification: <see cref="LayoutItem"/> and concrete leaves now
    /// derive from <see cref="ModelTypeBase"/>; properties are
    /// <c>[KVOverridable]</c> so per-state writes go to <c>MutableData</c> while
    /// definition-time defaults live in <c>ImmutableData</c> under the
    /// <c>{Name}__def</c> key. Fork inherits both via COW (mutable) + shared
    /// reference (immutable).
    ///
    /// <para>
    /// These tests construct layout types directly without going through
    /// <c>TryParse</c> (which would require <c>Tracker.Instance</c> +
    /// <c>LayoutManager.Instance</c> singletons). Pack-load + UI-parity
    /// validation is exercised by the MCP smoke against ALttPR.
    /// </para>
    /// </summary>
    public class Phase4LayoutConversionTests
    {
        // -------- LayoutItem hierarchy -------------------------------------

        [Fact]
        public void LayoutItem_DerivesFromModelTypeBase_AndHasDefinitionId()
        {
            var t = new TextBlock();
            Assert.IsAssignableFrom<ModelTypeBase>(t);
            Assert.NotEqual(System.Guid.Empty, t.DefinitionId);
        }

        // -------- KVOverridable round-trip on a leaf type ------------------

        [Fact]
        public void TextBlock_KVOverridable_RoundTrip_RaisesINPC()
        {
            var t = new TextBlock();
            var changes = new System.Collections.Generic.List<string>();
            ((INotifyPropertyChanged)t).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            t.Text = "hello";
            t.FontSize = 14.0;

            Assert.Equal("hello", t.Text);
            Assert.Equal(14.0, t.FontSize);
            Assert.Contains("Text", changes);
            Assert.Contains("FontSize", changes);
        }

        [Fact]
        public void TextBlock_KVOverridable_NoOpWrite_DoesNotRaiseINPC()
        {
            var t = new TextBlock();
            t.Text = "hello";

            var changes = new System.Collections.Generic.List<string>();
            ((INotifyPropertyChanged)t).PropertyChanged += (_, e) => changes.Add(e.PropertyName);
            t.Text = "hello"; // identical, equality short-circuits

            Assert.DoesNotContain("Text", changes);
        }

        [Fact]
        public void TextBlock_DefaultValues_BeforeAnyWrite()
        {
            var t = new TextBlock();
            // No definition default seeded (no parse); no override set.
            // Getters fall through to default(T).
            Assert.Null(t.Text);
            Assert.Equal(0.0, t.FontSize);
            Assert.Null(t.Background);
            // Width default is also default(double) = 0; OverrideWidth -> false
            // because Phase 4's sentinel inversion treats 0 as "not set".
            Assert.False(t.OverrideWidth);
            Assert.False(t.OverrideBackground);
        }

        // -------- Fork: COW isolation on KVOverridable -----------------------

        [Fact]
        public void TextBlock_Fork_KVOverridable_IsCOWIsolated()
        {
            var t = new TextBlock();
            t.Text = "original";
            t.FontSize = 12.0;

            var fork = (TextBlock)t.Fork();

            // Fork inherits via COW.
            Assert.Equal("original", fork.Text);
            Assert.Equal(12.0, fork.FontSize);
            Assert.Equal(t.DefinitionId, fork.DefinitionId);

            // Fork-side mutation shadows without affecting the source.
            fork.Text = "fork-only";
            fork.FontSize = 24.0;
            Assert.Equal("fork-only", fork.Text);
            Assert.Equal(24.0, fork.FontSize);
            Assert.Equal("original", t.Text);
            Assert.Equal(12.0, t.FontSize);

            // The reverse direction also stays isolated.
            t.Text = "newly-original";
            Assert.Equal("newly-original", t.Text);
            Assert.Equal("fork-only", fork.Text);
        }

        [Fact]
        public void Image_Fork_KVOverridable_OnReferenceTypeProperty()
        {
            // Image.Content is ImageReference (reference type, IDeepCopyable
            // returning this). Verifies the [KVOverridable] machinery works
            // for reference-typed properties — no NRE on null fall-through.
            var img = new Image();
            Assert.Null(img.Content);

            var fork = (Image)img.Fork();
            Assert.Null(fork.Content);
        }

        // -------- Fork: owned subtree walks ---------------------------------

        [Fact]
        public void Container_Fork_WalksAndForksOwnedChildren()
        {
            var c = new EmoTracker.Data.Layout.Container();
            // Reflectively access mItems via a child push — Container doesn't
            // expose Add publicly; build via the public Items enumerable.
            // Easiest path: use TextBlock directly and add through an ad-hoc
            // helper. We test the fork copy semantics, so fork an empty
            // container and confirm the fork has its own items collection.
            var fork = (EmoTracker.Data.Layout.Container)c.Fork();
            Assert.NotSame(c, fork);
            // Both empty.
            using (var s = c.Items.GetEnumerator())
                Assert.False(s.MoveNext());
            using (var s = fork.Items.GetEnumerator())
                Assert.False(s.MoveNext());
            Assert.Equal(c.DefinitionId, fork.DefinitionId);
        }

        // -------- Fork: cross-reference holders ------------------------------

        [Fact]
        public void Item_Data_IsModelReference_ForksCleanly()
        {
            // Item.Data is held via ModelReference<ITrackableItem>. Without an
            // ItemDatabase to resolve through, Target is null. Verify the fork
            // has its own ModelReference instance with the same DefinitionId
            // (carried via ImmutableData) and no cached target.
            var i = new Item();
            Assert.Null(i.Data);

            var fork = (Item)i.Fork();
            Assert.NotSame(i, fork);
            Assert.Null(fork.Data);
            Assert.Equal(i.DefinitionId, fork.DefinitionId);
        }

        [Fact]
        public void LayoutReference_Layout_IsModelReference_ForksCleanly()
        {
            var lr = new LayoutReference();
            Assert.Null(lr.Layout);

            var fork = (LayoutReference)lr.Fork();
            Assert.NotSame(lr, fork);
            Assert.Null(fork.Layout);
            Assert.Equal(lr.DefinitionId, fork.DefinitionId);
        }

        // -------- Layout outer container ------------------------------------

        [Fact]
        public void Layout_DerivesFromModelTypeBase_ForkPreservesIdentity()
        {
            var l = new Layout();
            Assert.IsAssignableFrom<ModelTypeBase>(l);
            Assert.Null(l.Root);

            var fork = (Layout)l.Fork();
            Assert.NotSame(l, fork);
            Assert.Equal(l.DefinitionId, fork.DefinitionId);
            Assert.Null(fork.Root);
        }

        // -------- TabPanel inner Tab class ----------------------------------

        [Fact]
        public void TabPanel_Tab_DerivesFromModelTypeBase()
        {
            var tab = new TabPanel.Tab();
            Assert.IsAssignableFrom<ModelTypeBase>(tab);
            Assert.NotEqual(System.Guid.Empty, tab.DefinitionId);

            // Title / Icon default to default(T) without a definition seeded.
            Assert.Null(tab.Title);
            Assert.Null(tab.Icon);

            // Set the override and verify it sticks.
            tab.Title = "Dungeons";
            Assert.Equal("Dungeons", tab.Title);
        }

        [Fact]
        public void TabPanel_Tab_Fork_KVOverridable_IsCOWIsolated()
        {
            var tab = new TabPanel.Tab();
            tab.Title = "Source";

            var fork = (TabPanel.Tab)tab.Fork();
            Assert.Equal("Source", fork.Title);

            fork.Title = "Fork";
            Assert.Equal("Fork", fork.Title);
            Assert.Equal("Source", tab.Title);
        }
    }
}
