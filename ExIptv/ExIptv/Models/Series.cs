namespace ExIptv.Models;

/// <summary>Eine Serie (Container für Staffeln/Episoden – Episoden werden lazy nachgeladen).</summary>
public sealed class Series
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public string ExternalId { get; set; } = "";     // series_id
    public string Name { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string? Plot { get; set; }
    public string CategoryExternalId { get; set; } = "";
    public double? Rating { get; set; }

    public override string ToString() => Name;
}

/// <summary>Eine einzelne Episode einer Serie. Wird erst bei Bedarf über die Xtream-API geladen.</summary>
public sealed class Episode
{
    public string ExternalId { get; set; } = "";     // episode id
    public string Title { get; set; } = "";
    public int Season { get; set; }
    public int EpisodeNumber { get; set; }
    public string? ContainerExtension { get; set; }
    public string? Plot { get; set; }
    public string StreamUrl { get; set; } = "";

    public string Display => $"S{Season:00}E{EpisodeNumber:00} – {Title}";
}
