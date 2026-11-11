using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace FloatingDock
{
    /// <summary>
    /// Dock ikonlarindaki okunmamis rozetleri.
    /// 1) Uygulamanin Windows'a bildirdigi gercek rozet (WhatsApp, Posta, Outlook... gorev cubugunda gosterdigi sayi):
    ///    Windows bildirim veritabanindaki "badge" kayitlari (yalniz okunur).
    /// 2) Rozet bildirmeyen uygulamalar icin: uygulamanin penceresine en son bakildiktan sonra gelen bildirim sayisi.
    /// Bildirimler uygulama kimligiyle (AUMID) gelir; dock ise exe yolunu bilir: pencerenin / surecin AUMID'i
    /// ve paket adi ile eslestirilir.
    /// </summary>
    static class Badges
    {
        static readonly string Db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\Notifications\wpndatabase.db");
        static readonly Dictionary<string, int> toasts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<string, string> system = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static System.Threading.Timer timer;
        static readonly object gate = new object();
        /// <summary>Arka plan is parcacigindan gelebilir.</summary>
        public static event Action Changed;

        public static void Start()
        {
            timer ??= new System.Threading.Timer(_ => PollSystem(), null, 1000, 3000);
        }

        // ---------------- bildirim sayaci ----------------
        /// <summary>Hub her yeni bildirimde cagirir.</summary>
        public static void OnToast(string aumid)
        {
            if (string.IsNullOrEmpty(aumid) || !Settings.Current.Badges) return;
            lock (gate) { toasts.TryGetValue(aumid, out int n); toasts[aumid] = n + 1; }
            Changed?.Invoke();
        }

        /// <summary>Uygulamanin penceresine bakildi: bildirim sayacini sifirla (gercek rozete uygulama karar verir).</summary>
        public static void Seen(string exePath, IEnumerable<string> aumids)
        {
            bool any = false;
            lock (gate)
            {
                if (toasts.Count == 0) return;
                var ids = Ids(exePath, aumids);
                foreach (var k in toasts.Keys.Where(k => Matches(k, exePath, ids)).ToList()) { toasts.Remove(k); any = true; }
            }
            if (any) Changed?.Invoke();
        }

        public static void Clear() { lock (gate) toasts.Clear(); Changed?.Invoke(); }

        // ---------------- gercek rozetler ----------------
        static void PollSystem()
        {
            if (!Settings.Current.Badges) return;
            var now = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Mode = SqliteOpenMode.ReadOnly }.ToString());
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT h.PrimaryId, n.Payload FROM Notification n JOIN NotificationHandler h ON n.HandlerId = h.RecordId " +
                                  "WHERE n.Type = 'badge' ORDER BY n.Id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (r.IsDBNull(0)) continue;
                    var raw = r.IsDBNull(1) ? null : r.GetValue(1);
                    string xml = raw is byte[] b ? Encoding.UTF8.GetString(b) : raw as string;
                    var v = ParseBadge(xml);
                    if (v == null) now.Remove(r.GetString(0)); // son kayit kazanir
                    else now[r.GetString(0)] = v;
                }
            }
            catch { return; }
            bool changed;
            lock (gate)
            {
                changed = now.Count != system.Count || now.Any(kv => !system.TryGetValue(kv.Key, out var o) || o != kv.Value);
                system = now;
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>&lt;badge value="3"/&gt; -> "3"; glif (newMessage, attention...) -> "•"; 0 / none -> null.</summary>
        static string ParseBadge(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            try
            {
                var v = (string)XDocument.Parse(xml.TrimEnd('\0')).Root?.Attribute("value");
                if (string.IsNullOrEmpty(v) || v == "none" || v == "0") return null;
                return int.TryParse(v, out int n) ? (n > 99 ? "99+" : n.ToString()) : "•";
            }
            catch { return null; }
        }

        // ---------------- dock ikonu icin ----------------
        /// <summary>Bu uygulamanin rozeti: sayi, "•" ya da null.</summary>
        public static string For(string exePath, IEnumerable<string> aumids)
        {
            if (!Settings.Current.Badges) return null;
            lock (gate)
            {
                if (system.Count == 0 && toasts.Count == 0) return null;
                var ids = Ids(exePath, aumids);
                // uygulamanin kendi rozeti varsa o gecerli
                foreach (var kv in system) if (Matches(kv.Key, exePath, ids)) return kv.Value;
                int sum = toasts.Where(kv => Matches(kv.Key, exePath, ids)).Sum(kv => kv.Value);
                return sum <= 0 ? null : sum > 99 ? "99+" : sum.ToString();
            }
        }

        static HashSet<string> Ids(string exePath, IEnumerable<string> aumids)
        {
            var set = new HashSet<string>(aumids?.Where(a => !string.IsNullOrEmpty(a)) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var fam = PackageFamily(exePath);
            if (fam != null) set.Add(fam);
            return set;
        }

        static readonly HashSet<string> Generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "exe", "app", "com", "microsoft", "windows", "desktop", "client", "win32", "x64", "setup", "update", "launcher" };

        static bool Matches(string aumid, string exePath, HashSet<string> ids)
        {
            if (ids.Contains(aumid)) return true;
            // paketli uygulama: "Aile_yayinci!App" -> kimlik paket ailesiyle baslar
            int bang = aumid.IndexOf('!');
            if (bang > 0 && ids.Contains(aumid.Substring(0, bang))) return true;
            // klasik uygulama: AUMID'in bir parcasi exe adi mi? ("com.squirrel.Discord.Discord" ~ Discord.exe)
            if (string.IsNullOrEmpty(exePath)) return false;
            var exe = Path.GetFileNameWithoutExtension(exePath);
            if (exe.Length < 3 || Generic.Contains(exe)) return false;
            return aumid.Split('.', '!', '_', ' ', '\\').Any(p => p.Equals(exe, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>"...\WindowsApps\5319275A.WhatsAppDesktop_2.24_x64__cv1g1gvanyjgm\WhatsApp.exe" -> "5319275A.WhatsAppDesktop_cv1g1gvanyjgm"</summary>
        static string PackageFamily(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            int i = exePath.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            var dir = exePath.Substring(i + 13).Split('\\')[0];
            var parts = dir.Split('_');
            return parts.Length >= 5 ? parts[0] + "_" + parts[parts.Length - 1] : null;
        }

        // ---------------- pencere / surec kimligi ----------------
        static readonly Dictionary<IntPtr, string> winIds = new Dictionary<IntPtr, string>();

        /// <summary>Pencerenin uygulama kimligi (pencereye ozel ya da paketli surecin). Onbellekli.</summary>
        public static string WindowAumid(IntPtr h)
        {
            lock (winIds)
            {
                if (winIds.TryGetValue(h, out var id)) return id;
                if (winIds.Count > 500) winIds.Clear();
                id = FromWindow(h) ?? FromProcess(h);
                winIds[h] = id;
                return id;
            }
        }

        static string FromWindow(IntPtr h)
        {
            try
            {
                var iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(h, ref iid, out var ps) != 0 || ps == null) return null;
                try
                {
                    var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 }; // PKEY_AppUserModel_ID
                    if (ps.GetValue(ref key, out var v) != 0 || v.vt != 31) return null;
                    var s = Marshal.PtrToStringUni(v.p);
                    PropVariantClear(ref v);
                    return string.IsNullOrEmpty(s) ? null : s;
                }
                finally { Marshal.ReleaseComObject(ps); }
            }
            catch { return null; }
        }

        static string FromProcess(IntPtr h)
        {
            Native.GetWindowThreadProcessId(h, out uint pid);
            var ph = Native.OpenProcess(0x1000, false, pid);
            if (ph == IntPtr.Zero) return null;
            try
            {
                uint len = 256;
                var sb = new StringBuilder((int)len);
                return GetApplicationUserModelId(ph, ref len, sb) == 0 ? sb.ToString() : null;
            }
            catch { return null; }
            finally { Native.CloseHandle(ph); }
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int c);
            [PreserveSig] int GetAt(int i, out PROPERTYKEY k);
            [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        }
        [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
        [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt; ushort r1, r2, r3; public IntPtr p; IntPtr p2; }
        [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore ps);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr process, ref uint len, StringBuilder id);
    }
}
