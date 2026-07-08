@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
title EX-IPTV Desktop - Installation

REM ============================================================
REM  EX-IPTV Desktop - One-Click-Installer
REM
REM  Ablauf:
REM   1) Liegt eine Setup-EXE im selben Ordner? -> direkt starten.
REM   2) Sonst: neuestes Release von GitHub laden und starten.
REM
REM  GITHUB-Repo einmalig anpassen (Format: benutzer/repo):
set "REPO=don1scriptz/ex-iptv-desktop"
REM ============================================================

echo.
echo   ==========================================
echo    EX-IPTV Desktop - Installation
echo   ==========================================
echo.

REM --- 1) Lokale Setup-EXE suchen ---
for %%F in ("%~dp0EX-IPTV-Setup-*.exe") do (
    echo   Lokales Setup gefunden: %%~nxF
    echo   Starte Installation...
    start "" "%%F"
    goto :done
)

REM --- 2) Portable ZIP im Ordner? ---
for %%F in ("%~dp0ExIptv-portable-*.zip") do (
    echo   Portable-Version gefunden: %%~nxF
    set "TARGET=%LOCALAPPDATA%\EX-IPTV\App"
    echo   Entpacke nach: !TARGET!
    powershell -NoProfile -Command "Expand-Archive -Path '%%F' -DestinationPath '!TARGET!' -Force"
    echo   Erstelle Desktop-Verknuepfung...
    powershell -NoProfile -Command ^
      "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop')+'\EX-IPTV Desktop.lnk'); $s.TargetPath='!TARGET!\ExIptv.exe'; $s.WorkingDirectory='!TARGET!'; $s.Save()"
    echo   Fertig. Starte Anwendung...
    start "" "!TARGET!\ExIptv.exe"
    goto :done
)

REM --- 3) Von GitHub laden ---
echo   Kein lokales Paket gefunden.
echo   Lade neueste Version von GitHub (%REPO%)...
echo.

set "DL=%TEMP%\EX-IPTV-Setup.exe"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "try {" ^
  "  $r = Invoke-RestMethod -Uri 'https://api.github.com/repos/%REPO%/releases/latest' -Headers @{ 'User-Agent'='exiptv-installer' };" ^
  "  $asset = $r.assets | Where-Object { $_.name -like '*Setup*.exe' } | Select-Object -First 1;" ^
  "  if (-not $asset) { Write-Host '   Kein Setup im neuesten Release gefunden.'; exit 2 }" ^
  "  Write-Host ('   Lade: ' + $asset.name);" ^
  "  Invoke-WebRequest -Uri $asset.browser_download_url -OutFile '%DL%' -Headers @{ 'User-Agent'='exiptv-installer' };" ^
  "  exit 0" ^
  "} catch { Write-Host ('   Fehler: ' + $_.Exception.Message); exit 1 }"

if !errorlevel! equ 0 (
    echo   Download abgeschlossen. Starte Installation...
    start "" "%DL%"
) else (
    echo.
    echo   Automatischer Download fehlgeschlagen.
    echo   Bitte das Setup manuell herunterladen:
    echo     https://github.com/%REPO%/releases/latest
    echo.
    echo   ...oder die Setup-EXE / portable ZIP neben diese Datei legen
    echo   und erneut ausfuehren.
)

:done
echo.
pause
endlocal
