using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace FloatingDock
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
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
