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
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // LibVLC muss initialisiert sein, bevor der MediaPlayer an das VideoView gebunden wird.
        _player.Initialize();
        VideoView.MediaPlayer = _player.Player;
        _player.SetVolume(((MainViewModel)DataContext).Volume);
    }

    // Doppelklick-Handler: InputBindings erben keinen DataContext, daher hier per Event
    // das ViewModel-Command mit dem geklickten Element aufrufen.
    private void OnContentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is ListBox { SelectedItem: PlayableItem item })
            vm.ActivateItemCommand.Execute(item);
    }

    private void OnEpisodeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is ListBox { SelectedItem: Episode ep })
            vm.PlayEpisodeCommand.Execute(ep);
    }

    // Vor dem Aufklappen die aktuellen Audio-/Untertitelspuren neu einlesen.
    private void OnTracksDropDownOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.RefreshTracksCommand.Execute(null);
    }
}
