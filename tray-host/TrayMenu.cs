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

    internal sealed class TrayMenu : ContextMenuStrip
    {
        private Font menuFont;
        internal float ScaleFactor = 1f;
        internal bool Dark;
        internal bool Contrast;
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
            Opened += delegate
            {
                // Windows 11 rounds the native popup; older Windows keeps the flat border.
                try { int preference = 2; DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int)); }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            };
            Prepare(1f);
        }

        internal void SetTheme(bool dark, bool contrast)
        {
            Dark = dark;
            Contrast = contrast || SystemInformation.HighContrast;
            BackColor = Contrast ? SystemColors.Menu : Dark ? Color.FromArgb(36, 36, 41) : Color.FromArgb(250, 250, 252);
            ForeColor = Contrast ? SystemColors.MenuText : Dark ? Color.FromArgb(240, 240, 244) : Color.FromArgb(36, 36, 42);
            Invalidate(true);
        }

        internal int Px(float value) { return Math.Max(1, (int)Math.Round(value * ScaleFactor)); }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width < 2 || Height < 2) return;
            using (var shape = TrayMenuRenderer.Rounded(new Rectangle(0, 0, Width, Height), Px(8)))
            {
                var previous = Region;
                Region = new Region(shape);
                if (previous != null) previous.Dispose();
            }
        }

        internal void Prepare(float scale)
        {
            ScaleFactor = Math.Max(1f, scale);
            var previous = menuFont;
            menuFont = new Font(SystemFonts.MessageBoxFont.FontFamily,
                Math.Max(9f, SystemFonts.MessageBoxFont.SizeInPoints) * 96f / 72f * ScaleFactor, FontStyle.Regular, GraphicsUnit.Pixel);
            Font = menuFont;
            if (previous != null) previous.Dispose();
            int inset = Px(6), row = Math.Max(Px(34), menuFont.Height + Px(14));
            int textWidth = Math.Max(TextRenderer.MeasureText(show.Text, menuFont).Width, TextRenderer.MeasureText(quit.Text, menuFont).Width);
            int width = Math.Max(Px(200), textWidth + Px(62));
            Padding = new Padding(inset);
            show.Margin = quit.Margin = separator.Margin = Padding.Empty;
            show.Size = quit.Size = new Size(width - inset * 2, row);
            separator.Size = new Size(width - inset * 2, Px(9));
            Size = new Size(width, row * 2 + separator.Height + inset * 2);
            UpdateRegion();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && menuFont != null) { menuFont.Dispose(); menuFont = null; }
        }
    }

    internal sealed class TrayMenuRenderer : ToolStripRenderer
    {
        internal static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var menu = (TrayMenu)e.ToolStrip;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = Rounded(new Rectangle(0, 0, menu.Width - 1, menu.Height - 1), menu.Px(8)))
            using (var brush = new SolidBrush(menu.BackColor)) e.Graphics.FillPath(brush, shape);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var menu = (TrayMenu)e.ToolStrip;
            var color = menu.Contrast ? SystemColors.WindowFrame : menu.Dark ? Color.FromArgb(65, 65, 73) : Color.FromArgb(214, 214, 221);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = Rounded(new Rectangle(0, 0, menu.Width - 1, menu.Height - 1), menu.Px(8)))
            using (var pen = new Pen(color)) e.Graphics.DrawPath(pen, shape);
        }

        private static Color TextColor(TrayMenu menu, ToolStripItem item)
        {
            if (menu.Contrast) return item.Selected ? SystemColors.HighlightText : SystemColors.MenuText;
            if (item.Name == "quit" && item.Selected) return menu.Dark ? Color.FromArgb(255, 150, 158) : Color.FromArgb(184, 40, 58);
            return menu.ForeColor;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var menu = (TrayMenu)e.ToolStrip;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (e.Item.Selected)
            {
                Color color = menu.Contrast ? SystemColors.Highlight : menu.Dark ? Color.FromArgb(55, 55, 63) : Color.FromArgb(232, 232, 239);
                if (!menu.Contrast && e.Item.Name == "quit") color = menu.Dark ? Color.FromArgb(65, 39, 47) : Color.FromArgb(253, 233, 237);
                using (var shape = Rounded(new Rectangle(menu.Px(3), menu.Px(2), e.Item.Width - menu.Px(6), e.Item.Height - menu.Px(4)), menu.Px(5)))
                using (var brush = new SolidBrush(color)) e.Graphics.FillPath(brush, shape);
            }
            var state = e.Graphics.Save();
            e.Graphics.TranslateTransform(menu.Px(12), (e.Item.Height - menu.Px(16)) / 2f);
            e.Graphics.ScaleTransform(menu.ScaleFactor, menu.ScaleFactor);
            using (var pen = new Pen(TextColor(menu, e.Item), 1.35f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                if (e.Item.Name == "quit")
                {
                    e.Graphics.DrawArc(pen, 2, 2, 12, 12, -50, 280);
                    e.Graphics.DrawLine(pen, 8, 1, 8, 7);
                }
                else
                {
                    e.Graphics.DrawRectangle(pen, 1, 2, 14, 12);
                    e.Graphics.DrawLine(pen, 1, 6, 15, 6);
                }
            }
            e.Graphics.Restore(state);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var menu = (TrayMenu)e.ToolStrip;
            var bounds = new Rectangle(menu.Px(38), 0, e.Item.Width - menu.Px(46), e.Item.Height);
            TextRenderer.DrawText(e.Graphics, e.Text, menu.Font, bounds, TextColor(menu, e.Item),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var menu = (TrayMenu)e.ToolStrip;
            var color = menu.Contrast ? SystemColors.MenuText : menu.Dark ? Color.FromArgb(63, 63, 70) : Color.FromArgb(222, 222, 229);
            using (var pen = new Pen(color)) e.Graphics.DrawLine(pen, menu.Px(9), e.Item.Height / 2, e.Item.Width - menu.Px(9), e.Item.Height / 2);
        }
    }
}
