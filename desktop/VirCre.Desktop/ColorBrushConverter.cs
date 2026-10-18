using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VirCre.Desktop;

public sealed class ColorBrushConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, string language) {
        var hex = ((string)value).TrimStart('#');
        return new SolidColorBrush(Color.FromArgb(255, System.Convert.ToByte(hex[..2], 16),
            System.Convert.ToByte(hex[2..4], 16), System.Convert.ToByte(hex[4..6], 16)));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
