using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeSessions;

sealed record Scenario(string Name, UsageReport Usage, IReadOnlyList<SessionSnapshot> Sessions, ServiceStatus Status, bool Night);

/// <summary>Made-up widget states for --demo and --snapshot, one per mood and weather. Never touches your login.</summary>
static class Demo
{
    static readonly ServiceStatus Ok = new(ServiceHealth.Operational, "All Claude systems operational");

    public static IReadOnlyList<Scenario> Scenarios(DateTime now) => new[]
    {
        Make("day", 34, 41, 18, "dd", Ok, false, now),
        Make("working", 10, 22, 6, "wwd", Ok, false, now),
        Make("waiting", 58, 47, 21, "nwyd", Ok, false, now),
        Make("maxed", 100, 88, 64, "d", Ok, false, now),
        Make("night", 12, 30, 9, "d", Ok, true, now),
        Make("rain", 27, 35, 12, "wd", new(ServiceHealth.Minor, "Elevated errors on Claude Opus"), false, now),
        Make("storm", 73, 66, 40, "nwwyy", new(ServiceHealth.Major, "Partial outage: claude.ai"), true, now),
    };

    static readonly (string Title, string Project)[] Titles =
    {
        ("Fix login redirect loop", "dib_Website"),
        ("Usage widget polish", "Claude_Helper"),
        ("SEO audit for product pages", "dib_Website"),
        ("Kineto calibration scene", "VR_Kineto"),
        ("Sterilization report export", "Medisync"),
    };

    /// <param name="sessions">One letter per session: n = needs you, w = working, y = your turn, d = done.</param>
    static Scenario Make(string name, double session, double week, double model, string sessions,
                         ServiceStatus status, bool night, DateTime now)
    {
        var weekReset = now + new TimeSpan(2, 9, 0, 30);
        var usage = new UsageReport(
            new Limit("five_hour", "SESSION", session, now + new TimeSpan(3, 11, 30)),
            new[]
            {
                new Limit("seven_day", "WEEK", week, weekReset),
                new Limit("seven_day_fable", "FABLE · WEEK", model, weekReset),
            },
            now);
        var snaps = sessions.Select((kind, i) => Session(kind, i, now)).ToList();
        return new Scenario(name, usage, snaps, status, night);
    }

    static SessionSnapshot Session(char kind, int i, DateTime now)
    {
        var (title, project) = Titles[i % Titles.Length];
        var (status, text) = kind switch
        {
            'n' => (SessionStatus.NeedsYou, "Needs you"),
            'w' => (SessionStatus.Working, "Working"),
            'y' => (SessionStatus.YourTurn, "Your turn"),
            _ => (SessionStatus.Done, "Done"),
        };
        var plan = kind == 'w'
            ? new[] { new PlanStep("Read", "completed"), new PlanStep("Build", "completed"), new PlanStep("Test", "in_progress"), new PlanStep("Ship", "pending") }
            : Array.Empty<PlanStep>();
        return new SessionSnapshot($"demo{i}", title, project, "", null, status, text,
            kind == 'n' ? "Which login provider should I keep?" : null, null, plan, now.AddMinutes(-i * 7), null, null);
    }
}
