#nullable enable annotations
using EmoTracker.Core;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Scripting;
using EmoTracker.Data.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EmoTracker.UI.Converters
{
    /// <summary>
    /// Converts a colour name or hex string (e.g. "#ff3030", "DarkOrange") to an <see cref="IBrush"/>.
    /// Returns <c>null</c> on failure so that FallbackValue can kick in.
    /// </summary>
    public class StringToBrushConverter : Singleton<StringToBrushConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try
                {
                    return Brush.Parse(s);
                }
                catch { }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts an <see cref="AccessibilityLevel"/> to the matching <see cref="IBrush"/> colour
    /// taken from <see cref="ApplicationColors.Instance"/>.
    /// </summary>
    public class AccessibilityLevelToBrushConverter : Singleton<AccessibilityLevelToBrushConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string colorStr = "#333333";
            if (value is AccessibilityLevel level)
            {
                var c = ApplicationColors.Instance;
                colorStr = level switch
                {
                    AccessibilityLevel.Normal        => c.AccessibilityColor_Normal,
                    AccessibilityLevel.Cleared       => c.AccessibilityColor_Cleared,
                    AccessibilityLevel.None          => c.AccessibilityColor_None,
                    AccessibilityLevel.Partial       => c.AccessibilityColor_Partial,
                    AccessibilityLevel.Inspect       => c.AccessibilityColor_Inspect,
                    AccessibilityLevel.SequenceBreak => c.AccessibilityColor_SequenceBreak,
                    AccessibilityLevel.Glitch        => c.AccessibilityColor_Glitch,
                    AccessibilityLevel.Unlockable    => c.AccessibilityColor_Unlockable,
                    _                                => "#333333"
                };
            }

            try
            {
                return Brush.Parse(colorStr);
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts a <c>bool</c> to an Avalonia <see cref="DropShadowDirectionEffect"/>
    /// when <c>true</c>, or <c>null</c> when <c>false</c>.
    /// Mirrors the WPF DropShadowEffect (BlurRadius=15, ShadowDepth=0, Opacity=0.8) which produces
    /// a centred glow with no directional offset.
    /// </summary>
    public class BoolToDropShadowEffectConverter : Singleton<BoolToDropShadowEffectConverter>, IValueConverter
    {
        private static readonly DropShadowDirectionEffect s_effect =
            new DropShadowDirectionEffect
            {
                BlurRadius  = 15,
                ShadowDepth = 0,
                Opacity     = 0.8,
                Color       = Colors.Black,
            };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? (object)s_effect : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts a <see cref="NotificationType"/> to its matching accent <see cref="IBrush"/>
    /// for the notification left-border stripe. Colors are resolved live from
    /// <see cref="ApplicationColors.Instance"/> so user configuration is respected.
    /// </summary>
    public class NotificationTypeToBrushConverter : Singleton<NotificationTypeToBrushConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var c = ApplicationColors.Instance;
            string colorStr = value is NotificationType t ? t switch
            {
                NotificationType.Celebration => c.Notification_Celebration,
                NotificationType.Warning     => c.Notification_Warning,
                NotificationType.Error       => c.Notification_Error,
                _                            => c.Notification_Message,
            } : c.Notification_Message;

            try { return Brush.Parse(colorStr); }
            catch { return Brush.Parse(c.Notification_Message); }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Multi-value converter for the Package Manager button foreground color.
    /// Replicates WPF DataTrigger priority: !AnyPackagesInstalled → Active,
    /// CurrentPackageHasUpdateAvailable → Warning, UpdatesAvailable → Active, else default gray.
    /// <para>values[0] = UpdatesAvailable (bool), values[1] = CurrentPackageHasUpdateAvailable (bool),
    /// values[2] = AnyPackagesInstalled (bool).</para>
    /// </summary>
    public class PackageManagerForegroundConverter : Singleton<PackageManagerForegroundConverter>, IMultiValueConverter
    {
        private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#717171"));

        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            bool updatesAvailable = values.Count > 0 && values[0] is true;
            bool currentHasUpdate = values.Count > 1 && values[1] is true;
            bool anyInstalled     = values.Count > 2 && values[2] is true;

            // WPF trigger priority: last matching trigger wins.
            // Order: UpdatesAvailable, CurrentPackageHasUpdateAvailable, !AnyPackagesInstalled
            if (!anyInstalled)
                return new SolidColorBrush(Color.Parse(
                    ApplicationColors.Instance.Status_Generic_Active));
            if (currentHasUpdate)
                return new SolidColorBrush(Color.Parse(
                    ApplicationColors.Instance.Status_Generic_Warning));
            if (updatesAvailable)
                return new SolidColorBrush(Color.Parse(
                    ApplicationColors.Instance.Status_Generic_Active));

            return DefaultBrush;
        }
    }
}
