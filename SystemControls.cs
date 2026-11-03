using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace FloatingDock
{
    /// <summary>Ekran parlakligi: dizustu ekranlarda WMI, harici monitorlerde DDC/CI. Ikisi de yoksa Supported = false.</summary>
    static class Brightness
    {
        static bool? wmi;

        public static bool Supported => Get() >= 0;

        /// <summary>0..100, desteklenmiyorsa -1.</summary>
        public static int Get()
        {
            if (wmi != false)
            {
                try
                {
                    using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                    foreach (ManagementObject o in s.Get()) { wmi = true; return Convert.ToInt32(o["CurrentBrightness"]); }
                    wmi = false;
                }
                catch { wmi = false; }
            }
            foreach (var m in Physical())
            {
                try { if (GetMonitorBrightness(m.h, out uint min, out uint cur, out uint max) && max > min) return (int)Math.Round(100.0 * (cur - min) / (max - min)); }
                finally { DestroyPhysicalMonitor(m.h); }
            }
            return -1;
        }

        public static void Set(int pct)
        {
            pct = Math.Clamp(pct, 0, 100);
            if (wmi == true)
            {
                try
                {
                    using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                    foreach (ManagementObject o in s.Get()) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)pct });
                    return;
                }
                catch { }
            }
            foreach (var m in Physical())
            {
                try
                {
                    if (GetMonitorBrightness(m.h, out uint min, out _, out uint max) && max > min)
                        SetMonitorBrightness(m.h, (uint)(min + (max - min) * pct / 100.0));
                }
                finally { DestroyPhysicalMonitor(m.h); }
            }
        }

        static List<(IntPtr h, string name)> Physical()
        {
            var list = new List<(IntPtr, string)>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hm, dc, r, d) =>
            {
                if (GetNumberOfPhysicalMonitorsFromHMONITOR(hm, out uint n) && n > 0)
                {
                    var arr = new PHYSICAL_MONITOR[n];
                    if (GetPhysicalMonitorsFromHMONITOR(hm, n, arr)) foreach (var p in arr) list.Add((p.h, p.desc));
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PHYSICAL_MONITOR { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
        delegate bool MonitorEnum(IntPtr hm, IntPtr dc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum cb, IntPtr data);
        [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hm, out uint n);
        [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hm, uint n, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitor(IntPtr h);
        [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint v);
    }

    /// <summary>Pil durumu (masaustu bilgisayarda HasBattery = false).</summary>
    static class Battery
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

        public static bool HasBattery => GetSystemPowerStatus(out var s) && (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255;
        public static int Percent => GetSystemPowerStatus(out var s) && s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : -1;
        public static bool Charging => GetSystemPowerStatus(out var s) && s.ACLineStatus == 1;
        public static TimeSpan? Remaining => GetSystemPowerStatus(out var s) && s.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(s.BatteryLifeTime) : (TimeSpan?)null;
    }
}
