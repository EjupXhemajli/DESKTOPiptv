using System.Net.Http;
using ExIptv.Models;
using ExIptv.Services.Data;
using ExIptv.Services.Xtream;
using Serilog;

namespace ExIptv.Services.Playlist;

/// <summary>Fortschrittsmeldung während eines Imports.</summary>
public sealed record ImportProgress(string Stage, int? Current = null, int? Total = null);

/// <summary>
/// Orchestriert den vollständigen Import einer Quelle in die lokale DB.
/// Xtream: Live + VOD + Serien getrennt (jeweils atomar ersetzt).
/// M3U/Datei: als Live-Sender importiert.
/// </summary>
public sealed class PlaylistImportService
{
    private readonly XtreamClient _xtream;
    private readonly M3uParser _m3u;
    private readonly IptvRepository _repo;
    private readonly IHttpClientFactory _httpFactory;

    public PlaylistImportService(XtreamClient xtream, M3uParser m3u, IptvRepository repo, IHttpClientFactory httpFactory)
    {
        _xtream = xtream;
        _m3u = m3u;
        _repo = repo;
        _httpFactory = httpFactory;
    }

    public async Task ImportAsync(PlaylistSource source, IProgress<ImportProgress>? progress, CancellationToken ct = default)
    {
        switch (source.Type)
        {
            case SourceType.Xtream:
                await ImportXtreamAsync(source, progress, ct);
                break;
            case SourceType.M3u:
            case SourceType.LocalFile:
                await ImportM3uAsync(source, progress, ct);
                break;
            default:
                throw new NotSupportedException($"Quelltyp {source.Type} wird nicht unterstützt.");
        }
        _repo.UpdateLastSync(source.Id);
        progress?.Report(new ImportProgress("Fertig"));
    }

    private async Task ImportXtreamAsync(PlaylistSource s, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        // --- Live ---
        progress?.Report(new ImportProgress("Lade Live-Kategorien…"));
        var liveCats = await _xtream.GetLiveCategoriesAsync(s, ct);
        progress?.Report(new ImportProgress("Lade Live-Sender…"));
        var liveChannels = await _xtream.GetLiveStreamsAsync(s, ct);
        EnsureCatalogCategory(liveCats, liveChannels.Select(c => c.CategoryExternalId), s.Id, ContentType.Live);
        _repo.ReplaceLiveData(s.Id, liveCats, liveChannels);
        Log.Information("Xtream Live importiert: {Cats} Kategorien, {Ch} Sender", liveCats.Count, liveChannels.Count);

        // --- VOD ---
        progress?.Report(new ImportProgress("Lade Film-Kategorien…"));
        var vodCats = await _xtream.GetVodCategoriesAsync(s, ct);
        progress?.Report(new ImportProgress("Lade Filme…"));
        var movies = await _xtream.GetVodStreamsAsync(s, ct);
        EnsureCatalogCategory(vodCats, movies.Select(m => m.CategoryExternalId), s.Id, ContentType.Movie);
        _repo.ReplaceVodData(s.Id, vodCats, movies);
        Log.Information("Xtream VOD importiert: {Cats} Kategorien, {M} Filme", vodCats.Count, movies.Count);

        // --- Serien ---
        progress?.Report(new ImportProgress("Lade Serien-Kategorien…"));
        var seriesCats = await _xtream.GetSeriesCategoriesAsync(s, ct);
        progress?.Report(new ImportProgress("Lade Serien…"));
        var series = await _xtream.GetSeriesAsync(s, ct);
        EnsureCatalogCategory(seriesCats, series.Select(x => x.CategoryExternalId), s.Id, ContentType.Series);
        _repo.ReplaceSeriesData(s.Id, seriesCats, series);
        Log.Information("Xtream Serien importiert: {Cats} Kategorien, {S} Serien", seriesCats.Count, series.Count);
    }

    private async Task ImportM3uAsync(PlaylistSource s, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new ImportProgress("Lade Playlist…"));
        string content;
        if (s.Type == SourceType.LocalFile)
        {
            if (string.IsNullOrWhiteSpace(s.Url) || !File.Exists(s.Url))
                throw new FileNotFoundException("Lokale Playlist nicht gefunden.", s.Url);
            content = await File.ReadAllTextAsync(s.Url, ct);
        }
        else
        {
            var http = _httpFactory.CreateClient("iptv");
            content = await http.GetStringAsync(s.Url, ct);
        }

        if (!M3uParser.LooksLikeM3u(content))
            throw new InvalidDataException("Der Inhalt sieht nicht nach einer gültigen M3U-Playlist aus.");

        progress?.Report(new ImportProgress("Verarbeite Sender…"));
        var result = _m3u.Parse(content, s.Id);
        _repo.ReplaceLiveData(s.Id, result.Categories, result.Channels);
        Log.Information("M3U importiert: {Cats} Gruppen, {Ch} Sender", result.Categories.Count, result.Channels.Count);
    }

    /// <summary>
    /// Sorgt dafür, dass Inhalte mit unbekannter/leerer Kategorie eine Sammelkategorie erhalten,
    /// damit in der UI nichts "verschwindet".
    /// </summary>
    private static void EnsureCatalogCategory(List<Category> cats, IEnumerable<string> usedCategoryIds, int sourceId, ContentType type)
    {
        var known = new HashSet<string>(cats.Select(c => c.ExternalId), StringComparer.OrdinalIgnoreCase);
        var needsFallback = usedCategoryIds.Any(id => string.IsNullOrWhiteSpace(id) || !known.Contains(id));
        if (needsFallback && !known.Contains(""))
        {
            cats.Add(new Category
            {
                SourceId = sourceId,
                ContentType = type,
                ExternalId = "",
                Name = "Ohne Kategorie"
            });
        }
    }
}
