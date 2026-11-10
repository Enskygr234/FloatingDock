using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace FloatingDock
{
    static class Program
    {
        /// <summary>--demo: ekran goruntuleri icin ornek veriler (gercek Wi-Fi adlari / dosyalar gorunmesin).</summary>
        internal static bool Demo;

        [STAThread]
        static void Main(string[] args)
        {
            Demo = Array.IndexOf(args, "--demo") >= 0;
            if (Demo) args = Array.FindAll(args, a => a != "--demo");
            // Gelistirici testi: FloatingDock.exe --panel wifi|takvim [saniye]  -> paneli dock olmadan ac
            if (args.Length >= 2 && args[0] == "--panel")
            {
                var a = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Theme.InstallResources(a);
                Flyout f = args[1] == "takvim" ? new CalendarFlyout() : args[1] == "baslat" ? new StartMenu() : args[1] == "ayarlar" ? new SettingsFlyout() : new ControlCenter();
                if (f is ControlCenter cc && args.Length > 3) cc.TestExpand = args[3];
                int secs = args.Length > 2 && int.TryParse(args[2], out var s0) ? s0 : 8;
                a.Startup += (s, e) =>
                {
                    // demo: arkaya duvar kagidi koy ki ekran goruntusunde masaustu gorunsun
                    var wp = @"C:\Users\Public\Pictures\modern-wallpaper.png";
                    if (Demo && File.Exists(wp))
                    {
                        var bd = new Window
                        {
                            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false,
                            Left = 0, Top = 0, Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight, ShowActivated = false,
                            Content = new System.Windows.Controls.Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(wp)), Stretch = System.Windows.Media.Stretch.UniformToFill }
                        };
                        try { bd.Show(); } catch (Exception ex) { Log(ex); }
                    }
                    double sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
                    double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(new System.Windows.Controls.Border()).DpiScaleX;
                    f.ShowAt(f is StartMenu ? sw / 2 : sw * 0.72, sh / dpi - 64);
                    if (f is StartMenu sm && args.Length > 3) { var q = args[3]; var qt = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; qt.Tick += (s3, e3) => { qt.Stop(); sm.TestQuery(q); }; qt.Start(); }
                    var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(secs) };
                    t.Tick += (s2, e2) => a.Shutdown();
                    t.Start();
                };
                a.DispatcherUnhandledException += (s9, e9) => { Log(e9.Exception); };
                a.Run();
                return;
            }

            bool restart = Array.IndexOf(args, "--restart") >= 0;
            using var mutex = new Mutex(true, "FloatingDock_SingleInstance", out bool created);
            if (!created)
            {
                // ayar degisikligi sonrasi yeniden baslatma: eski surecin kapanmasini bekle
                if (!restart) return;
                try { if (!mutex.WaitOne(TimeSpan.FromSeconds(10))) return; }
                catch (AbandonedMutexException) { }
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Theme.InstallResources(app);
            // "Sistem temasini izle" seciliyse Windows acik/koyu ya da vurgu rengi degisince yeniden baslat
            bool startDark = Theme.Dark; var startAccent = Theme.Accent;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category != Microsoft.Win32.UserPreferenceCategory.General && e.Category != Microsoft.Win32.UserPreferenceCategory.Color) return;
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Theme.Init();
                    if (Theme.Dark != startDark || Theme.Accent != startAccent) Settings.Restart();
                }));
            };
            // monitor takildi / cikarildi ya da cozunurluk degisti: dock'lari yeniden kur
            // (yalniz monitorlerin yerlesimi gercekten degistiyse; bazi surucular bu olayi bosuna da tetikler)
            string layout = MonitorLayout();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => app.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MonitorLayout() != layout) Settings.Restart();
            }), System.Windows.Threading.DispatcherPriority.Background);
            if (Settings.Current.HideWindowsBanners) Banners.Apply();
            Updater.StartAutomatic(app.Dispatcher);
            var dock = new DockWindow();
            var island = new IslandWindow(dock.GetIcon);
            dock.Loaded += (s, e) => island.Show();
            dock.Closed += (s, e) => island.Close();
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log(e.Exception);
                dock.RestoreTaskbar();
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log(e.ExceptionObject as Exception); dock.RestoreTaskbar(); };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => dock.RestoreTaskbar();
            app.Run(dock);
        }

        /// <summary>Monitorlerin konum ve boyutlari (degisti mi diye karsilastirmak icin).</summary>
        static string MonitorLayout()
        {
            var sb = new System.Text.StringBuilder();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref Native.RECT r, IntPtr d) =>
            {
                sb.Append(r.left).Append(',').Append(r.top).Append(',').Append(r.right).Append(',').Append(r.bottom).Append(';');
                return true;
            }, IntPtr.Zero);
            return sb.ToString();
        }

        internal static void Log(Exception ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatingDock");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now + "  " + ex + Environment.NewLine);
            }
            catch { }
        }
    }
}
