using System.Net;
using System.Text.Json;
using ExIptv.Models;
using Serilog;

namespace ExIptv.Services.Xtream;

/// <summary>
/// Kapselt die Kommunikation mit einem Xtream-Codes-Panel.
/// Verwendet einen über IHttpClientFactory bereitgestellten HttpClient mit Retry/Backoff (Polly).
/// Baut Stream-URLs deterministisch aus Host/User/Pass/StreamId auf.
/// </summary>
public sealed class XtreamClient
{
    private readonly IHttpClientFactory _httpFactory;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    public XtreamClient(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    private HttpClient Http => _httpFactory.CreateClient("iptv");

    private static string BaseApi(PlaylistSource s) =>
        $"{s.Host!.TrimEnd('/')}/player_api.php?username={Uri.EscapeDataString(s.Username ?? "")}&password={Uri.EscapeDataString(s.Password ?? "")}";

    /// <summary>Prüft Zugangsdaten. Gibt (true, null) bei Erfolg, sonst (false, Fehlermeldung).</summary>
    public async Task<(bool ok, string? error)> TestConnectionAsync(PlaylistSource s, CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(BaseApi(s), ct);
            var auth = JsonSerializer.Deserialize<XtreamAuthResponseDto>(json, JsonOpts);
            if (auth?.UserInfo is null)
                return (false, "Unerwartete Antwort vom Server (kein user_info).");

            var authOk = auth.UserInfo.Auth.ValueKind switch
            {
                JsonValueKind.Number => auth.UserInfo.Auth.GetInt32() == 1,
                JsonValueKind.String => auth.UserInfo.Auth.GetString() == "1",
                _ => false
            };
            if (!authOk)
                return (false, auth.UserInfo.Message ?? "Authentifizierung fehlgeschlagen.");

            if (!string.Equals(auth.UserInfo.Status, "Active", StringComparison.OrdinalIgnoreCase)
                && auth.UserInfo.Status is not null)
                return (false, $"Konto-Status: {auth.UserInfo.Status}");

            return (true, null);
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "Xtream-Verbindungstest fehlgeschlagen (HTTP)");
            return (false, $"Netzwerkfehler: {ex.StatusCode?.ToString() ?? ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Xtream-Verbindungstest fehlgeschlagen");
            return (false, ex.Message);
        }
    }

    // ---------- Kategorien ----------

    public Task<List<Category>> GetLiveCategoriesAsync(PlaylistSource s, CancellationToken ct = default) =>
        GetCategoriesAsync(s, "get_live_categories", ContentType.Live, ct);

    public Task<List<Category>> GetVodCategoriesAsync(PlaylistSource s, CancellationToken ct = default) =>
        GetCategoriesAsync(s, "get_vod_categories", ContentType.Movie, ct);

    public Task<List<Category>> GetSeriesCategoriesAsync(PlaylistSource s, CancellationToken ct = default) =>
        GetCategoriesAsync(s, "get_series_categories", ContentType.Series, ct);

    private async Task<List<Category>> GetCategoriesAsync(PlaylistSource s, string action, ContentType type, CancellationToken ct)
    {
        var url = $"{BaseApi(s)}&action={action}";
        var dtos = await GetJsonAsync<List<XtreamCategoryDto>>(url, ct) ?? new();
        return dtos
            .Where(d => !string.IsNullOrWhiteSpace(d.CategoryId))
            .Select(d => new Category
            {
                SourceId = s.Id,
                ContentType = type,
                ExternalId = d.CategoryId!,
                Name = string.IsNullOrWhiteSpace(d.CategoryName) ? "Ohne Kategorie" : d.CategoryName!.Trim()
            })
            .ToList();
    }

    // ---------- Live ----------

    public async Task<List<LiveChannel>> GetLiveStreamsAsync(PlaylistSource s, CancellationToken ct = default)
    {
        var url = $"{BaseApi(s)}&action=get_live_streams";
        var dtos = await GetJsonAsync<List<XtreamLiveStreamDto>>(url, ct) ?? new();
        var result = new List<LiveChannel>(dtos.Count);
        foreach (var d in dtos)
        {
            var streamId = AsString(d.StreamId);
            if (string.IsNullOrEmpty(streamId)) continue;
            result.Add(new LiveChannel
            {
                SourceId = s.Id,
                ExternalId = streamId,
                Name = d.Name?.Trim() ?? "Unbenannt",
                LogoUrl = NullIfEmpty(d.StreamIcon),
                EpgChannelId = NullIfEmpty(d.EpgChannelId),
                CategoryExternalId = AsString(d.CategoryId),
                StreamUrl = $"{s.Host!.TrimEnd('/')}/live/{s.Username}/{s.Password}/{streamId}.ts"
            });
        }
        return result;
    }

    // ---------- VOD ----------

    public async Task<List<VodStream>> GetVodStreamsAsync(PlaylistSource s, CancellationToken ct = default)
    {
        var url = $"{BaseApi(s)}&action=get_vod_streams";
        var dtos = await GetJsonAsync<List<XtreamVodStreamDto>>(url, ct) ?? new();
        var result = new List<VodStream>(dtos.Count);
        foreach (var d in dtos)
        {
            var streamId = AsString(d.StreamId);
            if (string.IsNullOrEmpty(streamId)) continue;
            var ext = string.IsNullOrWhiteSpace(d.ContainerExtension) ? "mp4" : d.ContainerExtension!.Trim();
            result.Add(new VodStream
            {
                SourceId = s.Id,
                ExternalId = streamId,
                Name = d.Name?.Trim() ?? "Unbenannt",
                PosterUrl = NullIfEmpty(d.StreamIcon),
                ContainerExtension = ext,
                CategoryExternalId = AsString(d.CategoryId),
                Rating = ParseDouble(d.Rating),
                Year = NullIfEmpty(d.Year),
                StreamUrl = $"{s.Host!.TrimEnd('/')}/movie/{s.Username}/{s.Password}/{streamId}.{ext}"
            });
        }
        return result;
    }

    // ---------- Serien ----------

    public async Task<List<Series>> GetSeriesAsync(PlaylistSource s, CancellationToken ct = default)
    {
        var url = $"{BaseApi(s)}&action=get_series";
        var dtos = await GetJsonAsync<List<XtreamSeriesDto>>(url, ct) ?? new();
        var result = new List<Series>(dtos.Count);
        foreach (var d in dtos)
        {
            var seriesId = AsString(d.SeriesId);
            if (string.IsNullOrEmpty(seriesId)) continue;
            result.Add(new Series
            {
                SourceId = s.Id,
                ExternalId = seriesId,
                Name = d.Name?.Trim() ?? "Unbenannt",
                PosterUrl = NullIfEmpty(d.Cover),
                Plot = NullIfEmpty(d.Plot),
                CategoryExternalId = AsString(d.CategoryId),
                Rating = ParseDouble(d.Rating)
            });
        }
        return result;
    }

    /// <summary>Lädt die Episoden einer Serie (lazy). Baut Episode-Stream-URLs auf.</summary>
    public async Task<List<Episode>> GetSeriesEpisodesAsync(PlaylistSource s, string seriesId, CancellationToken ct = default)
    {
        var url = $"{BaseApi(s)}&action=get_series_info&series_id={Uri.EscapeDataString(seriesId)}";
        var info = await GetJsonAsync<XtreamSeriesInfoDto>(url, ct);
        var episodes = new List<Episode>();
        if (info?.Episodes is null) return episodes;

        foreach (var (seasonKey, list) in info.Episodes)
        {
            _ = int.TryParse(seasonKey, out var seasonFromKey);
            foreach (var e in list)
            {
                if (string.IsNullOrEmpty(e.Id)) continue;
                var ext = string.IsNullOrWhiteSpace(e.ContainerExtension) ? "mp4" : e.ContainerExtension!.Trim();
                var season = e.Season.ValueKind == JsonValueKind.Undefined ? seasonFromKey : ParseInt(AsString(e.Season), seasonFromKey);
                episodes.Add(new Episode
                {
                    ExternalId = e.Id,
                    Title = string.IsNullOrWhiteSpace(e.Title) ? $"Episode {AsString(e.EpisodeNum)}" : e.Title!.Trim(),
                    Season = season,
                    EpisodeNumber = ParseInt(AsString(e.EpisodeNum), 0),
                    ContainerExtension = ext,
                    Plot = NullIfEmpty(e.Info?.Plot),
                    StreamUrl = $"{s.Host!.TrimEnd('/')}/series/{s.Username}/{s.Password}/{e.Id}.{ext}"
                });
            }
        }
        return episodes
            .OrderBy(e => e.Season)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();
    }

    // ---------- Helpers ----------

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        // Xtream liefert bei leeren Ergebnissen manchmal "[]" oder einen leeren Body.
        if (resp.Content.Headers.ContentLength == 0) return default;
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "JSON-Deserialisierung fehlgeschlagen für {Url}", Redact(url));
            return default;
        }
    }

    private static string AsString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l.ToString() : e.GetRawText(),
        _ => ""
    };

    private static string? NullIfEmpty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static double? ParseDouble(string? v) =>
        double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int ParseInt(string? v, int fallback) =>
        int.TryParse(v, out var i) ? i : fallback;

    /// <summary>Entfernt Zugangsdaten aus URLs vor dem Logging.</summary>
    private static string Redact(string url)
    {
        var idx = url.IndexOf("username=", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? url[..idx] + "username=***&password=***" : url;
    }
}
