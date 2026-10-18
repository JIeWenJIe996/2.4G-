using System;
using System.Drawing;
using System.Windows.Forms;

namespace MchoseBattery
{
    internal sealed class SettingsPanel : UserControl
    {
        private readonly FluentToggle autoStart = new FluentToggle("开机自动启动");
        private readonly FluentToggle alerts = new FluentToggle("低电量提醒");
        private readonly ComboBox colorMode = new ComboBox();
        private readonly TextBox colorHex = new TextBox();
        private readonly FluentButton pickColor = new FluentButton("选择颜色", false);
        private readonly NumericUpDown interval = new NumericUpDown();
        private readonly NumericUpDown threshold = new NumericUpDown();
        private readonly ComboBox style = new ComboBox();
        private readonly FluentButton save = new FluentButton("保存设置", true);
        private readonly FluentButton cancel = new FluentButton("取消", false);
        private readonly Func<Settings, string> onSave;
        private readonly Action onDone;
        private Settings draft;
        private UiTheme theme = UiTheme.Current();
        private float scale = 1;
        private string error = "";
        private Font editorFont;
        public bool IsChoosingColor { get; private set; }

        public SettingsPanel(Func<Settings, string> onSave, Action onDone)
        {
            this.onSave = onSave;
            this.onDone = onDone;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            interval.Minimum = 15; interval.Maximum = 3600; interval.Increment = 15;
            threshold.Minimum = 1; threshold.Maximum = 99;
            interval.AccessibleName = "刷新间隔，秒，15 到 3600";
            threshold.AccessibleName = "低电量阈值，百分比，1 到 99";
            style.DropDownStyle = ComboBoxStyle.DropDownList;
            style.DrawMode = DrawMode.OwnerDrawFixed;
            style.FlatStyle = FlatStyle.Flat;
            style.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                Color fill = (e.State & DrawItemState.Selected) != 0 ? theme.AccentSubtle : theme.Card;
                using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, e.Bounds);
                int index = e.Index < 0 ? style.SelectedIndex : e.Index;
                if (index >= 0)
                    TextRenderer.DrawText(e.Graphics, style.Items[index].ToString(), style.Font, e.Bounds,
                        theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            style.Items.AddRange(new object[] { "大号电量数字", "简约电池" });
            style.AccessibleName = "托盘图标样式";
            colorMode.DropDownStyle = ComboBoxStyle.DropDownList;
            colorMode.FlatStyle = FlatStyle.Flat;
            colorMode.DrawMode = DrawMode.OwnerDrawFixed;
            colorMode.AccessibleName = "托盘字体颜色模式";
            colorMode.Items.AddRange(new object[] { "随电量变化", "跟随系统", "自定义颜色" });
            colorMode.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                using (SolidBrush brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? theme.AccentSubtle : theme.Card))
                    e.Graphics.FillRectangle(brush, e.Bounds);
                int index = e.Index < 0 ? colorMode.SelectedIndex : e.Index;
                if (index >= 0) TextRenderer.DrawText(e.Graphics, colorMode.Items[index].ToString(), colorMode.Font,
                    e.Bounds, theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                e.DrawFocusRectangle();
            };
            colorHex.AccessibleName = "自定义颜色，六位十六进制，如 #FFFFFF";
            colorHex.MaxLength = 7;
            colorHex.BorderStyle = BorderStyle.FixedSingle;
            colorHex.TextChanged += delegate { Invalidate(); };
            colorMode.SelectedIndexChanged += delegate
            {
                colorHex.Enabled = pickColor.Enabled = colorMode.SelectedIndex == 2;
                Invalidate();
            };
            style.SelectedIndexChanged += delegate { Invalidate(); };
            pickColor.Click += delegate { ChooseColor(); };
            Controls.AddRange(new Control[] { autoStart, interval, alerts, threshold, style, colorMode, pickColor, colorHex, cancel, save });
            for (int i = 0; i < Controls.Count; i++) Controls[i].TabIndex = i;
            alerts.CheckedChanged += delegate { threshold.Enabled = alerts.Checked; };
            cancel.Click += delegate { onDone(); };
            save.Click += delegate { SaveDraft(); };
        }

