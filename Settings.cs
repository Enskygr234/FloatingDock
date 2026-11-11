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
        /// <summary>Birden fazla monitorde her ekranin altinda ayri bir dock.</summary>
        public bool DockOnAllScreens { get; set; } = true;
        /// <summary>Her dock yalnizca kendi ekranindaki pencereleri gosterir (sabitlenenler her yerde durur).</summary>
        public bool DockAppsPerScreen { get; set; } = true;
        /// <summary>Dock'ta sabitlenen uygulamalarin sirasi (anahtar = exe yolu ya da kisayol).</summary>
        public List<string> DockOrder { get; set; } = new List<string>();
        /// <summary>Surukle-birak ile eklenen sabitler (kisayol / exe yolu).</summary>
        public List<string> DockExtra { get; set; } = new List<string>();
        /// <summary>Gorev cubugu sabitlerinden dock'ta gizlenenler.</summary>
        public List<string> DockHidden { get; set; } = new List<string>();

        // ---------- v1.3 ----------
        /// <summary>Dock'ta sistem tepsisi (arka plan uygulama ikonlari) dugmesi.</summary>
        public bool TrayButton { get; set; } = true;
        /// <summary>Windows'un kendi bildirim balonlarini kapat: bildirimler yalniz adada gorunur.</summary>
        public bool HideWindowsBanners { get; set; }
        /// <summary>Balonlari kapatirken degistirdigimiz uygulamalarin eski "ShowBanner" degerleri (null = deger yoktu). Geri almak icin.</summary>
        public Dictionary<string, int?> BannerBackup { get; set; } = new Dictionary<string, int?>();
        /// <summary>Ses / parlaklik degisince adada gosterge.</summary>
        public bool IslandOsd { get; set; } = true;
        /// <summary>Sarja takilinca ve pil azalinca adada uyari (yalniz dizustu).</summary>
        public bool IslandBattery { get; set; } = true;
        /// <summary>Hava durumu icin sehir (bos = kapali). Koordinatlar ilk aramada bulunup saklanir.</summary>
        public string WeatherCity { get; set; } = "";
        public string WeatherPlace { get; set; } = "";
        public double WeatherLat { get; set; }
        public double WeatherLon { get; set; }
        /// <summary>Dock ikonlarinda okunmamis rozetleri.</summary>
        public bool Badges { get; set; } = true;
        /// <summary>Konumu Windows konum servisiyle bul (sehir yazmadan).</summary>
        public bool WeatherAuto { get; set; }
        /// <summary>Yagis yaklasinca adada uyar.</summary>
        public bool WeatherRainAlert { get; set; } = true;
        /// <summary>Kayitli sehirler: "ad|enlem|boylam" (panelde aralarinda gecilir).</summary>
        public List<string> WeatherSaved { get; set; } = new List<string>();

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
            SaveQuiet();
            Changed?.Invoke();
        }

        /// <summary>Yalniz dosyaya yazar, Changed olayini tetiklemez (her is parcacigindan guvenli).</summary>
        public void SaveQuiet()
        {
            lock (FilePath)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex) { Program.Log(ex); }
            }
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
