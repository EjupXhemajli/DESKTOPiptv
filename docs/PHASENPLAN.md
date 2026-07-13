# Entwicklungsphasen – Stand und Plan

## ✅ Phase 1 – Fundament (abgeschlossen)
- Projektstruktur (core / src-tauri / src / docs)
- Tauri-2-Konfiguration inkl. CSP, Fenster, Bundle (MSI + NSIS, DE/EN)
- Logo integriert (`public/assets/branding/`), App-Icons aus dem Logo generiert
- Designsystem: Tokens (`src/styles/tokens.css`), EX-Ring-Signaturmotiv,
  Skeleton-Loading, Fokuszustände, reduzierte Animationen (prefers-reduced-motion)
- Navigation (einklappbare Seitenleiste, Zustand persistiert), Routing, i18n DE/EN
- CI: Core-Tests + Windows-Installer-Build

## ✅ Phase 2 – Datenfundament (abgeschlossen)
- SQLite mit WAL, Foreign Keys, busy_timeout
- Migration v1: alle 23 Entitäten aus Abschnitt 26 inkl. Indizes und Löschregeln
- Einstellungsservice (Upsert, im UI: Sprache, Seitenleisten-Zustand)
- Provider-Datenmodell; Zugangsdaten über Windows Credential Manager
  (`secrets.rs`), DB hält nur `secret_ref`

## ✅ Phase 3 – M3U-Import (Kern abgeschlossen)
- Toleranter Parser: EXTINF-Attribute (tvg-id/-name/-logo/-chno, group-title,
  radio, catchup[-days/-source], timeshift, provider, audio-track,
  user-agent, referrer), #EXTGRP, #EXTVLCOPT, Kommentar-Direktiven
- Encoding: UTF-8 (±BOM), UTF-16 LE/BE, Windows-1252-Fallback
- Relative-URL-Auflösung gegen Playlist-Basis
- Import über URL (Retry/Backoff) und Datei (nativer Dateidialog)
- Staging-Verfahren: Altbestand bleibt bei jedem Fehlschlag erhalten
- Fortschritts-Events, Importprotokoll (`import_jobs`), Fehler-Warnliste
- UI: Anbieterverwaltung, Live-TV mit Gruppen + virtualisierter Liste,
  inkrementelles Nachladen, Logo-Fallback
- Offen in Phase 3 (bewusst): Duplikaterkennung über Anbieter hinweg,
  Kanal-Verstecken/-Sperren im UI (Datenmodell vorhanden)

## 🔜 Phase 4 – Wiedergabe
libmpv-Integration (`--wid`-Einbettung, D3D11VA + Software-Fallback),
PlaybackEngine-Implementierung, OSD beim Umschalten, Audio-/Untertitelspuren,
Vollbild, StreamHealthMonitor mit gestufter Wiederherstellung
(intern → URL neu laden → Player-Neustart → alternative Methode → Meldung).
**Technische Abhängigkeit:** mpv-2.dll (libmpv ≥ 0.38) wird dem Bundle
beigelegt; Lizenzhinweis (LGPL) in Drittanbieter-Übersicht.

## 🔜 Phase 5 – Xtream-Codes
player_api-Client (get_live_streams, get_vod_streams, get_series,
Kategorien, Kontostatus/Ablauf/max_connections), Provider-Statusanzeige.

## 🔜 Phase 6 – EPG
Streaming-XMLTV-Parser (quick-xml, auch .gz), tvg-id-Auto-Mapping +
manuelle Zuordnung, Zeitversatz je Quelle/Kanal, virtualisierte
Zeitleisten-Ansicht, Jetzt/Danach, Erinnerungen-Datenpfad.

## 🔜 Phase 7 – VOD/Serien-UI + Poster-Cache
LRU-Bildcache auf Datenträger (Tabelle vorhanden), begrenzte
Parallel-Downloads, Platzhalter, Wiedergabefortschritt.

## 🔜 Phase 8 – Profile, Favoriten, Verlauf, globale Suche, Jugendschutz
PIN als Hash (argon2), Sperrlisten, Gastprofil.

## 🔜 Phase 9 – Catch-up, Aufnahme, Timeshift, Multi-View
Nur ungeschützte, anbieterseitig freigegebene Streams; Ringpuffer;
Ressourcenüberwachung für Multi-View.

## 🔜 Phase 10 – Härtung
E2E-Tests (WebDriver), Performance-Szenarien (10k Sender / 100k EPG),
Update-System (tauri-plugin-updater, signierte Pakete), Benutzerhandbuch-Ausbau.
