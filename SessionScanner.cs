using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeSessions;

public enum SessionStatus { NeedsYou, Check, YourTurn, Working, Done, Idle }

public sealed record PlanStep(string Content, string Status);

public sealed record ProjectEntry(string Name, string Path, DateTime LastUsed);

/// <param name="Worked">Claude's working time summed over sessions (parallel sessions add up).</param>
public sealed record TodayStats(int Sessions, int Turns, TimeSpan Worked, IReadOnlyList<(string Project, int Sessions)> Projects)
{
    public static readonly TodayStats Empty = new(0, 0, TimeSpan.Zero, Array.Empty<(string, int)>());
}

public sealed record SessionSnapshot(
    string Key,
    string Title,
    string Project,
    string Cwd,
    string? Branch,
    SessionStatus Status,
    string StatusText,
    string? Activity,
    string? Detail,
    IReadOnlyList<PlanStep> Plan,
    DateTime LastActivity,
    string? Link,
    string? NotifyToken,
    bool Dismissable = false,
    IReadOnlyList<string>? Mcps = null);

/// <summary>
/// Reads the Claude desktop app's session metadata (title, folder, archived, last focus)
/// and each session's live transcript (~/.claude/projects/*/*.jsonl) to work out
/// what every session is doing right now. Read-only: never writes to Claude's files.
/// </summary>
public sealed class SessionScanner
{
    static readonly string ProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    static readonly TimeSpan ApprovalAfter = TimeSpan.FromSeconds(90);
    static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(15);

    readonly Dictionary<string, Meta> metas = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Transcript> transcripts = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, DateTime> seenAt = new();
    Dictionary<string, string> transcriptIndex = new(StringComparer.OrdinalIgnoreCase);
    List<string> metaFiles = new();
    DateTime lastIndex = DateTime.MinValue;
    readonly UnreadStore unread = new(AccountDirs());

    /// <summary>Project folders you've used with Claude, most recent first (refreshed every 10 s).</summary>
    public IReadOnlyList<ProjectEntry> Projects { get; private set; } = Array.Empty<ProjectEntry>();

    /// <summary>Sessions, turns and working time since local midnight (updated every scan).</summary>
    public TodayStats Today { get; private set; } = TodayStats.Empty;

    readonly Dictionary<string, DateTime> dismissed = new();

    public void MarkSeen(string key)
    {
        lock (seenAt) seenAt[key] = DateTime.UtcNow;
    }

    /// <summary>Clears a question you've decided not to answer. Returns when, so it can be saved.</summary>
    public DateTime Dismiss(string key)
    {
        var at = DateTime.UtcNow;
        lock (seenAt) dismissed[key] = at;
        return at;
    }

    public void RestoreDismissed(IReadOnlyDictionary<string, DateTime> saved)
    {
        lock (seenAt)
            foreach (var (key, at) in saved) dismissed[key] = at;
    }

    public List<SessionSnapshot> Scan(TimeSpan window)
    {
        var now = DateTime.UtcNow;
        bool reindexed = now - lastIndex > TimeSpan.FromSeconds(10);
        if (reindexed)
        {
            Reindex();
            lastIndex = now;
        }

        foreach (var file in metaFiles)
        {
            DateTime written;
            try { written = File.GetLastWriteTimeUtc(file); } catch { continue; }
            if (metas.TryGetValue(file, out var known) && known.FileTime == written) continue;
            var meta = Meta.Load(file, written);
            if (meta != null) metas[file] = meta;
        }
        if (reindexed) LoadProjects();

        var unreadIds = unread.Read();
        var results = new List<SessionSnapshot>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var horizon = window > TimeSpan.FromDays(3) ? window : TimeSpan.FromDays(3);

        foreach (var meta in metas.Values)
        {
            string? path = null;
            if (meta.CliId != null && transcriptIndex.TryGetValue(meta.CliId, out var p))
            {
                path = p;
                claimed.Add(p);
            }
            if (meta.Archived) continue;

            var last = meta.LastActivity;
            if (path != null) last = Max(last, SafeWriteTime(path));
            if (now - last > horizon) continue;

            var t = path != null ? GetTranscript(path) : null;
            var snap = Build(meta.LocalId, meta.Title, meta.Cwd, meta.LastFocused, unreadIds?.Contains(meta.LocalId),
                meta.StatusDetail, $"claude://claude.ai/epitaxy/{meta.LocalId}", t, last, now);
            if (Keep(snap, window, now)) results.Add(snap);
        }

        // Sessions started from a terminal (`claude` CLI) have no desktop metadata
        foreach (var (id, path) in transcriptIndex)
        {
            if (claimed.Contains(path)) continue;
            var written = SafeWriteTime(path);
            if (now - written > window) continue;
            var t = GetTranscript(path);
            if (t.Entrypoint != "cli" || t.PromptCount == 0) continue;
            var title = t.CustomTitle ?? Shorten(t.FirstPrompt, 60) ?? "Terminal session";
            var snap = Build(id, title, t.Cwd ?? "", DateTime.MinValue, null, null, null, t, written, now);
            if (Keep(snap, window, now)) results.Add(snap);
        }

        Today = CountToday(now);
        return results;
    }

