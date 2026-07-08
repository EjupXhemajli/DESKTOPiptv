using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ExIptv.Converters;

/// <summary>
/// Wandelt eine Bild-URL in ein dekodiertes BitmapImage. Begrenzt die Dekodiergröße
/// (Speicher) und liefert bei fehlender/ungültiger URL null, ohne zu werfen. Async-Ladefehler
/// (404 o. Ä.) landen still im ImageFailed-Ereignis des Image-Elements – kein Absturz.
/// </summary>
public sealed class UrlToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.DecodePixelWidth = 200;                 // kleine Dekodierung -> wenig Speicher
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.DelayCreation;
            bmp.EndInit();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
