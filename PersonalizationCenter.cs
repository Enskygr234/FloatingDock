using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>
    /// WINTER PERSONALIZATION CENTER: dock, Baslat menusu, duvar kagitlari, renk ve temalar, animasyonlar, telefon baglantisi
    /// ve masaustu bilesenleri tek pencerede. Solda kategoriler, ortada ayarlar, sagda canli onizleme.
    /// Ayarlar settings.json'a yazilir (yeniden baslatinca korunur); tema gibi tum arayuzu etkileyenler "Uygula" ile yeniden baslatir.
    /// </summary>
    class PersonalizationCenter : Window
    {
        public static readonly (string key, string glyph, string name, string desc)[] Categories =
        {
            ("dock", "", "Dock Ayarları", "Boyut, kutular, büyütme, gizleme ve ekranlar"),
            ("baslat", "", "Başlat Menüsü", "Görünüm, uygulamalar ve davranış"),
            ("duvar", "", "Duvar Kâğıtları", "Beş Winter duvar kâğıdı, kendi görsellerin ve her ekrana ayrı seçim"),
            ("renk", "", "Renk ve Temalar", "Açık / koyu tema, renk paleti, vurgu rengi ve hazır Winter temaları"),
            ("animasyon", "", "Animasyonlar", "Hareketler, hız ve açılış efektleri"),
            ("telefon", "", "Telefon Bağlantısı", "Telefon Bağlantısı uygulaması ve telefon bildirimleri"),
            ("bilesen", "", "Masaüstü Widget'ları", "Hava durumu, müzik ve Bugün kartları"),
        };

        static PersonalizationCenter instance;

        /// <summary>Merkezi ac (aciksa one getir) ve kategoriye gec.</summary>
        public static void Open(string category)
        {
            try
            {
                if (instance == null) instance = new PersonalizationCenter();
                if (!instance.IsVisible) instance.Show();
                if (instance.WindowState == WindowState.Minimized) instance.WindowState = WindowState.Normal;
                instance.Select(category ?? "dock");
                instance.Activate();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        readonly LivePreview preview = new LivePreview();
        readonly StackPanel nav = new StackPanel();
        readonly StackPanel pane = new StackPanel();
        readonly ScrollViewer paneScroll;
        readonly TextBlock paneTitle = Flyout.Text("", 26, Theme.TextPrimary, Theme.Heavy);
        readonly TextBlock paneDesc = Flyout.Text("", Theme.Body, Theme.TextTertiary);
        readonly TextBlock previewCaption = Flyout.Text("", Theme.Caption, Theme.TextTertiary);
        readonly StackPanel previewExtra = new StackPanel();
        readonly Border pendingBar = new Border { Visibility = Visibility.Collapsed };
        readonly Border overlay = new Border { Visibility = Visibility.Collapsed };
        readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        readonly Dictionary<string, Border> navButtons = new Dictionary<string, Border>();
        string current = "";
        static readonly Color Accent = Theme.Accent;
        Settings S => Settings.Current;

        public PersonalizationCenter()
        {
            Title = "Winter Kişiselleştirme Merkezi";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanMinimize;
            ShowInTaskbar = true;
            UseLayoutRounding = true;
            FontFamily = Theme.Ui;
            TextElement.SetFontFamily(this, Theme.Ui);
            TextElement.SetFontWeight(this, Theme.Regular);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            var wa = SystemParameters.WorkArea;
            Width = Math.Min(1280, wa.Width - 20);
            Height = Math.Min(800, wa.Height - 10);
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + (wa.Height - Height) / 2;
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); S.Save(); };
            Closed += (s, e) => { if (saveTimer.IsEnabled) { saveTimer.Stop(); S.Save(); } instance = null; };

            const double pad = 18, R = 28;
            var card = new Grid { Margin = new Thickness(pad) };
            card.Children.Add(new Border { CornerRadius = new CornerRadius(R), Background = Theme.Brush(Theme.Background), Effect = Theme.ClayShadow(36, 10) });
            card.Children.Add(Theme.ClayLight(R));
            var body = new Grid();
            body.SizeChanged += (s, e) => body.Clip = new RectangleGeometry(new Rect(e.NewSize), R, R);
            card.Children.Add(body);
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition());
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- baslik cubugu (surukle) ----
            var title = new Grid { Margin = new Thickness(22, 16, 12, 10), Background = Brushes.Transparent };
            title.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 1) try { DragMove(); } catch { } };
            var brand = new StackPanel { Orientation = Orientation.Horizontal };
            var logoTile = new Grid { Width = 40, Height = 40 };
            logoTile.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Theme.Brush(Theme.Accent), Effect = Theme.ClayShadow(10, 3) });
            logoTile.Children.Add(Theme.ClayLight(14));
            logoTile.Children.Add(WinterLogo.Create(22, Theme.Brush(Theme.OnAccent)));
            brand.Children.Add(logoTile);
            var bt = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var big = Flyout.Text("WINTER PERSONALIZATION CENTER", 15, Theme.TextPrimary, Theme.Heavy);
            bt.Children.Add(big);
            bt.Children.Add(Flyout.Text("Winter Floating Dock · Kişiselleştirme Merkezi", Theme.Caption, Theme.TextTertiary, Theme.Bold));
            brand.Children.Add(bt);
            title.Children.Add(brand);
            var winBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            winBtns.Children.Add(Flyout.IconButton("", "Simge durumuna küçült", () => WindowState = WindowState.Minimized, 36, 11));
            winBtns.Children.Add(Flyout.IconButton("", "Kapat", Close, 36, 11));
            title.Children.Add(winBtns);
            body.Children.Add(title);

            // ---- uc sutun ----
            var cols = new Grid { Margin = new Thickness(12, 0, 12, 0) };
            bool narrow = Width < 1150;
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(narrow ? 196 : 222) });
            cols.ColumnDefinitions.Add(new ColumnDefinition());
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(narrow ? 330 : 420) });
            Grid.SetRow(cols, 1);
            body.Children.Add(cols);

            // sol: kategoriler
            var left = new Grid { Margin = new Thickness(0, 0, 8, 12) };
            left.RowDefinitions.Add(new RowDefinition());
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            foreach (var c in Categories)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 10) };
                UIElement ic = c.glyph.Length > 0 ? Flyout.Icon(c.glyph, 16, Theme.TextSecondary) : WinterLogo.Create(17, Theme.Fg(Theme.TextSecondary));
                row.Children.Add(new Border { Width = 22, Child = ic });
                var t = Flyout.Text(c.name, Theme.Body, Theme.TextPrimary, Theme.Bold); t.Margin = new Thickness(12, 0, 0, 0);
                row.Children.Add(t);
                var key = c.key;
                var b = Flyout.Clickable(row, () => Select(key), Theme.RadiusMd);
                b.Margin = new Thickness(0, 1, 0, 1);
                System.Windows.Automation.AutomationProperties.SetName(b, c.name);
                navButtons[key] = b;
                nav.Children.Add(b);
            }
            left.Children.Add(new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
            var back = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            back.Children.Add(Flyout.Divider(0, 8));
            var revert = Flyout.LinkButton("", "Windows görünümüne dön", AskRevert);
            revert.ToolTip = "Duvar kâğıdı, Başlat ve görev çubuğu Windows'un kendi hâline döner";
            back.Children.Add(revert);
            Grid.SetRow(back, 1);
            left.Children.Add(back);
            cols.Children.Add(left);

            // orta: ayarlar
            var mid = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            paneTitle.Margin = new Thickness(4, 0, 0, 2);
            paneDesc.Margin = new Thickness(4, 0, 0, 10);
            paneDesc.TextWrapping = TextWrapping.Wrap;
            pane.Margin = new Thickness(0, 0, 14, 24);
            var midGrid = new Grid { Margin = new Thickness(12, 0, 6, 0) };
            midGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            midGrid.RowDefinitions.Add(new RowDefinition());
            mid.Children.Add(paneTitle); mid.Children.Add(paneDesc);
            midGrid.Children.Add(mid);
            paneScroll = new ScrollViewer { Content = pane, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly };
            Grid.SetRow(paneScroll, 1);
            midGrid.Children.Add(paneScroll);
            Grid.SetColumn(midGrid, 1);
            cols.Children.Add(midGrid);

            // sag: canli onizleme
            var right = new StackPanel { Margin = new Thickness(8, 4, 8, 12) };
            var ph = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 10) };
            ph.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.Brush(Theme.Success), VerticalAlignment = VerticalAlignment.Center });
            var pt = Flyout.Text("Canlı önizleme", Theme.Body, Theme.TextPrimary, Theme.Heavy); pt.Margin = new Thickness(8, 0, 0, 0);
            ph.Children.Add(pt);
            right.Children.Add(ph);
            right.Children.Add(preview);
            previewCaption.TextWrapping = TextWrapping.Wrap;
            previewCaption.Margin = new Thickness(2, 12, 0, 0);
            right.Children.Add(previewCaption);
            previewExtra.Margin = new Thickness(0, 10, 0, 0);
            right.Children.Add(previewExtra);
            Grid.SetColumn(right, 2);
            cols.Children.Add(right);

            // alt: yeniden baslatma gerektiren degisiklikler
            pendingBar.Margin = new Thickness(18, 0, 18, 16);
            Grid.SetRow(pendingBar, 2);
            body.Children.Add(pendingBar);

            overlay.Background = new SolidColorBrush(Color.FromArgb(Theme.Dark ? (byte)120 : (byte)70, 0, 0, 0));
            overlay.CornerRadius = new CornerRadius(R);
            card.Children.Add(overlay);

            Content = card;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && overlay.IsVisible) { overlay.Visibility = Visibility.Collapsed; e.Handled = true; } };
        }

        // ================= gezinme =================
        public void Select(string key)
        {
            var cat = Categories.FirstOrDefault(c => c.key == key);
            if (cat.key == null) cat = Categories[0];
            current = cat.key;
            foreach (var kv in navButtons)
            {
                bool on = kv.Key == current;
                kv.Value.Tag = on ? Theme.SurfaceSelected : (byte)0;
                kv.Value.Background = Theme.Ink(on ? Theme.SurfaceSelected : (byte)0);
                kv.Value.BorderBrush = on ? Theme.Brush(Theme.AccentMuted) : Brushes.Transparent;
            }
            paneTitle.Text = cat.name;
            paneDesc.Text = cat.desc;
            pane.Children.Clear();
            previewExtra.Children.Clear();
            preview.Wallpaper = null;
            switch (current)
            {
                case "baslat": BuildStart(); preview.Mode = "start"; previewCaption.Text = "Başlat menüsü seçtiğin boyut, konum, ton ve düzenle burada açılır."; break;
                case "duvar": BuildWallpapers(); preview.Mode = "desktop"; previewCaption.Text = "Galeriden bir duvar kâğıdına tıkla: masaüstü önizlemesi değişir. “Uygula” ile Windows masaüstüne yazılır."; break;
                case "renk": BuildColors(); preview.Mode = "colors"; previewCaption.Text = "Tema, palet ve vurgu rengi bütün arayüzde: üst çubuk, ada, dock, kartlar."; break;
                case "animasyon": BuildAnimations(); preview.Mode = "anim"; previewCaption.Text = "“Önizlemede oynat” ile seçtiğin Başlat açılışını ve hızı gör."; break;
                case "telefon": BuildPhone(); preview.Mode = "phone"; previewCaption.Text = "Telefonundan gelen bildirimler ekranın üstündeki adada görünür."; break;
                case "bilesen": BuildWidgets(); preview.Mode = "widgets"; previewCaption.Text = "Kartlar masaüstünde pencerelerin arkasında durur."; break;
                default: BuildDock(); preview.Mode = "dock"; previewCaption.Text = "Dock'un boyutu, kutu rengi ve büyütme efekti. Fare üçüncü uygulamanın üzerindeymiş gibi gösterilir."; break;
            }
            preview.Rebuild();
            if (current == "animasyon") Dispatcher.BeginInvoke(new Action(preview.Play), DispatcherPriority.Loaded);
            paneScroll.ScrollToTop();
            if (Theme.Motion) Flyout.Stagger(pane.Children, 14, 0);
        }

        /// <summary>Bir ayar degisti: onizlemeyi guncelle, kisa bir sure sonra kaydet; gerekiyorsa "yeniden baslat" cubugunu goster.</summary>
        void Changed(bool restart = false, bool saveNow = false)
        {
            preview.Rebuild();
            if (restart) ShowPending();
            if (saveNow) { saveTimer.Stop(); S.Save(); }
            else { saveTimer.Stop(); saveTimer.Start(); }
        }

        void ShowPending()
        {
            if (pendingBar.Visibility == Visibility.Visible) return;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = Flyout.Icon("", 16); ic.Foreground = Theme.Brush(Theme.Accent);
            g.Children.Add(ic);
            var t = Flyout.Text("Tema, logo ve dock boyutu gibi değişiklikler bütün arayüzü yeniden çizer. Kaydedildi; uygulamak için FloatingDock yeniden başlar.", Theme.Body, Theme.TextSecondary);
            t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(12, 0, 12, 0);
            Grid.SetColumn(t, 1); g.Children.Add(t);
            var go = Flyout.PrimaryButton("Şimdi uygula", () => { saveTimer.Stop(); S.Save(); Settings.Restart(); });
            Grid.SetColumn(go, 2); g.Children.Add(go);
            pendingBar.Child = Flyout.Card(new Border { Padding = new Thickness(14, 10, 10, 10), Child = g }, 0);
            pendingBar.Visibility = Visibility.Visible;
        }

        // ================= ortak kontroller =================
        static TextBlock T(string s, double size, byte alpha, FontWeight? w = null) => Flyout.Text(s, size, alpha, w);

        void Group(string title, string desc = null)
        {
            var sp = new StackPanel { Margin = new Thickness(4, pane.Children.Count == 0 ? 4 : 22, 0, 6) };
            sp.Children.Add(T(title, Theme.BodyLarge, Theme.TextPrimary, Theme.Heavy));
            if (desc != null) { var d = T(desc, Theme.Caption, Theme.TextTertiary); d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 2, 0, 0); sp.Children.Add(d); }
            pane.Children.Add(sp);
        }

        /// <summary>Kart icinde satir: baslik + aciklama solda, denetim sagda.</summary>
        void Row(string title, string desc, UIElement control) => pane.Children.Add(RowEl(title, desc, control));

        static UIElement RowEl(string title, string desc, UIElement control)
        {
            var g = new Grid { MinHeight = 34 };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var t = T(title, Theme.Body, Theme.TextPrimary, Theme.Bold); t.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(t);
            if (desc != null) { var d = T(desc, Theme.Caption, Theme.TextTertiary); d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 1, 0, 0); sp.Children.Add(d); }
            g.Children.Add(sp);
            if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return Flyout.Card(new Border { Padding = new Thickness(14, 10, 14, 10), Child = g }, 0).WithMargin(new Thickness(0, 3, 0, 3));
        }

        /// <summary>Baslik ustte, genis denetim altta (galeri, kaydirici, renkler).</summary>
        void Block(string title, string desc, UIElement control) => pane.Children.Add(BlockEl(title, desc, control));

        static UIElement BlockEl(string title, string desc, UIElement control)
        {
            var sp = new StackPanel();
            if (title != null) sp.Children.Add(T(title, Theme.Body, Theme.TextPrimary, Theme.Bold));
            if (desc != null) { var d = T(desc, Theme.Caption, Theme.TextTertiary); d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 1, 0, 0); sp.Children.Add(d); }
            if (control is FrameworkElement fe) fe.Margin = new Thickness(0, title == null && desc == null ? 0 : 10, 0, 0);
            sp.Children.Add(control);
            return Flyout.Card(new Border { Padding = new Thickness(14, 12, 14, 14), Child = sp }, 0).WithMargin(new Thickness(0, 3, 0, 3));
        }

        Toggle Tog(string name, bool value, Action<bool> set, bool restart = false)
        {
            var t = new Toggle(name) { IsOn = value };
            t.Changed += v => { set(v); Changed(restart, restart); };
            return t;
        }

        UIElement Seg(string[] labels, string[] keys, string cur, Action<string> set, bool restart = false) =>
            Flyout.Segmented(labels, Math.Max(0, Array.IndexOf(keys, cur)), i => { set(keys[i]); Changed(restart, restart); });

        /// <summary>Baslik + deger satiri, altinda tam genislikte kaydirici.</summary>
        void Slider(string title, string desc, double min, double max, double value, Func<double, string> fmt, Action<double> set)
        {
            var sp = new StackPanel();
            var head = new Grid();
            head.Children.Add(T(title, Theme.Body, Theme.TextPrimary, Theme.Bold));
            var val = T(fmt(value), Theme.Body, Theme.TextSecondary, Theme.Heavy); val.HorizontalAlignment = HorizontalAlignment.Right;
            val.Foreground = Theme.Brush(Theme.Accent);
            head.Children.Add(val);
            sp.Children.Add(head);
            if (desc != null) { var d = T(desc, Theme.Caption, Theme.TextTertiary); d.TextWrapping = TextWrapping.Wrap; sp.Children.Add(d); }
            var sl = new GlowSlider(title) { Margin = new Thickness(0, 8, 0, 0) };
            sl.Loaded += (s, e) => sl.Value = (value - min) / (max - min);
            sl.ValueChanged += v => { var x = min + v * (max - min); val.Text = fmt(x); set(x); Changed(); };
            sp.Children.Add(sl);
            pane.Children.Add(Flyout.Card(new Border { Padding = new Thickness(14, 12, 14, 10), Child = sp }, 0).WithMargin(new Thickness(0, 3, 0, 3)));
        }

        void Note(string text)
        {
            var t = T(text, Theme.Caption, Theme.TextTertiary);
            t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(6, 4, 0, 4);
            pane.Children.Add(t);
        }

        static UIElement Buttons(params UIElement[] items)
        {
            var wp = new WrapPanel();
            foreach (var i in items) { if (i is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 8); wp.Children.Add(i); }
            return wp;
        }

        // ================= 1. Dock =================
        void BuildDock()
        {
            Group("Görünüm");
            Row("Boyut", "Uygulama kutularının büyüklüğü", Seg(new[] { "Küçük", "Orta", "Büyük" }, new[] { "small", "medium", "large" }, S.DockSize, v => S.DockSize = v, true));
            Row("Kutu rengi", "Pastel: ikonun kendi renginden", Seg(new[] { "Pastel", "Beyaz" }, new[] { "pastel", "white" }, S.DockTiles == "white" ? "white" : "pastel", v => S.DockTiles = v, true));
            Group("Davranış");
            Row("İkonları büyüt", "Fare üzerindeyken ikonlar ve komşuları büyür", Tog("İkonları büyüt", S.Magnify, v => S.Magnify = v));
            Slider("Büyütme miktarı", null, 0.1, 0.6, S.MagnifyAmount, x => "%" + Math.Round(x * 100), x => S.MagnifyAmount = Math.Round(x, 2));
            Row("Otomatik gizle", "Bir pencere dock'a değince aşağı kayar; fareyi en alta götürünce geri gelir", Tog("Otomatik gizle", S.AutoHide, v => S.AutoHide = v));
            Row("Okunmamış rozetleri", "WhatsApp, Discord gibi uygulamaların ikonunda kırmızı sayı", Tog("Okunmamış rozetleri", S.Badges, v => { S.Badges = v; if (!v) FloatingDock.Badges.Clear(); }));
            Row("Sistem tepsisi düğmesi", "Arka planda çalışan uygulamaların ikonları (⌃)", Tog("Sistem tepsisi", S.TrayButton, v => S.TrayButton = v, true));
            if (Screens.All().Count > 1)
            {
                Group("Ekranlar");
                Row("Tüm ekranlarda göster", "Her monitörün altında ayrı bir dock", Tog("Tüm ekranlarda göster", S.DockOnAllScreens, v => S.DockOnAllScreens = v));
                Row("Yalnız o ekrandaki pencereler", "Her dock kendi ekranında açık uygulamaları gösterir", Tog("Yalnız o ekrandaki pencereler", S.DockAppsPerScreen, v => S.DockAppsPerScreen = v));
            }
            Group("Sabitlenenler");
            Note("Dock'taki uygulamaları dock'un üzerinde sürükleyerek sırala; dosya ya da uygulamayı dock'a bırakarak sabitle; sağ tıkla kaldır.");
        }

        // ================= 2. Baslat menusu =================
        List<StartApp> allApps;

        void BuildStart()
        {
            Group("Görünüm");
            Row("Başlat düğmesi logosu", "Üst çubuktaki Başlat düğmesi", Seg(new[] { "Winter", "Klasik" }, new[] { "winter", "classic" }, S.StartLogo, v => S.StartLogo = v, true));
            Slider("Genişlik", "%100 genişlik ve yükseklik: tam ekran; daha küçükse ayrı bir panel", 50, 100, S.StartWidth, x => "%" + Math.Round(x), x => S.StartWidth = (int)Math.Round(x));
            Slider("Yükseklik", null, 60, 100, S.StartHeight, x => "%" + Math.Round(x), x => S.StartHeight = (int)Math.Round(x));
            Slider("Opaklık", "Düşük değerde arkadaki masaüstü daha çok görünür", 40, 100, S.StartOpacity, x => "%" + Math.Round(x), x => S.StartOpacity = (int)Math.Round(x));
            Slider("Köşe yuvarlaklığı", "Panel modunda", 0, 48, S.StartRadius, x => Math.Round(x) + " px", x => S.StartRadius = (int)Math.Round(x));
            Row("Arka plan bulanıklığı", "Arkadaki masaüstü buzlu cam gibi bulanıklaşır", Tog("Arka plan bulanıklığı", S.StartBlur, v => S.StartBlur = v));
            Row("Renk tonu", null, Seg(new[] { "Tema", "Vurgu", "Winter", "Gece moru" }, new[] { "theme", "accent", "winter", "midnight" }, S.StartTint, v => S.StartTint = v));
            Row("Saat ve selamlama", "Üstte büyük saat, selamlama ve tarih", Tog("Saat ve selamlama", S.StartShowClock, v => S.StartShowClock = v));
            Row("Kenar şeridi", "Ana sayfa, Uygulamalar, Oyunlar, Belgeler, Resimler, Müzik, Videolar, Ayarlar, Denetim Masası, Güç", Tog("Kenar şeridi", S.StartSidebar, v => S.StartSidebar = v));

            Group("Uygulamalar", "Sabitlenenleri sürükleyerek ya da oklarla sırala. Başlat'ta da kutuları sürükleyebilirsin.");
            var pinBox = new StackPanel();
            var addBox = new StackPanel();
            Block("Sabitlenmiş uygulamalar", null, pinBox);
            Block(null, null, addBox);
            var catBox = new StackPanel();
            Group("Kategoriler", "Uygulamaları kendi gruplarına ayır; Başlat'ta “Uygulamalar” sayfasının en üstünde görünür. Başlat'ta bir uygulamaya sağ tıklayıp da ekleyebilirsin.");
            Block(null, null, catBox);
            Row("Son kullanılan uygulamalar", "Başlat'ın ikinci sayfasında", Tog("Son kullanılan uygulamalar", S.StartRecentApps, v => S.StartRecentApps = v));
            Row("Önerilen dosyalar", "Son açtığın dosyalar", Tog("Önerilen dosyalar", S.StartSuggestedFiles, v => S.StartSuggestedFiles = v));
            pane.Children.Add(Buttons(Flyout.SecondaryButton("Son kullanılanlar listesini temizle", () => { S.StartRecentList.Clear(); Changed(saveNow: true); })));

            Group("Davranış");
            Row("Açılma animasyonu", null, Seg(new[] { "Yakınlaş", "Kaydır", "Belir", "Yok" }, new[] { "zoom", "slide", "fade", "none" }, S.StartAnimation, v => { S.StartAnimation = v; Dispatcher.BeginInvoke(new Action(preview.Play), DispatcherPriority.Loaded); }));
            Row("Açılma konumu", "Panel modunda (genişlik ya da yükseklik %100'den az)", Seg(new[] { "Ortada", "Sol altta", "Altta" }, new[] { "center", "left", "bottom" }, S.StartPosition, v => S.StartPosition = v));
            Row("Windows tuşu Winter Başlat'ı açsın", "Kapalıyken Windows tuşu Windows'un kendi Başlat menüsünü açar", Tog("Windows tuşu", WinKeyHook.Enabled, v => DockWindow.SetWinKeyGlobal(v)));
            Group("Arama");
            Row("Yazınca aramaya başla", "Başlat açıkken klavyede yazmaya başlamak aramayı açar", Tog("Yazınca aramaya başla", S.SearchTypeToStart, v => S.SearchTypeToStart = v));
            Row("Ayarlarda ara", "Wi-Fi, Ekran, Windows Update…", Tog("Ayarlarda ara", S.SearchSettings, v => S.SearchSettings = v));
            Row("Dosyalarda ara", "Dosya Gezgini araması", Tog("Dosyalarda ara", S.SearchFiles, v => S.SearchFiles = v));
            Row("Web'de ara", "Varsayılan tarayıcıda açılır", Tog("Web'de ara", S.SearchWeb, v => S.SearchWeb = v));
            Group("Güç menüsü", "Başlat'ın alt kısmında ve kenar şeridindeki Güç düğmesinde hangi seçenekler olsun");
            var powerWrap = new WrapPanel();
            foreach (var (key, name) in new[] { ("lock", "Kilitle"), ("sleep", "Uyku"), ("hibernate", "Hazırda beklet"), ("signout", "Oturumu kapat"), ("restart", "Yeniden başlat"), ("shutdown", "Kapat") })
            {
                var k = key;
                var chip = Chip(name, S.PowerButtons.Contains(k), on => { S.PowerButtons.Remove(k); if (on) S.PowerButtons.Add(k); Changed(); });
                powerWrap.Children.Add(chip);
            }
            Block(null, null, powerWrap);
            Row("Kapatmadan önce sor", "Yeniden başlat, kapat ve oturumu kapat için onay", Tog("Kapatmadan önce sor", S.PowerConfirm, v => S.PowerConfirm = v));
            pane.Children.Add(Buttons(
                Flyout.SecondaryButton("Başlat görünümünü varsayılana döndür", () =>
                {
                    S.StartWidth = 100; S.StartHeight = 100; S.StartOpacity = 78; S.StartRadius = 32; S.StartBlur = true; S.StartTint = "theme";
                    S.StartAnimation = "zoom"; S.StartPosition = "center"; S.StartSidebar = true; S.StartShowClock = true;
                    Changed(saveNow: true); Select("baslat");
                }),
                Flyout.SecondaryButton("Windows Başlat ayarları", () => WindowsStart.OpenSettings())));

            _ = FillApps(pinBox, addBox, catBox);
        }

        /// <summary>Acma / kapama cipi (guc menusu secimleri).</summary>
        static Border Chip(string text, bool on, Action<bool> changed)
        {
            var t = new TextBlock { Text = text, FontFamily = Theme.Ui, FontSize = Theme.Body, Margin = new Thickness(14, 6, 14, 7) };
            Border b = null;
            void Paint()
            {
                b.Background = on ? Theme.Brush(Theme.Accent) : (Brush)Theme.Ink(Theme.SurfaceIdle);
                b.Tag = on ? (byte)0 : Theme.SurfaceIdle;
                t.Foreground = on ? Theme.Brush(Theme.OnAccent) : Theme.Fg(Theme.TextSecondary);
                t.FontWeight = on ? Theme.Heavy : Theme.Bold;
            }
            b = Flyout.Clickable(t, () => { on = !on; Paint(); changed(on); }, 16);
            b.Margin = new Thickness(0, 0, 8, 8);
            System.Windows.Automation.AutomationProperties.SetName(b, text);
            Paint();
            return b;
        }

        async Task FillApps(StackPanel pinBox, StackPanel addBox, StackPanel catBox)
        {
            pinBox.Children.Add(T("Uygulamalar yükleniyor…", Theme.Body, Theme.TextTertiary));
            if (allApps == null) allApps = await StartMenu.EnumerateAppsAsync();
            if (current != "baslat") return;
            var pins = StartMenu.LoadPins();
            void SavePins()
            {
                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StartMenu.PinFile));
                    System.IO.File.WriteAllLines(StartMenu.PinFile, pins);
                }
                catch (Exception ex) { Program.Log(ex); }
            }
            void Pins()
            {
                pinBox.Children.Clear();
                var list = pins.Select(id => allApps.FirstOrDefault(a => a.Id == id)).Where(a => a != null).ToList();
                if (list.Count == 0) pinBox.Children.Add(T("Henüz sabitlenmiş uygulama yok. Başlat ilk açılışta varsayılanları sabitler.", Theme.Body, Theme.TextTertiary));
                for (int i = 0; i < list.Count; i++)
                {
                    var a = list[i];
                    int idx = i;
                    var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition());
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var grip = Flyout.Icon("", 12, Theme.TextTertiary); grip.Margin = new Thickness(6, 0, 8, 0); grip.Cursor = Cursors.SizeAll;
                    g.Children.Add(grip);
                    var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    name.Children.Add(AppIcon(a, 22));
                    var nt = T(a.Name, Theme.Body, Theme.TextPrimary, Theme.Bold); nt.Margin = new Thickness(10, 0, 0, 0);
                    name.Children.Add(nt);
                    Grid.SetColumn(name, 1); g.Children.Add(name);
                    var tools = new StackPanel { Orientation = Orientation.Horizontal };
                    tools.Children.Add(Flyout.IconButton("", "Yukarı taşı", () => { if (idx == 0) return; pins.Remove(a.Id); pins.Insert(Math.Max(0, pins.IndexOf(list[idx - 1].Id)), a.Id); SavePins(); Pins(); }, 30, 11));
                    tools.Children.Add(Flyout.IconButton("", "Aşağı taşı", () => { if (idx == list.Count - 1) return; pins.Remove(a.Id); pins.Insert(pins.IndexOf(list[idx + 1].Id) + 1, a.Id); SavePins(); Pins(); }, 30, 11));
                    tools.Children.Add(Flyout.IconButton("", "Başlat'tan kaldır", () => { pins.Remove(a.Id); SavePins(); Pins(); }, 30, 11));
                    Grid.SetColumn(tools, 2); g.Children.Add(tools);
                    var row = new Border { Child = g, CornerRadius = new CornerRadius(Theme.RadiusMd), Background = Brushes.Transparent, Padding = new Thickness(0, 2, 0, 2), AllowDrop = true };
                    // surukle-birak ile sirala
                    Point down = default; bool pressed = false;
                    row.PreviewMouseLeftButtonDown += (s, e) => { if (e.OriginalSource is DependencyObject d && IsInside(d, tools)) return; down = e.GetPosition(this); pressed = true; };
                    row.PreviewMouseLeftButtonUp += (s, e) => pressed = false;
                    row.PreviewMouseMove += (s, e) =>
                    {
                        if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
                        var v = e.GetPosition(this) - down;
                        if (Math.Abs(v.Y) < 6 && Math.Abs(v.X) < 6) return;
                        pressed = false;
                        row.Opacity = 0.5;
                        try { DragDrop.DoDragDrop(row, new DataObject("fd-pin", a.Id), DragDropEffects.Move); } catch (Exception ex) { Program.Log(ex); }
                        row.Opacity = 1;
                    };
                    row.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent("fd-pin") ? DragDropEffects.Move : DragDropEffects.None; row.Background = Theme.Ink(Theme.SurfaceHover); e.Handled = true; };
                    row.DragLeave += (s, e) => row.Background = Brushes.Transparent;
                    row.Drop += (s, e) =>
                    {
                        if (!(e.Data.GetData("fd-pin") is string from) || from == a.Id) return;
                        pins.Remove(from);
                        pins.Insert(Math.Clamp(pins.IndexOf(a.Id), 0, pins.Count), from);
                        SavePins(); Pins();
                    };
                    pinBox.Children.Add(row);
                }
            }
            Pins();

            // uygulama ekle (arama)
            addBox.Children.Clear();
            addBox.Children.Add(T("Sabitlemek için uygulama ara", Theme.Body, Theme.TextPrimary, Theme.Bold));
            var results = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var q = SearchBox("Uygulama adı…", text =>
            {
                results.Children.Clear();
                if (text.Trim().Length == 0) return;
                foreach (var a in allApps.Where(x => x.Name.IndexOf(text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0).Take(6))
                {
                    var app = a;
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 6) };
                    sp.Children.Add(AppIcon(app, 20));
                    var nt = T(app.Name, Theme.Body, Theme.TextPrimary); nt.Margin = new Thickness(10, 0, 0, 0); sp.Children.Add(nt);
                    var on = pins.Contains(app.Id);
                    var st = T(on ? "  · sabitli" : "  · sabitle", Theme.Caption, Theme.TextTertiary); sp.Children.Add(st);
                    results.Children.Add(Flyout.Clickable(sp, () => { if (!pins.Contains(app.Id)) { pins.Add(app.Id); SavePins(); Pins(); st.Text = "  · sabitli"; } }, Theme.RadiusMd));
                }
            });
            q.Margin = new Thickness(0, 8, 0, 0);
            addBox.Children.Add(q);
            addBox.Children.Add(results);

            // kategoriler
            void Cats()
            {
                catBox.Children.Clear();
                foreach (var kv in S.StartCategories.OrderBy(k => k.Key))
                {
                    var name = kv.Key;
                    var head = new Grid { Margin = new Thickness(0, 4, 0, 2) };
                    var hl = new StackPanel { Orientation = Orientation.Horizontal };
                    var fi = Flyout.Icon("", 14); fi.Foreground = Theme.Brush(Theme.Accent); hl.Children.Add(fi);
                    var ht = T(name + "  ·  " + kv.Value.Count + " uygulama", Theme.Body, Theme.TextPrimary, Theme.Heavy); ht.Margin = new Thickness(8, 0, 0, 0); hl.Children.Add(ht);
                    head.Children.Add(hl);
                    var del = Flyout.IconButton("", "Kategoriyi sil", () => { S.StartCategories.Remove(name); Changed(saveNow: true); Cats(); }, 30, 11);
                    del.HorizontalAlignment = HorizontalAlignment.Right; head.Children.Add(del);
                    catBox.Children.Add(head);
                    var chips = new WrapPanel { Margin = new Thickness(22, 2, 0, 6) };
                    foreach (var id in kv.Value.ToList())
                    {
                        var a = allApps.FirstOrDefault(x => x.Id == id);
                        if (a == null) continue;
                        var c = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 4, 8, 4) };
                        c.Children.Add(T(a.Name, Theme.Caption + 0.5, Theme.TextSecondary, Theme.Bold));
                        var x = Flyout.Icon("", 9, Theme.TextTertiary); x.Margin = new Thickness(8, 0, 0, 0); c.Children.Add(x);
                        var cid = id;
                        var chip = Flyout.Clickable(c, () => { S.StartCategories[name].Remove(cid); Changed(saveNow: true); Cats(); }, 12, Theme.SurfaceIdle);
                        chip.ToolTip = "Kategoriden çıkar"; chip.Margin = new Thickness(0, 0, 6, 6);
                        chips.Children.Add(chip);
                    }
                    var add = SearchBox("“" + name + "” kategorisine uygulama ekle…", null);
                    var addRes = new StackPanel();
                    ((TextBox)((Grid)((Border)add).Child).Children[0]).TextChanged += (s, e) =>
                    {
                        var text = ((TextBox)s).Text.Trim();
                        addRes.Children.Clear();
                        if (text.Length == 0) return;
                        foreach (var a in allApps.Where(z => z.Name.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0 && !S.StartCategories[name].Contains(z.Id)).Take(5))
                        {
                            var app = a;
                            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 5, 8, 5) };
                            sp.Children.Add(AppIcon(app, 18));
                            var nt = T(app.Name, Theme.Body, Theme.TextPrimary); nt.Margin = new Thickness(10, 0, 0, 0); sp.Children.Add(nt);
                            addRes.Children.Add(Flyout.Clickable(sp, () => { S.StartCategories[name].Add(app.Id); Changed(saveNow: true); Cats(); }, Theme.RadiusMd));
                        }
                    };
                    add.Margin = new Thickness(22, 0, 0, 4);
                    catBox.Children.Add(chips);
                    catBox.Children.Add(add);
                    catBox.Children.Add(addRes);
                    catBox.Children.Add(Flyout.Divider(8, 6));
                }
                // yeni kategori
                var mk = new Grid();
                mk.ColumnDefinitions.Add(new ColumnDefinition());
                mk.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var box = SearchBox("Yeni kategori adı (örn. Tasarım, Okul)…", null);
                mk.Children.Add(box);
                var tb = (TextBox)((Grid)((Border)box).Child).Children[0];
                void Create()
                {
                    var n = tb.Text.Trim();
                    if (n.Length == 0 || S.StartCategories.ContainsKey(n)) return;
                    S.StartCategories[n] = new List<string>();
                    Changed(saveNow: true); Cats();
                }
                tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) Create(); };
                var cb = Flyout.PrimaryButton("Oluştur", Create); cb.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(cb, 1); mk.Children.Add(cb);
                catBox.Children.Add(mk);
            }
            Cats();
        }

        static bool IsInside(DependencyObject d, DependencyObject parent)
        {
            for (var x = d; x != null; x = x is Visual || x is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x)) if (x == parent) return true;
            return false;
        }

        static UIElement AppIcon(StartApp a, double size)
        {
            var img = new Image { Width = size, Height = size };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var c = ShellIcons.Cached(a.IconPath);
            if (c != null) img.Source = c;
            else ShellIcons.GetAsync(a.IconPath).ContinueWith(t => img.Dispatcher.BeginInvoke(new Action(() => img.Source = t.Result)));
            return img;
        }

        /// <summary>Ipucu yazili metin kutusu. Donen Border > Grid > [TextBox, ipucu].</summary>
        static Border SearchBox(string hint, Action<string> changed)
        {
            var tb = new TextBox
            {
                FontSize = Theme.Body, FontFamily = Theme.Ui, Foreground = Theme.Fg(), CaretBrush = Theme.Fg(), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(2, 7, 2, 7), SelectionBrush = Theme.Brush(Theme.Accent)
            };
            System.Windows.Automation.AutomationProperties.SetName(tb, hint);
            var h = T(hint, Theme.Body, Theme.TextTertiary); h.IsHitTestVisible = false; h.Margin = new Thickness(4, 0, 0, 0);
            tb.TextChanged += (s, e) => { h.Visibility = tb.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible; changed?.Invoke(tb.Text); };
            var g = new Grid(); g.Children.Add(tb); g.Children.Add(h);
            var b = new Border
            {
                CornerRadius = new CornerRadius(Theme.RadiusSm), Background = Theme.Brush(Theme.Field), BorderBrush = Theme.Ink(Theme.StrokeDefault),
                BorderThickness = new Thickness(1), Padding = new Thickness(8, 0, 8, 0), Child = g
            };
            tb.GotKeyboardFocus += (s, e) => b.BorderBrush = Theme.Brush(Theme.Accent);
            tb.LostKeyboardFocus += (s, e) => b.BorderBrush = Theme.Ink(Theme.StrokeDefault);
            return b;
        }

        // ================= 3. Duvar kagitlari =================
        string wpMonitor;        // null: tum ekranlar
        string wpSelected;
        readonly TextBlock wpStatus = T("", Theme.Caption, Theme.TextTertiary);

        void BuildWallpapers()
        {
            var mons = Wallpapers.Monitors();
            if (wpMonitor != null && !mons.Any(m => m.Id == wpMonitor)) wpMonitor = null;
            wpSelected ??= Wallpapers.Selected(wpMonitor ?? LivePreview.PrimaryMonitor);
            if (mons.Count > 1)
            {
                var labels = new[] { "Tüm ekranlar" }.Concat(mons.Select(m => "Ekran " + (m.Index + 1))).ToArray();
                var keys = new string[] { null }.Concat(mons.Select(m => m.Id)).ToArray();
                Block("Ekran", "Her monitöre ayrı duvar kâğıdı seçebilirsin. " + string.Join(" · ", mons.Select(m => m.Label)),
                    Flyout.Segmented(labels, Math.Max(0, Array.IndexOf(keys, wpMonitor)), i => { wpMonitor = keys[i]; wpSelected = Wallpapers.Selected(wpMonitor ?? LivePreview.PrimaryMonitor); Select("duvar"); }));
            }
            var gallery = new WrapPanel();
            string usedHere = Wallpapers.Selected(wpMonitor ?? LivePreview.PrimaryMonitor);
            foreach (var id in Wallpapers.All())
            {
                var wid = id;
                bool sel = id == wpSelected;
                var g = new Grid { Width = 150, Height = 84 };
                var th = new Image { Source = Wallpapers.Thumbnail(id), Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(th, BitmapScalingMode.HighQuality);
                g.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = new ImageBrush(th.Source) { Stretch = Stretch.UniformToFill }, Effect = Theme.ClayShadow(12, 4) });
                if (id == usedHere)
                    g.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(9), Background = Theme.Brush(Theme.Accent), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(6),
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Child = new TextBlock { Text = "Kullanımda", FontFamily = Theme.Ui, FontSize = 11, FontWeight = Theme.Heavy, Foreground = Theme.Brush(Theme.OnAccent) }
                    });
                if (Wallpapers.IsCustom(id))
                {
                    var rm = Flyout.Clickable(new Border { Width = 26, Height = 26, Child = new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
                        () => { Wallpapers.RemoveCustom(wid); if (wpSelected == wid) wpSelected = null; Select("duvar"); }, 13);
                    rm.Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)); rm.Tag = (byte)0;
                    rm.HorizontalAlignment = HorizontalAlignment.Right; rm.VerticalAlignment = VerticalAlignment.Top; rm.Margin = new Thickness(5);
                    rm.ToolTip = "Görseli kaldır";
                    g.Children.Add(rm);
                }
                var ring = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2.5), BorderBrush = sel ? Theme.Brush(Theme.Accent) : Brushes.Transparent, Child = g };
                var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 12) };
                sp.Children.Add(ring);
                var nt = T(Wallpapers.NameOf(id), Theme.Caption + 0.5, sel ? Theme.TextPrimary : Theme.TextSecondary, sel ? Theme.Heavy : Theme.Bold);
                nt.Margin = new Thickness(4, 6, 0, 0); nt.MaxWidth = 150;
                sp.Children.Add(nt);
                var b = Flyout.Clickable(sp, () => { wpSelected = wid; preview.Wallpaper = wid; preview.Rebuild(); Select2Gallery(); }, 16);
                b.ToolTip = Wallpapers.Builtin.Where(x => x.id == id).Select(x => x.desc).FirstOrDefault() ?? "Kendi görselin";
                System.Windows.Automation.AutomationProperties.SetName(b, Wallpapers.NameOf(id) + (sel ? ", seçili" : ""));
                gallery.Children.Add(b);
            }
            // gorsel ekle kutusu
            var add = new StackPanel { Width = 150, Height = 84 };
            var addB = Flyout.Clickable(new Border
            {
                Width = 156, Height = 90, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(2), BorderBrush = Theme.Ink(Theme.StrokeDefault),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { Flyout.Icon("", 18, Theme.TextSecondary), new TextBlock { Text = "Görsel ekle", FontFamily = Theme.Ui, FontSize = Theme.Caption, FontWeight = Theme.Bold, Foreground = Theme.Fg(Theme.TextSecondary), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) } }
                }
            }, AddImage, 16);
            addB.ToolTip = "Kişisel fotoğraf ya da görsel ekle (JPG, PNG, BMP)";
            gallery.Children.Add(addB);
            Block("Duvar kâğıtları", "Tıkla: sağdaki önizlemede gör · ✕: kendi görselini kaldır", gallery);
            galleryHost = (Border)pane.Children[pane.Children.Count - 1];

            Row("Yerleşim", "Görsel ekrana nasıl yerleşsin", Seg(new[] { "Doldur", "Sığdır", "Ortala" }, new[] { "fill", "fit", "center" }, S.WallpaperFit, v => { Wallpapers.SetFit(v); }));
            var apply = Flyout.PrimaryButton("Duvar kâğıdını uygula", () => _ = ApplyWallpaper());
            var full = Flyout.SecondaryButton("Tam ekran önizle", () => { if (wpSelected != null) FullPreview(wpSelected); });
            var addBtn = Flyout.SecondaryButton("Görsel ekle…", AddImage);
            pane.Children.Add(Buttons(apply, full, addBtn));
            wpStatus.TextWrapping = TextWrapping.Wrap; wpStatus.Margin = new Thickness(6, 0, 0, 6);
            pane.Children.Add(wpStatus);
            ShowWpStatus();
            if (Wallpapers.HasBackup)
            {
                Group("Windows duvar kâğıdı");
                Row("Eski duvar kâğıdına dön", "Winter'dan önce kullandığın Windows duvar kâğıtları geri gelir", Flyout.SecondaryButton("Geri al", () =>
                {
                    bool ok = Wallpapers.Restore();
                    wpStatus.Text = ok ? "Windows duvar kâğıdı geri yüklendi." : "Geri yüklenemedi (ayrıntı error.log'da).";
                    preview.Wallpaper = null; Select("duvar");
                }));
            }
            if (wpSelected != null) preview.Wallpaper = wpSelected;
            previewExtra.Children.Add(Flyout.SecondaryButton("Tam ekran önizle", () => FullPreview(wpSelected ?? "winter-night")));
        }

        Border galleryHost;
        /// <summary>Galeride secimi degistir (tum sayfayi yeniden kurmadan).</summary>
        void Select2Gallery()
        {
            var off = paneScroll.VerticalOffset;
            var keep = preview.Wallpaper;
            Select("duvar");
            preview.Wallpaper = keep; preview.Rebuild();
            paneScroll.ScrollToVerticalOffset(off);
        }

        void ShowWpStatus()
        {
            var on = Wallpapers.Selected(wpMonitor ?? LivePreview.PrimaryMonitor);
            wpStatus.Text = (wpMonitor == null ? "Tüm ekranlar" : "Bu ekran") + ": " + (on != null ? Wallpapers.NameOf(on) : "Windows'un kendi duvar kâğıdı")
                + (wpSelected != null && wpSelected != on ? " · seçili: " + Wallpapers.NameOf(wpSelected) + " (uygulanmadı)" : "");
        }

        async Task ApplyWallpaper()
        {
            if (wpSelected == null) { wpStatus.Text = "Önce galeriden bir duvar kâğıdı seç."; return; }
            wpStatus.Text = "Uygulanıyor… (her ekranın kendi çözünürlüğünde çiziliyor)";
            var err = await Wallpapers.ApplyAsync(wpSelected, wpMonitor, S.WallpaperFit);
            if (err != null) { wpStatus.Text = err; return; }
            var keep = wpSelected;
            Select2Gallery();
            wpStatus.Text = "✓ " + Wallpapers.NameOf(keep) + " Windows masaüstüne uygulandı" + (wpMonitor == null ? " (tüm ekranlar)." : ".");
        }

        void AddImage()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Duvar kâğıdı için görsel seç",
                Filter = "Görseller|*.jpg;*.jpeg;*.png;*.bmp;*.jfif;*.webp|Tüm dosyalar|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var id = Wallpapers.AddCustom(dlg.FileName);
                wpSelected = id;
                Select("duvar");
                preview.Wallpaper = id; preview.Rebuild();
                wpStatus.Text = "Eklendi: " + Wallpapers.NameOf(id) + " · uygulamak için “Duvar kâğıdını uygula”.";
            }
            catch (Exception ex) { Program.Log(ex); wpStatus.Text = "Görsel eklenemedi: " + ex.Message; }
        }

        /// <summary>Tam ekran onizleme: secili ekranda, Esc ya da tikla: kapat.</summary>
        void FullPreview(string id)
        {
            var mons = Wallpapers.Monitors();
            var m = mons.FirstOrDefault(x => x.Id == wpMonitor) ?? mons[0];
            var src = Wallpapers.IsCustom(id) ? Wallpapers.LoadImage(Wallpapers.CustomPath(id), 0) : Wallpapers.Render(id, Math.Max(640, m.Width), Math.Max(360, m.Height));
            var fit = S.WallpaperFit;
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            var g = new Grid();
            g.Children.Add(new Image { Source = src, Stretch = fit == "fit" ? Stretch.Uniform : fit == "center" ? Stretch.None : Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 10, 10, 10) };
            var nt = new TextBlock { Text = Wallpapers.NameOf(id) + "  ·  tam ekran önizleme", FontFamily = Theme.Ui, FontSize = 15, FontWeight = Theme.Heavy, Foreground = Theme.Fg(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            bar.Children.Add(nt);
            var ap = Flyout.PrimaryButton("Uygula", () => { w.Close(); wpSelected = id; _ = ApplyWallpaper(); });
            var cl = Flyout.SecondaryButton("Kapat (Esc)", w.Close); cl.Margin = new Thickness(8, 0, 0, 0);
            bar.Children.Add(ap); bar.Children.Add(cl);
            g.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(24), Background = Theme.Brush(Theme.Raised), Child = bar, Effect = Theme.ClayShadow(30, 8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 48)
            });
            w.Content = g;
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape) w.Close(); };
            g.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource is Image) w.Close(); };
            w.SourceInitialized += (s, e) =>
            {
                var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                SetWindowPos(h, IntPtr.Zero, m.Rect.left, m.Rect.top, m.Width, m.Height, 0x0040);
            };
            w.Show();
            w.Activate();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // ================= 4. Renk ve temalar =================
        static readonly (string name, string theme, string palette, string accent, string tint, string wallpaper, string desc)[] Presets =
        {
            ("Winter Night", "dark", "mint", "teal", "winter", "winter-night", "Koyu lacivert, buz mavisi vurgu"),
            ("Arctic Blue", "light", "mint", "teal", "winter", "arctic-blue", "Açık ve ferah, buz mavisi"),
            ("Midnight Purple", "dark", "lavender", "violet", "midnight", "midnight-purple", "Siyah ve mor"),
            ("Cyber Winter", "dark", "lavender", "rose", "accent", "cyber-winter", "Neon pembe vurgu"),
            ("Pure Minimal", "dark", "sand", "auto", "theme", "pure-minimal", "Sade ve sıcak koyu"),
        };
        bool presetWallpaper = true;

        void BuildColors()
        {
            Group("Hazır Winter temaları", "Tema, palet, vurgu, Başlat tonu ve duvar kâğıdı birlikte");
            var wrap = new WrapPanel();
            foreach (var p in Presets)
            {
                var pr = p;
                var pc = Theme.PreviewColors(p.theme, p.palette, p.accent);
                bool on = S.Theme == p.theme && S.Palette == p.palette && S.Accent == p.accent && S.StartTint == p.tint;
                var g = new Grid { Width = 168, Height = 104 };
                g.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = new ImageBrush(Wallpapers.Thumbnail(p.wallpaper)) { Stretch = Stretch.UniformToFill } });
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Bottom };
                foreach (var c in new[] { pc.bg, pc.card, pc.accent, pc.pastels[1], pc.pastels[5] })
                    chip.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(c), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0) });
                g.Children.Add(chip);
                var ring = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(2.5), BorderBrush = on ? Theme.Brush(Theme.Accent) : Brushes.Transparent, Child = g };
                var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 12) };
                sp.Children.Add(ring);
                var nt = T(p.name, Theme.Body, Theme.TextPrimary, Theme.Heavy); nt.Margin = new Thickness(4, 6, 0, 0); sp.Children.Add(nt);
                var dt = T(p.desc, Theme.Caption, Theme.TextTertiary); dt.Margin = new Thickness(4, 0, 0, 0); sp.Children.Add(dt);
                var b = Flyout.Clickable(sp, () => _ = ApplyPreset(pr), 18);
                System.Windows.Automation.AutomationProperties.SetName(b, p.name + " teması");
                wrap.Children.Add(b);
            }
            Block(null, null, wrap);
            Row("Duvar kâğıdını da uygula", "Hazır temayı seçince Windows duvar kâğıdı da değişir", Tog("Duvar kâğıdını da uygula", presetWallpaper, v => presetWallpaper = v));

            Group("Tema");
            Row("Açık / koyu", null, Seg(new[] { "Sistem", "Koyu", "Açık" }, new[] { "system", "dark", "light" }, S.Theme, v => S.Theme = v, true));
            var palKeys = Theme.Palettes.Select(p => p.key).ToArray();
            Block("Renk paleti", "Zemin, kartlar ve uygulama kutularının tonları",
                Flyout.Segmented(Theme.Palettes.Select(p => p.name).ToArray(), Math.Max(0, Array.IndexOf(palKeys, S.Palette)), i => { S.Palette = palKeys[i]; Changed(true, true); }));
            Block("Vurgu rengi", "Başlat düğmesi, seçili öğeler, kaydırıcılar", AccentSwatches());
            Note("Tema değişiklikleri önizlemede hemen görünür; uygulamanın kendisi “Şimdi uygula” ile yeniden başlayınca değişir.");
        }

        async Task ApplyPreset((string name, string theme, string palette, string accent, string tint, string wallpaper, string desc) p)
        {
            S.Theme = p.theme; S.Palette = p.palette; S.Accent = p.accent; S.StartTint = p.tint;
            Changed(true, true);
            if (presetWallpaper)
            {
                preview.Wallpaper = p.wallpaper; preview.Rebuild();
                var err = await Wallpapers.ApplyAsync(p.wallpaper, null, S.WallpaperFit);
                if (err != null) Program.Log(new Exception("Hazır tema duvar kâğıdı: " + err));
            }
            var keep = preview.Wallpaper;
            Select("renk");
            preview.Wallpaper = keep; preview.Rebuild();
        }

        UIElement AccentSwatches()
        {
            var wp = new WrapPanel();
            string cur = S.Accent == "frost" || string.IsNullOrEmpty(S.Accent) ? "auto" : S.Accent;
            bool dark = Theme.PreviewColors(S.Theme, S.Palette, S.Accent).dark;
            void Add(string key, Brush fill, string name)
            {
                bool on = cur == key;
                var g = new Grid { Width = 30, Height = 30 };
                g.Children.Add(new System.Windows.Shapes.Ellipse { Fill = fill });
                if (on) g.Children.Add(new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                var ring = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(2.5), BorderBrush = on ? Theme.Fg() : Brushes.Transparent, Child = g };
                var b = Flyout.Clickable(ring, () => { S.Accent = key; Changed(true, true); Select("renk"); }, 20);
                b.Margin = new Thickness(0, 0, 4, 0);
                b.ToolTip = name;
                System.Windows.Automation.AutomationProperties.SetName(b, name + (on ? ", seçili" : ""));
                wp.Children.Add(b);
            }
            Add("auto", Theme.Brush(Theme.AccentColor("auto", S.Palette, dark)), "Paletin rengi");
            foreach (var a in Theme.Accents) Add(a.key, Theme.Brush(dark ? a.dark : a.light), a.name);
            Add("windows", new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0x00, 0x78, 0xD4), 0), new GradientStop(Color.FromRgb(0x88, 0x17, 0x98), 0.5), new GradientStop(Color.FromRgb(0xE7, 0x48, 0x56), 1)
            }, 45), "Windows vurgu rengi");
            return wp;
        }

        // ================= 5. Animasyonlar =================
        void BuildAnimations()
        {
            bool sys = SystemParameters.ClientAreaAnimation;
            if (!sys) Note("Windows'ta “Animasyon efektleri” kapalı: FloatingDock da hareketleri atlar. Açmak için Windows ayarları → Erişilebilirlik → Görsel efektler.");
            Row("Animasyonlar", "Paneller, Başlat, dock ve ada hareketleri", Tog("Animasyonlar", S.Animations, v => S.Animations = v));
            Slider("Hız", "Sola: daha hızlı · sağa: daha yavaş ve yumuşak", 0.5, 1.6, S.AnimSpeed, x => x < 0.8 ? "Hızlı" : x > 1.25 ? "Yavaş" : "Normal", x => S.AnimSpeed = Math.Round(x, 2));
            Row("Başlat açılışı", null, Seg(new[] { "Yakınlaş", "Kaydır", "Belir", "Yok" }, new[] { "zoom", "slide", "fade", "none" }, S.StartAnimation, v => { S.StartAnimation = v; Dispatcher.BeginInvoke(new Action(preview.Play), DispatcherPriority.Loaded); }));
            Row("Dock ikonlarını büyüt", "Fare üzerindeyken", Tog("Dock ikonlarını büyüt", S.Magnify, v => S.Magnify = v));
            pane.Children.Add(Buttons(Flyout.PrimaryButton("Önizlemede oynat", preview.Play),
                Flyout.SecondaryButton("Windows görsel efektleri", () => Shell("ms-settings:easeofaccess-visualeffects"))));
            previewExtra.Children.Add(Flyout.SecondaryButton("▶  Tekrar oynat", preview.Play));
        }

        // ================= 6. Telefon =================
        void BuildPhone()
        {
            bool inst = PhoneLink.Installed;
            Group("Telefon Bağlantısı");
            var st = new StackPanel { Orientation = Orientation.Horizontal };
            st.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Theme.Brush(inst ? Theme.Success : Theme.Danger), VerticalAlignment = VerticalAlignment.Center });
            var stt = T(inst ? "Telefon Bağlantısı uygulaması yüklü" : "Telefon Bağlantısı uygulaması bulunamadı", Theme.Body, Theme.TextPrimary, Theme.Bold); stt.Margin = new Thickness(10, 0, 0, 0);
            st.Children.Add(stt);
            Block(null, null, st);
            Note("Windows'un Telefon Bağlantısı uygulamasıyla Android telefonunun ya da iPhone'unun bildirimleri, mesajları ve aramaları bilgisayarına gelir. Eşleştirmeyi o uygulamada yaparsın.");
            pane.Children.Add(Buttons(
                Flyout.PrimaryButton(inst ? "Telefon Bağlantısı'nı aç" : "Microsoft Store'da aç", PhoneLink.Open),
                Flyout.SecondaryButton("Mobil cihazlar ayarları", PhoneLink.OpenSettings)));
            Group("Bildirimler");
            Row("Telefon bildirimlerini adada göster", "Kapalıyken telefon bildirimleri adada çıkmaz, yalnız takvim panelindeki geçmişe yazılır", Tog("Telefon bildirimlerini adada göster", S.PhoneIsland, v => S.PhoneIsland = v));
            Row("Windows bildirim ayarları", "Telefon Bağlantısı'nın Windows bildirimlerini aç / kapat", Flyout.SecondaryButton("Aç", PhoneLink.OpenNotificationSettings));
            Group("Android için FloatingDock");
            Note("Telefonda da aynı dock, ada ve Başlat menüsü: Android için FloatingDock ana ekran uygulaması.");
            pane.Children.Add(Buttons(Flyout.SecondaryButton("APK sayfasını aç", () => Shell("https://github.com/Enskygr234/FloatingDock/releases/tag/android-son"))));
        }

        // ================= 7. Masaustu bilesenleri =================
        void BuildWidgets()
        {
            Row("Masaüstü bileşenleri", "Kartlar masaüstünde, pencerelerin arkasında", Tog("Masaüstü bileşenleri", S.DesktopWidgets, v => S.DesktopWidgets = v));
            Group("Kartlar");
            Row("Hava durumu", "Sıcaklık, durum, en yüksek / en düşük ve yağmur uyarısı", Tog("Hava durumu kartı", S.WidgetWeather, v => S.WidgetWeather = v));
            Row("Şimdi çalıyor", "Kapak, şarkı ve önceki / oynat / sonraki", Tog("Şimdi çalıyor kartı", S.WidgetMusic, v => S.WidgetMusic = v));
            Row("Bugün", "Tarih, zamanlayıcı, okunmamış bildirimler ve pil", Tog("Bugün kartı", S.WidgetToday, v => S.WidgetToday = v));
            Row("Konum", "Kartlar ekranın hangi tarafında dursun", Seg(new[] { "Sağda", "Solda" }, new[] { "right", "left" }, S.WidgetSide, v => S.WidgetSide = v));
            Group("Hava durumu konumu");
            var w = Settings.Current;
            Note(string.IsNullOrWhiteSpace(w.WeatherCity) && !w.WeatherAuto ? "Hava durumu kapalı: kartın görünmesi için FloatingDock ayarlarından şehir gir ya da konumunu kullan."
                : "Konum: " + (string.IsNullOrEmpty(w.WeatherPlace) ? w.WeatherCity : w.WeatherPlace));
            pane.Children.Add(Buttons(Flyout.SecondaryButton("Şehri değiştir…", () => DockManager.Main?.BarOpen("settings", null))));
        }

        // ================= Windows gorunumune don =================
        void AskRevert()
        {
            var sp = new StackPanel { Width = 420 };
            sp.Children.Add(T("Windows görünümüne dönülsün mü?", 20, Theme.TextPrimary, Theme.Heavy));
            var d = T("• Duvar kâğıdı Winter'dan önceki hâline döner\n• Windows tuşu Windows'un kendi Başlat menüsünü açar\n• Dock kapanır, Windows görev çubuğu geri gelir\n• Açılışta otomatik başlatma kapanır\n\nAyarların silinmez: FloatingDock'u yeniden açınca Winter görünümü seni bekler.",
                Theme.Body, Theme.TextSecondary);
            d.TextWrapping = TextWrapping.Wrap; d.TextTrimming = TextTrimming.None; d.Margin = new Thickness(0, 10, 0, 18);
            sp.Children.Add(d);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            row.Children.Add(Flyout.SecondaryButton("Vazgeç", () => overlay.Visibility = Visibility.Collapsed));
            var go = Flyout.PrimaryButton("Windows görünümüne dön", Revert); go.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(go);
            sp.Children.Add(row);
            overlay.Child = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = Theme.Clay(sp, Theme.Brush(Theme.Raised), Theme.RadiusXl, new Thickness(26), true, 40, 14) };
            overlay.Visibility = Visibility.Visible;
        }

        void Revert()
        {
            overlay.Visibility = Visibility.Collapsed;
            try { Wallpapers.Restore(); } catch (Exception ex) { Program.Log(ex); }
            try { DockWindow.SetWinKeyGlobal(false); } catch (Exception ex) { Program.Log(ex); }
            try { if (AutoStart.IsEnabled) AutoStart.Set(false); } catch (Exception ex) { Program.Log(ex); }
            saveTimer.Stop(); S.Save();
            Close();
            if (DockManager.Main != null) DockManager.Main.Close(); // gorev cubugu eski ayarlariyla geri gelir
            else Application.Current?.Shutdown();
        }

        static void Shell(string uri)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception ex) { Program.Log(ex); }
        }
    }

    static class UiExt
    {
        public static T WithMargin<T>(this T e, Thickness m) where T : FrameworkElement { e.Margin = m; return e; }
    }
}
