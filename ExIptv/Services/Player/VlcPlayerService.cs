using System.Windows.Threading;
using ExIptv.Models;
using LibVLCSharp.Shared;
using Serilog;

namespace ExIptv.Services.Player;

/// <summary>
/// Kapselt LibVLC und MediaPlayer. Setzt Puffer-/Netzwerkoptionen, überwacht Stream-Fehler
/// und stellt bei Abbruch automatisch mit Exponential-Backoff wieder her.
///
/// Wichtig: VLC-Events kommen aus einem Fremd-Thread; State-Änderungen für die UI werden
/// über den Dispatcher gemarshallt. Reconnect selbst läuft entkoppelt, um Re-Entrancy
/// aus den Event-Handlern zu vermeiden.
/// </summary>
public sealed class VlcPlayerService : IDisposable
{
    private readonly PlayerSettings _settings;
    private readonly Dispatcher _dispatcher;
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _currentMedia;

    private string? _currentUrl;
    private ContentType _currentType = ContentType.Live;
    private int _reconnectAttempts;
    private volatile bool _isReconnecting;
    private volatile bool _disposed;

    public event Action<string>? StatusChanged;   // Klartext-Status für die UI
    public event Action<bool>? PlayingChanged;     // true = spielt

    public VlcPlayerService(PlayerSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
    }

    public MediaPlayer Player => _player ?? throw new InvalidOperationException("Player nicht initialisiert. Initialize() aufrufen.");

    /// <summary>Muss einmalig nach dem Laden des Fensters aufgerufen werden (Core.Initialize()).</summary>
    public void Initialize()
    {
        if (_libVlc is not null) return;

        Core.Initialize(); // lädt die nativen libvlc-Bibliotheken

        var args = new List<string>
        {
            "--no-video-title-show",
            "--quiet",
            $"--network-caching={_settings.NetworkCachingMs}",
            $"--live-caching={_settings.LiveCachingMs}",
            "--http-reconnect",
            "--adaptive-livedelay=3000",
            "--clock-jitter=0",
            "--clock-synchro=0"
        };
        if (!_settings.HardwareDecoding)
            args.Add("--avcodec-hw=none");

        _libVlc = new LibVLC(args.ToArray());
        _player = new MediaPlayer(_libVlc);

        _player.EncounteredError += OnEncounteredError;
        _player.EndReached += OnEndReached;
        _player.Playing += (_, _) => Raise(() => { PlayingChanged?.Invoke(true); StatusChanged?.Invoke("Wiedergabe"); });
        _player.Buffering += (_, e) => Raise(() => StatusChanged?.Invoke(e.Cache >= 100f ? "Wiedergabe" : $"Puffere… {e.Cache:0}%"));
        _player.Paused += (_, _) => Raise(() => PlayingChanged?.Invoke(false));
        _player.Stopped += (_, _) => Raise(() => PlayingChanged?.Invoke(false));

        Log.Information("VLC initialisiert (network-caching={Net}ms, live-caching={Live}ms, hwdec={Hw})",
            _settings.NetworkCachingMs, _settings.LiveCachingMs, _settings.HardwareDecoding);
    }

    /// <summary>Spielt eine URL ab. Setzt streamtyp-abhängige Caching-Optionen pro Medium.</summary>
    public void Play(string url, ContentType type)
    {
        if (_disposed) return;
        Initialize();

        _currentUrl = url;
        _currentType = type;
        _reconnectAttempts = 0;
        StartMedia(url, type);
    }

    private void StartMedia(string url, ContentType type)
    {
        if (_libVlc is null || _player is null) return;

        var media = new Media(_libVlc, new Uri(url));

        // Pro-Medium-Optionen (überschreiben die globalen Defaults gezielt).
        var caching = type == ContentType.Live ? _settings.LiveCachingMs : _settings.NetworkCachingMs;
        media.AddOption($":network-caching={caching}");
        if (type == ContentType.Live)
            media.AddOption(":live-caching=" + _settings.LiveCachingMs);
        media.AddOption(":http-reconnect");

        _player.Play(media);
        Raise(() => StatusChanged?.Invoke("Verbinde…"));

        // Vorheriges Medium erst nach dem Umschalten freigeben (VLC hält das aktive selbst).
        var previous = _currentMedia;
        _currentMedia = media;
        previous?.Dispose();
    }

    public void Stop()
    {
        _currentUrl = null;
        try { _player?.Stop(); } catch (Exception ex) { Log.Debug(ex, "Stop fehlgeschlagen"); }
    }

    public void TogglePause()
    {
        if (_player is null) return;
        if (_player.CanPause) _player.Pause();
    }

    public void SetVolume(int volume)
    {
        if (_player is null) return;
        _player.Volume = Math.Clamp(volume, 0, 100);
    }

    // ---------- Fehlerbehandlung / Auto-Recovery ----------

    private void OnEndReached(object? sender, EventArgs e)
    {
        // Bei Live bedeutet EndReached fast immer einen Abriss -> Reconnect.
        if (_currentType == ContentType.Live && _settings.AutoReconnect)
            TryReconnect("Stream beendet");
        else
            Raise(() => { PlayingChanged?.Invoke(false); StatusChanged?.Invoke("Beendet"); });
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        Log.Warning("VLC meldet Stream-Fehler für {Url}", _currentUrl is null ? "(keine URL)" : "***");
        if (_settings.AutoReconnect)
            TryReconnect("Fehler");
        else
            Raise(() => StatusChanged?.Invoke("Fehler bei der Wiedergabe"));
    }

    private void TryReconnect(string reason)
    {
        if (_isReconnecting || _disposed || _currentUrl is null) return;
        _isReconnecting = true;

        _ = Task.Run(async () =>
        {
            var url = _currentUrl;
            var type = _currentType;
            while (!_disposed && url is not null && _reconnectAttempts < _settings.MaxReconnectAttempts)
            {
                _reconnectAttempts++;
                var delay = TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, _reconnectAttempts - 1), 8000));
                Raise(() => StatusChanged?.Invoke($"{reason} – Neuverbindung {_reconnectAttempts}/{_settings.MaxReconnectAttempts}…"));
                Log.Information("Reconnect-Versuch {N} in {Ms}ms", _reconnectAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay);

                if (_disposed || _currentUrl != url) break; // Nutzer hat gewechselt

                try
                {
                    // Play muss aus einem sinnvollen Kontext laufen; VLC selbst ist thread-safe genug.
                    StartMedia(url, type);
                    // kurze Wartezeit, dann prüfen, ob wir wieder spielen
                    await Task.Delay(2500);
                    if (_player is { IsPlaying: true })
                    {
                        _reconnectAttempts = 0;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Reconnect fehlgeschlagen");
                }
            }

            if (_reconnectAttempts >= _settings.MaxReconnectAttempts)
                Raise(() => StatusChanged?.Invoke("Verbindung dauerhaft verloren. Bitte Sender erneut wählen."));

            _isReconnecting = false;
        });
    }

    private void Raise(Action action)
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_player is not null)
            {
                _player.EncounteredError -= OnEncounteredError;
                _player.EndReached -= OnEndReached;
                _player.Stop();
                _player.Dispose();
            }
            _currentMedia?.Dispose();
            _libVlc?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Fehler beim Dispose des Players");
        }
    }
}
