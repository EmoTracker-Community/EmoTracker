using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Layout
{
    //
    // Summary:
    //     Indicates where an element should be displayed on the horizontal axis relative
    //     to the allocated layout slot of the parent element.
    public enum HorizontalAlignment
    {
        //
        // Summary:
        //     An element aligned to the left of the layout slot for the parent element.
        Left = 0,
        //
        // Summary:
        //     An element aligned to the center of the layout slot for the parent element.
        Center = 1,
        //
        // Summary:
        //     An element aligned to the right of the layout slot for the parent element.
        Right = 2,
        //
        // Summary:
        //     An element stretched to fill the entire layout slot of the parent element.
        Stretch = 3
    }

    //
    // Summary:
    //     Describes how a child element is vertically positioned or stretched within a
    //     parent's layout slot.
    public enum VerticalAlignment
    {
        //
        // Summary:
        //     The child element is aligned to the top of the parent's layout slot.
        Top = 0,
        //
        // Summary:
        //     The child element is aligned to the center of the parent's layout slot.
        Center = 1,
        //
        // Summary:
        //     The child element is aligned to the bottom of the parent's layout slot.
        Bottom = 2,
        //
        // Summary:
        //     The child element stretches to fill the parent's layout slot.
        Stretch = 3
    }

    /// <summary>
    /// Represents one of nine anchor positions on a 3×3 grid:
    ///
    ///   TopLeft   | Top    | TopRight
    ///   Left      | Center | Right
    ///   BottomLeft| Bottom | BottomRight
    /// </summary>
    public enum ContentAlignment
    {
        TopLeft = 0,
        Top = 1,
        TopRight = 2,
        Left = 3,
        Center = 4,
        Right = 5,
        BottomLeft = 6,
        Bottom = 7,
        BottomRight = 8
    }

    /// <summary>
    /// Phase 4: <see cref="LayoutItem"/> is now a <see cref="ModelTypeBase"/>. Most
    /// properties are <c>[KVOverridable]</c> — definition value lives in
    /// <see cref="ModelTypeBase.ImmutableData"/> under <c>{Name}__def</c>; per-state
    /// override (when present) lives in <see cref="ModelTypeBase.MutableData"/> under
    /// <c>{Name}</c>. The <c>OverrideXxx</c> computed flags inspect the resolved
    /// value the same way they did pre-Phase-4 (sentinels — <c>-1.0</c> for unset
    /// doubles, <c>null</c> / empty for unset strings).
    ///
    /// <para>
    /// <see cref="UniqueID"/> is <c>[KVImmutable]</c>: it is the definition-time
    /// identity of the layout element, never mutates at runtime.
    /// </para>
    /// <para>
    /// <see cref="Fork"/> uses an <see cref="Activator"/>-based default so concrete
    /// leaves without owned subtrees (e.g. <c>TextBlock</c>, <c>Image</c>, <c>Item</c>)
    /// don't need their own override. Subclasses holding owned children
    /// (<c>Container</c>, <c>TabPanel</c>, <c>MapPanel</c>, <c>Map</c> via
    /// <c>MapPanel</c>) override to walk + fork the subtree.
    /// </para>
    /// </summary>
    public abstract partial class LayoutItem : ModelTypeBase
    {
        // ---- [KVImmutable] -------------------------------------------------

        /// <summary>
        /// Definition-time identifier for this element. Set during parse via the
        /// per-parse definition dictionary (see <see cref="TryParse"/>). Never
        /// mutates at runtime; shared by reference across forks via
        /// <see cref="ModelTypeBase.ImmutableData"/>.
        /// </summary>
        [KVImmutable]
        public partial string UniqueID { get; }

        // ---- [KVOverridable] visual / layout properties --------------------

        [KVOverridable]
        public partial string Background { get; set; }

        [KVOverridable]
        public partial string Foreground { get; set; }

        [KVOverridable]
        public partial string DockLocation { get; set; }

        [KVOverridable]
        public partial string Margin { get; set; }

        [KVOverridable]
        public partial HorizontalAlignment HorizontalAlignment { get; set; }

        [KVOverridable]
        public partial VerticalAlignment VerticalAlignment { get; set; }

        // [DependentProperty] cascades preserve the pre-Phase-4 INPC behavior:
        // the original Width/Height/Scale/etc. setters explicitly raised
        // PropertyChanged for the matching OverrideXxx (and EffectiveScale)
        // computed flag. Because computed flags are getter-only, the cascade
        // is the only way XAML bindings on EffectiveScale (used heavily in
        // LayoutControl.axaml) and OverrideXxx flags refresh when the
        // underlying KVOverridable property is mutated at runtime.
        [KVOverridable]
        [DependentProperty(nameof(OverrideScale))]
        [DependentProperty(nameof(EffectiveScale))]
        public partial double Scale { get; set; }

        [KVOverridable]
        [DependentProperty(nameof(OverrideWidth))]
        public partial double Width { get; set; }

        [KVOverridable]
        [DependentProperty(nameof(OverrideHeight))]
        public partial double Height { get; set; }

        [KVOverridable]
        [DependentProperty(nameof(OverrideMinWidth))]
        public partial double MinWidth { get; set; }

        [KVOverridable]
        [DependentProperty(nameof(OverrideMinHeight))]
        public partial double MinHeight { get; set; }

        [KVOverridable]
        [DependentProperty(nameof(OverrideMaxWidth))]
        public partial double MaxWidth { get; set; }

        [KVOverridable]
        [DependentProperty(nameof(OverrideMaxHeight))]
        public partial double MaxHeight { get; set; }

        [KVOverridable]
        [DependentProperty(nameof(OverrideCanvasX))]
        public partial double CanvasX { get; set; }

        [KVOverridable]
        [DependentProperty(nameof(OverrideCanvasY))]
        public partial double CanvasY { get; set; }

        [KVOverridable]
        [DependentProperty(nameof(OverrideCanvasDepth))]
        public partial double CanvasDepth { get; set; }

        [KVOverridable]
        public partial bool DropShadow { get; set; }

        [KVOverridable]
        public partial bool BroadcastShadow { get; set; }

        [KVOverridable]
        public partial bool HitTestVisible { get; set; }

        // ---- "Override*" computed flags (preserve pre-Phase-4 semantics) ----

        public bool OverrideBackground => !string.IsNullOrEmpty(Background);
        public bool OverrideForeground => !string.IsNullOrEmpty(Foreground);
        public bool OverrideDockLocation => !string.IsNullOrEmpty(DockLocation);
        public bool OverrideWidth => Width >= 0.0;
        public bool OverrideHeight => Height >= 0.0;
        public bool OverrideMinWidth => MinWidth >= 0.0;
        public bool OverrideMinHeight => MinHeight >= 0.0;
        public bool OverrideMaxWidth => MaxWidth >= 0.0;
        public bool OverrideMaxHeight => MaxHeight >= 0.0;
        public bool OverrideScale => Scale > 0.0;
        public bool OverrideCanvasX => CanvasX > 0.0;
        public bool OverrideCanvasY => CanvasY > 0.0;
        public bool OverrideCanvasDepth => CanvasDepth > 0.0;
        public double EffectiveScale => OverrideScale ? Scale : 1.0;

        // -------- Construction / parse -------------------------------------

        /// <summary>
        /// Seeds <see cref="ImmutableData"/> with the LayoutItem-level sentinel
        /// defaults that match the pre-Phase-4 field initializers (Width = -1.0,
        /// HorizontalAlignment = Stretch, HitTestVisible = true, etc.) so that
        /// programmatically-constructed-but-unparsed LayoutItems behave the
        /// same as before. <see cref="TryParse"/> later replaces ImmutableData
        /// wholesale, so this seed is a no-op when parse runs.
        /// </summary>
        protected LayoutItem()
        {
            var def = new Dictionary<string, object>
            {
                { DefinitionIdKey, this.DefinitionId },
                { nameof(Margin) + "__def", "0" },
                { nameof(HorizontalAlignment) + "__def", HorizontalAlignment.Stretch },
                { nameof(VerticalAlignment) + "__def", VerticalAlignment.Stretch },
                { nameof(Scale) + "__def", -1.0 },
                { nameof(Width) + "__def", -1.0 },
                { nameof(Height) + "__def", -1.0 },
                { nameof(MinWidth) + "__def", -1.0 },
                { nameof(MinHeight) + "__def", -1.0 },
                { nameof(MaxWidth) + "__def", -1.0 },
                { nameof(MaxHeight) + "__def", -1.0 },
                { nameof(CanvasX) + "__def", -1.0 },
                { nameof(CanvasY) + "__def", -1.0 },
                { nameof(CanvasDepth) + "__def", -1.0 },
                { nameof(HitTestVisible) + "__def", true },
            };
            ImmutableData = new ImmutableKeyValueStore(def);
        }

        /// <summary>
        /// Outer parse entry point: populates <see cref="ImmutableData"/> with the
        /// parsed definition values (UniqueID + each <c>[KVOverridable]</c>
        /// property's <c>__def</c> slot) and then delegates to
        /// <see cref="TryParseInternal"/> for type-specific data. Subclasses
        /// continue to override <see cref="TryParseInternal"/> as before.
        /// </summary>
        protected bool TryParse(JObject data, IGamePackage package)
        {
            if (data == null)
                return false;

            // Build the definition store. SwapLeftRight is a parse-time, pack-wide
            // setting (read once per pack); the mirror logic is applied to the
            // definition values directly so forks all start from the mirrored
            // shape — same observable behavior as pre-Phase-4.
            var def = new Dictionary<string, object>
            {
                { DefinitionIdKey, this.DefinitionId },
            };

            string uniqueID = data.GetValue<string>("uid", null);
            if (!string.IsNullOrWhiteSpace(uniqueID))
            {
                def[nameof(UniqueID)] = uniqueID;
            }

            string background = data.GetValue<string>("background", null);
            string foreground = data.GetValue<string>("foreground", null);
            string dockLocation = data.GetValue<string>("dock", null);
            string margin = data.GetValue<string>("margin", "0");

            if (Tracker.Instance.SwapLeftRight)
            {
                bool bModified = false;

                if (string.Equals(dockLocation, "left", StringComparison.OrdinalIgnoreCase))
                {
                    dockLocation = "right";
                    bModified = true;
                }
                else if (string.Equals(dockLocation, "right", StringComparison.OrdinalIgnoreCase))
                {
                    dockLocation = "left";
                    bModified = true;
                }

                if (bModified && !string.IsNullOrWhiteSpace(margin))
                {
                    string[] tokens = margin.Split(',');
                    if (tokens.Length == 4)
                    {
                        margin = string.Format("{0},{1},{2},{3}", tokens[2], tokens[1], tokens[0], tokens[3]);
                    }
                }
            }

            def[nameof(Background) + "__def"] = background;
            def[nameof(Foreground) + "__def"] = foreground;
            def[nameof(DockLocation) + "__def"] = dockLocation;
            def[nameof(Margin) + "__def"] = margin;

            def[nameof(HorizontalAlignment) + "__def"] = data.GetEnumValue<HorizontalAlignment>("h_alignment", HorizontalAlignment.Stretch);
            def[nameof(VerticalAlignment) + "__def"] = data.GetEnumValue<VerticalAlignment>("v_alignment", VerticalAlignment.Stretch);
            def[nameof(Scale) + "__def"] = data.GetValue<double>("scale", -1.0);
            def[nameof(Width) + "__def"] = data.GetValue<double>("width", -1.0);
            def[nameof(Height) + "__def"] = data.GetValue<double>("height", -1.0);
            def[nameof(MinWidth) + "__def"] = data.GetValue<double>("min_width", -1.0);
            def[nameof(MinHeight) + "__def"] = data.GetValue<double>("min_height", -1.0);
            def[nameof(MaxWidth) + "__def"] = data.GetValue<double>("max_width", -1.0);
            def[nameof(MaxHeight) + "__def"] = data.GetValue<double>("max_height", -1.0);
            def[nameof(CanvasX) + "__def"] = data.GetValue<double>("canvas_left", -1.0);
            def[nameof(CanvasY) + "__def"] = data.GetValue<double>("canvas_top", -1.0);
            def[nameof(CanvasDepth) + "__def"] = data.GetValue<double>("canvas_depth", -1.0);
            def[nameof(DropShadow) + "__def"] = data.GetValue<bool>("dropshadow", false);
            def[nameof(BroadcastShadow) + "__def"] = data.GetValue<bool>("broadcast_shadow", false);
            def[nameof(HitTestVisible) + "__def"] = data.GetValue<bool>("hit_test_visible", true);

            // Subclass extension point: lets concrete leaves seed their own
            // [KVOverridable] / [KVImmutable] __def entries before the
            // ImmutableData store is frozen. Subclasses that hold no extra
            // definition state simply don't override.
            PopulateDefinitionData(data, package, def);

            ImmutableData = new ImmutableKeyValueStore(def);

            // UID registration is a side effect of parse, not of definition data —
            // every fork registers itself with the singleton LayoutManager today.
            // Phase 6 introduces per-state managers; the registration is wrapped
            // in a virtual hook so a per-state-aware override can swap the
            // target LayoutManager without touching this base class.
            if (!string.IsNullOrWhiteSpace(uniqueID))
            {
                RegisterUniqueID(uniqueID);
            }

            return TryParseInternal(data, package);
        }

        /// <summary>
        /// Registers <paramref name="uniqueID"/> on the appropriate
        /// <see cref="LayoutManager"/>. Defaults to the singleton; Phase 6
        /// per-state model graphs override to register against their state's
        /// manager. Also invoked on a fork's <see cref="OnForked"/> so the
        /// new instance is reachable by UID lookup in its target manager.
        /// </summary>
        protected virtual void RegisterUniqueID(string uniqueID)
        {
            // Phase 6 step 11: prefer the owning state's LayoutManager.
            var layouts = (this.OwnerState as Sessions.TrackerState)?.Layouts;
            layouts?.RegisterLayoutItemForUID(uniqueID, this);
        }

        /// <summary>
        /// Subclass extension point invoked from <see cref="TryParse"/> after the
        /// base layout-item definition keys are populated and before
        /// <see cref="ImmutableData"/> is frozen. Concrete leaves with their own
        /// <c>[KVOverridable]</c> or <c>[KVImmutable]</c> properties override this
        /// to add their <c>{Name}__def</c> entries to <paramref name="definition"/>.
        /// Subclasses with no type-specific definition state need not override.
        /// </summary>
        protected virtual void PopulateDefinitionData(JObject data, IGamePackage package, Dictionary<string, object> definition)
        {
        }

        protected void ParseLayoutItemList(JArray list, ICollection<LayoutItem> destination, IGamePackage package)
        {
            if (list != null)
            {
                foreach (JObject entry in list)
                {
                    LayoutItem item = CreateLayoutItem(entry, package);
                    if (item != null)
                        destination.Add(item);
                }
            }
        }

        protected abstract bool TryParseInternal(JObject data, IGamePackage package);

        public static LayoutItem CreateLayoutItem(JObject data, IGamePackage package)
        {
            if (data != null)
            {
                LayoutItem instance = JsonTypeTagsAttribute.CreateIntanceForTypeTag<LayoutItem>(data.GetValue<string>("type"));

                if (instance != null && instance.TryParse(data, package))
                    return instance;
            }

            return null;
        }

        // -------- Fork ------------------------------------------------------

        /// <summary>
        /// Default Activator-based Fork: allocates a fresh instance of the concrete
        /// type and runs <see cref="ModelTypeBase.InitializeAsForkOf"/>. Subclasses
        /// holding owned subtrees override to also walk + fork their children.
        /// Concrete leaves without owned subtrees inherit this default.
        /// </summary>
        public override ModelTypeBase Fork()
        {
            var copy = (LayoutItem)Activator.CreateInstance(this.GetType());
            copy.InitializeAsForkOf(this);
            return copy;
        }

        /// <summary>
        /// Phase 7 polish: enumerate this layout item's owned <see cref="LayoutItem"/>
        /// children. Used by <c>TrackerState.Fork</c>'s OwnerState-stamping walk
        /// so per-fork item / location resolution flows through the fork's
        /// resolver rather than the source's.
        /// <para>
        /// Subclasses with owned children (Container, DockPanel, CanvasPanel,
        /// ArrayPanel, TabPanel, ButtonPopup, ScrollPanel, ViewBox, etc.)
        /// override to yield their children. Leaves return empty.
        /// </para>
        /// </summary>
        public virtual System.Collections.Generic.IEnumerable<LayoutItem> EnumerateChildren()
        {
            yield break;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            // Re-register the fork's UID with its target LayoutManager — under
            // Phase 4's singleton model this just re-points the UID at the
            // fork (overwriting the source's registration); Phase 6 per-state
            // managers will record the fork against its own state's manager
            // and the source remains reachable in its own state's manager.
            // Documented Phase 4 limitation: see plan §4.8.
            string uid = UniqueID;
            if (!string.IsNullOrWhiteSpace(uid))
            {
                RegisterUniqueID(uid);
            }
        }
    }
}
