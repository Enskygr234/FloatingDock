using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace FloatingDock
{
    /// <summary>
    /// Windows'un kendi bildirim balonlarini kapatir / geri acar. Bildirimler bildirim merkezine yazilmaya devam eder,
    /// FloatingDock onlari adada ve takvim panelinde gosterir. Yalniz bizim kapattigimiz uygulamalar geri acilir.
    /// </summary>
    static class Banners
    {
        const string Root = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        static readonly string Db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\Notifications\wpndatabase.db");

        /// <summary>Ayar degisti ya da program acildi: ayara gore tum uygulamalara uygula.</summary>
        public static void Apply()
        {
            try
            {
                var s = Settings.Current;
                if (s.HideWindowsBanners) foreach (var id in KnownApps()) Hide(id, false);
                else Restore();
                s.Save();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        /// <summary>Yeni bir uygulamadan bildirim geldi: ayar aciksa onun balonunu da kapat.</summary>
        public static void Ensure(string appId)
        {
            if (!Settings.Current.HideWindowsBanners || string.IsNullOrEmpty(appId)) return;
            try { if (Hide(appId, true)) Settings.Current.Save(); } catch (Exception ex) { Program.Log(ex); }
        }

        static bool Hide(string appId, bool onlyIfNew)
        {
            var s = Settings.Current;
            if (onlyIfNew && s.BannerHiddenApps.Contains(appId, StringComparer.OrdinalIgnoreCase)) return false;
            using var k = Registry.CurrentUser.CreateSubKey(Root + "\\" + appId);
            if (k == null) return false;
            // kullanici zaten kendisi kapattiysa dokunma (geri acarken de acmayalim)
            if (k.GetValue("ShowBanner") is int v && v == 0) return false;
            k.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
            if (!s.BannerHiddenApps.Contains(appId, StringComparer.OrdinalIgnoreCase)) s.BannerHiddenApps.Add(appId);
            return true;
        }

        /// <summary>Dock kapanirken: balonlari geri ac (ayar korunur, sonraki acilista yine kapatilir).</summary>
        public static void Release()
        {
            if (Settings.Current.BannerHiddenApps.Count == 0) return;
            try { Restore(); Settings.Current.Save(); } catch { }
        }

        static void Restore()
        {
            var s = Settings.Current;
            foreach (var id in s.BannerHiddenApps)
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(Root + "\\" + id, true);
                    k?.DeleteValue("ShowBanner", false); // varsayilan: balon acik
                }
                catch { }
            }
            s.BannerHiddenApps.Clear();
        }

        /// <summary>Bildirim gondermis uygulamalar: kayit defterindekiler + bildirim veritabanindakiler.</summary>
        static IEnumerable<string> KnownApps()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var k = Registry.CurrentUser.OpenSubKey(Root))
                if (k != null) foreach (var n in k.GetSubKeyNames()) ids.Add(n);
            try
            {
                using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Mode = SqliteOpenMode.ReadOnly }.ToString());
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT PrimaryId FROM NotificationHandler";
                using var r = cmd.ExecuteReader();
                while (r.Read()) if (!r.IsDBNull(0)) ids.Add(r.GetString(0));
            }
            catch { }
            // bazi sistem anahtarlari uygulama degil
            return ids.Where(i => !string.IsNullOrWhiteSpace(i) && !i.StartsWith("Windows.SystemToast.SecurityAndMaintenance", StringComparison.OrdinalIgnoreCase));
        }
    }
}
