namespace ExIptv.Models;

/// <summary>Ein Film (Video on Demand).</summary>
public sealed class VodStream
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public string ExternalId { get; set; } = "";     // stream_id
    public string Name { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string? ContainerExtension { get; set; }  // z. B. mp4, mkv
    public string CategoryExternalId { get; set; } = "";
    public double? Rating { get; set; }
    public string? Year { get; set; }

    public string StreamUrl { get; set; } = "";

    public override string ToString() => Name;
}
