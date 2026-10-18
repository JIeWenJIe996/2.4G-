using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MchoseBattery
{
    // ApplicationContext 管理托盘和消息循环，详情窗口可以隐藏而不结束进程。
    public sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Settings cfg;
        private Reading last;
        private volatile bool busy;
        private DetailForm detail;
        private readonly List<Icon> retired = new List<Icon>();
        private DateTime lastAlertAt = DateTime.MinValue;
        private readonly bool keepOpen;
        // 工作线程只提交 Reading；控件和 NotifyIcon 由 UI 线程统一更新。
        private volatile Reading pending;
        private System.Windows.Forms.Timer uiPump;
        private int pumpCount;
        private bool iconDark;

        public TrayContext(bool showOnStart)
        {
            cfg = Settings.Load();
            // 用户可能在任务管理器或注册表中改过启动项，以注册表当前值为准。
            cfg.AutoStart = IsAutoStartEnabled();
            iconDark = IconFactory.TaskbarIsDark();
            keepOpen = showOnStart;

            tray = new NotifyIcon();
            tray.Text = "MCHOSE 鼠标电量";
            tray.Icon = IconFactory.Build(-1, false, cfg);
            tray.MouseClick += OnTrayClick;
            tray.ContextMenuStrip = BuildMenu();
            tray.Visible = true;

            uiPump = new System.Windows.Forms.Timer();
            uiPump.Interval = 250;
            pumpCount = 0;
            uiPump.Tick += delegate
            {
                // 主题变化很慢，没必要每 250ms 读一次注册表（原先每秒 4 次）。
                if (++pumpCount >= 20)
                {
                    pumpCount = 0;
                    bool dark = IconFactory.TaskbarIsDark();
                    if (iconDark != dark) { iconDark = dark; UpdateTrayIcon(); }
                }
                Reading r = pending;
                if (r != null)
                {
                    pending = null;
                    // 结果被 UI 消费后才允许下一次读取，避免覆盖尚未显示的结果。
                    busy = false;
                    Apply(r);
                }
            };
            uiPump.Start();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = Math.Max(15, cfg.RefreshSeconds) * 1000;
            timer.Tick += delegate { RefreshNow(); };
            timer.Start();

            RefreshNow();

            if (showOnStart)
            {
                try
                {
                    ShowDetail();
                }
                catch (Exception ex)
                {
                    try
                    {
                        System.IO.File.AppendAllText(
                            System.IO.Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "MCHOSEBattery", "error.log"),
                            DateTime.Now + " showOnStart: " + ex + Environment.NewLine);
                    }
                    catch { }
                }
            }
        }

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9f);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.Padding = new Padding(4);
            menu.Opening += delegate
            {
                UiTheme theme = UiTheme.Current();
                menu.BackColor = theme.Card;
                menu.ForeColor = theme.Text;
                menu.Renderer = new FluentMenuRenderer(theme);
                NativeWindowStyle.Apply(menu.Handle, theme.Dark);
            };
            menu.Items.Add("立即刷新", null, delegate { RefreshNow(); });
            menu.Items.Add("显示详情", null, delegate { ShowDetail(); });
            menu.Items.Add("设置", null, delegate { ShowDetail(); detail.ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem auto = new ToolStripMenuItem("开机自动启动");
            auto.Checked = IsAutoStartEnabled();
            menu.Opening += delegate { auto.Checked = IsAutoStartEnabled(); };
            auto.Click += delegate
            {
                bool want = !IsAutoStartEnabled();
                ApplyAutoStart(want);
                auto.Checked = IsAutoStartEnabled();
                cfg.AutoStart = auto.Checked;
                cfg.Save();
            };
            menu.Items.Add(auto);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Quit(); });
            foreach (ToolStripItem item in menu.Items)
                if (!(item is ToolStripSeparator)) item.Padding = new Padding(8, 6, 16, 6);
            return menu;
        }

        private void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                RefreshNow();
                ShowDetail();
            }
        }

        private void RefreshNow()
        {
            if (busy) return;
            busy = true;
            if (detail != null && !detail.IsDisposed) detail.SetRefreshing(true);
            // HID 读取可能等待设备响应，放到线程池中，避免阻塞面板和托盘菜单。
            ThreadPool.QueueUserWorkItem(delegate
            {
                Reading r = null;
                try { r = BatteryReader.Read(); }
                catch (Exception ex)
                {
                    r = new Reading();
                    r.Connected = false;
                    r.Error = ex.Message;
                }
                finally
                {
                    pending = r;
                }
            });
        }

        private void Apply(Reading r)
        {
            // 电量通常几分钟才变一次，值没变就不重建图标（避免无谓的 GDI 分配）。
            bool changed = last == null || last.Connected != r.Connected || last.Percent != r.Percent;
            last = r;
            if (changed) UpdateTrayIcon();

            string text;
            if (!r.Connected)
            {
                text = "MCHOSE A7 V3 Ultra+\n状态：" + (string.IsNullOrEmpty(r.Error) ? "未连接" : r.Error);
            }
            else
            {
                string pct = r.Percent < 0 ? "--" : (r.Percent + "%");
                text = "MCHOSE A7 V3 Ultra+\n电量：" + pct
                       + (string.IsNullOrEmpty(r.ChargeState) ? "" : "（" + r.ChargeState + "）")
                       + "\n更新：" + r.At.ToString("HH:mm:ss");
            }
            tray.Text = Trim(text, 127);

            if (detail != null && !detail.IsDisposed)
            {
                detail.UpdateReading(r);
            }

            // 只提醒已知电量；未知值 -1 不应被误判为低电量。
            if (cfg.AlertsEnabled && r.Connected && r.Percent >= 0 && r.Percent < cfg.AlertThreshold)
            {
                if ((DateTime.Now - lastAlertAt).TotalMinutes >= 30)
                {
                    lastAlertAt = DateTime.Now;
                    tray.ShowBalloonTip(6000, "鼠标电量偏低",
                        "MCHOSE A7 V3 Ultra+ 剩余 " + r.Percent + "%，建议充电。",
                        ToolTipIcon.Warning);
                }
            }
        }

        private void UpdateTrayIcon()
        {
            // 延迟释放最近替换的图标；退出时再清理仍保留的资源。
            if (tray.Icon != null) retired.Add(tray.Icon);
            tray.Icon = IconFactory.Build(last == null ? -1 : last.Percent, last != null && last.Connected, cfg);
            while (retired.Count > 2)
            {
                IconFactory.Release(retired[0]);
                retired.RemoveAt(0);
            }
        }

        private string SaveSettings(Settings next)
        {
            Color custom;
            if (!Settings.TryParseColor(next.CustomIconColor, out custom)) return "请输入有效颜色，如 #FFFFFF";
            next.CustomIconColor = Settings.ToHex(custom);
            next.TrayFont = TrayFonts.Resolve(next.TrayFont);
            if (next.RefreshSeconds < 15 || next.RefreshSeconds > 3600 || next.AlertThreshold < 1 || next.AlertThreshold > 99 ||
                (next.TrayStyle != "number" && next.TrayStyle != "battery")) return "设置数值无效，请检查后重试";
            // 启动项和配置文件分别保存。配置写入失败时，尝试恢复原来的启动状态。
            bool previousAutoStart = IsAutoStartEnabled();
            if (previousAutoStart != next.AutoStart)
            {
                ApplyAutoStart(next.AutoStart);
                if (IsAutoStartEnabled() != next.AutoStart) return "无法修改开机启动，请重试";
            }
            if (!next.Save())
            {
                if (previousAutoStart != next.AutoStart) ApplyAutoStart(previousAutoStart);
                return "设置保存失败，请检查配置目录权限";
            }
            // 保存成功后才替换运行中的配置，取消或保存失败不会改变轮询和显示。
            cfg.CopyFrom(next);
            timer.Interval = cfg.RefreshSeconds * 1000;
            UpdateTrayIcon();
            if (detail != null && !detail.IsDisposed) detail.Invalidate();
            return null;
        }

        private static string Trim(string s, int max)
        {
            if (s == null) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max);
        }

        private void ShowDetail()
        {
            if (detail == null || detail.IsDisposed)
            {
                detail = new DetailForm(cfg, delegate { RefreshNow(); }, delegate { Quit(); }, !keepOpen, SaveSettings);
                if (last != null) detail.UpdateReading(last);
            }
            detail.SetRefreshing(busy);
            detail.PositionNearTray();
            detail.Show();
            detail.Activate();
        }

        private void Quit()
        {
            // 先停止回调并移除托盘图标，再释放窗口和 GDI 资源。
            timer.Stop();
            if (uiPump != null) { uiPump.Stop(); uiPump.Dispose(); }
            tray.Visible = false;
            if (detail != null && !detail.IsDisposed) detail.Dispose();
            foreach (Icon i in retired) IconFactory.Release(i);
            retired.Clear();
            if (tray.Icon != null) IconFactory.Release(tray.Icon);
            Font menuFont = tray.ContextMenuStrip.Font;
            tray.ContextMenuStrip.Dispose();
            menuFont.Dispose();
            tray.Dispose();
            ExitThread();
        }

        private static string RunKey
        {
            get { return @"Software\Microsoft\Windows\CurrentVersion\Run"; }
        }

        private static string AppName
        {
            get { return "MCHOSEBattery"; }
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(AppName);
                    return v != null && v.ToString().Length > 0;
                }
            }
            catch { return false; }
        }

        public static void ApplyAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (enable)
                    {
                        string exe = Application.ExecutablePath;
                        k.SetValue(AppName, "\"" + exe + "\""); // 路径可能包含空格，需保留引号。
                    }
                    else
                    {
                        k.DeleteValue(AppName, false);
                    }
                }
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (timer != null) timer.Dispose();
                if (tray != null) tray.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
