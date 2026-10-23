using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ClaudeSessions;

public sealed record UsagePoint(DateTime At, double Percent);

/// <summary>
/// Every 5-hour usage reading (one per fetch, every 2 min), kept for two days in
/// %APPDATA%\SessionDeck\usage-history.json. Feeds the pace forecast.
/// </summary>
public sealed class UsageHistory
{
    static readonly TimeSpan Keep = TimeSpan.FromDays(2);
    static readonly TimeSpan PaceWindow = TimeSpan.FromMinutes(30);
    static readonly TimeSpan MinPaceSpan = TimeSpan.FromMinutes(8);

    static string FilePath => Path.Combine(Settings.DataDir, "usage-history.json");

    readonly List<UsagePoint> points;

    public UsageHistory()
    {
        try { points = JsonSerializer.Deserialize<List<UsagePoint>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { points = new(); }
    }

    /// <summary>Made-up readings for --dashboard: not loaded from disk (and never added to, so never saved).</summary>
    public UsageHistory(IEnumerable<UsagePoint> readings) => points = readings.ToList();

    public void Add(UsageReport report)
    {
        if (report.Session is not { } session) return;
        points.Add(new UsagePoint(report.FetchedAt, session.Percent));
        points.RemoveAll(p => report.FetchedAt - p.At > Keep);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(points));
        }
        catch { }
    }

    /// <summary>
    /// "At this pace…" from the last 30 minutes of the current window (readings after the last reset).
    /// Warning is true when the limit would be hit before the window resets.
    /// </summary>
    public (string Text, bool Warning) Forecast(UsageReport? usage, DateTime now)
    {
        if (usage?.Session is not { } session) return ("", false);
        if (session.ResetsAt is not { } reset) return ("Next message starts a new 5-hour window", false);
        string resetClock = reset.ToLocalTime().ToString("HH:mm");

        var recent = points.Where(p => now - p.At <= PaceWindow).ToList();
        int start = 0; // readings after the last reset (a drop in %) belong to this window
        for (int i = 1; i < recent.Count; i++)
            if (recent[i].Percent < recent[i - 1].Percent - 1) start = i;
        recent = recent.Skip(start).ToList();
        if (recent.Count < 3 || recent[^1].At - recent[0].At < MinPaceSpan)
            return ($"Resets at {resetClock} · measuring your pace…", false);

        double minutes = (recent[^1].At - recent[0].At).TotalMinutes;
        double rate = (recent[^1].Percent - recent[0].Percent) / minutes; // % per minute
        if (rate < 0.02) return ($"Quiet pace · resets at {resetClock}", false);

        var limitAt = now.AddMinutes((100 - session.Percent) / rate);
        if (limitAt < reset)
            return ($"At this pace: limit at {limitAt.ToLocalTime():HH:mm} (reset {resetClock})", true);
        double atReset = Math.Min(100, session.Percent + rate * (reset - now).TotalMinutes);
        return ($"At this pace: ~{atReset:0}% by reset at {resetClock}", false);
    }
}
