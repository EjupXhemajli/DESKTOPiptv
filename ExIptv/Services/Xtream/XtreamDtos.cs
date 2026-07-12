using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExIptv.Services.Xtream;

/// <summary>
/// DTOs für die Xtream-Codes-"player_api.php".
/// Die API ist notorisch inkonsistent: Zahlen kommen mal als Zahl, mal als String.
/// Daher werden alle numerischen Felder als string? deklariert und defensiv geparst.
/// </summary>
internal sealed class XtreamCategoryDto
{
    [JsonPropertyName("category_id")] public string? CategoryId { get; set; }
    [JsonPropertyName("category_name")] public string? CategoryName { get; set; }
}

internal sealed class XtreamLiveStreamDto
{
    [JsonPropertyName("stream_id")] public JsonElement StreamId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("stream_icon")] public string? StreamIcon { get; set; }
    [JsonPropertyName("epg_channel_id")] public string? EpgChannelId { get; set; }
    [JsonPropertyName("category_id")] public JsonElement CategoryId { get; set; }
}

internal sealed class XtreamVodStreamDto
{
    [JsonPropertyName("stream_id")] public JsonElement StreamId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("stream_icon")] public string? StreamIcon { get; set; }
    // Panel-abhängig: manche Panels liefern das Filmbild nicht in stream_icon,
    // sondern in cover oder movie_image.
    [JsonPropertyName("cover")] public string? Cover { get; set; }
    [JsonPropertyName("movie_image")] public string? MovieImage { get; set; }
    [JsonPropertyName("category_id")] public JsonElement CategoryId { get; set; }
    [JsonPropertyName("container_extension")] public string? ContainerExtension { get; set; }
    [JsonPropertyName("rating")] public string? Rating { get; set; }
    [JsonPropertyName("year")] public string? Year { get; set; }
}

internal sealed class XtreamSeriesDto
{
    [JsonPropertyName("series_id")] public JsonElement SeriesId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("cover")] public string? Cover { get; set; }
    [JsonPropertyName("plot")] public string? Plot { get; set; }
    [JsonPropertyName("category_id")] public JsonElement CategoryId { get; set; }
    [JsonPropertyName("rating")] public string? Rating { get; set; }
}

// --- get_series_info ---
internal sealed class XtreamSeriesInfoDto
{
    [JsonPropertyName("episodes")] public Dictionary<string, List<XtreamEpisodeDto>>? Episodes { get; set; }
}

internal sealed class XtreamEpisodeDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("season")] public JsonElement Season { get; set; }
    [JsonPropertyName("episode_num")] public JsonElement EpisodeNum { get; set; }
    [JsonPropertyName("container_extension")] public string? ContainerExtension { get; set; }
    [JsonPropertyName("info")] public XtreamEpisodeInfoDto? Info { get; set; }
}

internal sealed class XtreamEpisodeInfoDto
{
    [JsonPropertyName("plot")] public string? Plot { get; set; }
}

// --- Verbindungstest (player_api ohne action) ---
internal sealed class XtreamAuthResponseDto
{
    [JsonPropertyName("user_info")] public XtreamUserInfoDto? UserInfo { get; set; }
}

internal sealed class XtreamUserInfoDto
{
    [JsonPropertyName("auth")] public JsonElement Auth { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}
