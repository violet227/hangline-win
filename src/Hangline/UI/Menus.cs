using System.Drawing;
using Forms = System.Windows.Forms;

namespace Hangline.UI;

/// Menus are WinForms menus: they open and close properly from a window
/// that never takes focus, and from the tray icon alike. A light renderer
/// keeps them close to the Windows 11 look.
internal static class Menus
{
    public static Forms.ContextMenuStrip New()
    {
        var dark = Theme.IsDark;
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = new Font("Segoe UI", 9.5f),
            Renderer = new Renderer(dark),
            Padding = new Forms.Padding(2, 4, 2, 4),
        };
        menu.BackColor = dark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF9, 0xF9, 0xF9);
        menu.ForeColor = dark ? Color.White : Color.FromArgb(0x1C, 0x1C, 0x1E);
        return menu;
    }

    public static Forms.ToolStripMenuItem Item(string title, Action action, bool enabled = true, bool check = false)
    {
        var mi = new Forms.ToolStripMenuItem(title) { Enabled = enabled, Checked = check, Padding = new Forms.Padding(4, 3, 4, 3) };
        mi.Click += (_, _) => action();
        return mi;
    }

    public static Forms.ToolStripMenuItem Sub(string title, params Forms.ToolStripItem[] items)
    {
        var mi = new Forms.ToolStripMenuItem(title) { Padding = new Forms.Padding(4, 3, 4, 3) };
        mi.DropDownItems.AddRange(items);
        if (mi.DropDown is Forms.ToolStripDropDownMenu dd)
        {
            dd.ShowImageMargin = false;
            dd.ShowCheckMargin = true;
            dd.Renderer = new Renderer(Theme.IsDark);
            dd.BackColor = Theme.IsDark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF9, 0xF9, 0xF9);
            dd.ForeColor = Theme.IsDark ? Color.White : Color.FromArgb(0x1C, 0x1C, 0x1E);
        }
        return mi;
    }

    private sealed class Renderer : Forms.ToolStripProfessionalRenderer
    {
        private readonly bool dark;
        public Renderer(bool dark) : base(new Colors(dark)) { this.dark = dark; RoundedEdges = true; }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Color.Gray : dark ? Color.White : Color.FromArgb(0x1C, 0x1C, 0x1E);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = dark ? Color.White : Color.FromArgb(0x1C, 0x1C, 0x1E);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            using var pen = new Pen(dark ? Color.White : Color.FromArgb(0x1C, 0x1C, 0x1E), 1.6f);
            float x = r.Left + r.Width * 0.2f, y = r.Top + r.Height * 0.55f;
            g.DrawLines(pen, new[]
            {
                new PointF(x, y),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f),
                new PointF(r.Left + r.Width * 0.82f, r.Top + r.Height * 0.28f),
            });
        }
    }

    private sealed class Colors : Forms.ProfessionalColorTable
    {
        private readonly bool dark;
        public Colors(bool dark) { this.dark = dark; UseSystemColors = false; }
        private Color Back => dark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF9, 0xF9, 0xF9);
        private Color Hover => dark ? Color.FromArgb(0x3D, 0x3D, 0x3D) : Color.FromArgb(0xE8, 0xE8, 0xEC);
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xD6, 0xD6, 0xDA);
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xE0, 0xE0, 0xE4);
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => Back;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}
