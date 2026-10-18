using System;
using System.Runtime.InteropServices;

namespace MchoseBattery
{
    // 一次读取的结果；线程池生成后交给 UI 线程，不再修改。
    public sealed class Reading
    {
        public bool Connected;
        public int Percent = -1; // -1 表示电量未知，与设备未连接是不同状态。
        public string ChargeState = "";
        public string DeviceName = "MCHOSE A7 V3 Ultra+";
        public DateTime At = DateTime.Now;
        public string Error = "";
    }

    internal static class HidNative
    {
        // Windows HID 设备接口类 GUID，用于枚举当前连接的 HID 接口。
        public static readonly Guid HidGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OVERLAPPED
        {
            // 句柄和指针相关字段用 IntPtr，兼容 32 位与 64 位进程。
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData,
            ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData,
            uint DeviceInterfaceDetailDataSize, out uint RequiredSize, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped,
            out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, IntPtr lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    }

    public static class BatteryReader
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        // 当前设备使用 output/input report 0x4D 查询设备信息。
        // 字段位置参考 M HUB 通信记录，数组下标从 Report ID 开始计算：
        // 请求：4D 01 01 00 00 09 00 00 08，其余补零。
        // 响应：buf[20] 为电量，buf[21] 为充电状态。
        public static string LastDevicePath = "";

        public static Reading Read()
        {
            var r = new Reading();
            string path = FindDevicePath();
            if (path == null)
            {
                r.Connected = false;
                r.Error = "未检测到 2.4G 接收器";
                return r;
            }

            LastDevicePath = path;
            // 打开厂商接口的读写通道；共享标志允许其他程序同时打开设备。
            IntPtr h = HidNative.CreateFile(path,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);

            if (h == IntPtr.Zero || h == new IntPtr(-1))
            {
                r.Connected = false;
                r.Error = "打开设备失败";
                return r;
            }

            try
            {
                byte[] frame = new byte[65]; // Report ID 占 1 字节，后面是 64 字节报告数据。
                frame[0] = 0x4D;
                frame[1] = 0x01;
                frame[2] = 0x01;
                frame[3] = 0x00;
                frame[4] = 0x00;
                frame[5] = 0x09;
                frame[6] = 0x00;
                frame[7] = 0x00;
                frame[8] = 0x08;

                Write(h, frame);

                byte[] buf = ReadReply(h, 700);
                if (buf == null)
                {
                    r.Connected = false;
                    r.Error = "设备无响应（唤醒鼠标后重试）";
                    return r;
                }

                // ReadReply 已检查长度，这里再确认响应头与当前查询匹配。
                if (buf[0] != 0x4D || buf[1] != 0x01 || buf[2] != 0x01)
                {
                    r.Connected = false;
                    r.Error = "响应格式不符";
                    return r;
                }

                r.Connected = true;
                int pct = buf[20];
                if (pct < 0 || pct > 100) pct = -1;
                r.Percent = pct;

                int st = buf[21];
                // 已观察到的充电状态：高四位非零或低四位为 4 表示充满，3 表示充电中。
                int fullBit = (st >> 4) & 0x0F;
                int chargeBit = st & 0x0F;
                if (fullBit != 0 || chargeBit == 4)
                    r.ChargeState = "已充满";
                else if (chargeBit == 3)
                    r.ChargeState = "充电中";
                else
                    r.ChargeState = "";

                r.At = DateTime.Now;
                return r;
            }
            catch (Exception ex)
            {
                r.Connected = false;
                r.Error = ex.Message;
                return r;
            }
            finally
            {
                HidNative.CloseHandle(h);
            }
        }

        private static void Write(IntPtr h, byte[] frame)
        {
            var ov = new HidNative.OVERLAPPED();
            ov.hEvent = HidNative.CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            try
            {
                uint wrote;
                bool ok = HidNative.WriteFile(h, frame, (uint)frame.Length, out wrote, ref ov);
                if (!ok)
                {
                    HidNative.WaitForSingleObject(ov.hEvent, 500);
                    uint t;
                    HidNative.GetOverlappedResult(h, ref ov, out t, false);
                }
            }
            finally
            {
                HidNative.CloseHandle(ov.hEvent);
            }
        }

        private static byte[] ReadReply(IntPtr h, int timeoutMs)
        {
            // 使用 UTC 计算等待截止时间，避免本地时区切换影响超时判断。
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var ov = new HidNative.OVERLAPPED();
                ov.hEvent = HidNative.CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                byte[] buf = new byte[256];
                uint got = 0;
                bool ok = HidNative.ReadFile(h, buf, (uint)buf.Length, out got, ref ov);
                if (!ok)
                {
                    uint w = HidNative.WaitForSingleObject(ov.hEvent, 250);
                    if (w != 0)
                    {
                        HidNative.CloseHandle(ov.hEvent);
                        continue;
                    }
                    HidNative.GetOverlappedResult(h, ref ov, out got, false);
                }
                HidNative.CloseHandle(ov.hEvent);
                if (got >= 24) return buf; // 后续解析最多访问下标 21，短报告不交给解析器。
            }
            return null;
        }

        private static string FindDevicePath()
        {
            Guid guid = HidNative.HidGuid;
            // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE：只枚举当前存在的设备接口。
            IntPtr set = HidNative.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return null;

            var iface = new HidNative.SP_DEVICE_INTERFACE_DATA();
            iface.cbSize = Marshal.SizeOf(iface);
            string found = null;
            try
            {
                for (uint i = 0; ; i++)
                {
                    if (!HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface)) break;

                    uint need = 0;
                    var info = new HidNative.SP_DEVINFO_DATA();
                    info.cbSize = Marshal.SizeOf(info);
                    // 第一次调用只获取路径结构所需的缓冲区大小。
                    HidNative.SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out need, ref info);
                    if (need == 0) continue;

                    IntPtr buffer = Marshal.AllocHGlobal((int)need);
                    try
                    {
                        // 结构的 cbSize 在 x64/x86 下不同；Unicode 路径仍从偏移 4 开始。
                        Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                        if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref iface, buffer, need, out need, ref info))
                            continue;

                        string p = Marshal.PtrToStringAuto(new IntPtr(buffer.ToInt64() + 4));
                        if (p == null) continue;

                        string low = p.ToLowerInvariant();
                        // 同一个接收器有多个 HID 集合，只打开当前协议使用的厂商接口。
                        if (low.Contains("vid_3837") && low.Contains("pid_1018") && low.Contains("mi_02") && low.Contains("col02"))
                        {
                            found = p;
                            break;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            finally
            {
                HidNative.SetupDiDestroyDeviceInfoList(set);
            }
            return found;
        }
    }
}
