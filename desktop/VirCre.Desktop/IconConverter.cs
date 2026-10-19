using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace VirCre.Desktop;

public sealed class IconConverter : IValueConverter {
    public object? Convert(object value, Type targetType, object parameter, string language) {
        var path=value as string;
        if(parameter as string=="fallback") return string.IsNullOrEmpty(path)?Visibility.Visible:Visibility.Collapsed;
        return string.IsNullOrEmpty(path)?null:new BitmapImage(new Uri(path));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language)=>throw new NotSupportedException();
}
