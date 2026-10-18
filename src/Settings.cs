using System;
using System.IO;
using System.Drawing;
using System.Globalization;

namespace MchoseBattery
{
    internal sealed class Settings
    {
        // 新增配置项在字段中给出默认值，旧配置文件缺少该项时也能正常使用。
        public bool AutoStart;
        public int AlertThreshold = 20;
        public int RefreshSeconds = 60;
        public bool AlertsEnabled = true;
        public bool ColorfulIcon = true;
        public string TrayStyle = "number";
        public bool UseCustomIconColor;
        public string CustomIconColor = "#FFFFFF";
        private readonly string storagePath;

        public Settings(string path = null) { storagePath = path; }

        internal static string ConfigPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MCHOSEBattery", "config.ini"); }
        }

        public Settings Clone() { return (Settings)MemberwiseClone(); }
        // 只复制设置值，保留目标对象自己的配置文件路径。
        public void CopyFrom(Settings other)
        {
            AutoStart = other.AutoStart;
            AlertThreshold = other.AlertThreshold;
            RefreshSeconds = other.RefreshSeconds;
            AlertsEnabled = other.AlertsEnabled;
            ColorfulIcon = other.ColorfulIcon;
            TrayStyle = other.TrayStyle;
            UseCustomIconColor = other.UseCustomIconColor;
            CustomIconColor = other.CustomIconColor;
        }

        public static Settings Load() { return LoadFrom(ConfigPath); }
        internal static Settings LoadFrom(string path)
        {
            Settings s = new Settings(path);
            try
            {
                if (!File.Exists(path)) return s;
                // 忽略未知字段；范围不合法的值保留默认设置。
                foreach (string line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int n;
                    if (k == "AutoStart") s.AutoStart = v == "1";
                    else if (k == "AlertThreshold" && int.TryParse(v, out n) && n >= 1 && n <= 99) s.AlertThreshold = n;
                    else if (k == "RefreshSeconds" && int.TryParse(v, out n) && n >= 15 && n <= 3600) s.RefreshSeconds = n;
                    else if (k == "AlertsEnabled" && (v == "0" || v == "1")) s.AlertsEnabled = v == "1";
                    else if (k == "ColorfulIcon" && (v == "0" || v == "1")) s.ColorfulIcon = v == "1";
                    else if (k == "TrayStyle" && (v == "number" || v == "battery")) s.TrayStyle = v;
                    else if (k == "UseCustomIconColor" && (v == "0" || v == "1")) s.UseCustomIconColor = v == "1";
                    else if (k == "CustomIconColor")
                    {
                        Color color;
                        if (TryParseColor(v, out color)) s.CustomIconColor = ToHex(color);
                    }
                }
            }
            catch { }
            return s;
        }

        public bool Save() { return SaveTo(storagePath ?? ConfigPath); }
        internal bool SaveTo(string path)
        {
            // 在同一目录写临时文件，再替换旧文件，避免中断写入留下半份配置。
            string temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllLines(temporary, new string[] {
                    "AutoStart=" + (AutoStart ? "1" : "0"),
                    "AlertThreshold=" + AlertThreshold, "RefreshSeconds=" + RefreshSeconds,
                    "AlertsEnabled=" + (AlertsEnabled ? "1" : "0"),
                    "ColorfulIcon=" + (ColorfulIcon ? "1" : "0"), "TrayStyle=" + TrayStyle,
                    "UseCustomIconColor=" + (UseCustomIconColor ? "1" : "0"), "CustomIconColor=" + CustomIconColor });
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                return false;
            }
        }

        public static bool TryParseColor(string text, out Color color)
        {
            // 只接受 RGB 六位颜色，输入可省略 #；写回时统一为 #RRGGBB。
            color = Color.White;
            if (text == null) return false;
            string hex = text.Trim();
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            int rgb;
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return false;
            color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return true;
        }

        public static string ToHex(Color color) { return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2"); }
    }
}
