using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExIptvDesktop.Models;
using SQLite;

namespace ExIptvDesktop.Services;

/// <summary>
/// Zugriff auf die lokale SQLite-Datenbank. Batch-Inserts innerhalb einer
/// Transaktion sind bei 100.000+ Eintraegen um Groessenordnungen schneller
/// als Einzel-Inserts (WAL-Modus + eine Transaktion statt N Commits).
/// </summary>
public sealed class DatabaseService : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private readonly FileLogger _logger;

    public DatabaseService(string dbPath, FileLogger logger)
    {
        _logger = logger;
        var options = new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: true);
        _db = new SQLiteAsyncConnection(options);
    }

    public async Task InitializeAsync()
    {
        // WAL-Modus: bessere Nebenlaeufigkeit zwischen UI-Reads und Import-Writes.
        await _db.ExecuteAsync("PRAGMA journal_mode=WAL;");
        await _db.ExecuteAsync("PRAGMA synchronous=NORMAL;");

        await _db.CreateTableAsync<XtreamProfile>();
        await _db.CreateTableAsync<Category>();
        await _db.CreateTableAsync<Channel>();
        await _db.CreateTableAsync<SeriesItem>();
        await _db.CreateTableAsync<Episode>();
        await _db.CreateTableAsync<MovieItem>();

        _logger.Info("Database", "Schema initialisiert.");
    }

    public Task<List<XtreamProfile>> GetProfilesAsync() => _db.Table<XtreamProfile>().ToListAsync();
    public Task<int> SaveProfileAsync(XtreamProfile profile) =>
        profile.Id == 0 ? _db.InsertAsync(profile) : _db.UpdateAsync(profile);

    public async Task<List<Category>> GetCategoriesAsync(int profileId, ContentType type) =>
        await _db.Table<Category>()
            .Where(c => c.ProfileId == profileId && c.Type == type)
            .OrderBy(c => c.Name)
            .ToListAsync();

    public async Task<List<Channel>> GetChannelsAsync(int categoryId, bool includeAdult) =>
        await _db.Table<Channel>()
            .Where(c => c.CategoryId == categoryId)
            .OrderBy(c => c.Name)
            .ToListAsync();

    public async Task<List<Channel>> SearchChannelsAsync(int profileId, string query)
    {
        var like = $"%{query}%";
        return await _db.QueryAsync<Channel>(
            "SELECT * FROM Channels WHERE ProfileId = ? AND Name LIKE ? ORDER BY Name LIMIT 500",
            profileId, like);
    }

    /// <summary>
    /// Ersetzt Kategorien/Kanaele eines Profils in einer einzigen Transaktion.
    /// Leere Kategorien werden nicht persistiert, doppelte Gruppennamen werden
    /// dedupliziert bevor sie in die DB geschrieben werden.
    /// </summary>
    public async Task ReplaceChannelsForProfileAsync(
        int profileId, List<Category> categories, List<Channel> channels)
    {
        var nonEmptyCategoryIds = channels.Select(c => c.ExternalCategoryId).ToHashSet();

        var dedupedCategories = categories
            .Where(c => nonEmptyCategoryIds.Contains(c.ExternalCategoryId))
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(c => c.Name)
            .ToList();

        await _db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM Channels WHERE ProfileId = ?", profileId);
            conn.Execute("DELETE FROM Categories WHERE ProfileId = ? AND Type = ?",
                profileId, (int)ContentType.LiveTv);

            conn.InsertAll(dedupedCategories);

            // Externe Kategorie-ID -> lokale DB-Id mappen (Foreign Key erst nach Insert bekannt).
            var idMap = dedupedCategories
                .Where(c => c.Id != 0)
                .ToDictionary(c => c.ExternalCategoryId, c => c.Id);

            foreach (var ch in channels)
            {
                if (idMap.TryGetValue(ch.CategoryId.ToString(), out var localId))
                    ch.CategoryId = localId;
            }

            conn.InsertAll(channels);
        });

        _logger.Info("Database",
            $"Import abgeschlossen: {dedupedCategories.Count} Kategorien, {channels.Count} Kanaele.");
    }

    /// <summary>
    /// Filme deduplizieren (gleicher Titel + Jahr = Duplikat) bevor sie gespeichert werden.
    /// </summary>
    public async Task ReplaceMoviesForProfileAsync(int profileId, List<Category> categories, List<MovieItem> movies)
    {
        var deduped = movies
            .GroupBy(m => m.DedupeKey)
            .Select(g => g.First())
            .ToList();

        await _db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM Movies WHERE ProfileId = ?", profileId);
            conn.Execute("DELETE FROM Categories WHERE ProfileId = ? AND Type = ?",
                profileId, (int)ContentType.Movie);
            conn.InsertAll(categories);
            conn.InsertAll(deduped);
        });

        _logger.Info("Database",
            $"Filme-Import: {movies.Count} gelesen, {deduped.Count} nach Deduplizierung gespeichert.");
    }

    public Task UpdateResumePositionAsync(int channelId, long positionMs) =>
        _db.ExecuteAsync(
            "UPDATE Channels SET ResumePositionMs = ?, LastWatchedAt = ? WHERE Id = ?",
            positionMs, DateTime.UtcNow, channelId);

    public Task ToggleFavoriteAsync(int channelId, bool isFavorite) =>
        _db.ExecuteAsync("UPDATE Channels SET IsFavorite = ? WHERE Id = ?", isFavorite, channelId);

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();
}
