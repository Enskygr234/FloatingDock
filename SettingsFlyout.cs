using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FloatingDock
{
    /// <summary>FloatingDock ayarlari: gorunum, dock, bildirimler, sistem.</summary>
    class SettingsFlyout : Flyout
    {
        /// <summary>Dock tarafindan baglanir: Windows tusu kancasini ac/kapat, dock'u kapat.</summary>
        public Action<bool> SetWinKey;
        public Action CloseDock;

        readonly Toggle autoStart = new Toggle("Windows açılışında başlat"), winKey = new Toggle("Windows tuşu");
        readonly TextBlock restartNote = Text("Bu değişiklik için FloatingDock yeniden başlatılıyor…", Theme.Caption, Theme.TextTertiary);

        public SettingsFlyout() : base("FloatingDockSettings")
        {
            Width = 380;
            var s = Settings.Current;
            var root = new StackPanel();

            var head = new Grid { Margin = new Thickness(2, 0, 0, 6) };
            head.Children.Add(Text("Ayarlar", Theme.Subtitle, Theme.TextPrimary, FontWeights.SemiBold));
            var close = IconButton("", "Kapat", HideFlyout, 32, 12);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(close);
            root.Children.Add(head);

            // ---- gorunum ----
            root.Children.Add(Group("Görünüm"));
            root.Children.Add(Row("Tema", null, Segmented(new[] { "Sistem", "Koyu", "Açık" }, Array.IndexOf(new[] { "system", "dark", "light" }, s.Theme),
                i => { s.Theme = new[] { "system", "dark", "light" }[i]; ApplyAndRestart(); })));
            root.Children.Add(Row("Vurgu rengi", null, Segmented(new[] { "Buz mavisi", "Windows" }, s.Accent == "windows" ? 1 : 0,
                i => { s.Accent = i == 1 ? "windows" : "frost"; ApplyAndRestart(); })));
            var langs = new[] { "system", "tr", "en" };
            root.Children.Add(Row(L.T("Dil", "Language"), null, Segmented(new[] { L.T("Sistem", "System"), "Türkçe", "English" }, Math.Max(0, Array.IndexOf(langs, s.Language)),
                i => { s.Language = langs[i]; ApplyAndRestart(); })));
            if (Glass.IsWin11)
            {
                var keys = new[] { "acrylic", "mica", "micaalt" };
                root.Children.Add(Row("Panel malzemesi", "Mica, duvar kâğıdının rengini alır", Segmented(new[] { "Akrilik", "Mica", "Mica Alt" }, Math.Max(0, Array.IndexOf(keys, s.Backdrop)),
                    i => { s.Backdrop = keys[i]; ApplyAndRestart(); })));
            }

            // ---- dock ----
            root.Children.Add(Group("Dock"));
            root.Children.Add(Row("Boyut", null, Segmented(new[] { "Küçük", "Orta", "Büyük" }, Array.IndexOf(new[] { "small", "medium", "large" }, s.DockSize),
                i => { s.DockSize = new[] { "small", "medium", "large" }[i]; ApplyAndRestart(); })));
            root.Children.Add(Row("İkonları büyüt", "Fare üzerindeyken ikonlar büyür", Live(new Toggle("İkonları büyüt") { IsOn = s.Magnify }, v => s.Magnify = v)));
            root.Children.Add(Row("Otomatik gizle", "Bir pencere dock'a değince aşağı kayar", Live(new Toggle("Otomatik gizle") { IsOn = s.AutoHide }, v => s.AutoHide = v)));
            root.Children.Add(Row("Görev görünümü düğmesi", "Açık pencereler ve masaüstleri (Win+Tab)", Restarting(new Toggle("Görev görünümü düğmesi") { IsOn = s.TaskViewButton }, v => s.TaskViewButton = v)));
            if (Glass.IsWin11)
                root.Children.Add(Row("Widget'lar düğmesi", "Hava durumu, haberler, takvim (Win+W)", Restarting(new Toggle("Widget'lar düğmesi") { IsOn = s.WidgetsButton }, v => s.WidgetsButton = v)));
            root.Children.Add(Row(L.T("Arka plan uygulamaları", "Background apps"), L.T("Sistem tepsisi simgeleri (˄ düğmesi)", "System tray icons (˄ button)"),
                Restarting(new Toggle(L.T("Arka plan uygulamaları", "Background apps")) { IsOn = s.TrayIcons }, v => s.TrayIcons = v)));
            root.Children.Add(Row(L.T("Tüm ekranlarda", "On all displays"), L.T("Diğer monitörlerde de dock olur", "Show a dock on every monitor"),
                Restarting(new Toggle(L.T("Tüm ekranlarda", "On all displays")) { IsOn = s.AllMonitors }, v => s.AllMonitors = v)));

            // ---- bildirimler ----
            root.Children.Add(Group("Bildirimler"));
            root.Children.Add(Row("Rahatsız etme", "Bildirimler adada gösterilmez, takvim panelinde birikir", Live(new Toggle("Rahatsız etme") { IsOn = s.DoNotDisturb }, v => s.DoNotDisturb = v)));
            var banners = new Toggle(L.T("Windows bildirim balonlarını gizle", "Hide Windows notification banners")) { IsOn = s.HideWindowsBanners };
            banners.Changed += v => { s.HideWindowsBanners = v; Banners.Apply(); };
            root.Children.Add(Row(L.T("Windows bildirim balonlarını gizle", "Hide Windows notification banners"),
                L.T("Bildirimler yalnız adada görünür, iki kez çıkmaz", "Notifications only show in the island, not twice"), banners));

            // ---- sistem ----
            root.Children.Add(Group("Sistem"));
            autoStart.Changed += v => { AutoStart.Set(v); autoStart.IsOn = AutoStart.IsEnabled; };
            root.Children.Add(Row("Windows açılışında başlat", null, autoStart));
            winKey.Changed += v => SetWinKey?.Invoke(v);
            root.Children.Add(Row("Windows tuşu bu Başlat menüsünü açsın", null, winKey));
            root.Children.Add(Row(L.T("Güncellemeleri denetle", "Check for updates"),
                L.T("Günde bir kez GitHub'daki son sürüme bakar · Sürüm ", "Looks for a new release on GitHub once a day · Version ") + Updater.Current,
                Live(new Toggle(L.T("Güncellemeleri denetle", "Check for updates")) { IsOn = s.CheckUpdates }, v => s.CheckUpdates = v)));

            restartNote.Visibility = Visibility.Collapsed;
            restartNote.Margin = new Thickness(2, 10, 0, 0);
            root.Children.Add(restartNote);

            root.Children.Add(Divider(12, 6));
            var foot = new Grid { Margin = new Thickness(-6, 0, -6, -6) };
            foot.Children.Add(LinkButton("", "Windows kişiselleştirme ayarları", () => { Shell("ms-settings:personalization"); HideFlyout(); }));
            var quit = Clickable(new TextBlock { Text = "Dock'u kapat", FontFamily = Theme.Ui, FontSize = Theme.Body, Foreground = Theme.Brush(Theme.Danger), Margin = new Thickness(12, 7, 12, 7) },
                () => { HideFlyout(); CloseDock?.Invoke(); });
            quit.HorizontalAlignment = HorizontalAlignment.Right;
            quit.ToolTip = "Dock kapanır, Windows görev çubuğu geri gelir";
            foot.Children.Add(quit);
            root.Children.Add(foot);

            SetBody(root);
        }

        protected override void OnOpening()
        {
            autoStart.IsOn = AutoStart.IsEnabled;
            winKey.IsOn = WinKeyHook.Enabled;
            restartNote.Visibility = Visibility.Collapsed;
        }

        void ApplyAndRestart()
        {
            Settings.Current.Save();
            restartNote.Visibility = Visibility.Visible;
            // notu kisa bir an goster, sonra yeniden baslat
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            t.Tick += (s, e) => { t.Stop(); Settings.Restart(); };
            t.Start();
        }

        /// <summary>Dock'un yeniden kurulmasini gerektiren anahtar.</summary>
        Toggle Restarting(Toggle t, Action<bool> set)
        {
            t.Changed += v => { set(v); ApplyAndRestart(); };
            return t;
        }

        static Toggle Live(Toggle t, Action<bool> set)
        {
            t.Changed += v => { set(v); Settings.Current.Save(); };
            return t;
        }

        static UIElement Group(string title)
        {
            var t = Text(title, Theme.Caption, Theme.TextTertiary, FontWeights.SemiBold);
            t.Margin = new Thickness(2, 14, 0, 6);
            return t;
        }

        static UIElement Row(string title, string desc, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(2, 5, 0, 5), MinHeight = 32 };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var t = Text(title, Theme.Body, Theme.TextPrimary);
            t.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(t);
            if (desc != null)
            {
                var d = Text(desc, Theme.Caption, Theme.TextTertiary);
                d.TextWrapping = TextWrapping.Wrap;
                d.Margin = new Thickness(0, 1, 0, 0);
                sp.Children.Add(d);
            }
            g.Children.Add(sp);
            if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        static void Shell(string uri)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        }
    }
}
