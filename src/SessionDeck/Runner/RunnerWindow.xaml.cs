using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// The games in their own window, one tab each, to play while sessions work. When a session needs you or
/// finishes, the game stops and says which one, with a button to open it; best scores are kept per game
/// and mascot.
/// </summary>
public partial class RunnerWindow : Window
{
    readonly Settings settings;
    readonly ArcadeGame[] games;
    ArcadeGame game;
    SessionItem? noticeItem;
    DateTime noticeShown;

    /// <summary>Open session in the notice was clicked.</summary>
    public event Action<SessionItem>? OpenSessionRequested;

    internal RunnerWindow(Settings settings)
    {
        InitializeComponent();
        this.settings = settings;
        games = new ArcadeGame[] { new RunnerView(), new DungeonGame(), new MergeGame(), new ClimbGame(), new DefenseGame(), new DiffGame() };
        foreach (var each in games)
        {
            each.Daily = settings.RunnerDaily;
            each.GameOver += score => SaveBest(each, score);
            each.MouseLeftButtonDown += (_, e) => { each.Focus(); each.Click(At(e, each), false); e.Handled = true; };
            each.MouseLeftButtonUp += (_, e) => each.ClickUp(At(e, each), false);
            each.MouseRightButtonDown += (_, e) => { each.Click(At(e, each), true); e.Handled = true; };
            each.MouseRightButtonUp += (_, e) => each.ClickUp(At(e, each), true);
            each.MouseMove += (_, e) => each.Hover(At(e, each));
            var tab = new RadioButton { Content = each.Title, Style = (Style)FindResource("GameTab"), GroupName = "games" };
            tab.Checked += (_, _) => Play(each);
            Tabs.Children.Add(tab);
        }
        game = games.FirstOrDefault(g => g.Id == settings.Game) ?? games[0];
        SetMascot(settings.Mascot);
        Tab(game).IsChecked = true;

        Deactivated += (_, _) => game.Pause("Paused · Space to go on");
        Loaded += (_, _) => game.Focus();
        SourceInitialized += (_, _) =>
        {
            int on = 1; // dark title bar, like the dashboard
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
        };
    }

    static Point At(MouseEventArgs e, ArcadeGame on)
    {
        var p = e.GetPosition(on);
        return new Point(p.X / ArcadeGame.Zoom, p.Y / ArcadeGame.Zoom);
    }

    RadioButton Tab(ArcadeGame of) => (RadioButton)Tabs.Children[Array.IndexOf(games, of)];

    /// <summary>Switches to a game; the one left behind pauses and waits where it was.</summary>
    void Play(ArcadeGame next)
    {
        if (Stage.Child == next) return;
        game.Pause("Paused · Space to go on");
        game = next;
        Stage.Child = next;
        Keys.Text = next.Keys + " · Tab: next game · D: daily · P: pause · Esc: close";
        ShowTitle();
        ShowBest(next);
        if (settings.Game != next.Id)
        {
            settings.Game = next.Id;
            settings.Save();
        }
        next.Focus();
    }

    /// <summary>Who plays; switching mid-run just carries on with the new one.</summary>
    public void SetMascot(MascotKind kind)
    {
        foreach (var each in games)
        {
            each.Mascot = Mascot.For(kind);
            ShowBest(each);
        }
        ShowTitle();
    }

    void ShowTitle() => Title = $"{game.Title} · {Mascot.Choices.FirstOrDefault(c => c.Kind == settings.Mascot).Label}";

    static string DayKey(ArcadeGame of) => of.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The HI score: today's best on the daily course, else this mascot's best.</summary>
    void ShowBest(ArcadeGame of) =>
        of.Best = of.Daily
            ? (settings.DailyDate == DayKey(of) ? settings.DailyBest.GetValueOrDefault(of.Id) : 0)
            : settings.GameBest.GetValueOrDefault($"{of.Id}:{settings.Mascot}");

