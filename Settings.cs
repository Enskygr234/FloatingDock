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
        /// <summary>Dock'ta sabitlenen uygulamalarin sirasi (anahtar = exe yolu ya da kisayol).</summary>
        public List<string> DockOrder { get; set; } = new List<string>();
        /// <summary>Surukle-birak ile eklenen sabitler (kisayol / exe yolu).</summary>
        public List<string> DockExtra { get; set; } = new List<string>();
        /// <summary>Gorev cubugu sabitlerinden dock'ta gizlenenler.</summary>
        public List<string> DockHidden { get; set; } = new List<string>();

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

        public double ItemSize => DockSize == "small" ? 36 : DockSize == "large" ? 52 : 44;
        public double IconSize => DockSize == "small" ? 22 : DockSize == "large" ? 34 : 28;

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
