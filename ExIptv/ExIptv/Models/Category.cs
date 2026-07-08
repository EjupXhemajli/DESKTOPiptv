namespace ExIptv.Models;

/// <summary>Kategorie/Gruppe für Live, VOD oder Serien.</summary>
public sealed class Category
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public ContentType ContentType { get; set; }

    /// <summary>Provider-seitige Kategorie-ID (bei Xtream ein String, bei M3U die group-title).</summary>
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public int ItemCount { get; set; }

    public override string ToString() => $"{Name} ({ItemCount})";
}
