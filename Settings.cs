using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace FloatingDock
{
    /// <summary>Kullanici ayarlari: %LOCALAPPDATA%\FloatingDock\settings.json</summary>
    class Settings
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatingDock");
        static readonly string FilePath = Path.Combine(Dir, "settings.json");

        /// <summary>"system" | "dark" | "light"</summary>
        public string Theme { get; set; } = "system";
        /// <summary>"frost" (buz mavisi) | "windows" (Windows vurgu rengi)</summary>
        public string Accent { get; set; } = "frost";
        /// <summary>"small" | "medium" | "large"</summary>
        public string DockSize { get; set; } = "medium";
        public bool Magnify { get; set; } = true;
        public bool AutoHide { get; set; } = true;
        /// <summary>Rahatsiz etme: bildirimler adada gosterilmez, sadece gecmise yazilir.</summary>
        public bool DoNotDisturb { get; set; }
        /// <summary>Windows 11 panel malzemesi: "acrylic" | "mica" | "micaalt"</summary>
        public string Backdrop { get; set; } = "acrylic";
        /// <summary>Dock'ta Gorev gorunumu (Win+Tab) dugmesi.</summary>
        public bool TaskViewButton { get; set; } = true;
        /// <summary>Dock'ta Widget'lar (Win+W) dugmesi (yalniz Windows 11).</summary>
        public bool WidgetsButton { get; set; }
        /// <summary>Dock'ta sabitlenen uygulamalarin sirasi (anahtar = exe yolu ya da kisayol).</summary>
        public List<string> DockOrder { get; set; } = new List<string>();
        /// <summary>Surukle-birak ile eklenen sabitler (kisayol / exe yolu).</summary>
        public List<string> DockExtra { get; set; } = new List<string>();
        /// <summary>Gorev cubugu sabitlerinden dock'ta gizlenenler.</summary>
        public List<string> DockHidden { get; set; } = new List<string>();
        /// <summary>Arayuz dili: "system" | "tr" | "en"</summary>
        public string Language { get; set; } = "system";
        /// <summary>Arka plan uygulamalarinin simgeleri (sistem tepsisi) dock'ta gosterilsin.</summary>
        public bool TrayIcons { get; set; } = true;
        /// <summary>Diger monitorlerde de dock olsun (Windows gorev cubugu orada da gizlenir).</summary>
        public bool AllMonitors { get; set; } = true;
        /// <summary>Windows'un kendi bildirim balonlari kapatilsin (bildirimler adada ve gecmiste kalir).</summary>
        public bool HideWindowsBanners { get; set; }
        /// <summary>Balonunu bizim kapattigimiz uygulamalar (geri acmak icin).</summary>
        public List<string> BannerHiddenApps { get; set; } = new List<string>();
        /// <summary>GitHub'da yeni surum var mi diye bak (gunde bir kez).</summary>
        public bool CheckUpdates { get; set; } = true;
        public DateTime LastUpdateCheck { get; set; }
        /// <summary>Kullanicinin "bu surumu atla" dedigi surum.</summary>
        public string SkippedVersion { get; set; } = "";

        static Settings current;
        public static Settings Current => current ??= Load();

        public event Action Changed;

        static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch (Exception ex) { Program.Log(ex); }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Program.Log(ex); }
            Changed?.Invoke();
        }

        [System.Text.Json.Serialization.JsonIgnore] public double ItemSize => DockSize == "small" ? 36 : DockSize == "large" ? 52 : 44;
        [System.Text.Json.Serialization.JsonIgnore] public double IconSize => DockSize == "small" ? 22 : DockSize == "large" ? 34 : 28;

        /// <summary>Tema/boyut gibi tum arayuzu etkileyen degisiklikler icin uygulamayi yeniden baslatir.</summary>
        public static void Restart()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--restart") { UseShellExecute = false });
                Application.Current.Shutdown();
            }
            catch (Exception ex) { Program.Log(ex); }
        }
    }
}
