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
        public string Accent { get; set; } = "auto";
        /// <summary>Renk paleti: "mint" (Nane & deniz) | "lavender" (Lavanta) | "sand" (Sicak kum).</summary>
        public string Palette { get; set; } = "mint";
        /// <summary>Dock uygulama kutulari: "pastel" (ikonun renginden) | "white".</summary>
        public string DockTiles { get; set; } = "pastel";
        /// <summary>Ust kisim: "islands" (iki kucuk ada) | "bar" (tek tam cubuk).</summary>
        public string TopBarStyle { get; set; } = "islands";
        public bool TopBarAppName { get; set; } = true;
        public bool TopBarDesktops { get; set; } = true;
        public bool TopBarWeather { get; set; } = true;
        public bool TopBarDate { get; set; } = true;
        public bool TopBarSeconds { get; set; }
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
        /// <summary>Masaustunde hava / muzik / bugun kartlari (kil kabuk).</summary>
        public bool DesktopWidgets { get; set; } = true;
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

        // ---------- v1.6: Winter Kisisellestirme Merkezi ----------
        /// <summary>Ekran basina duvar kagidi: anahtar = Windows monitor yolu (IDesktopWallpaper), deger = duvar kagidi kimligi ("winter-night" | "custom:dosya.jpg").</summary>
        public Dictionary<string, string> WallpaperPerMonitor { get; set; } = new Dictionary<string, string>();
        /// <summary>"fill" (doldur) | "fit" (sigdir) | "center" (ortala).</summary>
        public string WallpaperFit { get; set; } = "fill";
        /// <summary>Kullanicinin ekledigi gorseller (Wallpapers\custom klasorundeki dosya adlari).</summary>
        public List<string> WallpaperCustom { get; set; } = new List<string>();
        /// <summary>Ilk degisiklikten onceki Windows duvar kagitlari (monitor yolu -> dosya); geri donmek icin.</summary>
        public Dictionary<string, string> WallpaperBackup { get; set; } = new Dictionary<string, string>();
        public int WallpaperBackupPosition { get; set; } = -1;

        /// <summary>Baslat dugmesindeki logo: "winter" | "classic".</summary>
        public string StartLogo { get; set; } = "winter";
        /// <summary>Baslat menusu genisligi / yuksekligi (ekranin yuzdesi; 100 = tam ekran).</summary>
        public int StartWidth { get; set; } = 100;
        public int StartHeight { get; set; } = 100;
        /// <summary>Zemin opakligi (yuzde, 40-100).</summary>
        public int StartOpacity { get; set; } = 78;
        /// <summary>Kose yuvarlakligi (piksel, panel modunda).</summary>
        public int StartRadius { get; set; } = 32;
        /// <summary>Arkadaki masaustunu bulaniklastir.</summary>
        public bool StartBlur { get; set; } = true;
        /// <summary>Zemin tonu: "theme" | "accent" | "winter" | "midnight".</summary>
        public string StartTint { get; set; } = "theme";
        /// <summary>Acilis animasyonu: "zoom" | "slide" | "fade" | "none".</summary>
        public string StartAnimation { get; set; } = "zoom";
        /// <summary>Panel modunda konum: "center" | "left" | "bottom".</summary>
        public string StartPosition { get; set; } = "center";
        /// <summary>Soldaki kisayol seridi (Ana sayfa, Uygulamalar, Oyunlar, Belgeler...).</summary>
        public bool StartSidebar { get; set; } = true;
        public bool StartShowClock { get; set; } = true;
        public bool StartRecentApps { get; set; } = true;
        public bool StartSuggestedFiles { get; set; } = true;
        /// <summary>Son acilan uygulamalar (Baslat'tan; en yeni basta).</summary>
        public List<string> StartRecentList { get; set; } = new List<string>();
        /// <summary>Kullanicinin kategorileri: ad -> uygulama kimlikleri.</summary>
        public Dictionary<string, List<string>> StartCategories { get; set; } = new Dictionary<string, List<string>>();
        /// <summary>Oyunlar sayfasina elle eklenen / cikarilan uygulamalar.</summary>
        public List<string> StartGames { get; set; } = new List<string>();
        public List<string> StartNotGames { get; set; } = new List<string>();
        /// <summary>Arama: klavyeden yazinca aramaya basla, ayarlar / dosyalar / web sonuclari.</summary>
        public bool SearchTypeToStart { get; set; } = true;
        public bool SearchSettings { get; set; } = true;
        public bool SearchFiles { get; set; } = true;
        public bool SearchWeb { get; set; } = true;
        /// <summary>Guc menusu: "lock","sleep","hibernate","signout","restart","shutdown".</summary>
        public List<string> PowerButtons { get; set; } = new List<string> { "lock", "sleep", "restart", "shutdown" };
        public bool PowerConfirm { get; set; } = true;

        /// <summary>Animasyonlar acik mi (Windows'ta kapaliysa yine kapali) ve hiz carpani (0.5 hizli .. 1.6 yavas).</summary>
        public bool Animations { get; set; } = true;
        public double AnimSpeed { get; set; } = 1.0;
        /// <summary>Dock ikonlari uzerine gelince ne kadar buyusun (0.1 .. 0.6).</summary>
        public double MagnifyAmount { get; set; } = 0.32;

        /// <summary>Telefon Baglantisi (Phone Link) bildirimleri adada gosterilsin.</summary>
        public bool PhoneIsland { get; set; } = true;

        /// <summary>Masaustu bilesenleri: hangi kartlar ve hangi tarafta.</summary>
        public bool WidgetWeather { get; set; } = true;
        public bool WidgetMusic { get; set; } = true;
        public bool WidgetToday { get; set; } = true;
        public string WidgetSide { get; set; } = "right";

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
