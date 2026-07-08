using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExIptv.Models;
using ExIptv.Services.Player;
using ExIptv.ViewModels;

namespace ExIptv.Views;

public partial class MainWindow : Window
{
    private readonly VlcPlayerService _player;

    public MainWindow(MainViewModel viewModel, VlcPlayerService player)
    {
        InitializeComponent();
        DataContext = viewModel;
        _player = player;
        // Subscription im Konstruktor: kann so nicht durch einen frühen Abbruch in OnLoaded ausfallen.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
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
            CatCol.Width = new GridLength(240);

            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;

            if (grid)
            {
                ContentCol.Width = new GridLength(1, GridUnitType.Star);
                PlayerCol.Width = new GridLength(0);
            }
            else
            {
                ContentCol.Width = new GridLength(340);
                PlayerCol.Width = new GridLength(1, GridUnitType.Star);
            }
        }
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

    // Vor dem Aufklappen die aktuellen Audio-/Untertitelspuren neu einlesen.
    private void OnTracksDropDownOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.RefreshTracksCommand.Execute(null);
    }
}
