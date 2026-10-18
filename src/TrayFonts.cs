using System;
using System.Collections.Generic;
using System.Drawing;

namespace MchoseBattery
{
    internal static class TrayFonts
    {
        public const string DefaultName = "Segoe UI";
        public const string CalculatorName = "计算器数码管";
        private static readonly string[] available = FindAvailable();
        public static string[] Available { get { return (string[])available.Clone(); } }

        private static string[] FindAvailable()
        {
            // 只列出本机已安装且能绘制数字的常用字体，避免符号字体出现在列表中。
            string[] candidates = { DefaultName, "Arial", "Arial Narrow", "Tahoma", "Verdana",
                "Calibri", "Consolas", "Cascadia Mono", "Bahnschrift", "Microsoft YaHei UI", "Courier New", "Georgia" };
            List<string> names = new List<string>();
            foreach (string name in candidates)
            {
                try
                {
                    using (FontFamily family = new FontFamily(name))
                        if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase) &&
                            (family.IsStyleAvailable(FontStyle.Bold) || family.IsStyleAvailable(FontStyle.Regular)))
                            names.Add(name);
                }
                catch (ArgumentException) { }
            }
            if (names.Count == 0) names.Add(FontFamily.GenericSansSerif.Name);
            names.Add(CalculatorName);
            return names.ToArray();
        }

        public static string Resolve(string name)
        {
            foreach (string installed in available)
                if (string.Equals(installed, name, StringComparison.OrdinalIgnoreCase)) return installed;
            return available[0]; // 配置从其他电脑复制过来或字体已卸载时回退。
        }

        public static FontStyle Weight(FontFamily family)
        {
            return family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
        }
    }
}