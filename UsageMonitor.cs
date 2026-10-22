using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClaudeSessions;

/// <summary>
/// Fetches plan usage (every 2 min) and Claude's status page (every 5 min) once for the whole app;
/// the usage widget and the dashboard banner both draw from it. Ticks every second so countdowns stay live.
/// When the login has expired it asks Claude Code to renew it, then fetches again.
/// </summary>
public sealed class UsageMonitor
{
    static readonly TimeSpan UsageEvery = TimeSpan.FromMinutes(2);
    static readonly TimeSpan StatusEvery = TimeSpan.FromMinutes(5);
    static readonly TimeSpan AutoRenewEvery = TimeSpan.FromMinutes(10);

    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    DateTime nextUsage, nextStatus, lastAutoRenew;
    bool fetchingUsage, fetchingStatus;

    public UsageReport? Usage { get; private set; }
    public UsageHistory History { get; } = new();
    public string? Error { get; private set; }
    /// <summary>The last fetch failed because of the login; the widget offers Refresh sign-in / Sign in.</summary>
    public bool NeedsSignIn { get; private set; }
    public bool RenewingSignIn { get; private set; }
    public ServiceStatus Status { get; private set; } = new(ServiceHealth.Unknown, "Checking Claude status…");

    /// <summary>Raised every second and after each fetch.</summary>
    public event Action? Changed;

    public UsageMonitor() => timer.Tick += (_, _) => Tick();

    public void Start()
    {
        timer.Start();
        Tick();
    }

    public void Stop() => timer.Stop();

    public void RefreshNow() => nextUsage = nextStatus = DateTime.MinValue;

    /// <summary>Lets Claude Code renew its own sign-in, then checks usage again straight away.</summary>
    public async Task RenewSignIn()
    {
        if (RenewingSignIn) return;
        RenewingSignIn = true;
        Changed?.Invoke();
        bool ok = false;
        try { ok = await ClaudeCli.RenewSignInAsync(); }
        catch { }
        RenewingSignIn = false;
        if (ok) RefreshNow();
        else Error = ClaudeCli.Find() == null
            ? "Claude Code (claude) isn't installed on this PC."
            : "Couldn't refresh the sign-in. Use Sign in to log in again.";
        Changed?.Invoke();
    }

    void Tick()
    {
        var now = DateTime.UtcNow;
        if (now >= nextUsage && !fetchingUsage) _ = FetchUsage();
        if (now >= nextStatus && !fetchingStatus) _ = FetchStatus();
        Changed?.Invoke();
    }

    async Task FetchUsage()
    {
        fetchingUsage = true;
        try
        {
            Usage = await UsageClient.FetchAsync();
            History.Add(Usage);
            Error = null;
            NeedsSignIn = false;
            nextUsage = DateTime.UtcNow + UsageEvery;
        }
        catch (UsageException ex)
        {
            Error = ex.Message;
            NeedsSignIn = ex.SignIn;
            nextUsage = DateTime.UtcNow + ex.RetryAfter;
            // Renew on our own first (at most every 10 min); the buttons stay as the fallback if it fails
            if (ex.SignIn && DateTime.UtcNow - lastAutoRenew > AutoRenewEvery)
            {
                lastAutoRenew = DateTime.UtcNow;
                _ = RenewSignIn();
            }
        }
        catch
        {
            Error = "Can't reach Claude right now";
            nextUsage = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        }
        finally
        {
            fetchingUsage = false;
            Changed?.Invoke();
        }
    }

    async Task FetchStatus()
    {
        fetchingStatus = true;
        try { Status = await UsageClient.FetchStatusAsync(); }
        catch { Status = new(ServiceHealth.Unknown, "Claude status unavailable"); }
        finally
        {
            nextStatus = DateTime.UtcNow + StatusEvery;
            fetchingStatus = false;
            Changed?.Invoke();
        }
    }
}

public sealed record WeekRow(string Label, string Figure, GridLength Fill, GridLength Rest);

/// <summary>How usage and sessions turn into colours, hearts, weather and Clawd's mood; shared by the widget and the banner.</summary>
static class UsageLook
{
    public static readonly Brush Green = Ink.Of(0x4A, 0xDE, 0x80);
    public static readonly Brush Blue = Ink.Of(0x7C, 0xC4, 0xE8);
    public static readonly Brush Amber = Ink.Of(0xF5, 0xB8, 0x3D);
    public static readonly Brush Red = Ink.Of(0xF0, 0x71, 0x71);
    public static readonly Brush Gray = Ink.Of(0x6B, 0x70, 0x7B);

    public static double Percent(UsageReport? usage) => Math.Clamp(usage?.Session?.Percent ?? 0, 0, 100);

    public static Brush Accent(double pct, int working) =>
        pct >= 90 ? Red : pct >= 70 ? Amber : working > 0 ? Blue : Green;

    /// <summary>Three hearts for the 5-hour limit left, in half-heart steps.</summary>
    public static double Hearts(UsageReport? usage) =>
        usage == null ? 3 : Math.Ceiling(3 * (1 - Percent(usage) / 100) * 2) / 2;

    public static string ResetText(UsageReport? usage, string? error, DateTime now) =>
        usage == null ? error ?? "Loading usage…"
        : usage.Session?.ResetsAt is { } reset ? "resets in " + Until(reset, now)
        : usage.Session == null ? "No 5-hour limit on this plan"
        : "5-hour window starts with your next message";

    public static List<WeekRow> Weeks(UsageReport? usage, DateTime now) =>
        (usage?.Weekly ?? Array.Empty<Limit>())
            .Select(l => new WeekRow(l.Label,
                l.ResetsAt is { } r ? $"{l.Percent:0}%  ·  {Until(r, now)}" : $"{l.Percent:0}%",
                new GridLength(Math.Clamp(l.Percent, 0, 100), GridUnitType.Star),
                new GridLength(100 - Math.Clamp(l.Percent, 0, 100), GridUnitType.Star)))
            .ToList();

    public static Brush StatusBrush(ServiceStatus status) => status.Health switch
    {
        ServiceHealth.Operational => Green,
        ServiceHealth.Minor => Amber,
        ServiceHealth.Major or ServiceHealth.Critical => Red,
        _ => Gray,
    };

    public static Weather WeatherFor(ServiceStatus status) => status.Health switch
    {
        ServiceHealth.Minor => Weather.Rain,
        ServiceHealth.Major or ServiceHealth.Critical => Weather.Storm,
        _ => Weather.Clear,
    };

    public static bool IsNight => DateTime.Now.Hour is >= 20 or < 6;

    /// <param name="needYou">Sessions with a question or a likely approval prompt.</param>
    public static Mood MoodFor(UsageReport? usage, int needYou, int working, bool night) =>
        usage != null && Percent(usage) >= 100 ? Mood.Maxed
        : needYou > 0 ? Mood.Waiting
        : working > 0 ? Mood.Working
        : night ? Mood.Sleeping
        : Mood.Idle;

    public static string Until(DateTime utc, DateTime now)
    {
        var span = utc - now;
        if (span <= TimeSpan.Zero) return "now";
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))}m";
    }
}
