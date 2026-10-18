using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MchoseBattery
{
    internal static class IconFactory
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr handle);

        internal static bool TaskbarIsDark()
        {
            // 任务栏使用系统主题，可能与详情窗口使用的应用主题不同。
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key == null || Convert.ToInt32(key.GetValue("SystemUsesLightTheme", 0)) == 0;
            }
            catch { return true; }
        }

        public static Color ColorFor(int percent, bool connected)
        {
            return UiTheme.Create(TaskbarIsDark()).BatteryColor(connected ? percent : -1);
        }

        public static Icon Build(int percent, bool connected) { return Build(percent, connected, new Settings()); }
        public static Icon Build(int percent, bool connected, Settings settings)
        {
            int size = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
            try
            {
                // 使用任务栏所在屏幕的 DPI，避免先生成小图标再被 Windows 放大。
                uint dpi = GetDpiForWindow(FindWindow("Shell_TrayWnd", null));
                if (dpi > 0) size = (int)Math.Round(16 * dpi / 96.0);
            }
            catch (EntryPointNotFoundException) { }
            using (Bitmap bitmap = RenderPreview(percent, connected, Math.Max(16, size), settings, TaskbarIsDark()))
            {
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    // FromHandle 不接管原生句柄。克隆后返回独立图标，再释放临时 HICON。
                    using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone();
                }
                finally { DestroyIcon(handle); }
            }
        }

        public static void Release(Icon icon) { if (icon != null) icon.Dispose(); }

        public static Bitmap RenderPreview(int percent, bool connected, int size)
        {
            return RenderPreview(percent, connected, size, new Settings(), TaskbarIsDark());
        }

        internal static Bitmap RenderPreview(int percent, bool connected, int size, Settings settings, bool dark)
        {
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            bool known = connected && percent >= 0 && percent <= 100;
            UiTheme theme = UiTheme.Create(dark);
            Color ink = settings.ColorfulIcon ? theme.BatteryColor(known ? percent : -1) : theme.Text;
            Color custom;
            // 自定义颜色优先于状态色；高对比度模式仍以系统颜色为准。
            if (settings.UseCustomIconColor && !System.Windows.Forms.SystemInformation.HighContrast &&
                Settings.TryParseColor(settings.CustomIconColor, out custom)) ink = custom;
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // 统一按 16×16 的逻辑坐标绘制，实际位图尺寸由任务栏 DPI 决定。
                g.ScaleTransform(size / 16f, size / 16f);
                if (settings.TrayStyle == "battery")
                {
                    UiDrawing.FillRound(g, new RectangleF(.8f, 3.8f, 12.5f, 8.4f), 2, Color.Empty, ink);
                    UiDrawing.FillRound(g, new RectangleF(14.1f, 6.2f, 1.5f, 3.6f), .7f, ink, Color.Empty);
                    if (known && percent > 0)
                        UiDrawing.FillRound(g, new RectangleF(2.6f, 5.6f, Math.Max(1, 8.9f * percent / 100f), 4.8f),
                            1, ink, Color.Empty);
                    if (!known)
                        using (Pen pen = new Pen(ink, 1.3f)) g.DrawLine(pen, 5, 8, 9, 8);
                }
                else
                {
                    string text = known ? percent.ToString() : "--";
                    using (GraphicsPath glyph = new GraphicsPath())
                    using (FontFamily font = new FontFamily("Segoe UI"))
                    using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
                    using (SolidBrush brush = new SolidBrush(ink))
                    {
                        glyph.AddString(text, font, (int)FontStyle.Bold, 16, PointF.Empty, format);
                        RectangleF bounds = glyph.GetBounds();
                        // 数字尽量占满高度；三位数只压缩宽度，避免 100 变得过小。
                        float sy = (known ? 14.2f : 3f) / bounds.Height;
                        float sx = Math.Min(sy, 15.2f / bounds.Width);
                        using (Matrix transform = new Matrix(sx, 0, 0, sy,
                            (16 - bounds.Width * sx) / 2 - bounds.X * sx,
                            (16 - bounds.Height * sy) / 2 - bounds.Y * sy))
                            glyph.Transform(transform);
                        g.FillPath(brush, glyph);
                        if (known && text.Length == 3)
                            // 横向压缩会让笔画变细，额外描边补偿三位数的字重。
                            using (Pen weight = new Pen(ink, .25f))
                            {
                                weight.LineJoin = LineJoin.Round;
                                g.DrawPath(weight, glyph);
                            }
                    }
                }
            }
            return bitmap;
        }
    }
}
