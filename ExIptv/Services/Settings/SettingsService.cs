using System.IO;
using System.Text.Json;
using ExIptv.Models;
using Serilog;

namespace ExIptv.Services.Settings;

/// <summary>Lädt und speichert die AppSettings als JSON. Robust gegen defekte Dateien.</summary>
public sealed class SettingsService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EX-IPTV");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Einstellungen konnten nicht geladen werden – Standardwerte werden verwendet.");
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOpts));
            Log.Information("Einstellungen gespeichert.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Einstellungen konnten nicht gespeichert werden.");
        }
    }
}
