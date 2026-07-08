using Dapper;
using ExIptv.Models;
using Microsoft.Data.Sqlite;

namespace ExIptv.Services.Data;

/// <summary>
/// Datenzugriff über Dapper. Bulk-Inserts laufen in einer einzigen Transaktion mit
/// wiederverwendeten, parametrisierten Commands – dadurch auch bei 100k+ Zeilen schnell.
/// </summary>
public sealed class IptvRepository
{
    private readonly IptvDatabase _db;

    public IptvRepository(IptvDatabase db) => _db = db;

    // ---------- Quellen ----------

    public IReadOnlyList<PlaylistSource> GetSources()
    {
        using var conn = _db.OpenConnection();
        return conn.Query<PlaylistSource>(
            "SELECT Id, Name, Type, Host, Username, Password, Url, CreatedUtc, LastSyncUtc FROM sources ORDER BY Name")
            .ToList();
    }

    public int InsertSource(PlaylistSource s)
    {
        using var conn = _db.OpenConnection();
        var id = conn.ExecuteScalar<long>("""
            INSERT INTO sources (Name, Type, Host, Username, Password, Url, CreatedUtc, LastSyncUtc)
            VALUES (@Name, @Type, @Host, @Username, @Password, @Url, @CreatedUtc, @LastSyncUtc);
            SELECT last_insert_rowid();
        """, s);
        return (int)id;
    }