    readonly Dictionary<string, TokenLog> tokenLogs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tokens each session used in the last <paramref name="window"/>, its subagents included.
    /// Archived and hidden sessions count too: the tokens were spent all the same. Call after <see cref="Scan"/>.
    /// </summary>
    public TokenStats CountTokens(TimeSpan window)
    {
        var now = DateTime.UtcNow;
        var since = now - window;
        var step = TokenStats.StepFor(window);
        // Buckets end on the next whole step of local time (hour, quarter hour…) and cover the window
        long localEnd = (DateTime.Now.Ticks / step.Ticks + 1) * step.Ticks;
        var end = new DateTime(localEnd, DateTimeKind.Local).ToUniversalTime();
        int count = (int)Math.Ceiling((end - since) / step);
        var from = end - step * count;

        var byCli = new Dictionary<string, Meta>(StringComparer.OrdinalIgnoreCase);
        foreach (var meta in metas.Values)
            if (meta.CliId != null) byCli[meta.CliId] = meta;

        var sessions = new List<TokenEntry>();
        foreach (var (id, path) in transcriptIndex)
        {
            var files = new List<string> { path };
            var subagents = Path.Combine(Path.GetDirectoryName(path)!, id, "subagents");
            try { if (Directory.Exists(subagents)) files.AddRange(Directory.EnumerateFiles(subagents, "*.jsonl")); }
            catch { }
            var logs = files.Where(f => SafeWriteTime(f) >= since).Select(TokenLogFor).ToList();
            if (logs.Count == 0) continue;

            var sum = default(Tokens);
            var buckets = new Tokens[count];
            foreach (var log in logs)
                foreach (var (at, tokens) in log.Replies)
                {
                    if (at < since || at > now) continue;
                    sum += tokens;
                    int b = (int)((at - from).Ticks / step.Ticks);
                    if (b >= 0 && b < count) buckets[b] += tokens;
                }
            if (sum.Total == 0) continue;

            var main = TokenLogFor(path);
            byCli.TryGetValue(id, out var m);
            string cwd = WithoutWorktree(m?.OriginCwd ?? (string.IsNullOrEmpty(m?.Cwd) ? null : m.Cwd) ?? main.Cwd ?? "");
            string title = (string.IsNullOrWhiteSpace(m?.Title) ? null : m.Title) ?? main.CustomTitle
                           ?? (transcripts.TryGetValue(path, out var t) ? Shorten(t.FirstPrompt, 60) : null) ?? "Untitled session";
            sessions.Add(new TokenEntry(m?.LocalId ?? id, title, cwd,
                m != null ? $"claude://claude.ai/epitaxy/{m.LocalId}" : null, sum, buckets));
        }
        return new TokenStats(window, from, step, sessions);
    }

    TokenLog TokenLogFor(string path)
    {
        if (!tokenLogs.TryGetValue(path, out var log))
            tokenLogs[path] = log = new TokenLog(path);
        log.Update();
        return log;
    }

