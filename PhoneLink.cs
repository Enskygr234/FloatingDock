using System;
using System.Diagnostics;
using System.IO;

namespace FloatingDock
{
    /// <summary>Windows'un Telefon Baglantisi (Phone Link) uygulamasi: kurulu mu, acma, bildirimlerini tanima.</summary>
    static class PhoneLink
    {
        const string Package = "Microsoft.YourPhone_8wekyb3d8bbwe";

        public static bool Installed =>
            Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", Package));

        /// <summary>Bildirim Telefon Baglantisi'ndan mi geldi (telefondaki uygulamalarin bildirimleri bu kimlikle gelir).</summary>
        public static bool IsPhoneApp(string appId) =>
            !string.IsNullOrEmpty(appId) && (appId.IndexOf("YourPhone", StringComparison.OrdinalIgnoreCase) >= 0 || appId.IndexOf("PhoneLink", StringComparison.OrdinalIgnoreCase) >= 0);

        public static void Open() => Shell(Installed ? "shell:AppsFolder\\" + Package + "!App" : "ms-windows-store://pdp/?ProductId=9NMPJ99VJBWV", true);
        public static void OpenSettings() => Shell("ms-settings:mobile-devices", false);
        public static void OpenNotificationSettings() => Shell("ms-settings:notifications", false);

        static void Shell(string target, bool explorer)
        {
            try
            {
                if (explorer && target.StartsWith("shell:", StringComparison.Ordinal)) Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex) { Program.Log(ex); }
        }
    }
}
