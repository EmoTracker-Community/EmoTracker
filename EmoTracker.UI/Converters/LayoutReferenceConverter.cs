using EmoTracker.Core;
using EmoTracker.Data.Layout;
using System;
using System.Globalization;

#if WINDOWS
using System.Windows.Data;
#else
using Avalonia.Data.Converters;
#endif

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
                    return LayoutManager.Instance.FindLayout(layoutName);
                }
                catch { }
            }

            if (parameter != null)
            {
                try
                {
                    string layoutName = parameter.ToString();
                    return LayoutManager.Instance.FindLayout(layoutName);
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
