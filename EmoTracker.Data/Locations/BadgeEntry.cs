using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Media;

namespace EmoTracker.Data.Locations
{
    public partial class BadgeEntry : ModelTypeBase
    {
        // Key is set once at construction (it's the badge's identity); ImmutableData
        // is the natural home so it's shared by reference across forks.
        [KVImmutable]
        public partial string Key { get; }

        // Image / offsets are runtime-mutable per-state.
        [KVMutable]
        public partial ImageReference Image { get; set; }

        [KVMutable]
        public partial double OffsetX { get; set; }

        [KVMutable]
        public partial double OffsetY { get; set; }

        // Parameterless ctor so Fork's Activator path can construct fresh instances.
        public BadgeEntry()
        {
        }

        public BadgeEntry(string key, ImageReference image, double offsetX = 0, double offsetY = 0)
        {
            // Seed Key into ImmutableData (preserving the auto-generated DefinitionId
            // from the base ctor).
            var seed = new System.Collections.Generic.Dictionary<string, object>
            {
                { ModelTypeBase.DefinitionIdKey, this.DefinitionId },
                { nameof(Key), key },
            };
            this.ImmutableData = new ImmutableKeyValueStore(seed);

            Image = image;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public override ModelTypeBase Fork()
        {
            var copy = new BadgeEntry();
            copy.InitializeAsForkOf(this);
            return copy;
        }
    }
}
