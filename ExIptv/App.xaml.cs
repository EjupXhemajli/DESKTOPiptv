using System.Windows;
using System.Windows.Threading;
using ExIptv.Services.Data;
using ExIptv.Services.Player;
using ExIptv.Services.Playlist;
using ExIptv.Services.Settings;
using ExIptv.Services.Xtream;
using ExIptv.ViewModels;
using ExIptv.Views;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Serilog;

namespace ExIptv;

/// <summary>
/// Anwendungs-Einstiegspunkt. Baut den DI-Container, konfiguriert Logging und Netzwerk
/// (HttpClient mit Retry/Backoff) und öffnet das Hauptfenster.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EX-IPTV", "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(logDir, "exiptv-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== EX-IPTV Desktop startet ===");

        // globale Fehler abfangen, statt hart abzustürzen
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Nicht behandelte Ausnahme (AppDomain)");

        var sc = new ServiceCollection();
        ConfigureServices(sc);
        _services = sc.BuildServiceProvider();

        // DB-Schema anlegen
        _services.GetRequiredService<IptvDatabase>().Initialize();

        // Gespeichertes Farb-Theme anwenden, bevor das Fenster erscheint
        ThemeManager.Apply(_services.GetRequiredService<SettingsService>().Current);

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    private static void ConfigureServices(IServiceCollection sc)
    {
        // HttpClient mit robustem Retry (transiente Fehler + 429) und Timeout
        sc.AddHttpClient("iptv", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ExIptv/0.1 (Windows)");
        })
        .AddPolicyHandler(GetRetryPolicy());

        // Einstellungen (persistent) + Player
        sc.AddSingleton<SettingsService>();
        sc.AddSingleton(sp => new VlcPlayerService(
            sp.GetRequiredService<SettingsService>(),
            Application.Current.Dispatcher));

        // Datenzugriff
        sc.AddSingleton<IptvDatabase>();
        sc.AddSingleton<IptvRepository>();

        // IPTV-Dienste
        sc.AddSingleton<XtreamClient>();
        sc.AddSingleton<M3uParser>();
        sc.AddSingleton<PlaylistImportService>();

        // ViewModels
        sc.AddSingleton<MainViewModel>();
        sc.AddTransient<SourceDialogViewModel>();
        sc.AddTransient<SettingsViewModel>();

        // Views
        sc.AddSingleton<MainWindow>();
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt - 1)));

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Nicht behandelte UI-Ausnahme");
        MessageBox.Show(
            "Ein unerwarteter Fehler ist aufgetreten. Details wurden protokolliert.\n\n" + e.Exception.Message,
            "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // App nicht abstürzen lassen
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Laufzeit-Änderungen (Lautstärke, Bildformat) sichern
            _services?.GetService<SettingsService>()?.Save();
            _services?.GetService<VlcPlayerService>()?.Dispose();
            _services?.Dispose();
        }
        finally
        {
            Log.Information("=== EX-IPTV Desktop beendet ===");
            Log.CloseAndFlush();
        }
        base.OnExit(e);
    }
}
