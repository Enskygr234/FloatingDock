using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>
    /// GitHub'daki son surume bakar (gunde bir kez, ayarlardan kapatilabilir). Yeni surum varsa adada haber verir;
    /// tiklaninca yeni exe indirilir, bu exe'nin yerine konur ve FloatingDock yeniden baslar.
    /// Gonderilen tek istek: api.github.com'dan son surum bilgisi. Kisisel bilgi gonderilmez.
    /// </summary>
    static class Updater
    {
        const string Repo = "Enskygr234/FloatingDock";
        const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
        static readonly HttpClient http = CreateClient();
        static bool busy;

        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FloatingDock/" + Current);
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>Acilista cagrilir: ayar aciksa 20 sn sonra ve sonra saatte bir (son kontrolden 24 saat gectiyse) bakar.</summary>
        public static void StartAutomatic(Dispatcher d)
        {
            var t = new DispatcherTimer(DispatcherPriority.Background, d) { Interval = TimeSpan.FromSeconds(20) };
            t.Tick += async (s, e) =>
            {
                t.Interval = TimeSpan.FromHours(1);
                var st = Settings.Current;
                if (!st.CheckUpdates || (DateTime.Now - st.LastUpdateCheck).TotalHours < 24) return;
                await Check(false);
            };
            t.Start();
        }

        /// <summary>manual: kullanici menuden istediyse "guncelsin" / hata mesaji da gosterilir.</summary>
        public static async Task Check(bool manual)
        {
            if (busy) return;
            busy = true;
            try
            {
                var (version, exeUrl) = await Latest();
                var st = Settings.Current;
                st.LastUpdateCheck = DateTime.Now;
                st.Save();
                if (version == null) { if (manual) Hub.Toast(L.T("Güncelleme bulunamadı", "No update found"), L.T("Sürüm bilgisi okunamadı", "Couldn't read the release info")); return; }
                if (version <= Current)
                {
                    if (manual) Hub.Toast(L.T("FloatingDock güncel", "FloatingDock is up to date"), L.T("Sürüm ", "Version ") + Current);
                    return;
                }
                if (!manual && st.SkippedVersion == version.ToString()) return;
                Hub.Toast(L.T("Yeni sürüm: FloatingDock ", "New version: FloatingDock ") + version,
                    L.T("Güncellemek için tıkla, dock kendini yeniler", "Click to update, the dock restarts itself"),
                    () => _ = Install(exeUrl));
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                if (manual) Hub.Toast(L.T("Güncelleme denetlenemedi", "Couldn't check for updates"), L.T("İnternet bağlantını kontrol et", "Check your internet connection"));
            }
            finally { busy = false; }
        }

        static async Task<(Version, string)> Latest()
        {
            var json = await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string exe = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : "";
                    if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && a.TryGetProperty("browser_download_url", out var u))
                    { exe = u.GetString(); break; }
                }
            return (Parse(tag), exe);
        }

        /// <summary>"v1.3.0", "1.3", "FloatingDock 1.3.0" gibi etiketlerden surum.</summary>
        static Version Parse(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(tag, @"\d+(\.\d+){1,2}");
            if (!m.Success || !Version.TryParse(m.Value, out var v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        /// <summary>Yeni exe'yi indirir, bu exe'nin yerine koyar ve yeniden baslatir. Olmazsa surum sayfasini acar.</summary>
        static async Task Install(string exeUrl)
        {
            var self = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exeUrl) || string.IsNullOrEmpty(self) || !self.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                OpenPage();
                return;
            }
            try
            {
                Hub.Toast(L.T("Güncelleme indiriliyor…", "Downloading update…"), null);
                var dir = Path.Combine(Path.GetTempPath(), "FloatingDockUpdate");
                Directory.CreateDirectory(dir);
                var tmp = Path.Combine(dir, "FloatingDock.exe");
                using (var resp = await http.GetAsync(exeUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    using var fs = File.Create(tmp);
                    await resp.Content.CopyToAsync(fs);
                }
                if (new FileInfo(tmp).Length < 1_000_000) throw new Exception("indirilen dosya cok kucuk");

                // calisan exe'nin uzerine yazilamaz: kucuk bir betik bu surec kapaninca dosyayi tasir ve yeniden baslatir
                var script = Path.Combine(dir, "update.cmd");
                File.WriteAllText(script,
                    "@echo off\r\n" +
                    ":wait\r\n" +
                    "tasklist /FI \"PID eq " + Environment.ProcessId + "\" | find \"" + Environment.ProcessId + "\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                    "move /y \"" + tmp + "\" \"" + self + "\" >nul\r\n" +
                    "start \"\" \"" + self + "\"\r\n");
                Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true });
                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                OpenPage();
            }
        }

        static void OpenPage()
        {
            try { Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true }); } catch { }
        }
    }
}