    /// <summary>A worktree's tokens count for the project it was made from.</summary>
    static string WithoutWorktree(string cwd)
    {
        int at = cwd.IndexOf(@"\.claude\worktrees\", StringComparison.OrdinalIgnoreCase);
        return at > 0 ? cwd[..at] : cwd;
    }

    TodayStats CountToday(DateTime now)
    {
        var midnight = DateTime.Today.ToUniversalTime();
        var perProject = new Dictionary<string, int>();
        int sessions = 0, turns = 0;
        var worked = TimeSpan.Zero;
        foreach (var t in transcripts.Values)
        {
            var (w, n) = t.WorkSince(midnight, now);
            if (n == 0) continue;
            sessions++;
            turns += n;
            worked += w;
            var project = ProjectName(t.Cwd ?? "");
            perProject[project] = perProject.GetValueOrDefault(project) + 1;
        }
        return new TodayStats(sessions, turns, worked,
            perProject.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Select(p => (p.Key, p.Value)).ToList());
    }

    static bool Keep(SessionSnapshot s, TimeSpan window, DateTime now) =>
        now - s.LastActivity <= window
        || s.Status is SessionStatus.Working or SessionStatus.Check
        || (s.Status is SessionStatus.NeedsYou or SessionStatus.YourTurn && now - s.LastActivity <= TimeSpan.FromDays(3));

    /// <param name="appUnread">The Claude sidebar's unread flag for this session; null when unknown.</param>
    /// <param name="statusDetail">Claude's own one-line status for the session, if it wrote one.</param>
    SessionSnapshot Build(string key, string? title, string cwd, DateTime lastFocused, bool? appUnread, string? statusDetail,
                          string? link, Transcript? t, DateTime last, DateTime now)
    {
        DateTime clicked, dismissedAt;
        lock (seenAt)
        {
            seenAt.TryGetValue(key, out clicked);
            dismissed.TryGetValue(key, out dismissedAt);
        }
        bool dismissable = false;

        string project = ProjectName(cwd);
        title = string.IsNullOrWhiteSpace(title) ? t?.CustomTitle ?? "Untitled session" : title;

        if (t == null)
            return new(key, title, project, cwd, null, SessionStatus.Idle, "Idle",
                null, null, Array.Empty<PlanStep>(), last, link, null);

        last = Max(last, t.LastEventAt);
        var plan = t.Plan.ToList();
        string? step = plan.FirstOrDefault(s => s.Status == "in_progress")?.Content;
        SessionStatus status;
        string statusText;
        string? activity, detail = null, token = null;

        var question = t.TurnActive ? t.Pending.Values.FirstOrDefault(p => p.Question != null) : null;
        if (question != null)
        {
            status = SessionStatus.NeedsYou;
            statusText = "Needs you";
            activity = question.Question;
            detail = step;
            token = $"{key}|q|{question.Id}";
        }
        else if (t.TurnActive)
        {
            var oldest = t.Pending.Values.OrderBy(p => p.At).FirstOrDefault();
            var quiet = now - t.LastEventAt;
            if (oldest != null && now - oldest.At > ApprovalAfter && now - oldest.At < TimeSpan.FromHours(3))
            {
                status = SessionStatus.Check;
                statusText = "Approval?";
                activity = $"{oldest.Summary} has been running for {Duration(now - oldest.At)}. It may be waiting for your approval.";
                detail = step;
                token = $"{key}|c|{oldest.Id}";
            }
            else if (quiet > StalledAfter)
            {
                status = SessionStatus.Idle;
                statusText = "Stopped";
                activity = $"No activity for {Duration(quiet)}";
            }
            else
            {
                status = SessionStatus.Working;
                statusText = "Working";
                activity = step ?? t.LastTool ?? "Thinking…";
                detail = step != null ? t.LastTool : Shorten(t.LastPrompt, 140) is { } asked ? "You asked: " + asked : null;
            }
        }
        else
        {
            // "Checked" = opened from this app since it finished, or Claude's own sidebar no longer marks it unread
            bool clickedSince = clicked >= t.TurnEndAt.AddSeconds(-2);
            bool unseen = link == null ? !clickedSince && now - t.TurnEndAt < TimeSpan.FromMinutes(30)
                : appUnread is bool u ? u && !clickedSince
                : !clickedSince && lastFocused < t.TurnEndAt.AddSeconds(-2);
            // Claude writes its unread list a few seconds after the turn ends; don't flash "Done" meanwhile
            bool settling = appUnread == false && !clickedSince && now - t.TurnEndAt < TimeSpan.FromSeconds(8);
            string? summary = Shorten(t.Recap, 300) ?? Shorten(t.LastText, 300);
            // A turn that ended on a question stays "Needs you" until you reply (a new prompt resets
            // the turn) or dismiss it; opening the session doesn't count as answering
            string? ask = t.Ask ?? TrailingQuestion(t.LastText);
            bool isDismissed = dismissedAt >= t.TurnEndAt.AddSeconds(-2);
            if (t.Interrupted)
            {
                status = SessionStatus.Done;
                statusText = "Interrupted";
                activity = summary;
            }
            else if (ask != null && !isDismissed)
            {
                status = SessionStatus.NeedsYou;
                statusText = "Needs you";
                activity = ask;
                detail = summary;
                dismissable = true;
            }
            else if (t.Background.Count > 0 && now - t.LastEventAt < TimeSpan.FromHours(3))
            {
                // The turn ended but its background commands / agents are still going
                status = SessionStatus.Working;
                statusText = "Background";
                var tasks = t.Background.Values.Distinct().ToList();
                activity = (tasks.Count == 1 ? "Running in background: " : $"{tasks.Count} background tasks: ")
                           + string.Join(", ", tasks.Take(3));
                detail = Shorten(statusDetail, 200) ?? summary;
            }
            else if (settling)
            {
                status = SessionStatus.Working;
                statusText = "Finishing";
                activity = summary;
            }
            else if (unseen)
            {
                status = SessionStatus.YourTurn;
                statusText = "Your turn";
                activity = summary;
            }
            else
            {
                status = SessionStatus.Done;
                statusText = "Done";
                activity = ask != null ? "Dismissed question: " + ask : summary;
            }
            if (status is SessionStatus.NeedsYou or SessionStatus.YourTurn)
                token = $"{key}|e|{t.TurnEndAt.Ticks}";
        }

        return new(key, title, project, cwd, t.Branch, status, statusText,
            activity, detail, plan, last, link, token, dismissable, t.McpServers());
    }

    /// <summary>The closing sentence of a reply when it is a question, for sessions that didn't use the next-step box.</summary>
    static string? TrailingQuestion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.TrimEnd().TrimEnd('*', '_', '`').TrimEnd();
        if (!trimmed.EndsWith('?')) return null;
        var line = trimmed[(trimmed.LastIndexOf('\n') + 1)..];
        int cut = Math.Max(line.LastIndexOf(". ", StringComparison.Ordinal), line.LastIndexOf("! ", StringComparison.Ordinal));
        if (cut >= 0) line = line[(cut + 2)..];
        return Shorten(line, 200);
    }

