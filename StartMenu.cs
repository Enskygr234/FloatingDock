using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
    class StartApp
    {
        public string Name = "", Id = "", Kind = "Uygulama";
        public string IconPath;            // ikon icin kabuk yolu
        public Action Run;
    }

    /// <summary>
    /// Tam ekran Baslat (kil): arkadaki masaustu bulaniklasir; buyuk saat ve selamlama, arama, hava ve muzik icin genis
    /// canli kutular, ikonun kendi renginden pastel uygulama kutulari. Sayfalar: sabitlenenler, son dosyalar, tum uygulamalar
    /// (fare tekerlegi ya da alttaki noktalar). Yazmaya baslayinca arama sonuclari gelir.
    /// </summary>
    class StartMenu : Flyout
    {
        static readonly string PinFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatingDock", "start-pins.txt");
        const double TileSize = 100, TileRadius = 32, Cell = 140;

        readonly TextBox search = new TextBox();
        readonly TextBlock placeholder = Text("Uygulama, ayar veya dosya ara", Theme.BodyLarge, Theme.TextTertiary);
        readonly Grid middle = new Grid();
        readonly StackPanel home = new StackPanel();
        readonly UniformGrid pinGrid = new UniformGrid { Columns = 6, Width = 6 * Cell };
        readonly UniformGrid recentGrid = new UniformGrid { Columns = 2, Width = 860 };
        readonly ScrollViewer recentView;
        readonly StackPanel liveRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) };
        readonly StackPanel dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock bigClock = new TextBlock { FontSize = 60, LineHeight = 60, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, HorizontalAlignment = HorizontalAlignment.Center };
        int page;
        readonly StackPanel allList = new StackPanel();
        readonly ScrollViewer allView;
        readonly StackPanel results = new StackPanel();
        readonly ScrollViewer resultsView;
        readonly Border powerMenu;
        readonly Border allBtn;
        readonly TextBlock sectionTitle;
        readonly TextBlock allBtnText = Text("Tüm uygulamalar", Theme.Body, Theme.TextPrimary);
        readonly TextBlock allBtnIcon = Icon("", 10, Theme.TextSecondary);

        List<StartApp> apps = new List<StartApp>();
        List<string> pins;
        List<(StartApp app, Border row)> shown = new List<(StartApp, Border)>();
        int sel;
        bool showingAll;

        public StartMenu() : base("FloatingDockStart")
        {
            // tam ekran: kil kart yerine arkadaki masaustunu bulaniklastiran sistem akriligi
            AllowsTransparency = false;
            Glass.Setup(this);
            SourceInitialized += (s, e) => Glass.ApplyFullscreenGlass(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            SizeToContent = SizeToContent.Manual;
            TextElement.SetFontFamily(this, Theme.Ui);
            TextElement.SetFontWeight(this, Theme.Regular);
            var name = Program.Demo ? "Kullanıcı" : UserName();
            userName = name.Split(' ')[0];

            var root = new Grid();
            // akriligin ustune kil zemini: lavanta sis (masaustu hafifce secilir)
            root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(Theme.Dark ? (byte)200 : (byte)190, Theme.Background.R, Theme.Background.G, Theme.Background.B)) });
            root.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == root || e.OriginalSource is Border bd && bd.Parent == root) HideFlyout(); };
            var page0 = new Grid { Margin = new Thickness(96, 64, 96, 40), MaxWidth = 1500 };
            page0.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page0.RowDefinitions.Add(new RowDefinition());
            page0.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(page0);

            // ---- ust: ortada saat + selamlama, altinda genis arama ----
            var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var hl = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            bigClock.FontFamily = Theme.Ui; bigClock.FontWeight = Theme.Heavy; bigClock.Foreground = Theme.Fg();
            System.Windows.Documents.Typography.SetNumeralAlignment(bigClock, FontNumeralAlignment.Tabular);
            hl.Children.Add(bigClock);
            hud.FontWeight = Theme.Bold; hud.FontSize = 17; hud.Margin = new Thickness(0, 6, 0, 0); hud.HorizontalAlignment = HorizontalAlignment.Center;
            hl.Children.Add(hud);
            head.Children.Add(hl);

            search.FontSize = 18; search.FontFamily = Theme.Ui; search.FontWeight = Theme.Bold; search.Foreground = Theme.Fg(); search.CaretBrush = Theme.Brush(Theme.Accent);
            search.Background = Brushes.Transparent; search.BorderThickness = new Thickness(0); search.Padding = new Thickness(0);
            search.SelectionBrush = Theme.Brush(Accent);
            System.Windows.Automation.AutomationProperties.SetName(search, "Uygulama, ayar ya da dosya ara");
            search.VerticalContentAlignment = VerticalAlignment.Center;
            search.TextChanged += (s, e) => OnSearch();
            placeholder.Text = "Yazmaya başla…"; placeholder.FontSize = 18; placeholder.FontWeight = Theme.Bold;
            placeholder.IsHitTestVisible = false;
            var sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.ColumnDefinitions.Add(new ColumnDefinition());
            var mag = Icon("", 18); mag.Foreground = Theme.Brush(Theme.Accent); mag.Margin = new Thickness(0, 0, 14, 0);
            sg.Children.Add(mag);
            var sbox = new Grid(); sbox.Children.Add(search); sbox.Children.Add(placeholder);
            Grid.SetColumn(sbox, 1); sg.Children.Add(sbox);
            // gomuk (ic golgeli) arama kuyusu
            var well = new Grid { Width = 620, Height = 56, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
            well.Children.Add(new Border { CornerRadius = new CornerRadius(28), Background = Theme.Brush(Theme.Field), Effect = Theme.ClayShadow(18, 5) });
            well.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(28), IsHitTestVisible = false, BorderThickness = new Thickness(0, 2.5, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(Theme.Dark ? (byte)70 : (byte)26, Theme.Shadow.R, Theme.Shadow.G, Theme.Shadow.B))
            });
            well.Children.Add(new Border { Padding = new Thickness(22, 0, 22, 0), Child = sg });
            head.Children.Add(well);
            page0.Children.Add(head);

            // ---- orta: canli kutular + sabitlenenler | son dosyalar | tum uygulamalar | sonuclar ----
            home.HorizontalAlignment = HorizontalAlignment.Center;
            home.VerticalAlignment = VerticalAlignment.Center;
            home.Children.Add(liveRow);
            home.Children.Add(pinGrid);
            middle.Children.Add(home);
            recentGrid.HorizontalAlignment = HorizontalAlignment.Center;
            var recWrap = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var recHead = Text("Son kullanılanlar", 22, Theme.TextPrimary, Theme.Heavy);
            recHead.Margin = new Thickness(6, 0, 0, 14);
            recWrap.Children.Add(recHead);
            recWrap.Children.Add(recentGrid);
            recentView = new ScrollViewer { Content = recWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Visibility = Visibility.Collapsed, PanningMode = PanningMode.VerticalOnly };
            middle.Children.Add(recentView);
            allList.Width = 760;
            allView = new ScrollViewer { Content = allList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Visibility = Visibility.Collapsed, PanningMode = PanningMode.VerticalOnly, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 16) };
            middle.Children.Add(allView);
            results.Width = 760;
            resultsView = new ScrollViewer { Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 16) };
            middle.Children.Add(resultsView);
            Grid.SetRow(middle, 1);
            page0.Children.Add(middle);
            // fare tekerlegi: sayfalar arasinda gecis (liste sayfalari kendi icinde kayar)
            middle.PreviewMouseWheel += (s, e) =>
            {
                if (search.Text.Length > 0) return;
                if (page == 2 && !(e.Delta > 0 && allView.VerticalOffset <= 0)) return;
                Page(Math.Clamp(page + (e.Delta > 0 ? -1 : 1), 0, 2));
                e.Handled = true;
            };

            // ---- alt: kullanici | sayfa noktalari | guc ----
            var foot = new Grid();
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            var av = new Grid { Width = 40, Height = 40 };
            av.Children.Add(new Border { CornerRadius = new CornerRadius(20), Background = Theme.Brush(Theme.Accent), Effect = Theme.ClayShadow(10, 3) });
            av.Children.Add(Theme.ClayLight(20));
            av.Children.Add(new TextBlock { Text = name.Substring(0, 1).ToUpper(new System.Globalization.CultureInfo("tr-TR")), Foreground = Theme.Brush(Theme.OnAccent), FontSize = 17, FontWeight = Theme.Heavy, FontFamily = Theme.Ui, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 6, 18, 6) };
            who.Children.Add(av);
            var wn = Text(name, 16, Theme.TextPrimary, Theme.Bold); wn.Margin = new Thickness(10, 0, 0, 0);
            who.Children.Add(wn);
            var whoBtn = Clickable(who, () => Run(() => Shell("ms-settings:accounts")), 26, Theme.SurfaceIdle);
            whoBtn.HorizontalAlignment = HorizontalAlignment.Left;
            whoBtn.ToolTip = "Hesap ayarları · sağ tık: oturumu kapat";
            var um = new ContextMenu();
            var so = new MenuItem { Header = "Oturumu kapat" }; so.Click += (s, e) => Run(() => ExitWindowsEx(0, 0)); um.Items.Add(so);
            whoBtn.ContextMenu = um;
            foot.Children.Add(whoBtn);
            Grid.SetColumn(dots, 1); foot.Children.Add(dots);
            var power = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            power.Children.Add(Round("", "Kilitle", () => Run(() => LockWorkStation())));
            power.Children.Add(Round("", "Uyku", () => Run(() => SetSuspendState(false, false, false))));
            power.Children.Add(Round("", "Yeniden başlat", () => Run(() => Shell("shutdown.exe", "/r /t 0"))));
            var off = Round("", "Bilgisayarı kapat", () => Run(() => Shell("shutdown.exe", "/s /t 0")));
            var (pf, pi) = Theme.PastelColors(Theme.Pastel.Pink);
            off.Background = Theme.Brush(pf); off.Tag = (byte)0; ((TextBlock)((Border)off.Child).Child).Foreground = Theme.Brush(pi);
            power.Children.Add(off);
            Grid.SetColumn(power, 2); foot.Children.Add(power);
            Grid.SetRow(foot, 2);
            page0.Children.Add(foot);
            BuildDots();

            // eski kodla uyum: "Tum uygulamalar" dugmesi ve guc menusu artik sayfalarda / alt seritte
            allBtn = new Border { Child = new StackPanel() };
            sectionTitle = new TextBlock();
            powerMenu = new Border { Visibility = Visibility.Collapsed };

            Content = root;
            PreviewKeyDown += OnKey;
            // uygulama listesini ve ikonlari simdiden yukle: ilk acilista bos gorunmesin
            Dispatcher.BeginInvoke(new Action(() => _ = LoadAsync()), DispatcherPriority.ApplicationIdle);
            PreviewTextInput += (s, e) => { if (!search.IsKeyboardFocused) { search.Focus(); } };
            clockTimer.Tick += (s, e) => UpdateHeader();
            Weather.Changed += () => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) BuildLive(); }));
            Hub.MediaChanged += m => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) BuildLive(); }));
        }

        // ---- sayfalar ----
        void BuildDots()
        {
            dots.Children.Clear();
            var names = new[] { "Sabitlenenler", "Son kullanılanlar", "Tüm uygulamalar" };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                bool on = i == page && search.Text.Length == 0;
                var d = new Border { Width = on ? 34 : 12, Height = 12, CornerRadius = new CornerRadius(6), Background = on ? Theme.Brush(Theme.Accent) : Theme.Ink(Theme.StrokeDefault) };
                var b = Clickable(new Border { Padding = new Thickness(6, 14, 6, 14), Child = d }, () => { search.Text = ""; Page(k); }, 12);
                b.ToolTip = names[i];
                System.Windows.Automation.AutomationProperties.SetName(b, names[i] + (on ? ", şu an" : ""));
                dots.Children.Add(b);
            }
        }

        void Page(int p)
        {
            if (p == 2 && page != 2) BuildAll();
            if (p == 1) BuildRecent();
            page = p;
            showingAll = p == 2;
            Swap(p == 0 ? home : p == 1 ? (UIElement)recentView : allView);
            BuildDots();
        }

        // ---- canli kutular: hava + calan muzik ----
        void BuildLive()
        {
            liveRow.Children.Clear();
            var w = Weather.Current;
            if (w != null)
            {
                var (f, ink) = Theme.PastelColors(Theme.Pastel.Sky);
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(Weather.Icon(w.Code, w.IsDay, 58, Theme.Brush(ink)));
                var tx = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var t = Text(Weather.Temp(w.Temp), 34, null, Theme.Heavy); t.Foreground = Theme.Brush(ink);
                tx.Children.Add(t);
                // yagis yaklasiyorsa onu, yoksa durumu yaz (kutuya sigsin)
                var nc = Weather.RainStart(w, out _, out _) ? Weather.NowcastText(w) : Weather.Describe(w.Code);
                var d = Text(nc, Theme.Body, null, Theme.Bold); d.Foreground = Theme.Brush(ink); d.MaxWidth = 170;
                tx.Children.Add(d);
                sp.Children.Add(tx);
                liveRow.Children.Add(Wide(sp, f, "Hava durumu: " + w.Place + ", " + Weather.Temp(w.Temp), () => { HideFlyout(); CalendarFlyout.OpenWeather?.Invoke(); }));
            }
            var m = Hub.CurrentMedia;
            if (m != null)
            {
                var g = new Grid { Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var art = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(24), Background = new LinearGradientBrush(Color.FromRgb(0xF4, 0x72, 0xB6), Theme.Accent, 45) };
                var img = m.Art != null ? IslandWindow.Decode(m.Art, 152) : null;
                if (img != null) art.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                g.Children.Add(art);
                var tx = new StackPanel { Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                var t = Text(m.Title, 18, null, Theme.Heavy); t.Foreground = Brushes.White;
                var a = Text(m.Artist + " · " + IslandWindow.FriendlySource(m.SourceId), Theme.Body, null, Theme.Bold); a.Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xD4, 0xE3));
                tx.Children.Add(t); tx.Children.Add(a);
                Grid.SetColumn(tx, 1); g.Children.Add(tx);
                var pi = new TextBlock { Text = m.Playing ? "" : "", FontFamily = Theme.Icons, FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x2F, 0x3A)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var pb = Clickable(new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = Brushes.White, Child = pi }, () => Hub.Media.PlayPause(), 24);
                pb.ToolTip = "Oynat / duraklat";
                Grid.SetColumn(pb, 2); g.Children.Add(pb);
                liveRow.Children.Add(Wide(g, Color.FromRgb(0x33, 0x2F, 0x3A), "Şimdi çalıyor: " + m.Title, () => { HideFlyout(); Hub.OpenPlayer?.Invoke(); }));
            }
            liveRow.Visibility = liveRow.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Iki kutu genisliginde kil kutu (canli icerik icin).</summary>
        Border Wide(UIElement content, Color fill, string name, Action click)
        {
            var g = new Grid { Width = 2 * Cell - 20, Height = TileSize };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(TileRadius), Background = Theme.Brush(fill), Effect = Theme.ClayShadow(24, 9) });
            g.Children.Add(Theme.ClayLight(TileRadius));
            g.Children.Add(content);
            var b = Clickable(g, click, TileRadius);
            b.Margin = new Thickness(10, 0, 10, 0);
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            return b;
        }

        // ---- ust bilgi ----
        readonly TextBlock greeting = Text("", Theme.Title, Theme.TextPrimary, FontWeights.SemiBold);
        readonly TextBlock hud = Text("", Theme.Body, Theme.TextSecondary);
        readonly TextBlock status = Text("", Theme.Caption, Theme.TextTertiary);
        readonly DispatcherTimer clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        string userName = "";

        void UpdateHeader()
        {
            var tr = new System.Globalization.CultureInfo("tr-TR");
            var n = DateTime.Now;
            string hi = n.Hour >= 5 && n.Hour < 12 ? "Günaydın" : n.Hour < 18 && n.Hour >= 12 ? "İyi günler" : n.Hour >= 18 && n.Hour < 22 ? "İyi akşamlar" : "İyi geceler";
            bigClock.Text = n.ToString("HH:mm", tr);
            var d = n.ToString("dddd d MMMM", tr);
            hud.Text = hi + ", " + userName + " · " + char.ToUpper(d[0], tr) + d.Substring(1);
            status.Text = apps.Count > 0 ? apps.Count + " uygulama yüklü" : "";
        }

        // ---------------- acilis ----------------
        protected override void OnOpening()
        {
            search.Text = "";
            Page(0);
            if (apps.Count > 0) _ = RefreshAppsAsync();
            BuildLive();
            UpdateHeader(); clockTimer.Start();
            Stagger(liveRow.Children, 30, 60);
            Stagger(pinGrid.Children, 14, 100);
            Dispatcher.BeginInvoke(new Action(() => { search.Focus(); Keyboard.Focus(search); }), DispatcherPriority.Input);
        }

        protected override void OnClosing() => clockTimer.Stop();

        async Task LoadAsync()
        {
            pins = LoadPins();
            await RefreshAppsAsync();
        }

        async Task RefreshAppsAsync()
        {
            var list = await EnumerateAppsAsync();
            if (list.Count == 0) return;
            apps = list;
            if (pins == null || pins.Count == 0) { pins = DefaultPins(apps); SavePins(); }
            BuildPins();
            foreach (var id in pins) { var a = apps.FirstOrDefault(x => x.Id == id); if (a != null) _ = ShellIcons.GetAsync(a.IconPath); }
            if (showingAll) BuildAll();
            if (search.Text.Length > 0) OnSearch();
        }

        // ---------------- uygulama listesi (shell:AppsFolder) ----------------
        static Task<List<StartApp>> EnumerateAppsAsync()
        {
            var tcs = new TaskCompletionSource<List<StartApp>>();
            var t = new Thread(() =>
            {
                var list = new List<StartApp>();
                try
                {
                    dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                    dynamic folder = sh.NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
                    foreach (dynamic it in folder.Items())
                    {
                        string n = it.Name, id = it.Path;
                        if (string.IsNullOrWhiteSpace(n) || string.IsNullOrWhiteSpace(id)) continue;
                        if (id.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                        var low = n.ToLowerInvariant();
                        if (low.Contains("uninstall") || low.Contains("kaldır") || low.Contains("readme") || low.Contains("benioku")) continue;
                        string full = "shell:AppsFolder\\" + id;
                        list.Add(new StartApp { Name = n, Id = id, IconPath = full, Run = () => Shell("explorer.exe", full) });
                    }
                }
                catch (Exception ex) { Program.Log(ex); }
                tcs.TrySetResult(list.GroupBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First())
                                     .OrderBy(a => a.Name, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true)).ToList());
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return tcs.Task;
        }

        static List<string> DefaultPins(List<StartApp> all)
        {
            string[] wanted =
            {
                "Microsoft Edge", "Dosya Gezgini|File Explorer", "Ayarlar|Settings", "Microsoft Store", "Fotoğraflar|Photos",
                "Hesap Makinesi|Calculator", "Not Defteri|Notepad", "Paint", "Terminal", "Ekran Alıntısı Aracı|Snipping Tool",
                "Spotify", "WhatsApp", "Visual Studio Code", "Discord", "Steam", "Google Chrome", "Word", "Excel"
            };
            var res = new List<string>();
            foreach (var w in wanted)
            {
                var names = w.Split('|');
                var a = all.FirstOrDefault(x => names.Any(n => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase)))
                     ?? all.FirstOrDefault(x => names.Any(n => x.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase)));
                if (a != null && !res.Contains(a.Id)) res.Add(a.Id);
            }
            return res.Take(18).ToList();
        }

        static List<string> LoadPins()
        {
            try { return File.Exists(PinFile) ? File.ReadAllLines(PinFile).Where(l => l.Trim().Length > 0).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        void SavePins()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(PinFile)); File.WriteAllLines(PinFile, pins); } catch { }
        }

        void TogglePin(StartApp a)
        {
            if (pins.Contains(a.Id)) pins.Remove(a.Id); else pins.Add(a.Id);
            SavePins();
            BuildPins();
        }

        // ---------------- gorunumler ----------------
        void BuildPins()
        {
            pinGrid.Children.Clear();
            foreach (var id in pins.Take(18))
            {
                var a = apps.FirstOrDefault(x => x.Id == id);
                if (a != null) pinGrid.Children.Add(Tile(a));
            }
            // satir sayisini sabit tut (3 satir) ki arayuz zıplamasın
            while (pinGrid.Children.Count % pinGrid.Columns != 0) pinGrid.Children.Add(new Border { Height = 92 }); // son satiri tamamla
        }

        UIElement Tile(StartApp a)
        {
            // kil kutu: ikonun kendi rengine en yakin pastel dolgu, kabarik golge; alti etiket
            var fill = new SolidColorBrush(Theme.PastelColors(Theme.Pastel.Lilac).fill);
            var box = new Grid { Width = TileSize, Height = TileSize, HorizontalAlignment = HorizontalAlignment.Center };
            box.Children.Add(new Border { CornerRadius = new CornerRadius(TileRadius), Background = fill, Effect = Theme.ClayShadow(22, 8) });
            box.Children.Add(Theme.ClayLight(TileRadius));
            var img = (Image)AppImage(a, 56, src => fill.Color = Theme.PastelColors(Theme.PastelOf(src)).fill);
            box.Children.Add(img);
            var p = new StackPanel { Margin = new Thickness(6, 8, 6, 8) };
            p.Children.Add(box);
            var t = Text(a.Name, 14, Theme.TextPrimary, Theme.Bold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextAlignment = TextAlignment.Center;
            t.MaxWidth = Cell - 10;
            t.Margin = new Thickness(0, 12, 0, 0);
            p.Children.Add(t);
            var b = Clickable(p, () => Run(a.Run), 30);
            b.Margin = new Thickness(0, 0, 0, 14);
            b.ToolTip = a.Name;
            System.Windows.Automation.AutomationProperties.SetName(b, a.Name);
            b.ContextMenu = ItemMenu(a);
            return b;
        }

        ContextMenu ItemMenu(StartApp a)
        {
            if (a.Kind != "Uygulama") return null;
            var m = new ContextMenu();
            var pin = new MenuItem();
            m.Opened += (s, e) => pin.Header = pins != null && pins.Contains(a.Id) ? "Başlangıçtan kaldır" : "Başlangıca sabitle";
            pin.Click += (s, e) => TogglePin(a);
            m.Items.Add(pin);
            return m;
        }

        static UIElement AppImage(StartApp a, double size) => AppImage(a, size, null);

        /// <summary>Kabuk ikonu (onbellekte yoksa arka planda yuklenir); loaded: ikon gelince (renk icin).</summary>
        static UIElement AppImage(StartApp a, double size, Action<ImageSource> loaded)
        {
            var img = new Image { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            if (a.IconPath == null) return img;
            var c = ShellIcons.Cached(a.IconPath);
            if (c != null) { img.Source = c; loaded?.Invoke(c); }
            else
            {
                img.Opacity = 0;
                ShellIcons.GetAsync(a.IconPath).ContinueWith(t =>
                    img.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        img.Source = t.Result;
                        if (t.Result != null) loaded?.Invoke(t.Result);
                        img.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Theme.D(Theme.Normal)));
                    })));
            }
            return img;
        }

        static readonly HashSet<string> Junk = new HashSet<string> { "pdb", "tmp", "log", "migrated", "db", "db-wal", "db-shm", "wal", "shm", "cache", "dat", "ini", "lock", "bak", "etl" };

        /// <summary>Sadece test icin: arama kutusuna yaz / tum uygulamalari ac.</summary>
        public void TestQuery(string q) { if (q == "*") ShowAll(true); else search.Text = q; }

        void BuildRecent()
        {
            recentGrid.Children.Clear();
            var items = new List<FileInfo>();
            try
            {
                var dir = new DirectoryInfo(Program.Demo ? DemoRecent() : Environment.GetFolderPath(Environment.SpecialFolder.Recent));
                foreach (var f in dir.GetFiles(Program.Demo ? "*.*" : "*.lnk").OrderByDescending(f => f.LastWriteTime))
                {
                    var n = Program.Demo ? f.Name : Path.GetFileNameWithoutExtension(f.Name);
                    if (!Path.HasExtension(n) || n.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue; // klasor/baglanti degil, dosya
                    if (Junk.Contains(Path.GetExtension(n).TrimStart('.').ToLowerInvariant()) || n.StartsWith(".")) continue;   // teknik dosyalar
                    items.Add(f);
                    if (items.Count == 12) break;
                }
            }
            catch { }
            if (items.Count == 0)
            {
                var e = Text("Henüz yeni bir dosya açılmadı.", Theme.Body, Theme.TextTertiary);
                e.Margin = new Thickness(4, 4, 0, 0);
                recentGrid.Children.Add(e);
                return;
            }
            foreach (var f in items)
            {
                var g = new Grid { Margin = new Thickness(12, 10, 12, 10) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var a = new StartApp { IconPath = f.FullName };
                g.Children.Add(new Border { Width = 32, Height = 32, Child = AppImage(a, 28) });
                var tp = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                tp.Children.Add(Text(Path.GetFileNameWithoutExtension(f.Name), Theme.BodyLarge, Theme.TextPrimary, Theme.Bold));
                tp.Children.Add(Text(Ago(f.LastWriteTime), Theme.Caption, Theme.TextTertiary));
                Grid.SetColumn(tp, 1); g.Children.Add(tp);
                var path = f.FullName;
                var card = Clickable(g, () => Run(() => Shell(path)), Theme.RadiusLg, Theme.SurfaceIdle);
                card.Margin = new Thickness(6);
                card.ToolTip = f.Name;
                recentGrid.Children.Add(card);
            }
        }

        static string DemoRecent()
        {
            var dir = Path.Combine(Path.GetTempPath(), "fd-demo-recent");
            Directory.CreateDirectory(dir);
            var names = new[] { ("Proje sunumu.pptx", 0.3), ("Bütçe 2026.xlsx", 2), ("Tatil fotoğrafı.jpg", 20), ("Ödev raporu.docx", 30), ("Notlar.txt", 50), ("Kılavuz.pdf", 80) };
            foreach (var (n, h) in names) { var p = Path.Combine(dir, n); if (!File.Exists(p)) File.WriteAllText(p, ""); File.SetLastWriteTime(p, DateTime.Now.AddHours(-h)); }
            return dir;
        }

        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "Az önce";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " dk önce";
            if (d.TotalDays < 1) return (int)d.TotalHours + " saat önce";
            if (d.TotalDays < 2) return "Dün";
            return t.ToString("d MMMM", new System.Globalization.CultureInfo("tr-TR"));
        }

        void ShowAll(bool on) => Page(on ? 2 : 0);

        void Swap(UIElement show)
        {
            // ana gorunum icerigi kadar yer kaplar; liste/sonuc gorunumleri sabit yukseklikte kaydirilir
            foreach (UIElement c in middle.Children)
            {
                bool v = c == show;
                if (v && c.Visibility != Visibility.Visible)
                {
                    c.Visibility = Visibility.Visible;
                    c.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Theme.D(Theme.Normal)));
                    var tt = new TranslateTransform(); c.RenderTransform = tt;
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Theme.Motion ? 8 : 0, 0, Theme.D(Theme.Slow)) { EasingFunction = Theme.EaseOut });
                }
                else if (!v) c.Visibility = Visibility.Collapsed;
            }
        }

        void BuildAll()
        {
            allList.Children.Clear();
            var tr = new System.Globalization.CultureInfo("tr-TR");
            foreach (var g in apps.GroupBy(a => char.IsLetter(a.Name[0]) ? char.ToUpper(a.Name[0], tr).ToString() : "#"))
            {
                // harf basligi: vurgu renginde harf + ince cizgi
                var hg = new Grid { Margin = new Thickness(12, 14, 10, 4) };
                hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                hg.ColumnDefinitions.Add(new ColumnDefinition());
                var letter = Text(g.Key, Theme.Body, Theme.TextPrimary, FontWeights.SemiBold);
                letter.Foreground = Theme.Brush(Accent);
                hg.Children.Add(letter);
                var ln = new Border { Height = 1, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = Theme.Ink(Theme.StrokeSubtle) };
                Grid.SetColumn(ln, 1); hg.Children.Add(ln);
                allList.Children.Add(hg);
                foreach (var a in g) allList.Children.Add(Row(a, false));
            }
            allView.ScrollToTop();
        }

        Border Row(StartApp a, bool showKind)
        {
            var g = new Grid { Margin = new Thickness(10, 6, 12, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(a.IconPath != null
                ? new Border { Width = 32, Height = 32, Child = AppImage(a, 24) }
                : new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(Theme.RadiusMd), Background = Theme.Ink(Theme.SurfaceHover), Child = Icon(a.Id, 15) });
            var tp = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tp.Children.Add(Text(a.Name, Theme.Body, Theme.TextPrimary));
            if (showKind) { var k = Text(a.Kind, Theme.Caption, Theme.TextTertiary); k.Margin = new Thickness(0, 1, 0, 0); tp.Children.Add(k); }
            Grid.SetColumn(tp, 1); g.Children.Add(tp);
            var b = Clickable(g, () => Run(a.Run), Theme.RadiusMd);
            b.Margin = new Thickness(0, 1, 0, 1);
            System.Windows.Automation.AutomationProperties.SetName(b, a.Name);
            b.ContextMenu = ItemMenu(a);
            return b;
        }

        // ---------------- arama ----------------
        static readonly (string name, string glyph, string uri)[] Settings =
        {
            ("Wi-Fi ayarları", "", "ms-settings:network-wifi"), ("Bluetooth ve cihazlar", "", "ms-settings:bluetooth"),
            ("Ekran ayarları", "", "ms-settings:display"), ("Ses ayarları", "", "ms-settings:sound"),
            ("Windows Update", "", "ms-settings:windowsupdate"), ("Kişiselleştirme", "", "ms-settings:personalization"),
            ("Arka plan / duvar kağıdı", "", "ms-settings:personalization-background"), ("Renkler", "", "ms-settings:colors"),
            ("Yüklü uygulamalar", "", "ms-settings:appsfeatures"), ("Başlangıç uygulamaları", "", "ms-settings:startupapps"),
            ("Bildirimler", "", "ms-settings:notifications"), ("Güç ve pil", "", "ms-settings:powersleep"),
            ("Depolama", "", "ms-settings:storagesense"), ("Tarih ve saat", "", "ms-settings:dateandtime"),
            ("Dil ve bölge", "", "ms-settings:regionlanguage"), ("Fare", "", "ms-settings:mousetouchpad"),
            ("Klavye", "", "ms-settings:typing"), ("Yazıcılar ve tarayıcılar", "", "ms-settings:printers"),
            ("Gizlilik ve güvenlik", "", "ms-settings:privacy"), ("Hesaplar", "", "ms-settings:accounts"),
            ("Ağ ve internet", "", "ms-settings:network"), ("Varsayılan uygulamalar", "", "ms-settings:defaultapps"),
            ("Görev Yöneticisi", "", "taskmgr.exe"), ("Denetim Masası", "", "control.exe"),
        };

        void OnSearch()
        {
            var q = search.Text.Trim();
            placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (q.Length == 0) { Page(page); return; }
            BuildDots();
            sectionTitle.Text = "Sonuçlar";
            allBtn.Visibility = Visibility.Hidden;

            var tr = new System.Globalization.CultureInfo("tr-TR");
            int Score(string name)
            {
                var n = name.ToLower(tr); var k = q.ToLower(tr);
                if (n.StartsWith(k)) return 0;
                if (n.Split(' ', '-', '.').Any(w => w.StartsWith(k))) return 1;
                if (n.Contains(k)) return 2;
                // harfler sirayla geciyor mu ("vsc" -> Visual Studio Code)
                int i = 0; foreach (var ch in n) if (i < k.Length && ch == k[i]) i++;
                return i == k.Length && k.Length >= 2 ? 3 : 99;
            }
            var found = apps.Select(a => (a, s: Score(a.Name))).Where(x => x.s < 99).OrderBy(x => x.s).ThenBy(x => x.a.Name.Length).Select(x => x.a).Take(8).ToList();
            var sets = Settings.Select(st => (st, s: Score(st.name))).Where(x => x.s < 99).OrderBy(x => x.s).Take(4)
                .Select(x => new StartApp { Name = x.st.name, Id = x.st.glyph, Kind = "Ayar", Run = () => Shell(x.st.uri) }).ToList();
            var web = new StartApp { Name = $"\"{q}\" için web'de ara", Id = "", Kind = "Varsayılan tarayıcıda açılır", Run = () => Shell("https://www.google.com/search?q=" + Uri.EscapeDataString(q)) };
            var files = new StartApp { Name = $"\"{q}\" için dosyalarda ara", Id = "", Kind = "Dosya Gezgini araması", Run = () => Shell("explorer.exe", "\"search-ms:query=" + q.Replace("\"", "") + "&crumb=location:" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\"") };

            results.Children.Clear();
            shown.Clear();
            void Section(string title, IEnumerable<StartApp> items)
            {
                var list = items.ToList();
                if (list.Count == 0) return;
                var h = Text(title, Theme.Caption, Theme.TextTertiary, FontWeights.SemiBold);
                h.Margin = new Thickness(12, shown.Count == 0 ? 0 : 12, 0, 4);
                results.Children.Add(h);
                foreach (var a in list) { var r = Row(a, true); results.Children.Add(r); shown.Add((a, r)); }
            }
            Section("Uygulamalar", found);
            Section("Ayarlar", sets);
            Section("Diğer", new[] { files, web });
            sel = 0;
            Highlight();
            Swap(resultsView);
        }

        void Highlight()
        {
            for (int i = 0; i < shown.Count; i++)
            {
                var b = shown[i].row;
                bool on = i == sel;
                // secili satir: aydinlik yuzey + ince vurgu kenari (Enter ile acilacak olan)
                b.Background = Theme.Ink(on ? Theme.SurfaceSelected : (byte)0);
                b.Tag = on ? Theme.SurfaceSelected : (byte)0;
                b.BorderBrush = on ? Theme.Brush(Theme.AccentMuted) : (Brush)Brushes.Transparent;
                if (on) b.BringIntoView();
            }
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && powerMenu.IsVisible) { powerMenu.Visibility = Visibility.Collapsed; e.Handled = true; return; }
            // sabitlenenler arasinda ok tuslariyla gezinme (arama bosken)
            if (home.Visibility == Visibility.Visible && search.Text.Length == 0 && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down))
            {
                var tiles = pinGrid.Children.OfType<Border>().Where(b => b.Focusable).ToList();
                if (tiles.Count == 0) return;
                int i = tiles.FindIndex(b => b.IsKeyboardFocused);
                if (i < 0) { if (e.Key == Key.Down) { tiles[0].Focus(); e.Handled = true; } return; }
                int cols = pinGrid.Columns;
                int next = e.Key == Key.Left ? i - 1 : e.Key == Key.Right ? i + 1 : e.Key == Key.Up ? i - cols : i + cols;
                if (e.Key == Key.Up && next < 0) { search.Focus(); e.Handled = true; return; }
                if (next >= 0 && next < tiles.Count) tiles[next].Focus();
                e.Handled = true;
                return;
            }
            if (shown.Count == 0 || resultsView.Visibility != Visibility.Visible) return;
            if (e.Key == Key.Down) { sel = Math.Min(shown.Count - 1, sel + 1); Highlight(); e.Handled = true; }
            else if (e.Key == Key.Up) { sel = Math.Max(0, sel - 1); Highlight(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Run(shown[sel].app.Run); e.Handled = true; }
        }

        // ---------------- guc / yardimcilar ----------------
        void TogglePower()
        {
            bool show = powerMenu.Visibility != Visibility.Visible;
            powerMenu.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) powerMenu.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Theme.D(Theme.Fast)));
        }

        Border PowerItem(string glyph, string text, Action a)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 9, 12, 9) };
            sp.Children.Add(Icon(glyph, 14, Theme.TextSecondary));
            var t = Text(text, Theme.Body, Theme.TextPrimary); t.Margin = new Thickness(12, 0, 0, 0);
            sp.Children.Add(t);
            return Clickable(sp, () => { powerMenu.Visibility = Visibility.Collapsed; Run(a); }, Theme.RadiusSm);
        }

        Border Round(string glyph, string tip, Action a)
        {
            var b = Clickable(new Border { Width = 52, Height = 52, Child = Icon(glyph, 18) }, a, 26, Theme.SurfaceIdle);
            b.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.Margin = new Thickness(10, 0, 0, 0);
            return b;
        }

        void Run(Action a)
        {
            HideFlyout();
            try { a(); } catch (Exception ex) { Program.Log(ex); }
        }

        static void Shell(string file, string args = null)
        {
            try { Process.Start(new ProcessStartInfo(file) { Arguments = args ?? "", UseShellExecute = true }); } catch { }
        }

        static string UserName()
        {
            try
            {
                var sb = new System.Text.StringBuilder(256); uint n = 256;
                if (GetUserNameEx(3 /*NameDisplay*/, sb, ref n) && sb.Length > 0) return sb.ToString();
            }
            catch { }
            return Environment.UserName;
        }

        [DllImport("secur32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserNameEx(int fmt, System.Text.StringBuilder name, ref uint size);
        [DllImport("user32.dll")] static extern bool LockWorkStation();
        [DllImport("user32.dll")] static extern bool ExitWindowsEx(uint flags, uint reason);
        [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
    }
}