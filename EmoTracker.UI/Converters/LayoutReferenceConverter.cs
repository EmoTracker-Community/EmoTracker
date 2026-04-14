using EmoTracker.Core;
using EmoTracker.Data.Layout;
using System;
using System.Globalization;

using Avalonia.Data.Converters;
using EmoTracker.Data.Session;

namespace EmoTracker.UI.Converters
{
    public class LayoutReferenceConverter : Singleton<LayoutReferenceConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                try
                {
                    string layoutName = value.ToString();
                    return TrackerSession.Current.Layouts.FindLayout(layoutName);
                }
                catch { }
            }

            if (parameter != null)
            {
                try
                {
                    string layoutName = parameter.ToString();
                    return TrackerSession.Current.Layouts.FindLayout(layoutName);
                }
                catch { }
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
