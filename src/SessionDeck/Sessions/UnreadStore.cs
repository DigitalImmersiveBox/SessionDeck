using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeSessions;

/// <summary>
/// Reads the Claude app's own "unread" list (the sidebar dots): sessions that finished
/// while you weren't looking at them. The app keeps it in its local storage (a LevelDB
/// folder) under the key epitaxy-unread-v1. The newest copy is in the .log file, or in
/// the newest .ldb table after a compaction.
/// </summary>
sealed class UnreadStore
{
    static readonly Regex Value = new(@"""unreadIds"":\[([^\]]*)\],""explicitUnreadIds"":\[([^\]]*)\]", RegexOptions.Compiled);
    static readonly Regex Id = new(@"local_[0-9a-f-]{36}", RegexOptions.Compiled);

    readonly IReadOnlyList<string> folders;
    DateTime stamp;
    HashSet<string>? cached;

    public UnreadStore(IEnumerable<string> accountDirs) =>
        folders = accountDirs.Select(d => Path.Combine(d, "Local Storage", "leveldb")).Where(Directory.Exists).ToList();

    /// <summary>Local session ids marked unread, or null when the list can't be read.</summary>
    public HashSet<string>? Read()
    {
        List<FileInfo> files;
        try
        {
            files = folders.SelectMany(d => new DirectoryInfo(d).EnumerateFiles())
                .Where(f => f.Extension is ".log" or ".ldb")
                .ToList();
        }
        catch { return cached; }
        if (files.Count == 0) return null;

        var newest = files.Max(f => f.LastWriteTimeUtc);
        if (newest == stamp) return cached;

        HashSet<string>? found = null;
        foreach (var file in files.OrderBy(f => f.Extension == ".log" ? 0 : 1).ThenByDescending(f => f.LastWriteTimeUtc))
        {
            string text;
            try
            {
                using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.Latin1);
                text = reader.ReadToEnd();
            }
            catch { continue; }

            var matches = Value.Matches(text);
            if (matches.Count == 0) continue;
            var last = matches[^1];
            found = Id.Matches(last.Groups[1].Value + last.Groups[2].Value).Select(m => m.Value).ToHashSet();
            break;
        }

        stamp = newest;
        cached = found;
        return found;
    }
}
