using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>
/// The runner game in its own window, to play while sessions work. When a session needs you or
/// finishes it stops and says which one, with a button to open it; best scores are kept per mascot.
/// </summary>
public partial class RunnerWindow : Window
{
    readonly Settings settings;
    SessionItem? noticeItem;
    DateTime noticeShown;

    /// <summary>Open session in the notice was clicked.</summary>
    public event Action<SessionItem>? OpenSessionRequested;

    internal RunnerWindow(Settings settings)
    {
        InitializeComponent();
        this.settings = settings;
        View.Daily = settings.RunnerDaily;
        SetMascot(settings.Mascot);
        View.GameOver += score =>
        {
            if (View.Daily)
            {
                string day = DayKey;
                if (settings.RunnerDailyDate == day && score <= settings.RunnerDailyBest) return;
                settings.RunnerDailyDate = day;
                settings.RunnerDailyBest = score;
                settings.Save();
                return;
            }
            string key = settings.Mascot.ToString();
            if (score <= settings.RunnerBest.GetValueOrDefault(key)) return;
            settings.RunnerBest[key] = score;
            settings.Save();
        };

        View.MouseLeftButtonDown += (_, e) => { View.Focus(); View.JumpPressed(); e.Handled = true; };
        View.MouseLeftButtonUp += (_, _) => View.JumpReleased();
        View.MouseRightButtonDown += (_, e) => { View.PowerPressed(); e.Handled = true; };
        Deactivated += (_, _) => View.Pause("Paused · Space to go on");
        Loaded += (_, _) => View.Focus();
        SourceInitialized += (_, _) =>
        {
            int on = 1; // dark title bar, like the dashboard
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
        };
    }

    /// <summary>Who runs; switching mid-run just carries on with the new one.</summary>
    public void SetMascot(MascotKind kind)
    {
        View.Mascot = Mascot.For(kind);
        ShowBest(kind);
        Title = $"Runner · {Mascot.Choices.FirstOrDefault(c => c.Kind == kind).Label}";
    }

    string DayKey => View.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The HI score: today's best on the daily course, else this mascot's best.</summary>
    void ShowBest(MascotKind kind) =>
        View.Best = View.Daily
            ? (settings.RunnerDailyDate == DayKey ? settings.RunnerDailyBest : 0)
            : settings.RunnerBest.GetValueOrDefault(kind.ToString());

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
        View.Pause("");
        if (WindowState == WindowState.Minimized) FlashTaskbar();
    }

    void KeepPlaying_Click(object sender, RoutedEventArgs e)
    {
        Notice.Visibility = Visibility.Collapsed;
        View.Pause("Space to go on");
        View.Focus();
    }

    void OpenSession_Click(object sender, RoutedEventArgs e)
    {
        Notice.Visibility = Visibility.Collapsed;
        View.Pause("Space to go on");
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
            case Key.Space or Key.Up or Key.W:
                if (!e.IsRepeat) View.JumpPressed();
                e.Handled = true;
                break;
            case Key.X or Key.LeftShift or Key.RightShift or Key.E:
                View.PowerPressed();
                e.Handled = true;
                break;
            case Key.P:
                View.TogglePause();
                e.Handled = true;
                break;
            case Key.D when View.State is RunnerView.GameState.Ready or RunnerView.GameState.Over:
                // Between runs only: the daily course or a new one each run
                View.Daily = !View.Daily;
                settings.RunnerDaily = View.Daily;
                settings.Save();
                ShowBest(settings.Mascot);
                e.Handled = true;
                break;
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key is Key.Space or Key.Up or Key.W) View.JumpReleased();
    }

    // ---- --runner -------------------------------------------------------------

    /// <summary>Plays itself off-screen and saves a frame every 1/15 s; ends on a "needs you" notice.</summary>
    internal void SaveAutopilotFrames(string dir, double seconds)
    {
        Directory.CreateDirectory(dir);
        Left = Top = -10000;
        ShowActivated = false;
        Show();
        View.Autopilot = true;
        ShowSessions(1, 2);
        int frame = 0;
        for (double t = 0; t < seconds; t += 1 / 30.0)
        {
            View.Tick(1 / 30.0);
            if (((int)Math.Round(t * 30)) % 2 != 0) continue;
            View.InvalidateVisual();
            UpdateLayout();
            UsageWidget.SavePng((FrameworkElement)VisualTreeHelper.GetChild(this, 0), Path.Combine(dir, $"{frame++:0000}.png"));
        }
        var demo = new SessionItem("demo");
        demo.Update(new SessionSnapshot("demo", "Fix login redirect loop", "dib_Website", "", null, SessionStatus.NeedsYou, "Needs you",
            "Which login provider should I keep?", null, Array.Empty<PlanStep>(), DateTime.UtcNow, null, null), DateTime.UtcNow);
        Interrupt(demo, SessionStatus.NeedsYou);
        View.InvalidateVisual();
        UpdateLayout();
        for (int i = 0; i < 20; i++)
            UsageWidget.SavePng((FrameworkElement)VisualTreeHelper.GetChild(this, 0), Path.Combine(dir, $"{frame++:0000}.png"));
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
