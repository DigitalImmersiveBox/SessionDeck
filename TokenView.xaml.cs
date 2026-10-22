using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// The dashboard's token view: how many tokens each project or session used in the chosen
/// time range, as a donut, bars, a treemap or a timeline. Clicking a project shows its sessions;
/// clicking a session opens it.
/// </summary>
public partial class TokenView : UserControl
{
    // Categorical hues in fixed order, stepped for the dark surface
    // (validated: worst adjacent colour-blind ΔE 8.4, normal-vision ΔE 19.3, all ≥ 3:1 on #1D1F24)
    static readonly Brush[] Hues =
    {
        Ink.Of(0x39, 0x87, 0xE5), Ink.Of(0xD9, 0x59, 0x26), Ink.Of(0x19, 0x9E, 0x70), Ink.Of(0xC9, 0x85, 0x00),
        Ink.Of(0xD5, 0x51, 0x81), Ink.Of(0x00, 0x83, 0x00), Ink.Of(0x90, 0x85, 0xE9), Ink.Of(0xE6, 0x67, 0x67),
    };
    static readonly Brush OtherBrush = Ink.Of(0x55, 0x59, 0x62);
    static readonly Brush DarkInk = Ink.Of(0x11, 0x12, 0x15);
    static readonly Brush LightInk = Ink.Of(0xE6, 0xE7, 0xEA);

    Settings settings = new();
    bool persist, ready;
    TokenStats? stats;
    string? filterPath, filterName;
    Func<string, (string Name, string Path)> projectOf = cwd => (SessionScanner.ProjectName(cwd), cwd);
    /// <summary>Project or session → its colour slot, so a colour stays with its owner as others come and go.</summary>
    readonly Dictionary<string, int> slots = new();

    public event Action<string>? ProjectChosen;
    public event Action<TokenEntry>? SessionChosen;

    public TokenView()
    {
        InitializeComponent();
        Chart.HoverTextChanged += text => Readout.Text = text ?? Hint;
        Chart.SliceClicked += Choose;
    }

    /// <summary>Takes the chart choices from <paramref name="settings"/>; changes are saved back when <paramref name="save"/>.</summary>
    internal void Bind(Settings settings, bool save = true)
    {
        this.settings = settings;
        persist = save;
        ready = false;
        (settings.TokenChart switch
        {
            TokenChartKind.Bars => BarsChip,
            TokenChartKind.Treemap => TreemapChip,
            TokenChartKind.Timeline => TimelineChip,
            _ => DonutChip,
        }).IsChecked = true;
        (settings.TokenSessions ? SessionsChip : ProjectsChip).IsChecked = true;
        CacheChip.IsChecked = settings.TokenCacheReads;
        CacheMark.Text = settings.TokenCacheReads ? "" : "";
        ready = true;
        Render();
    }

    /// <param name="filterPath">The project picked in the side panel; the sessions chart shows only its sessions.</param>
    /// <param name="projectOf">Which project (name, folder) a session folder belongs to.</param>
    public void Show(TokenStats stats, string? filterPath, string? filterName, Func<string, (string Name, string Path)> projectOf)
    {
        this.stats = stats;
        this.filterPath = filterPath;
        this.filterName = filterName;
        this.projectOf = projectOf;
        Render();
    }

    /// <summary>The project a session folder belongs to: the deepest known project holding it, else its own folder.</summary>
    public static (string Name, string Path) ProjectOf(IReadOnlyList<ProjectEntry> projects, string cwd)
    {
        var known = projects.Where(p => ProjectItem.IsInside(cwd, p.Path)).OrderByDescending(p => p.Path.Length).FirstOrDefault();
        if (known != null) return (known.Name, known.Path);
        string name = SessionScanner.ProjectName(cwd);
        return name == "No folder" ? (name, "") : (name, cwd.TrimEnd('\\', '/'));
    }

    string Hint => settings.TokenSessions ? "Click a session to open it in Claude" : "Click a project to see its sessions";