    public void UpdateLastSync(int sourceId)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("UPDATE sources SET LastSyncUtc=@Now WHERE Id=@Id",
            new { Now = DateTime.UtcNow.ToString("o"), Id = sourceId });
    }

    public void DeleteSource(int sourceId)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("DELETE FROM sources WHERE Id=@Id", new { Id = sourceId });
    }

    // ---------- Import (ersetzt den Bestand einer Quelle atomar) ----------

    public void ReplaceLiveData(int sourceId, IReadOnlyList<Category> categories, IReadOnlyList<LiveChannel> channels)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();

        conn.Execute("DELETE FROM categories WHERE SourceId=@Id AND ContentType=@T",
            new { Id = sourceId, T = (int)ContentType.Live }, tx);
        conn.Execute("DELETE FROM live_channels WHERE SourceId=@Id", new { Id = sourceId }, tx);

        BulkInsertCategories(conn, tx, categories);

        var counts = channels.GroupBy(c => c.CategoryExternalId)
                             .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        UpdateCategoryCounts(conn, tx, sourceId, ContentType.Live, counts);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO live_channels (SourceId, ExternalId, Name, LogoUrl, EpgChannelId, CategoryExternalId, StreamUrl)
                VALUES ($src, $ext, $name, $logo, $epg, $cat, $url);
            """;
            var p = AddParams(cmd, "$src", "$ext", "$name", "$logo", "$epg", "$cat", "$url");
            foreach (var c in channels)
            {
                p[0].Value = c.SourceId;
                p[1].Value = c.ExternalId;
                p[2].Value = c.Name;
                p[3].Value = (object?)c.LogoUrl ?? DBNull.Value;
                p[4].Value = (object?)c.EpgChannelId ?? DBNull.Value;
                p[5].Value = c.CategoryExternalId;
                p[6].Value = c.StreamUrl;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    public void ReplaceVodData(int sourceId, IReadOnlyList<Category> categories, IReadOnlyList<VodStream> movies)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();

        conn.Execute("DELETE FROM categories WHERE SourceId=@Id AND ContentType=@T",
            new { Id = sourceId, T = (int)ContentType.Movie }, tx);
        conn.Execute("DELETE FROM vod_streams WHERE SourceId=@Id", new { Id = sourceId }, tx);

        BulkInsertCategories(conn, tx, categories);
        var counts = movies.GroupBy(m => m.CategoryExternalId)
                           .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        UpdateCategoryCounts(conn, tx, sourceId, ContentType.Movie, counts);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO vod_streams (SourceId, ExternalId, Name, PosterUrl, ContainerExtension, CategoryExternalId, Rating, Year, StreamUrl)
                VALUES ($src, $ext, $name, $poster, $container, $cat, $rating, $year, $url);
            """;
            var p = AddParams(cmd, "$src", "$ext", "$name", "$poster", "$container", "$cat", "$rating", "$year", "$url");
            foreach (var m in movies)
            {
                p[0].Value = m.SourceId;
                p[1].Value = m.ExternalId;
                p[2].Value = m.Name;
                p[3].Value = (object?)m.PosterUrl ?? DBNull.Value;
                p[4].Value = (object?)m.ContainerExtension ?? DBNull.Value;
                p[5].Value = m.CategoryExternalId;
                p[6].Value = (object?)m.Rating ?? DBNull.Value;
                p[7].Value = (object?)m.Year ?? DBNull.Value;
                p[8].Value = m.StreamUrl;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    public void ReplaceSeriesData(int sourceId, IReadOnlyList<Category> categories, IReadOnlyList<Series> series)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();

        conn.Execute("DELETE FROM categories WHERE SourceId=@Id AND ContentType=@T",
            new { Id = sourceId, T = (int)ContentType.Series }, tx);
        conn.Execute("DELETE FROM series WHERE SourceId=@Id", new { Id = sourceId }, tx);

        BulkInsertCategories(conn, tx, categories);
        var counts = series.GroupBy(s => s.CategoryExternalId)
                          .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        UpdateCategoryCounts(conn, tx, sourceId, ContentType.Series, counts);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO series (SourceId, ExternalId, Name, PosterUrl, Plot, CategoryExternalId, Rating)
                VALUES ($src, $ext, $name, $poster, $plot, $cat, $rating);
            """;
            var p = AddParams(cmd, "$src", "$ext", "$name", "$poster", "$plot", "$cat", "$rating");
            foreach (var s in series)
            {
                p[0].Value = s.SourceId;
                p[1].Value = s.ExternalId;
                p[2].Value = s.Name;
                p[3].Value = (object?)s.PosterUrl ?? DBNull.Value;
                p[4].Value = (object?)s.Plot ?? DBNull.Value;
                p[5].Value = s.CategoryExternalId;
                p[6].Value = (object?)s.Rating ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    // ---------- Abfragen ----------

    public IReadOnlyList<Category> GetCategories(int sourceId, ContentType type)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<Category>("""
            SELECT Id, SourceId, ContentType, ExternalId, Name, ItemCount
            FROM categories WHERE SourceId=@S AND ContentType=@T
            ORDER BY Name COLLATE NOCASE
        """, new { S = sourceId, T = (int)type }).ToList();
    }

    public IReadOnlyList<LiveChannel> GetLiveChannels(int sourceId, string? categoryExternalId, string? search, int limit = 5000)
    {
        using var conn = _db.OpenConnection();
        var sql = """
            SELECT Id, SourceId, ExternalId, Name, LogoUrl, EpgChannelId, CategoryExternalId, StreamUrl
            FROM live_channels
            WHERE SourceId=@S
        """;
        if (!string.IsNullOrEmpty(categoryExternalId)) sql += " AND CategoryExternalId=@C";
        if (!string.IsNullOrWhiteSpace(search)) sql += " AND Name LIKE @Q";
        sql += " ORDER BY Id LIMIT @L";   // Id = Importreihenfolge = Reihenfolge des Anbieters
        return conn.Query<LiveChannel>(sql, new
        {
            S = sourceId,
            C = categoryExternalId,
            Q = $"%{search}%",
            L = limit
        }).ToList();
    }

    public IReadOnlyList<VodStream> GetMovies(int sourceId, string? categoryExternalId, string? search, string sort = "name", int limit = 5000)
    {
        using var conn = _db.OpenConnection();
        var sql = """
            SELECT Id, SourceId, ExternalId, Name, PosterUrl, ContainerExtension, CategoryExternalId, Rating, Year, StreamUrl
            FROM vod_streams WHERE SourceId=@S
        """;
        if (!string.IsNullOrEmpty(categoryExternalId)) sql += " AND CategoryExternalId=@C";
        if (!string.IsNullOrWhiteSpace(search)) sql += " AND Name LIKE @Q";
        sql += " ORDER BY " + OrderClause(sort) + " LIMIT @L";
        return conn.Query<VodStream>(sql, new { S = sourceId, C = categoryExternalId, Q = $"%{search}%", L = limit }).ToList();
    }

    public IReadOnlyList<Series> GetSeries(int sourceId, string? categoryExternalId, string? search, string sort = "name", int limit = 5000)
    {
        using var conn = _db.OpenConnection();
        var sql = """
            SELECT Id, SourceId, ExternalId, Name, PosterUrl, Plot, CategoryExternalId, Rating
            FROM series WHERE SourceId=@S
        """;
        if (!string.IsNullOrEmpty(categoryExternalId)) sql += " AND CategoryExternalId=@C";
        if (!string.IsNullOrWhiteSpace(search)) sql += " AND Name LIKE @Q";
        sql += " ORDER BY " + OrderClause(sort) + " LIMIT @L";
        return conn.Query<Series>(sql, new { S = sourceId, C = categoryExternalId, Q = $"%{search}%", L = limit }).ToList();
    }

    // Feste, injektionssichere ORDER-BY-Klauseln (der sort-Wert wird nie direkt eingesetzt).
    private static string OrderClause(string sort) => sort switch
    {
        "recent" => "Id DESC",                                  // Insert-Reihenfolge ≈ zuletzt hinzugefügt
        "rating" => "Rating IS NULL, Rating DESC, Name COLLATE NOCASE",
        _ => "Name COLLATE NOCASE"
    };

    // ---------- Favoriten ----------

    public void AddFavorite(int sourceId, ContentType type, string externalId, string name, string streamUrl)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("""
            INSERT OR IGNORE INTO favorites (SourceId, ContentType, ExternalId, Name, StreamUrl, AddedUtc)
            VALUES (@S, @T, @E, @N, @U, @D)
        """, new { S = sourceId, T = (int)type, E = externalId, N = name, U = streamUrl, D = DateTime.UtcNow.ToString("o") });
    }

    public void RemoveFavorite(int sourceId, ContentType type, string externalId)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("DELETE FROM favorites WHERE SourceId=@S AND ContentType=@T AND ExternalId=@E",
            new { S = sourceId, T = (int)type, E = externalId });
    }

    // ---------- interne Helfer ----------

    private static void BulkInsertCategories(SqliteConnection conn, SqliteTransaction tx, IReadOnlyList<Category> categories)
    {
        if (categories.Count == 0) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO categories (SourceId, ContentType, ExternalId, Name, ItemCount)
            VALUES ($src, $type, $ext, $name, $count);
        """;
        var p = AddParams(cmd, "$src", "$type", "$ext", "$name", "$count");
        foreach (var c in categories)
        {
            p[0].Value = c.SourceId;
            p[1].Value = (int)c.ContentType;
            p[2].Value = c.ExternalId;
            p[3].Value = c.Name;
            p[4].Value = c.ItemCount;
            cmd.ExecuteNonQuery();
        }
    }

    private static void UpdateCategoryCounts(SqliteConnection conn, SqliteTransaction tx, int sourceId,
        ContentType type, Dictionary<string, int> counts)
    {
        if (counts.Count == 0) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE categories SET ItemCount=$cnt WHERE SourceId=$src AND ContentType=$type AND ExternalId=$ext";
        var p = AddParams(cmd, "$cnt", "$src", "$type", "$ext");
        foreach (var (ext, cnt) in counts)
        {
            p[0].Value = cnt;
            p[1].Value = sourceId;
            p[2].Value = (int)type;
            p[3].Value = ext;
            cmd.ExecuteNonQuery();
        }
    }

    private static SqliteParameter[] AddParams(SqliteCommand cmd, params string[] names)
    {
        var arr = new SqliteParameter[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            arr[i] = cmd.CreateParameter();
            arr[i].ParameterName = names[i];
            cmd.Parameters.Add(arr[i]);
        }
        return arr;
    }
}
