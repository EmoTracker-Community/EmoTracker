using EmoTracker.Core;
using EmoTracker.Data.Packages;
using System;
using System.Globalization;

using Avalonia.Data.Converters;

namespace EmoTracker.UI.Converters
{
    public class GameNameToActualGameNameConverter : Singleton<GameNameToActualGameNameConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string name = null;
            if (value != null)
                name = value.ToString();

            var game = PackageManager.Instance.FindGame(name);
            if (game != null)
                return game.Name;

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class GameNameToActualGameImageConverter : Singleton<GameNameToActualGameImageConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string name = null;
            if (value != null)
                name = value.ToString();

            var game = PackageManager.Instance.FindGame(name);
            if (game != null)
                return Media.ImageReferenceService.Instance.ResolveImageReference(game.Image);

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
