using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>Her ekrana bir dock: birincil ekrandaki ana dock uygulamanin kendisidir (paneller, Windows tusu, gorev cubugu);
    /// diger ekranlardakiler ayni panelleri paylasir. Ekran takilip cikarilinca ya da cozunurluk degisince yeniden dizilir.</summary>
    static class DockManager
    {
        static readonly List<DockWindow> docks = new List<DockWindow>();
        static DispatcherTimer syncLater;

        public static DockWindow Main { get; private set; }
        public static IReadOnlyList<DockWindow> All => docks;
        public static bool Multi => docks.Count > 1;

        public static DockWindow Start()
        {
            Main = new DockWindow(Screens.Primary, true);
            docks.Add(Main);
            Main.Loaded += (s, e) => Sync();
            Main.Closed += (s, e) => { foreach (var d in docks.Where(d => d != Main).ToList()) d.Close(); docks.Clear(); };
            // ekran degisiklikleri ardi ardina gelir: kisa bir sure bekleyip bir kez diz
            syncLater = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            syncLater.Tick += (s, e) => { syncLater.Stop(); Sync(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Main.Dispatcher.BeginInvoke(new Action(() => { syncLater.Stop(); syncLater.Start(); }));
            Settings.Current.Changed += Sync;
            return Main;
        }

        /// <summary>Dock'lari mevcut ekranlara gore olustur / tasi / kapat.</summary>
        public static void Sync()
        {
            if (Main == null || !Main.IsLoaded) return;
            var all = Screens.All();
            Main.SetScreen(all[0]);
            var wanted = Settings.Current.DockOnAllScreens ? all.Skip(1).ToList() : new List<Screen>();
            foreach (var d in docks.Where(d => d != Main).ToList())
            {
                var s = wanted.FirstOrDefault(w => Screens.Same(w, d.Screen));
                if (s == null) { docks.Remove(d); d.Close(); }
                else d.SetScreen(s);
            }
            foreach (var s in wanted)
            {
                if (docks.Any(d => Screens.Same(d.Screen, s))) continue;
                var d = new DockWindow(s, false);
                docks.Add(d);
                d.Show();
            }
            Main.SyncSecondaryTaskbars(Settings.Current.DockOnAllScreens);
            foreach (var d in docks) d.Invalidate();
        }

        /// <summary>Farenin bulundugu ekrandaki dock (yoksa ana dock).</summary>
        public static DockWindow AtCursor()
        {
            var s = Screens.FromCursor();
            return docks.FirstOrDefault(d => Screens.Same(d.Screen, s)) ?? Main;
        }

        /// <summary>Sabitlenenler degisti: tum dock'lari yenile.</summary>
        public static void ReloadPinned()
        {
            foreach (var d in docks.ToList()) d.ReloadPinned();
        }
    }
}
