using EmoTracker.Core;
using EmoTracker.Data.Layout;
using System;
using System.Globalization;

using Avalonia.Data.Converters;

// Phase 6 step 11: XAML converters cannot reach the per-state graph from
// converter context (no holder available). Routing through the singleton
// LayoutManager is acceptable until the multi-window UI work introduces
// a converter-context state pointer.
#pragma warning disable CS0618

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
