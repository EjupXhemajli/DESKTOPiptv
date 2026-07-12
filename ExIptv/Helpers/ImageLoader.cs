using System.Collections.Concurrent;
using System.Threading;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ExIptv.Helpers;

/// <summary>
/// Lädt Bilder für ein Image-Element über HttpClient statt über BitmapImage.UriSource.
/// Grund: WPFs eingebauter Web-Loader sendet keinen User-Agent, scheitert oft an Redirects
/// und an den (bei IPTV üblichen) ungültigen Zertifikaten. Nutzung im XAML:
///   &lt;Image helpers:ImageLoader.SourceUrl="{Binding LogoUrl}"/&gt;
/// Dekodierung läuft im Hintergrund, Ergebnisse werden gecacht.
/// </summary>
public static class ImageLoader
{
    private static readonly HttpClient _http = CreateClient();
    private static readonly ConcurrentDictionary<string, BitmapImage> _cache = new();
    // Höchstens wenige parallele Downloads – sonst konkurrieren hunderte Poster-Requests
    // mit dem laufenden Video-Stream um Bandbreite (Ruckeln/Einfrieren).
    private static readonly SemaphoreSlim _gate = new(4);
    // Cache-Deckel: ~800 Bilder à ~240 KB (200 px dekodiert) ≈ 190 MB Obergrenze.
    private const int CacheLimit = 800;

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            // IPTV-Bildserver haben häufig ungültige/self-signed Zertifikate.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36");
        return client;
    }

    public static readonly DependencyProperty SourceUrlProperty =
        DependencyProperty.RegisterAttached(
            "SourceUrl", typeof(string), typeof(ImageLoader),
            new PropertyMetadata(null, OnSourceUrlChanged));

    public static void SetSourceUrl(DependencyObject o, string? value) => o.SetValue(SourceUrlProperty, value);
    public static string? GetSourceUrl(DependencyObject o) => (string?)o.GetValue(SourceUrlProperty);

    private static void OnSourceUrlChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not Image img) return;

        var url = e.NewValue as string;
        img.Source = null;                       // alten Inhalt löschen (wichtig bei Container-Recycling)
        if (string.IsNullOrWhiteSpace(url)) return;

        if (_cache.TryGetValue(url, out var cached))
        {
            img.Source = cached;
            return;
        }

        _ = LoadAsync(img, url);   // bewusst abgekoppelt; LoadAsync fängt alle Fehler selbst
    }

    private static async Task LoadAsync(Image img, string url)
    {
        try
        {
            byte[] bytes;
            await _gate.WaitAsync().ConfigureAwait(true);
            try
            {
                // Zeigt das Element inzwischen eine andere URL? Dann Download sparen.
                if (GetSourceUrl(img) != url) return;
                bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(true);
            }
            finally { _gate.Release(); }

            var bmp = await Task.Run(() =>
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.StreamSource = new MemoryStream(bytes);
                b.DecodePixelWidth = 200;        // kleine Dekodierung -> wenig Speicher
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.EndInit();
                b.Freeze();                      // über Threadgrenzen hinweg nutzbar
                return b;
            }).ConfigureAwait(true);

            if (_cache.Count >= CacheLimit) _cache.Clear();   // simpler Deckel gegen unbegrenztes Wachstum
            _cache[url] = bmp;
            // Prüfen, ob dieses Image inzwischen eine andere URL zeigt (Recycling/schnelles Scrollen).
            if (GetSourceUrl(img) == url) img.Source = bmp;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Posterbild via HttpClient fehlgeschlagen: {Url}", url);
            // Fallback: WPF-eigener Lader (hilft z. B. hinter System-Proxys).
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(url);
                b.DecodePixelWidth = 200;
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.EndInit();
                if (GetSourceUrl(img) == url) img.Source = b;
            }
            catch (Exception ex2)
            {
                Serilog.Log.Debug(ex2, "Posterbild auch via UriSource fehlgeschlagen: {Url}", url);
            }
        }
    }
}
