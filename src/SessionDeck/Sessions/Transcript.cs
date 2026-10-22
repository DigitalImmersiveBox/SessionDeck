using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeSessions;

sealed record PendingTool(string Id, string Name, string Summary, DateTime At, string? Question);

/// <summary>
/// Incrementally follows one session transcript (.jsonl), reading only the bytes
/// appended since the last call, and keeps the state of its current turn.
/// </summary>
sealed class Transcript
{
    const long FirstReadLimit = 32L << 20; // bigger files: start from the last 8 MB
    readonly string path;
    long position;

    public string? Entrypoint, Cwd, Branch, CustomTitle;
    public string? FirstPrompt, LastPrompt, LastText, LastTool, Recap, Ask;
    public int PromptCount;
    public bool TurnActive, Interrupted;
    public DateTime LastEventAt, TurnEndAt;
    public List<PlanStep> Plan = new();
    public readonly Dictionary<string, PendingTool> Pending = new();
    /// <summary>Background commands and agents still running (task id → what it is); they outlive the turn.</summary>
    public readonly Dictionary<string, string> Background = new();
    readonly Dictionary<string, string> agents = new(); // agents can be resumed with SendMessage
    readonly Dictionary<string, int> mcpCalls = new(); // MCP server → calls, over the whole session
    /// <summary>Finished turns of the last two days (for "Claude worked … today").</summary>
    public readonly List<(DateTime Start, DateTime End)> Turns = new();
    DateTime? turnStart;

