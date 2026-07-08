namespace ExIptv.Models;

/// <summary>Ein Live-TV-Sender.</summary>
public sealed class LiveChannel
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public string ExternalId { get; set; } = "";     // stream_id (Xtream) bzw. laufende Nr. (M3U)
    public string Name { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? EpgChannelId { get; set; }        // tvg-id
    public string CategoryExternalId { get; set; } = "";

    /// <summary>Fertig aufgelöste, abspielbare Stream-URL.</summary>
    public string StreamUrl { get; set; } = "";

    public override string ToString() => Name;
}
