using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows;

namespace ClaudeSessions;

public partial class App : Application
{
    Mutex? mutex;
    EventWaitHandle? showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --dump <file>: write one scan as JSON and exit (for troubleshooting)
        int dump = Array.IndexOf(e.Args, "--dump");
        if (dump >= 0)
        {
            string path = e.Args.Length > dump + 1 ? e.Args[dump + 1] : "sessions-dump.json";
            var snaps = new SessionScanner().Scan(TimeSpan.FromHours(24));
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            File.WriteAllText(path, JsonSerializer.Serialize(snaps, options));
            Shutdown();
            return;
        }

        // --mascot dib|dibwhite|clawd: who to draw for --snapshot and --write-icon (default dib)
        int mascotArg = Array.IndexOf(e.Args, "--mascot");
        var mascot = mascotArg >= 0 && e.Args.Length > mascotArg + 1 && Enum.TryParse(e.Args[mascotArg + 1], true, out MascotKind kind)
            ? kind : MascotKind.Dib;

        // --snapshot <dir>: render every usage-widget demo state to PNG and exit (no login, no network)
        int snapshot = Array.IndexOf(e.Args, "--snapshot");
        if (snapshot >= 0)
        {
            string dir = e.Args.Length > snapshot + 1 ? e.Args[snapshot + 1] : "snapshots";
            var settings = new Settings { Mascot = mascot };
            new UsageWidget(new UsageMonitor(), settings, demo: true, snapshot: true).SaveSnapshots(dir);
            SettingsDialog.SaveSnapshot(settings, Path.Combine(dir, "settings.png"));
            Shutdown();
            return;
        }

        // --token-charts <dir>: render the token view's charts from your real logs (24 h and 7 days) to PNG and exit
        int tokenCharts = Array.IndexOf(e.Args, "--token-charts");
        if (tokenCharts >= 0)
        {
            TokenView.SaveSnapshots(e.Args.Length > tokenCharts + 1 ? e.Args[tokenCharts + 1] : "token-charts");
            Shutdown();
            return;
        }

        // --signatures <dir>: render every mascot's own move, frame by frame, to <dir>/<mascot>/ and exit
        int signatures = Array.IndexOf(e.Args, "--signatures");
        if (signatures >= 0)
        {
            string dir = e.Args.Length > signatures + 1 ? e.Args[signatures + 1] : "signatures";
            foreach (var (each, _) in Mascot.Choices)
                new UsageWidget(new UsageMonitor(), new Settings { Mascot = each }, demo: true, snapshot: true)
                    .SaveSignatureFrames(Path.Combine(dir, each.ToString()));
            Shutdown();
            return;
        }

        // --stories <dir>: render every mascot's story, frame by frame, to <dir>/<mascot>/ and exit
        int stories = Array.IndexOf(e.Args, "--stories");
        if (stories >= 0)
        {
            string dir = e.Args.Length > stories + 1 ? e.Args[stories + 1] : "stories";
            foreach (var (each, _) in Mascot.Choices.Where(c => c.Kind != MascotKind.DibWhite))
                new UsageWidget(new UsageMonitor(), new Settings { Mascot = each }, demo: true, snapshot: true)
                    .SaveStoryFrames(Path.Combine(dir, each.ToString()));
            Shutdown();
            return;
        }

        // --runner <dir> [--daily]: the runner game playing itself for 14 s, a frame per 1/15 s, ending on a "needs you" notice
        int runner = Array.IndexOf(e.Args, "--runner");
        if (runner >= 0)
        {
            string dir = e.Args.Length > runner + 1 ? e.Args[runner + 1] : "runner";
            new RunnerWindow(new Settings { Mascot = mascot, RunnerDaily = e.Args.Contains("--daily") }).SaveAutopilotFrames(dir, 14);
            Shutdown();
            return;
        }

        // --write-icon <file.ico>: regenerate the .exe icon (the pixel mascot) and exit
        int writeIcon = Array.IndexOf(e.Args, "--write-icon");
        if (writeIcon >= 0)
        {
            MascotIcon.WriteIco(Mascot.For(mascot), e.Args.Length > writeIcon + 1 ? e.Args[writeIcon + 1] : "app.ico", 16, 20, 24, 32, 40, 48, 64, 256);
            Shutdown();
            return;
        }

        // --project-icons <file.png>: draw every project with the icon it gets and where it came from, and exit
        int projectIcons = Array.IndexOf(e.Args, "--project-icons");
        if (projectIcons >= 0)
        {
            var scanner = new SessionScanner();
            scanner.Scan(TimeSpan.FromHours(24));
            ProjectIcon.SaveSheet(scanner.Projects, Settings.Load().ProjectIcons,
                e.Args.Length > projectIcons + 1 ? e.Args[projectIcons + 1] : "project-icons.png");
            Shutdown();
            return;
        }

        // Single instance: a second launch just brings the first window up
        mutex = new Mutex(true, "SessionDeck.SingleInstance", out bool created);
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "SessionDeck.Show");
        if (!created)
        {
            showEvent.Set();
            Shutdown();
            return;
        }

        var window = new MainWindow(demoWidget: e.Args.Contains("--demo"));
        var listener = new Thread(() =>
        {
            while (showEvent.WaitOne())
                window.Dispatcher.Invoke(window.ShowFromTray);
        }) { IsBackground = true };
        listener.Start();

        if (!e.Args.Contains("--tray"))
            window.Show();
    }
}
