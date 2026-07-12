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
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/*,*/*;q=0.8");
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
                bytes = await FetchAsync(url).ConfigureAwait(true);
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

    /// <summary>
    /// Lädt Bild-Bytes mit Self-Referer (Origin der Bild-URL). Viele IPTV-Panels haben
    /// Hotlink-Schutz und liefern ohne passenden Referer 403 – der eigene Origin ist der
    /// sichere, übliche Weg (keine Abschaltung von Sicherheitsmechanismen).
    /// </summary>
    private static async Task<byte[]> FetchAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            req.Headers.Referrer = new Uri(u.GetLeftPart(UriPartial.Authority) + "/");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead).ConfigureAwait(true);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Poster-Diagnose (F12): führt für die übergebenen URLs echte Anfragen mit denselben
    /// Headern wie der Bild-Lader aus und liefert pro URL Status, Content-Type, Größe und
    /// Fehlertext. Zugangsdaten in Query-Parametern werden maskiert.
    /// </summary>
    public static async Task<string> DiagnoseAsync(IReadOnlyList<string> urls)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Poster-Diagnose (" + DateTime.Now.ToString("HH:mm:ss") + ")");
        foreach (var url in urls)
        {
            sb.AppendLine();
            sb.AppendLine("URL: " + MaskUrl(url));
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (Uri.TryCreate(url, UriKind.Absolute, out var u))
                    req.Headers.Referrer = new Uri(u.GetLeftPart(UriPartial.Authority) + "/");
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead).ConfigureAwait(true);
                var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(true);
                sb.AppendLine($"  Status: {(int)resp.StatusCode} {resp.StatusCode}");
                sb.AppendLine($"  Content-Type: {resp.Content.Headers.ContentType?.ToString() ?? "(keiner)"}");
                sb.AppendLine($"  Größe: {bytes.Length:N0} Bytes");
                if (resp.RequestMessage?.RequestUri is { } final && final.ToString() != url)
                    sb.AppendLine("  Umgeleitet nach: " + MaskUrl(final.ToString()));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  FEHLER: " + ex.GetBaseException().Message);
            }
        }
        Serilog.Log.Information("Poster-Diagnose:\n{Report}", sb.ToString());
        return sb.ToString();
    }

    /// <summary>Maskiert username/password/token/auth-Werte in Query-Strings.</summary>
    private static string MaskUrl(string url)
    {
        try
        {
            var i = url.IndexOf('?');
            if (i < 0) return url;
            var query = url[(i + 1)..].Split('&').Select(p =>
            {
                var kv = p.Split('=', 2);
                var key = kv[0].ToLowerInvariant();
                return kv.Length == 2 && (key.Contains("user") || key.Contains("pass") || key.Contains("token") || key.Contains("auth"))
                    ? kv[0] + "=***"
                    : p;
            });
            return url[..i] + "?" + string.Join("&", query);
        }
        catch { return url; }
    }
}
