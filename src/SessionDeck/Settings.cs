using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeSessions;

sealed class Settings
{
    public bool Topmost { get; set; }
    public int WindowHours { get; set; } = 24;
    public bool Notifications { get; set; } = true;
    public bool HideDone { get; set; }
    public bool ShowProjects { get; set; } = true;
    public List<string> HiddenProjects { get; set; } = new();
    public bool ShowHiddenProjects { get; set; }
    /// <summary>The token charts take the cards' place.</summary>
    public bool ShowTokens { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TokenChartKind TokenChart { get; set; } = TokenChartKind.Donut;
    /// <summary>Chart sessions instead of projects.</summary>
    public bool TokenSessions { get; set; }
    /// <summary>Count cache reads (the context re-read on every turn, by far the largest share).</summary>
    public bool TokenCacheReads { get; set; } = true;
    /// <summary>Project folder → the icon you picked (a copy in %AppData%\SessionDeck\icons), or "generated".</summary>
    public Dictionary<string, string> ProjectIcons { get; set; } = new();
    /// <summary>Session key → when you dismissed its question (kept for a week).</summary>
    public Dictionary<string, DateTime> DismissedQuestions { get; set; } = new();
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 980;
    public double Height { get; set; } = 760;
    public WidgetSettings Widget { get; set; } = new();
    /// <summary>Who lives in the banner, the widget and the icons.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MascotKind Mascot { get; set; } = MascotKind.Dib;
    /// <summary>The mascot also walks along the top of the taskbar on the main monitor.</summary>
    public bool MascotOnTaskbar { get; set; } = true;
    /// <summary>Where the weather is for; detected from your internet address and refreshed daily.</summary>
    public WeatherPlace? WeatherPlace { get; set; }
    /// <summary>Show temperatures in °F; the forecast itself always comes in °C.</summary>
    public bool Fahrenheit { get; set; }
    /// <summary>
    /// Best score in each game for each mascot, by "game:mascot". These started over when the runner got
    /// harder; its old, easier scores were under RunnerBest and are dropped.
    /// </summary>
    public Dictionary<string, int> GameBest { get; set; } = new();
    /// <summary>The games play the day's course (the same all day) instead of a new one each run.</summary>
    public bool RunnerDaily { get; set; }
    /// <summary>Best score on each game's daily course, any mascot, and the day (yyyy-MM-dd) they're for.</summary>
    public Dictionary<string, int> DailyBest { get; set; } = new();
    public string DailyDate { get; set; } = "";
    /// <summary>The game the game window opens on.</summary>
    public string Game { get; set; } = "runner";
    public int Version { get; set; }
    /// <summary>Made up for an off-screen render (--dashboard): never saved over your real settings.</summary>
    [JsonIgnore]
    public bool Transient { get; init; }

    /// <summary>%AppData%\SessionDeck. The app used to be called Claude Sessions, so its old folder is moved here once.</summary>
    public static string DataDir { get; } = PrepareDataDir();

    static string PrepareDataDir()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "SessionDeck"), old = Path.Combine(appData, "ClaudeSessions");
        try { if (Directory.Exists(old) && !Directory.Exists(dir)) Directory.Move(old, dir); }
        catch { }
        return dir;
    }

    static string FilePath => Path.Combine(DataDir, "settings.json");

    public static Settings Load()
    {
        Settings s;
        try { s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { s = new(); }
        if (s.Version < 2)
        {
            // v1 was a narrow single column; widen once for the grid layout
            s.Width = Math.Max(s.Width, 980);
            s.Version = 2;
        }
        if (s.Version < 3)
        {
            // v3 merged in the standalone ClaudeUsage widget; keep where you had put it
            s.Widget = WidgetSettings.FromStandalone() ?? s.Widget;
            s.Version = 3;
        }
        // The goat took the sheep's place in the mascot list
        if (s.Mascot == MascotKind.Sheep) s.Mascot = MascotKind.Goat;
        return s;
    }

    public void Save()
    {
        if (Transient) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

sealed class WidgetSettings
{
    public bool Visible { get; set; } = true;
    public bool Topmost { get; set; } = true;
    public bool Compact { get; set; }
    public double Scale { get; set; } = 1;
    public double? Left { get; set; }
    public double? Top { get; set; }

    /// <summary>The settings of the old standalone ClaudeUsage app, if it was ever run.</summary>
    public static WidgetSettings? FromStandalone()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsage", "settings.json");
            return JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }
}
