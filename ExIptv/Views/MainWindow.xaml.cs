using System.Windows;
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
}
