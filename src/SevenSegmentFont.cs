using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MchoseBattery
{
    // 内置七段数字，不依赖系统安装字体，也不需要分发字体文件。
    internal static class SevenSegmentFont
    {
        // 位顺序：上、右上、右下、下、左下、左上、中。
        private static readonly int[] Masks = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

        public static void Draw(Graphics graphics, string text, RectangleF area, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (GraphicsPath path = new GraphicsPath())
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char ch = text[i];
                    int mask = ch == '-' ? 0x40 : ch >= '0' && ch <= '9' ? Masks[ch - '0'] : 0;
                    float x = i * 7.3f;
                    if ((mask & 1) != 0) Horizontal(path, x, 0);
                    if ((mask & 2) != 0) Vertical(path, x + 5, 1.1f);
                    if ((mask & 4) != 0) Vertical(path, x + 5, 7.1f);
                    if ((mask & 8) != 0) Horizontal(path, x, 12.6f);
                    if ((mask & 16) != 0) Vertical(path, x, 7.1f);
                    if ((mask & 32) != 0) Vertical(path, x, 1.1f);
                    if ((mask & 64) != 0) Horizontal(path, x, 6.3f);
                }
                // 保留等宽数码位的间隔；三位数只压缩宽度，充分利用图标高度。
                float width = text.Length * 7.3f - .9f;
                float sy = area.Height / 14;
                float sx = Math.Min(sy, area.Width / width);
                using (Matrix transform = new Matrix(sx, 0, 0, sy,
                    area.X + (area.Width - width * sx) / 2, area.Y))
                    path.Transform(transform);
                GraphicsState state = graphics.Save();
                try
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush brush = new SolidBrush(color)) graphics.FillPath(brush, path);
                }
                finally { graphics.Restore(state); }
            }
        }

        private static void Horizontal(GraphicsPath path, float x, float y)
        {
            path.AddPolygon(new PointF[] {
                new PointF(x + .4f, y + .7f), new PointF(x + 1.1f, y),
                new PointF(x + 5.3f, y), new PointF(x + 6, y + .7f),
                new PointF(x + 5.3f, y + 1.4f), new PointF(x + 1.1f, y + 1.4f) });
        }

        private static void Vertical(GraphicsPath path, float x, float y)
        {
            path.AddPolygon(new PointF[] {
                new PointF(x + .7f, y), new PointF(x + 1.4f, y + .7f),
                new PointF(x + 1.4f, y + 4.5f), new PointF(x + .7f, y + 5.2f),
                new PointF(x, y + 4.5f), new PointF(x, y + .7f) });
        }
    }
}