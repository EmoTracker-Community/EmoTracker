using EmoTracker.Core;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Sessions;
using System;
using System.Globalization;

using Avalonia.Data.Converters;

// Phase 7.1: XAML converters route through the active state's
// LayoutManager via SessionContext. Per-window scoping arrives in
// Phase 7.6 (the converter currently picks up whichever window is
// active globally).

namespace EmoTracker.UI.Converters
{
    public class LayoutReferenceConverter : Singleton<LayoutReferenceConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var layouts = SessionContext.ActiveState?.Layouts;
            if (layouts == null) return null;

            if (value != null)
            {
                try
                {
                    return layouts?.FindLayout(value.ToString());
                }
                catch { }
            }

            if (parameter != null)
            {
                try
                {
                    return layouts?.FindLayout(parameter.ToString());
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
