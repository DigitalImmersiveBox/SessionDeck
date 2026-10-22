using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClaudeSessions;

public partial class MainWindow : Window
{
    readonly ObservableCollection<SessionItem> items = new();
    readonly ObservableCollection<ProjectItem> projects = new();
    readonly ProjectItem allProjects = new("All projects", null) { IsSelected = true };
    ProjectItem selectedProject;
    IReadOnlyList<ProjectEntry> projectSource = Array.Empty<ProjectEntry>();
    string projectsKey = "";
    readonly Dictionary<string, ProjectIconLook> icons = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SessionItem> byKey = new();
    readonly SessionScanner scanner = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly HashSet<string> notified = new();
    readonly Settings settings;
    readonly Tray? tray; // none in an off-screen render
    readonly bool offscreen;
    readonly UsageMonitor usage = new();
    readonly WeatherService weather;
    readonly UsageWidget widget;
    RunnerWindow? game;
    TokenStats? tokenStats;
    int chartCount;
    DateTime chartAt;
    (int, bool) shownIcon = (-1, false);
    static readonly Brush Muted = Ink.Of(0xA9, 0xAC, 0xB4);
    readonly ListCollectionView view;
    bool scanning, firstScan = true, exiting, hintShown;
    string lastOrder = "";
    int questions, working;

