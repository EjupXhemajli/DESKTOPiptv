using System.Text;
using System.Text.RegularExpressions;
using ExIptv.Models;

namespace ExIptv.Services.Playlist;

/// <summary>
/// Robuster M3U/M3U8-Parser (Extended M3U). Liest tvg-id, tvg-name, tvg-logo, group-title.
/// Streamt zeilenweise, um auch sehr große Playlisten (100k+ Einträge) speicherschonend zu verarbeiten.
/// </summary>
public sealed partial class M3uParser
{
    [GeneratedRegex("(?<key>[a-zA-Z0-9\\-]+)=\"(?<val>[^\"]*)\"", RegexOptions.Compiled)]
    private static partial Regex AttrRegex();

    public sealed record M3uResult(List<Category> Categories, List<LiveChannel> Channels);

    /// <summary>Parst einen M3U-Text (bereits als String geladen).</summary>
    public M3uResult Parse(string content, int sourceId)
    {
        using var reader = new StringReader(content);
        return Parse(reader, sourceId);
    }

    /// <summary>Parst aus einem TextReader (Datei-/Netzwerkstream).</summary>
    public M3uResult Parse(TextReader reader, int sourceId)
    {
        var channels = new List<LiveChannel>();
        var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        string? line;
        string pendingName = "";
        string pendingLogo = "";
        string pendingTvgId = "";
        string pendingGroup = "";
        bool haveExtinf = false;
        var seq = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                (pendingName, pendingLogo, pendingTvgId, pendingGroup) = ParseExtinf(line);
                haveExtinf = true;
            }
            else if (line.StartsWith("#EXTGRP", StringComparison.OrdinalIgnoreCase))
            {
                var g = line[(line.IndexOf(':') + 1)..].Trim();
                if (g.Length > 0) pendingGroup = g;
            }
            else if (line.StartsWith('#'))
            {
                // andere Direktiven (#EXTVLCOPT etc.) überspringen
                continue;
            }
            else
            {
                // URL-Zeile
                if (!haveExtinf) continue; // URL ohne EXTINF -> ignorieren
                var group = string.IsNullOrWhiteSpace(pendingGroup) ? "Ohne Kategorie" : pendingGroup;
                if (!groups.ContainsKey(group)) groups[group] = 0;
                groups[group]++;

                channels.Add(new LiveChannel
                {
                    SourceId = sourceId,
                    ExternalId = (++seq).ToString(),
                    Name = string.IsNullOrWhiteSpace(pendingName) ? $"Sender {seq}" : pendingName,
                    LogoUrl = string.IsNullOrWhiteSpace(pendingLogo) ? null : pendingLogo,
                    EpgChannelId = string.IsNullOrWhiteSpace(pendingTvgId) ? null : pendingTvgId,
                    CategoryExternalId = group,
                    StreamUrl = line
                });

                haveExtinf = false;
                pendingName = pendingLogo = pendingTvgId = pendingGroup = "";
            }
        }

        var categories = groups
            .Select(kv => new Category
            {
                SourceId = sourceId,
                ContentType = ContentType.Live,
                ExternalId = kv.Key,
                Name = kv.Key,
                ItemCount = kv.Value
            })
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new M3uResult(categories, channels);
    }

    private (string name, string logo, string tvgId, string group) ParseExtinf(string line)
    {
        // Format: #EXTINF:-1 tvg-id="x" tvg-logo="y" group-title="z",Anzeigename
        var commaIdx = line.LastIndexOf(',');
        var name = commaIdx >= 0 ? line[(commaIdx + 1)..].Trim() : "";

        string logo = "", tvgId = "", tvgName = "", group = "";
        foreach (Match m in AttrRegex().Matches(line))
        {
            var key = m.Groups["key"].Value.ToLowerInvariant();
            var val = m.Groups["val"].Value.Trim();
            switch (key)
            {
                case "tvg-logo": logo = val; break;
                case "tvg-id": tvgId = val; break;
                case "tvg-name": tvgName = val; break;
                case "group-title": group = val; break;
            }
        }

        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(tvgName))
            name = tvgName;

        return (name, logo, tvgId, group);
    }

    /// <summary>Heuristik: Sieht der Anfang eines Texts nach einer M3U-Playlist aus?</summary>
    public static bool LooksLikeM3u(string content)
    {
        var head = content.AsSpan(0, Math.Min(content.Length, 512)).ToString().TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        return head.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase);
    }
}
