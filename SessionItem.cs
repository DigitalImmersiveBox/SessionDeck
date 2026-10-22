using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ClaudeSessions;

/// <summary>One card in the dashboard; updated in place from each scan.</summary>
public sealed class SessionItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public SessionItem(string key) => Key = key;

    public string Key { get; }
    public string Title { get => title; private set => Set(ref title, value); }
    public string Project { get => project; private set => Set(ref project, value); }
    public string Cwd { get; private set; } = "";
    public string? Link { get; private set; }
    public string? Branch { get => branch; private set { Set(ref branch, value); Raise(nameof(HasBranch)); } }
    public bool HasBranch => !string.IsNullOrEmpty(branch);
    public SessionStatus Status { get => status; private set => Set(ref status, value); }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public Brush StatusBrush => Palette.For(status);
    public double CardOpacity => status is SessionStatus.Done or SessionStatus.Idle ? 0.62 : 1.0;
    public int Rank => (int)status;
    public bool CanMarkSeen => status == SessionStatus.YourTurn;
    public bool CanDismiss { get => canDismiss; private set => Set(ref canDismiss, value); }
    public DateTime LastActivity { get => lastActivity; private set => Set(ref lastActivity, value); }
    public string AgoText { get => agoText; private set => Set(ref agoText, value); }
    public string? Activity { get => activity; private set { Set(ref activity, value); Raise(nameof(HasActivity)); } }
    public bool HasActivity => !string.IsNullOrEmpty(activity);
    public string? Detail { get => detail; private set { Set(ref detail, value); Raise(nameof(HasDetail)); } }
    public bool HasDetail => !string.IsNullOrEmpty(detail);
    public bool HasPlan { get => hasPlan; private set => Set(ref hasPlan, value); }
    public double PlanProgress { get => planProgress; private set => Set(ref planProgress, value); }
    public string PlanText { get => planText; private set => Set(ref planText, value); }
    public string Tooltip { get => tooltip; private set => Set(ref tooltip, value); }
    /// <summary>MCP servers the session used, most used first; past five the rest fold into "+N".</summary>
    public IReadOnlyList<string> McpChips { get => mcpChips; private set { Set(ref mcpChips, value); Raise(nameof(HasMcps)); } }
    public bool HasMcps => mcpChips.Count > 0;
    /// <summary>The icon of the project this session runs in; set by the window, not by a scan.</summary>
    public ImageSource? ProjectIcon { get => projectIcon; set { Set(ref projectIcon, value); Raise(nameof(HasProjectIcon)); } }
    public bool HasProjectIcon => projectIcon != null;
    /// <summary>An icon file (not a generated tile): drawn on a dark tile so it shows on the status colour.</summary>
    public bool ProjectIconBacked { get => projectIconBacked; set => Set(ref projectIconBacked, value); }

    const int MaxChips = 5;

    public void Update(SessionSnapshot s, DateTime now)
    {
        Title = s.Title;
        Project = s.Project;
        Cwd = s.Cwd;
        Link = s.Link;
        Branch = s.Branch;
        if (status != s.Status)
        {
            Status = s.Status;
            Raise(nameof(StatusBrush));
            Raise(nameof(CardOpacity));
            Raise(nameof(Rank));
            Raise(nameof(CanMarkSeen));
        }
        CanDismiss = s.Dismissable;
        StatusText = s.StatusText;
        LastActivity = s.LastActivity;
        AgoText = Ago(now - s.LastActivity);
        Activity = s.Activity;
        Detail = s.Detail;

        int total = s.Plan.Count, completed = s.Plan.Count(p => p.Status == "completed");
        HasPlan = total > 0;
        PlanProgress = total > 0 ? (double)completed / total : 0;
        PlanText = total > 0 ? $"{completed}/{total}" : "";

        var mcps = s.Mcps ?? Array.Empty<string>();
        var chips = mcps.Count <= MaxChips ? mcps.ToList()
            : mcps.Take(MaxChips - 1).Append($"+{mcps.Count - MaxChips + 1}").ToList();
        if (!chips.SequenceEqual(mcpChips)) McpChips = chips; // a new list each scan would rebuild the chips

        var lines = new List<string> { s.Title, s.Cwd };
        if (total > 0)
        {
            lines.Add("");
            lines.AddRange(s.Plan.Select(p => (p.Status switch { "completed" => "✓ ", "in_progress" => "▶ ", _ => "○ " }) + p.Content));
        }
        if (mcps.Count > 0)
        {
            lines.Add("");
            lines.Add("MCPs: " + string.Join(", ", mcps));
        }
        lines.Add("");
        lines.Add(s.Link != null ? "Click to open in Claude · right-click for more" : "Terminal session (no Claude desktop link)");
        Tooltip = string.Join("\n", lines);
    }

    static string Ago(TimeSpan span) =>
        span.TotalSeconds < 60 ? "now"
        : span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes}m ago"
        : span.TotalHours < 24 ? $"{(int)span.TotalHours}h ago"
        : $"{(int)span.TotalDays}d ago";

    string title = "", project = "", statusText = "", agoText = "", planText = "", tooltip = "";
    string? branch, activity, detail;
    IReadOnlyList<string> mcpChips = Array.Empty<string>();
    ImageSource? projectIcon;
    SessionStatus status = (SessionStatus)(-1);
    DateTime lastActivity;
    bool hasPlan, canDismiss, projectIconBacked;
    double planProgress;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name!);
    }

    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

static class Palette
{
    public static readonly Color NeedsYou = Color.FromRgb(0xFF, 0x7A, 0x59);
    public static readonly Color Check = Color.FromRgb(0xF5, 0xB8, 0x3D);
    public static readonly Color YourTurn = Color.FromRgb(0x4A, 0xDE, 0x80);
    public static readonly Color Working = Color.FromRgb(0x60, 0xA5, 0xFA);
    public static readonly Color Done = Color.FromRgb(0x6B, 0x70, 0x7B);
    public static readonly Color Idle = Color.FromRgb(0x4A, 0x4E, 0x57);

    static readonly Dictionary<SessionStatus, Brush> brushes = new()
    {
        [SessionStatus.NeedsYou] = Frozen(NeedsYou),
        [SessionStatus.Check] = Frozen(Check),
        [SessionStatus.YourTurn] = Frozen(YourTurn),
        [SessionStatus.Working] = Frozen(Working),
        [SessionStatus.Done] = Frozen(Done),
        [SessionStatus.Idle] = Frozen(Idle),
    };

    public static Brush For(SessionStatus s) => brushes.TryGetValue(s, out var b) ? b : brushes[SessionStatus.Idle];

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