        public void BeginEdit(Settings settings, bool actualAutoStart)
        {
            // 在副本上编辑，切换选项和预览颜色都不会提前改动运行中的配置。
            draft = settings.Clone();
            autoStart.Checked = actualAutoStart;
            interval.Value = settings.RefreshSeconds;
            threshold.Value = settings.AlertThreshold;
            alerts.Checked = settings.AlertsEnabled;
            threshold.Enabled = alerts.Checked;
            colorMode.SelectedIndex = settings.UseCustomIconColor ? 2 : settings.ColorfulIcon ? 0 : 1;
            colorHex.Text = settings.CustomIconColor;
            style.SelectedIndex = settings.TrayStyle == "battery" ? 1 : 0;
            error = "";
            Invalidate();
        }

        private void SaveDraft()
        {
            draft.AutoStart = autoStart.Checked;
            draft.RefreshSeconds = (int)interval.Value;
            draft.AlertThreshold = (int)threshold.Value;
            draft.AlertsEnabled = alerts.Checked;
            draft.TrayStyle = style.SelectedIndex == 1 ? "battery" : "number";
            draft.ColorfulIcon = colorMode.SelectedIndex == 0;
            draft.UseCustomIconColor = colorMode.SelectedIndex == 2;
            Color color;
            if (!Settings.TryParseColor(colorHex.Text, out color))
            {
                if (draft.UseCustomIconColor)
                {
                    error = "请输入六位颜色值，如 #FFFFFF";
                    Invalidate(); colorHex.Focus(); return;
                }
                Settings.TryParseColor(draft.CustomIconColor, out color);
            }
            draft.CustomIconColor = Settings.ToHex(color);
            // 保存由 TrayContext 执行。返回错误时保留当前页面，便于修改后重试。
            error = onSave == null ? "暂时无法保存设置" : onSave(draft);
            if (string.IsNullOrEmpty(error)) onDone();
            else Invalidate();
        }

