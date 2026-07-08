using Microsoft.Data.Sqlite;
using Serilog;

namespace ExIptv.Services.Data;

/// <summary>
/// Verwaltet die SQLite-Datei, das Schema und die WAL-/PRAGMA-Konfiguration.
/// Die DB liegt in %AppData%\EX-IPTV\iptv.db.
/// </summary>
public sealed class IptvDatabase
{
    public string DbPath { get; }
    public string ConnectionString { get; }

    public IptvDatabase()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EX-IPTV");
        Directory.CreateDirectory(dir);
        DbPath = Path.Combine(dir, "iptv.db");
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    /// <summary>Öffnet eine neue Verbindung mit performanten PRAGMAs.</summary>
    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // WAL + normale Sync-Stufe: guter Kompromiss aus Sicherheit und Import-Speed.
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA temp_store=MEMORY;
            PRAGMA cache_size=-16000;
            PRAGMA foreign_keys=ON;
        """;
        cmd.ExecuteNonQuery();
        return conn;
    }

    /// <summary>Erstellt das Schema, falls noch nicht vorhanden.</summary>
    public void Initialize()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();
        Log.Information("Datenbank initialisiert: {Path}", DbPath);
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS sources (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            Name        TEXT NOT NULL,
            Type        INTEGER NOT NULL,
            Host        TEXT,
            Username    TEXT,
            Password    TEXT,
            Url         TEXT,
            CreatedUtc  TEXT NOT NULL,
            LastSyncUtc TEXT
        );

        CREATE TABLE IF NOT EXISTS categories (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            SourceId    INTEGER NOT NULL,
            ContentType INTEGER NOT NULL,
            ExternalId  TEXT NOT NULL,
            Name        TEXT NOT NULL,
            ItemCount   INTEGER NOT NULL DEFAULT 0,
            FOREIGN KEY(SourceId) REFERENCES sources(Id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_categories_src_type ON categories(SourceId, ContentType);

        CREATE TABLE IF NOT EXISTS live_channels (
            Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            SourceId           INTEGER NOT NULL,
            ExternalId         TEXT NOT NULL,
            Name               TEXT NOT NULL,
            LogoUrl            TEXT,
            EpgChannelId       TEXT,
            CategoryExternalId TEXT NOT NULL,
            StreamUrl          TEXT NOT NULL,
            FOREIGN KEY(SourceId) REFERENCES sources(Id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_live_src_cat ON live_channels(SourceId, CategoryExternalId);
        CREATE INDEX IF NOT EXISTS ix_live_name    ON live_channels(SourceId, Name);

        CREATE TABLE IF NOT EXISTS vod_streams (
            Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            SourceId           INTEGER NOT NULL,
            ExternalId         TEXT NOT NULL,
            Name               TEXT NOT NULL,
            PosterUrl          TEXT,
            ContainerExtension TEXT,
            CategoryExternalId TEXT NOT NULL,
            Rating             REAL,
            Year               TEXT,
            StreamUrl          TEXT NOT NULL,
            FOREIGN KEY(SourceId) REFERENCES sources(Id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_vod_src_cat ON vod_streams(SourceId, CategoryExternalId);
        CREATE INDEX IF NOT EXISTS ix_vod_name    ON vod_streams(SourceId, Name);

        CREATE TABLE IF NOT EXISTS series (
            Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            SourceId           INTEGER NOT NULL,
            ExternalId         TEXT NOT NULL,
            Name               TEXT NOT NULL,
            PosterUrl          TEXT,
            Plot               TEXT,
            CategoryExternalId TEXT NOT NULL,
            Rating             REAL,
            FOREIGN KEY(SourceId) REFERENCES sources(Id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_series_src_cat ON series(SourceId, CategoryExternalId);
        CREATE INDEX IF NOT EXISTS ix_series_name    ON series(SourceId, Name);

        CREATE TABLE IF NOT EXISTS favorites (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            SourceId    INTEGER NOT NULL,
            ContentType INTEGER NOT NULL,
            ExternalId  TEXT NOT NULL,
            Name        TEXT NOT NULL,
            StreamUrl   TEXT NOT NULL,
            AddedUtc    TEXT NOT NULL,
            UNIQUE(SourceId, ContentType, ExternalId)
        );
    """;
}
