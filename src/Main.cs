using System;
using System.Threading;
using System.Windows.Forms;

namespace MchoseBattery
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool showOnStart = false;
            foreach (string a in args)
            {
                if (string.Equals(a, "--show", StringComparison.OrdinalIgnoreCase)) showOnStart = true;
            }

            bool created;
            // 托盘程序没有常驻主窗口，用命名互斥量避免重复创建托盘图标。
            using (Mutex mutex = new Mutex(true, "MCHOSEBattery_SingleInstance_v1", out created))
            {
                if (!created) return;

                // DPI 模式必须在创建窗口或读取控件尺寸前设置。
                NativeWindowStyle.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext(showOnStart));

                GC.KeepAlive(mutex); // 保持互斥量存活到消息循环结束。
            }
        }
    }
}
