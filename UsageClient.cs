using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClaudeSessions;

public sealed record Limit(string Key, string Label, double Percent, DateTime? ResetsAt);

/// <param name="Session">The rolling 5-hour limit; null when the plan reports none.</param>
public sealed record UsageReport(Limit? Session, IReadOnlyList<Limit> Weekly, DateTime FetchedAt);

public enum ServiceHealth { Unknown, Operational, Minor, Major, Critical }

public sealed record ServiceStatus(ServiceHealth Health, string Description);

public sealed class UsageException : Exception
{
    public UsageException(string message, TimeSpan retryAfter, bool signIn = false) : base(message)
    {
        RetryAfter = retryAfter;
        SignIn = signIn;
    }

    public TimeSpan RetryAfter { get; }
    /// <summary>The login is missing, expired or not allowed: signing in again fixes it.</summary>
    public bool SignIn { get; }
}

/// <summary>
/// Reads your plan limits from the endpoint behind Claude Code's /usage, signed in with
/// Claude Code's own OAuth token. Read-only: never refreshes, copies or writes the token.
/// </summary>
public static class UsageClient
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static string ConfigDir =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir ? dir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    public static async Task<UsageReport> FetchAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReadToken());
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd("claude-usage-widget/1.0");

        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UsageException("Sign-in expired.", TimeSpan.FromMinutes(1), signIn: true);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new UsageException("This login can't read usage. Sign in again with your Claude account.", TimeSpan.FromMinutes(5), signIn: true);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new UsageException("Usage check rate-limited; retrying in 5 min", TimeSpan.FromMinutes(5));
        if (!response.IsSuccessStatusCode)
            throw new UsageException($"Usage check failed ({(int)response.StatusCode})", TimeSpan.FromMinutes(2));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Parse(doc.RootElement, DateTime.UtcNow);
    }

    /// <summary>
    /// Shape: { "five_hour": { "utilization": 34.0, "resets_at": "…" }, "seven_day": {…}, "seven_day_opus": {…}, … }.
    /// Every seven_day_* entry with data becomes its own weekly row, so new model limits show up without a code change.
    /// </summary>
    public static UsageReport Parse(JsonElement root, DateTime now)
    {
        Limit? session = null;
        Limit? week = null;
        var models = new List<Limit>();
        foreach (var p in root.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object
                || !p.Value.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) continue;
            var limit = new Limit(p.Name, Label(p.Name), u.GetDouble(), Json.Time(p.Value, "resets_at"));
            if (p.Name == "five_hour") session = limit;
            else if (p.Name == "seven_day") week = limit;
            // Unused model limits come back as 0 % with no reset time; leave them out
            else if (p.Name.StartsWith("seven_day_") && p.Name != "seven_day_oauth_apps"
                     && (limit.Percent > 0 || limit.ResetsAt != null)) models.Add(limit);
        }
        var weekly = new List<Limit>();
        if (week != null) weekly.Add(week);
        weekly.AddRange(models);
        return new UsageReport(session, weekly, now);
    }

    static string Label(string key) =>
        key == "seven_day" ? "WEEK"
        : key.StartsWith("seven_day_") ? key["seven_day_".Length..].Replace('_', ' ').ToUpperInvariant() + " · WEEK"
        : key.Replace('_', ' ').ToUpperInvariant();

    /// <summary>CLAUDE_CODE_OAUTH_TOKEN (from `claude setup-token`) wins; otherwise Claude Code's saved login.</summary>
    static string ReadToken()
    {
        if (Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN") is { Length: > 0 } env) return env;

        var path = Path.Combine(ConfigDir, ".credentials.json");
        if (!File.Exists(path))
            throw new UsageException("No Claude Code login on this PC yet.", TimeSpan.FromMinutes(1), signIn: true);
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || Json.Str(oauth, "accessToken") is not { Length: > 0 } token)
                throw new UsageException("No Claude Code login on this PC yet.", TimeSpan.FromMinutes(1), signIn: true);
            if (Json.Ms(oauth, "expiresAt") is var expires && expires != DateTime.MinValue && expires < DateTime.UtcNow)
                throw new UsageException("Sign-in expired.", TimeSpan.FromMinutes(1), signIn: true);
            return token;
        }
        catch (JsonException)
        {
            throw new UsageException("Couldn't read Claude Code's login file", TimeSpan.FromMinutes(1)); // mid-write; retry
        }
    }

    public static async Task<ServiceStatus> FetchStatusAsync()
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync("https://status.claude.com/api/v2/status.json"));
        var status = doc.RootElement.GetProperty("status");
        var health = Json.Str(status, "indicator") switch
        {
            "none" => ServiceHealth.Operational,
            "minor" => ServiceHealth.Minor,
            "major" => ServiceHealth.Major,
            "critical" => ServiceHealth.Critical,
            _ => ServiceHealth.Unknown,
        };
        return new ServiceStatus(health, health == ServiceHealth.Operational
            ? "All Claude systems operational"
            : Json.Str(status, "description") ?? "Claude status unknown");
    }
}