        private void ChooseColor()
        {
            Color initial;
            Settings.TryParseColor(colorHex.Text, out initial);
            IsChoosingColor = true; // 告知详情窗口：弹出选色器时暂不因失焦而隐藏。
            try
            {
                using (ColorDialog dialog = new ColorDialog { Color = initial, FullOpen = true, AnyColor = true })
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK) colorHex.Text = Settings.ToHex(dialog.Color);
            }
            finally
            {
                Form owner = FindForm();
                if (owner != null && !owner.IsDisposed) owner.Activate();
                IsChoosingColor = false;
            }
        }

        public void Configure(UiTheme value, float dpiScale)
        {
            theme = value; scale = dpiScale;
            Font previous = null;
            // 相同字号要复用 Font。WinForms 可能忽略相等字体的赋值，释放旧对象会使控件继续引用已释放字体。
            if (editorFont == null || Math.Abs(editorFont.Size - 12 * scale) > .01f)
            {
                previous = editorFont;
                editorFont = new Font("Microsoft YaHei UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            BackColor = theme.Background;
            SetBounds(autoStart, 284, 16, 44, 24);
            SetBounds(interval, 236, 80, 92, 30);
            SetBounds(alerts, 284, 154, 44, 24);
            SetBounds(threshold, 236, 197, 68, 28);
            SetBounds(style, 172, 270, 156, 30);
            style.ItemHeight = (int)Math.Round(22 * scale);
            SetBounds(colorMode, 172, 334, 156, 30);
            colorMode.ItemHeight = (int)Math.Round(22 * scale);
            SetBounds(pickColor, 16, 384, 128, 30);
            SetBounds(colorHex, 164, 386, 112, 28);
            SetBounds(cancel, 0, 454, 100, 38);
            SetBounds(save, 220, 454, 132, 38);
            foreach (Control control in Controls)
            {
                control.ForeColor = theme.Text;
                control.BackColor = theme.Card;
                FluentButton button = control as FluentButton;
                FluentToggle toggle = control as FluentToggle;
                if (button != null) { button.Theme = theme; button.UiScale = scale; }
                else if (toggle != null) { toggle.Theme = theme; toggle.UiScale = scale; }
                else
                {
                    control.Font = editorFont;
                }
            }
            if (previous != null) previous.Dispose();
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && editorFont != null) { editorFont.Dispose(); editorFont = null; }
        }

        private void SetBounds(Control c, int x, int y, int w, int h)
        {
            c.Bounds = new Rectangle((int)Math.Round(x * scale), (int)Math.Round(y * scale),
                (int)Math.Round(w * scale), (int)Math.Round(h * scale));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.ScaleTransform(scale, scale);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int[] ys = { 0, 66, 132, 254, 324 };
            int[] hs = { 58, 58, 114, 62, 110 };
            for (int i = 0; i < ys.Length; i++)
                UiDrawing.FillRound(g, new RectangleF(.5f, ys[i] + .5f, 351, hs[i] - 1), 8, theme.Card, theme.Border);
            Label(g, "开机自动启动", 16, 8, 190, 20, 12, theme.Text);
            Label(g, "登录 Windows 后在托盘运行", 16, 29, 240, 18, 10, theme.Muted);
            Label(g, "自动刷新", 16, 74, 180, 20, 12, theme.Text);
            Label(g, "间隔 15–3600 秒", 16, 95, 200, 18, 10, theme.Muted);
            Label(g, "低电量提醒", 16, 142, 200, 20, 12, theme.Text);
            Label(g, "每 30 分钟最多提醒一次", 16, 163, 250, 18, 10, theme.Muted);
            Label(g, "电量低于", 16, 199, 200, 24, 12, theme.Muted);
            Label(g, "%", 312, 199, 24, 24, 12, theme.Muted);
            Label(g, "托盘图标", 16, 262, 150, 20, 12, theme.Text);
            Label(g, "更换任务栏的显示方式", 16, 283, 150, 18, 10, theme.Muted);
            Label(g, "字体颜色", 16, 332, 150, 20, 12, theme.Text);
            Label(g, "自定义托盘显示颜色", 16, 353, 150, 18, 10, theme.Muted);
            Settings preview = new Settings {
                ColorfulIcon = colorMode.SelectedIndex == 0, UseCustomIconColor = colorMode.SelectedIndex == 2,
                CustomIconColor = colorHex.Text, TrayStyle = style.SelectedIndex == 1 ? "battery" : "number" };
            // 预览复用托盘的绘制逻辑；按实际 DPI 生成位图，避免先画小图再拉伸。
            using (Bitmap icon = IconFactory.RenderPreview(100, true, (int)Math.Round(28 * scale), preview, theme.Dark))
                g.DrawImage(icon, new Rectangle(300, 384, 28, 28));
            if (!string.IsNullOrEmpty(error)) Label(g, error, 0, 434, 352, 19, 10, theme.Warning);
        }

        private static void Label(Graphics g, string text, int x, int y, int w, int h, int size, Color color)
        {
            UiDrawing.Text(g, text, x, y, w, h, size, color, false);
        }
    }

    internal sealed class FluentToggle : CheckBox
    {
        public UiTheme Theme = UiTheme.Current();
        public float UiScale = 1;
        private bool hover;
        public FluentToggle(string name)
        {
            AccessibleName = name;
            Cursor = Cursors.Hand;
            AutoSize = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Card);
            g.ScaleTransform(UiScale, UiScale);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            UiDrawing.FillRound(g, new RectangleF(2, 3, 40, 18), 9,
                Checked ? Theme.Accent : hover ? Theme.Hover : Theme.Card, Checked ? Theme.Accent : Theme.Muted);
            using (SolidBrush brush = new SolidBrush(Checked ? Theme.AccentText : Theme.Muted))
                g.FillEllipse(brush, Checked ? 27 : 7, 7, 10, 10);
            if (Focused && ShowFocusCues)
                UiDrawing.FillRound(g, new RectangleF(.5f, .5f, 43, 23), 6, Color.Empty, Theme.Text);
        }
    }
}
