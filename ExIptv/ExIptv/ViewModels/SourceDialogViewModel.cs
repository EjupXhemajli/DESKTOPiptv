using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExIptv.Models;
using ExIptv.Services.Playlist;
using ExIptv.Services.Xtream;

namespace ExIptv.ViewModels;

/// <summary>
/// ViewModel für den Dialog "Quelle hinzufügen".
/// Unterstützt zwei Eingabemodi: (1) freies Feld (URL/Pfad) mit Auto-Erkennung,
/// (2) explizite Xtream-Felder (Host/User/Pass).
/// </summary>
public sealed partial class SourceDialogViewModel : ObservableObject
{
    private readonly XtreamClient _xtream;

    public SourceDialogViewModel(XtreamClient xtream) => _xtream = xtream;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _useXtreamFields;

    // Freies Feld
    [ObservableProperty] private string _urlOrPath = "";

    // Xtream-Felder
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    /// <summary>Ergebnis nach OK. Null, solange nicht bestätigt/gültig.</summary>
    public PlaylistSource? Result { get; private set; }

    [RelayCommand]
    private async Task TestAsync()
    {
        var src = BuildSource();
        if (src is null) { StatusText = "Bitte Eingaben prüfen."; return; }
        if (src.Type != SourceType.Xtream)
        {
            StatusText = "Verbindungstest nur für Xtream verfügbar. M3U/Datei wird beim Import geprüft.";
            return;
        }

        IsBusy = true;
        StatusText = "Teste Verbindung…";
        var (ok, error) = await _xtream.TestConnectionAsync(src);
        IsBusy = false;
        StatusText = ok ? "Verbindung erfolgreich." : $"Fehlgeschlagen: {error}";
    }

    [RelayCommand]
    private void Ok(Window window)
    {
        var src = BuildSource();
        if (src is null)
        {
            StatusText = "Eingaben unvollständig oder nicht erkennbar.";
            return;
        }
        Result = src;
        window.DialogResult = true;
        window.Close();
    }

    [RelayCommand]
    private void Cancel(Window window)
    {
        window.DialogResult = false;
        window.Close();
    }

    private PlaylistSource? BuildSource()
    {
        if (UseXtreamFields)
        {
            if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                return null;
            var host = Host.Trim();
            if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                host = "http://" + host;
            return new PlaylistSource
            {
                Name = string.IsNullOrWhiteSpace(Name) ? new Uri(host).Host : Name.Trim(),
                Type = SourceType.Xtream,
                Host = host.TrimEnd('/'),
                Username = Username.Trim(),
                Password = Password.Trim()
            };
        }

        if (string.IsNullOrWhiteSpace(UrlOrPath)) return null;
        var detection = PlaylistDetector.Detect(UrlOrPath, string.IsNullOrWhiteSpace(Name) ? null : Name.Trim());
        StatusText = detection.Message;
        return detection.Source;
    }
}