    const double MinCardWidth = 300;

    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(int), typeof(MainWindow), new PropertyMetadata(1));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    static bool IsFinished(SessionItem i) => i.Status is SessionStatus.Done or SessionStatus.Idle;

    bool IsListed(SessionItem i) => !settings.HideDone || !IsFinished(i);

    /// <param name="demoWidget">Show made-up states in the usage widget (--demo); the dashboard stays live.</param>
    /// <param name="snapshot">Settings for an off-screen render (--dashboard): no tray, widget, scans, timers or network.</param>
    internal MainWindow(bool demoWidget = false, Settings? snapshot = null)
    {
        // Set before InitializeComponent: the XAML's Checked handlers already read the settings
        settings = snapshot ?? Settings.Load();
        offscreen = snapshot != null;
        InitializeComponent();
        scanner.RestoreDismissed(settings.DismissedQuestions);
        selectedProject = allProjects;
        projects.Add(allProjects);

        view = (ListCollectionView)CollectionViewSource.GetDefaultView(items);
        view.SortDescriptions.Add(new SortDescription(nameof(SessionItem.Rank), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(SessionItem.LastActivity), ListSortDirection.Descending));
        view.Filter = o => o is SessionItem i && IsListed(i) && selectedProject.Contains(i.Cwd);
        List.ItemsSource = view;
        HideDoneButton.IsChecked = settings.HideDone;
        ProjectList.ItemsSource = projects;
        ShowHiddenButton.IsChecked = settings.ShowHiddenProjects;
        ProjectsButton.IsChecked = settings.ShowProjects;
        ProjectsPanel.Visibility = settings.ShowProjects ? Visibility.Visible : Visibility.Collapsed;
        Tokens.Bind(settings);
        Tokens.ProjectChosen += ChooseProject;
        Tokens.SessionChosen += s =>
        {
            scanner.MarkSeen(s.Key);
            if (s.Link != null) OpenLink(s.Link);
        };
        TokensButton.IsChecked = settings.ShowTokens;
        ShowTokenView();

        Width = settings.Width;
        Height = settings.Height;
        if (settings.Left is double left && settings.Top is double top && OnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        PinButton.IsChecked = Topmost = settings.Topmost;

        foreach (var chip in FindChips())
            if ((string)chip.Tag == settings.WindowHours.ToString()) chip.IsChecked = true;

        if (!offscreen)
        {
            tray = new Tray(settings.Topmost, settings.Notifications, settings.Widget.Visible, settings.Mascot);
            tray.ShowRequested += ShowFromTray;
            tray.ExitRequested += ExitApp;
            tray.TopmostChanged += on => PinButton.IsChecked = on;
            tray.NotificationsChanged += on => { settings.Notifications = on; settings.Save(); };
            tray.WidgetChanged += SetWidgetVisible;
            tray.MascotChanged += SetMascot;
            tray.LinkClicked += OpenLink;
        }
        BannerScene.Mascot = Mascot.For(settings.Mascot);

        widget = new UsageWidget(usage, settings, demoWidget, snapshot: offscreen);
        widget.OpenSessionRequested += Open;
        widget.DashboardRequested += ShowFromTray;
        widget.GameRequested += OpenGame;
        widget.VisibleChangedByUser += SetWidgetVisible;
        WidgetButton.IsChecked = settings.Widget.Visible;
        if (settings.Widget.Visible && !offscreen) widget.ShowWidget();
        usage.Changed += RenderBanner;
        // Keep Clawd and the clouds clear of the text as columns come and go
        BannerText.SizeChanged += (_, _) => BannerScene.SkyLeft = BannerText.ActualWidth + 40;
        weather = new WeatherService(settings);
        weather.Changed += RenderWeather;

        SourceInitialized += (_, _) => UseDarkTitleBar();
        timer.Tick += async (_, _) => await Refresh();
        if (offscreen) return;
        usage.Start();
        weather.Start();
        timer.Start();
        _ = Refresh();
    }

    /// <summary>
    /// For --dashboard: the window filled with made-up projects and sessions (Demo.Dashboard), rendered
    /// off-screen to PNG. Reads none of your sessions and saves nothing.
    /// </summary>
    public static void SaveSnapshot(MascotKind mascot, string path)
    {
        // Wide enough for four columns of cards, so the eight demo sessions fill two rows
        var settings = new Settings { Transient = true, Mascot = mascot, Width = 1560, Height = 680 };
        var window = new MainWindow(snapshot: settings)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };
        window.Show();
        var demo = Demo.Dashboard(DateTime.UtcNow);
        window.usage.ShowDemo(demo.Usage, demo.Status, demo.History);
        window.weather.ShowDemo(demo.Weather);
        window.Apply(demo.Sessions.ToList());
        window.ShowProjects(demo.Projects);
        window.RenderToday(demo.Today);
        window.RenderWeather();
        window.UpdateLayout();
        window.BannerScene.Advance(2.5);
        // Let the queued layout passes (the banner fitting the weather in) run before capturing
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir != null) Directory.CreateDirectory(dir);
        // The window's own background isn't part of its content; without it the header's text renders on black
        var content = (Panel)window.Content;
        content.Background ??= window.Background;
        window.UpdateLayout();
        UsageWidget.SavePng(content, path);
        window.exiting = true;
        window.Close();
    }

    async Task Refresh()
    {
        if (scanning) return;
        scanning = true;
        try
        {
            var window = TimeSpan.FromHours(settings.WindowHours);
            bool tokens = settings.ShowTokens;
            // Tokens are counted only while their view is open (the first count reads whole logs)
            var (snaps, stats) = await Task.Run(() => (scanner.Scan(window), tokens ? scanner.CountTokens(window) : null));
            Apply(snaps);
            ShowProjects(scanner.Projects);
            RenderToday(scanner.Today);
            if (stats != null && stats.Window == TimeSpan.FromHours(settings.WindowHours))
            {
                tokenStats = stats;
                RenderTokens();
            }
        }
        catch (Exception ex)
        {
            Summary.Text = "Scan error: " + ex.Message;
        }
        finally
        {
            scanning = false;
        }
    }

    void Apply(List<SessionSnapshot> snaps)
    {
        var now = DateTime.UtcNow;
        var live = new HashSet<string>();
        int finished = 0;
        foreach (var s in snaps)
        {
            live.Add(s.Key);
            if (!byKey.TryGetValue(s.Key, out var item))
            {
                item = new SessionItem(s.Key);
                byKey[s.Key] = item;
                item.Update(s, now);
                items.Add(item);
            }
            else
            {
                bool wasWorking = item.Status == SessionStatus.Working;
                item.Update(s, now);
                if (wasWorking && item.Status is SessionStatus.YourTurn or SessionStatus.Done) finished++;
            }

            if (s.NotifyToken != null && notified.Add(s.NotifyToken) && !firstScan)
            {
                if (settings.Notifications) Notify(s);
                game?.Interrupt(item, s.Status); // stops the runner whether or not pop-ups are on
            }
        }
        foreach (var gone in byKey.Keys.Where(k => !live.Contains(k)).ToList())
        {
            items.Remove(byKey[gone]);
            byKey.Remove(gone);
        }
        RefreshViewIfChanged();
        firstScan = false;

        int needYou = items.Count(i => i.Status is SessionStatus.NeedsYou or SessionStatus.Check or SessionStatus.YourTurn);
        int working = items.Count(i => i.Status == SessionStatus.Working);
        int done = items.Count(IsFinished);
        ShowSummary(needYou, working, done);
        UpdateEmptyText();
        game?.ShowSessions(needYou, working);
        BannerScene.SessionNote = PixelScene.NoteFor(needYou, working);

        // The mascot (widget and banner): "!" for questions and approvals, scuttling while working, hops when one finishes
        this.working = working;
        questions = items.Count(i => i.Status is SessionStatus.NeedsYou or SessionStatus.Check);
        var active = items.Where(i => i.Status is SessionStatus.NeedsYou or SessionStatus.Check or SessionStatus.YourTurn or SessionStatus.Working)
            .OrderBy(i => i.Rank).ThenByDescending(i => i.LastActivity).ToList();
        widget.ShowSessions(questions, working, done, active, finished);
        if (finished > 0) BannerScene.Celebrate();
        RenderBanner();

        string tip = needYou > 0 ? $"{needYou} session(s) need you · {working} working" : $"Session Deck · {working} working";
        tray?.Update(needYou, working, tip);
        var iconState = (needYou, working > 0);
        if (iconState != shownIcon)
        {
            shownIcon = iconState;
            Icon = MascotIcon.Source(Mascot.For(settings.Mascot), 32, needYou, working > 0); // taskbar and title bar
        }
        Title = needYou > 0 ? $"({needYou}) Session Deck" : "Session Deck";
    }

    void ShowProjects(IReadOnlyList<ProjectEntry> list)
    {
        projectSource = list;
        var hidden = new HashSet<string>(settings.HiddenProjects, StringComparer.OrdinalIgnoreCase);
        string key = settings.ShowHiddenProjects + "|" + string.Join("|", hidden) + "|"
                     + string.Join("|", list.Select(p => p.Name + ">" + p.Path));
        if (key != projectsKey)
        {
            projectsKey = key;
            while (projects.Count > 1) projects.RemoveAt(1);
            var duplicates = list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var p in list)
            {
                bool isHidden = hidden.Contains(p.Path);
                if (isHidden && !settings.ShowHiddenProjects) continue;
                var item = new ProjectItem(p.Name, p.Path, duplicates.Contains(p.Name) ? FolderHint(p) : null, isHidden);
                if (string.Equals(p.Path, selectedProject.Path, StringComparison.OrdinalIgnoreCase))
                {
                    item.IsSelected = true;
                    selectedProject = item;
                }
                projects.Add(item);
            }
            if (selectedProject.Path != null && !projects.Contains(selectedProject))
                SelectProject(allProjects);

            int hiddenCount = list.Count(p => hidden.Contains(p.Path));
            ShowHiddenButton.Visibility = hiddenCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowHiddenButton.Content = settings.ShowHiddenProjects ? "Done showing hidden" : $"Show hidden ({hiddenCount})";
        }
        // Counts follow what the grid would show for that project (time range + Hide done)
        foreach (var p in projects)
            p.Count = items.Count(i => IsListed(i) && p.Contains(i.Cwd));
        ApplyIcons();
    }

    // ---- Project icons -------------------------------------------------------

    // Sizes in the layout (MainWindow.xaml): the projects panel and the cards
    const double PanelIcon = 18, CardIcon = 36;

    // Rendered at the screen's real pixel size, so the identicon's cells stay crisp
    int IconPixels(double size) => (int)Math.Ceiling(size * VisualTreeHelper.GetDpi(this).DpiScaleX);

    void ApplyIcons()
    {
        foreach (var p in projects)
            if (p.Path != null) p.Look = IconFor(p.Path);
        foreach (var item in items)
        {
            var look = ProjectFolderOf(item.Cwd) is { } folder ? IconFor(folder) : null;
            item.ProjectIcon = look?.CardImage;
            item.ProjectIconBacked = look != null && look.Source != IconSource.Generated;
        }
    }

    // The deepest known project folder that holds the session (hidden projects too)
    string? ProjectFolderOf(string cwd) => projectSource
        .Where(p => ProjectItem.IsInside(cwd, p.Path))
        .OrderByDescending(p => p.Path.Length)
        .FirstOrDefault()?.Path;

    // Your pick first, then (looked up in the background) the project's own icon, else the identicon
    ProjectIconLook IconFor(string path)
    {
        if (icons.TryGetValue(path, out var known)) return known;
        int size = IconPixels(PanelIcon), cardSize = IconPixels(CardIcon);
        string? chosen = IconOverride(path);
        ProjectIconLook look;
        // Files are loaded once, big enough for the larger of the two
        if (chosen != null && chosen != ProjectIcon.GeneratedOverride && ProjectIcon.Load(chosen, cardSize) is { } custom)
        {
            look = new(custom, custom, IconSource.Custom, chosen, true);
        }
        else
        {
            look = new(ProjectIcon.Generate(path, size), ProjectIcon.Generate(path, cardSize), IconSource.Generated, null, chosen != null);
            if (chosen == null) _ = FindOwnIcon(path, cardSize);
        }
        icons[path] = look;
        return look;
    }

    async Task FindOwnIcon(string path, int size)
    {
        var file = await Task.Run(() => ProjectIcon.FindInProject(path));
        // Skip if you've picked an icon meanwhile, or the screen's scale changed
        if (file == null || size != IconPixels(CardIcon)
            || !icons.TryGetValue(path, out var current) || current.Source != IconSource.Generated || current.Overridden) return;
        if (ProjectIcon.Load(file, size) is not { } image) return;
        icons[path] = new(image, image, IconSource.ProjectFile, Path.GetRelativePath(path, file), false);
        ApplyIcons();
    }

    string? IconOverride(string path) =>
        settings.ProjectIcons.FirstOrDefault(p => string.Equals(p.Key, path, StringComparison.OrdinalIgnoreCase)).Value;

    void SetIconOverride(string path, string? value)
    {
        // Your previous pick's copy isn't needed any more
        if (IconOverride(path) is { } old && old != value
            && old.StartsWith(ProjectIcon.CustomDir + "\\", StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(old); } catch { }
        }
        foreach (var key in settings.ProjectIcons.Keys.Where(k => string.Equals(k, path, StringComparison.OrdinalIgnoreCase)).ToList())
            settings.ProjectIcons.Remove(key);
        if (value != null) settings.ProjectIcons[path] = value;
        settings.Save();
        icons.Remove(path);
        ApplyIcons();
    }

    void ChangeIcon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProjectItem { Path: { } path } project) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Choose an icon for {project.Name}",
            Filter = "Images (*.png, *.ico, *.svg, *.jpg, *.bmp, *.gif)|*.png;*.ico;*.svg;*.jpg;*.jpeg;*.bmp;*.gif",
            InitialDirectory = Directory.Exists(path) ? path : null,
        };
        if (dialog.ShowDialog(this) != true) return;
        if (ProjectIcon.Load(dialog.FileName, IconPixels(CardIcon)) == null)
        {
            MessageBox.Show("That file couldn't be read as an image.", "Session Deck");
            return;
        }
        try
        {
            // Keep a copy, so the icon stays when the original is moved or deleted
            Directory.CreateDirectory(ProjectIcon.CustomDir);
            string copy = Path.Combine(ProjectIcon.CustomDir,
                $"{ProjectIcon.KeyOf(path)}-{DateTime.UtcNow.Ticks}{Path.GetExtension(dialog.FileName).ToLowerInvariant()}");
            File.Copy(dialog.FileName, copy);
            SetIconOverride(path, copy);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not save the icon:\n" + ex.Message, "Session Deck");
        }
    }

    void GeneratedIcon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectItem { Path: { } path })
            SetIconOverride(path, ProjectIcon.GeneratedOverride);
    }

    void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectItem { Path: { } path })
            SetIconOverride(path, null);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        icons.Clear();
        ApplyIcons();
    }

    // Two projects with the same name: show the folder when the name differs from it, else its parent
    static string FolderHint(ProjectEntry p)
    {
        string folder = Path.GetFileName(p.Path);
        return " " + (string.Equals(folder, p.Name, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(p.Path) ?? "")
            : folder);
    }

    void HideProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProjectItem { Path: { } path } project) return;
        if (project.IsHidden)
            settings.HiddenProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        else
            settings.HiddenProjects.Add(path);
        settings.Save();
        ShowProjects(projectSource);
    }

    void ShowHidden_Changed(object sender, RoutedEventArgs e)
    {
        settings.ShowHiddenProjects = ShowHiddenButton.IsChecked == true;
        settings.Save();
        ShowProjects(projectSource);
    }

    void ProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectItem { Path: { } path } && Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    void SelectProject(ProjectItem project)
    {
        selectedProject.IsSelected = false;
        selectedProject = project;
        project.IsSelected = true;
        RefreshViewIfChanged();
        UpdateEmptyText();
        RenderTokens();
    }

    void UpdateEmptyText()
    {
        if (settings.ShowTokens)
        {
            EmptyText.Visibility = Visibility.Collapsed;
            return;
        }
        EmptyText.Text = selectedProject.Path == null
            ? "No sessions to show in this time range."
            : $"No sessions in {selectedProject.Name} for this time range.\nTry a longer range, or press + to start one.";
        EmptyText.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    // Sorting and the filter aren't live, so refresh the view only when
    // what it would show (order, statuses, Hide done, the project filter) has changed.
    // While the pointer is over the cards the order holds still, so a card can't move
    // under a click; the change lands when the pointer leaves.
    void RefreshViewIfChanged()
    {
        string order = settings.HideDone + "|" + selectedProject.Path + "|" + string.Join("|", items
            .OrderBy(i => i.Rank).ThenByDescending(i => i.LastActivity)
            .Select(i => i.Key + ":" + i.Rank));
        if (order == lastOrder || CardScroller.IsMouseOver) return;
        lastOrder = order;
        view.Refresh();
    }

    void Cards_MouseLeave(object sender, MouseEventArgs e) => RefreshViewIfChanged();

    void Notify(SessionSnapshot s)
    {
        string head = s.Status switch
        {
            SessionStatus.NeedsYou => "needs you",
            SessionStatus.Check => "may need approval",
            _ => "finished, your turn",
        };
        tray?.Notify($"{s.Project} — {head}", $"{s.Title}\n{s.Activity ?? s.Detail ?? ""}", s.Link);
    }

    void ShowSummary(int needYou, int working, int done)
    {
        Summary.Inlines.Clear();
        void Part(int n, string label, Color color)
        {
            if (Summary.Inlines.Count > 0) Summary.Inlines.Add(new Run("   "));
            Summary.Inlines.Add(new Run("● ") { Foreground = new SolidColorBrush(color) });
            Summary.Inlines.Add(new Run($"{n} {label}"));
        }
        Part(needYou, needYou == 1 ? "needs you" : "need you", Palette.NeedsYou);
        Part(working, "working", Palette.Working);
        Part(done, settings.HideDone ? "done (hidden)" : "done / idle", Palette.Done);
    }

    // ---- Usage banner and widget ---------------------------------------------

    void RenderBanner()
    {
        var now = DateTime.UtcNow;
        var report = usage.Usage;
        double pct = UsageLook.Percent(report);
        BannerFigure.Visibility = report == null ? Visibility.Collapsed : Visibility.Visible;
        BannerPercent.Text = Math.Round(pct).ToString("0");
        BannerPercent.Foreground = UsageLook.Accent(pct, working);
        BannerReset.Text = UsageLook.ResetText(report, usage.Error, now);
        var weeks = UsageLook.Weeks(report, now);
        if (BannerWeeks.ItemsSource is not IEnumerable<WeekRow> shown || !shown.SequenceEqual(weeks))
            BannerWeeks.ItemsSource = weeks;
        BannerStatusDot.Fill = UsageLook.StatusBrush(usage.Status);
        BannerStatus.Text = usage.Status.Description;

        // The chart only changes with a new reading (every 2 min) or as time slides by
        if (usage.History.Points.Count != chartCount || now - chartAt > TimeSpan.FromMinutes(1))
        {
            chartCount = usage.History.Points.Count;
            chartAt = now;
            BannerSpark.Show(usage.History.Chart(now, TimeSpan.FromHours(12)), UsageLook.Accent(pct, working));
        }
        var (forecast, warning) = usage.History.Forecast(report, now);
        BannerForecast.Text = forecast;
        BannerForecast.Foreground = warning ? UsageLook.Amber : Muted;

        bool night = UsageLook.IsNight;
        BannerScene.SkyLeft = BannerText.ActualWidth + 40;
        BannerScene.Night = night;
        BannerScene.Weather = UsageLook.WeatherFor(usage.Status);
        BannerScene.Mood = UsageLook.MoodFor(report, questions, working, night);
    }

    void RenderToday(TodayStats today)
    {
        TodayWorked.Text = today.Sessions == 0 ? "No sessions yet"
            : $"Claude worked {(today.Worked.TotalHours >= 1 ? $"{(int)today.Worked.TotalHours}h {today.Worked.Minutes}m" : $"{Math.Max(1, (int)today.Worked.TotalMinutes)}m")}";
        TodayProjects.Text = string.Join(" · ", today.Projects.Take(3).Select(p => $"{p.Project} {p.Sessions}"));
        TodayProjects.ToolTip = today.Projects.Count == 0 ? null
            : string.Join("\n", today.Projects.Select(p => $"{p.Project}: {p.Sessions} session{(p.Sessions == 1 ? "" : "s")}"));
    }

    void RenderWeather()
    {
        if (weather.Current is not { } w)
        {
            WeatherCity.Text = settings.WeatherPlace?.City.ToUpperInvariant() ?? "WEATHER";
            WeatherIcon.Text = WeatherTemp.Text = "";
            WeatherText.Text = weather.Error ?? "Loading…";
            return;
        }
        var (text, symbol) = WeatherService.Describe(w.Code, w.IsDay);
        WeatherCity.Text = w.Place.ToUpperInvariant();
        WeatherIcon.Text = symbol;
        WeatherIcon.Foreground = symbol switch
        {
            "☀" or "⚡" => UsageLook.Amber,
            "☂" or "❄" => UsageLook.Blue,
            _ => Muted,
        };
        string Degrees(double celsius) => $"{Math.Round(settings.Fahrenheit ? celsius * 9 / 5 + 32 : celsius):0}°";
        WeatherTemp.Text = Degrees(w.Temperature) + (settings.Fahrenheit ? "F" : "C");
        string range = double.IsNaN(w.High) || double.IsNaN(w.Low) ? "" : $" · {Degrees(w.High)}/{Degrees(w.Low)}";
        WeatherText.Text = text + range + (w.RainChance >= 20 ? $" · {w.RainChance}% rain" : "");
        BannerWeather.ToolTip = (weather.HasManualCity ? w.Place : $"{w.Place} (approximate, from your internet address)")
            + $"\nUpdated {w.FetchedAt.ToLocalTime():HH:mm} · Open-Meteo\nRight-click to change the city, or use Settings (gear)";
        // Its width just changed: re-check what fits once it's measured
        Dispatcher.BeginInvoke(() => LayoutBanner(Banner.ActualWidth), DispatcherPriority.Loaded);
    }

    void WeatherMenu_Opened(object sender, RoutedEventArgs e) => UseDetectedItem.IsEnabled = weather.HasManualCity;

    void ChangeCity_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CityDialog(weather.HasManualCity ? settings.WeatherPlace?.City : null) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Chosen is { } city)
            weather.SetCity(city);
    }

    void UseDetected_Click(object sender, RoutedEventArgs e) => weather.UseDetectedPlace();

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(settings, weather) { Owner = this };
        dialog.MascotChanged += SetMascot;
        dialog.UnitChanged += RenderWeather;
        dialog.ShowDialog();
    }

    void Game_Click(object sender, RoutedEventArgs e) => OpenGame();

    /// <summary>The runner game: one window, brought to the front if it's already open.</summary>
    void OpenGame()
    {
        if (game != null)
        {
            if (game.WindowState == WindowState.Minimized) game.WindowState = WindowState.Normal;
            game.Activate();
            return;
        }
        game = new RunnerWindow(settings);
        game.OpenSessionRequested += Open;
        game.Closed += (_, _) => game = null;
        game.ShowSessions(questions + items.Count(i => i.Status == SessionStatus.YourTurn), working);
        game.Show();
    }

    // The weather sits in the top-right corner (Clawd walks under it); as the window narrows
    // the chart drops out first, then the weather
    void Banner_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutBanner(e.NewSize.Width);

    void LayoutBanner(double width)
    {
        const double fixedPart = 16 + 118 + 24 + 190, gap = 20, chartWidth = 185 + 24;
        double weatherWidth = BannerWeather.ActualWidth > 0 ? BannerWeather.ActualWidth + 12 : 260;
        double room = width - fixedPart - gap;
        bool showWeather = room >= weatherWidth;
        if (showWeather) room -= weatherWidth;
        bool showChart = room >= chartWidth;
        BannerWeather.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
        BannerChart.Visibility = showChart ? Visibility.Visible : Visibility.Collapsed;
    }

    void Widget_Changed(object sender, RoutedEventArgs e) => SetWidgetVisible(WidgetButton.IsChecked == true);

    void SetWidgetVisible(bool on)
    {
        // XAML sets IsChecked before the constructor has made the widget; an off-screen render never shows it
        if (widget == null || offscreen) return;
        settings.Widget.Visible = on;
        settings.Save();
        if (on) widget.ShowWidget();
        else widget.Hide();
        tray?.Widget = on;
        WidgetButton.IsChecked = on;
    }

    void SetMascot(MascotKind kind)
    {
        settings.Mascot = kind;
        settings.Save();
        var mascot = Mascot.For(kind);
        BannerScene.Mascot = mascot;
        widget.Mascot = mascot;
        tray?.Mascot = kind;
        game?.SetMascot(kind);
        Icon = MascotIcon.Source(mascot, 32, shownIcon.Item1 < 0 ? 0 : shownIcon.Item1, shownIcon.Item2);
    }

    // ---- Card actions -------------------------------------------------------

    static SessionItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as SessionItem;

    void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (ItemOf(sender) is { } item) Open(item);
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Open(item);
    }

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item && Directory.Exists(item.Cwd))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.Cwd}\"") { UseShellExecute = true });
    }

    void Seen_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            scanner.MarkSeen(item.Key);
            _ = Refresh();
        }
    }

    void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        var at = scanner.Dismiss(item.Key);
        var keep = DateTime.UtcNow.AddDays(-7);
        foreach (var old in settings.DismissedQuestions.Where(d => d.Value < keep).Select(d => d.Key).ToList())
            settings.DismissedQuestions.Remove(old);
        settings.DismissedQuestions[item.Key] = at;
        settings.Save();
        _ = Refresh();
    }

    // Questions aren't included: they stay until answered or dismissed one by one
    void MarkAllSeen_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in items.Where(i => i.Status == SessionStatus.YourTurn))
            scanner.MarkSeen(item.Key);
        _ = Refresh();
    }

    void Open(SessionItem item)
    {
        scanner.MarkSeen(item.Key);
        if (item.Link != null) OpenLink(item.Link);
        _ = Refresh();
    }

    static void OpenLink(string link)
    {
        try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Could not open Claude:\n" + ex.Message, "Session Deck"); }
    }

    // ---- Header -------------------------------------------------------------

    void Range_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out int hours) && hours != settings.WindowHours)
        {
            settings.WindowHours = hours;
            settings.Save();
            _ = Refresh();
        }
    }

    void HideDone_Changed(object sender, RoutedEventArgs e)
    {
        bool hide = HideDoneButton.IsChecked == true;
        if (hide == settings.HideDone) return;
        settings.HideDone = hide;
        settings.Save();
        RefreshViewIfChanged();
        UpdateEmptyText();
        _ = Refresh();
    }

    void Tokens_Changed(object sender, RoutedEventArgs e)
    {
        bool on = TokensButton.IsChecked == true;
        if (on == settings.ShowTokens) return;
        settings.ShowTokens = on;
        settings.Save();
        ShowTokenView();
        _ = Refresh();
    }

    void ShowTokenView()
    {
        bool on = settings.ShowTokens;
        Tokens.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        CardScroller.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        UpdateEmptyText();
        RenderTokens();
    }

    void RenderTokens()
    {
        if (!settings.ShowTokens || tokenStats == null) return;
        var known = projectSource;
        Tokens.Show(tokenStats, selectedProject.Path, selectedProject.Path != null ? selectedProject.Name : null,
            cwd => TokenView.ProjectOf(known, cwd));
    }

    // A project clicked in the token charts: filter to it, like a click in the projects panel
    void ChooseProject(string path)
    {
        var project = projects.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        if (project == null) return;
        ProjectsButton.IsChecked = true;
        SelectProject(project);
    }

    void Projects_Changed(object sender, RoutedEventArgs e)
    {
        settings.ShowProjects = ProjectsButton.IsChecked == true;
        ProjectsPanel.Visibility = settings.ShowProjects ? Visibility.Visible : Visibility.Collapsed;
        settings.Save();
        // A hidden panel shouldn't leave an invisible filter behind
        if (!settings.ShowProjects && selectedProject != allProjects) SelectProject(allProjects);
    }

    // Click a project to show only its sessions; click it again to show all
    void Project_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectItem project)
            SelectProject(project == selectedProject ? allProjects : project);
    }

    // The same link the Claude app uses for "New Claude Code Session" in a folder
    void NewSession_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectItem { Path: { } path })
            OpenLink("claude://code/new?folder=" + Uri.EscapeDataString(path));
    }

    void List_SizeChanged(object sender, SizeChangedEventArgs e) =>
        Columns = Math.Max(1, (int)(e.NewSize.Width / MinCardWidth));

    void Pin_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = PinButton.IsChecked == true;
        settings.Topmost = Topmost;
        settings.Save();
        tray?.Topmost = Topmost;
    }

    // The time-range chips (Tag "2", "8", "24"…); the token view's own chips carry a TokenChartKind instead
    IEnumerable<RadioButton> FindChips() => FindLogical<RadioButton>(this).Where(c => c.Tag is string);

    static IEnumerable<T> FindLogical<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var deeper in FindLogical<T>(child)) yield return deeper;
        }
    }

    // ---- Window / tray lifecycle ---------------------------------------------

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveBounds();
        if (!exiting)
        {
            e.Cancel = true;
            Hide();
            if (!hintShown)
            {
                hintShown = true;
                tray?.Notify("Session Deck is still running", "It keeps watching your sessions from the tray. Right-click the tray icon to exit.", null);
            }
            return;
        }
        base.OnClosing(e);
    }

    void ExitApp()
    {
        exiting = true;
        timer.Stop();
        usage.Stop();
        SaveBounds();
        widget.Close();
        tray?.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    void SaveBounds()
    {
        if (WindowState == WindowState.Normal && IsVisible)
        {
            settings.Left = Left;
            settings.Top = Top;
            settings.Width = Width;
            settings.Height = Height;
        }
        settings.Save();
    }

    static bool OnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50
        && top >= SystemParameters.VirtualScreenTop - 10
        && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
        && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

    void UseDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
