using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ClaudeSessions;

/// <summary>
/// Runs the Claude Code CLI for sign-in chores. This app never reads or writes the token itself:
/// Claude Code renews or replaces its own login.
/// </summary>
static class ClaudeCli
{
    /// <summary>The native installer's claude.exe first, then whatever `claude` is on PATH.</summary>
    public static string? Find()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string> { Path.Combine(home, ".local", "bin", "claude.exe") };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(dir.Trim(), "claude.exe"));
            candidates.Add(Path.Combine(dir.Trim(), "claude.cmd"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// `claude auth status` makes Claude Code renew an expired sign-in (it uses no plan usage).
    /// Returns false when the CLI is missing, fails or takes too long.
    /// </summary>
    public static async Task<bool> RenewSignInAsync()
    {
        var cli = Find();
        if (cli == null) return false;
        var psi = Command(cli, "auth status");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        using var process = Process.Start(psi);
        if (process == null) return false;
        _ = process.StandardOutput.ReadToEndAsync(); // drain, so a full pipe can't stall it
        _ = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            return false;
        }
        return process.ExitCode == 0;
    }

    /// <summary>Opens a terminal running `claude auth login`, where you finish signing in in the browser.</summary>
    public static bool OpenLogin()
    {
        var cli = Find();
        if (cli == null) return false;
        Process.Start(new ProcessStartInfo("cmd.exe", $"/k \"\"{cli}\" auth login\"") { UseShellExecute = true });
        return true;
    }

    static ProcessStartInfo Command(string cli, string args) =>
        cli.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("cmd.exe", $"/c \"\"{cli}\" {args}\"")
            : new ProcessStartInfo(cli, args);
}
