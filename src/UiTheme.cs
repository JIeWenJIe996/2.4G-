using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MchoseBattery
{
    internal sealed class UiTheme
    {
        public bool Dark;
        public Color Background, Card, Text, Muted, Border, Accent, AccentText, AccentSubtle, Track, Hover, Warning;

        public static UiTheme Current()
        {
            // 窗口跟随应用主题；任务栏的系统主题由 IconFactory 单独读取。
            bool dark = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    dark = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch { }
            return Create(dark);
        }

        public static UiTheme Create(bool dark)
        {
            // 高对比度模式优先使用用户的系统颜色，不能套用普通深浅色配色。
            if (SystemInformation.HighContrast)
                return new UiTheme { Dark = dark, Background = SystemColors.Window, Card = SystemColors.Window,
                    Text = SystemColors.WindowText, Muted = SystemColors.WindowText, Border = SystemColors.WindowText,
                    Accent = SystemColors.Highlight, AccentText = SystemColors.HighlightText, AccentSubtle = SystemColors.Control,
                    Track = SystemColors.ControlDark, Hover = SystemColors.Control, Warning = SystemColors.WindowText };
            return new UiTheme { Dark = dark,
                Background = ColorTranslator.FromHtml(dark ? "#202020" : "#F3F3F3"),
                Card = ColorTranslator.FromHtml(dark ? "#2B2B2B" : "#FFFFFF"),
                Text = ColorTranslator.FromHtml(dark ? "#F5F5F5" : "#1A1A1A"),
                Muted = ColorTranslator.FromHtml(dark ? "#B5B5B5" : "#636363"),
                Border = ColorTranslator.FromHtml(dark ? "#3D3D3D" : "#E0E0E0"),
                Accent = ColorTranslator.FromHtml(dark ? "#99CDFF" : "#0067C0"),
                AccentText = ColorTranslator.FromHtml(dark ? "#002D50" : "#FFFFFF"),
                AccentSubtle = ColorTranslator.FromHtml(dark ? "#303C47" : "#EDF5FC"),
                Track = ColorTranslator.FromHtml(dark ? "#414141" : "#EAEAEA"),
                Hover = ColorTranslator.FromHtml(dark ? "#383838" : "#E9E9E9"),
                Warning = ColorTranslator.FromHtml(dark ? "#FCE100" : "#8A5700") };
        }

        public Color BatteryColor(int percent)
        {
            if (SystemInformation.HighContrast) return SystemColors.WindowText;
            if (percent < 0) return Muted;
            if (percent < 20) return ColorTranslator.FromHtml(Dark ? "#FF9999" : "#C42B1C");
            if (percent < 50) return Warning;
            return ColorTranslator.FromHtml(Dark ? "#6ED6A1" : "#0F7B4B");
        }
    }

    internal static class UiDrawing
    {
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color fill, Color border)
        {
            using (GraphicsPath path = Round(r, radius))
            {
                if (!fill.IsEmpty) using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (!border.IsEmpty) using (Pen pen = new Pen(border)) g.DrawPath(pen, path);
            }
        }

        public static Font Font(float size, bool bold, string family)
        {
            // 字号使用逻辑像素，绘制时再随 Graphics 的 DPI 变换一起缩放。
            return new Font(family, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public static void Text(Graphics g, string text, float x, float y, float w, float h,
            float size, Color color, bool bold, string family = "Microsoft YaHei UI", StringAlignment alignment = StringAlignment.Near)
        {
            using (Font font = Font(size, bold, family))
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = alignment;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(text, font, brush, new RectangleF(x, y, w, h), format);
            }
        }
    }

    internal sealed class FluentButton : Button
    {
        private bool hover, pressed;
        private UiTheme theme = UiTheme.Current();
        public bool Primary, IsClose;
        public float UiScale = 1f;
        public UiTheme Theme { set { theme = value; Invalidate(); } }

        public FluentButton(string text, bool primary)
        {
            Text = text;
            AccessibleName = text;
            Primary = primary;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { hover = pressed = false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(theme.Background);
            g.ScaleTransform(UiScale, UiScale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Width / UiScale, h = Height / UiScale;
            Color fill = Primary ? (Enabled ? theme.Accent : theme.Track) : hover ? theme.Hover : IsClose ? theme.Background : theme.Card;
            if (pressed && Enabled) fill = ControlPaint.Dark(fill, .08f);
            Color ink = !Enabled ? theme.Muted : Primary ? theme.AccentText : theme.Text;
            UiDrawing.FillRound(g, new RectangleF(.5f, .5f, w - 1, h - 1), 5, fill,
                Primary || IsClose ? Color.Empty : theme.Border);
            if (IsClose)
            {
                using (Pen pen = new Pen(ink, 1.2f))
                {
                    g.DrawLine(pen, w / 2 - 4, h / 2 - 4, w / 2 + 4, h / 2 + 4);
                    g.DrawLine(pen, w / 2 + 4, h / 2 - 4, w / 2 - 4, h / 2 + 4);
                }
            }
            else UiDrawing.Text(g, Text, 0, 0, w, h, 12, ink, false, "Microsoft YaHei UI", StringAlignment.Center);
            if (Focused && ShowFocusCues)
                UiDrawing.FillRound(g, new RectangleF(3, 3, w - 6, h - 6), 3, Color.Empty, ink);
        }
    }

    internal sealed class FluentMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly UiTheme theme;
        public FluentMenuRenderer(UiTheme theme) : base(new MenuColors(theme)) { this.theme = theme; RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = theme.Text;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = theme.Text;
            base.OnRenderArrow(e);
        }
        private sealed class MenuColors : ProfessionalColorTable
        {
            private readonly UiTheme t;
            public MenuColors(UiTheme theme) { t = theme; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return t.Card; } }
            public override Color ImageMarginGradientBegin { get { return t.Card; } }
            public override Color ImageMarginGradientMiddle { get { return t.Card; } }
            public override Color ImageMarginGradientEnd { get { return t.Card; } }
            public override Color MenuBorder { get { return t.Border; } }
            public override Color MenuItemBorder { get { return t.Border; } }
            public override Color MenuItemSelected { get { return t.Hover; } }
            public override Color CheckBackground { get { return t.AccentSubtle; } }
            public override Color CheckSelectedBackground { get { return t.AccentSubtle; } }
            public override Color SeparatorDark { get { return t.Border; } }
            public override Color SeparatorLight { get { return t.Card; } }
        }
    }

    internal static class NativeWindowStyle
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        public static void EnableDpiAwareness()
        {
            // 优先启用 Per Monitor V2；系统不提供该接口时退回系统 DPI 感知。
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
            catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }

        public static void Apply(IntPtr handle, bool dark)
        {
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(handle, 20, ref value, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
                value = 2;
                DwmSetWindowAttribute(handle, 33, ref value, 4); // DWMWA_WINDOW_CORNER_PREFERENCE，2 为圆角。
                value = -1;
                DwmSetWindowAttribute(handle, 34, ref value, 4); // DWMWA_BORDER_COLOR，-1 使用系统默认边框色。
                Margins margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
                DwmExtendFrameIntoClientArea(handle, ref margins);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