    static readonly Regex BackgroundId = new(@"Command running in background with ID: ([\w-]+)", RegexOptions.Compiled);
    static readonly Regex MovedToBackground = new(@"moved to the background \(ID: ([\w-]+)\)", RegexOptions.Compiled);
    static readonly Regex AgentId = new(@"agentId: ([\w-]+)", RegexOptions.Compiled);
    static readonly Regex TaskDone = new(@"<task-id>([^<]+)</task-id>.*?<status>([^<]+)</status>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    static readonly byte[] NotificationTag = Encoding.UTF8.GetBytes("<task-notification>");

    public Transcript(string path) => this.path = path;

    public void Update()
    {
        FileInfo info;
        try { info = new FileInfo(path); if (!info.Exists) return; } catch { return; }
        if (info.Length < position) Reset();
        if (info.Length == position) return;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool skipFirst = false;
            if (position == 0 && fs.Length > FirstReadLimit)
            {
                position = fs.Length - (8L << 20);
                skipFirst = true;
            }
            fs.Seek(position, SeekOrigin.Begin);
            var buffer = new byte[fs.Length - position];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = fs.Read(buffer, read, buffer.Length - read);
                if (n == 0) break;
                read += n;
            }

            int end = Array.LastIndexOf(buffer, (byte)'\n', Math.Max(read - 1, 0));
            if (end < 0) return; // no complete line yet
            position += end + 1;

            int start = 0;
            for (int i = 0; i <= end; i++)
            {
                if (buffer[i] != '\n') continue;
                if (skipFirst) skipFirst = false;
                else ProcessLine(new ReadOnlyMemory<byte>(buffer, start, i - start));
                start = i + 1;
            }
        }
        catch (IOException) { }
    }

    void Reset()
    {
        position = 0;
        PromptCount = 0;
        FirstPrompt = LastPrompt = LastText = LastTool = Recap = Ask = null;
        TurnActive = Interrupted = false;
        Plan = new();
        Pending.Clear();
        Background.Clear();
        agents.Clear();
        mcpCalls.Clear();
        Turns.Clear();
        turnStart = null;
    }

    static readonly byte[][] SkipPrefixes =
    {
        Encoding.UTF8.GetBytes("{\"type\":\"api-request"),
        Encoding.UTF8.GetBytes("{\"type\":\"atis"),
        Encoding.UTF8.GetBytes("{\"type\":\"file-history"),
    };

    void ProcessLine(ReadOnlyMemory<byte> line)
    {
        if (line.Length < 2) return;
        foreach (var prefix in SkipPrefixes)
            if (line.Span.StartsWith(prefix)) return;

        // Completion notices arrive as prompts, queued messages or attachments; match them in any record
        if (line.Span.IndexOf(NotificationTag) >= 0)
            foreach (Match m in TaskDone.Matches(Encoding.UTF8.GetString(line.Span)))
                if (m.Groups[2].Value != "running") Background.Remove(m.Groups[1].Value);

        try
        {
            using var doc = JsonDocument.Parse(line);
            Process(doc.RootElement);
        }
        catch (JsonException) { }
    }

    void Process(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        if (root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) return;

        string? type = Json.Str(root, "type");
        var ts = Json.Time(root, "timestamp");
        if (ts > LastEventAt) LastEventAt = ts.Value;

        if (Json.Str(root, "entrypoint") is { } entry) Entrypoint = entry;
        if (Json.Str(root, "cwd") is { } cwd) Cwd = cwd;
        if (Json.Str(root, "gitBranch") is { Length: > 0 } branch && branch != "HEAD") Branch = branch;

        switch (type)
        {
            case "custom-title":
                CustomTitle = Json.Str(root, "customTitle") ?? CustomTitle;
                break;
            case "assistant":
                OnAssistant(root, ts ?? LastEventAt);
                break;
            case "user":
                OnUser(root, ts ?? LastEventAt);
                break;
            case "system" when Json.Str(root, "subtype") == "stop_hook_summary":
                EndTurn(ts ?? LastEventAt);
                break;
        }
    }

    void OnAssistant(JsonElement root, DateTime ts)
    {
        if (!root.TryGetProperty("message", out var msg)) return;
        bool usedTool = false;
        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                switch (Json.Str(block, "type"))
                {
                    case "text" when Json.Str(block, "text") is { } text && !string.IsNullOrWhiteSpace(text):
                        LastText = text;
                        break;
                    case "tool_use":
                        usedTool = true;
                        OnTool(block, ts);
                        break;
                }
            }
        }

        if (Json.Str(msg, "stop_reason") == "end_turn" && !usedTool) EndTurn(ts);
        else TurnActive = true;
    }

    void OnTool(JsonElement block, DateTime ts)
    {
        string name = Json.Str(block, "name") ?? "tool";
        string id = Json.Str(block, "id") ?? Guid.NewGuid().ToString();
        block.TryGetProperty("input", out var input);
        string shortName = name.Contains("__") ? name[(name.LastIndexOf("__", StringComparison.Ordinal) + 2)..] : name;
        CountMcp(name, shortName);

        if (shortName == "update_plan")
        {
            ApplyPlan(input);
            return;
        }
        if (shortName == "wrap_up")
        {
            Recap = Json.Str(input, "recap");
            Ask = Json.Str(input, "ask");
            Plan = Plan.Select(s => s with { Status = "completed" }).ToList();
            return;
        }

        if (shortName == "TaskStop" && Json.Str(input, "task_id") is { } stopped)
            Background.Remove(stopped);
        if (shortName == "SendMessage" && Json.Str(input, "to") is { } to && agents.TryGetValue(to, out var resumed))
            Background[to] = resumed;

        var summary = ToolSummary(name, shortName, input);
        LastTool = summary;
        Pending[id] = new PendingTool(id, name, summary, ts, QuestionText(shortName, input));
    }

    void OnUser(JsonElement root, DateTime ts)
    {
        if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) return;
        if (!root.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content)) return;

        if (content.ValueKind == JsonValueKind.String)
        {
            OnPrompt(content.GetString() ?? "", ts);
            return;
        }
        if (content.ValueKind != JsonValueKind.Array) return;

        bool hadResult = false;
        var texts = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            switch (Json.Str(block, "type"))
            {
                case "tool_result":
                    hadResult = true;
                    if (Json.Str(block, "tool_use_id") is { } id && Pending.Remove(id, out var tool))
                        NoteBackground(tool, ResultText(block));
                    break;
                case "text" when Json.Str(block, "text") is { } text:
                    texts.Add(text);
                    break;
            }
        }
        if (hadResult) TurnActive = true;
        else if (texts.Count > 0) OnPrompt(string.Join("\n", texts), ts);
    }

    // Only the tool's own launch message counts: the same words can appear inside any output
    // (a search through another transcript, for example)
    void NoteBackground(PendingTool tool, string result)
    {
        result = result.TrimStart();
        string shortName = tool.Name.Contains("__") ? tool.Name[(tool.Name.LastIndexOf("__", StringComparison.Ordinal) + 2)..] : tool.Name;
        if (shortName is "Bash" or "PowerShell")
        {
            var command = result.StartsWith("Command running in background", StringComparison.Ordinal) ? BackgroundId.Match(result)
                : result.StartsWith("Command did not complete", StringComparison.Ordinal) ? MovedToBackground.Match(result)
                : Match.Empty;
            if (command.Success) Background[command.Groups[1].Value] = tool.Summary;
        }
        else if (shortName is "Agent" or "Task" && result.StartsWith("Async agent launched", StringComparison.Ordinal)
                 && AgentId.Match(result) is { Success: true } agent)
        {
            Background[agent.Groups[1].Value] = tool.Summary;
            agents[agent.Groups[1].Value] = tool.Summary;
        }
    }

    static string ResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content)) return "";
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array) return "";
        return string.Join("\n", content.EnumerateArray().Select(b => Json.Str(b, "text")).Where(s => s != null));
    }

    void OnPrompt(string text, DateTime ts)
    {
        if (text.StartsWith("[Request interrupted", StringComparison.Ordinal))
        {
            EndTurn(ts);
            Interrupted = true;
            return;
        }
        PromptCount++;
        if (!text.TrimStart().StartsWith('<'))
        {
            FirstPrompt ??= text;
            LastPrompt = text;
        }
        if (!TurnActive) turnStart = ts;
        TurnActive = true;
        Interrupted = false;
        Ask = Recap = LastTool = LastText = null;
        Plan = new();
        Pending.Clear();
    }

    void EndTurn(DateTime ts)
    {
        if (TurnActive && turnStart is { } start && ts > start)
        {
            Turns.Add((start, ts));
            Turns.RemoveAll(t => ts - t.End > TimeSpan.FromDays(2));
        }
        turnStart = null;
        TurnActive = false;
        TurnEndAt = ts;
        Pending.Clear();
    }

    /// <summary>How long Claude worked in this session between <paramref name="from"/> and now, and how many turns.</summary>
    public (TimeSpan Worked, int Turns) WorkSince(DateTime from, DateTime now)
    {
        var spans = Turns.Select(t => (t.Start, t.End)).ToList();
        // A turn that went quiet (app closed mid-turn) stops counting at its last activity
        if (TurnActive && turnStart is { } open)
            spans.Add((open, now - LastEventAt > TimeSpan.FromMinutes(15) ? LastEventAt : now));
        var worked = TimeSpan.Zero;
        int count = 0;
        foreach (var (start, end) in spans)
        {
            if (end <= from) continue;
            worked += end - (start > from ? start : from);
            count++;
        }
        return (worked, count);
    }

    void ApplyPlan(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return;
        if (input.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            Plan = items.EnumerateArray()
                .Select(i => new PlanStep(Json.Str(i, "content") ?? "", Json.Str(i, "status") ?? "pending"))
                .ToList();
            return;
        }

        var steps = Plan.ToList();
        if (input.TryGetProperty("advance", out var adv) && adv.ValueKind == JsonValueKind.True)
        {
            int current = steps.FindIndex(s => s.Status == "in_progress");
            if (current >= 0) steps[current] = steps[current] with { Status = "completed" };
            int next = steps.FindIndex(Math.Max(current, 0), s => s.Status == "pending");
            if (next >= 0) steps[next] = steps[next] with { Status = "in_progress" };
        }
        if (input.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.Array)
            foreach (var n in done.EnumerateArray())
                if (n.TryGetInt32(out int i) && i >= 1 && i <= steps.Count)
                    steps[i - 1] = steps[i - 1] with { Status = "completed" };
        if (input.TryGetProperty("start", out var start) && start.TryGetInt32(out int s) && s >= 1 && s <= steps.Count)
        {
            for (int i = 0; i < steps.Count; i++)
                if (steps[i].Status == "in_progress") steps[i] = steps[i] with { Status = "pending" };
            steps[s - 1] = steps[s - 1] with { Status = "in_progress" };
        }
        Plan = steps;
    }

    static string? QuestionText(string shortName, JsonElement input)
    {
        switch (shortName)
        {
            case "AskUserQuestion":
                if (input.TryGetProperty("questions", out var qs) && qs.ValueKind == JsonValueKind.Array)
                {
                    var list = qs.EnumerateArray().Select(q => Json.Str(q, "question")).Where(q => q != null).ToList();
                    if (list.Count > 0) return string.Join("  ·  ", list);
                }
                return "Asking you a question";
            case "ExitPlanMode":
                return "Waiting for you to approve the plan";
            case "start_session" when Json.Str(input, "initiation") == "offer":
                return "Offers a new session: " + (Json.Str(input, "title") ?? "choose New session or Do it here");
            case "propose_branch":
                return "Asks which git branch to use";
            case "request_directory":
                return "Asks you to pick a folder";
            default:
                return null;
        }
    }

    static string ToolSummary(string name, string shortName, JsonElement input)
    {
        string? File(string key) => Json.Str(input, key) is { } p ? Path.GetFileName(p) : null;
        string? Text(string key, int max) => SessionScanner.Shorten(Json.Str(input, key), max);

        return shortName switch
        {
            "Bash" or "PowerShell" => Text("description", 80) ?? Text("command", 80) ?? "Running a command",
            "Read" => "Reading " + (File("file_path") ?? "a file"),
            "Edit" or "MultiEdit" => "Editing " + (File("file_path") ?? "a file"),
            "Write" => "Writing " + (File("file_path") ?? "a file"),
            "Grep" or "Glob" => "Searching " + (Text("pattern", 50) ?? "files"),
            "Agent" or "Task" => "Subagent: " + (Text("description", 60) ?? "working"),
            "WebSearch" => "Searching the web: " + (Text("query", 60) ?? ""),
            "WebFetch" => "Reading a web page",
            "ToolSearch" => "Loading tools",
            "Skill" => "Using skill " + (Json.Str(input, "skill") ?? ""),
            _ when name.StartsWith("mcp__", StringComparison.Ordinal) => McpLabel(name),
            _ => shortName,
        };
    }

    // ---- MCP servers used --------------------------------------------------

    static readonly Dictionary<string, string> McpNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Claude_Browser"] = "Browser", ["claude-in-chrome"] = "Chrome", ["UnityMCP"] = "Unity",
        ["blender"] = "Blender", ["cloudflare"] = "Cloudflare", ["context7"] = "Context7", ["gemini"] = "Gemini",
        ["terminal"] = "Terminal", ["visualize"] = "Visualize", ["mcp-registry"] = "Connectors",
        ["scheduled-tasks"] = "Scheduled tasks", ["photocraft"] = "PhotoCraft", ["vectorcraft"] = "VectorCraft",
        ["pdfcraft"] = "PdfCraft", ["cadcraft"] = "CADCraft", ["figcraft"] = "FigCraft",
        ["soundcraft"] = "SoundCraft", ["lightcraft"] = "LightCraft",
    };

    // claude.ai connectors are named by an id; their tool names tell which service it is.
    // Shared by all transcripts, so a connector seen once by a telling tool is named everywhere.
    static readonly Dictionary<string, string> connectorNames = new(StringComparer.OrdinalIgnoreCase);

    void CountMcp(string name, string tool)
    {
        if (!name.StartsWith("mcp__", StringComparison.Ordinal)) return;
        var parts = name.Split("__");
        if (parts.Length < 3) return;
        string server = parts[1];
        if (server.StartsWith("ccd_", StringComparison.Ordinal)) return; // the desktop app's own plumbing (task list, sessions, panes)
        mcpCalls[server] = mcpCalls.GetValueOrDefault(server) + 1;
        if (Guid.TryParse(server, out _) && ConnectorFor(tool) is { } service)
            lock (connectorNames) connectorNames[server] = service;
    }

    static string? ConnectorFor(string tool) =>
        tool.Contains("figma") || tool == "get_design_context" ? "Figma"
        : tool.Contains("spreadsheet") || tool.Contains("values") || tool.Contains("formulas") ? "Sheets"
        : tool.Contains("presentation") || tool.Contains("slide") ? "Slides"
        : tool is "read_doc" or "update_doc" ? "Google Docs"
        : tool.Contains("event") || tool.Contains("calendar") || tool == "suggest_time" ? "Calendar"
        : tool.Contains("file") ? "Drive"
        : tool is "guide" or "batch" ? "Claude Docs"
        : null;

    /// <summary>Display names of the MCP servers this session used, most used first.</summary>
    public IReadOnlyList<string> McpServers()
    {
        lock (connectorNames)
            return mcpCalls
                .GroupBy(c => McpNames.TryGetValue(c.Key, out var known) ? known
                    : Guid.TryParse(c.Key, out _) ? connectorNames.GetValueOrDefault(c.Key, "Connector")
                    : c.Key)
                .OrderByDescending(g => g.Sum(c => c.Value)).ThenBy(g => g.Key)
                .Select(g => g.Key).ToList();
    }

    static string McpLabel(string name)
    {
        var parts = name.Split("__");
        string server = parts.Length > 1 ? parts[1] : "";
        string tool = parts[^1].Replace('_', ' ');
        if (server.Length > 20 || server.Contains('-') && Guid.TryParse(server, out _)) return tool;
        return $"{server}: {tool}";
    }
}
