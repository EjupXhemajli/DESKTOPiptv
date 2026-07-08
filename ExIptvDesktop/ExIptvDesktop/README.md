# EX-IPTV Desktop (Windows)

Nativer WPF-Client, kein WebView2 im Wiedergabepfad. Video-Rendering läuft
vollständig über LibVLCSharp direkt in ein natives `VideoView`-Control.

## Warum kein WebView2?

Die vorherige Hybrid-Architektur (WebView2-UI + LibVLCSharp-Player) verursachte
Rendering-Konkurrenz zwischen Chromium-Compositor und VLC-Direct3D-Ausgabe,
zusätzlichen IPC-Overhead bei jeder UI-Interaktion und einen deutlich höheren
RAM-Fußabdruck. Diese Version ersetzt das vollständig: UI und Player teilen
sich dieselbe native Render-Pipeline.

## Architektur

```
src/
  Models/          Datenmodelle (Channel, Category, Series, Episode, Movie)
  Services/
    PlaylistTypeDetector.cs   Erkennung M3U/M3U8/Xtream/Encoding
    M3uParser.cs               Streaming-Parser (100k+ Einträge, kein Vollladen)
    XtreamClient.cs            player_api.php-Client mit Retry/Backoff
    DatabaseService.cs         SQLite, Batch-Insert, Deduplizierung
    VlcPlayerService.cs        Player-Core: Watchdog, Auto-Reconnect, adaptiver Cache
    FileLogger.cs               Logging (Debug/Info/Warning/Error/Critical)
  ViewModels/       MVVM (CommunityToolkit.Mvvm)
  Views/            XAML, natives VideoView (LibVLCSharp.WPF)
```

## Build

Ausschließlich über GitHub Actions (`.github/workflows/build-windows.yml`):

- Push auf `main`/`develop` → Build + Artefakt-Upload
- Tag `v*` → zusätzlich automatischer Release mit `ExIptvDesktop.exe`
  (self-contained, single-file, win-x64)

Kein lokales .NET SDK notwendig — der Workflow übernimmt Restore, Build und
Publish vollständig.

## Nächste Schritte (nicht im Grundgerüst enthalten)

- VOD/Serien-Sync analog zu `SyncPlaylistAsync` (aktuell nur Live-TV)
- EPG-Integration (XMLTV-Parser + Anzeige in der Senderliste)
- Catch-up/Timeshift-UI (Backend-URLs sind in `XtreamClient` bereits vorbereitet)
- Stalker-/MAC-Portal-Support (Erkennung vorhanden, Client fehlt noch)
- Automatisierte Tests für `M3uParser` und `PlaylistTypeDetector`
