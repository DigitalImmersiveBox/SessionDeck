using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace ClaudeSessions;

/// <summary>
/// The small always-on window: plan usage, the sessions that need attention, and pixel Clawd
/// reacting to both. Usage comes from the shared <see cref="UsageMonitor"/>; sessions are pushed
/// in by the dashboard's scanner through <see cref="ShowSessions"/>.
/// </summary>
public partial class UsageWidget : Window
{
    const double CardWidth = 372;
    const double MinScale = 0.7, MaxScale = 2.5;
    const int MaxRows = 4;
    static readonly TimeSpan DemoEvery = TimeSpan.FromSeconds(7);

    readonly UsageMonitor monitor;
    readonly Settings appSettings;
    readonly WidgetSettings settings;
    readonly WinForms.ContextMenuStrip? menu;
    readonly WinForms.ToolStripMenuItem? compactItem, topmostItem;
    readonly DispatcherTimer? demoTimer;

    // Demo mode shows made-up usage and sessions instead of the live ones
    readonly bool demo;
    Scenario? scenario;
    readonly List<SessionItem> demoItems = new();
    int demoIndex;

    int needYou, working, done;
    IReadOnlyList<SessionItem> active = Array.Empty<SessionItem>();

    public event Action<SessionItem>? OpenSessionRequested;
    public event Action? DashboardRequested;
    public event Action? GameRequested;
    /// <summary>Raised when the widget hides or shows itself (× button, menu), so the tray can follow.</summary>
    public event Action<bool>? VisibleChangedByUser;

    /// <param name="snapshot">Off-screen render for --snapshot: no menu, timers or saved settings.</param>
    internal UsageWidget(UsageMonitor monitor, Settings settings, bool demo, bool snapshot = false)
    {
        InitializeComponent();
        this.monitor = monitor;
        appSettings = settings;
        this.settings = settings.Widget;
        this.settings.Scale = Math.Clamp(this.settings.Scale, MinScale, MaxScale);
        this.demo = demo;
        Topmost = this.settings.Topmost;
        Scene.Mascot = Mascot.For(settings.Mascot);
        Card.SizeChanged += (_, _) => FitToCard();
        // Aero Snap would otherwise maximize the widget when dragged to the top edge
        StateChanged += (_, _) => { if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; };
        ApplyCompact();
        if (snapshot) return;

        menu = DarkMenu.Create();
        menu.Items.Add("Open dashboard", null, (_, _) => DashboardRequested?.Invoke());
        menu.Items.Add("Refresh usage", null, (_, _) => monitor.RefreshNow());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        compactItem = new WinForms.ToolStripMenuItem("Compact") { CheckOnClick = true, Checked = this.settings.Compact };
        compactItem.Click += (_, _) => { this.settings.Compact = compactItem.Checked; ApplyCompact(); Save(); };
        topmostItem = new WinForms.ToolStripMenuItem("Always on top") { CheckOnClick = true, Checked = this.settings.Topmost };
        topmostItem.Click += (_, _) => { this.settings.Topmost = Topmost = topmostItem.Checked; Save(); };
        menu.Items.AddRange(new WinForms.ToolStripItem[] { compactItem, topmostItem });
        menu.Items.Add("Reset size", null, (_, _) => { this.settings.Scale = 1; FitToCard(); Save(); });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Hide widget", null, (_, _) => HideByUser());

        monitor.Changed += Render;
        if (demo)
        {
            demoTimer = new DispatcherTimer { Interval = DemoEvery };
            demoTimer.Tick += (_, _) => NextDemo();
            demoTimer.Start();
            NextDemo();
        }
        Loaded += (_, _) => Place();
        TopmostGuard.Attach(this);
        Closed += (_, _) => menu.Dispose();
    }

    public Mascot Mascot { set => Scene.Mascot = value; }

    /// <param name="needYou">Questions plus likely approval prompts.</param>
    /// <param name="active">Sessions worth a row, most urgent first.</param>
    public void ShowSessions(int needYou, int working, int done, IReadOnlyList<SessionItem> active, int finished)
    {
        if (demo) return;
        this.needYou = needYou;
        this.working = working;
        this.done = done;
        this.active = active;
        if (finished > 0) Scene.Celebrate();
        Render();
    }

