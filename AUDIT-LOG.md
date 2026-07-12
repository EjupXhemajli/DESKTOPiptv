# AUDIT-LOG — EX-IPTV Desktop

## Audit 2026-07-12 (Voll-Audit, v0.7 → v0.8)

Scan vorher: 3 Gefahrenmuster-Treffer · nachher: 2 (beide verifizierte Fehlalarme).
Qualitäts-Score: 82/100 (Struktur 22/25, Funktionalität 20/25, Stabilität 21/25, Verbesserung/Diagnose 19/25).

### Befunde (behoben)
- **K1** PasswordBox im Quelle-Dialog unbenutzbar/unsichtbar. Ursache: einziges Eingabefeld ohne eigenes ControlTemplate → Standard-Chrome geht im dunklen Dialog unter. Fix: Template mit PART_ContentHost analog TextBox (Theme.xaml). ✅ XAML validiert.
- **K2** Diagnose-Blindflug: Serilog MinimumLevel=Information verwarf alle Debug-Logs (Poster-Nachladen, Player). Fix: MinimumLevel=Debug; 7-Tage-Rotation begrenzt Größe (App.xaml.cs). ✅
- **H1** ImageLoader: `async void` als DP-Callback (Prozessabriss-Risiko bei unerwarteter Exception vor erstem await) + unbegrenzter Bild-Cache (~240 KB/Bild → GB-Risiko). Fix: synchroner Callback + abgekoppeltes `LoadAsync` mit vollständiger Fehlerdeckung; Cache-Deckel 800 Einträge (~190 MB). ✅
- **M1** Totes Setting: „Zusätzlicher Datei-Puffer" (FileCaching) war seit v0.5 wirkungslos. Fix: wieder an `:file-caching` gebunden (VlcPlayerService). ✅
- **D1** Poster-Nachladen ohne Ergebnis-Transparenz. Fix: Information-Log „X gefunden, Y ohne Bild" + differenzierte Statuszeile inkl. „Panel liefert keine Poster". ✅

### Verifizierte Fehlalarme (nicht erneut aufrollen)
- scan: `.Result` MainViewModel (2×) = `dialogVm.Result` (ViewModel-Property, kein Task).
- scan: Secret-Verdacht SourceDialog.xaml.cs:34 = PasswordBox→VM-Spiegelung (PasswordBox ist nicht bindbar; by design).
- ImageLoader `async void` (vor H1): war DP-Callback mit try/catch-Deckung; dennoch auf sauberes Muster umgestellt.

### Geprüfte Kernpfade (ohne Befund)
Lebenszyklus (OnExit: Settings.Save + Player.Dispose), Volume-/AspectRatio-Persistenz, Kategorie→LoadItems-Kette mit Generation-Guard, GetJsonAsync (using/EnsureSuccess/JsonException geloggt), SQL durchgängig parametrisiert (OrderClause aus Festwerten), Watchdog-/Reconnect-Zustandsmaschine (Erschöpfungs-Flag, Pause-/Seek-Ausnahmen), GridSplitter-Layout, Xtream-Guard vor Poster-Nachladen.

### Offene Vorschläge (Verbesserung, nicht umgesetzt — nur auf Auftrag)
- V1: Virtualisiertes Poster-Grid (VirtualizingWrapPanel) → hebt 500er-Limit. Nutzen hoch / Aufwand mittel / Risiko mittel.
- V2: EPG-Anzeige (Programm jetzt/gleich) für Live. Nutzen hoch / Aufwand hoch.
- V3: Cache-Eviction LRU statt Clear-Deckel. Nutzen niedrig / Aufwand niedrig.
- V4: Stalker/MAC-Portal-Quellen (Phase 2 laut Plan).
