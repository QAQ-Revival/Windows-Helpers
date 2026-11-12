using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QaqrTrayHost
{
    internal static class DisplayScale
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int awareness);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

        internal static void Initialize()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
            catch (EntryPointNotFoundException) { }
            try { if (SetProcessDpiAwareness(2) == 0) return; }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }

        internal static float At(Point point)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out x, out y) == 0 && x > 0) return x / 96f;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) return graphics.DpiX / 96f;
        }
    }

    // The native twin of the manager's dropdown menu: the same surface, row and icon sizes,
    // with its translucent colours flattened onto the surface.
    internal sealed class TrayMenu : ContextMenuStrip
    {
        internal Color Surface, Hover, Ink, HoverInk, Glyph, Line, Edge;
        internal float ScaleFactor = 1f;
        // True when Windows draws the rounded frame, its border and its shadow.
        internal bool Framed;
        // The gap between the popup's edge and its rows.
        internal int Inset;
        private bool contrast;
        private Font menuFont;
        private readonly ToolStripMenuItem show;
        private readonly ToolStripMenuItem quit;
        private readonly ToolStripSeparator separator = new ToolStripSeparator();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        internal TrayMenu(string showLabel, string quitLabel, Action onShow, Action onQuit)
        {
            ShowImageMargin = false;
            ShowCheckMargin = false;
            DropShadowEnabled = true;
            AutoSize = false;
            Renderer = new TrayMenuRenderer();
            show = new ToolStripMenuItem(showLabel, null, delegate { onShow(); }) { Name = "show", AutoSize = false };
            quit = new ToolStripMenuItem(quitLabel, null, delegate { onQuit(); }) { Name = "quit", AutoSize = false };
            separator.AutoSize = false;
            Items.AddRange(new ToolStripItem[] { show, separator, quit });
            Opening += delegate { Prepare(DisplayScale.At(Cursor.Position)); };
            SetTheme(false, false);
            Prepare(1f);
        }

        internal void SetTheme(bool dark, bool highContrast)
        {
            contrast = highContrast || SystemInformation.HighContrast;
            if (contrast)
            {
                Surface = SystemColors.Menu; Hover = SystemColors.Highlight; Ink = Glyph = Line = SystemColors.MenuText;
                HoverInk = SystemColors.HighlightText; Edge = SystemColors.WindowFrame;
            }
            else if (dark)
            {
                Surface = Color.FromArgb(29, 28, 35); Hover = Color.FromArgb(37, 36, 44); Ink = HoverInk = Color.FromArgb(241, 240, 245);
                Glyph = Color.FromArgb(176, 174, 187); Line = Color.FromArgb(45, 44, 50); Edge = Color.FromArgb(61, 60, 66);
            }
            else
            {
                Surface = Color.White; Hover = Color.FromArgb(240, 239, 243); Ink = HoverInk = Color.FromArgb(27, 26, 33);
                Glyph = Color.FromArgb(91, 89, 102); Line = Color.FromArgb(234, 234, 235); Edge = Color.FromArgb(213, 213, 215);
            }
            BackColor = Surface;
            ForeColor = Ink;
            Frame();
            Invalidate(true);
        }

        internal int Px(float value) { return Math.Max(1, (int)(value * ScaleFactor + 0.5f)); }

        internal int Hairline { get { return Math.Max(1, (int)ScaleFactor); } }

        // The menu layout resets Padding to this default whenever it measures its items.
        protected override Padding DefaultPadding { get { return new Padding(Inset); } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Frame();
        }

        private void Frame()
        {
            if (!IsHandleCreated) return;
            Framed = false;
            try
            {
                // Windows 11 rounds, outlines and shades the popup itself. Older Windows refuses and keeps a square one.
                int round = 2, edge = contrast ? -1 : ColorTranslator.ToWin32(Edge);
                Framed = DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)) == 0
                    && DwmSetWindowAttribute(Handle, 34, ref edge, sizeof(int)) == 0;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        internal void Prepare(float scale)
        {
            ScaleFactor = Math.Max(1f, scale);
            // 13 px matches the manager; a larger system text size enlarges it in step.
            using (var system = SystemFonts.MessageBoxFont)
            {
                float size = 13f * Math.Max(1f, system.SizeInPoints / 9f) * ScaleFactor;
                // Assigning an equal font keeps the old object, so only a changed one may replace and release it.
                if (menuFont == null || menuFont.Size != size || menuFont.Name != system.Name)
                {
                    var previous = menuFont;
                    menuFont = new Font(system.Name, size, FontStyle.Regular, GraphicsUnit.Pixel);
                    Font = menuFont;
                    if (previous != null) previous.Dispose();
                }
            }
            // Windows draws its border over the outermost pixel.
            Inset = Px(4) + 1;
            int row = Math.Max(Px(32), menuFont.Height + Px(12));
            int label = Math.Max(TextRenderer.MeasureText(show.Text, menuFont).Width, TextRenderer.MeasureText(quit.Text, menuFont).Width);
            int width = Math.Max(Px(180), Inset * 2 + Px(8 + 16 + 8) + label + Px(16));
            Padding = new Padding(Inset);
            show.Margin = quit.Margin = separator.Margin = Padding.Empty;
            // The layout honours the vertical padding only: rows start at the left edge and separators pick their own
            // width, so rows reach from that edge to the right inset and the renderer draws from the inset.
            show.Size = quit.Size = new Size(width - Inset, row);
            separator.Size = new Size(width - 4, Px(4) * 2 + Hairline);
            Size = new Size(width, row * 2 + separator.Height + Inset * 2);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && menuFont != null) { menuFont.Dispose(); menuFont = null; }
        }
    }

    internal sealed class TrayMenuRenderer : ToolStripRenderer
    {
        private static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Line icons in the manager's style. Straight strokes sit on whole pixels so they stay sharp at every scale.
        internal static void DrawGlyph(Graphics graphics, bool quit, Color color, int x, int y, int box, float scale)
        {
            int stroke = Math.Max(1, (int)(1.17f * scale + 0.5f));
            float unit = box / 16f, half = stroke / 2f;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            using (var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (quit)
                {
                    float centre = x + (box - stroke) / 2 + half, middle = y + 8.75f * unit, radius = 5.5f * unit;
                    graphics.DrawArc(pen, centre - radius, middle - radius, radius * 2, radius * 2, -45, 270);
                    graphics.DrawLine(pen, centre, y + 1.75f * unit, centre, y + 8f * unit);
                }
                else
                {
                    int side = (int)(2 * unit + 0.5f), top = (int)(2.5f * unit + 0.5f);
                    var frame = new RectangleF(x + side + half, y + top + half, box - side * 2 - stroke, box - top * 2 - stroke);
                    using (var shape = Rounded(frame, 1.25f * unit)) graphics.DrawPath(pen, shape);
                    float bar = frame.Top + (int)(3 * unit + 0.5f);
                    pen.StartCap = pen.EndCap = LineCap.Flat;
                    graphics.DrawLine(pen, frame.Left, bar, frame.Right, bar);
                }
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(((TrayMenu)e.ToolStrip).Surface)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var menu = (TrayMenu)e.ToolStrip;
            if (menu.Framed) return;
            using (var pen = new Pen(menu.Edge)) e.Graphics.DrawRectangle(pen, 0, 0, menu.Width - 1, menu.Height - 1);
        }

        // The row area between the menu's insets, in the item's own coordinates.
        private static Rectangle Row(TrayMenu menu, ToolStripItem item)
        {
            return new Rectangle(menu.Inset - item.Bounds.X, 0, menu.Width - menu.Inset * 2, item.Height);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var menu = (TrayMenu)e.ToolStrip;
            var row = Row(menu, e.Item);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            if (e.Item.Selected)
                using (var shape = Rounded(row, menu.Px(4)))
                using (var brush = new SolidBrush(menu.Hover)) e.Graphics.FillPath(brush, shape);
            int box = menu.Px(16);
            DrawGlyph(e.Graphics, e.Item.Name == "quit", e.Item.Selected ? menu.HoverInk : menu.Glyph,
                row.Left + menu.Px(8), (row.Height - box) / 2, box, menu.ScaleFactor);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var menu = (TrayMenu)e.ToolStrip;
            var row = Row(menu, e.Item);
            int left = menu.Px(8 + 16 + 8);
            TextRenderer.DrawText(e.Graphics, e.Text, menu.Font, new Rectangle(row.Left + left, 0, row.Width - left - menu.Px(8), row.Height),
                e.Item.Selected ? menu.HoverInk : menu.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var menu = (TrayMenu)e.ToolStrip;
            var row = Row(menu, e.Item);
            using (var brush = new SolidBrush(menu.Line))
                e.Graphics.FillRectangle(brush, row.Left + menu.Px(4), (row.Height - menu.Hairline) / 2, row.Width - menu.Px(8), menu.Hairline);
        }
    }
}
