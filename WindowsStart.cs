using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace FloatingDock
{
    /// <summary>
    /// Windows'un kendi Baslat menusu: Ayarlar > Kisisellestirme > Baslat sayfasindaki secenekler (kullanici kayit defteri)
    /// ve yerel menuyu acma. Gezgin'e kod sokulmaz; yalniz Windows'un kendi ayarlari degistirilir.
    /// </summary>
    static class WindowsStart
    {
        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        /// <summary>Duzen: 0 varsayilan, 1 daha fazla sabitlenen, 2 daha fazla oneri.</summary>
        public static int Layout { get => Get("Start_Layout", 0); set => Set("Start_Layout", value); }
        /// <summary>Baslat'in (ve gizli gorev cubugunun) konumu: true ortada, false solda.</summary>
        public static bool Centered { get => Get("TaskbarAl", 1) != 0; set => Set("TaskbarAl", value ? 1 : 0); }
        public static bool MostUsedApps { get => Get("Start_TrackProgs", 1) != 0; set => Set("Start_TrackProgs", value ? 1 : 0); }
        /// <summary>Onerilen bolumundeki son dosyalar (Gezgin'deki son dosyalari ve atlama listelerini de etkiler).</summary>
        public static bool RecentFiles { get => Get("Start_TrackDocs", 1) != 0; set => Set("Start_TrackDocs", value ? 1 : 0); }
        public static bool Recommendations { get => Get("Start_IrisRecommendations", 1) != 0; set => Set("Start_IrisRecommendations", value ? 1 : 0); }
        public static bool AccountNotifications { get => Get("Start_AccountNotifications", 1) != 0; set => Set("Start_AccountNotifications", value ? 1 : 0); }

        static int Get(string name, int def)
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(Adv); return k?.GetValue(name) is int v ? v : def; }
            catch { return def; }
        }

        static void Set(string name, int value)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(Adv);
                k.SetValue(name, value, RegistryValueKind.DWord);
            }
            catch (Exception ex) { Program.Log(ex); return; }
            Notify();
        }

        /// <summary>Gezgin'e ve Baslat'a ayarlarin degistigini haber ver (Ayarlar uygulamasinin yaptigi gibi).</summary>
        static void Notify() => Task.Run(() =>
        {
            try { SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "TraySettings", 0x0002, 1000, out _); } catch { }
        });

        /// <summary>Windows'un kendi Baslat menusunu ac. Tus FloatingDock isaretiyle gonderilir, Windows tusu kancasi onu gecirir.</summary>
        public static void OpenNative()
        {
            keybd_event(0x5B, 0, 0, Marker);
            keybd_event(0x5B, 0, 2, Marker);
        }

        public static void OpenSettings(string page = "ms-settings:personalization-start")
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page) { UseShellExecute = true }); } catch { }
        }

        static readonly UIntPtr Marker = (UIntPtr)0x46444B; // WinKeyHook ile ayni isaret

        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    }
}
