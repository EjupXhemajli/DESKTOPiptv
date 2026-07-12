using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ExIptv.Models;
using ExIptv.Services.Player;
using ExIptv.ViewModels;

namespace ExIptv.Views;

public partial class MainWindow : Window
{
    private readonly VlcPlayerService _player;

    // Auto-Hide der Steuerleiste im Vollbild: Mausbewegung über dem nativen VideoView erreicht
    // WPF nicht (Airspace). Deshalb wird die Cursorposition per Win32 gepollt.
    private readonly DispatcherTimer _cursorTimer;
    private POINT _lastCursor;
    private DateTime _lastCursorMove = DateTime.UtcNow;

    // Vom Nutzer gewählte Spaltenbreiten (per GridSplitter), Standard beim Start.
    private GridLength _catWidth = new(240);
    private GridLength _contentWidth = new(340);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    public MainWindow(MainViewModel viewModel, VlcPlayerService player)
    {
        InitializeComponent();
        DataContext = viewModel;
        _player = player;
        // Subscription im Konstruktor: kann so nicht durch einen frühen Abbruch in OnLoaded ausfallen.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _cursorTimer.Tick += OnCursorTimerTick;
        _cursorTimer.Start();

        Loaded += OnLoaded;
    }

    private void OnCursorTimerTick(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsFullscreen) return;

        GetCursorPos(out var p);
        var moved = Math.Abs(p.X - _lastCursor.X) > 2 || Math.Abs(p.Y - _lastCursor.Y) > 2;
        if (moved)
        {
            _lastCursor = p;
            _lastCursorMove = DateTime.UtcNow;
            if (PlayerBar.Visibility != Visibility.Visible) PlayerBar.Visibility = Visibility.Visible;
        }
        else if (PlayerBar.Visibility == Visibility.Visible
                 && (DateTime.UtcNow - _lastCursorMove).TotalSeconds > 3)
        {
            PlayerBar.Visibility = Visibility.Collapsed;   // nach 3 s Ruhe ausblenden
        }
    }

    // Live: Einzelklick (Auswahl) schaltet sofort um -> schnelles Zappen ohne Doppelklick.
    private void OnLiveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm
            && vm.CurrentSection == ContentType.Live
            && sender is ListBox { SelectedItem: PlayableItem item })
        {
            vm.ActivateItemCommand.Execute(item);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // LibVLC muss initialisiert sein, bevor der MediaPlayer an das VideoView gebunden wird.
        _player.Initialize();
        VideoView.MediaPlayer = _player.Player;
        _player.SetVolume(((MainViewModel)DataContext).Volume);
        ApplyLayout();
    }

    // Layout hängt an zwei Zuständen: Vollbild und Sektion (Live vs. Filme/Serien).
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsFullscreen) or nameof(MainViewModel.CurrentSection))
            ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (DataContext is not MainViewModel vm) return;
        var full = vm.IsFullscreen;
        var grid = vm.CurrentSection != ContentType.Live;   // Filme/Serien = Poster-Grid

        // Inhaltsdarstellung: Liste (Live) vs. Poster-Grid (Filme/Serien) – direkt gesetzt,
        // damit die Umschaltung nicht von Binding-Benachrichtigungen abhängt.
        LiveList.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        PosterGrid.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        SortBox.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;

        if (full)
        {
            ToolbarBar.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            PlayerBar.Visibility = Visibility.Collapsed;
            SplitCat.Visibility = Visibility.Collapsed;
            SplitContent.Visibility = Visibility.Collapsed;
            CatCol.Width = new GridLength(0);
            ContentCol.Width = new GridLength(0);
            PlayerCol.Width = new GridLength(1, GridUnitType.Star);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            // Randloses Vollbild: bei WindowStyle=None deckt Maximized den gesamten Bildschirm.
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            ToolbarBar.Visibility = Visibility.Visible;
            StatusBar.Visibility = Visibility.Visible;
            PlayerBar.Visibility = Visibility.Visible;
            SplitCat.Visibility = Visibility.Visible;                 // Kategorienbreite immer verstellbar
            CatCol.Width = _catWidth;                                 // vom Nutzer gewählte Breite

            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;

            if (grid)
            {
                // Filme/Serien: Poster-Grid füllt, keine Player-Spalte -> Content-Splitter sinnlos.
                ContentCol.Width = new GridLength(1, GridUnitType.Star);
                PlayerCol.Width = new GridLength(0);
                SplitContent.Visibility = Visibility.Collapsed;
            }
            else
            {
                ContentCol.Width = _contentWidth;
                PlayerCol.Width = new GridLength(1, GridUnitType.Star);
                SplitContent.Visibility = Visibility.Visible;         // Liste/Player-Grenze verstellbar
            }
        }
    }

    // Vom Nutzer gezogene Spaltenbreiten merken, damit ApplyLayout sie nicht zurücksetzt.
    private void OnSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (CatCol.Width.IsAbsolute && CatCol.Width.Value > 80) _catWidth = CatCol.Width;
        if (ContentCol.Width.IsAbsolute && ContentCol.Width.Value > 120) _contentWidth = ContentCol.Width;
    }

    private void OnToggleFullscreen(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsFullscreen = !vm.IsFullscreen;
            ApplyLayout();
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        switch (e.Key)
        {
            case Key.F11:
                vm.IsFullscreen = !vm.IsFullscreen;
                ApplyLayout();
                e.Handled = true;
                break;
            case Key.Escape:
                if (vm.IsFullscreen) { vm.IsFullscreen = false; ApplyLayout(); e.Handled = true; }
                break;
            case Key.Space:
                // Nicht auslösen, während im Suchfeld getippt wird.
                if (Keyboard.FocusedElement is not TextBox)
                {
                    vm.TogglePauseCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }

    // Doppelklick-Handler: InputBindings erben keinen DataContext, daher hier per Event
    // das ViewModel-Command mit dem geklickten Element aufrufen.
    private void OnContentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is ListBox { SelectedItem: PlayableItem item })
        {
            vm.ActivateItemCommand.Execute(item);
            ApplyLayout();   // Film -> Vollbild wird im VM gesetzt; Layout sofort anwenden
        }
    }

    private void OnEpisodeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is ListBox { SelectedItem: Episode ep })
        {
            vm.PlayEpisodeCommand.Execute(ep);
            ApplyLayout();
        }
    }

    // Seekbar: Während des Ziehens nur Vorschau, genau ein Seek beim Loslassen.
    private void OnSeekDragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.BeginSeek();
    }

    private void OnSeekDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.EndSeek();
    }

    // Vor dem Aufklappen die aktuellen Audio-/Untertitelspuren neu einlesen.
    private void OnTracksDropDownOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.RefreshTracksCommand.Execute(null);
    }
}