    public void ShowWidget()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
    }

    void HideByUser()
    {
        Hide();
        settings.Visible = false;
        Save();
        VisibleChangedByUser?.Invoke(false);
    }

    void NextDemo()
    {
        var all = Demo.Scenarios(DateTime.UtcNow);
        Apply(all[demoIndex++ % all.Count]);
        Render();
    }

    void Apply(Scenario s)
    {
        int wasWorking = working;
        scenario = s;
        demoItems.Clear();
        foreach (var snap in s.Sessions)
        {
            var item = new SessionItem(snap.Key);
            item.Update(snap, DateTime.UtcNow);
            demoItems.Add(item);
        }
        needYou = demoItems.Count(i => i.Status is SessionStatus.NeedsYou or SessionStatus.Check);
        working = demoItems.Count(i => i.Status == SessionStatus.Working);
        done = demoItems.Count(i => i.Status is SessionStatus.Done or SessionStatus.Idle);
        active = demoItems.Where(i => i.Status is not (SessionStatus.Done or SessionStatus.Idle)).ToList();
        if (working < wasWorking) Scene.Celebrate();
    }

    void Render()
    {
        Scene.SessionNote = PixelScene.NoteFor(needYou, working);
        var now = DateTime.UtcNow;
        var usage = scenario?.Usage ?? monitor.Usage;
        var status = scenario?.Status ?? monitor.Status;
        string? error = scenario != null ? null : monitor.Error;
        double pct = UsageLook.Percent(usage);
        var accent = UsageLook.Accent(pct, working);

        // No data yet: let Clawd's scene and the message below speak instead of a bare "%"
        Hero.Visibility = usage == null ? Visibility.Hidden : Visibility.Visible;
        Percent.Text = Math.Round(pct).ToString("0");
        Percent.Foreground = accent;
        BarInk.Background = accent;
        BarFill.Width = new GridLength(pct, GridUnitType.Star);
        BarRest.Width = new GridLength(100 - pct, GridUnitType.Star);
        Hearts.Value = UsageLook.Hearts(usage);
        ResetText.Text = UsageLook.ResetText(usage, error, now);
        bool signIn = scenario == null && monitor.NeedsSignIn;
        SignInActions.Visibility = signIn ? Visibility.Visible : Visibility.Collapsed;
        RenewButton.IsEnabled = !monitor.RenewingSignIn;
        RenewButton.Content = monitor.RenewingSignIn ? "Refreshing…" : "Refresh sign-in";

        var rows = UsageLook.Weeks(usage, now);
        if (WeekList.ItemsSource is not IEnumerable<WeekRow> shownWeeks || !shownWeeks.SequenceEqual(rows))
            WeekList.ItemsSource = rows;

        var parts = new List<string>();
        if (needYou > 0) parts.Add($"{needYou} need{(needYou == 1 ? "s" : "")} you");
        parts.Add($"{working} working");
        if (needYou == 0) parts.Add($"{done} done");
        SessionsText.Text = string.Join("  ·  ", parts);

        var shown = active.Take(MaxRows).ToList();
        if (SessionList.ItemsSource is not IEnumerable<SessionItem> shownRows || !shownRows.SequenceEqual(shown))
            SessionList.ItemsSource = shown;
        int more = active.Count - shown.Count;
        MoreText.Text = more > 0 ? $"+{more} more  ·  open dashboard"
            : shown.Count == 0 ? "Nothing needs you right now  ·  open dashboard" : "";
        MoreText.Visibility = MoreText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        StatusDot.Fill = UsageLook.StatusBrush(status);
        StatusText.Text = status.Description;
        ErrorText.Text = usage != null ? error ?? "" : "";
        UpdatedText.Text = usage?.FetchedAt.ToLocalTime().ToString("HH:mm:ss") ?? "";

        bool night = scenario?.Night ?? UsageLook.IsNight;
        Scene.Night = night;
        Scene.Weather = UsageLook.WeatherFor(status);
        Scene.Mood = UsageLook.MoodFor(usage, needYou, working, night);
    }

    void ApplyCompact() => Details.Visibility = settings.Compact ? Visibility.Collapsed : Visibility.Visible;

    void Save() => appSettings.Save();

    // ---- Interaction -------------------------------------------------------

    void Row_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true; // a click, not a window drag

    void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SessionItem item && !demo)
            OpenSessionRequested?.Invoke(item);
    }

    void More_Click(object sender, MouseButtonEventArgs e) => DashboardRequested?.Invoke();

    void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        settings.Left = Left;
        settings.Top = Top;
        Save();
    }

    void OnMenu(object sender, RoutedEventArgs e)
    {
        var at = MenuButton.PointToScreen(new Point(0, MenuButton.ActualHeight + 2));
        menu?.Show(new System.Drawing.Point((int)at.X, (int)at.Y));
    }

    void OnDashboard(object sender, RoutedEventArgs e) => DashboardRequested?.Invoke();

    void OnGame(object sender, RoutedEventArgs e) => GameRequested?.Invoke();

    void OnRenewSignIn(object sender, RoutedEventArgs e) => _ = monitor.RenewSignIn();

    void OnSignIn(object sender, RoutedEventArgs e)
    {
        if (!ClaudeCli.OpenLogin())
            MessageBox.Show("Claude Code (claude) isn't installed on this PC, so there's nothing to sign in with.", "Claude Usage");
    }

    void OnHide(object sender, RoutedEventArgs e) => HideByUser();

    // ---- Window shape --------------------------------------------------------

    void Place()
    {
        var area = SystemParameters.WorkArea;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (settings.Left is double left && settings.Top is double top && screen.Contains(new Point(left + 40, top + 20)))
        {
            Left = left;
            Top = top;
        }
        else
        {
            Left = area.Right - ActualWidth - 16;
            Top = area.Top + 16;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Windows 11: rounded corners and a border matching the card; ignored on Windows 10
        var hwnd = new WindowInteropHelper(this).Handle;
        int round = 2, border = 0x413B3B; // DWMWCP_ROUND; COLORREF is 0x00BBGGRR
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);
    }

    /// <summary>The window is always the card times settings.Scale, so its height follows the content (compact, session rows).</summary>
    void FitToCard()
    {
        if (Card.ActualHeight <= 0) return;
        Width = CardWidth * settings.Scale;
        Height = Card.ActualHeight * settings.Scale;
    }

    /// <summary>Keeps the card's proportions while an edge or corner is dragged, so resizing scales the whole widget.</summary>
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SIZING = 0x0214, WM_EXITSIZEMOVE = 0x0232;
        if (msg == WM_SIZING && Card.ActualHeight > 0)
        {
            var r = Marshal.PtrToStructure<RECT>(lParam);
            double dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            int edge = wParam.ToInt32(); // WMSZ_: 1 left, 2 right, 3 top, 4 top-left, 5 top-right, 6 bottom, 7 bottom-left, 8 bottom-right
            double scale = edge is 3 or 6
                ? (r.Bottom - r.Top) / (Card.ActualHeight * dpi)
                : (r.Right - r.Left) / (CardWidth * dpi);
            settings.Scale = Math.Clamp(scale, MinScale, MaxScale);

            int w = (int)Math.Round(CardWidth * settings.Scale * dpi);
            int h = (int)Math.Round(Card.ActualHeight * settings.Scale * dpi);
            if (edge is 1 or 4 or 7) r.Left = r.Right - w; else r.Right = r.Left + w;
            if (edge is 3 or 4 or 5) r.Top = r.Bottom - h; else r.Bottom = r.Top + h;
            Marshal.StructureToPtr(r, lParam, false);
            handled = true;
            return (IntPtr)1;
        }
        if (msg == WM_EXITSIZEMOVE)
        {
            settings.Left = Left;
            settings.Top = Top;
            Save();
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---- --snapshot -----------------------------------------------------------

    /// <summary>Renders each demo state (and a compact one) to PNG, off-screen.</summary>
    public void SaveSnapshots(string dir)
    {
        Directory.CreateDirectory(dir);
        Left = Top = -10000;
        Show();
        var all = Demo.Scenarios(DateTime.UtcNow);
        for (int i = 0; i < all.Count; i++)
        {
            Apply(all[i]);
            Render();
            UpdateLayout();
            Scene.Advance(i == 0 ? 2.5 : 1.3);
            UpdateLayout(); // flushes the scene's OnRender before capturing
            SavePng(Card, Path.Combine(dir, $"{i + 1}-{all[i].Name}.png"));
        }
        settings.Compact = true;
        ApplyCompact();
        Apply(all[1]);
        Render();
        UpdateLayout();
        Scene.Advance(0.6);
        UpdateLayout();
        SavePng(Card, Path.Combine(dir, $"{all.Count + 1}-compact.png"));
        Close();
    }

    /// <summary>For --signatures: the scene playing the mascot's own move, one PNG per frame (12 a second).</summary>
    public void SaveSignatureFrames(string dir)
    {
        Directory.CreateDirectory(dir);
        Left = Top = -10000;
        Show();
        Apply(Demo.Scenarios(DateTime.UtcNow)[0]);
        Render();
        UpdateLayout();
        Scene.Advance(1);
        Scene.PlaySignature();
        int frames = (int)Math.Ceiling(Scene.Mascot.SignatureLength * 12) + 10;
        for (int i = 0; i < frames; i++)
        {
            Scene.Advance(1 / 12.0);
            UpdateLayout();
            SavePng(Scene, Path.Combine(dir, $"{i:000}.png"));
        }
        Close();
    }

    /// <summary>For --stories: the scene playing the mascot's story, one PNG per frame (12 a second).</summary>
    public void SaveStoryFrames(string dir)
    {
        Directory.CreateDirectory(dir);
        Left = Top = -10000;
        Show();
        Apply(Demo.Scenarios(DateTime.UtcNow)[0]);
        Render();
        UpdateLayout();
        Scene.Advance(1);
        Scene.PlayStory();
        for (int i = 0; i < 12 * 15 && (Scene.PlayingStory || i < 6); i++)
        {
            Scene.Advance(1 / 12.0);
            UpdateLayout();
            SavePng(Scene, Path.Combine(dir, $"{i:000}.png"));
        }
        Close();
    }

    internal static void SavePng(FrameworkElement element, string path)
    {
        const double scale = 2;
        var size = new Size(element.ActualWidth, element.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(size));
        var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
    }
}
