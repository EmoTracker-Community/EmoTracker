#nullable enable annotations
using EmoTracker.Core;
using EmoTracker.Data.Locations;
using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace EmoTracker.UI.Converters
{
    /// <summary>
    /// Returns <c>true</c> when the value is null or unset, <c>false</c> when non-null.
    /// Use for IsVisible bindings where the element should appear when no data is present.
    /// </summary>
    public class NullToTrueConverter : Singleton<NullToTrueConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null || value == AvaloniaProperty.UnsetValue || value == BindingOperations.DoNothing;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>true</c> when the value is non-null and non-unset, <c>false</c> otherwise.
    /// </summary>
    public class NullToFalseConverter : Singleton<NullToFalseConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null && value != AvaloniaProperty.UnsetValue && value != BindingOperations.DoNothing;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>true</c> when the value is a non-null, non-empty string.
    /// Treats UnsetValue and DoNothing as empty on Avalonia.
    /// </summary>
    public class NonEmptyStringToBoolConverter : Singleton<NonEmptyStringToBoolConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string s && s.Length > 0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>true</c> when the integer/uint value is non-zero.
    /// </summary>
    public class NonZeroToBoolConverter : Singleton<NonZeroToBoolConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i) return i != 0;
            if (value is uint u) return u != 0;
            if (value is long l) return l != 0;
            if (value is double d) return d != 0;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Inverts a <see cref="bool"/> value.
    /// </summary>
    public class BoolInverseConverter : Singleton<BoolInverseConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

    /// <summary>
    /// Alias for <see cref="BoolInverseConverter"/> — inverts a <see cref="bool"/> value.
    /// </summary>
    public class InverseBoolConverter : Singleton<InverseBoolConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

    /// <summary>
    /// Returns <c>true</c> when the value's <c>ToString()</c> matches the <c>ConverterParameter</c>
    /// string (case-insensitive).  Useful for controlling IsVisible based on an enum property.
    /// <example><c>IsVisible="{Binding Style, Converter={x:Static converters:ObjectEqualsConverter.Instance}, ConverterParameter=Settings}"</c></example>
    /// </summary>
    public class ObjectEqualsConverter : Singleton<ObjectEqualsConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Multi-value converter that replicates WPF's MultiDataTrigger-based map location
    /// visibility logic. Evaluates (in priority order): ForceInvisible, ForceVisible,
    /// HasVisibleSections, Cleared+DisplayAll+Shift, Empty+DisplayAll+Shift.
    /// <para>Binding order:
    /// [0] ForceVisible (bool), [1] ForceInvisible (bool),
    /// [2] Location.HasVisibleSections (bool), [3] Location.AccessibilityLevel (enum),
    /// [4] Location.HasAvailableItems (bool), [5] Location.Badges.Count (int),
    /// [6] Location.NoteTakingSite.Empty (bool),
    /// [7] DisplayAllLocations (bool), [8] IsShiftPressed (bool).</para>
    /// </summary>
    public class MapLocationVisibilityConverter : Singleton<MapLocationVisibilityConverter>, IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count < 9) return true;

            bool forceVisible       = values[0] is true;
            bool forceInvisible     = values[1] is true;
            bool hasVisibleSections = values[2] is true;
            bool isCleared          = values[3] is AccessibilityLevel level
                                      && level == AccessibilityLevel.Cleared;
            bool hasAvailableItems  = values[4] is true;
            int  badgeCount         = values[5] is int bc ? bc : 0;
            bool notesEmpty         = values[6] is not false; // default true when null/unset
            bool displayAll         = values[7] is true;
            bool shiftPressed       = values[8] is true;

            // Highest priority: script-driven force rules (last WPF triggers win)
            if (forceInvisible) return false;
            if (forceVisible)   return true;

            // No visible sections at all → hide
            if (!hasVisibleSections) return false;

            // Cleared location hidden unless DisplayAll or Shift
            if (isCleared && !displayAll && !shiftPressed) return false;

            // Empty location (no items, badges, or notes) hidden unless DisplayAll or Shift
            if (!hasAvailableItems && badgeCount == 0 && notesEmpty && !displayAll && !shiftPressed)
                return false;

            return true;
        }
    }

    /// <summary>
    /// Replicates the WPF MultiDataTrigger that controlled chest accessibility.
    /// Returns <c>false</c> (inaccessible / grayed-out / disabled) when all three conditions
    /// are true: AccessibilityLevel == None, AlwaysAllowChestManipulation == false, and
    /// AlwaysAllowClearing == false.  Returns <c>true</c> in all other cases.
    /// <para>Binding order:
    /// [0] AccessibilityLevel, [1] AlwaysAllowChestManipulation (bool),
    /// [2] ApplicationSettings.AlwaysAllowClearing (bool).</para>
    /// </summary>
    public class ChestAccessibleConverter : Singleton<ChestAccessibleConverter>, IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count < 3) return true;
            bool isNone             = values[0] is AccessibilityLevel level && level == AccessibilityLevel.None;
            bool alwaysAllowSection = values[1] is true;
            bool alwaysAllowGlobal  = values[2] is true;
            return !(isNone && !alwaysAllowSection && !alwaysAllowGlobal);
        }
    }
}
