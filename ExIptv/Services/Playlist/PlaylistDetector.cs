using System.Text.RegularExpressions;
using ExIptv.Models;

namespace ExIptv.Services.Playlist;

/// <summary>
/// Erkennt anhand einer Benutzereingabe (URL oder Pfad) den wahrscheinlichen Quelltyp
/// und extrahiert – wo möglich – Host/User/Pass aus typischen Xtream-URLs.
/// </summary>
public static partial class PlaylistDetector
{
    // http(s)://host:port/get.php?username=U&password=P&type=m3u_plus...
    [GeneratedRegex("""^(?<host>https?://[^/]+)/get\.php\?.*username=(?<user>[^&]+)&password=(?<pass>[^&]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex XtreamGetPhpRegex();

    // http(s)://host:port/player_api.php?username=U&password=P
    [GeneratedRegex("""^(?<host>https?://[^/]+)/player_api\.php\?.*username=(?<user>[^&]+)&password=(?<pass>[^&]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex XtreamPlayerApiRegex();

    public sealed record DetectionResult(SourceType Type, PlaylistSource? Source, string Message);

    public static DetectionResult Detect(string input, string? name = null)
    {
        input = input.Trim();
        if (input.Length == 0)
            return new DetectionResult(SourceType.Unknown, null, "Leere Eingabe.");

        // 1) Lokale Datei?
        if (File.Exists(input) || (!input.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(input)))
        {
            return new DetectionResult(SourceType.LocalFile, new PlaylistSource
            {
                Name = name ?? Path.GetFileNameWithoutExtension(input),
                Type = SourceType.LocalFile,
                Url = input
            }, "Lokale Datei erkannt.");
        }

        // 2) Xtream über get.php oder player_api.php?
        var m = XtreamGetPhpRegex().Match(input);
        if (!m.Success) m = XtreamPlayerApiRegex().Match(input);
        if (m.Success)
        {
            var src = new PlaylistSource
            {
                Name = name ?? new Uri(m.Groups["host"].Value).Host,
                Type = SourceType.Xtream,
                Host = m.Groups["host"].Value,
                Username = Uri.UnescapeDataString(m.Groups["user"].Value),
                Password = Uri.UnescapeDataString(m.Groups["pass"].Value)
            };
            return new DetectionResult(SourceType.Xtream, src, "Xtream-Zugang aus URL erkannt.");
        }

        // 3) Sonstige HTTP-URL -> als M3U behandeln
        if (input.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return new DetectionResult(SourceType.M3u, new PlaylistSource
            {
                Name = name ?? TryHost(input),
                Type = SourceType.M3u,
                Url = input
            }, "Remote-M3U-URL erkannt.");
        }

        return new DetectionResult(SourceType.Unknown, null, "Format nicht erkannt.");
    }

    private static string TryHost(string url)
    {
        try { return new Uri(url).Host; } catch { return "M3U-Playlist"; }
    }
}
