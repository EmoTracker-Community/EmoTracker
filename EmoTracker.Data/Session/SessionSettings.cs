using EmoTracker.Core;

namespace EmoTracker.Data.Session
{
    /// <summary>
    /// Session-scoped tracker flags. Phase 2: thin facade forwarding to the
    /// shared <see cref="ApplicationSettings"/> store so persistence behavior
    /// is preserved. Later phases will migrate the backing state here so each
    /// forked session can carry independent values.
    /// </summary>
    public class SessionSettings : ObservableObject
    {
        readonly ApplicationSettings mBacking;

        public SessionSettings(ApplicationSettings backing)
        {
            mBacking = backing;
            mBacking.PropertyChanged += (_, e) => NotifyPropertyChanged(e.PropertyName);
        }

        public bool IgnoreAllLogic
        {
            get => mBacking.IgnoreAllLogic;
            set => mBacking.IgnoreAllLogic = value;
        }

        public bool DisplayAllLocations
        {
            get => mBacking.DisplayAllLocations;
            set => mBacking.DisplayAllLocations = value;
        }

        public bool AlwaysAllowClearing
        {
            get => mBacking.AlwaysAllowClearing;
            set => mBacking.AlwaysAllowClearing = value;
        }

        public bool AutoUnpinLocationsOnClear
        {
            get => mBacking.AutoUnpinLocationsOnClear;
            set => mBacking.AutoUnpinLocationsOnClear = value;
        }

        public bool PinLocationsOnItemCapture
        {
            get => mBacking.PinLocationsOnItemCapture;
            set => mBacking.PinLocationsOnItemCapture = value;
        }
    }
}
