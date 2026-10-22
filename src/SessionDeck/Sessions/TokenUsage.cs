using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClaudeSessions;

/// <summary>Token counts as the API reports them for one reply (or a sum of replies).</summary>
public readonly record struct Tokens(long Input, long CacheWrite, long CacheRead, long Output)
{
    public long Total => Input + CacheWrite + CacheRead + Output;

    /// <summary>Everything but cache reads: the context re-read every turn dwarfs the rest.</summary>
    public long Fresh => Input + CacheWrite + Output;

    public long Count(bool withCacheReads) => withCacheReads ? Total : Fresh;

    public static Tokens operator +(Tokens a, Tokens b) =>
        new(a.Input + b.Input, a.CacheWrite + b.CacheWrite, a.CacheRead + b.CacheRead, a.Output + b.Output);

    public static string Short(long n) =>
        n >= 1_000_000_000 ? $"{n / 1e9:0.##}B"
        : n >= 10_000_000 ? $"{n / 1e6:0}M"
        : n >= 1_000_000 ? $"{n / 1e6:0.#}M"
        : n >= 10_000 ? $"{n / 1e3:0}k"
        : n >= 1_000 ? $"{n / 1e3:0.#}k"
        : n.ToString();
}

/// <summary>One session's token use in the chosen time range, with a bucket per time step.</summary>
/// <param name="Cwd">The folder the session started in (worktrees count as their project).</param>
public sealed record TokenEntry(string Key, string Title, string Cwd, string? Link, Tokens Tokens, Tokens[] Buckets);

/// <param name="From">Start of the first bucket; buckets are <paramref name="Step"/> long.</param>
public sealed record TokenStats(TimeSpan Window, DateTime From, TimeSpan Step, IReadOnlyList<TokenEntry> Sessions)
{
    public static readonly TokenStats Empty = new(TimeSpan.FromHours(24), DateTime.MinValue, TimeSpan.FromHours(1), Array.Empty<TokenEntry>());

    /// <summary>About 24–32 buckets for every range the dashboard offers.</summary>
    public static TimeSpan StepFor(TimeSpan window) =>
        window <= TimeSpan.FromHours(2) ? TimeSpan.FromMinutes(5)
        : window <= TimeSpan.FromHours(8) ? TimeSpan.FromMinutes(15)
        : window <= TimeSpan.FromHours(24) ? TimeSpan.FromHours(1)
        : window <= TimeSpan.FromDays(3) ? TimeSpan.FromHours(3)
        : TimeSpan.FromHours(6);
}

/// <summary>
/// Follows one transcript (.jsonl) for the token use of each API reply, reading only the bytes
/// appended since the last call. A reply split over several lines (one per content block)
/// repeats its usage, so replies are counted once by message id.
/// </summary>
sealed class TokenLog
{
    readonly string path;
    long position;
    readonly Dictionary<string, (DateTime At, Tokens Tokens)> replies = new();

    public string? Cwd, CustomTitle;
    public DateTime FileTime;

    public TokenLog(string path) => this.path = path;

    public IEnumerable<(DateTime At, Tokens Tokens)> Replies => replies.Values;

    public void Update()
    {
        FileInfo info;
        try { info = new FileInfo(path); if (!info.Exists) return; } catch { return; }
        FileTime = info.LastWriteTimeUtc;
        if (info.Length < position)
        {
            position = 0;
            replies.Clear();
        }
        if (info.Length == position) return;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(position, SeekOrigin.Begin);
            // Whole files can be tens of MB: read in chunks, growing the buffer only for a longer line
            var buffer = new byte[4 << 20];
            int filled = 0;
            while (true)
            {
                int n = fs.Read(buffer, filled, buffer.Length - filled);
                if (n == 0) break;
                filled += n;
                int start = 0;
                while (true)
                {
                    int newline = Array.IndexOf(buffer, (byte)'\n', start, filled - start);
                    if (newline < 0) break;
                    ProcessLine(new ReadOnlySpan<byte>(buffer, start, newline - start));
                    start = newline + 1;
                }
                position += start;
                if (start == 0 && filled == buffer.Length)
                {
                    Array.Resize(ref buffer, buffer.Length * 2);
                }
                else
                {
                    Buffer.BlockCopy(buffer, start, buffer, 0, filled - start);
                    filled -= start;
                }
            }
        }
        catch (IOException) { }
    }

    void ProcessLine(ReadOnlySpan<byte> line)
    {
        bool reply = line.IndexOf("\"usage\":"u8) >= 0 && line.IndexOf("\"type\":\"assistant\""u8) >= 0;
        bool title = !reply && line.IndexOf("\"type\":\"custom-title\""u8) >= 0;
        if (!reply && !title) return;
        try
        {
            using var doc = JsonDocument.Parse(line.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            string? type = Json.Str(root, "type");
            if (type == "custom-title")
            {
                CustomTitle = Json.Str(root, "customTitle") ?? CustomTitle;
                return;
            }
            if (type != "assistant" || !root.TryGetProperty("message", out var msg)
                || !msg.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
            if (Json.Str(root, "cwd") is { } cwd) Cwd = cwd;

            long Get(string name) => usage.TryGetProperty(name, out var v) && v.TryGetInt64(out long n) ? n : 0;
            var tokens = new Tokens(Get("input_tokens"), Get("cache_creation_input_tokens"), Get("cache_read_input_tokens"), Get("output_tokens"));
            if (tokens.Total == 0) return;
            string id = Json.Str(msg, "id") ?? Json.Str(root, "uuid") ?? Guid.NewGuid().ToString();
            var at = Json.Time(root, "timestamp") ?? (replies.TryGetValue(id, out var seen) ? seen.At : DateTime.UtcNow);
            replies[id] = (at, tokens); // the last line of a reply has its final output count
        }
        catch (JsonException) { }
    }
}