    void SaveBest(ArcadeGame of, int score)
    {
        if (of.Daily)
        {
            string day = DayKey(of);
            if (settings.DailyDate != day)
            {
                settings.DailyDate = day;
                settings.DailyBest.Clear();
            }
            if (score <= settings.DailyBest.GetValueOrDefault(of.Id)) return;
            settings.DailyBest[of.Id] = score;
        }
        else
        {
            string key = $"{of.Id}:{settings.Mascot}";
            if (score <= settings.GameBest.GetValueOrDefault(key)) return;
            settings.GameBest[key] = score;
        }
        settings.Save();
    }

    /// <summary>The line under the game: how many sessions are still working, and how many wait for you.</summary>
    public void ShowSessions(int needYou, int working)
    {
        Sessions.Text = (working == 0 ? "No session working" : working == 1 ? "1 session working" : $"{working} sessions working")
                        + (needYou > 0 ? $" · {needYou} need{(needYou == 1 ? "s" : "")} you" : "");
    }

    /// <summary>A session needs you or finished: the game pauses and says so.</summary>
    public void Interrupt(SessionItem item, SessionStatus status)
    {
        noticeItem = item;
        noticeShown = DateTime.UtcNow;
        NoticeTitle.Text = item.Project + status switch
        {
            SessionStatus.NeedsYou => " needs you",
            SessionStatus.Check => " may need approval",
            _ => " finished, your turn",
        };
        NoticeText.Text = item.Title;
        Notice.Visibility = Visibility.Visible;
        game.Pause("");
        if (WindowState == WindowState.Minimized) FlashTaskbar();
    }

    void KeepPlaying_Click(object sender, RoutedEventArgs e)
    {
        Notice.Visibility = Visibility.Collapsed;
        game.Pause("Space to go on");
        game.Focus();
    }

    void OpenSession_Click(object sender, RoutedEventArgs e)
    {
        Notice.Visibility = Visibility.Collapsed;
        game.Pause("Space to go on");
        if (noticeItem != null) OpenSessionRequested?.Invoke(noticeItem);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (Notice.Visibility == Visibility.Visible)
        {
            // Enter opens the session; Space keeps playing, once the notice has been up long enough to be read
            if (e.Key == Key.Enter) OpenSession_Click(this, e);
            else if (e.Key == Key.Space && DateTime.UtcNow - noticeShown > TimeSpan.FromSeconds(0.6)) KeepPlaying_Click(this, e);
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Tab:
                int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? games.Length - 1 : 1;
                Tab(games[(Array.IndexOf(games, game) + step) % games.Length]).IsChecked = true;
                e.Handled = true;
                return;
            case Key.P:
                game.TogglePause();
                e.Handled = true;
                return;
            case Key.D when game.State is GameState.Ready or GameState.Over:
                // Between runs only: the daily course or a new one each run, for every game
                settings.RunnerDaily = !game.Daily;
                settings.Save();
                foreach (var each in games)
                {
                    each.Daily = settings.RunnerDaily;
                    ShowBest(each);
                }
                e.Handled = true;
                return;
        }
        // The game gets the rest; arrows never move the focus off it
        if (game.Press(e.Key, e.IsRepeat) || e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Space) e.Handled = true;
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        game.Release(e.Key);
    }

    // ---- Off-screen flags -----------------------------------------------------

    FrameworkElement Root => (FrameworkElement)VisualTreeHelper.GetChild(this, 0);

    void ShowOffscreen()
    {
        Left = Top = -10000;
        ShowActivated = false;
        Show();
        ShowSessions(1, 2);
    }

    void SaveFrame(string path)
    {
        game.InvalidateVisual();
        UpdateLayout();
        UsageWidget.SavePng(Root, path);
    }

