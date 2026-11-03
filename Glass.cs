using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace FloatingDock
{
    /// <summary>Cerceveyiz, odak calmayan, akrilik camli pencere ayarlari (dock ve onizleme icin ortak).</summary>
    static class Glass
    {
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

            int v = 1; Native.DwmSetWindowAttribute(hwnd, 20, ref v, 4); // koyu mod
            v = 2; Native.DwmSetWindowAttribute(hwnd, 33, ref v, 4);     // yuvarlak koseler
            v = 3; Native.DwmSetWindowAttribute(hwnd, 38, ref v, 4);     // akrilik arka plan
            var m = new Native.MARGINS { l = -1, r = -1, t = -1, b = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
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
            int v = 1; Native.DwmSetWindowAttribute(hwnd, 20, ref v, 4);
            v = 2; Native.DwmSetWindowAttribute(hwnd, 33, ref v, 4);
            v = 3; Native.DwmSetWindowAttribute(hwnd, 38, ref v, 4);
            var m = new Native.MARGINS { l = -1, r = -1, t = -1, b = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
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