    void Render()
    {
        if (!ready) return;
        bool bars = settings.TokenChart == TokenChartKind.Bars;
        LegendColumn.Width = bars ? new GridLength(0) : new GridLength(270);
        LegendScroller.Visibility = bars ? Visibility.Collapsed : Visibility.Visible;
        if (stats == null)
        {
            EmptyText.Text = "Counting tokens…";
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        bool cache = settings.TokenCacheReads, bySession = settings.TokenSessions;
        var entries = bySession && filterPath != null
            ? stats.Sessions.Where(s => ProjectItem.IsInside(s.Cwd, filterPath)).ToList()
            : stats.Sessions.ToList();
        var sum = entries.Aggregate(default(Tokens), (a, e) => a + e.Tokens);
        long total = sum.Count(cache);

        long[] Buckets(IEnumerable<TokenEntry> group)
        {
            var b = new long[stats.Sessions.FirstOrDefault()?.Buckets.Length ?? 0];
            foreach (var e in group)
                for (int i = 0; i < b.Length; i++) b[i] += e.Buckets[i].Count(cache);
            return b;
        }

        var all = bySession
            ? entries.Select(e => new TokenSlice("s:" + e.Key, e.Title, projectOf(e.Cwd).Name, e.Tokens.Count(cache), Buckets(new[] { e }), e))
                .ToList()
            : entries.GroupBy(e => projectOf(e.Cwd).Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TokenSlice("p:" + g.Key, projectOf(g.First().Cwd).Name, Count(g.Count(), "session"),
                    g.Sum(e => e.Tokens.Count(cache)), Buckets(g), g.Key.Length > 0 ? g.Key : null))
                .ToList();
        all = all.Where(s => s.Value > 0).OrderByDescending(s => s.Value).ToList();

        // Eight colours at most; the rest folds into "Other"
        const int max = 8;
        var slices = all.Take(max).ToList();
        if (all.Count > max)
        {
            var rest = all.Skip(max).ToList();
            var buckets = new long[slices[0].Buckets.Length];
            foreach (var s in rest)
                for (int i = 0; i < buckets.Length; i++) buckets[i] += s.Buckets[i];
            slices.Add(new TokenSlice("other", "Other", Count(rest.Count, bySession ? "more session" : "more project"),
                rest.Sum(s => s.Value), buckets, null));
        }
        Paint(slices);
        foreach (var s in slices) s.Share = total > 0 ? (double)s.Value / total : 0;

        string range = stats.Window.TotalHours <= 24 ? $"{stats.Window.TotalHours:0} H" : $"{stats.Window.TotalDays:0} DAYS";
        string scope = !bySession ? "ALL PROJECTS" : filterName != null ? filterName.ToUpperInvariant() : "ALL SESSIONS";
        Heading.Text = $"TOKENS · LAST {range} · {scope}";
        TotalText.Text = Tokens.Short(total);
        TotalUnit.Text = cache ? "tokens" : "tokens, without cache reads";
        Breakdown.Text = $"{Tokens.Short(sum.Input)} input · {Tokens.Short(sum.CacheWrite)} cache write · "
                         + $"{Tokens.Short(sum.CacheRead)} cache read{(cache ? "" : " (not counted)")} · "
                         + $"{Tokens.Short(sum.Output)} output · {Count(entries.Count, "session")}";

        EmptyText.Text = filterPath != null && bySession
            ? $"No tokens used in {filterName} in this time range."
            : "No tokens used in this time range.";
        EmptyText.Visibility = total > 0 ? Visibility.Collapsed : Visibility.Visible;
        Chart.Show(settings.TokenChart, slices, total, stats);
        Legend.ItemsSource = slices;
        if (Chart.Highlight == null) Readout.Text = Hint;
    }

    static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    // A slice keeps the colour it had while it stays on screen; newcomers take the lowest free one
    void Paint(List<TokenSlice> slices)
    {
        var taken = new HashSet<int>();
        var fresh = new List<TokenSlice>();
        foreach (var s in slices.Where(s => s.Key != "other"))
        {
            if (slots.TryGetValue(s.Key, out int slot) && taken.Add(slot)) continue;
            fresh.Add(s);
        }
        foreach (var s in fresh)
        {
            int slot = Enumerable.Range(0, Hues.Length).First(i => !taken.Contains(i));
            taken.Add(slot);
            slots[s.Key] = slot;
        }
        foreach (var s in slices)
        {
            bool other = s.Key == "other";
            s.Brush = other ? OtherBrush : Hues[slots[s.Key]];
            s.OnBrush = other ? LightInk : DarkInk;
        }
    }

    void Choose(TokenSlice slice)
    {
        if (slice.Tag is TokenEntry session)
        {
            SessionChosen?.Invoke(session);
        }
        else if (slice.Tag is string path)
        {
            ProjectChosen?.Invoke(path);
            SessionsChip.IsChecked = true;
        }
    }

    void Kind_Checked(object sender, RoutedEventArgs e)
    {
        if (!ready || sender is not RadioButton { Tag: TokenChartKind kind }) return;
        settings.TokenChart = kind;
        Changed();
    }

    void Scope_Checked(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.TokenSessions = SessionsChip.IsChecked == true;
        Changed();
    }

    void Cache_Changed(object sender, RoutedEventArgs e)
    {
        CacheMark.Text = CacheChip.IsChecked == true ? "" : "";
        if (!ready) return;
        settings.TokenCacheReads = CacheChip.IsChecked == true;
        Changed();
    }

    void Changed()
    {
        if (persist) settings.Save();
        Chart.Highlight = null;
        Render();
    }

    static TokenSlice? SliceOf(object sender) => (sender as FrameworkElement)?.DataContext as TokenSlice;

    void LegendRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (SliceOf(sender) is not { } slice) return;
        Chart.Highlight = slice;
        Readout.Text = Chart.Describe(slice, -1);
    }

    void LegendRow_MouseLeave(object sender, MouseEventArgs e)
    {
        Chart.Highlight = null;
        Readout.Text = Hint;
    }

    void LegendRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (SliceOf(sender) is { Tag: not null } slice) Choose(slice);
    }

    /// <summary>--token-charts: every chart, for projects and sessions, over 24 h and 7 days, from your real logs.</summary>
    internal static void SaveSnapshots(string dir)
    {
        Directory.CreateDirectory(dir);
        var scanner = new SessionScanner();
        var view = new TokenView { Background = TokenChart.Surface };
        var host = new Window
        {
            Content = view, Width = 900, Height = 560, Left = -10000, Top = -10000,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Background = TokenChart.Surface,
        };
        host.Show();
        foreach (int hours in new[] { 24, 168 })
        {
            var window = TimeSpan.FromHours(hours);
            scanner.Scan(window);
            var stats = scanner.CountTokens(window);
            var projects = scanner.Projects;
            foreach (bool sessions in new[] { false, true })
                foreach (var kind in Enum.GetValues<TokenChartKind>())
                {
                    view.Bind(new Settings { TokenChart = kind, TokenSessions = sessions }, save: false);
                    view.Show(stats, null, null, cwd => ProjectOf(projects, cwd));
                    host.UpdateLayout();
                    UsageWidget.SavePng(view, Path.Combine(dir,
                        $"{hours}h-{(sessions ? "sessions" : "projects")}-{kind.ToString().ToLowerInvariant()}.png"));
                }
        }
        host.Close();
    }
}
