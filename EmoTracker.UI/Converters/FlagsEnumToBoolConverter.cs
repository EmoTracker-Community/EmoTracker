using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace EmoTracker.UI.Converters
{
    public class FlagsEnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                Enum valueEnum = value as Enum;
                if (valueEnum != null)
                {
                    Type enumType = valueEnum.GetType();
                    Enum paramValue = (Enum)Enum.Parse(enumType, parameter.ToString(), true);
                    if (valueEnum.HasFlag(paramValue))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
