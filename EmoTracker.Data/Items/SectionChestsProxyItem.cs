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
        // Definition data.
        CodeProvider mCodeProvider = new CodeProvider();
        // Cached resolution code so OnForked can re-resolve through the singleton
        // Tracker (Phase 2 known limitation: resolves to the original state's
        // section).
        string mSectionCode;

        // Cross-reference: stored as a private field, not in MutableData. The
        // setter manages the PropertyChanged subscription.
        Section mSection;
        public Section Section
        {
            get { return mSection; }
            protected set
            {
                Section prevSection = mSection;
                if (!ReferenceEquals(prevSection, value))
                {
                    NotifyPropertyChanging();
                    if (prevSection != null) prevSection.PropertyChanged -= Section_PropertyChanged;
                    mSection = value;
                    if (mSection != null) mSection.PropertyChanged += Section_PropertyChanged;
                    NotifyPropertyChanged();
                    RefreshState();
                }
            }
        }

        public SectionChestsProxyItem()
        {
            Tracker.Instance.PropertyChanged += Tracker_PropertyChanged;
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
                if (Section != null)
                    return Section.AvailableChestCount;

                return 0;
            }
        }

        bool AllowManipulation
        {
            get
            {
                if (Section != null && Section.Visible)
                {
                    if (ApplicationSettings.Instance.AlwaysAllowClearing || Section.AccessibilityLevel != AccessibilityLevel.None)
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
            if (AllowManipulation && Section.AvailableChestCount > 0)
                --Section.AvailableChestCount;
        }

        public override void OnRightClick()
        {
            if (AllowManipulation && Section.AvailableChestCount < Section.ChestCount)
                ++Section.AvailableChestCount;
        }

        public override uint ProvidesCode(string code)
        {
            if (CanProvideCode(code))
                return Count;

            return 0;
        }
        private void RefreshState()
        {
            if (Section != null)
            {
                BadgeText = Count.ToString();

                if (Count >= Section.ChestCount)
                    BadgeTextColor = "#00ff00";
                else
                    BadgeTextColor = "WhiteSmoke";

                if (Count == 0)
                    Icon = AllowManipulation ? Section.OpenChestImage : Section.UnavailableOpenChestImage;
                else
                    Icon = AllowManipulation ? Section.ClosedChestImage : Section.UnavailableClosedChestImage;
            }
        }

        protected override void ParseDataInternal(JObject data, IGamePackage package)
        {
            mCodeProvider.AddCodes(data.GetValue<string>("codes"));
            mSectionCode = data.GetValue<string>("section");
            Section = Tracker.Instance.FindObjectForCode(mSectionCode) as Section;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (SectionChestsProxyItem)source;
            mCodeProvider = src.mCodeProvider;
            mSectionCode = src.mSectionCode;
            // Re-resolve via the singleton Tracker. Phase 2 known limitation
            // (per §2.3): this resolves to the original state's Section
            // instance.
            Section = Tracker.Instance.FindObjectForCode(mSectionCode) as Section;
        }
    }
}
