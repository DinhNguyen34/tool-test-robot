using Common.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;

namespace WinStyles.Converter
{
    public class TileTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            SolidColorBrush brush = new SolidColorBrush();
            TileType type = (TileType)value;
            switch (type)
            {
                case TileType.Mortor: brush.Color = Colors.DarkViolet; break;
                case TileType.Head: brush.Color = Colors.Maroon; break;
                case TileType.Arm: brush.Color = Colors.DarkOliveGreen; break;
                case TileType.Leg: brush.Color = Colors.DodgerBlue; break;
                case TileType.Body: brush.Color = Colors.DarkGoldenrod; break;
                case TileType.Hand: brush.Color = Colors.DarkOrange; break;
                case TileType.LLB: brush.Color = Colors.Navy; break;
                case TileType.Network: brush.Color = Colors.SaddleBrown; break;
                case TileType.Led: brush.Color = Colors.Teal; break;
            }
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
