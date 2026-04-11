using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Data;

namespace ModuleCamera
{
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }
    }
}
