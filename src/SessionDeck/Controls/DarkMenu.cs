using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace ClaudeSessions;

/// <summary>Dark styling for the tray and widget menus: dark background, light text, light check marks.</summary>
static class DarkMenu
{
    static readonly Color Text = Color.FromArgb(0xE6, 0xE7, 0xEA);
    static readonly Color Back = Color.FromArgb(0x1E, 0x1E, 0x21);
    static readonly Color Hover = Color.FromArgb(0x2E, 0x2F, 0x35);
    static readonly Color Line = Color.FromArgb(0x3B, 0x3B, 0x41);

    public static WinForms.ContextMenuStrip Create()
    {
        var menu = new WinForms.ContextMenuStrip { Renderer = new Renderer(), ShowImageMargin = false, ShowCheckMargin = true };
        return menu;
    }

    sealed class Renderer : WinForms.ToolStripProfessionalRenderer
    {
        public Renderer() : base(new Colors()) => RoundedEdges = false;

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Text, 1.6f);
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
                new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.28f),
            });
        }
    }

    sealed class Colors : WinForms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => Back;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}
