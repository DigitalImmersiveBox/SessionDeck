using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClaudeSessions;

/// <summary>
/// Puts an always-on-top window back on top when Windows drops it from the topmost layer (Explorer restarting,
/// sleep or lock, a display change, another app's SetWindowPos). WPF's Topmost still reads true then, so setting
/// it again does nothing; this checks the real window style every couple of seconds while the window shows.
/// </summary>
static class TopmostGuard
{
    static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(2);

    public static void Attach(Window window)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = CheckEvery };
        timer.Tick += (_, _) => Restore(window);
        window.IsVisibleChanged += (_, _) =>
        {
            if (window.IsVisible) { Restore(window); timer.Start(); }
            else timer.Stop();
        };
        window.StateChanged += (_, _) => Restore(window);
        window.Closed += (_, _) => timer.Stop();
        if (window.IsVisible) timer.Start();
    }

    static void Restore(Window window)
    {
        if (!window.Topmost || !window.IsVisible || window.WindowState == WindowState.Minimized) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOPMOST = 0x8;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
