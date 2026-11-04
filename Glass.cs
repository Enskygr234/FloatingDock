using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace FloatingDock
{
    /// <summary>Cerceveyiz, odak calmayan, akrilik camli pencere ayarlari (dock ve onizleme icin ortak).</summary>
    static class Glass
    {
        /// <summary>Windows 11 (22000+) mi? FLOATINGDOCK_WIN10=1 ortam degiskeni Windows 10 gorunumunu zorlar (test icin).</summary>
        public static readonly bool IsWin11 = Environment.GetEnvironmentVariable("FLOATINGDOCK_WIN10") != "1" && Environment.OSVersion.Version.Build >= 22000;
        public static int Build => Environment.OSVersion.Version.Build;

        /// <summary>
        /// Cam arka plan. Windows 11: DWM sistem akriligi + yuvarlak koseler.
        /// Windows 10: SetWindowCompositionAttribute ile akrilik bulaniklik (1803+), koseler Win10 tarzi keskin.
        /// </summary>
        static void Backdrop(IntPtr hwnd, bool panel)
        {
            int v = Theme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, 20, ref v, 4);      // koyu/acik pencere modu (Win10 2004+ / Win11)
            if (IsWin11)
            {
                v = 2; Native.DwmSetWindowAttribute(hwnd, 33, ref v, 4); // yuvarlak koseler
                // panel malzemesi: 2 = Mica, 3 = Akrilik, 4 = Mica Alt. Mica yalniz odakli pencerede gorunur,
                // bu yuzden odak almayan pencereler (onizleme) her zaman akrilik kalir.
                var b = panel ? Settings.Current.Backdrop : "acrylic";
                v = b == "mica" ? 2 : b == "micaalt" ? 4 : 3;
                Native.DwmSetWindowAttribute(hwnd, 38, ref v, 4);
            }
            else
            {
                v = Theme.Dark ? 1 : 0; Native.DwmSetWindowAttribute(hwnd, 19, ref v, 4); // eski Win10 surumleri (1809-1909)
                v = 1; Native.DwmSetWindowAttribute(hwnd, 33, ref v, 4); // (Win11'de zorlanmis testte) koseleri yuvarlama
                if (Build >= 17134) SetAccent(hwnd, 4, Theme.Dark ? 0x401A1612u : 0x40FAF7F6u); // akrilik bulaniklik, hafif tonlu (AABBGGRR)
            }
            var m = new Native.MARGINS { l = -1, r = -1, t = -1, b = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
        }

        [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State, Flags; public uint Color; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)] struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref CompositionData d);

        /// <summary>Windows 10 akrilik/bulaniklik: state 4 = ACCENT_ENABLE_ACRYLICBLURBEHIND, 3 = BLURBEHIND.</summary>
        static void SetAccent(IntPtr hwnd, int state, uint abgr)
        {
            try
            {
                var accent = new AccentPolicy { State = state, Flags = 2, Color = abgr };
                int size = Marshal.SizeOf<AccentPolicy>();
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(accent, ptr, false);
                    var data = new CompositionData { Attribute = 19, Data = ptr, Size = size }; // WCA_ACCENT_POLICY
                    SetWindowCompositionAttribute(hwnd, ref data);
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch { }
        }
        public static void Setup(Window w)
        {
            w.WindowStyle = WindowStyle.SingleBorderWindow;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.Topmost = true;
            w.Background = Brushes.Transparent;
            w.UseLayoutRounding = true;
            WindowChrome.SetWindowChrome(w, new WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
        }

        public static void Apply(IntPtr hwnd)
        {
            var src = HwndSource.FromHwnd(hwnd);
            src.CompositionTarget.BackgroundColor = Colors.Transparent;
            src.AddHook(NoActivateHook);

            long ex = (long)Native.GetWindowLongPtr(hwnd, -20);
            ex |= 0x08000000; // WS_EX_NOACTIVATE
            Native.SetWindowLongPtr(hwnd, -20, (IntPtr)ex);
            long style = (long)Native.GetWindowLongPtr(hwnd, -16);
            style &= ~0x80000L; // WS_SYSMENU yok -> (x) dugmesi cizilmez
            Native.SetWindowLongPtr(hwnd, -16, (IntPtr)style);

            Backdrop(hwnd, false);
        }

        /// <summary>Odak alabilen (klavye girisi icin) cam panel: Alt-Tab'de gorunmez.</summary>
        public static void ApplyFocusable(IntPtr hwnd)
        {
            HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
            long ex = (long)Native.GetWindowLongPtr(hwnd, -20);
            ex |= 0x80; // WS_EX_TOOLWINDOW
            Native.SetWindowLongPtr(hwnd, -20, (IntPtr)ex);
            long style = (long)Native.GetWindowLongPtr(hwnd, -16);
            style &= ~0x80000L;
            Native.SetWindowLongPtr(hwnd, -16, (IntPtr)style);
            Backdrop(hwnd, true);
        }

        /// <summary>Sadece odak calmama + Alt-Tab'de gorunmeme (seffaf katmanli pencereler icin).</summary>
        public static void NoActivate(IntPtr hwnd)
        {
            HwndSource.FromHwnd(hwnd).AddHook(NoActivateHook);
            long ex = (long)Native.GetWindowLongPtr(hwnd, -20);
            ex |= 0x08000000 | 0x80; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            Native.SetWindowLongPtr(hwnd, -20, (IntPtr)ex);
        }

        static IntPtr NoActivateHook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0021) { handled = true; return (IntPtr)3; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            return IntPtr.Zero;
        }
    }
}
