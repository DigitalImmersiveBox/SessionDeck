using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using Color = System.Drawing.Color;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ClaudeSessions;

public enum IconSource { Generated, ProjectFile, Custom }

/// <summary>
/// What a project row and its session cards show: the picture and where it came from.
/// The card's is drawn at its own size, so a generated icon's cells stay crisp there too.
/// </summary>
public sealed record ProjectIconLook(ImageSource Image, ImageSource CardImage, IconSource Source, string? File, bool Overridden)
{
    public string Tooltip => Source switch
    {
        IconSource.Custom => $"Your icon ({System.IO.Path.GetFileName(File)})",
        IconSource.ProjectFile => $"From the project: {File}",
        _ => "Made from the folder path",
    };
}

/// <summary>
/// Project icons: the project's own icon file when it has one (favicon, app.ico, icon.png…),
/// else a pixel identicon worked out from the folder path, so it's the same on every run.
/// </summary>
static class ProjectIcon
{
    /// <summary>Settings value for "always use the generated icon, even if the project has its own".</summary>
    public const string GeneratedOverride = "generated";

    public static string CustomDir => System.IO.Path.Combine(Settings.DataDir, "icons");

    static readonly string[] Extensions = { ".svg", ".ico", ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    // Where icons usually live, checked in the project and in each folder directly inside it
    static readonly string[] Subfolders = { "", "public", "static", "assets", "images", "img", "res", "resources",
        "src", "src/assets", "app", "app/public", "web", "www", "WebSite", "wwwroot" };

    static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", "bin", "obj", "dist", "build", "out", "Library", "Temp", "Logs", "Packages", "UserSettings", "venv" };

    // ---- Finding the project's own icon --------------------------------------

    /// <summary>The project's best icon file, or null. Touches the disk: call it off the UI thread.</summary>
    public static string? FindInProject(string root)
    {
        (string File, int Score)? best = null;
        void Consider(string dir, int depth)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir).ToList(); }
            catch { return; }
            foreach (var file in files)
            {
                if (Rank(System.IO.Path.GetFileName(file)) is not int rank) continue;
                int score = rank * 10 + depth;
                if (best == null || score < best.Value.Score) best = (file, score);
            }
        }

        foreach (var sub in Subfolders)
            Consider(System.IO.Path.Combine(root, sub), sub.Length == 0 ? 0 : 1);
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(root)
                .Where(d => System.IO.Path.GetFileName(d) is { } name && !name.StartsWith('.') && !SkipFolders.Contains(name))
                .Take(40).ToList();
        }
        catch { children = Array.Empty<string>(); }
        foreach (var child in children)
            foreach (var sub in Subfolders)
                Consider(System.IO.Path.Combine(child, sub), sub.Length == 0 ? 1 : 2);
        return best?.File;
    }

    /// <summary>Lower is better; null when the name doesn't look like an icon.</summary>
    static int? Rank(string fileName)
    {
        string ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        if (!Extensions.Contains(ext)) return null;
        string name = System.IO.Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        if (name == "favicon") return 0;
        if (name is "icon" or "app" or "appicon" or "app-icon" or "app_icon" or "logo-icon") return 1;
        if (ext == ".ico") return 2;
        if (name.StartsWith("apple-touch-icon") || name.StartsWith("android-chrome") || name.StartsWith("favicon")) return 3;
        if (name.StartsWith("icon") || name.EndsWith("-icon") || name.EndsWith("_icon")) return 4;
        if (name == "logo" || name.StartsWith("logo") || name.EndsWith("logo") || name.Contains("_logo")) return 5;
        return null;
    }

    // ---- Loading a file ----------------------------------------------------

    /// <summary>The image, or null if the file can't be read. Call it on the UI thread.</summary>
    public static ImageSource? Load(string file, int size)
    {
        try
        {
            ImageSource image;
            string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".svg")
            {
                using var reader = new FileSvgReader(new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true });
                var drawing = reader.Read(file);
                if (drawing == null || drawing.Bounds.IsEmpty) return null;
                image = new DrawingImage(drawing);
            }
            else
            {
                // Read into memory so the file isn't kept locked; for .ico, the smallest frame that's big enough
                using var stream = new MemoryStream(File.ReadAllBytes(file));
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                image = decoder.Frames.OrderBy(f => f.PixelWidth).FirstOrDefault(f => f.PixelWidth >= size)
                        ?? decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            }
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>For --project-icons: each project as a panel row, with the icon's source beside it (2× scale).</summary>
    internal static void SaveSheet(IReadOnlyList<ProjectEntry> projects, Dictionary<string, string> overrides, string file)
    {
        const int size = 36;
        var list = new System.Windows.Controls.StackPanel { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x12, 0x13, 0x16)) };
        foreach (var p in projects)
        {
            string? chosen = overrides.FirstOrDefault(o => string.Equals(o.Key, p.Path, StringComparison.OrdinalIgnoreCase)).Value;
            string? own = chosen == null ? FindInProject(p.Path) : null;
            var (image, source) = chosen != null && chosen != GeneratedOverride && Load(chosen, size) is { } custom ? (custom, "your pick")
                : own != null && Load(own, size) is { } found ? (found, System.IO.Path.GetRelativePath(p.Path, own))
                : ((ImageSource)Generate(p.Path, size), "generated");
            var row = new System.Windows.Controls.DockPanel { Margin = new System.Windows.Thickness(16, 7, 16, 7), Width = 420 };
            row.Children.Add(new System.Windows.Controls.Image { Source = image, Width = 18, Height = 18, Margin = new System.Windows.Thickness(0, 0, 9, 0) });
            row.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"{p.Name}   ·   {source}",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB9, 0xBC, 0xC4)),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 13, VerticalAlignment = System.Windows.VerticalAlignment.Center,
            });
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(row, BitmapScalingMode.HighQuality);
            list.Children.Add(row);
        }
        list.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        list.Arrange(new System.Windows.Rect(list.DesiredSize));
        list.UpdateLayout();
        UsageWidget.SavePng(list, file);
    }

    // ---- The generated identicon ---------------------------------------------

    /// <summary>A 5×5 mirrored pixel pattern on a tinted rounded tile; colour and pattern come from the path.</summary>
    public static BitmapSource Generate(string path, int size)
    {
        var hash = HashOf(path);
        double hue = ((hash[0] << 8) | hash[1]) % 360;
        var ink = FromHsl(hue, 0.62 + hash[2] % 20 / 100.0, 0.62 + hash[3] % 10 / 100.0);
        var tile = Mix(ink, Color.FromArgb(0x1D, 0x1F, 0x24), 0.2);

        // 15 bits: three columns of five, mirrored into five columns; keep it neither too empty nor too full
        var on = new bool[5, 3];
        int count = 0;
        for (int i = 0; i < 15; i++)
            if (on[i / 3, i % 3] = (hash[4 + i / 8] >> (i % 8) & 1) == 1) count++;
        if (count < 6 || count > 10)
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 3; c++)
                    on[r, c] = !on[r, c];

        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var tileBrush = new SolidBrush(tile))
            using (var shape = Rounded(new RectangleF(0, 0, size - 0.5f, size - 0.5f), size * 0.24f))
                g.FillPath(tileBrush, shape);

            // Pixel-snapped cells, as in MascotIcon: crisp even when a cell isn't a whole number of pixels
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            double cell = size * 0.68 / 5, origin = (size - cell * 5) / 2;
            using var brush = new SolidBrush(ink);
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    if (!on[r, Math.Min(c, 4 - c)]) continue;
                    int x0 = (int)Math.Round(origin + c * cell), x1 = (int)Math.Round(origin + (c + 1) * cell);
                    int y0 = (int)Math.Round(origin + r * cell), y1 = (int)Math.Round(origin + (r + 1) * cell);
                    g.FillRectangle(brush, x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
                }
        }

        using var png = new MemoryStream();
        bmp.Save(png, ImageFormat.Png);
        png.Position = 0;
        var frame = BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    /// <summary>A short name for files that belong to a project (your picked icon's copy).</summary>
    public static string KeyOf(string path) => Convert.ToHexString(HashOf(path), 0, 8).ToLowerInvariant();

    static byte[] HashOf(string path) => SHA256.HashData(Encoding.UTF8.GetBytes(path.TrimEnd('\\', '/').ToLowerInvariant()));

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static Color Mix(Color a, Color b, double amountOfA) => Color.FromArgb(
        (int)Math.Round(a.R * amountOfA + b.R * (1 - amountOfA)),
        (int)Math.Round(a.G * amountOfA + b.G * (1 - amountOfA)),
        (int)Math.Round(a.B * amountOfA + b.B * (1 - amountOfA)));

    static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
}
