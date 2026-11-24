using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FloatingDock
{
    /// <summary>Komut satiri testleri (CI'daki sanal Windows'ta): duvar kagitlarini ciz, uygula, dogrula, geri al.</summary>
    static class WallpaperTest
    {
        /// <summary>--wallpaper-render klasor: bes hazir duvar kagidini 1920x1080 ve kucuk resim olarak kaydeder.</summary>
        public static void Render(string dir)
        {
            Directory.CreateDirectory(dir);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var (id, _, _) in Wallpapers.Builtin)
            {
                try
                {
                    var t0 = DateTime.Now;
                    Save(Wallpapers.Render(id, 1920, 1080), Path.Combine(dir, "wallpaper-" + id + ".png"));
                    File.AppendAllText(Path.Combine(dir, "render.txt"), id + " 1920x1080 " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms" + Environment.NewLine);
                }
                catch (Exception ex) { File.AppendAllText(Path.Combine(dir, "render.txt"), id + " HATA " + ex + Environment.NewLine); }
            }
        }

        static void Save(BitmapSource b, string path)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(b));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        /// <summary>--wallpaper-test cikti.txt apply|restore [kimlik]</summary>
        public static void Run(string outFile, string mode, string id)
        {
            var sb = new StringBuilder();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Startup += async (s, e) =>
            {
                try
                {
                    var mons = Wallpapers.Monitors();
                    sb.AppendLine("Ekranlar: " + string.Join(" | ", mons.Select(m => m.Label + " [" + m.Id + "]")));
                    foreach (var m in mons) sb.AppendLine("Önce: " + m.Label + " -> " + Wallpapers.WindowsWallpaper(m.Id));
                    if (mode == "apply")
                    {
                        var err = await Wallpapers.ApplyAsync(id, null, "fill");
                        sb.AppendLine("Uygula(" + id + "): " + (err ?? "tamam"));
                        foreach (var m in mons)
                        {
                            var now = Wallpapers.WindowsWallpaper(m.Id);
                            sb.AppendLine("Sonra: " + m.Label + " -> " + now + (now != null && now.Contains(id) ? "  ✓ Windows duvar kâğıdı değişti" : "  ✗"));
                        }
                        sb.AppendLine("Yedek: " + string.Join(", ", Settings.Current.WallpaperBackup.Select(kv => kv.Value)));
                    }
                    else
                    {
                        var before = Settings.Current.WallpaperBackup.ToDictionary(kv => kv.Key, kv => kv.Value);
                        bool ok = Wallpapers.Restore();
                        sb.AppendLine("Geri al: " + (ok ? "tamam" : "HATA"));
                        foreach (var m in mons)
                        {
                            var now = Wallpapers.WindowsWallpaper(m.Id);
                            before.TryGetValue(m.Id, out var want);
                            sb.AppendLine("Şimdi: " + m.Label + " -> " + now + (string.Equals(now, want, StringComparison.OrdinalIgnoreCase) ? "  ✓ eski duvar kâğıdı geri geldi" : "  (beklenen: " + want + ")"));
                        }
                    }
                }
                catch (Exception ex) { sb.AppendLine("HATA: " + ex); }
                File.WriteAllText(outFile, sb.ToString());
                app.Shutdown();
            };
            app.Run();
        }
    }
}