    /// <summary>--runner: the runner plays itself off-screen, a frame every 1/15 s, and ends on a "needs you" notice.</summary>
    internal void SaveAutopilotFrames(string dir, double seconds)
    {
        Directory.CreateDirectory(dir);
        ShowOffscreen();
        Tab(games[0]).IsChecked = true;
        game.Autopilot = true;
        int frame = 0;
        for (double t = 0; t < seconds; t += 1 / 30.0)
        {
            game.Tick(1 / 30.0);
            if (((int)Math.Round(t * 30)) % 2 != 0) continue;
            SaveFrame(Path.Combine(dir, $"{frame++:0000}.png"));
        }
        var demo = new SessionItem("demo");
        demo.Update(new SessionSnapshot("demo", "Fix login redirect loop", "dib_Website", "", null, SessionStatus.NeedsYou, "Needs you",
            "Which login provider should I keep?", null, Array.Empty<PlanStep>(), DateTime.UtcNow, null, null), DateTime.UtcNow);
        Interrupt(demo, SessionStatus.NeedsYou);
        for (int i = 0; i < 20; i++)
            SaveFrame(Path.Combine(dir, $"{frame++:0000}.png"));
        Close();
    }

    /// <summary>
    /// --games: every game plays itself off-screen for a while, saving a few frames each to &lt;dir&gt;/&lt;game&gt;/;
    /// then the runner's autopilot runs many courses to check none is impossible. Writes report.txt.
    /// </summary>
    internal void CheckGames(string dir, int courses)
    {
        Directory.CreateDirectory(dir);
        var report = new StringBuilder();
        ShowOffscreen();
        foreach (var each in games)
        {
            string sub = Path.Combine(dir, each.Id);
            Directory.CreateDirectory(sub);
            Tab(each).IsChecked = true;
            each.Autopilot = true;
            var shots = new[] { 0.5, 4, 10, 20, 35 };
            int runs = 0, best = 0;
            Action<int> over = score => { runs++; best = Math.Max(best, score); };
            each.GameOver += over;
            try
            {
                for (double t = 0; t < 40; t += 1 / 30.0)
                {
                    each.Tick(1 / 30.0);
                    foreach (var at in shots)
                        if (t <= at && t + 1 / 30.0 > at) SaveFrame(Path.Combine(sub, $"{at:00.0}s.png"));
                }
                report.AppendLine($"{each.Id}: ok, {runs} runs ended in 40 s, best {Math.Max(best, each.Score)}, now {each.State} at {each.Score}");
            }
            catch (Exception ex)
            {
                report.AppendLine($"{each.Id}: CRASHED {ex}");
            }
            each.GameOver -= over;
            each.Autopilot = false;
        }

        // The runner on many courses: a perfect autopilot should clear every one (else a course is impossible),
        // and one whose timing is off like a person's shows how hard it is
        foreach (double slop in new[] { 0, 0.03, 0.06 })
        {
            var runner = new RunnerView { Autopilot = true, PilotSlop = slop };
            var deaths = new Dictionary<string, int>();
            var lasted = new List<double>();
            for (int seed = 1; seed <= courses; seed++)
            {
                runner.FixedSeed = seed;
                runner.Restart();
                for (double t = 0; t < 180 && runner.State != GameState.Over; t += 1 / 60.0) runner.Tick(1 / 60.0);
                lasted.Add(runner.Seconds);
                string why = runner.State == GameState.Over ? runner.Death.Split(' ')[0] : "survived 180 s";
                deaths[why] = deaths.GetValueOrDefault(why) + 1;
                if (runner.State == GameState.Over && slop == 0) report.AppendLine($"  runner course {seed}: {runner.Seconds:0.0} s, {runner.Death}");
            }
            lasted.Sort();
            report.AppendLine($"runner autopilot, timing off by up to {slop * 1000:0} ms, {courses} courses: median {lasted[lasted.Count / 2]:0.0} s, min {lasted[0]:0.0} s, max {lasted[^1]:0.0} s");
            foreach (var (why, n) in deaths.OrderByDescending(d => d.Value)) report.AppendLine($"  {why}: {n}");
        }
        File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
        Close();
    }

    void FlashTaskbar()
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FLASHWINFO>(),
            hwnd = new WindowInteropHelper(this).Handle,
            dwFlags = 3 | 12, // caption and taskbar, until the window comes to the front
        };
        FlashWindowEx(ref info);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool FlashWindowEx(ref FLASHWINFO info);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
