using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FloatingDock
{
    /// <summary>Bir monitor: sinirlar fiziksel piksel (sanal masaustu koordinatlari).</summary>
    class Screen
    {
        public string Device;
        public Native.RECT Bounds, Work;
        public bool Primary;
        public double Scale = 1;

        public int Width => Bounds.right - Bounds.left;
        public int Height => Bounds.bottom - Bounds.top;
        public double CenterX => (Bounds.left + Bounds.right) / 2.0;
        public bool Contains(int x, int y) => x >= Bounds.left && x < Bounds.right && y >= Bounds.top && y < Bounds.bottom;
        public override string ToString() => Device + " " + Bounds.left + "," + Bounds.top + " " + Width + "x" + Height + " @" + Scale + (Primary ? " (birincil)" : "");
    }

    /// <summary>Monitorleri listeler ve pencereleri fiziksel pikselle yerlestirir (karisik DPI'da WPF Left/Top'a guvenmeden).</summary>
    static class Screens
    {
        /// <summary>Gelistirici testi (--split): her monitoru sol/sag iki sanal ekrana boler.</summary>
        internal static bool SplitTest;

        public static List<Screen> All()
        {
            var list = new List<Screen>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hm, dc, r, d) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(hm, ref mi)) return true;
                double sc = 1;
                try { if (GetDpiForMonitor(hm, 0, out uint dx, out _) == 0 && dx > 0) sc = dx / 96.0; } catch { }
                list.Add(new Screen { Device = mi.szDevice, Bounds = mi.rcMonitor, Work = mi.rcWork, Primary = (mi.dwFlags & 1) != 0, Scale = sc });
                return true;
            }, IntPtr.Zero);
            if (list.Count == 0)
                list.Add(new Screen
                {
                    Device = "primary", Primary = true,
                    Bounds = new Native.RECT { right = Native.GetSystemMetrics(0), bottom = Native.GetSystemMetrics(1) },
                    Work = new Native.RECT { right = Native.GetSystemMetrics(0), bottom = Native.GetSystemMetrics(1) }
                });
            if (SplitTest)
            {
                var split = new List<Screen>();
                foreach (var s in list)
                {
                    int mid = (s.Bounds.left + s.Bounds.right) / 2;
                    split.Add(new Screen { Device = s.Device + "#L", Primary = s.Primary, Scale = s.Scale,
                        Bounds = new Native.RECT { left = s.Bounds.left, top = s.Bounds.top, right = mid, bottom = s.Bounds.bottom },
                        Work = new Native.RECT { left = s.Work.left, top = s.Work.top, right = mid, bottom = s.Work.bottom } });
                    split.Add(new Screen { Device = s.Device + "#R", Primary = false, Scale = s.Scale,
                        Bounds = new Native.RECT { left = mid, top = s.Bounds.top, right = s.Bounds.right, bottom = s.Bounds.bottom },
                        Work = new Native.RECT { left = mid, top = s.Work.top, right = s.Work.right, bottom = s.Work.bottom } });
                }
                list = split;
            }
            // birincil once, sonra soldan saga
            return list.OrderByDescending(s => s.Primary).ThenBy(s => s.Bounds.left).ThenBy(s => s.Bounds.top).ToList();
        }

        public static Screen Primary => All()[0];

        public static Screen FromPoint(int x, int y, List<Screen> all = null)
        {
            all ??= All();
            return all.FirstOrDefault(s => s.Contains(x, y)) ?? Nearest(all, x, y);
        }

        public static Screen FromCursor()
        {
            Native.GetCursorPos(out var c);
            return FromPoint(c.x, c.y);
        }

        /// <summary>Pencerenin en cok kapladigi ekran (simge durumundakiler icin son konumu).</summary>
        public static Screen FromWindow(IntPtr h, List<Screen> all)
        {
            Native.RECT r;
            if (Native.IsIconic(h))
            {
                var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (!GetWindowPlacement(h, ref wp)) return all[0];
                r = wp.rcNormalPosition; // calisma alani koordinatlari; birincil ekranda gorev cubugu ustte degilse ayni
            }
            else if (!Native.GetWindowRect(h, out r)) return all[0];
            Screen best = null; long bestArea = 0;
            foreach (var s in all)
            {
                long w = Math.Min(r.right, s.Bounds.right) - Math.Max(r.left, s.Bounds.left);
                long hgt = Math.Min(r.bottom, s.Bounds.bottom) - Math.Max(r.top, s.Bounds.top);
                if (w > 0 && hgt > 0 && w * hgt > bestArea) { bestArea = w * hgt; best = s; }
            }
            return best ?? Nearest(all, (r.left + r.right) / 2, (r.top + r.bottom) / 2);
        }

        static Screen Nearest(List<Screen> all, int x, int y) => all.OrderBy(s =>
        {
            double dx = Math.Max(Math.Max(s.Bounds.left - x, 0), x - s.Bounds.right);
            double dy = Math.Max(Math.Max(s.Bounds.top - y, 0), y - s.Bounds.bottom);
            return dx * dx + dy * dy;
        }).First();

        /// <summary>Ekranin hemen altinda baska bir ekran var mi? (dock asagi kayarak gizlenirken orada gorunmesin)</summary>
        public static bool HasScreenBelow(Screen s, List<Screen> all) => all.Any(o => o != s && o.Bounds.top >= s.Bounds.bottom - 1 && o.Bounds.top <= s.Bounds.bottom + 1
                                                                                  && o.Bounds.left < s.Bounds.right && o.Bounds.right > s.Bounds.left);

        public static bool Same(Screen a, Screen b) => a != null && b != null && a.Device == b.Device;

        /// <summary>Pencereyi fiziksel piksel konumuna tasir. Boyut degismez.</summary>
        public static void MoveTo(IntPtr h, double x, double y)
        {
            if (h == IntPtr.Zero) return;
            SetWindowPos(h, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        /// <summary>Pencereyi once hedef ekrana alir (DPI'i oraya gore guncellensin), sonra o ekranin olcegiyle hesaplanan konuma yerlestirir.
        /// pos: (olcek) -> sol-ust fiziksel piksel.</summary>
        public static void Place(Window w, Screen s, Func<double, (double x, double y)> pos)
        {
            var h = new WindowInteropHelper(w).EnsureHandle();
            if (Native.GetWindowRect(h, out var r))
            {
                int cx = (r.left + r.right) / 2, cy = (r.top + r.bottom) / 2;
                if (!s.Contains(cx, cy)) MoveTo(h, s.Work.left + 1, s.Work.top + 1);
            }
            w.UpdateLayout();
            var (x, y) = pos(VisualTreeHelper.GetDpi(w).DpiScaleX);
            MoveTo(h, x, y);
        }

        public static Native.RECT WindowRect(IntPtr h) { Native.GetWindowRect(h, out var r); return r; }

        const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        delegate bool MonitorEnum(IntPtr hm, IntPtr dc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum cb, IntPtr data);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize; public Native.RECT rcMonitor, rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hm, ref MONITORINFOEX mi);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hm, int type, out uint dx, out uint dy);
        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPLACEMENT { public int length, flags, showCmd; public Native.POINT ptMin, ptMax; public Native.RECT rcNormalPosition; }
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
    }
}
