using EmoTracker.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

using Avalonia.Data.Converters;

namespace EmoTracker.UI.Converters
{
    /// <summary>
    /// Multi-value converter for the Package Manager button tooltip.
    /// <para>values[0] = UpdatesAvailable (bool), values[1] = CurrentPackageHasUpdateAvailable (bool),
    /// values[2] = AnyPackagesInstalled (bool).</para>
    /// </summary>
    public class PackageManagerTooltipConverter : Singleton<PackageManagerTooltipConverter>, IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            bool updatesAvailable = values.Count > 0 && values[0] is true;
            bool currentHasUpdate = values.Count > 1 && values[1] is true;
            bool anyInstalled     = values.Count > 2 && values[2] is true;

            if (!anyInstalled) return "Install your first package!";
            if (currentHasUpdate || updatesAvailable) return "Package Updates Are Available";
            return "Package Manager";
        }
    }

    public class EnspacenCamelCaseConverter : Singleton<EnspacenCamelCaseConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                return Regex.Replace(value.ToString(), "([a-z](?=[A-Z0-9])|[A-Z](?=[A-Z][a-z]))", "$1 ");
            }
            catch { }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
