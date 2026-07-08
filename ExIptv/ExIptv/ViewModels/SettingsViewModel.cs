using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExIptv.Models;
using ExIptv.Services.Settings;

namespace ExIptv.ViewModels;

/// <summary>Auswahloption mit deutschem Label und zugrundeliegendem Wert (für ComboBoxen).</summary>
public sealed record Choice(string Label, object Value);

/// <summary>Farbfeld für die Theme-Auswahl.</summary>
public sealed record Swatch(string Key, string Name, Brush Brush);

/// <summary>
/// ViewModel für den Einstellungsdialog. Liest beim Öffnen die aktuellen Werte, bietet
/// Live-Vorschau der Farben und schreibt beim Speichern zurück in den SettingsService.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly string _origBg;
    private readonly string _origText;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        var s = settings.Current;
        _origBg = s.BackgroundThemeKey;
        _origText = s.TextColorKey;

        // Werte übernehmen
        _networkCachingMs = s.NetworkCachingMs;
        _liveCachingMs = s.LiveCachingMs;
        _autoReconnect = s.AutoReconnect;
        _maxReconnectAttempts = s.MaxReconnectAttempts;
        _httpReconnect = s.HttpReconnect;
        _fileCaching = s.FileCaching;
        _deinterlace = s.Deinterlace;
        _epgOffsetHours = s.EpgOffsetHours;
        _playbackMode = s.PlaybackMode;
        _imageQuality = s.ImageQuality;
        _frameRateMode = s.FrameRateMode;
        _liveProfile = s.LiveProfile;
        _movieProfile = s.MovieProfile;
        _seriesProfile = s.SeriesProfile;
        _selectedBackgroundKey = s.BackgroundThemeKey;
        _selectedTextKey = s.TextColorKey;

        Backgrounds = ThemeManager.Backgrounds
            .Select(b => new Swatch(b.Key, b.Name, new SolidColorBrush(b.Background))).ToList();
        TextColors = ThemeManager.TextColors
            .Select(t => new Swatch(t.Key, t.Name, new SolidColorBrush(t.Foreground))).ToList();
    }

    // --- Puffer ---
    [ObservableProperty] private int _networkCachingMs;
    [ObservableProperty] private int _liveCachingMs;

    // --- Stabilisierung ---
    [ObservableProperty] private bool _autoReconnect;
    [ObservableProperty] private int _maxReconnectAttempts;
    [ObservableProperty] private bool _httpReconnect;
    [ObservableProperty] private bool _fileCaching;

    // --- EPG ---
    [ObservableProperty] private int _epgOffsetHours;

    // --- Bild ---
    [ObservableProperty] private bool _deinterlace;
    [ObservableProperty] private PlaybackMode _playbackMode;
    [ObservableProperty] private ImageQuality _imageQuality;
    [ObservableProperty] private FrameRateMode _frameRateMode;

    // --- Player-Profile ---
    [ObservableProperty] private PlayerProfile _liveProfile;
    [ObservableProperty] private PlayerProfile _movieProfile;
    [ObservableProperty] private PlayerProfile _seriesProfile;

    // --- Farben ---
    public IReadOnlyList<Swatch> Backgrounds { get; }
    public IReadOnlyList<Swatch> TextColors { get; }
    [ObservableProperty] private string _selectedBackgroundKey;
    [ObservableProperty] private string _selectedTextKey;

    partial void OnSelectedBackgroundKeyChanged(string value) => Preview();
    partial void OnSelectedTextKeyChanged(string value) => Preview();

    // Auswahllisten für ComboBoxen
    public IReadOnlyList<Choice> PlaybackModes { get; } = new[]
    {
        new Choice("Automatisch", PlaybackMode.Auto),
        new Choice("Nie umwandeln – nur Direktwiedergabe", PlaybackMode.DirectOnly),
        new Choice("Immer umwandeln (max. Kompatibilität)", PlaybackMode.AlwaysTranscode),
    };
    public IReadOnlyList<Choice> ImageQualities { get; } = new[]
    {
        new Choice("Standard", ImageQuality.Standard),
        new Choice("Gute Qualität", ImageQuality.Good),
        new Choice("Brillant", ImageQuality.Brilliant),
    };
    public IReadOnlyList<Choice> FrameRates { get; } = new[]
    {
        new Choice("Standard", FrameRateMode.Standard),
        new Choice("Flüssig", FrameRateMode.Smooth),
    };
    public IReadOnlyList<Choice> Profiles { get; } = new[]
    {
        new Choice("Automatisch (empfohlen)", PlayerProfile.Auto),
        new Choice("Hardware – Direct3D 11", PlayerProfile.HardwareD3D11),
        new Choice("Hardware – DXVA2", PlayerProfile.HardwareDxva2),
        new Choice("Software", PlayerProfile.Software),
        new Choice("Kompatibilität", PlayerProfile.Compatibility),
    };

    private void Preview()
    {
        var probe = new AppSettings
        {
            BackgroundThemeKey = SelectedBackgroundKey,
            TextColorKey = SelectedTextKey
        };
        ThemeManager.Apply(probe);
    }

    [RelayCommand]
    private void Save(Window window)
    {
        var s = _settings.Current;
        s.NetworkCachingMs = NetworkCachingMs;
        s.LiveCachingMs = LiveCachingMs;
        s.AutoReconnect = AutoReconnect;
        s.MaxReconnectAttempts = MaxReconnectAttempts;
        s.HttpReconnect = HttpReconnect;
        s.FileCaching = FileCaching;
        s.EpgOffsetHours = EpgOffsetHours;
        s.Deinterlace = Deinterlace;
        s.PlaybackMode = PlaybackMode;
        s.ImageQuality = ImageQuality;
        s.FrameRateMode = FrameRateMode;
        s.LiveProfile = LiveProfile;
        s.MovieProfile = MovieProfile;
        s.SeriesProfile = SeriesProfile;
        s.BackgroundThemeKey = SelectedBackgroundKey;
        s.TextColorKey = SelectedTextKey;
        _settings.Save();
        window.DialogResult = true;
        window.Close();
    }

    [RelayCommand]
    private void Cancel(Window window)
    {
        // Farb-Vorschau zurücksetzen
        ThemeManager.Apply(new AppSettings { BackgroundThemeKey = _origBg, TextColorKey = _origText });
        window.DialogResult = false;
        window.Close();
    }
}
