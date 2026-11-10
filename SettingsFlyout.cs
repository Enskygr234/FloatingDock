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

        readonly Toggle autoStart = new Toggle(L.T("Windows açılışında başlat", "Start with Windows")), winKey = new Toggle(L.T("Windows tuşu", "Windows key"));
        readonly TextBlock restartNote = Text(L.T("Bu değişiklik için FloatingDock yeniden başlatılıyor…", "Restarting FloatingDock to apply this…"), Theme.Caption, Theme.TextTertiary);

        public SettingsFlyout() : base("FloatingDockSettings")
        {
            Width = 380;
            var s = Settings.Current;
            var root = new StackPanel();

            var head = new Grid { Margin = new Thickness(2, 0, 0, 6) };
            head.Children.Add(Text(L.T("Ayarlar", "Settings"), Theme.Subtitle, Theme.TextPrimary, FontWeights.SemiBold));
            var close = IconButton("", L.T("Kapat", "Close"), HideFlyout, 32, 12);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(close);
            root.Children.Add(head);

            // ---- gorunum ----
            root.Children.Add(Group(L.T("Görünüm", "Appearance")));
            root.Children.Add(Row(L.T("Tema", "Theme"), null, Segmented(new[] { L.T("Sistem", "System"), L.T("Koyu", "Dark"), L.T("Açık", "Light") }, Array.IndexOf(new[] { "system", "dark", "light" }, s.Theme),
                i => { s.Theme = new[] { "system", "dark", "light" }[i]; ApplyAndRestart(); })));
            root.Children.Add(Row(L.T("Vurgu rengi", "Accent color"), null, Segmented(new[] { L.T("Buz mavisi", "Frost blue"), "Windows" }, s.Accent == "windows" ? 1 : 0,
                i => { s.Accent = i == 1 ? "windows" : "frost"; ApplyAndRestart(); })));
            var langs = new[] { "system", "tr", "en" };
            root.Children.Add(Row(L.T("Dil", "Language"), null, Segmented(new[] { L.T("Sistem", "System"), "Türkçe", "English" }, Math.Max(0, Array.IndexOf(langs, s.Language)),
                i => { s.Language = langs[i]; ApplyAndRestart(); })));
            if (Glass.IsWin11)
            {
                var keys = new[] { "acrylic", "mica", "micaalt" };
                root.Children.Add(Row(L.T("Panel malzemesi", "Panel material"), L.T("Mica, duvar kâğıdının rengini alır", "Mica takes on your wallpaper's color"), Segmented(new[] { L.T("Akrilik", "Acrylic"), "Mica", "Mica Alt" }, Math.Max(0, Array.IndexOf(keys, s.Backdrop)),
                    i => { s.Backdrop = keys[i]; ApplyAndRestart(); })));
            }

            // ---- dock ----
            root.Children.Add(Group("Dock"));
            root.Children.Add(Row(L.T("Boyut", "Size"), null, Segmented(new[] { L.T("Küçük", "Small"), L.T("Orta", "Medium"), L.T("Büyük", "Large") }, Array.IndexOf(new[] { "small", "medium", "large" }, s.DockSize),
                i => { s.DockSize = new[] { "small", "medium", "large" }[i]; ApplyAndRestart(); })));
            root.Children.Add(Row(L.T("İkonları büyüt", "Magnify icons"), L.T("Fare üzerindeyken ikonlar büyür", "Icons grow under the mouse"), Live(new Toggle(L.T("İkonları büyüt", "Magnify icons")) { IsOn = s.Magnify }, v => s.Magnify = v)));
            root.Children.Add(Row(L.T("Otomatik gizle", "Auto-hide"), L.T("Bir pencere dock'a değince aşağı kayar", "Slides down when a window touches it"), Live(new Toggle(L.T("Otomatik gizle", "Auto-hide")) { IsOn = s.AutoHide }, v => s.AutoHide = v)));
            root.Children.Add(Row(L.T("Görev görünümü düğmesi", "Task view button"), L.T("Açık pencereler ve masaüstleri (Win+Tab)", "Open windows and desktops (Win+Tab)"), Restarting(new Toggle(L.T("Görev görünümü düğmesi", "Task view button")) { IsOn = s.TaskViewButton }, v => s.TaskViewButton = v)));
            if (Glass.IsWin11)
                root.Children.Add(Row(L.T("Widget'lar düğmesi", "Widgets button"), L.T("Hava durumu, haberler, takvim (Win+W)", "Weather, news, calendar (Win+W)"), Restarting(new Toggle(L.T("Widget'lar düğmesi", "Widgets button")) { IsOn = s.WidgetsButton }, v => s.WidgetsButton = v)));
            root.Children.Add(Row(L.T("Arka plan uygulamaları", "Background apps"), L.T("Sistem tepsisi simgeleri (˄ düğmesi)", "System tray icons (˄ button)"),
                Restarting(new Toggle(L.T("Arka plan uygulamaları", "Background apps")) { IsOn = s.TrayIcons }, v => s.TrayIcons = v)));
            root.Children.Add(Row(L.T("Tüm ekranlarda", "On all displays"), L.T("Diğer monitörlerde de dock olur", "Show a dock on every monitor"),
                Restarting(new Toggle(L.T("Tüm ekranlarda", "On all displays")) { IsOn = s.AllMonitors }, v => s.AllMonitors = v)));

            // ---- bildirimler ----
            root.Children.Add(Group(L.T("Bildirimler", "Notifications")));
            root.Children.Add(Row(L.T("Rahatsız etme", "Do not disturb"), L.T("Bildirimler adada gösterilmez, takvim panelinde birikir", "Notifications skip the island and collect in the calendar panel"), Live(new Toggle(L.T("Rahatsız etme", "Do not disturb")) { IsOn = s.DoNotDisturb }, v => s.DoNotDisturb = v)));
            var banners = new Toggle(L.T("Windows bildirim balonlarını gizle", "Hide Windows notification banners")) { IsOn = s.HideWindowsBanners };
            banners.Changed += v => { s.HideWindowsBanners = v; Banners.Apply(); };
            root.Children.Add(Row(L.T("Windows bildirim balonlarını gizle", "Hide Windows notification banners"),
                L.T("Bildirimler yalnız adada görünür, iki kez çıkmaz", "Notifications only show in the island, not twice"), banners));

            // ---- sistem ----
            root.Children.Add(Group(L.T("Sistem", "System")));
            autoStart.Changed += v => { AutoStart.Set(v); autoStart.IsOn = AutoStart.IsEnabled; };
            root.Children.Add(Row(L.T("Windows açılışında başlat", "Start with Windows"), null, autoStart));
            winKey.Changed += v => SetWinKey?.Invoke(v);
            root.Children.Add(Row(L.T("Windows tuşu bu Başlat menüsünü açsın", "Windows key opens this Start menu"), null, winKey));
            root.Children.Add(Row(L.T("Güncellemeleri denetle", "Check for updates"),
                L.T("Günde bir kez GitHub'daki son sürüme bakar · Sürüm ", "Looks for a new release on GitHub once a day · Version ") + Updater.Current,
                Live(new Toggle(L.T("Güncellemeleri denetle", "Check for updates")) { IsOn = s.CheckUpdates }, v => s.CheckUpdates = v)));

            restartNote.Visibility = Visibility.Collapsed;
            restartNote.Margin = new Thickness(2, 10, 0, 0);
            root.Children.Add(restartNote);

            root.Children.Add(Divider(12, 6));
            var foot = new Grid { Margin = new Thickness(-6, 0, -6, -6) };
            foot.Children.Add(LinkButton("", L.T("Windows kişiselleştirme ayarları", "Windows personalization settings"), () => { Shell("ms-settings:personalization"); HideFlyout(); }));
            var quit = Clickable(new TextBlock { Text = L.T("Dock'u kapat", "Close dock"), FontFamily = Theme.Ui, FontSize = Theme.Body, Foreground = Theme.Brush(Theme.Danger), Margin = new Thickness(12, 7, 12, 7) },
                () => { HideFlyout(); CloseDock?.Invoke(); });
            quit.HorizontalAlignment = HorizontalAlignment.Right;
            quit.ToolTip = L.T("Dock kapanır, Windows görev çubuğu geri gelir", "The dock closes and the Windows taskbar comes back");
            foot.Children.Add(quit);
            root.Children.Add(foot);

            // kucuk ekranlarda (orn. 1366x768) panel ekrana sigmayabilir: kaydirilabilir olsun
            SetBody(new ScrollViewer
            {
                Content = root, MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 120),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.VerticalOnly, Margin = new Thickness(0, 0, -8, 0), Padding = new Thickness(0, 0, 8, 0)
            });
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
