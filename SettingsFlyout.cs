using System;
using System.Linq;
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
            var palKeys = Theme.Palettes.Select(p => p.key).ToArray();
            root.Children.Add(Block("Renk paleti", "Zemin, kartlar ve uygulama kutularının tonları",
                Segmented(Theme.Palettes.Select(p => p.name).ToArray(), Math.Max(0, Array.IndexOf(palKeys, s.Palette)),
                    i => { s.Palette = palKeys[i]; ApplyAndRestart(); })));
            root.Children.Add(Block("Vurgu rengi", "Başlat düğmesi, seçili öğeler, kaydırıcılar", AccentSwatches(s)));

            // ---- dock ----
            root.Children.Add(Group("Dock"));
            root.Children.Add(Row("Boyut", null, Segmented(new[] { "Küçük", "Orta", "Büyük" }, Array.IndexOf(new[] { "small", "medium", "large" }, s.DockSize),
                i => { s.DockSize = new[] { "small", "medium", "large" }[i]; ApplyAndRestart(); })));
            root.Children.Add(Row("Kutu rengi", "Pastel: ikonun kendi renginden", Segmented(new[] { "Pastel", "Beyaz" }, s.DockTiles == "white" ? 1 : 0,
                i => { s.DockTiles = i == 1 ? "white" : "pastel"; ApplyAndRestart(); })));
            root.Children.Add(Row("İkonları büyüt", "Fare üzerindeyken ikonlar büyür", Live(new Toggle("İkonları büyüt") { IsOn = s.Magnify }, v => s.Magnify = v)));
            root.Children.Add(Row("Masaüstü bileşenleri", "Sağda hava, müzik ve bugün kartları", Live(new Toggle("Masaüstü bileşenleri") { IsOn = s.DesktopWidgets }, v => s.DesktopWidgets = v)));
            root.Children.Add(Row("Okunmamış rozetleri", "Mesaj uygulamalarının ikonunda kırmızı sayı", Live(new Toggle("Okunmamış rozetleri") { IsOn = s.Badges }, v => { s.Badges = v; if (!v) FloatingDock.Badges.Clear(); })));
            root.Children.Add(Row("Otomatik gizle", "Bir pencere dock'a değince aşağı kayar", Live(new Toggle("Otomatik gizle") { IsOn = s.AutoHide }, v => s.AutoHide = v)));
            root.Children.Add(Row("Sistem tepsisi", "Arka planda çalışan uygulamaların ikonları (⌃ düğmesi)", Restarting(new Toggle("Sistem tepsisi") { IsOn = s.TrayButton }, v => s.TrayButton = v)));
            if (Screens.All().Count > 1)
            {
                root.Children.Add(Row("Tüm ekranlarda göster", "Her monitörün altında ayrı bir dock", Live(new Toggle("Tüm ekranlarda göster") { IsOn = s.DockOnAllScreens }, v => s.DockOnAllScreens = v)));
                root.Children.Add(Row("Yalnız o ekrandaki pencereler", "Her dock kendi ekranında açık uygulamaları gösterir", Live(new Toggle("Yalnız o ekrandaki pencereler") { IsOn = s.DockAppsPerScreen }, v => s.DockAppsPerScreen = v)));
            }

            // ---- ust kisim ----
            root.Children.Add(Group("Üst kısım"));
            root.Children.Add(Row("Biçim", null, Segmented(new[] { "İki ada", "Tam çubuk" }, s.TopBarStyle == "bar" ? 1 : 0,
                i => { s.TopBarStyle = i == 1 ? "bar" : "islands"; ApplyAndRestart(); })));
            root.Children.Add(Row("Uygulama adı", "Öndeki uygulama ve pencere sayısı", Live(new Toggle("Uygulama adı") { IsOn = s.TopBarAppName }, v => s.TopBarAppName = v)));
            root.Children.Add(Row("Sanal masaüstleri", "1, 2, 3 ve yeni masaüstü düğmesi", Live(new Toggle("Sanal masaüstleri") { IsOn = s.TopBarDesktops }, v => s.TopBarDesktops = v)));
            root.Children.Add(Row("Hava durumu", "Saatin yanında sıcaklık", Live(new Toggle("Hava durumu") { IsOn = s.TopBarWeather }, v => s.TopBarWeather = v)));
            root.Children.Add(Row("Tarih", "Saatin yanında gün ve ay", Live(new Toggle("Tarih") { IsOn = s.TopBarDate }, v => s.TopBarDate = v)));
            root.Children.Add(Row("Saniye", "Saati saniyeli göster", Live(new Toggle("Saniye") { IsOn = s.TopBarSeconds }, v => s.TopBarSeconds = v)));

            // ---- bildirimler ----
            root.Children.Add(Group("Bildirimler"));
            root.Children.Add(Row("Rahatsız etme", "Bildirimler adada gösterilmez, takvim panelinde birikir", Live(new Toggle("Rahatsız etme") { IsOn = s.DoNotDisturb }, v => s.DoNotDisturb = v)));
            var banners = new Toggle("Windows bildirim balonlarını gizle") { IsOn = s.HideWindowsBanners };
            banners.Changed += v =>
            {
                if (v) { s.HideWindowsBanners = true; s.Save(); Banners.Apply(); }
                else Banners.Restore(false);
            };
            root.Children.Add(Row("Windows bildirim balonlarını gizle", "Bildirimler yalnız adada görünür. Dock kapanınca Windows'un balonları geri gelir", banners));

            // ---- ada ----
            root.Children.Add(Group("Ada"));
            root.Children.Add(Row("Ses ve parlaklık göstergesi", "Değiştirince ekranın üstünde kısa bir çubuk", Live(new Toggle("Ses ve parlaklık göstergesi") { IsOn = s.IslandOsd }, v => s.IslandOsd = v)));
            if (Battery.HasBattery)
                root.Children.Add(Row("Şarj ve pil uyarısı", "Şarja takınca animasyon, %20 ve %10'da uyarı", Live(new Toggle("Şarj ve pil uyarısı") { IsOn = s.IslandBattery }, v => s.IslandBattery = v)));

            // ---- hava durumu ----
            root.Children.Add(Group("Hava durumu"));
            root.Children.Add(BuildCityRow());
            root.Children.Add(Row("Yağmur uyarısı", "Yağış yaklaşınca adada haber verir", Live(new Toggle("Yağmur uyarısı") { IsOn = s.WeatherRainAlert }, v => s.WeatherRainAlert = v)));

            // ---- sistem ----
            root.Children.Add(Group("Sistem"));
            autoStart.Changed += v => { AutoStart.Set(v); autoStart.IsOn = AutoStart.IsEnabled; };
            root.Children.Add(Row("Windows açılışında başlat", null, autoStart));
            winKey.Changed += v => SetWinKey?.Invoke(v);
            root.Children.Add(Row("Windows tuşu bu Başlat menüsünü açsın", null, winKey));

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

            SetBody(root, scroll: true);
        }

        readonly TextBox city = new TextBox();
        readonly TextBlock cityStatus = Text("", Theme.Caption, Theme.TextTertiary);

        /// <summary>Sehir kutusu + Kaydet. Bos birakip kaydetmek hava durumunu kapatir.</summary>
        UIElement BuildCityRow()
        {
            var sp = new StackPanel { Margin = new Thickness(2, 4, 0, 4) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            city.FontSize = Theme.Body; city.FontFamily = UiFont; city.Foreground = Theme.Fg(); city.CaretBrush = Theme.Fg();
            city.Background = Brushes.Transparent; city.BorderThickness = new Thickness(0); city.Padding = new Thickness(2, 6, 2, 6);
            city.SelectionBrush = Theme.Brush(Accent);
            city.Text = Settings.Current.WeatherCity;
            System.Windows.Automation.AutomationProperties.SetName(city, "Şehir");
            var hint = Text("Şehir, örn. İstanbul", Theme.Body, Theme.TextTertiary);
            hint.IsHitTestVisible = false; hint.Margin = new Thickness(4, 0, 0, 0);
            void Hint() => hint.Visibility = city.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            city.TextChanged += (s, e) => Hint();
            Hint();
            city.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) _ = SaveCity(); };
            var box = new Grid();
            box.Children.Add(city); box.Children.Add(hint);
            var field = new Border
            {
                CornerRadius = new CornerRadius(Theme.RadiusSm), Background = Theme.Brush(Theme.Field),
                BorderBrush = Theme.Ink(Theme.StrokeDefault), BorderThickness = new Thickness(1), Padding = new Thickness(8, 1, 8, 0), Child = box
            };
            city.GotKeyboardFocus += (s, e) => field.BorderBrush = Theme.Brush(Accent);
            city.LostKeyboardFocus += (s, e) => field.BorderBrush = Theme.Ink(Theme.StrokeDefault);
            g.Children.Add(field);
            var save = SecondaryButton("Kaydet", () => _ = SaveCity());
            save.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(save, 1);
            g.Children.Add(save);
            sp.Children.Add(g);
            var auto = LinkButton("", "Konumumu kullan", () => _ = UseAuto());
            auto.Margin = new Thickness(-8, 6, 0, 0);
            auto.ToolTip = "Windows konum servisiyle bulunduğun yerin hava durumu";
            sp.Children.Add(auto);
            cityStatus.TextWrapping = TextWrapping.Wrap;
            cityStatus.Margin = new Thickness(2, 6, 0, 0);
            sp.Children.Add(cityStatus);
            ShowCityStatus();
            return sp;
        }

        void ShowCityStatus()
        {
            var s = Settings.Current;
            cityStatus.Text = s.WeatherAuto
                ? "Konumum: " + (string.IsNullOrEmpty(s.WeatherPlace) ? "aranıyor…" : s.WeatherPlace) + " (Windows konum servisi; yer adı için koordinat bigdatacloud.net'e sorulur) · şehir yazıp kaydedersen ona geçer"
                : string.IsNullOrWhiteSpace(s.WeatherCity)
                ? "Kapalı. Şehir girersen ya da konumunu kullanırsan dock'ta hava durumu görünür (open-meteo.com; yalnız şehir adı gönderilir)."
                : "Konum: " + (string.IsNullOrEmpty(s.WeatherPlace) ? s.WeatherCity : s.WeatherPlace) + " · kapatmak için kutuyu boşaltıp kaydet";
        }

        async System.Threading.Tasks.Task UseAuto()
        {
            cityStatus.Text = "Konum alınıyor…";
            var err = await Weather.UseAutoAsync();
            if (err != null) cityStatus.Text = err;
            else { city.Text = ""; ShowCityStatus(); }
        }

        async System.Threading.Tasks.Task SaveCity()
        {
            cityStatus.Text = "Aranıyor…";
            var name = await Weather.SetCityAsync(city.Text);
            if (name == null) cityStatus.Text = "“" + city.Text.Trim() + "” bulunamadı (ya da internet yok). Başka bir yazımla dene.";
            else ShowCityStatus();
        }

        protected override void OnOpening()
        {
            autoStart.IsOn = AutoStart.IsEnabled;
            winKey.IsOn = WinKeyHook.Enabled;
            restartNote.Visibility = Visibility.Collapsed;
            city.Text = Settings.Current.WeatherAuto ? "" : Settings.Current.WeatherCity;
            ShowCityStatus();
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

        /// <summary>Basligi ustte, denetimi altinda tam genislikte satir (renk secicileri icin).</summary>
        static UIElement Block(string title, string desc, UIElement control)
        {
            var sp = new StackPanel { Margin = new Thickness(2, 6, 0, 8) };
            var t = Text(title, Theme.Body, Theme.TextPrimary);
            sp.Children.Add(t);
            if (desc != null)
            {
                var d = Text(desc, Theme.Caption, Theme.TextTertiary);
                d.TextWrapping = TextWrapping.Wrap;
                d.Margin = new Thickness(0, 1, 0, 0);
                sp.Children.Add(d);
            }
            if (control is FrameworkElement fe) { fe.Margin = new Thickness(0, 8, 0, 0); fe.HorizontalAlignment = HorizontalAlignment.Left; }
            sp.Children.Add(control);
            return sp;
        }

        /// <summary>Vurgu rengi: renkli yuvarlaklar (paletin rengi, hazir renkler, Windows); secili olanin cevresinde halka.</summary>
        UIElement AccentSwatches(Settings s)
        {
            var wp = new WrapPanel();
            string cur = s.Accent == "frost" || string.IsNullOrEmpty(s.Accent) ? "auto" : s.Accent;
            void Add(string key, Brush fill, string name)
            {
                bool on = cur == key;
                var g = new Grid { Width = 30, Height = 30 };
                g.Children.Add(new System.Windows.Shapes.Ellipse { Fill = fill });
                if (on) g.Children.Add(new TextBlock { Text = "\uE73E", FontFamily = Theme.Icons, FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                var ring = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(2.5), BorderBrush = on ? Theme.Fg() : Brushes.Transparent, Child = g };
                var b = Clickable(ring, () => { if (s.Accent == key) return; s.Accent = key; ApplyAndRestart(); }, 20);
                b.Margin = new Thickness(0, 0, 4, 0);
                b.ToolTip = name;
                System.Windows.Automation.AutomationProperties.SetName(b, name + (on ? ", seçili" : ""));
                wp.Children.Add(b);
            }
            Add("auto", Theme.Brush(Theme.AccentColor("auto", s.Palette, Theme.Dark)), "Paletin rengi");
            foreach (var a in Theme.Accents) Add(a.key, Theme.Brush(Theme.Dark ? a.dark : a.light), a.name);
            Add("windows", new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0x00, 0x78, 0xD4), 0), new GradientStop(Color.FromRgb(0x88, 0x17, 0x98), 0.5), new GradientStop(Color.FromRgb(0xE7, 0x48, 0x56), 1)
            }, 45), "Windows vurgu rengi");
            return wp;
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