    Transcript GetTranscript(string path)
    {
        if (!transcripts.TryGetValue(path, out var t))
            transcripts[path] = t = new Transcript(path);
        t.Update();
        return t;
    }

    void Reindex()
    {
        var files = new List<string>();
        foreach (var root in MetaRoots())
        {
            try { files.AddRange(Directory.EnumerateFiles(root, "local_*.json", SearchOption.AllDirectories)); }
            catch { }
        }
        metaFiles = files;
        foreach (var gone in metas.Keys.Except(files, StringComparer.OrdinalIgnoreCase).ToList())
            metas.Remove(gone);

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(ProjectsDir))
                foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
                    index[Path.GetFileNameWithoutExtension(file)] = file;
        }
        catch { }
        transcriptIndex = index;
    }

    /// <summary>The Claude app's per-install data folders (Claude-Data\claude_*, or the legacy Roaming\Claude).</summary>
    static List<string> AccountDirs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dataRoots = new List<string> { Path.Combine(local, "Claude-Data") };
        try
        {
            // Store (MSIX) installs may keep their data in the package's virtualised folder
            foreach (var pkg in Directory.EnumerateDirectories(Path.Combine(local, "Packages"), "Claude_*"))
                dataRoots.Add(Path.Combine(pkg, "LocalCache", "Local", "Claude-Data"));
        }
        catch { }

        var dirs = new List<string>();
        foreach (var dataRoot in dataRoots.Where(Directory.Exists))
            dirs.AddRange(Directory.EnumerateDirectories(dataRoot));
        var legacy = Path.Combine(roaming, "Claude");
        if (Directory.Exists(legacy)) dirs.Add(legacy);
        return dirs;
    }

    static IEnumerable<string> MetaRoots() =>
        AccountDirs().Select(d => Path.Combine(d, "claude-code-sessions")).Where(Directory.Exists);

    /// <summary>Folders from your sessions plus the environments saved in the Claude app (local ones only).</summary>
    void LoadProjects()
    {
        var byPath = new Dictionary<string, ProjectEntry>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path, string? name, DateTime used)
        {
            if (string.IsNullOrWhiteSpace(path)
                || path.Contains("scratch-workspaces", StringComparison.OrdinalIgnoreCase)
                || path.Contains(@"\.claude\worktrees\", StringComparison.OrdinalIgnoreCase)) return;
            path = path.TrimEnd('\\', '/');
            if (byPath.TryGetValue(path, out var known))
                byPath[path] = known with { Name = name ?? known.Name, LastUsed = Max(known.LastUsed, used) };
            else
                byPath[path] = new ProjectEntry(name ?? ProjectName(path), path, used);
        }

        foreach (var meta in metas.Values)
            Add(meta.OriginCwd ?? meta.Cwd, null, meta.LastActivity);

        foreach (var dir in AccountDirs())
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "ccd-session-presets.json")));
                if (!doc.RootElement.TryGetProperty("byAccount", out var accounts)) continue;
                foreach (var account in accounts.EnumerateObject())
                {
                    if (!account.Value.TryGetProperty("presets", out var presets) || presets.ValueKind != JsonValueKind.Array) continue;
                    foreach (var preset in presets.EnumerateArray())
                        if (preset.TryGetProperty("target", out var target) && Json.Str(target, "kind") == "local")
                            Add(Json.Str(target, "cwd"), Json.Str(preset, "name"), Json.Ms(preset, "lastUsedAt"));
                }
            }
            catch { }
        }

        Projects = byPath.Values.Where(p => Directory.Exists(p.Path)).OrderByDescending(p => p.LastUsed).ToList();
    }

    public static string ProjectName(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd) || cwd.Contains("scratch-workspaces", StringComparison.OrdinalIgnoreCase))
            return "No folder";
        return Path.GetFileName(cwd.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : cwd;
    }

    static DateTime SafeWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
    }

    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    public static string Duration(TimeSpan span) =>
        span.TotalMinutes < 1 ? $"{(int)span.TotalSeconds}s"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min"
        : span.TotalDays < 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
        : $"{(int)span.TotalDays} d";

    static readonly Regex Markdown = new(@"\*\*|__|`|(?<!\w)\*(?=\S)|(?<=\S)\*(?!\w)|^#+\s*|^\s*[-*]\s+|\|?\s*:?-{3,}:?\s*\|?|\s*\|\s*|\[([^\]]*)\]\([^)]*\)", RegexOptions.Multiline | RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static string? Shorten(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = Markdown.Replace(text, m => m.Groups[1].Success ? m.Groups[1].Value : m.Value.Contains('|') ? " " : "");
        text = Spaces.Replace(text, " ").Trim();
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }

    sealed class Meta
    {
        public required string LocalId;
        public string? CliId;
        public string? Title;
        public string Cwd = "";
        public string? OriginCwd;
        public string? StatusDetail;
        public bool Archived;
        public DateTime LastActivity;
        public DateTime LastFocused;
        public DateTime FileTime;

        public static Meta? Load(string path, DateTime written)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(fs);
                var r = doc.RootElement;
                return new Meta
                {
                    LocalId = Json.Str(r, "sessionId") ?? Path.GetFileNameWithoutExtension(path),
                    CliId = Json.Str(r, "cliSessionId"),
                    Title = Json.Str(r, "title"),
                    Cwd = Json.Str(r, "cwd") ?? "",
                    OriginCwd = Json.Str(r, "originCwd"),
                    StatusDetail = r.TryGetProperty("postTurnSummary", out var summary) ? Json.Str(summary, "status_detail") : null,
                    Archived = r.TryGetProperty("isArchived", out var a) && a.ValueKind == JsonValueKind.True,
                    LastActivity = Json.Ms(r, "lastActivityAt"),
                    LastFocused = Json.Ms(r, "lastFocusedAt"),
                    FileTime = written,
                };
            }
            catch
            {
                return null; // half-written; try again next tick
            }
        }
    }
}

static class Json
{
    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    public static DateTime Ms(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : DateTime.MinValue;

    public static DateTime? Time(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
            ? t : null;
}
