using System;
using System.Collections.Generic;
using EmoTracker.Core;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("sectionchests")]
    public class SectionChestsProxyItem : ItemBase
    {
        CodeProvider mCodeProvider = new CodeProvider();

        Section mSection;
        public Section Section
        {
            get { return mSection; }
            protected set
            {
                Section prevSection = mSection;
                if (SetProperty(ref mSection, value))
                {
                    if (prevSection != null)
                        prevSection.PropertyChanged -= Section_PropertyChanged;

                    if (mSection != null)
                        mSection.PropertyChanged += Section_PropertyChanged;

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
            Section = Tracker.Instance.FindObjectForCode(data.GetValue<string>("section")) as Section;
        }       
    }
}
