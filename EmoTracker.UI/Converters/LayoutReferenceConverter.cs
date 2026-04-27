using EmoTracker.Core;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Sessions;
using System;
using System.Globalization;

using Avalonia.Data.Converters;

namespace EmoTracker.UI.Converters
{
    public class LayoutReferenceConverter : Singleton<LayoutReferenceConverter>, IValueConverter
    {
        // Resolver hook installed by the host (ApplicationModel) at startup.
        // Evaluates to the LayoutManager that should resolve a referenced
        // layout key — typically the currently-focused window's active state.
        // Avalonia value converters have no access to the binding's holder
        // context, so this resolver is the threading point. No ambient slot
        // is held here; the resolver consults real state on each call.
        public static Func<LayoutManager> ActiveLayoutsResolver { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var layouts = ActiveLayoutsResolver?.Invoke();
            if (layouts == null) return null;

            if (value != null)
            {
                try
                {
                    return layouts.FindLayout(value.ToString());
                }
                catch { }
            }

            if (parameter != null)
            {
                try
                {
                    return layouts.FindLayout(parameter.ToString());
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
