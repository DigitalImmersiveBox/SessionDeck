using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace ClaudeSessions;

/// <summary>
/// The mascot walking along the top of the taskbar on the main monitor: the banner's scene minus the sky, as
/// wide as the screen. The see-through window only shows the part of it around him and moves along with him,
/// since a screen-wide one costs a lot to redraw every frame. Only his own pixels catch the mouse (click him
/// for a trick, double-click for the dashboard, drag him about, right-click for a menu); clicks anywhere else
/// go to the windows underneath.
/// He steps off while a full-screen app, game or presentation is running.
/// </summary>
sealed class TaskbarMascot : Window
{
    const double StripHeight = 104; // as tall as the banner, so his stories have the same room
    const double ViewWidth = 480;   // how much of the strip the window shows around him
    const double EdgeRoom = 120;    // the window moves to center on him again once he's this close to its edge
    static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(2);

    readonly DispatcherTimer fullScreenCheck = new(DispatcherPriority.Background) { Interval = CheckEvery };
    readonly WinForms.ContextMenuStrip menu;
    readonly Canvas view = new() { ClipToBounds = true };
    double stripLeft, stripWidth, offset;

    public PixelScene Scene { get; } = new() { Bare = true };

    public event Action? DashboardRequested;
    /// <summary>"Hide from taskbar" in his menu; the dashboard turns the setting off.</summary>
    public event Action? HideRequested;

    public TaskbarMascot(Mascot mascot)
    {
        Title = "Session Deck mascot";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true; // per-pixel alpha: the empty parts of the strip let clicks through
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        UseLayoutRounding = true;
        Scene.Mascot = mascot;
        Scene.Advanced += () => Follow(force: false);
        Scene.DoubleClicked += () => DashboardRequested?.Invoke();
        view.Children.Add(Scene);
        Content = view;

        menu = DarkMenu.Create();
        menu.Items.Add("Open dashboard", null, (_, _) => DashboardRequested?.Invoke());
        menu.Items.Add("Hide from taskbar", null, (_, _) => HideRequested?.Invoke());

        SourceInitialized += (_, _) =>
        {
            // Not in Alt+Tab, and clicking him doesn't take the focus from the window you're typing in
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        Place();
        SystemParameters.StaticPropertyChanged += OnSystemChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        fullScreenCheck.Tick += (_, _) => FollowFullScreen();
        fullScreenCheck.Start();
        TopmostGuard.Attach(this);
        Closed += (_, _) =>
        {
            fullScreenCheck.Stop();
            SystemParameters.StaticPropertyChanged -= OnSystemChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            menu.Dispose();
        };
    }

    /// <summary>Along the bottom of the main monitor's work area, which is the top of the taskbar when it's at the bottom.</summary>
    void Place()
    {
        var area = SystemParameters.WorkArea;
        stripLeft = area.Left;
        stripWidth = area.Width;
        Scene.Width = stripWidth;
        Scene.Height = StripHeight;
        Top = area.Bottom - StripHeight;
        Width = Math.Min(ViewWidth, stripWidth);
        Height = StripHeight;
        Follow(force: true);
    }

    /// <summary>Keeps the window over him; it jumps rather than gliding, so it moves every few seconds instead of every frame.</summary>
    void Follow(bool force)
    {
        double center = Scene.MascotCenter;
        if (double.IsNaN(center)) center = stripWidth * 0.62; // where the scene first puts him
        if (!force && Math.Abs(center - (offset + Width / 2)) < Width / 2 - EdgeRoom) return;
        offset = Math.Clamp(Math.Round(center - Width / 2), 0, Math.Max(0, stripWidth - Width));
        Canvas.SetLeft(Scene, -offset);
        Left = stripLeft + offset;
    }

    void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea)) Place();
    }

    void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Place);

    /// <summary>Hides while something full-screen has the main monitor (as the taskbar does), and comes back after.</summary>
    void FollowFullScreen()
    {
        bool fullScreen = CoversMainMonitor(GetForegroundWindow())
                          || (SHQueryUserNotificationState(out int state) == 0 && state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE);
        if (fullScreen && IsVisible) Hide();
        else if (!fullScreen && !IsVisible) Show();
    }

    /// <summary>
    /// The window fills the main monitor, taskbar and all. Windows' own "busy" state isn't used for this:
    /// it also says busy for a full-screen app on another monitor, and stays so for a while after some close.
    /// </summary>
    static bool CoversMainMonitor(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == GetShellWindow() || !GetWindowRect(hwnd, out var r)) return false;
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONULL), ref info) || (info.Flags & MONITORINFOF_PRIMARY) == 0) return false;
        // The desktop behind the icons is as big as the screen too
        var name = new System.Text.StringBuilder(32);
        GetClassName(hwnd, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW") return false;
        var m = info.Monitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        menu.Show(WinForms.Cursor.Position);
        e.Handled = true;
    }

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x80;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;
    const uint MONITOR_DEFAULTTONULL = 0, MONITORINFOF_PRIMARY = 1;

    struct RECT { public int Left, Top, Right, Bottom; }

    struct MONITORINFO
    {
        public int Size;
        public RECT Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int size);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("shell32.dll")]
    static extern int SHQueryUserNotificationState(out int state);
}
