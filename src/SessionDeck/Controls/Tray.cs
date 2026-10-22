using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace ClaudeSessions;

/// <summary>System-tray icon: coloured badge with the "needs you" count, menu, and pop-up notifications.</summary>
sealed class Tray : IDisposable
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "SessionDeck";
    const string OldRunValue = "ClaudeSessions"; // the app's name before it became Session Deck

    readonly WinForms.NotifyIcon icon = new();
    readonly WinForms.ToolStripMenuItem widgetItem, topmostItem, notifyItem, startupItem, mascotItem;
    IntPtr iconHandle;
    string? balloonLink;
    MascotKind mascot;

    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action<bool>? TopmostChanged;
    public event Action<bool>? NotificationsChanged;
    public event Action<bool>? WidgetChanged;
    public event Action<MascotKind>? MascotChanged;
    public event Action<string>? LinkClicked;

    public Tray(bool topmost, bool notifications, bool widget, MascotKind mascot)
    {
        this.mascot = mascot;
        var menu = DarkMenu.Create();
        menu.Items.Add("Open dashboard", null, (_, _) => ShowRequested?.Invoke());
        widgetItem = new WinForms.ToolStripMenuItem("Usage widget") { CheckOnClick = true, Checked = widget };
        widgetItem.Click += (_, _) => WidgetChanged?.Invoke(widgetItem.Checked);
        menu.Items.Add(widgetItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        topmostItem = new WinForms.ToolStripMenuItem("Always on top") { CheckOnClick = true, Checked = topmost };
        topmostItem.Click += (_, _) => TopmostChanged?.Invoke(topmostItem.Checked);
        notifyItem = new WinForms.ToolStripMenuItem("Notifications") { CheckOnClick = true, Checked = notifications };
        notifyItem.Click += (_, _) => NotificationsChanged?.Invoke(notifyItem.Checked);
        MigrateStartup();
        startupItem = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = StartsWithWindows };
        startupItem.Click += (_, _) => StartsWithWindows = startupItem.Checked;
        mascotItem = new WinForms.ToolStripMenuItem("Mascot");
        foreach (var (kind, label) in ClaudeSessions.Mascot.Choices)
        {
            var item = new WinForms.ToolStripMenuItem(label) { Tag = kind, Checked = kind == mascot };
            item.Click += (_, _) => MascotChanged?.Invoke(kind);
            mascotItem.DropDownItems.Add(item);
        }
        var mascotMenu = (WinForms.ToolStripDropDownMenu)mascotItem.DropDown;
        mascotMenu.Renderer = menu.Renderer;
        mascotMenu.ShowImageMargin = false;
        mascotMenu.ShowCheckMargin = true;
        menu.Items.AddRange(new WinForms.ToolStripItem[] { topmostItem, notifyItem, startupItem, mascotItem });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        icon.ContextMenuStrip = menu;
        icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) ShowRequested?.Invoke(); };
        icon.BalloonTipClicked += (_, _) =>
        {
            if (balloonLink != null) LinkClicked?.Invoke(balloonLink);
            else ShowRequested?.Invoke();
        };
        Update(0, 0, "Session Deck");
        icon.Visible = true;
    }

    public bool Topmost { set => topmostItem.Checked = value; }

    public bool Widget { set => widgetItem.Checked = value; }

    /// <summary>Ticks the mascot in the menu and redraws the icon with it.</summary>
    public MascotKind Mascot
    {
        set
        {
            mascot = value;
            foreach (WinForms.ToolStripMenuItem item in mascotItem.DropDownItems)
                item.Checked = (MascotKind)item.Tag! == value;
            DrawIcon();
        }
    }

    int shownNeedYou = -1;
    bool shownWorking;

    /// <summary>The pixel mascot with a status badge, drawn at the tray's own icon size so it stays crisp.</summary>
    public void Update(int needYou, int working, string tooltip)
    {
        icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (needYou == shownNeedYou && working > 0 == shownWorking) return;
        shownNeedYou = needYou;
        shownWorking = working > 0;
        DrawIcon();
    }

    void DrawIcon()
    {
        var old = iconHandle;
        iconHandle = MascotIcon.Handle(ClaudeSessions.Mascot.For(mascot), WinForms.SystemInformation.SmallIconSize.Width,
            Math.Max(0, shownNeedYou), shownWorking);
        icon.Icon = Icon.FromHandle(iconHandle);
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    public void Notify(string title, string text, string? link)
    {
        balloonLink = link;
        icon.ShowBalloonTip(6000, Clip(title, 63), Clip(string.IsNullOrWhiteSpace(text) ? " " : text, 255), WinForms.ToolTipIcon.None);
    }

    static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";


    static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) != null;
        }
        set
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    /// <summary>Moves a "Start with Windows" entry left by the old ClaudeSessions.exe over to SessionDeck.exe, in the same folder.</summary>
    static void MigrateStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(OldRunValue) is not string old) return;
        if (key.GetValue(RunValue) == null)
            key.SetValue(RunValue, old.Replace(@"\ClaudeSessions.exe", @"\SessionDeck.exe", StringComparison.OrdinalIgnoreCase));
        key.DeleteValue(OldRunValue, throwOnMissingValue: false);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}
