using EmoTracker.Core;
using EmoTracker.Data.JSON;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace EmoTracker.Data.Settings
{
    public class ApplicationColors : ObservableSingleton<ApplicationColors>
    {
        #region -- Accessibility Colors --

        string mAccessibilityColor_None = "#ff3030";
        string mAccessibilityColor_Partial = "DarkOrange";
        string mAccessibilityColor_Unlockable = "MediumPurple";
        string mAccessibilityColor_Inspect = "CornflowerBlue";
        string mAccessibilityColor_Glitch = "#b399c1";
        string mAccessibilityColor_SequenceBreak = "Yellow";
        string mAccessibilityColor_Normal = "#00ff00";
        string mAccessibilityColor_Cleared = "#333333";

        public string AccessibilityColor_None
        {
            get { return mAccessibilityColor_None; }
            set { SetProperty(ref mAccessibilityColor_None, value); }
        }
        public string AccessibilityColor_Partial
        {
            get { return mAccessibilityColor_Partial; }
            set { SetProperty(ref mAccessibilityColor_Partial, value); }
        }
        public string AccessibilityColor_Unlockable
        {
            get { return mAccessibilityColor_Unlockable; }
            set { SetProperty(ref mAccessibilityColor_Unlockable, value); }
        }
        public string AccessibilityColor_Inspect
        {
            get { return mAccessibilityColor_Inspect; }
            set { SetProperty(ref mAccessibilityColor_Inspect, value); }
        }
        public string AccessibilityColor_SequenceBreak
        {
            get { return mAccessibilityColor_SequenceBreak; }
            set { SetProperty(ref mAccessibilityColor_SequenceBreak, value); }
        }
        public string AccessibilityColor_Glitch
        {
            get { return mAccessibilityColor_Glitch; }
            set { SetProperty(ref mAccessibilityColor_Glitch, value); }
        }
        public string AccessibilityColor_Normal
        {
            get { return mAccessibilityColor_Normal; }
            set { SetProperty(ref mAccessibilityColor_Normal, value); }
        }
        public string AccessibilityColor_Cleared
        {
            get { return mAccessibilityColor_Cleared; }
            set { SetProperty(ref mAccessibilityColor_Cleared, value); }
        }

        #endregion

        #region -- Status Indicator Colors --

        string mStatus_Generic_Success = "#00ff00";

        public string Status_Generic_Success
        {
            get { return mStatus_Generic_Success; }
            set { SetProperty(ref mStatus_Generic_Success, value); }
        }

        string mStatus_Generic_Error = "#ff0000";

        public string Status_Generic_Error
        {
            get { return mStatus_Generic_Error; }
            set { SetProperty(ref mStatus_Generic_Error, value); }
        }

        string mStatus_Generic_Warning = "#ffff00";

        public string Status_Generic_Warning
        {
            get { return mStatus_Generic_Warning; }
            set { SetProperty(ref mStatus_Generic_Warning, value); }
        }

        string mStatus_Generic_Active = "#35e0b5";

        public string Status_Generic_Active
        {
            get { return mStatus_Generic_Active; }
            set { SetProperty(ref mStatus_Generic_Active, value); }
        }

        #endregion

        #region -- Miscellaneous --

        string mMap_LocationNoteBadgeBackground = "#35e0b5";

        public string Map_LocationNoteBadgeBackground
        {
            get { return mMap_LocationNoteBadgeBackground; }
            set { SetProperty(ref mMap_LocationNoteBadgeBackground, value); }
        }

        #endregion

        public ApplicationColors()
        {
            LoadColors();
        }

        public void ResetColors()
        {
            AccessibilityColor_None = "#ff3030";
            AccessibilityColor_Partial = "DarkOrange";
            AccessibilityColor_Unlockable = "MediumPurple";
            AccessibilityColor_Inspect = "CornflowerBlue";
            AccessibilityColor_Glitch = "#b399c1";
            AccessibilityColor_SequenceBreak = "Yellow";
            AccessibilityColor_Normal = "#00ff00";
            AccessibilityColor_Cleared = "#333333";

            Status_Generic_Success = "#00ff00";
            Status_Generic_Warning = "#ffff00";
            Status_Generic_Error = "#ff0000";
            Status_Generic_Active = "#35e0b5";

            Map_LocationNoteBadgeBackground = "#35e0b5";
        }

        public void LoadColors()
        {
            ResetColors();

            string path = Path.Combine(UserDirectory.Path, "application_colors.json");
            if (File.Exists(path))
            {
                try
                {
                    using (StreamReader reader = new StreamReader(File.OpenRead(Path.Combine(UserDirectory.Path, "application_colors.json"))))
                    {
                        JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));

                        AccessibilityColor_None = root.GetValue<string>("accessibility_none", AccessibilityColor_None);
                        AccessibilityColor_Partial = root.GetValue<string>("accessibility_partial", AccessibilityColor_Partial);
                        AccessibilityColor_Unlockable = root.GetValue<string>("accessibility_unlockable", AccessibilityColor_Unlockable);
                        AccessibilityColor_Inspect = root.GetValue<string>("accessibility_inspect", AccessibilityColor_Inspect);
                        AccessibilityColor_SequenceBreak = root.GetValue<string>("accessibility_sequencebreak", AccessibilityColor_SequenceBreak);
                        AccessibilityColor_Normal = root.GetValue<string>("accessibility_normal", AccessibilityColor_Normal);
                        AccessibilityColor_Cleared = root.GetValue<string>("accessibility_cleared", AccessibilityColor_Cleared);

                        Status_Generic_Active = root.GetValue<string>("status_generic_active", Status_Generic_Active);
                        Status_Generic_Success = root.GetValue<string>("status_generic_success", Status_Generic_Success);
                        Status_Generic_Warning = root.GetValue<string>("status_generic_warning", Status_Generic_Warning);
                        Status_Generic_Error = root.GetValue<string>("status_generic_error", Status_Generic_Error);

                        Map_LocationNoteBadgeBackground = root.GetValue<string>("map_location_has_note_badge_background", Map_LocationNoteBadgeBackground);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
 