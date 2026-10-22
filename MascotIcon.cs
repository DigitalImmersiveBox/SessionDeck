using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace ClaudeSessions;

/// <summary>
/// The app's icon: the pixel mascot (as in the banner and widget), with a status badge.
/// A coral badge with the count when sessions need you, a blue dot while something works.
/// </summary>
static class MascotIcon
{
    static readonly Color Eye = Color.FromArgb(0x16, 0x16, 0x18);
    static readonly Color Ink = Color.FromArgb(0x14, 0x14, 0x18);

    public static Bitmap Render(Mascot mascot, int size, int needYou, bool working)
    {
        var pixels = mascot.Icon;
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        // Pixel-snapped cells: crisp at any size, even when a cell isn't a whole number of pixels
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        double cell = size * 0.94 / pixels[0].Length;
        double ox = (size - cell * pixels[0].Length) / 2;
        double oy = (size - cell * pixels.Length) / 2 + (needYou > 0 || working ? -size * 0.04 : 0);
        var brushes = mascot.Palette.ToDictionary(p => p.Key, p => new SolidBrush(Color.FromArgb(p.Value.R, p.Value.G, p.Value.B)));
        brushes['E'] = new SolidBrush(Eye);
        for (int r = 0; r < pixels.Length; r++)
            for (int c = 0; c < pixels[r].Length; c++)
            {
                if (!brushes.TryGetValue(pixels[r][c], out var brush)) continue;
                int x0 = (int)Math.Round(ox + c * cell), x1 = (int)Math.Round(ox + (c + 1) * cell);
                int y0 = (int)Math.Round(oy + r * cell), y1 = (int)Math.Round(oy + (r + 1) * cell);
                g.FillRectangle(brush, x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
            }

        foreach (var b in brushes.Values) b.Dispose();

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Default;
        if (needYou > 0)
        {
            float d = size * 0.56f, x = size - d, y = size - d;
            using var ring = new SolidBrush(Ink);
            using var fill = new SolidBrush(Color.FromArgb(Palette.NeedsYou.R, Palette.NeedsYou.G, Palette.NeedsYou.B));
            g.FillEllipse(ring, x - size * 0.04f, y - size * 0.04f, d + size * 0.08f, d + size * 0.08f);
            g.FillEllipse(fill, x, y, d, d);
            string label = needYou > 9 ? "9+" : needYou.ToString();
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Segoe UI", d * (label.Length > 1 ? 0.55f : 0.72f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(label, font, ring, new RectangleF(x, y + d * 0.03f, d, d), format);
        }
        else if (working)
        {
            float d = size * 0.34f, x = size - d - size * 0.02f, y = size - d - size * 0.02f;
            using var ring = new SolidBrush(Ink);
            using var fill = new SolidBrush(Color.FromArgb(Palette.Working.R, Palette.Working.G, Palette.Working.B));
            g.FillEllipse(ring, x - size * 0.05f, y - size * 0.05f, d + size * 0.1f, d + size * 0.1f);
            g.FillEllipse(fill, x, y, d, d);
        }
        return bmp;
    }

    /// <summary>For the tray; the caller destroys the handle (DestroyIcon).</summary>
    public static IntPtr Handle(Mascot mascot, int size, int needYou, bool working)
    {
        using var bmp = Render(mascot, size, needYou, working);
        return bmp.GetHicon();
    }

    /// <summary>For the window and taskbar.</summary>
    public static BitmapSource Source(Mascot mascot, int size, int needYou, bool working)
    {
        using var bmp = Render(mascot, size, needYou, working);
        using var png = new MemoryStream();
        bmp.Save(png, ImageFormat.Png);
        png.Position = 0;
        var frame = BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    /// <summary>
    /// Writes a multi-size .ico, e.g. the .exe icon: the mascot with no badge. Classic 32-bit bitmap
    /// frames below 256 px (every icon reader handles those), PNG for the 256 px one.
    /// </summary>
    public static void WriteIco(Mascot mascot, string path, params int[] sizes)
    {
        var frames = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using var bmp = Render(mascot, sizes[i], 0, false);
            using var ms = new MemoryStream();
            if (sizes[i] >= 256) bmp.Save(ms, ImageFormat.Png);
            else WriteDib(bmp, ms);
            frames[i] = ms.ToArray();
        }
        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte dim = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
            w.Write(dim);
            w.Write(dim);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(frames[i].Length);
            w.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var f in frames) w.Write(f);
    }

    /// <summary>An icon frame as a DIB: header, BGRA rows bottom-up, then an empty AND mask (alpha does the masking).</summary>
    static void WriteDib(Bitmap bmp, Stream output)
    {
        int size = bmp.Width;
        var pixels = new byte[size * size * 4];
        var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int row = 0; row < size; row++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * size * 4, size * 4);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        int maskRow = (size + 31) / 32 * 4;
        using var w = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(40);                // BITMAPINFOHEADER size
        w.Write(size);
        w.Write(size * 2);          // colour rows + mask rows
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0);                 // BI_RGB
        w.Write(pixels.Length + maskRow * size);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        for (int row = size - 1; row >= 0; row--)
            w.Write(pixels, row * size * 4, size * 4);
        w.Write(new byte[maskRow * size]);
    }
}
