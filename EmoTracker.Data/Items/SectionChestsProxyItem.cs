using System;
using System.Collections.Generic;
using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("sectionchests")]
    public partial class SectionChestsProxyItem : ItemBase
    {
        // Definition data: parsed once at pack-load.
        CodeProvider mCodeProvider = new CodeProvider();

        // Cross-reference (Phase 3 retrofit, deferred from Phase 2.5 because
        // Section gained DefinitionId via ModelTypeBase only here in Phase 3).
        // ModelReference<Section>: identity carried across forks via ForFork(this);
        // Target resolves through this item's GetModelResolver() (singleton-backed
        // ambient resolver in Phase 3, per-state when state lifecycle lands).
        ModelReference<Section> mSectionRef;
        // Tracks the previous resolved target for clean PropertyChanged unwiring.
        Section mSubscribedSection;

        public SectionChestsProxyItem()
        {
            mSectionRef = new ModelReference<Section>(this);
            Tracker.Instance.PropertyChanged += Tracker_PropertyChanged;
        }

        public Section Section
        {
            get { return mSectionRef.Target; }
            protected set
            {
                var current = mSectionRef.Target;
                if (ReferenceEquals(current, value)) return;
                NotifyPropertyChanging();
                if (mSubscribedSection != null)
                {
                    mSubscribedSection.PropertyChanged -= Section_PropertyChanged;
                    mSubscribedSection = null;
                }
                mSectionRef.Set(value);
                if (value != null)
                {
                    value.PropertyChanged += Section_PropertyChanged;
                    mSubscribedSection = value;
                }
                NotifyPropertyChanged();
                RefreshState();
            }
        }

        private void Tracker_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "AlwaysAllowClearing")
                RefreshState();
        }

        private void Section_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RefreshState();
        }

        public uint Count
        {
            get
            {
                var s = Section;
                if (s != null)
                    return s.AvailableChestCount;

                return 0;
            }
        }

        bool AllowManipulation
        {
            get
            {
                var s = Section;
                if (s != null && s.Visible)
                {
                    if (ApplicationSettings.Instance.AlwaysAllowClearing || s.AccessibilityLevel != AccessibilityLevel.None)
                        return true;
                }

                return false;
            }
        }

        public override void AdvanceToCode(string code = null)
        {
        }

        public override bool CanProvideCode(string code)
        {
            return mCodeProvider.ProvidesCode(code);
        }

        public override IEnumerable<string> GetAllProvidedCodes() => mCodeProvider.ProvidedCodes;

        public override void OnLeftClick()
        {
            var s = Section;
            if (AllowManipulation && s.AvailableChestCount > 0)
                --s.AvailableChestCount;
        }

        public override void OnRightClick()
        {
            var s = Section;
            if (AllowManipulation && s.AvailableChestCount < s.ChestCount)
                ++s.AvailableChestCount;
        }

        public override uint ProvidesCode(string code)
        {
            if (CanProvideCode(code))
                return Count;

            return 0;
        }

        private void RefreshState()
        {
            var s = Section;
            if (s != null)
            {
                BadgeText = Count.ToString();

                if (Count >= s.ChestCount)
                    BadgeTextColor = "#00ff00";
                else
                    BadgeTextColor = "WhiteSmoke";

                if (Count == 0)
                    Icon = AllowManipulation ? s.OpenChestImage : s.UnavailableOpenChestImage;
                else
                    Icon = AllowManipulation ? s.ClosedChestImage : s.UnavailableClosedChestImage;
            }
        }

        protected override void ParseDataInternal(JObject data, IGamePackage package)
        {
            mCodeProvider.AddCodes(data.GetValue<string>("codes"));
            Section = Tracker.Instance.FindObjectForCode(data.GetValue<string>("section")) as Section;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (SectionChestsProxyItem)source;
            mCodeProvider = src.mCodeProvider;

            // Carry Section reference by identity. ForFork(this) gives us a
            // fresh ModelReference bound to this fork — same DefinitionId, no
            // cache.
            mSectionRef = src.mSectionRef.ForFork(this);

            // Re-subscribe to the (fork's) resolved Section's PropertyChanged.
            // Done explicitly rather than via the Section setter to avoid the
            // setter's ReferenceEquals shortcut (the resolved Target is the
            // same instance as the source's via the singleton resolver, so
            // the setter would early-exit and skip the resubscribe).
            mSubscribedSection = null;
            var resolved = mSectionRef.Target;
            if (resolved != null)
            {
                resolved.PropertyChanged += Section_PropertyChanged;
                mSubscribedSection = resolved;
            }
        }
    }
}
