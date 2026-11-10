using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FloatingDock
{
    /// <summary>Bir arka plan uygulamasinin tepsi simgesi.</summary>
    class TrayIcon
    {
        public IntPtr Owner;
        public uint Id, CallbackMessage, Version;
        public Guid Guid;
        public ImageSource Image;
        public string Tip = "";
        public bool Hidden;
        public uint Pid;

        public bool Matches(IntPtr owner, uint id, Guid guid, bool useGuid) =>
            useGuid && guid != Guid.Empty ? Guid == guid : Owner == owner && Id == id;
    }

    /// <summary>
    /// Sistem tepsisi: Windows'taki uygulamalar simgelerini Shell_NotifyIcon ile "Shell_TrayWnd" sinifli pencereye
    /// WM_COPYDATA olarak gonderir. Ayni sinifta, z-sirasinda Explorer'in gorev cubugunun ustunde duran gizli bir
    /// pencere acariz; boylece bu mesajlar once bize gelir. Her mesaji Explorer'a da aynen iletiriz, yani Windows'un
    /// kendi tepsisi de guncel kalir (dock kapaninca simgeler kaybolmaz).
    /// </summary>
    static class TrayHost
    {
        const uint WM_COPYDATA = 0x004A, WM_COMMAND = 0x0111, WM_USER = 0x0400;
        const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETFOCUS = 3, NIM_SETVERSION = 4;
        const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_STATE = 0x8, NIF_GUID = 0x20;
        const uint NIS_HIDDEN = 0x1;
        const string ClassName = "Shell_TrayWnd";
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_BOTTOM = new IntPtr(1);

        public static readonly List<TrayIcon> Icons = new List<TrayIcon>();
        /// <summary>Simgeler eklendi / degisti / silindi (UI is parcaciginda).</summary>
        public static event Action Changed;
        /// <summary>Uygulamalar tepsi simgesinin yerini sordugunda dondurulecek alan (fiziksel piksel): dock'taki ˄ dugmesi.</summary>
        public static Func<Native.RECT?> AnchorRect;

        static IntPtr hwnd;
        static Native.WndProcDelegate proc; // GC toplamasin
        static uint taskbarCreated;
        static DateTime lastBroadcast;

        public static bool Running => hwnd != IntPtr.Zero;

        // Shell_NotifyIcon'in WM_COPYDATA ile gonderdigi yapi (64 bit sistemde de tutamaclar 32 bit)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA32
        {
            public int cbSize;
            public uint hWnd, uID, uFlags, uCallbackMessage, hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion; // uTimeout ile ayni alan
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public uint hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SHELLTRAYDATA
        {
            public int dwSignature;
            public uint dwMessage;
            public NOTIFYICONDATA32 nid;
        }

        static IntPtr H(uint v) => new IntPtr(unchecked((int)v)); // 32 bit tutamaci isaret genisletmesiyle IntPtr'a cevir

        /// <summary>Explorer'in kendi gorev cubugu penceresi (bizimki degil).</summary>
        public static IntPtr ExplorerTray()
        {
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, ClassName, null)) != IntPtr.Zero)
                if (h != hwnd) return h;
            return IntPtr.Zero;
        }

        public static void Start()
        {
            if (hwnd != IntPtr.Zero) return;
            try
            {
                proc = WndProc;
                var inst = Native.GetModuleHandle(null);
                var wc = new Native.WNDCLASSEX { cbSize = Marshal.SizeOf<Native.WNDCLASSEX>(), lpfnWndProc = proc, hInstance = inst, lpszClassName = ClassName };
                if (Native.RegisterClassEx(ref wc) == 0) { Program.Log(new Exception("Tepsi: pencere sinifi kaydedilemedi " + Marshal.GetLastWin32Error())); return; }
                // WS_POPUP, gizli; WS_EX_TOOLWINDOW | WS_EX_TOPMOST
                int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
                hwnd = Native.CreateWindowEx(0x80 | 0x8, ClassName, "", 0x80000000, 0, sh - 48, sw, 48, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) { Program.Log(new Exception("Tepsi: pencere acilamadi " + Marshal.GetLastWin32Error())); return; }
                taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
                lastBroadcast = DateTime.Now;
                KeepOnTop();
                // calisan uygulamalar simgelerini yeniden eklesin (Explorer yeniden basladiginda yaptiklari gibi)
                Native.SendNotifyMessage(new IntPtr(0xFFFF), taskbarCreated, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex) { Program.Log(ex); hwnd = IntPtr.Zero; }
        }

        /// <summary>Bizim pencere z-sirasinda Explorer'inkinin ustunde kalsin (FindWindow once bizi bulsun).</summary>
        public static void KeepOnTop()
        {
            if (hwnd == IntPtr.Zero) return;
            const uint flags = 0x0001 | 0x0002 | 0x0010; // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
            if (Native.FindWindow(ClassName, null) != hwnd)
            {
                var ex = ExplorerTray();
                if (ex != IntPtr.Zero) Native.SetWindowPos(ex, HWND_BOTTOM, 0, 0, 0, 0, flags);
                Native.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, flags);
                // arada Explorer'a giden simgeler olabilir: uygulamalar yeniden eklesin (en fazla 5 sn'de bir)
                if ((DateTime.Now - lastBroadcast).TotalSeconds > 5)
                {
                    lastBroadcast = DateTime.Now;
                    Native.SendNotifyMessage(new IntPtr(0xFFFF), taskbarCreated, IntPtr.Zero, IntPtr.Zero);
                }
            }
            // sahibi kapanmis simgeleri temizle
            if (Icons.RemoveAll(i => !Native.IsWindow(i.Owner)) > 0) Changed?.Invoke();
        }

        public static void Stop()
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                Native.DestroyWindow(hwnd);
                Native.UnregisterClass(ClassName, Native.GetModuleHandle(null));
                var ex = ExplorerTray();
                if (ex != IntPtr.Zero) Native.SetWindowPos(ex, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
            catch { }
            hwnd = IntPtr.Zero;
        }

        static IntPtr Forward(uint msg, IntPtr w, IntPtr l)
        {
            var ex = ExplorerTray();
            if (ex == IntPtr.Zero) return IntPtr.Zero;
            Native.SendMessageTimeout(ex, msg, w, l, 0x0002, 2000, out var r); // SMTO_ABORTIFHUNG
            return r;
        }

        static IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            try
            {
                if (msg == WM_COPYDATA && l != IntPtr.Zero)
                {
                    var cds = Marshal.PtrToStructure<Native.COPYDATASTRUCT>(l);
                    long kind = (long)cds.dwData;
                    if (kind == 1) // Shell_NotifyIcon
                    {
                        var fwd = Forward(msg, w, l); // Explorer da bilsin
                        bool ok = HandleNotify(cds);
                        return ok ? (IntPtr)1 : fwd;
                    }
                    if (kind == 3 && cds.cbData >= 8 && AnchorRect?.Invoke() is Native.RECT r) // Shell_NotifyIconGetRect
                    {
                        int which = Marshal.ReadInt32(cds.lpData, 4);
                        int x = which == 1 ? r.left : r.right, y = which == 1 ? r.top : r.bottom;
                        return (IntPtr)((y << 16) | (x & 0xFFFF));
                    }
                    return Forward(msg, w, l); // AppBar mesajlari vb.: Explorer cevaplasin
                }
                if (msg == WM_COMMAND || (msg >= WM_USER && msg < 0x8000))
                    return Forward(msg, w, l);
            }
            catch (Exception ex) { Program.Log(ex); }
            return Native.DefWindowProc(h, msg, w, l);
        }

        static bool HandleNotify(Native.COPYDATASTRUCT cds)
        {
            int size = Marshal.SizeOf<SHELLTRAYDATA>();
            if (cds.cbData < 8 || cds.lpData == IntPtr.Zero) return false;
            // gelen veri kisa olabilir: tam boyutlu, sifirlanmis bir tampona kopyala
            var buf = Marshal.AllocHGlobal(size);
            SHELLTRAYDATA d;
            try
            {
                var zero = new byte[size];
                Marshal.Copy(zero, 0, buf, size);
                var bytes = new byte[Math.Min(size, cds.cbData)];
                Marshal.Copy(cds.lpData, bytes, 0, bytes.Length);
                Marshal.Copy(bytes, 0, buf, bytes.Length);
                d = Marshal.PtrToStructure<SHELLTRAYDATA>(buf);
            }
            finally { Marshal.FreeHGlobal(buf); }

            var n = d.nid;
            var owner = H(n.hWnd);
            bool useGuid = (n.uFlags & NIF_GUID) != 0;
            var icon = Icons.FirstOrDefault(i => i.Matches(owner, n.uID, n.guidItem, useGuid));

            switch (d.dwMessage)
            {
                case NIM_ADD:
                    // ayni simge yeniden eklenirse (Explorer yeniden basladi vb.) guncelle
                    if (icon != null) { Apply(icon, n); break; }
                    icon = new TrayIcon { Owner = owner, Id = n.uID, Guid = useGuid ? n.guidItem : Guid.Empty };
                    Native.GetWindowThreadProcessId(owner, out icon.Pid);
                    Apply(icon, n);
                    Icons.Add(icon);
                    break;
                case NIM_MODIFY:
                    if (icon == null) return false;
                    Apply(icon, n);
                    break;
                case NIM_DELETE:
                    if (icon == null) return false;
                    Icons.Remove(icon);
                    break;
                case NIM_SETVERSION:
                    if (icon == null) return false;
                    icon.Version = n.uVersion;
                    return true;
                case NIM_SETFOCUS:
                    return icon != null;
                default:
                    return false;
            }
            Changed?.Invoke();
            return true;
        }

        static void Apply(TrayIcon icon, NOTIFYICONDATA32 n)
        {
            if ((n.uFlags & NIF_MESSAGE) != 0) icon.CallbackMessage = n.uCallbackMessage;
            if ((n.uFlags & NIF_TIP) != 0) icon.Tip = (n.szTip ?? "").Trim();
            if ((n.uFlags & NIF_STATE) != 0 && (n.dwStateMask & NIS_HIDDEN) != 0) icon.Hidden = (n.dwState & NIS_HIDDEN) != 0;
            if ((n.uFlags & NIF_ICON) != 0)
            {
                // simge tutamaci uygulamaya ait, sonradan silinebilir: hemen kendi resmimize cevir
                var copy = Native.CopyIcon(H(n.hIcon));
                if (copy != IntPtr.Zero)
                {
                    try
                    {
                        var src = Imaging.CreateBitmapSourceFromHIcon(copy, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                        icon.Image = src;
                    }
                    catch { }
                    finally { Native.DestroyIcon(copy); }
                }
            }
        }

        // ---------------- tiklamalar ----------------
        const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203,
                   WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_CONTEXTMENU = 0x007B, NIN_SELECT = 0x400;

        public static void LeftClick(TrayIcon i) { Send(i, WM_LBUTTONDOWN); Send(i, WM_LBUTTONUP); if (i.Version >= 3) Send(i, NIN_SELECT); }
        public static void DoubleClick(TrayIcon i) { Send(i, WM_LBUTTONDBLCLK); Send(i, WM_LBUTTONUP); }
        public static void RightClick(TrayIcon i) { Send(i, WM_RBUTTONDOWN); Send(i, WM_RBUTTONUP); if (i.Version >= 3) Send(i, WM_CONTEXTMENU); }
        public static void Hover(TrayIcon i) => Send(i, WM_MOUSEMOVE);

        static void Send(TrayIcon i, uint mouseMsg)
        {
            if (i.CallbackMessage == 0 || !Native.IsWindow(i.Owner)) return;
            // uygulama kendi penceresini / menusunu one getirebilsin
            if (i.Pid != 0) Native.AllowSetForegroundWindow(i.Pid);
            Native.GetCursorPos(out var p);
            IntPtr w, l;
            if (i.Version >= 4)
            {
                w = (IntPtr)(((p.y & 0xFFFF) << 16) | (p.x & 0xFFFF));
                l = (IntPtr)(((i.Id & 0xFFFF) << 16) | (mouseMsg & 0xFFFF));
            }
            else
            {
                w = (IntPtr)i.Id;
                l = (IntPtr)mouseMsg;
            }
            Native.PostMessage(i.Owner, i.CallbackMessage, w, l);
        }
    }
}
