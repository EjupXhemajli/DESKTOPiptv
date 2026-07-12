using CommunityToolkit.Mvvm.ComponentModel;
using ExIptv.Models;

namespace ExIptv.ViewModels;

/// <summary>
/// Vereinheitlichte Darstellung eines abspielbaren (oder aufklappbaren) Listeneintrags,
/// damit Live, Filme und Serien in derselben Liste dargestellt werden können.
/// LogoUrl ist beobachtbar, damit nachgeladene Poster (get_vod_info) sofort erscheinen.
/// </summary>
public sealed partial class PlayableItem : ObservableObject
{
    public required string Name { get; init; }
    public string? Subtitle { get; init; }
    public string StreamUrl { get; init; } = "";
    public ContentType Type { get; init; }
    public string ExternalId { get; init; } = "";

    /// <summary>Datenbank-Id (vod_streams.Id bzw. series.Id) für Poster-Rückschreiben.</summary>
    public int DbId { get; init; }

    /// <summary>True, wenn es sich um eine Serie handelt (Klick lädt Episoden statt abzuspielen).</summary>
    public bool IsSeriesContainer { get; init; }

    [ObservableProperty] private string? _logoUrl;

    public static PlayableItem FromLive(LiveChannel c) => new()
    {
        Name = c.Name, LogoUrl = c.LogoUrl, StreamUrl = c.StreamUrl,
        Type = ContentType.Live, ExternalId = c.ExternalId
    };

    public static PlayableItem FromMovie(VodStream m) => new()
    {
        Name = m.Name, LogoUrl = m.PosterUrl, StreamUrl = m.StreamUrl,
        Subtitle = m.Year, Type = ContentType.Movie, ExternalId = m.ExternalId, DbId = m.Id
    };

    public static PlayableItem FromSeries(Series s) => new()
    {
        Name = s.Name, LogoUrl = s.PosterUrl, Subtitle = s.Rating?.ToString("0.0"),
        Type = ContentType.Series, ExternalId = s.ExternalId, DbId = s.Id, IsSeriesContainer = true
    };
}
