using System.Windows;
using System.Windows.Media;
using ExIptv.Models;
using Serilog;

namespace ExIptv.Services.Settings;

/// <summary>
/// Wendet Hintergrund- und Schriftfarben zur Laufzeit an, indem die Color der im
/// ResourceDictionary registrierten SolidColorBrushes mutiert wird (nicht-frozen).
/// So schlagen Änderungen ohne Neustart durch, ohne alle Bindings auf DynamicResource umzustellen.
/// </summary>
public static class ThemeManager
{
    public sealed record BgTheme(string Key, string Name, Color Background);
    public sealed record TextTheme(string Key, string Name, Color Foreground);

    // 20 dunkle Hintergrund-Themes (unterschiedliche Farbstiche, alle kontraststark)
    public static readonly IReadOnlyList<BgTheme> Backgrounds = new List<BgTheme>
    {
        new("midnight",     "Mitternacht",   Hex("#13161C")),
        new("carbon",       "Carbon",        Hex("#121212")),
        new("graphite",     "Graphit",       Hex("#1A1D21")),
        new("navy",         "Marineblau",    Hex("#0E1726")),
        new("ocean",        "Ozean",         Hex("#0A1929")),
        new("forest",       "Wald",          Hex("#0F1A14")),
        new("wine",         "Wein",          Hex("#1A0F14")),
        new("plum",         "Pflaume",       Hex("#160E1C")),
        new("espresso",     "Espresso",      Hex("#1A1410")),
        new("slate",        "Schiefer",      Hex("#171A1F")),
        new("steel",        "Stahl",         Hex("#141A22")),
        new("teal",         "Petrol",        Hex("#0A1A1A")),
        new("indigo",       "Indigo",        Hex("#12122A")),
        new("crimson",      "Purpur",        Hex("#1C0E12")),
        new("moss",         "Moos",          Hex("#141A10")),
        new("charcoal",     "Holzkohle",     Hex("#0D0D0F")),
        new("deeppurple",   "Tiefviolett",   Hex("#140A24")),
        new("bronze",       "Bronze",        Hex("#1B160C")),
        new("nightblue",    "Nachtblau",     Hex("#0C1220")),
        new("obsidian",     "Obsidian",      Hex("#101014")),
    };

    // 7 Schriftfarben (hell, auf dunklem Grund gut lesbar)
    public static readonly IReadOnlyList<TextTheme> TextColors = new List<TextTheme>
    {
        new("default",   "Standard",   Hex("#E8EBF0")),
        new("white",     "Weiß",       Hex("#FFFFFF")),
        new("silver",    "Silber",     Hex("#C7CDD6")),
        new("amber",     "Bernstein",  Hex("#F2C94C")),
        new("mint",      "Minze",      Hex("#6FCF97")),
        new("sky",       "Himmelblau", Hex("#56CCF2")),
        new("rose",      "Rosé",       Hex("#F2A0C4")),
    };

    public static void Apply(AppSettings s)
    {
        var bg = Backgrounds.FirstOrDefault(b => b.Key == s.BackgroundThemeKey) ?? Backgrounds[0];
        var text = TextColors.FirstOrDefault(t => t.Key == s.TextColorKey) ?? TextColors[0];

        // Panel-/Rahmen-Töne aus der Hintergrundfarbe ableiten (leichtes Aufhellen)
        SetBrush("BgBrush", bg.Background);
        SetBrush("PanelBrush", Lighten(bg.Background, 0.05));
        SetBrush("PanelAltBrush", Lighten(bg.Background, 0.10));
        SetBrush("BorderBrush", Lighten(bg.Background, 0.14));

        // Schriftfarben
        SetBrush("TextBrush", text.Foreground);
        SetBrush("TextDimBrush", Dim(text.Foreground, 0.55, bg.Background));

        Log.Information("Theme angewendet: BG={Bg}, Text={Text}", bg.Key, text.Key);
    }

    private static void SetBrush(string key, Color color)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = color;
        else if (Application.Current is not null)
            Application.Current.Resources[key] = new SolidColorBrush(color); // Fallback
    }

    private static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromRgb(
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    private static Color Lighten(Color c, double amount)
    {
        byte L(byte v) => (byte)Math.Clamp(v + 255 * amount, 0, 255);
        return Color.FromRgb(L(c.R), L(c.G), L(c.B));
    }

    // Gedimmte Variante der Textfarbe in Richtung Hintergrund (für Sekundärtext)
    private static Color Dim(Color text, double factor, Color bg)
    {
        byte M(byte t, byte b) => (byte)Math.Clamp(b + (t - b) * factor, 0, 255);
        return Color.FromRgb(M(text.R, bg.R), M(text.G, bg.G), M(text.B, bg.B));
    }
}
