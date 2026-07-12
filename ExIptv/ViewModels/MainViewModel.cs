using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExIptv.Models;
using ExIptv.Services.Data;
using ExIptv.Services.Player;
using ExIptv.Services.Playlist;
using ExIptv.Services.Settings;
using ExIptv.Services.Xtream;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ExIptv.ViewModels;

/// <summary>
/// Zentrales ViewModel des Hauptfensters. Steuert Quellenauswahl, Sektion (Live/Filme/Serien),
/// Kategorien, Inhaltsliste, Suche, Serien-Episoden und Wiedergabe.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IptvRepository _repo;
    private readonly PlaylistImportService _importer;
    private readonly XtreamClient _xtream;
    private readonly VlcPlayerService _player;
    private readonly SettingsService _settings;
    private readonly IServiceProvider _services;

    private CancellationTokenSource? _searchCts;
    private bool _suppressSeek;

    public MainViewModel(IptvRepository repo, PlaylistImportService importer,
        XtreamClient xtream, VlcPlayerService player, SettingsService settings, IServiceProvider services)
    {
        _repo = repo;
        _importer = importer;
        _xtream = xtream;
        _player = player;
        _settings = settings;
        _services = services;

        _player.StatusChanged += s => PlayerStatus = s;
        _player.PlayingChanged += OnPlayingChanged;
        _player.TimeChanged += OnPlayerTimeChanged;
        _player.MediaReady += OnMediaReady;

        _volume = settings.Current.Volume;
        _selectedAspectRatio = settings.Current.AspectRatio;

        LoadSources();
    }

    // ---------- Quellen ----------

    public ObservableCollection<PlaylistSource> Sources { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource))]
    private PlaylistSource? _selectedSource;

    public bool HasSource => SelectedSource is not null;

    partial void OnSelectedSourceChanged(PlaylistSource? value)
    {
        if (value is not null) _ = LoadSectionAsync();
    }

    private void LoadSources()
    {
        Sources.Clear();
        foreach (var s in _repo.GetSources()) Sources.Add(s);
        SelectedSource ??= Sources.FirstOrDefault();
    }

    // ---------- Sektion (Live / Filme / Serien) ----------

    [ObservableProperty] private ContentType _currentSection = ContentType.Live;

    // Live -> Listenansicht mit Player rechts; Filme/Serien -> Poster-Grid, Player-Spalte aus
    public bool IsLiveSection => CurrentSection == ContentType.Live;
    public bool IsVodSection => CurrentSection != ContentType.Live;

    partial void OnCurrentSectionChanged(ContentType value)
    {
        ShowEpisodes = false;
        OnPropertyChanged(nameof(IsLiveSection));
        OnPropertyChanged(nameof(IsVodSection));
        _ = LoadSectionAsync();
    }

    [RelayCommand] private void ShowLive() => CurrentSection = ContentType.Live;
    [RelayCommand] private void ShowMovies() => CurrentSection = ContentType.Movie;
    [RelayCommand] private void ShowSeries() => CurrentSection = ContentType.Series;

    // ---------- Vollbild ----------

    [ObservableProperty] private bool _isFullscreen;
    [RelayCommand] private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

    partial void OnIsFullscreenChanged(bool value)
    {
        // Im Poster-Modus (Filme/Serien) gibt es keine sichtbare Player-Spalte. Wird das Vollbild
        // dort verlassen, würde die Wiedergabe unsichtbar weiterlaufen -> daher beenden.
        if (!value && CurrentSection != ContentType.Live)
        {
            _player.Stop();
            NowPlaying = "";
        }
    }

    // ---------- Sortierung (Filme/Serien) ----------

    public IReadOnlyList<Choice> SortModes { get; } = new[]
    {
        new Choice("A–Z", "name"),
        new Choice("Zuletzt hinzugefügt", "recent"),
        new Choice("Best bewertet", "rating"),
    };
    [ObservableProperty] private string _selectedSortMode = "name";

    partial void OnSelectedSortModeChanged(string value) => _ = LoadItemsAsync();

    // ---------- Kategorien ----------

    public ObservableCollection<Category> Categories { get; } = new();

    [ObservableProperty] private Category? _selectedCategory;

    partial void OnSelectedCategoryChanged(Category? value)
    {
        ShowEpisodes = false;
        _ = LoadItemsAsync();
    }

    // ---------- Inhaltsliste ----------

    public ObservableCollection<PlayableItem> Items { get; } = new();

    [ObservableProperty] private PlayableItem? _selectedItem;

    [ObservableProperty] private string _searchText = "";

    partial void OnSearchTextChanged(string value) => DebouncedSearch();

    // ---------- Serien-Episoden ----------

    public ObservableCollection<Episode> Episodes { get; } = new();
    [ObservableProperty] private bool _showEpisodes;
    [ObservableProperty] private string _episodesTitle = "";

    // ---------- Status / Player ----------

    [ObservableProperty] private string _statusText = "Bereit.";
    [ObservableProperty] private string _playerStatus = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private int _volume = 90;
    [ObservableProperty] private string _nowPlaying = "";

    partial void OnVolumeChanged(int value)
    {
        _player.SetVolume(value);
        _settings.Current.Volume = value;
    }

    // ---------- Player-Overlay: Seek, Zeit, Bildformat, Spuren ----------

    [ObservableProperty] private double _playbackPosition;   // 0..1
    [ObservableProperty] private string _playbackTimeText = "00:00";
    [ObservableProperty] private string _playbackLengthText = "00:00";
    [ObservableProperty] private bool _canSeek;              // nur VOD/Serie & seekbar

    public IReadOnlyList<string> AspectRatios { get; } = new[] { "Auto", "16:9", "4:3", "16:10", "Füllen" };
    [ObservableProperty] private string _selectedAspectRatio = "Auto";

    public ObservableCollection<TrackInfo> AudioTracks { get; } = new();
    public ObservableCollection<TrackInfo> SubtitleTracks { get; } = new();
    [ObservableProperty] private TrackInfo? _selectedAudioTrack;
    [ObservableProperty] private TrackInfo? _selectedSubtitleTrack;

    private bool _isUserSeeking;   // Nutzer zieht gerade den Seek-Regler

    /// <summary>Vom Code-Behind beim Anfassen des Seek-Reglers gerufen.</summary>
    public void BeginSeek() => _isUserSeeking = true;

    /// <summary>Beim Loslassen: genau ein Seek zur gewählten Position.</summary>
    public void EndSeek()
    {
        _isUserSeeking = false;
        _player.SeekTo(PlaybackPosition);
    }

    partial void OnPlaybackPositionChanged(double value)
    {
        if (_suppressSeek) return;   // vom Player gepusht -> nicht zurückspulen
        if (_isUserSeeking)
        {
            // Nur Zeit-Vorschau während des Ziehens – der eigentliche Seek kommt beim Loslassen.
            var len = _player.LengthMs;
            if (len > 0) PlaybackTimeText = FormatMs((long)(value * len));
            return;
        }
        // Direkter Klick auf die Spur (kein Drag): einmaliger Sofort-Seek.
        _player.SeekTo(value);
    }

    partial void OnSelectedAspectRatioChanged(string value)
    {
        _player.SetAspectRatio(value);
        _settings.Current.AspectRatio = value;
    }

    partial void OnSelectedAudioTrackChanged(TrackInfo? value)
    {
        if (value is { } t) _player.SetAudioTrack(t.Id);
    }

    partial void OnSelectedSubtitleTrackChanged(TrackInfo? value)
    {
        if (value is { } t) _player.SetSubtitleTrack(t.Id);
    }

    private void OnPlayingChanged(bool playing)
    {
        IsPlaying = playing;
        if (playing) _player.SetAspectRatio(SelectedAspectRatio);
    }

    private void OnPlayerTimeChanged(long timeMs, long lengthMs)
    {
        // Während der Nutzer zieht, den Regler nicht vom Player überschreiben lassen –
        // sonst "kämpft" der Slider gegen die Maus.
        if (!_isUserSeeking)
        {
            _suppressSeek = true;
            PlaybackPosition = lengthMs > 0 ? Math.Clamp((double)timeMs / lengthMs, 0, 1) : 0;
            _suppressSeek = false;
            PlaybackTimeText = FormatMs(timeMs);
        }
        PlaybackLengthText = FormatMs(lengthMs);
        CanSeek = _player.IsSeekable && lengthMs > 0 && CurrentSection != ContentType.Live;
    }

    private void OnMediaReady() => RefreshTracks();

    /// <summary>Lädt die verfügbaren Audio-/Untertitelspuren des aktuellen Mediums neu.</summary>
    [RelayCommand]
    private void RefreshTracks()
    {
        AudioTracks.Clear();
        foreach (var t in _player.GetAudioTracks()) AudioTracks.Add(t);
        SubtitleTracks.Clear();
        SubtitleTracks.Add(new TrackInfo(-1, "Aus"));
        foreach (var t in _player.GetSubtitleTracks()) SubtitleTracks.Add(t);

        var curA = _player.CurrentAudioTrack;
        _selectedAudioTrack = AudioTracks.Cast<TrackInfo?>().FirstOrDefault(t => t!.Value.Id == curA);
        OnPropertyChanged(nameof(SelectedAudioTrack));
        var curS = _player.CurrentSubtitleTrack;
        _selectedSubtitleTrack = SubtitleTracks.Cast<TrackInfo?>().FirstOrDefault(t => t!.Value.Id == curS);
        OnPropertyChanged(nameof(SelectedSubtitleTrack));
    }

    [RelayCommand] private void SeekForward() => _player.SeekRelative(15);
    [RelayCommand] private void SeekBackward() => _player.SeekRelative(-15);

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = _services.GetRequiredService<SettingsViewModel>();
        var dialog = new Views.SettingsDialog { DataContext = vm, Owner = Application.Current.MainWindow };
        dialog.ShowDialog();

        // Änderungen anwenden: Theme sofort, Player-Optionen beim nächsten Start.
        ThemeManager.Apply(_settings.Current);
        Volume = _settings.Current.Volume;
        _ = LoadSectionAsync(); // falls sich Quellen/Filter geändert haben
    }

    private static string FormatMs(long ms)
    {
        if (ms <= 0) return "00:00";
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    // ---------- Laden ----------

    private async Task LoadSectionAsync()
    {
        if (SelectedSource is null) return;
        try
        {
            var cats = await Task.Run(() => _repo.GetCategories(SelectedSource.Id, CurrentSection));
            Categories.Clear();
            Categories.Add(new Category { ExternalId = "__all__", Name = "Alle", ContentType = CurrentSection });
            foreach (var c in cats) Categories.Add(c);
            SelectedCategory = Categories.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fehler beim Laden der Kategorien");
            StatusText = "Fehler beim Laden der Kategorien.";
        }
    }

    private int _loadGeneration;

    /// <summary>
    /// Lädt die Inhaltsliste. Die DB-Abfrage läuft im Hintergrund-Thread (kein UI-Freeze bei
    /// großen Katalogen); die ObservableCollection wird anschließend auf dem UI-Thread befüllt.
    /// Ein Generationszähler verwirft veraltete Ergebnisse bei schnellen Wechseln.
    /// </summary>
    private async Task LoadItemsAsync()
    {
        if (SelectedSource is null || SelectedCategory is null) { Items.Clear(); return; }

        var gen = ++_loadGeneration;
        var sourceId = SelectedSource.Id;
        var section = CurrentSection;
        var catId = SelectedCategory.ExternalId == "__all__" ? null : SelectedCategory.ExternalId;
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var sort = SelectedSortMode;

        try
        {
            var loaded = await Task.Run(() =>
            {
                var list = new List<PlayableItem>();
                switch (section)
                {
                    case ContentType.Live:
                        foreach (var c in _repo.GetLiveChannels(sourceId, catId, search))
                            list.Add(PlayableItem.FromLive(c));
                        break;
                    case ContentType.Movie:
                        foreach (var m in _repo.GetMovies(sourceId, catId, search, sort, 500))
                            list.Add(PlayableItem.FromMovie(m));
                        break;
                    case ContentType.Series:
                        foreach (var s in _repo.GetSeries(sourceId, catId, search, sort, 500))
                            list.Add(PlayableItem.FromSeries(s));
                        break;
                }
                return list;
            });

            if (gen != _loadGeneration) return; // ein neuerer Ladevorgang hat übernommen

            Items.Clear();
            foreach (var it in loaded) Items.Add(it);
            var vodLimited = section is ContentType.Movie or ContentType.Series && Items.Count >= 500;
            if (section is ContentType.Movie or ContentType.Series)
            {
                var withPoster = Items.Count(i => !string.IsNullOrEmpty(i.LogoUrl));
                // Diagnose: belegt im Log, ob und welche Bild-URLs in der DB liegen.
                var samples = Items.Where(i => !string.IsNullOrEmpty(i.LogoUrl)).Take(2).Select(i => i.LogoUrl).ToList();
                Log.Information("VOD geladen: {Count} Einträge, {WithPoster} mit Poster-URL. Beispiele: {Samples}",
                    Items.Count, withPoster, samples.Count > 0 ? string.Join(" | ", samples) : "(keine)");
                StatusText = vodLimited
                    ? $"{Items.Count} angezeigt ({withPoster} mit Poster) – Kategorie oder Suche eingrenzen"
                    : $"{Items.Count} Einträge · {withPoster} mit Poster";

                // Fehlende Poster über die Detail-API nachladen (viele Panels liefern in der
                // Listen-API keine Bilder). Gedrosselt; Ergebnisse werden in die DB geschrieben,
                // sodass sie beim nächsten Mal sofort da sind.
                var source = SelectedSource;
                if (source is { Type: SourceType.Xtream })
                {
                    var missing = Items.Where(i => string.IsNullOrEmpty(i.LogoUrl)).ToList();
                    if (missing.Count > 0) _ = EnrichPostersAsync(gen, source, missing, section);
                }
            }
            else
            {
                StatusText = $"{Items.Count} Einträge";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fehler beim Laden der Inhalte");
            StatusText = "Fehler beim Laden der Inhalte.";
        }
    }

    /// <summary>
    /// Lädt fehlende Poster einzeln über get_vod_info / get_series_info nach.
    /// Läuft gedrosselt (3 parallel), bricht bei Kategorienwechsel ab (Generationszähler)
    /// und schreibt gefundene URLs in die DB zurück.
    /// </summary>
    private async Task EnrichPostersAsync(int gen, PlaylistSource source, List<PlayableItem> missing, ContentType section)
    {
        var gate = new SemaphoreSlim(3);
        var found = 0;
        try
        {
            var tasks = missing.Select(async item =>
            {
                await gate.WaitAsync();
                try
                {
                    if (gen != _loadGeneration) return;
                    var url = section == ContentType.Movie
                        ? await _xtream.GetVodPosterAsync(source, item.ExternalId)
                        : await _xtream.GetSeriesPosterAsync(source, item.ExternalId);
                    if (url is null || gen != _loadGeneration) return;

                    item.LogoUrl = url;   // beobachtbar -> Kachel lädt das Bild sofort nach
                    Interlocked.Increment(ref found);
                    try
                    {
                        if (section == ContentType.Movie) _repo.UpdateVodPoster(item.DbId, url);
                        else _repo.UpdateSeriesPoster(item.DbId, url);
                    }
                    catch (Exception ex) { Log.Debug(ex, "Poster-Rückschreiben fehlgeschlagen"); }
                }
                catch (Exception ex) { Log.Debug(ex, "Poster-Nachladen fehlgeschlagen"); }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks);
        }
        finally { gate.Dispose(); }

        if (gen == _loadGeneration)
        {
            Log.Information("Poster-Nachladen: {Found} gefunden, {None} ohne Bild (von {Total})",
                found, missing.Count - found, missing.Count);
            if (found > 0)
                StatusText = $"{Items.Count} Einträge · {found} Poster nachgeladen";
            else
                StatusText = $"{Items.Count} Einträge · Panel liefert für diese Kategorie keine Poster";
        }
    }

    private void DebouncedSearch()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(300, token); }
            catch (TaskCanceledException) { return; }
            if (token.IsCancellationRequested) return;
            await Application.Current.Dispatcher.InvokeAsync(LoadItemsAsync);
        }, token);
    }

    // ---------- Aktionen ----------

    [RelayCommand]
    private async Task ActivateItemAsync(PlayableItem? item)
    {
        item ??= SelectedItem;
        if (item is null || SelectedSource is null) return;

        if (item.IsSeriesContainer)
        {
            await LoadEpisodesAsync(item);
            return;
        }

        NowPlaying = item.Name;
        _player.Play(item.StreamUrl, item.Type);
        StatusText = $"Spiele: {item.Name}";
        // Filme laufen aus dem Poster-Grid – ohne sichtbare Player-Spalte -> Vollbild
        if (item.Type == ContentType.Movie) IsFullscreen = true;
    }

    private async Task LoadEpisodesAsync(PlayableItem seriesItem)
    {
        if (SelectedSource is null) return;
        IsBusy = true;
        EpisodesTitle = seriesItem.Name;
        Episodes.Clear();
        try
        {
            var eps = await _xtream.GetSeriesEpisodesAsync(SelectedSource, seriesItem.ExternalId);
            foreach (var e in eps) Episodes.Add(e);
            ShowEpisodes = true;
            StatusText = $"{eps.Count} Episoden geladen";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fehler beim Laden der Episoden");
            StatusText = "Episoden konnten nicht geladen werden.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void PlayEpisode(Episode? episode)
    {
        if (episode is null) return;
        NowPlaying = $"{EpisodesTitle} – {episode.Display}";
        _player.Play(episode.StreamUrl, ContentType.Series);
        StatusText = $"Spiele: {episode.Display}";
        ShowEpisodes = false;
        IsFullscreen = true;
    }

    [RelayCommand] private void CloseEpisodes() => ShowEpisodes = false;

    [RelayCommand] private void TogglePause() => _player.TogglePause();

    [RelayCommand]
    private void StopPlayback()
    {
        _player.Stop();
        NowPlaying = "";
        StatusText = "Gestoppt.";
    }

    // ---------- Quellen-Verwaltung ----------

    [RelayCommand]
    private async Task AddSourceAsync()
    {
        var dialogVm = _services.GetRequiredService<SourceDialogViewModel>();
        var dialog = new Views.SourceDialog { DataContext = dialogVm, Owner = Application.Current.MainWindow };
        var ok = dialog.ShowDialog();
        if (ok != true || dialogVm.Result is null) return;

        var src = dialogVm.Result;
        var id = _repo.InsertSource(src);
        src.Id = id;
        Sources.Add(src);
        SelectedSource = src;

        await SyncSourceAsync(src);
    }

    [RelayCommand]
    private async Task RefreshSourceAsync()
    {
        if (SelectedSource is null) return;
        await SyncSourceAsync(SelectedSource);
    }

    [RelayCommand]
    private void DeleteSource()
    {
        if (SelectedSource is null) return;
        var result = MessageBox.Show(
            $"Quelle \"{SelectedSource.Name}\" wirklich löschen?",
            "Löschen bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var id = SelectedSource.Id;
        _repo.DeleteSource(id);
        Sources.Remove(SelectedSource);
        SelectedSource = Sources.FirstOrDefault();
        if (SelectedSource is null)
        {
            Categories.Clear();
            Items.Clear();
        }
    }

    private async Task SyncSourceAsync(PlaylistSource src)
    {
        IsBusy = true;
        var progress = new Progress<ImportProgress>(p => StatusText = p.Stage);
        try
        {
            await _importer.ImportAsync(src, progress);
            StatusText = "Import abgeschlossen.";
            await LoadSectionAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Import fehlgeschlagen");
            StatusText = $"Import fehlgeschlagen: {ex.Message}";
            MessageBox.Show(ex.Message, "Import-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }
}
