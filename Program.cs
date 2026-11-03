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
                Flyout f = args[1] == "takvim" ? new CalendarFlyout() : args[1] == "baslat" ? new StartMenu() : new ControlCenter();
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

            using var mutex = new Mutex(true, "FloatingDock_SingleInstance", out bool created);
            if (!created) return;

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
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
