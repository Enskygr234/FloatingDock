using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace FloatingDock
{
    /// <summary>
    /// Windows'un kendi bildirim balonlarini (sag alttaki toast) kapatir; bildirimler yine bildirim merkezine yazilir,
    /// ada onlari oradan okur. Her uygulamanin "Bildirim basliklarini goster" (ShowBanner) ayari kapatilir,
    /// eski degerler Settings.BannerBackup'ta saklanir ve ayar kapaninca / dock kapaninca geri yuklenir.
    /// </summary>
    static class Banners
    {
        const string Root = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        static readonly string Db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\Notifications\wpndatabase.db");
        static readonly object gate = new object();

        /// <summary>Ayar aciksa tum bilinen uygulamalarin balonlarini kapatir (program basinda ve ayar acilinca).</summary>
        public static void Apply()
        {
            if (!Settings.Current.HideWindowsBanners) return;
            lock (gate)
            {
                bool changed = false;
                foreach (var id in KnownApps()) changed |= HideOne(id);
                if (changed) Settings.Current.SaveQuiet();
            }
        }

        /// <summary>Yeni bir uygulama bildirim gonderdi: onun balonunu da kapat (ilk bildirimi Windows gostermis olabilir).</summary>
        public static void Track(string appId)
        {
            if (!Settings.Current.HideWindowsBanners || string.IsNullOrEmpty(appId)) return;
            lock (gate) { if (HideOne(appId)) Settings.Current.SaveQuiet(); }
        }

        /// <summary>Degistirdigimiz her seyi eski haline getirir. keepSetting: dock kapanirken ayar acik kalsin, sonraki acilista yeniden uygulansin.</summary>
        public static void Restore(bool keepSetting)
        {
            lock (gate)
            {
                var s = Settings.Current;
                if (s.BannerBackup.Count == 0) { if (!keepSetting && s.HideWindowsBanners) { s.HideWindowsBanners = false; s.SaveQuiet(); } return; }
                try
                {
                    using var root = Registry.CurrentUser.CreateSubKey(Root);
                    foreach (var kv in s.BannerBackup)
                    {
                        try
                        {
                            using var k = root.CreateSubKey(kv.Key);
                            if (kv.Value.HasValue) k.SetValue("ShowBanner", kv.Value.Value, RegistryValueKind.DWord);
                            else k.DeleteValue("ShowBanner", false);
                        }
                        catch (Exception ex) { Program.Log(ex); }
                    }
                }
                catch (Exception ex) { Program.Log(ex); }
                s.BannerBackup.Clear();
                if (!keepSetting) s.HideWindowsBanners = false;
                s.SaveQuiet();
            }
        }

        static bool HideOne(string id)
        {
            var backup = Settings.Current.BannerBackup;
            if (backup.ContainsKey(id)) return false;
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(Root + "\\" + id);
                int? old = k.GetValue("ShowBanner") is int v ? v : (int?)null;
                if (old == 0) return false; // kullanici zaten kapatmis: dokunma, geri alirken de acma
                backup[id] = old;
                k.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
                return true;
            }
            catch (Exception ex) { Program.Log(ex); return false; }
        }

        /// <summary>Bildirim gondermis (ya da ayar kaydi olan) tum uygulamalar.</summary>
        static IEnumerable<string> KnownApps()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(Root);
                if (root != null)
                    foreach (var n in root.GetSubKeyNames())
                        if (!n.StartsWith("NOC_", StringComparison.OrdinalIgnoreCase)) set.Add(n);
            }
            catch (Exception ex) { Program.Log(ex); }
            try
            {
                using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Mode = SqliteOpenMode.ReadOnly }.ToString());
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT PrimaryId FROM NotificationHandler";
                using var r = cmd.ExecuteReader();
                while (r.Read()) if (!r.IsDBNull(0)) { var id = r.GetString(0); if (id.Length > 0) set.Add(id); }
            }
            catch (Exception ex) { Program.Log(ex); }
            return set;
        }
    }
}
