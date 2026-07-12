using System.Windows.Threading;
using ExIptv.Models;
using ExIptv.Services.Settings;
using LibVLCSharp.Shared;
using Serilog;

namespace ExIptv.Services.Player;

/// <summary>Beschreibung einer wählbaren Audio-/Untertitelspur.</summary>
public readonly record struct TrackInfo(int Id, string Name);

/// <summary>
/// Kapselt LibVLC und MediaPlayer. Liest die aktuellen Einstellungen aus dem SettingsService,
/// setzt Puffer-/Decode-/Bildoptionen, überwacht Stream-Fehler und stellt bei Abbruch mit
/// Exponential-Backoff wieder her. VLC-Events werden thread-sicher über den Dispatcher gemarshallt.
/// </summary>
public sealed class VlcPlayerService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly Dispatcher _dispatcher;
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _currentMedia;

    private string? _currentUrl;
    private ContentType _currentType = ContentType.Live;
    private int _reconnectAttempts;
    private volatile bool _isReconnecting;
    private volatile bool _reconnectExhausted;
    private volatile bool _isPaused;
    private volatile bool _disposed;

    // Stall-Watchdog: erkennt eingefrorene Wiedergabe (Zeit läuft nicht weiter, obwohl gespielt wird).
    private DispatcherTimer? _watchdog;
    private long _lastPosMs = -1;
    private DateTime _lastAdvanceUtc = DateTime.UtcNow;

    private AppSettings S => _settingsService.Current;

    public event Action<string>? StatusChanged;         // Klartext-Status
    public event Action<bool>? PlayingChanged;           // true = spielt
    public event Action<long, long>? TimeChanged;        // (aktuelle ms, Gesamt-ms)
    public event Action? MediaReady;                     // Spuren verfügbar (nach Play)

    public VlcPlayerService(SettingsService settingsService, Dispatcher dispatcher)
    {
        _settingsService = settingsService;
        _dispatcher = dispatcher;
    }

    public MediaPlayer Player => _player ?? throw new InvalidOperationException("Player nicht initialisiert.");
    public bool IsSeekable => _player?.IsSeekable ?? false;
    public long LengthMs => _player?.Length ?? 0;

    public void Initialize()
    {
        if (_libVlc is not null) return;
        Core.Initialize();

        // Globale Basis-Argumente. Decode-Profil und Bildoptionen werden pro Medium gesetzt,
        // damit sie je Inhaltstyp unterschiedlich sein können.
        var args = new List<string>
        {
            "--no-video-title-show",
            "--quiet",
            $"--network-caching={S.NetworkCachingMs}",
            $"--live-caching={S.LiveCachingMs}",
            "--adaptive-livedelay=3000",
            "--clock-jitter=0",
            "--clock-synchro=0"
        };

        _libVlc = new LibVLC(args.ToArray());
        _player = new MediaPlayer(_libVlc);

        _player.EncounteredError += OnEncounteredError;
        _player.EndReached += OnEndReached;
        _player.Playing += OnPlaying;
        _player.Buffering += OnBuffering;
        _player.Paused += (_, _) => { _isPaused = true; Raise(() => PlayingChanged?.Invoke(false)); };
        _player.Stopped += (_, _) => Raise(() => PlayingChanged?.Invoke(false));
        _player.TimeChanged += (_, e) => Raise(() =>
        {
            var t = e.Time;
            if (t != _lastPosMs) { _lastPosMs = t; _lastAdvanceUtc = DateTime.UtcNow; }
            TimeChanged?.Invoke(t, _player?.Length ?? 0);
        });

        // Watchdog gegen eingefrorene Wiedergabe (läuft auf dem UI-Thread über den Dispatcher).
        _watchdog = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _watchdog.Tick += OnWatchdogTick;
        _watchdog.Start();

        Log.Information("VLC initialisiert (net={Net}ms live={Live}ms)", S.NetworkCachingMs, S.LiveCachingMs);
    }

    public void Play(string url, ContentType type)
    {
        if (_disposed) return;
        Initialize();
        _currentUrl = url;
        _currentType = type;
        _reconnectAttempts = 0;
        _reconnectExhausted = false;
        _isPaused = false;
        StartMedia(url, type);
    }

    private void StartMedia(string url, ContentType type, double startSeconds = 0)
    {
        if (_libVlc is null || _player is null) return;

        var media = new Media(_libVlc, new Uri(url));

        // VOD braucht deutlich mehr Puffer als Live, sonst friert die Wiedergabe bei
        // Bandbreitenschwankungen ein. Untergrenze 3000 ms, auch wenn die Einstellung kleiner ist.
        var caching = type == ContentType.Live ? S.LiveCachingMs : Math.Max(S.NetworkCachingMs, 3000);
        media.AddOption($":network-caching={caching}");
        if (type == ContentType.Live)
        {
            media.AddOption($":live-caching={S.LiveCachingMs}");
        }
        else
        {
            media.AddOption($":file-caching={caching}");
            media.AddOption(":http-reconnect");   // bei VOD-Abbruch automatisch neu verbinden
            // An letzter Position fortsetzen (nach Stall/Abbruch), sofern der Server Seeking erlaubt.
            if (startSeconds > 1)
                media.AddOption($":start-time={startSeconds:0.###}");
        }
        if (S.HttpReconnect && type == ContentType.Live)
            media.AddOption(":http-reconnect");

        // Decode-Profil (pro Inhaltstyp)
        var profile = type switch
        {
            ContentType.Live => S.LiveProfile,
            ContentType.Movie => S.MovieProfile,
            _ => S.SeriesProfile
        };
        foreach (var opt in ProfileOptions(profile, S.PlaybackMode))
            media.AddOption(opt);

        // Bild: Deinterlace, Bildrate, Bildverbesserung
        if (S.Deinterlace || S.FrameRateMode == FrameRateMode.Smooth)
        {
            media.AddOption(":deinterlace=1");
            media.AddOption(":deinterlace-mode=blend");
        }
        foreach (var opt in ImageOptions(S.ImageQuality))
            media.AddOption(opt);

        _player.Play(media);
        _lastAdvanceUtc = DateTime.UtcNow;   // Watchdog-Frist nach (Neu-)Start zurücksetzen
        _lastPosMs = -1;
        _isPaused = false;
        Raise(() => StatusChanged?.Invoke("Verbinde…"));

        var previous = _currentMedia;
        _currentMedia = media;
        previous?.Dispose();
    }

    private static IEnumerable<string> ProfileOptions(PlayerProfile profile, PlaybackMode mode)
    {
        // Wiedergabeweg beeinflusst die Dekodierung: "immer umwandeln" erzwingt Software-Pfad
        // (maximale Kompatibilität), "nur direkt" bevorzugt Hardware.
        var hw = profile switch
        {
            PlayerProfile.HardwareD3D11 => "d3d11va",
            PlayerProfile.HardwareDxva2 => "dxva2",
            PlayerProfile.Software => "none",
            PlayerProfile.Compatibility => "none",
            _ => "any"
        };
        if (mode == PlaybackMode.AlwaysTranscode) hw = "none";
        yield return $":avcodec-hw={hw}";
        if (profile == PlayerProfile.Compatibility || mode == PlaybackMode.AlwaysTranscode)
            yield return ":avcodec-threads=0"; // konservativ, alle Kerne
    }

    private static IEnumerable<string> ImageOptions(ImageQuality q)
    {
        switch (q)
        {
            case ImageQuality.Good:
                yield return ":video-filter=sharpen";
                yield return ":sharpen-sigma=0.15";
                break;
            case ImageQuality.Brilliant:
                yield return ":video-filter=sharpen";
                yield return ":sharpen-sigma=0.30";
                yield return ":contrast=1.08";
                yield return ":saturation=1.10";
                break;
        }
    }

    public void Stop()
    {
        _currentUrl = null;            // stoppt Watchdog und laufende Neuverbindung
        _reconnectExhausted = false;
        try { _player?.Stop(); } catch (Exception ex) { Log.Debug(ex, "Stop fehlgeschlagen"); }
    }

    public void TogglePause()
    {
        if (_player is { CanPause: true }) _player.Pause();
    }

    public void SetVolume(int volume)
    {
        if (_player is not null) _player.Volume = Math.Clamp(volume, 0, 100);
    }

    // ---------- Seek ----------

    /// <summary>Springt zu einer relativen Position 0..1.</summary>
    public void SeekTo(double position01)
    {
        if (_player is { IsSeekable: true })
        {
            _lastAdvanceUtc = DateTime.UtcNow;   // Puffern nach dem Sprung zählt nicht als Einfrieren
            _player.Position = (float)Math.Clamp(position01, 0, 1);
        }
    }

    /// <summary>Spult um die angegebenen Sekunden vor (positiv) oder zurück (negativ).</summary>
    public void SeekRelative(int seconds)
    {
        if (_player is null || !_player.IsSeekable) return;
        var len = _player.Length;
        if (len <= 0) return;
        _lastAdvanceUtc = DateTime.UtcNow;
        var target = Math.Clamp(_player.Time + seconds * 1000L, 0, len);
        _player.Time = target;
    }

    // ---------- Bildformat ----------

    public void SetAspectRatio(string key)
    {
        if (_player is null) return;
        // erst zurücksetzen
        _player.AspectRatio = null;
        _player.CropGeometry = null;
        _player.Scale = 0; // 0 = an Fenster anpassen
        switch (key)
        {
            case "16:9": _player.AspectRatio = "16:9"; break;
            case "4:3": _player.AspectRatio = "4:3"; break;
            case "16:10": _player.AspectRatio = "16:10"; break;
            case "Füllen": _player.CropGeometry = "16:9"; break; // Rand abschneiden, Fläche füllen
            // "Auto" -> alles zurückgesetzt
        }
    }

    // ---------- Spuren ----------

    public IReadOnlyList<TrackInfo> GetAudioTracks()
    {
        var list = new List<TrackInfo>();
        // Kein expliziter Elementtyp: LibVLCSharp legt die Spur-Beschreibung je nach Version
        // in unterschiedliche Namespaces – var leitet den Typ aus der Property ab.
        var desc = _player?.AudioTrackDescription;
        if (desc != null)
            foreach (var t in desc)
                list.Add(new TrackInfo(t.Id, string.IsNullOrWhiteSpace(t.Name) ? $"Spur {t.Id}" : t.Name));
        return list;
    }

    public IReadOnlyList<TrackInfo> GetSubtitleTracks()
    {
        var list = new List<TrackInfo>();
        var desc = _player?.SpuDescription;
        if (desc != null)
            foreach (var t in desc)
                list.Add(new TrackInfo(t.Id, string.IsNullOrWhiteSpace(t.Name) ? $"Spur {t.Id}" : t.Name));
        return list;
    }

    public int CurrentAudioTrack => _player?.AudioTrack ?? -1;
    public int CurrentSubtitleTrack => _player?.Spu ?? -1;
    public void SetAudioTrack(int id) { if (_player is not null) _player.SetAudioTrack(id); }
    public void SetSubtitleTrack(int id) { if (_player is not null) _player.SetSpu(id); }

    // ---------- Events ----------

    private void OnPlaying(object? sender, EventArgs e) => Raise(() =>
    {
        _isPaused = false;
        PlayingChanged?.Invoke(true);
        StatusChanged?.Invoke("Wiedergabe");
        MediaReady?.Invoke();
    });

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs e) =>
        Raise(() => StatusChanged?.Invoke(e.Cache >= 100f ? "Wiedergabe" : $"Puffere… {e.Cache:0}%"));

    private void OnEndReached(object? sender, EventArgs e)
    {
        var len = _player?.Length ?? 0;
        var nearEnd = len > 0 && _lastPosMs >= len * 0.97;   // wirklich zu Ende gesehen
        if (_currentType == ContentType.Live && S.AutoReconnect)
            TryReconnect("Stream beendet");
        else if (_currentType != ContentType.Live && S.AutoReconnect && len > 0 && !nearEnd)
            TryReconnect("Abbruch");                          // VOD vorzeitig abgebrochen -> an Position weiter
        else
            Raise(() => { PlayingChanged?.Invoke(false); StatusChanged?.Invoke("Beendet"); });
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        Log.Warning("VLC meldet Stream-Fehler");
        if (S.AutoReconnect) TryReconnect("Fehler");
        else Raise(() => StatusChanged?.Invoke("Fehler bei der Wiedergabe"));
    }

    // Erkennt eingefrorene Wiedergabe: Zustand ist "spielt" (nicht pausiert), aber die Zeit
    // steht seit >15 s. LibVLC feuert dabei oft kein Event, daher dieser aktive Wächter.
    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (_disposed || _player is null || _currentUrl is null) return;
        if (_isReconnecting || _reconnectExhausted || _isPaused || !S.AutoReconnect) return;
        if (!_player.IsPlaying) return;
        var stalled = (DateTime.UtcNow - _lastAdvanceUtc).TotalSeconds;
        if (stalled > 15)
        {
            Log.Warning("Wiedergabe eingefroren ({Sec:0}s ohne Fortschritt) – Neuverbindung", stalled);
            TryReconnect("Eingefroren");
        }
    }

    private void TryReconnect(string reason)
    {
        if (_isReconnecting || _reconnectExhausted || _disposed || _currentUrl is null) return;
        _isReconnecting = true;
        _ = Task.Run(async () =>
        {
            var url = _currentUrl;
            var type = _currentType;
            var maxAttempts = Math.Max(1, S.MaxReconnectAttempts);
            // VOD an der zuletzt bekannten Position fortsetzen (kleiner Rücksprung als Puffer).
            var resumeSeconds = type != ContentType.Live && _lastPosMs > 3000
                ? _lastPosMs / 1000.0 - 2
                : 0;

            while (!_disposed && url is not null && _reconnectAttempts < maxAttempts)
            {
                _reconnectAttempts++;
                var delay = TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, _reconnectAttempts - 1), 8000));
                Raise(() => StatusChanged?.Invoke($"{reason} – Neuverbindung {_reconnectAttempts}/{maxAttempts}…"));
                await Task.Delay(delay);
                if (_disposed || _currentUrl != url) break;
                try
                {
                    StartMedia(url, type, resumeSeconds);
                    await Task.Delay(3000);
                    if (_player is { IsPlaying: true })
                    {
                        _reconnectAttempts = 0;
                        _isReconnecting = false;
                        return;
                    }
                }
                catch (Exception ex) { Log.Warning(ex, "Reconnect fehlgeschlagen"); }
            }

            if (_reconnectAttempts >= maxAttempts)
            {
                _reconnectExhausted = true;   // kein Dauer-Neuverbinden mehr, bis der Nutzer neu wählt
                Raise(() => StatusChanged?.Invoke("Verbindung dauerhaft verloren. Bitte erneut wählen."));
            }
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
            _watchdog?.Stop();
            if (_player is not null)
            {
                _player.EncounteredError -= OnEncounteredError;
                _player.EndReached -= OnEndReached;
                _player.Playing -= OnPlaying;
                _player.Buffering -= OnBuffering;
                _player.Stop();
                _player.Dispose();
            }
            _currentMedia?.Dispose();
            _libVlc?.Dispose();
        }
        catch (Exception ex) { Log.Debug(ex, "Fehler beim Dispose des Players"); }
    }
}
