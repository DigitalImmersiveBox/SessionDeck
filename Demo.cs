using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeSessions;

sealed record Scenario(string Name, UsageReport Usage, IReadOnlyList<SessionSnapshot> Sessions, ServiceStatus Status, bool Night);

sealed record DashboardDemo(IReadOnlyList<ProjectEntry> Projects, IReadOnlyList<SessionSnapshot> Sessions, TodayStats Today,
                            UsageReport Usage, ServiceStatus Status, IReadOnlyList<UsagePoint> History, WeatherNow Weather);

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

    const string Root = @"D:\Code\";

    /// <summary>A full dashboard of made-up projects and sessions for --dashboard (the README screenshot).</summary>
    public static DashboardDemo Dashboard(DateTime now)
    {
        var projects = new[] { "api-server", "portfolio-site", "pixel-game", "weather-app", "docs" }
            .Select((name, i) => new ProjectEntry(name, Root + name, now.AddMinutes(-i * 9)))
            .ToList();

        SessionSnapshot Card(string title, string project, string? branch, SessionStatus status, string statusText,
                             string? activity, string? detail, PlanStep[] plan, int minutesAgo, params string[] mcps) =>
            new($"dash-{title}", title, project, Root + project, branch, status, statusText, activity, detail,
                plan, now.AddMinutes(-minutesAgo), null, null, Mcps: mcps);

        var sessions = new[]
        {
            Card("Add a dark mode toggle", "portfolio-site", "feature/dark-mode", SessionStatus.NeedsYou, "Needs you",
                 "Should the toggle follow the system theme by default, or always start in light mode?",
                 "Wire the toggle into the header", Steps(1, "Add theme colours", "Wire the toggle into the header", "Remember the choice", "Test both themes"), 1),
            Card("Migrate the database to Postgres", "api-server", "db-migration", SessionStatus.Check, "Approval?",
                 "npm run migrate has been running for 3m. It may be waiting for your approval.",
                 "Run the migration", Steps(3, "Write the schema", "Port the queries", "Update the config", "Run the migration", "Check the data"), 3),
            Card("Fix the flaky login test", "api-server", "fix/flaky-login", SessionStatus.Working, "Working",
                 "Run the test suite 20 times", "Bash: npm test -- --repeat 20",
                 Steps(2, "Reproduce the failure", "Fix the race in the session store", "Run the test suite 20 times"), 0, "github"),
            Card("Boss fight for level 3", "pixel-game", "main", SessionStatus.Working, "Working",
                 "Tune the boss's attack timing", "Edit: BossController.cs",
                 Steps(3, "Sketch the attack pattern", "Add the boss sprite", "Spawn it at the end of level 3", "Tune the boss's attack timing", "Add a health bar", "Playtest"), 0, "blender", "context7"),
            Card("Hourly forecast chart", "weather-app", "hourly-chart", SessionStatus.YourTurn, "Your turn",
                 "Added an hourly chart under the current conditions, with the chance of rain as bars. All 42 tests pass.",
                 null, Steps(3, "Fetch hourly data", "Draw the chart", "Add tests"), 6, "context7"),
            Card("Refresh the README screenshots", "docs", null, SessionStatus.YourTurn, "Your turn",
                 "Replaced the four screenshots and fixed two broken links.", null, Array.Empty<PlanStep>(), 18),
            Card("Speed up image loading", "portfolio-site", "perf/images", SessionStatus.Done, "Done",
                 "Images now load lazily as WebP. The home page went from 2.1 MB to 640 KB.", null, Array.Empty<PlanStep>(), 64),
            Card("Rate-limit the public API", "api-server", "main", SessionStatus.Done, "Done",
                 "Added a limit of 60 requests a minute per key, with a Retry-After header.", null, Array.Empty<PlanStep>(), 190),
        };

        var today = new TodayStats(9, 64, new TimeSpan(4, 12, 0),
            new[] { ("api-server", 3), ("portfolio-site", 2), ("pixel-game", 2), ("weather-app", 1), ("docs", 1) });

        // The previous 5-hour window, a break, then this one, read every 2 minutes like the real thing
        var history = new List<UsagePoint>();
        for (int m = 0; m <= 260; m += 2)
            history.Add(new(now.AddMinutes(-660 + m), 72.0 * m / 260));
        double percent = 0;
        for (int m = 156; m >= 0; m -= 2)
        {
            history.Add(new(now.AddMinutes(-m), Math.Round(percent, 1)));
            percent += m % 40 < 24 ? 0.7 : 0.1;
        }
        double session = history[^1].Percent;

        var weekReset = now + new TimeSpan(3, 5, 0, 0);
        var usage = new UsageReport(
            new Limit("five_hour", "SESSION", session, now + new TimeSpan(2, 20, 30)),
            new[]
            {
                new Limit("seven_day", "WEEK", 38, weekReset),
                new Limit("seven_day_fable", "FABLE · WEEK", 17, weekReset),
            },
            now);
        var weather = new WeatherNow("Lisbon", 21.4, 1, true, 23, 15, 10, now);
        return new DashboardDemo(projects, sessions, today, usage, Ok, history, weather);
    }

    /// <summary>A plan with the first <paramref name="done"/> steps completed and the next one in progress.</summary>
    static PlanStep[] Steps(int done, params string[] steps) =>
        steps.Select((s, i) => new PlanStep(s, i < done ? "completed" : i == done ? "in_progress" : "pending")).ToArray();

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
