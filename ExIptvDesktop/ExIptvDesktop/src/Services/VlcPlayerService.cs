using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ExIptvDesktop.Models;
using LibVLCSharp.Shared;

namespace ExIptvDesktop.Services;

public enum PlaybackState { Idle, Opening, Buffering, Playing, Paused, Error, Reconnecting }

public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    public PlaybackState State { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Kapselt LibVLC vollstaendig nativ (kein WebView/HTML im Player-Pfad).
/// Enthaelt:
///  - Adaptives Netzwerk-Caching (Live vs. VOD unterschiedlich)
///  - Watchdog gegen Decoder-Freezes (erkennt Stillstand der Wiedergabezeit
///    trotz "Playing"-Status und erzwingt Reconnect)
///  - Automatischer Reconnect mit exponentiellem Backoff bei Verbindungsabbruch
/// </summary>
public sealed class VlcPlayerService : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly FileLogger _logger;
    private readonly Dispatcher _dispatcher;

    private MediaPlayer? _mediaPlayer;
    private DispatcherTimer? _watchdogTimer;
    private long _lastKnownTimeMs = -1;
    private int _stallTicks;
    private int _reconnectAttempt;
    private string? _currentMrl;
    private bool _isLive;
    private CancellationTokenSource? _reconnectCts;

    private const int WatchdogIntervalMs = 2000;
    private const int MaxStallTicksBeforeRecovery = 4; // 4 x 2s = 8s ohne Fortschritt -> Recovery
    private const int MaxReconnectAttempts = 6;

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    public MediaPlayer? Player => _mediaPlayer;

    public VlcPlayerService(LibVLC libVlc, FileLogger logger)
    {
        _libVlc = libVlc;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public MediaPlayer CreatePlayer()
    {
        _mediaPlayer = new MediaPlayer(_libVlc)
        {
            EnableHardwareDecoding = true,
        };

        _mediaPlayer.EncounteredError += (_, _) => RaiseState(PlaybackState.Error, "Wiedergabefehler.");
        _mediaPlayer.Playing += (_, _) => RaiseState(PlaybackState.Playing);
        _mediaPlayer.Buffering += (_, e) =>
        {
            if (e.Cache < 100f) RaiseState(PlaybackState.Buffering, $"{e.Cache:F0}%");
        };
        _mediaPlayer.EndReached += (_, _) => OnEndReached();

        _watchdogTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(WatchdogIntervalMs)
        };
        _watchdogTimer.Tick += WatchdogTick;
        _watchdogTimer.Start();

        return _mediaPlayer;
    }

    /// <summary>
    /// Oeffnet einen Stream mit auf Live/VOD abgestimmten Caching-Parametern.
    /// Live: kleinerer Puffer fuer geringe Latenz beim Senderwechsel.
    /// VOD/Catch-up: groesserer Puffer, da Latenz weniger kritisch ist als
    /// Stabilitaet bei schwankender Bandbreite.
    /// </summary>
    public void Open(string mrl, bool isLive)
    {
        if (_mediaPlayer == null) throw new InvalidOperationException("CreatePlayer() zuerst aufrufen.");

        _currentMrl = mrl;
        _isLive = isLive;
        _reconnectAttempt = 0;
        _lastKnownTimeMs = -1;
        _stallTicks = 0;

        RaiseState(PlaybackState.Opening);

        using var media = new Media(_libVlc, mrl, FromType.FromLocation);

        if (isLive)
        {
            // Niedrige Netzwerk-Cache-Zeit fuer schnellen Senderwechsel;
            // Grossteil der Stabilitaet kommt hier vom Watchdog+Reconnect, nicht von Riesenpuffern.
            media.AddOption(":network-caching=1500");
            media.AddOption(":live-caching=1500");
            media.AddOption(":clock-jitter=0");
            media.AddOption(":clock-synchro=0");
        }
        else
        {
            media.AddOption(":network-caching=3000");
            media.AddOption(":file-caching=1000");
        }

        // TCP statt UDP fuer HLS/HTTP-Streams robuster gegen Paketverlust bei
        // instabilen Verbindungen; automatische Wiederverbindung durch VLC-Core selbst.
        media.AddOption(":http-reconnect");

        _mediaPlayer.Play(media);
    }

    public void Pause() => _mediaPlayer?.Pause();
    public void Resume() => _mediaPlayer?.Play();
    public void Stop() => _mediaPlayer?.Stop();

    public void SeekTo(TimeSpan position)
    {
        if (_mediaPlayer != null) _mediaPlayer.Time = (long)position.TotalMilliseconds;
    }

    private void WatchdogTick(object? sender, EventArgs e)
    {
        if (_mediaPlayer == null || _currentMrl == null) return;

        var state = _mediaPlayer.State;

        if (state != VLCState.Playing)
        {
            _stallTicks = 0;
            return;
        }

        var currentTime = _mediaPlayer.Time;

        // Wiedergabezeit bewegt sich nicht, obwohl Status "Playing" ist -> klassisches
        // Freeze-Symptom (Decoder haengt, Netzwerk still, o.ae.).
        if (currentTime == _lastKnownTimeMs)
        {
            _stallTicks++;
            _logger.Warning("Watchdog", $"Kein Fortschritt seit {_stallTicks * WatchdogIntervalMs}ms.");

            if (_stallTicks >= MaxStallTicksBeforeRecovery)
            {
                _stallTicks = 0;
                _ = TriggerReconnectAsync("Watchdog: Stillstand erkannt.");
            }
        }
        else
        {
            _stallTicks = 0;
        }

        _lastKnownTimeMs = currentTime;
    }

    private void OnEndReached()
    {
        // Bei Live-Streams bedeutet EndReached meist einen Verbindungsabbruch,
        // nicht ein natuerliches Ende -> Reconnect versuchen.
        if (_isLive)
            _ = TriggerReconnectAsync("Stream-Ende erreicht (Live) -> vermutlich Verbindungsabbruch.");
    }

    private async Task TriggerReconnectAsync(string reason)
    {
        if (_currentMrl == null) return;

        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();
        var ct = _reconnectCts.Token;

        if (_reconnectAttempt >= MaxReconnectAttempts)
        {
            _logger.Error("VlcPlayerService", $"Max. Reconnect-Versuche erreicht ({reason}).");
            RaiseState(PlaybackState.Error, "Verbindung konnte nicht wiederhergestellt werden.");
            return;
        }

        _reconnectAttempt++;
        var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, _reconnectAttempt));
        _logger.Warning("VlcPlayerService",
            $"Reconnect-Versuch {_reconnectAttempt}/{MaxReconnectAttempts} in {delay.TotalMilliseconds}ms ({reason}).");

        RaiseState(PlaybackState.Reconnecting, $"Versuch {_reconnectAttempt}/{MaxReconnectAttempts}");

        try
        {
            await Task.Delay(delay, ct);
            if (ct.IsCancellationRequested) return;

            await _dispatcher.InvokeAsync(() => Open(_currentMrl!, _isLive));
        }
        catch (TaskCanceledException)
        {
            // Reconnect wurde durch neue Nutzeraktion (z. B. manueller Senderwechsel) abgebrochen.
        }
    }

    private void RaiseState(PlaybackState state, string? message = null) =>
        _dispatcher.BeginInvoke(() =>
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs { State = state, Message = message }));

    public void Dispose()
    {
        _watchdogTimer?.Stop();
        _reconnectCts?.Cancel();
        _mediaPlayer?.Stop();
        _mediaPlayer?.Dispose();
    }
}
