namespace ExIptv.Models;

public enum PlaybackMode { Auto, DirectOnly, AlwaysTranscode }
public enum ImageQuality { Standard, Good, Brilliant }
public enum FrameRateMode { Standard, Smooth }

/// <summary>
/// Player-Modus. Da LibVLC die Engine ist, sind dies verschiedene Decode-/Render-Profile
/// derselben Engine – bewusst so, weil fünf getrennte native Player-Backends unverhältnismäßig
/// wären. Jedes Profil setzt andere LibVLC-Argumente (siehe VlcPlayerService).
/// </summary>
public enum PlayerProfile
{
    Auto,            // Hardware-Dekodierung automatisch
    HardwareD3D11,   // erzwungen D3D11VA
    HardwareDxva2,   // erzwungen DXVA2
    Software,        // reine Software-Dekodierung (max. Kompatibilität)
    Compatibility    // Software + konservatives Rendering
}

/// <summary>
/// Alle dauerhaft gespeicherten Einstellungen. Wird als JSON in
/// %AppData%\EX-IPTV\settings.json abgelegt.
/// </summary>
public sealed class AppSettings
{
    // --- Puffer (ms) ---
    public int NetworkCachingMs { get; set; } = 1500;
    public int LiveCachingMs { get; set; } = 1500;

    // --- Stabilisierung ---
    public bool AutoReconnect { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = 5;
    public bool HttpReconnect { get; set; } = true;
    public bool FileCaching { get; set; } = true;   // zusätzlicher Puffer für VOD

    // --- Wiedergabeweg ---
    public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.Auto;

    // --- Bild ---
    public ImageQuality ImageQuality { get; set; } = ImageQuality.Standard;
    public FrameRateMode FrameRateMode { get; set; } = FrameRateMode.Standard;
    public bool Deinterlace { get; set; }

    // --- Player-Profil je Inhaltstyp ---
    public PlayerProfile LiveProfile { get; set; } = PlayerProfile.Auto;
    public PlayerProfile MovieProfile { get; set; } = PlayerProfile.Auto;
    public PlayerProfile SeriesProfile { get; set; } = PlayerProfile.Auto;

    // --- EPG ---
    public int EpgOffsetHours { get; set; }

    // --- Aussehen ---
    public string BackgroundThemeKey { get; set; } = "midnight";
    public string TextColorKey { get; set; } = "default";

    // --- Wiedergabe-Zustand ---
    public int Volume { get; set; } = 90;
    public string AspectRatio { get; set; } = "Auto";
}
