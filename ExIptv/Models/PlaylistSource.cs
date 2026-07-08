namespace ExIptv.Models;

/// <summary>
/// Eine konfigurierte Datenquelle (Xtream-Zugang, M3U-URL oder lokale Datei).
/// Zugangsdaten werden lokal in SQLite gehalten – bewusst kein Klartext-Logging.
/// </summary>
public sealed class PlaylistSource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public SourceType Type { get; set; } = SourceType.Unknown;

    // Xtream
    public string? Host { get; set; }        // z. B. http://server.tld:8080
    public string? Username { get; set; }
    public string? Password { get; set; }

    // M3U / lokale Datei
    public string? Url { get; set; }         // Remote-M3U oder lokaler Pfad

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncUtc { get; set; }

    /// <summary>Anzeigebarer, sicherer Bezeichner ohne Passwort.</summary>
    public override string ToString() => $"{Name} ({Type})";
}
