# EX-IPTV Desktop

Native Windows-IPTV-Anwendung (C# / .NET 8 / WPF) mit LibVLCSharp als Player-Engine.
Neuentwicklung ohne WebView2/VLC-Hybrid – der Player rendert direkt über `VideoView`.

## Funktionsumfang (Phase 1)

- **Quellen:** Xtream Codes (primär), M3U/M3U8-URL, lokale M3U-Datei
- **Auto-Erkennung** des Quelltyps aus URL (`get.php`, `player_api.php`) oder Dateipfad
- **Inhalte:** Live-TV, Filme (VOD), Serien mit lazy geladenen Episoden
- **Kategorien** je Sektion, Volltextsuche mit Debounce
- **Lokaler Cache** in SQLite (WAL), Bulk-Import in einer Transaktion – ausgelegt für 100k+ Einträge
- **Player:** konfigurierbares Puffer-/Netzwerk-Caching, Hardware-Dekodierung,
  automatische Neuverbindung mit Exponential-Backoff bei Stream-Abriss
- Robustes Logging (Serilog, rollierend), globale Fehlerbehandlung ohne harten Absturz

## Architektur

Ein einzelnes WPF-Projekt mit klarer Schichtung nach Ordnern:

```
ExIptv/
├─ Models/            Datenmodelle (Channel, VodStream, Series, Category, ...)
├─ Services/
│  ├─ Xtream/         Xtream-Codes-API-Client + DTOs
│  ├─ Playlist/       M3U-Parser, Quellen-Erkennung, Import-Orchestrierung
│  ├─ Data/           SQLite-Schema + Dapper-Repository (Bulk-Insert)
│  └─ Player/         LibVLCSharp-Wrapper mit Auto-Recovery
├─ ViewModels/        MVVM (CommunityToolkit.Mvvm, source-generated)
├─ Views/             XAML (MainWindow, SourceDialog, Theme)
└─ Converters/
```

**Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, Dapper + Microsoft.Data.Sqlite,
LibVLCSharp(.WPF), Microsoft.Extensions.DependencyInjection/Http(+Polly), Serilog.

## Build

Der Build läuft über **GitHub Actions** (`.github/workflows/build.yml`), kein lokales SDK nötig.

- **Push** auf `main`/`master` → baut Portable-ZIP + Setup-EXE als Artefakte
- **Tag** `v*` (z. B. `v0.1.0`) → hängt beide zusätzlich an ein GitHub-Release

Erzeugt werden:
- `EX-IPTV-Setup-<version>.exe` – Installer (Inno Setup, self-contained, deutsch)
- `ExIptv-portable-win-x64.zip` – entpacken und `ExIptv.exe` starten

### Lokal (falls SDK vorhanden)

```bash
dotnet publish ExIptv/ExIptv.csproj -c Release -r win-x64 --self-contained true -o publish
```

## Installation

`INSTALLIEREN.bat` doppelklicken. Das Skript:
1. startet eine `EX-IPTV-Setup-*.exe` bzw. `ExIptv-portable-*.zip` aus demselben Ordner, falls vorhanden,
2. lädt sonst automatisch das neueste Release von GitHub.

> Repo-Pfad im BAT (`set "REPO=..."`) einmalig auf das eigene Repository anpassen.

## Roadmap (Phase 2+)

- Stalker-/MAC-Portal-Quellen
- EPG (XMLTV) inkl. „Jetzt/Gleich"
- Catch-Up / Timeshift
- Metadaten-Anreicherung (TMDB) für Cover/Beschreibungen
- Favoriten-UI und zuletzt gesehen
- Adaptive Puffer-Strategie nach Messung der Verbindungsqualität
- Einstellungsdialog (Caching, Hardware-Dekodierung, Sprache)
