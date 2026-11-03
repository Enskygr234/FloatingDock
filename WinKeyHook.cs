using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FloatingDock
{
    /// <summary>
    /// Windows tusuna TEK basildiginda Windows'un Baslat menusu yerine bizimkini acar.
    /// Win+E, Win+D, Win+Shift+S gibi kisayollar aynen calismaya devam eder.
    /// </summary>
    sealed class WinKeyHook : IDisposable
    {
        delegate IntPtr Proc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, Proc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        [StructLayout(LayoutKind.Sequential)]
        struct KBD { public uint vk, scan, flags, time; public UIntPtr extra; }

        const int WH_KEYBOARD_LL = 13;
        const byte VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_DUMMY = 0xE8; // 0xE8: atanmamis tus
        static readonly UIntPtr Marker = (UIntPtr)0x46444B; // "FDK": kendi gonderdigimiz tuslari tanimak icin

        readonly Proc proc;
        readonly Action onWin;
        IntPtr hook;
        bool winDown, combo;

        static readonly string RegKey = @"Software\FloatingDock";
        public static bool Enabled
        {
            get { using var k = Registry.CurrentUser.OpenSubKey(RegKey); return (k?.GetValue("WinKey") as int?) != 0; }
            set { using var k = Registry.CurrentUser.CreateSubKey(RegKey); k.SetValue("WinKey", value ? 1 : 0, RegistryValueKind.DWord); }
        }

        public WinKeyHook(Action onWin)
        {
            this.onWin = onWin;
            proc = Callback;
        }

        public void Start()
        {
            if (hook != IntPtr.Zero) return;
            using var m = Process.GetCurrentProcess().MainModule;
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(m.ModuleName), 0);
        }

        public void Stop()
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            winDown = false;
        }

        IntPtr Callback(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                var k = Marshal.PtrToStructure<KBD>(l);
                if (k.extra != Marker)
                {
                    int msg = (int)w;
                    bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                    bool isWin = k.vk == VK_LWIN || k.vk == VK_RWIN;
                    if (isWin && down)
                    {
                        if (!winDown) { winDown = true; combo = false; }
                    }
                    else if (isWin && up)
                    {
                        if (winDown && !combo)
                        {
                            // Windows bunu "Win+baska tus" sansin -> kendi Baslat menusunu acmasin
                            keybd_event(VK_DUMMY, 0, 0, Marker);
                            keybd_event(VK_DUMMY, 0, 2, Marker);
                            try { onWin(); } catch { }
                        }
                        winDown = false;
                    }
                    else if (winDown && down) combo = true;
                }
            }
            return CallNextHookEx(hook, code, w, l);
        }

        public void Dispose() => Stop();
    }
}
