using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MchoseBattery
{
    internal sealed class DetailForm : Form
    {
        private const int LogicalWidth = 400;
        private const int LogicalHeight = 458;
        private readonly Settings cfg;
        private readonly FluentButton refresh;
        private readonly FluentButton quit;
        private readonly FluentButton close;
        private readonly FluentButton settings;
        private readonly SettingsPanel settingsPanel;
        private readonly ToolTip tips = new ToolTip();
        private UiTheme theme;
        private Reading reading;
        private bool refreshing;
        private float scale = 1f;

        public DetailForm(Settings cfg, Action onRefresh, Action onQuit, bool autoHide,
            Func<Settings, string> onSaveSettings = null)
        {
            this.cfg = cfg;
            theme = UiTheme.Current();
            Text = "鼠标电量";
            AccessibleName = "MCHOSE 鼠标电量详情";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            // 面板使用逻辑像素布局，由 ApplyScale 统一缩放，避免 WinForms 再次缩放。
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            DoubleBuffered = true;
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;

            refresh = new FluentButton("刷新电量", true);
            refresh.Click += delegate { if (onRefresh != null) onRefresh(); };
            quit = new FluentButton("退出程序", false);
            quit.Click += delegate { if (onQuit != null) onQuit(); };
            close = new FluentButton("关闭", false);
            close.IsClose = true;
            close.Click += delegate { Hide(); };
            settings = new FluentButton("设置", false);
            settings.Click += delegate { ShowSettings(); };
            settingsPanel = new SettingsPanel(onSaveSettings, delegate { ShowBattery(); });
            settingsPanel.Visible = false;
            Controls.Add(refresh);
            Controls.Add(quit);
            Controls.Add(close);
            Controls.Add(settings);
            Controls.Add(settingsPanel);
            refresh.TabIndex = 0;
            quit.TabIndex = 1;
            close.TabIndex = 2;
            settings.TabIndex = 3;
            settingsPanel.TabIndex = 4;
            AcceptButton = refresh;
            tips.SetToolTip(close, "收起面板 (Esc)");
            tips.SetToolTip(refresh, "立即读取鼠标电量 (F5)");
            ApplyTheme();
            ApplyScale();
            PositionNearTray();

            // 选色器会让主面板失去焦点，此时不能按普通托盘弹窗的规则自动收起。
            Deactivate += delegate { if (autoHide && !settingsPanel.IsChoosingColor) Hide(); };
            MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) Hide();
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    if (settingsPanel.Visible) ShowBattery(); else Hide();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                if (e.KeyCode == Keys.F5 && !settingsPanel.Visible)
                {
                    if (!refreshing && onRefresh != null) onRefresh();
                    e.Handled = true;
                }
            };
        }

        private int Px(float value) { return (int)Math.Round(value * scale); }

        private void ApplyScale()
        {
            ClientSize = new Size(Px(LogicalWidth), Px(settingsPanel.Visible ? 610 : LogicalHeight));
            refresh.Bounds = new Rectangle(Px(244), Px(396), Px(132), Px(38));
            quit.Bounds = new Rectangle(Px(24), Px(396), Px(100), Px(38));
            close.Bounds = new Rectangle(Px(344), Px(18), Px(32), Px(32));
            settings.Bounds = new Rectangle(Px(272), Px(20), Px(60), Px(30));
            settingsPanel.Bounds = new Rectangle(Px(24), Px(94), Px(352), Px(492));
            foreach (Control control in Controls)
                if (control is FluentButton) ((FluentButton)control).UiScale = scale;
            settingsPanel.Configure(theme, scale);
            Invalidate(true);
        }

        public void PositionNearTray()
        {
            // 以鼠标所在屏幕的工作区定位，避开任务栏占用的区域。
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(area.Left, area.Right - Width - Px(12)),
                Math.Max(area.Top, area.Bottom - Height - Px(12)));
        }

        private void ApplyTheme()
        {
            theme = UiTheme.Current();
            BackColor = theme.Background;
            foreach (Control control in Controls)
                if (control is FluentButton) ((FluentButton)control).Theme = theme;
            settingsPanel.Configure(theme, scale);
            if (IsHandleCreated) NativeWindowStyle.Apply(Handle, theme.Dark);
            Invalidate(true);
        }

        public void ShowSettings()
        {
            settingsPanel.BeginEdit(cfg, TrayContext.IsAutoStartEnabled());
            settingsPanel.Visible = true;
            settings.Visible = refresh.Visible = quit.Visible = false;
            AcceptButton = null;
            ApplyScale();
            PositionNearTray();
            settingsPanel.SelectNextControl(null, true, true, true, false);
        }

        private void ShowBattery()
        {
            settingsPanel.Visible = false;
            settings.Visible = refresh.Visible = quit.Visible = true;
            AcceptButton = refresh;
            ApplyScale();
            PositionNearTray();
            settings.Focus();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeWindowStyle.Apply(Handle, theme.Dark);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // WM_SETTINGCHANGE / WM_DWMCOLORIZATIONCOLORCHANGED：重新读取系统主题。
            if (m.Msg == 0x001A || m.Msg == 0x0320) ApplyTheme();
            // WM_DPICHANGED：移到不同缩放比例的屏幕时，重算窗口和控件尺寸。
            if (m.Msg == 0x02E0)
            {
                scale = (m.WParam.ToInt64() & 0xffff) / 96f;
                ApplyScale();
                PositionNearTray();
            }
        }

        public void SetRefreshing(bool value)
        {
            refreshing = value;
            refresh.Enabled = !value;
            refresh.Text = value ? "正在刷新…" : "刷新电量";
            Invalidate();
        }

        public void UpdateReading(Reading value)
        {
            if (value == null) return;
            reading = value;
            SetRefreshing(false);
            AccessibleDescription = (value.Connected ? "已连接，电量 " +
                (value.Percent < 0 ? "未知" : value.Percent + "%") + "，" + value.ChargeState :
                "未连接，" + value.Error) + "。更新于 " + value.At.ToString("HH:mm:ss");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // 关闭面板不退出托盘程序，真正退出时由 TrayContext 直接 Dispose。
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.ScaleTransform(scale, scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            bool connected = reading != null && reading.Connected;
            bool known = connected && reading.Percent >= 0;
            int percent = known ? Math.Min(100, reading.Percent) : 0;
            Color battery = theme.BatteryColor(known ? percent : -1);
            bool low = known && percent < cfg.AlertThreshold;
            string state = reading == null ? "正在读取" : !connected ? "未连接" :
                !string.IsNullOrEmpty(reading.ChargeState) ? reading.ChargeState :
                !known ? "电量未知" : low ? "电量偏低" : "电量充足";
            Color status = !connected || !known ? theme.Muted : battery;

            using (Pen border = new Pen(theme.Border))
            using (GraphicsPath outer = UiDrawing.Round(new RectangleF(.5f, .5f, 399, settingsPanel.Visible ? 609 : 457), 8))
                g.DrawPath(border, outer);

            UiDrawing.Text(g, settingsPanel.Visible ? "设置" : "鼠标电量", 24, 22, 240, 28, 20, theme.Text, true);
            UiDrawing.Text(g, settingsPanel.Visible ? "启动、刷新与显示选项" : "2.4G 接收器电量", 24, 54, 320, 22, 12, theme.Muted, false);
            if (settingsPanel.Visible) return;

            UiDrawing.FillRound(g, new RectangleF(24, 94, 352, 216), 10, theme.Card, theme.Border);
            UiDrawing.FillRound(g, new RectangleF(40, 110, 40, 40), 8, theme.AccentSubtle, Color.Empty);
            DrawMouse(g, new PointF(50, 117), theme.Accent);
            string deviceName = reading == null || string.IsNullOrEmpty(reading.DeviceName)
                ? "MCHOSE A7 V3 Ultra+" : reading.DeviceName;
            UiDrawing.Text(g, deviceName, 92, 110, 268, 23, 14, theme.Text, true);
            UiDrawing.Text(g, "无线鼠标 · 2.4 GHz", 92, 133, 268, 20, 11, theme.Muted, false);

            string number = known ? percent.ToString() : "--";
            using (Font font = UiDrawing.Font(64, false, "Segoe UI"))
            using (SolidBrush ink = new SolidBrush(theme.Text))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                // 用坐标定位大号数字，避免分数缩放下被固定文本框裁掉。
                g.DrawString(number, font, ink, new PointF(38, 160), format);
                float w = g.MeasureString(number, font, 200, format).Width;
                if (known) UiDrawing.Text(g, "%", 40 + w + 4, 205, 36, 32, 23, theme.Muted, false, "Segoe UI");
            }
            DrawBattery(g, new RectangleF(278, 186, 62, 32), percent, known, battery);

            UiDrawing.FillRound(g, new RectangleF(40, 256, 320, 6), 3, theme.Track, Color.Empty);
            if (known && percent > 0)
                UiDrawing.FillRound(g, new RectangleF(40, 256, Math.Max(6, 320 * percent / 100f), 6), 3, battery, Color.Empty);
            using (SolidBrush dot = new SolidBrush(status)) g.FillEllipse(dot, 40, 281, 6, 6);
            UiDrawing.Text(g, state, 53, 273, 165, 23, 12, status, false);
            UiDrawing.Text(g, connected ? "已连接" : "等待连接", 251, 273, 109, 23, 12, theme.Muted, false,
                "Microsoft YaHei UI", StringAlignment.Far);

            string info = reading == null ? "正在连接接收器…" : !connected ?
                (string.IsNullOrEmpty(reading.Error) ? "请连接接收器并唤醒鼠标" : reading.Error) :
                !known ? "未获取到电量，请稍后刷新" : low ? "电量偏低，请及时充电" :
                reading.ChargeState == "充电中" ? "正在充电" : "已连接 2.4G 接收器";
            UiDrawing.Text(g, info, 24, 325, 352, 22, 12,
                reading != null && (!connected || low) ? theme.Warning : theme.Muted, false);
            string interval = cfg.RefreshSeconds % 60 == 0 ? cfg.RefreshSeconds / 60 + " 分钟" : cfg.RefreshSeconds + " 秒";
            string time = refreshing ? "正在更新…" : reading == null ? "等待首次更新" :
                "更新于 " + reading.At.ToString("HH:mm:ss");
            UiDrawing.Text(g, time + "  ·  每 " + interval + "自动刷新", 24, 350, 352, 20, 11, theme.Muted, false);
            using (Pen line = new Pen(theme.Border)) g.DrawLine(line, 24, 381, 376, 381);
        }

        private void DrawMouse(Graphics g, PointF origin, Color color)
        {
            using (Pen pen = new Pen(color, 1.6f))
            using (GraphicsPath path = UiDrawing.Round(new RectangleF(origin.X, origin.Y, 20, 26), 10))
            {
                g.DrawPath(pen, path);
                g.DrawLine(pen, origin.X + 10, origin.Y + 1, origin.X + 10, origin.Y + 10);
                g.DrawLine(pen, origin.X + 2, origin.Y + 11, origin.X + 18, origin.Y + 11);
            }
        }

        private void DrawBattery(Graphics g, RectangleF rect, int percent, bool known, Color color)
        {
            UiDrawing.FillRound(g, rect, 6, Color.Empty, theme.Muted);
            UiDrawing.FillRound(g, new RectangleF(rect.Right + 3, rect.Y + 10, 3, 12), 1.5f, theme.Muted, Color.Empty);
            if (known && percent > 0)
                UiDrawing.FillRound(g, new RectangleF(rect.X + 4, rect.Y + 4, Math.Max(3, (rect.Width - 8) * percent / 100f), rect.Height - 8),
                    3, color, Color.Empty);
            if (!known)
                using (Pen pen = new Pen(theme.Muted, 2)) g.DrawLine(pen, rect.X + 24, rect.Y + 16, rect.X + 38, rect.Y + 16);
            if (reading != null && reading.ChargeState == "充电中")
            {
                PointF[] bolt = { new PointF(313, 190), new PointF(303, 204), new PointF(311, 204),
                    new PointF(307, 216), new PointF(319, 201), new PointF(311, 201) };
                using (SolidBrush brush = new SolidBrush(theme.Card)) g.FillPolygon(brush, bolt);
                using (Pen pen = new Pen(theme.Text, 1)) g.DrawPolygon(pen, bolt);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
