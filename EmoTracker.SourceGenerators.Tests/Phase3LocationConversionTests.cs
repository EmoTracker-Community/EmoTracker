using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Locations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 3 verification: location/section/map types now derive from
    /// <see cref="ModelTypeBase"/> / <see cref="TransactableModelTypeBase"/>;
    /// cross-references resolve via <see cref="ModelReference{T}"/>; coordinated
    /// fork rewires owned subtrees while leaving cross-references on the lazy
    /// resolver path.
    ///
    /// <para>
    /// These tests construct location models in isolation (without going through
    /// LocationDatabase). The pack-loaded MCP smoke test against ALttPR exercises
    /// the rest end-to-end.
    /// </para>
    /// </summary>
    [Collection(nameof(TransactionCollection))]
    public class Phase3LocationConversionTests
    {
        readonly TransactionFixture _fix;

        public Phase3LocationConversionTests(TransactionFixture fix)
        {
            _fix = fix;
        }

        // -------- BadgeEntry ------------------------------------------------

        [Fact]
        public void BadgeEntry_DerivesFromModelTypeBase_KeyIsImmutable()
        {
            var b = new BadgeEntry("k", null, 1.0, 2.0);
            Assert.IsAssignableFrom<ModelTypeBase>(b);
            Assert.Equal("k", b.Key);
            Assert.Equal(1.0, b.OffsetX);
            Assert.Equal(2.0, b.OffsetY);
            Assert.NotEqual(Guid.Empty, b.DefinitionId);
        }

        [Fact]
        public void BadgeEntry_Fork_PreservesKeyAndDefinitionId()
        {
            var b = new BadgeEntry("k", null, 1.0, 2.0);
            var fork = (BadgeEntry)b.Fork();
            Assert.NotSame(b, fork);
            Assert.Equal(b.DefinitionId, fork.DefinitionId);
            Assert.Equal("k", fork.Key);
            // OffsetX is per-state (KVMutable); inherited via COW.
            Assert.Equal(1.0, fork.OffsetX);
            fork.OffsetX = 99.0;
            Assert.Equal(99.0, fork.OffsetX);
            Assert.Equal(1.0, b.OffsetX);
        }

        // -------- Group -----------------------------------------------------

        [Fact]
        public void Group_DerivesFromModelTypeBase_AndExposesKVMutable()
        {
            var g = new Group();
            Assert.IsAssignableFrom<ModelTypeBase>(g);
            g.Name = "Dungeons";
            g.Color = "red";
            Assert.Equal("Dungeons", g.Name);
            Assert.Equal("red", g.Color);
            Assert.False(g.HasLocations);
            Assert.False(g.HasAvailableItems);

            g.HasAvailableItems = true;
            Assert.True(g.HasAvailableItems);
        }

        // -------- MapLocation -----------------------------------------------

        [Fact]
        public void MapLocation_KVMutable_ScalarsRoundTrip()
        {
            var ml = new MapLocation();
            ml.X = 10.0;
            ml.Y = 20.0;
            ml.AlwaysVisible = true;
            ml.OverrideBadgeSize = true;
            Assert.Equal(10.0, ml.X);
            Assert.Equal(20.0, ml.Y);
            Assert.True(ml.AlwaysVisible);
            Assert.True(ml.OverrideBadgeSize);
        }

        [Fact]
        public void MapLocation_Size_CascadesIntoBadgeSize()
        {
            var ml = new MapLocation();
            // Default OverrideBadgeSize is false; setting Size should drive
            // BadgeSize = Size * 0.75.
            ml.Size = 100.0;
            Assert.Equal(100.0, ml.Size);
            Assert.Equal(75.0, ml.BadgeSize);
        }

        [Fact]
        public void MapLocation_Size_OverrideBadgeSize_PinsBadgeSize()
        {
            var ml = new MapLocation();
            ml.OverrideBadgeSize = true;
            ml.BadgeSize = 50.0;
            ml.Size = 100.0; // shouldn't touch BadgeSize
            Assert.Equal(50.0, ml.BadgeSize);
        }

        [Fact]
        public void MapLocation_Fork_COWIsolatesMutableValues()
        {
            var ml = new MapLocation();
            ml.X = 10.0;

            var fork = (MapLocation)ml.Fork();
            Assert.Equal(10.0, fork.X);

            fork.X = 99.0;
            Assert.Equal(99.0, fork.X);
            Assert.Equal(10.0, ml.X);
        }

        // -------- Map --------------------------------------------------------

        [Fact]
        public void Map_OwnedMapLocations_ForkedAlongWithMap()
        {
            var m = new Map { Name = "world" };
            m.AddLocation(new MapLocation { X = 1, Y = 2 });
            m.AddLocation(new MapLocation { X = 3, Y = 4 });

            var fork = (Map)m.Fork();
            Assert.Equal("world", fork.Name);
            int count = 0;
            foreach (var ml in fork.Locations) count++;
            Assert.Equal(2, count);
        }

        [Fact]
        public void Map_DisplayName_FallsBackToName()
        {
            var m = new Map();
            m.Name = "world";
            Assert.Equal("world", m.DisplayName);

            m.DisplayName = "World";
            Assert.Equal("World", m.DisplayName);

            // Setting to null restores fallback to Name.
            m.DisplayName = null;
            Assert.Equal("world", m.DisplayName);
        }

        [Fact]
        public void Map_Name_RaisesDisplayNameNotificationWhenFallingBack()
        {
            var m = new Map();
            var changes = new List<string>();
            ((INotifyPropertyChanged)m).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            m.Name = "world";
            Assert.Contains("Name", changes);
            Assert.Contains("DisplayName", changes); // because no DisplayName was explicitly set

            // After setting DisplayName explicitly, Name changes should NOT
            // raise DisplayName.
            m.DisplayName = "World";
            changes.Clear();
            m.Name = "another";
            Assert.Contains("Name", changes);
            Assert.DoesNotContain("DisplayName", changes);
        }

        // -------- LocationVisualProperties / fall-through -------------------

        [Fact]
        public void Location_AlwaysAllowChestManipulation_FallsThroughVisualParent()
        {
            var parent = new Location();
            parent.AlwaysAllowChestManipulation = true;

            var child = new Location();
            child.Parent = parent;
            // Child inherits via VisualParent.
            Assert.True(child.AlwaysAllowChestManipulation);

            // Local override wins.
            child.AlwaysAllowChestManipulation = false;
            Assert.False(child.AlwaysAllowChestManipulation);
            Assert.True(parent.AlwaysAllowChestManipulation);
        }

        [Fact]
        public void Location_OpenChestImage_DerivesUnavailableVariant()
        {
            // We can't construct a non-null ImageReference easily without a
            // real package; cover only the null-flow here.
            var loc = new Location();
            loc.OpenChestImage = null;
            Assert.Null(loc.OpenChestImage);
            Assert.Null(loc.UnavailableOpenChestImage);
        }

        // -------- Location DefinitionId / ShortName fallback ---------------

        [Fact]
        public void Location_ShortName_FallsBackToName()
        {
            var loc = new Location();
            loc.Name = "Hyrule Castle";
            Assert.Equal("Hyrule Castle", loc.ShortName);

            loc.ShortName = "HC";
            Assert.Equal("HC", loc.ShortName);

            loc.ShortName = null;
            Assert.Equal("Hyrule Castle", loc.ShortName);
        }

        [Fact]
        public void Location_DefinitionId_StableAcrossForks()
        {
            var loc = new Location();
            loc.Name = "Castle";
            var defId = loc.DefinitionId;
            Assert.NotEqual(Guid.Empty, defId);

            var fork = (Location)loc.Fork();
            Assert.Equal(defId, fork.DefinitionId);
        }

        // -------- Coordinated fork (Location → Sections / Children) ----------

        [Fact]
        public void Location_Fork_ForksOwnedSections_AndRewiresSectionOwner()
        {
            var loc = new Location();
            loc.Name = "Castle";

            var section = new Section(loc);
            section.Name = "Throne";
            loc.AddSection(section);

            var forkLoc = (Location)loc.Fork();
            // Fork has its own Sections list with a freshly-forked Section.
            int forkSectionCount = 0;
            Section forkedSection = null;
            foreach (var s in forkLoc.Sections) { forkSectionCount++; forkedSection = s; }
            Assert.Equal(1, forkSectionCount);
            Assert.NotSame(section, forkedSection);

            // Owner back-reference rewired to the forked Location.
            Assert.Same(forkLoc, forkedSection.Owner);
            // Source's section is unaffected.
            Assert.Same(loc, section.Owner);
            // Same DefinitionId (cross-state identity).
            Assert.Equal(section.DefinitionId, forkedSection.DefinitionId);
        }

        [Fact]
        public void Location_Fork_ForksChildren_AndRewiresParent()
        {
            var loc = new Location();
            var child = new Location();
            child.Parent = loc;
            loc.AddChild(child);

            var forkLoc = (Location)loc.Fork();
            int forkChildCount = 0;
            Location forkedChild = null;
            foreach (var c in forkLoc.Children) { forkChildCount++; forkedChild = c; }
            Assert.Equal(1, forkChildCount);
            Assert.NotSame(child, forkedChild);
            Assert.Same(forkLoc, forkedChild.Parent);
            Assert.Same(loc, child.Parent);
        }

        // -------- Section transactable Pinned-style cycle (Section.Captured-
        //          Item / AvailableChestCount full setup is complex; smoke
        //          covered by MCP. This test focuses on ChestCount + the
        //          AvailableChestCount transactable round-trip without the
        //          captured-item cascade.) -----------------------------------

        [Fact]
        public void Section_AvailableChestCount_IsTransactable_AndUndoes()
        {
            var loc = new Location();
            var section = new Section(loc);
            section.ChestCount = 5;
            loc.AddSection(section);

            using (TransactionProcessor.Current.OpenTransaction())
                section.AvailableChestCount = 3;
            Assert.Equal(3u, section.AvailableChestCount);

            ((IUndoableTransactionProcessor)TransactionProcessor.Current).Undo();
            Assert.Equal(0u, section.AvailableChestCount);
        }

        [Fact]
        public void Section_HostedItem_ResolvesViaCachedRef()
        {
            // We can't construct an ItemBase target easily without a pack here
            // — exercise the empty-state path.
            var loc = new Location();
            var section = new Section(loc);
            Assert.Null(section.HostedItem);
            Assert.Null(section.CapturedItem);
            Assert.Null(section.GateItem);
        }

        // -------- ModelReference + AmbientSingletonModelResolver ------------

        [Fact]
        public void AmbientSingletonModelResolver_DoesNotResolveUnknownGuid()
        {
            var resolver = new AmbientSingletonModelResolver();
            var result = resolver.Resolve<Location>(Guid.NewGuid());
            Assert.Null(result);
        }

        [Fact]
        public void AmbientSingletonModelResolver_ReturnsNullOnEmptyGuid()
        {
            var resolver = new AmbientSingletonModelResolver();
            Assert.Null(resolver.Resolve<Location>(Guid.Empty));
        }

        // -------- SectionChestsProxyItem retrofit (Phase 2.5 deferred) ------

        [Fact]
        public void SectionChestsProxyItem_ForksWithModelReference()
        {
            var item = new EmoTracker.Data.Items.SectionChestsProxyItem();
            // No section assigned — the proxy is in empty state.
            Assert.Null(item.Section);
            Assert.Equal(0u, item.Count);

            var fork = (EmoTracker.Data.Items.SectionChestsProxyItem)item.Fork();
            Assert.NotSame(item, fork);
            Assert.Equal(item.DefinitionId, fork.DefinitionId);
            Assert.Null(fork.Section);
        }
    }
}
