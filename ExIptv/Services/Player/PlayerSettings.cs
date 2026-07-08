namespace ExIptv.Services.Player;

/// <summary>
/// Puffer- und Netzwerkparameter für den VLC-Player. Werte in Millisekunden.
/// Defaults sind auf zügigen Senderwechsel bei akzeptabler Stabilität ausgelegt.
/// </summary>
public sealed class PlayerSettings
{
    /// <summary>Puffer für Live-Streams (--live-caching). Niedriger = schnellerer Zap, höher = stabiler.</summary>
    public int LiveCachingMs { get; set; } = 1500;

    /// <summary>Puffer für Netzwerk-Streams allgemein (--network-caching).</summary>
    public int NetworkCachingMs { get; set; } = 1500;

    /// <summary>Hardware-Dekodierung aktivieren (D3D11 auf Windows).</summary>
    public bool HardwareDecoding { get; set; } = true;

    /// <summary>Automatischer Neustart bei Stream-Fehler.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>Maximale aufeinanderfolgende Reconnect-Versuche, bevor aufgegeben wird.</summary>
    public int MaxReconnectAttempts { get; set; } = 5;
}
