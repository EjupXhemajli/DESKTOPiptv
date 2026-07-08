using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExIptv.Models;
using ExIptv.Services.Data;
using ExIptv.Services.Player;
using ExIptv.Services.Playlist;
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
    private readonly IServiceProvider _services;

    private CancellationTokenSource? _searchCts;

    public MainViewModel(IptvRepository repo, PlaylistImportService importer,
        XtreamClient xtream, VlcPlayerService player, IServiceProvider services)
    {
        _repo = repo;
        _importer = importer;
        _xtream = xtream;
        _player = player;
        _services = services;

        _player.StatusChanged += s => PlayerStatus = s;
        _player.PlayingChanged += p => IsPlaying = p;

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

    partial void OnCurrentSectionChanged(ContentType value)
    {
        ShowEpisodes = false;
        _ = LoadSectionAsync();
    }

    [RelayCommand] private void ShowLive() => CurrentSection = ContentType.Live;
    [RelayCommand] private void ShowMovies() => CurrentSection = ContentType.Movie;
    [RelayCommand] private void ShowSeries() => CurrentSection = ContentType.Series;

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

    partial void OnVolumeChanged(int value) => _player.SetVolume(value);

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
                        foreach (var m in _repo.GetMovies(sourceId, catId, search))
                            list.Add(PlayableItem.FromMovie(m));
                        break;
                    case ContentType.Series:
                        foreach (var s in _repo.GetSeries(sourceId, catId, search))
                            list.Add(PlayableItem.FromSeries(s));
                        break;
                }
                return list;
            });

            if (gen != _loadGeneration) return; // ein neuerer Ladevorgang hat übernommen

            Items.Clear();
            foreach (var it in loaded) Items.Add(it);
            StatusText = $"{Items.Count} Einträge";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fehler beim Laden der Inhalte");
            StatusText = "Fehler beim Laden der Inhalte.";
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
