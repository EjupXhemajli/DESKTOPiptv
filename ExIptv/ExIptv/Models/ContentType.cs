namespace ExIptv.Models;

/// <summary>Art des IPTV-Inhalts. Wird für Routing im Player und in der Datenbank genutzt.</summary>
public enum ContentType
{
    Live,
    Movie,
    Series
}

/// <summary>Herkunft einer Playlist-Quelle.</summary>
public enum SourceType
{
    Unknown,
    Xtream,
    M3u,
    LocalFile
}
