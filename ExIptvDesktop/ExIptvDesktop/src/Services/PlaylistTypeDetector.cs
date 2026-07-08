using System;
using System.Text;
using System.Text.RegularExpressions;
using ExIptvDesktop.Models;

namespace ExIptvDesktop.Services;

public sealed record DetectionResult(
    PlaylistSourceType SourceType,
    Encoding Encoding,
    bool LooksValid,
    string? Warning);

/// <summary>
/// Erkennt automatisch Playlist-Typ, Zeichensatz und Encoding.
/// Arbeitet rein auf Text-/URL-Heuristiken, ohne den kompletten Inhalt
/// vorab laden zu muessen (Grossdatei-tauglich: liest max. die ersten
/// SampleBytes Bytes fuer die Erkennung).
/// </summary>
public static class PlaylistTypeDetector
{
    private const int SampleBytes = 8192;

    public static DetectionResult DetectFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new DetectionResult(PlaylistSourceType.Unknown, Encoding.UTF8, false, "Leere URL.");

        // Xtream Codes erkennt man typischerweise an player_api.php oder get.php mit
        // username/password-Query-Parametern.
        if (Regex.IsMatch(url, @"/(player_api|get)\.php\?.*username=.*password=", RegexOptions.IgnoreCase))
            return new DetectionResult(PlaylistSourceType.XtreamCodes, Encoding.UTF8, true, null);

        if (Regex.IsMatch(url, @"/(stalker_portal|portal\.php|c/)", RegexOptions.IgnoreCase))
            return new DetectionResult(PlaylistSourceType.StalkerPortal, Encoding.UTF8, true,
                "Stalker-Portal-Erkennung ist experimentell -- bitte Ergebnis pruefen.");

        if (url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            return new DetectionResult(PlaylistSourceType.M3U8, Encoding.UTF8, true, null);

        if (url.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
            return new DetectionResult(PlaylistSourceType.M3U, Encoding.UTF8, true, null);

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeFile)
            return new DetectionResult(PlaylistSourceType.LocalFile, Encoding.UTF8, true, null);

        // Kein eindeutiges Muster -> Inhaltsbasierte Erkennung noetig (siehe DetectFromContent).
        return new DetectionResult(PlaylistSourceType.Unknown, Encoding.UTF8, false,
            "Typ konnte nicht aus der URL abgeleitet werden, Inhalt wird analysiert.");
    }

    /// <summary>
    /// Inhaltsbasierte Erkennung inkl. Zeichensatz-Heuristik (BOM-Erkennung,
    /// Fallback auf UTF-8, dann Windows-1252 fuer aeltere europaeische Provider-Exports).
    /// </summary>
    public static DetectionResult DetectFromContent(byte[] sampleBytes)
    {
        if (sampleBytes.Length == 0)
            return new DetectionResult(PlaylistSourceType.Unknown, Encoding.UTF8, false, "Leerer Inhalt.");

        var encoding = DetectEncoding(sampleBytes);
        var text = encoding.GetString(sampleBytes, 0, Math.Min(sampleBytes.Length, SampleBytes));

        if (text.TrimStart().StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
        {
            var isHls = text.Contains("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("#EXT-X-TARGETDURATION", StringComparison.OrdinalIgnoreCase);
            return new DetectionResult(
                isHls ? PlaylistSourceType.M3U8 : PlaylistSourceType.M3U,
                encoding, true, null);
        }

        if (text.TrimStart().StartsWith("{") && text.Contains("\"user_info\""))
            return new DetectionResult(PlaylistSourceType.XtreamCodes, encoding, true,
                "JSON-Antwort erkannt (Xtream player_api.php Account-Info).");

        return new DetectionResult(PlaylistSourceType.Unknown, encoding, false,
            "Unbekanntes Format -- manuelle Pruefung empfohlen.");
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // Kein BOM: pruefen ob gueltiges UTF-8, sonst auf Windows-1252 zurueckfallen
        // (haeufig bei aelteren IPTV-Provider-Exports aus Westeuropa).
        try
        {
            var strict = new UTF8Encoding(false, true);
            strict.GetString(bytes);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding("Windows-1252");
        }
    }
}
