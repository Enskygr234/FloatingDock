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

    /// <summary>Kendi Baslat menumuz: arama, sabitlenenler, son kullanilanlar, tum uygulamalar, guc.</summary>
    class StartMenu : Flyout
    {
        static readonly string PinFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatingDock", "start-pins.txt");
        const double ContentHeight = 470;

        readonly TextBox search = new TextBox();
        readonly TextBlock placeholder = Text("Uygulama, ayar veya dosya ara", 14, 120);
        readonly Grid middle = new Grid { Height = ContentHeight };
        readonly StackPanel home = new StackPanel();
        readonly UniformGrid pinGrid = new UniformGrid { Columns = 6 };
        readonly UniformGrid recentGrid = new UniformGrid { Columns = 2 };
        readonly StackPanel allList = new StackPanel();
        readonly ScrollViewer allView;
        readonly StackPanel results = new StackPanel();
        readonly ScrollViewer resultsView;
        readonly Border powerMenu;
        readonly Border allBtn;
        readonly TextBlock sectionTitle;
        readonly TextBlock allBtnText = Text("Tüm uygulamalar", 12.5, 230);
        readonly TextBlock allBtnIcon = Icon("", 10, 230);

        List<StartApp> apps = new List<StartApp>();
        List<string> pins;
        List<(StartApp app, Border row)> shown = new List<(StartApp, Border)>();
        int sel;
        bool showingAll;

        public StartMenu() : base("FloatingDockStart")
        {
            Width = 660;
            var root = new Grid();
            var stack = new StackPanel();
            root.Children.Add(stack);
            var name = UserName();

            // ---- ust: selamlama + HUD saat + avatar ----
            var head = new Grid { Margin = new Thickness(6, 2, 4, 16) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            greeting.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            hl.Children.Add(greeting);
            hud.Margin = new Thickness(1, 3, 0, 0);
            hl.Children.Add(hud);
            head.Children.Add(hl);
            var ring = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Padding = new Thickness(2), Background = AccentGradient(),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 },
                Child = new Border
                {
                    CornerRadius = new CornerRadius(20), Background = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x2C)),
                    Child = new TextBlock { Text = name.Substring(0, 1).ToUpperInvariant(), Foreground = AccentGradient(), FontSize = 17, FontWeight = FontWeights.Bold, FontFamily = UiFont, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            var ringBtn = Clickable(ring, () => Run(() => Shell("ms-settings:accounts")), 22, 0, 0);
            ringBtn.Background = Brushes.Transparent;
            ringBtn.ToolTip = name + " — hesap ayarları";
            Grid.SetColumn(ringBtn, 1); head.Children.Add(ringBtn);
            stack.Children.Add(head);
            userName = name;

            // ---- arama: parlayan hap ----
            search.FontSize = 15; search.FontFamily = UiFont; search.Foreground = Brushes.White; search.CaretBrush = AccentGradient();
            search.Background = Brushes.Transparent; search.BorderThickness = new Thickness(0); search.Padding = new Thickness(0, 11, 0, 11);
            search.SelectionBrush = new SolidColorBrush(Accent);
            search.VerticalContentAlignment = VerticalAlignment.Center;
            search.TextChanged += (s, e) => OnSearch();
            placeholder.IsHitTestVisible = false;
            var sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.ColumnDefinitions.Add(new ColumnDefinition());
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var mag = Icon("", 15); mag.Foreground = AccentGradient(); mag.Margin = new Thickness(4, 0, 14, 0);
            sg.Children.Add(mag);
            var sbox = new Grid(); sbox.Children.Add(search); sbox.Children.Add(placeholder);
            Grid.SetColumn(sbox, 1); sg.Children.Add(sbox);
            var hint = new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 3), VerticalAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = "⏎ AÇ", FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)) }
            };
            Grid.SetColumn(hint, 2); sg.Children.Add(hint);
            var searchPill = new Border
            {
                CornerRadius = new CornerRadius(24),
                Background = new SolidColorBrush(Color.FromArgb(110, 4, 4, 16)),
                BorderBrush = Holo(220),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 0, 10, 0),
                Margin = new Thickness(0, 0, 0, 20),
                Child = sg
            };
            var glow = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x6F, 0xB8, 0xFF), BlurRadius = 22, ShadowDepth = 0, Opacity = 0 };
            searchPill.Effect = glow;
            search.GotKeyboardFocus += (s, e) => glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(0.55, TimeSpan.FromMilliseconds(250)));
            search.LostKeyboardFocus += (s, e) => glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
            stack.Children.Add(searchPill);

            // ---- bolum basligi + "Tum uygulamalar" cipi ----
            var secHead = new Grid { Margin = new Thickness(6, 0, 0, 10) };
            secHead.Children.Add(HudTitle("Sabitlenenler", out sectionTitle));
            var ab = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 5, 10, 5) };
            ab.Children.Add(allBtnText); ab.Children.Add(new Border { Width = 8 }); ab.Children.Add(allBtnIcon);
            allBtn = Clickable(ab, () => ShowAll(!showingAll), 13, 16, 36);
            allBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0x8A, 0x7C, 0xFF));
            allBtn.BorderThickness = new Thickness(1);
            allBtn.HorizontalAlignment = HorizontalAlignment.Right;
            secHead.Children.Add(allBtn);
            stack.Children.Add(secHead);

            home.Children.Add(pinGrid);
            var recHead = HudTitle("Son kullanılanlar");
            ((FrameworkElement)recHead).Margin = new Thickness(6, 16, 0, 10);
            home.Children.Add(recHead);
            home.Children.Add(recentGrid);
            middle.Children.Add(home);

            allView = new ScrollViewer { Content = allList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Visibility = Visibility.Collapsed, PanningMode = PanningMode.VerticalOnly };
            middle.Children.Add(allView);
            resultsView = new ScrollViewer { Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Visibility = Visibility.Collapsed };
            middle.Children.Add(resultsView);
            stack.Children.Add(middle);

            // ---- alt serit: cam serit icinde durum + hizli dugmeler ----
            var foot = new Grid();
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            status.Margin = new Thickness(14, 0, 0, 0);
            foot.Children.Add(status);
            var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 5, 5) };
            quick.Children.Add(Round("", "Dosya Gezgini", () => Run(() => Shell("explorer.exe"))));
            quick.Children.Add(Round("", "Görev Yöneticisi", () => Run(() => Shell("taskmgr.exe"))));
            quick.Children.Add(Round("", "Ayarlar", () => Run(() => Shell("ms-settings:"))));
            var pw = Round("", "Güç", () => TogglePower());
            pw.MouseEnter += (s, e) => pw.Background = new LinearGradientBrush(Color.FromArgb(200, 0xFF, 0x5A, 0x7A), Color.FromArgb(200, 0xC0, 0x4C, 0xF0), 45);
            pw.MouseLeave += (s, e) => pw.Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            quick.Children.Add(pw);
            Grid.SetColumn(quick, 1); foot.Children.Add(quick);
            stack.Children.Add(new Border
            {
                Margin = new Thickness(0, 16, 0, 0),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = foot
            });

            // ---- guc menusu (sag altta acilan kucuk panel) ----
            var pm = new StackPanel { Margin = new Thickness(6) };
            pm.Children.Add(PowerItem("", "Kilitle", () => LockWorkStation()));
            pm.Children.Add(PowerItem("", "Uyku", () => SetSuspendState(false, false, false)));
            pm.Children.Add(PowerItem("", "Oturumu kapat", () => ExitWindowsEx(0, 0)));
            pm.Children.Add(PowerItem("", "Yeniden başlat", () => Shell("shutdown.exe", "/r /t 0")));
            pm.Children.Add(PowerItem("", "Bilgisayarı kapat", () => Shell("shutdown.exe", "/s /t 0")));
            powerMenu = new Border
            {
                Width = 220,
                CornerRadius = new CornerRadius(16),
                Background = new LinearGradientBrush(Color.FromArgb(250, 0x1E, 0x1A, 0x3C), Color.FromArgb(250, 0x10, 0x10, 0x24), 90),
                BorderBrush = Holo(160),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 4, 60),
                Visibility = Visibility.Collapsed,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Accent, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.45 },
                Child = pm
            };
            root.Children.Add(powerMenu);
            root.MouseLeftButtonDown += (s, e) => { if (powerMenu.IsVisible && !powerMenu.IsMouseOver) powerMenu.Visibility = Visibility.Collapsed; };

            SetBody(root);
            PreviewKeyDown += OnKey;
            // uygulama listesini ve ikonlari simdiden yukle: ilk acilista bos gorunmesin
            Dispatcher.BeginInvoke(new Action(() => _ = LoadAsync()), DispatcherPriority.ApplicationIdle);
            PreviewTextInput += (s, e) => { if (!search.IsKeyboardFocused) { search.Focus(); } };
            clockTimer.Tick += (s, e) => UpdateHeader();
        }

        // ---- ust bilgi ----
        readonly TextBlock greeting = Text("", 24, 255, FontWeights.Light);
        readonly TextBlock hud = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(160, 170, 200, 255)) };
        readonly TextBlock status = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromArgb(130, 200, 210, 255)), VerticalAlignment = VerticalAlignment.Center };
        readonly DispatcherTimer clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        string userName = "";

        void UpdateHeader()
        {
            var tr = new System.Globalization.CultureInfo("tr-TR");
            var n = DateTime.Now;
            string hi = n.Hour >= 5 && n.Hour < 12 ? "Günaydın" : n.Hour < 18 && n.Hour >= 12 ? "İyi günler" : n.Hour >= 18 && n.Hour < 22 ? "İyi akşamlar" : "İyi geceler";
            greeting.Text = hi + ", " + userName;
            hud.Text = n.ToString("dddd · d MMMM yyyy · HH:mm:ss", tr).ToUpper(tr);
            status.Text = "● ÇEVRİMİÇİ  ·  " + apps.Count + " UYGULAMA";
        }

        // ---------------- acilis ----------------
        protected override void OnOpening()
        {
            powerMenu.Visibility = Visibility.Collapsed;
            search.Text = "";
            ShowAll(false);
            if (apps.Count > 0) _ = RefreshAppsAsync();
            BuildRecent();
            UpdateHeader(); clockTimer.Start();
            Stagger(pinGrid.Children, 18, 80);
            Stagger(recentGrid.Children, 30, 260);
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
            while (pinGrid.Children.Count < 18) pinGrid.Children.Add(new Border { Height = 84 });
        }

        UIElement Tile(StartApp a)
        {
            // ikon, cam bir "squircle" icinde; uzerine gelince yukselir ve parlar
            var lift = new TranslateTransform();
            var grow = new ScaleTransform(1, 1);
            var shine = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x7C, 0x9C, 0xFF), BlurRadius = 20, ShadowDepth = 0, Opacity = 0 };
            var squircle = new Border
            {
                Width = 50, Height = 50, CornerRadius = new CornerRadius(15),
                Background = new LinearGradientBrush(Color.FromArgb(34, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), 90),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), BorderThickness = new Thickness(1),
                Child = AppImage(a, 30), HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new TransformGroup { Children = { grow, lift } },
                Effect = shine
            };
            var p = new StackPanel { Margin = new Thickness(2, 8, 2, 6) };
            p.Children.Add(squircle);
            var t = Text(a.Name, 11.5, 215);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextAlignment = TextAlignment.Center;
            t.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(t);
            var b = Clickable(p, () => Run(a.Run), 14, 0, 12);
            b.Height = 92;
            b.ToolTip = a.Name;
            b.ContextMenu = ItemMenu(a);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            void Hover(bool on)
            {
                var d = TimeSpan.FromMilliseconds(on ? 180 : 240);
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(on ? -4 : 0, d) { EasingFunction = ease });
                grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(on ? 1.08 : 1, d) { EasingFunction = ease });
                grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? 1.08 : 1, d) { EasingFunction = ease });
                shine.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(on ? 0.85 : 0, d));
                squircle.BorderBrush = on ? Holo(230) : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                t.Foreground = new SolidColorBrush(Color.FromArgb((byte)(on ? 255 : 215), 255, 255, 255));
            }
            b.MouseEnter += (s, e) => Hover(true);
            b.MouseLeave += (s, e) => Hover(false);
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

        static UIElement AppImage(StartApp a, double size)
        {
            var img = new Image { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            if (a.IconPath == null) return img;
            var c = ShellIcons.Cached(a.IconPath);
            if (c != null) img.Source = c;
            else
            {
                img.Opacity = 0;
                ShellIcons.GetAsync(a.IconPath).ContinueWith(t =>
                    img.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        img.Source = t.Result;
                        img.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
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
                var dir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.Recent));
                foreach (var f in dir.GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime))
                {
                    var n = Path.GetFileNameWithoutExtension(f.Name);
                    if (!Path.HasExtension(n) || n.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue; // klasor/baglanti degil, dosya
                    if (Junk.Contains(Path.GetExtension(n).TrimStart('.').ToLowerInvariant()) || n.StartsWith(".")) continue;   // teknik dosyalar
                    items.Add(f);
                    if (items.Count == 6) break;
                }
            }
            catch { }
            if (items.Count == 0)
            {
                var e = Text("Henüz yeni bir dosya açılmadı.", 12.5, 140);
                e.Margin = new Thickness(14, 4, 0, 0);
                recentGrid.Children.Add(e);
                return;
            }
            foreach (var f in items)
            {
                var g = new Grid { Margin = new Thickness(10, 8, 10, 8) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var a = new StartApp { IconPath = f.FullName };
                g.Children.Add(new Border
                {
                    Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Child = AppImage(a, 24),
                    Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255))
                });
                var tp = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                tp.Children.Add(Text(Path.GetFileNameWithoutExtension(f.Name), 12.5, 235));
                var when = Text(Ago(f.LastWriteTime), 10.5, 140);
                when.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                tp.Children.Add(when);
                Grid.SetColumn(tp, 1); g.Children.Add(tp);
                var path = f.FullName;
                var card = Clickable(g, () => Run(() => Shell(path)), 14, 10, 28);
                card.Margin = new Thickness(3);
                card.BorderThickness = new Thickness(1);
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
                card.MouseEnter += (s, e) => card.BorderBrush = Holo(140);
                card.MouseLeave += (s, e) => card.BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
                recentGrid.Children.Add(card);
            }
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

        void ShowAll(bool on)
        {
            showingAll = on;
            allBtnText.Text = on ? "Geri" : "Tüm uygulamalar";
            allBtnIcon.Text = on ? "" : "";
            if (on)
            {
                // "Geri" yazisi solda ok ile
                var sp = (StackPanel)allBtn.Child;
                sp.Children.Clear(); sp.Children.Add(allBtnIcon); sp.Children.Add(new Border { Width = 8 }); sp.Children.Add(allBtnText);
                BuildAll();
            }
            else
            {
                var sp = (StackPanel)allBtn.Child;
                sp.Children.Clear(); sp.Children.Add(allBtnText); sp.Children.Add(new Border { Width = 8 }); sp.Children.Add(allBtnIcon);
            }
            sectionTitle.Text = Spaced(on ? "Tüm uygulamalar" : "Sabitlenenler");
            Swap(on ? (UIElement)allView : home);
        }

        void Swap(UIElement show)
        {
            foreach (UIElement c in middle.Children)
            {
                bool v = c == show;
                if (v && c.Visibility != Visibility.Visible)
                {
                    c.Visibility = Visibility.Visible;
                    c.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                    var tt = new TranslateTransform(); c.RenderTransform = tt;
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
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
                // harf: holografik cerceveli kucuk kare + soluk cizgi
                var hg = new Grid { Margin = new Thickness(10, 14, 10, 6) };
                hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                hg.ColumnDefinitions.Add(new ColumnDefinition());
                hg.Children.Add(new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = AccentGradient(),
                    Background = new SolidColorBrush(Color.FromArgb(40, 0x8A, 0x7C, 0xFF)),
                    Child = new TextBlock { Text = g.Key, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });
                var ln = new Border { Height = 1, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = Holo(60) };
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
                ? new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), Child = AppImage(a, 24) }
                : new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(11), Background = AccentGradient(), Opacity = 0.9, Child = Icon(a.Id, 15) });
            var tp = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tp.Children.Add(Text(a.Name, 13.5, 240));
            if (showKind) { var k = Text(Spaced(a.Kind), 9.5, 130); k.FontFamily = new FontFamily("Cascadia Mono, Consolas"); k.Margin = new Thickness(0, 2, 0, 0); tp.Children.Add(k); }
            Grid.SetColumn(tp, 1); g.Children.Add(tp);
            var b = Clickable(g, () => Run(a.Run), 12, 0, 22);
            b.Margin = new Thickness(2, 1, 2, 1);
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
            if (q.Length == 0) { sectionTitle.Text = Spaced(showingAll ? "Tüm uygulamalar" : "Sabitlenenler"); Swap(showingAll ? (UIElement)allView : home); allBtn.Visibility = Visibility.Visible; return; }
            sectionTitle.Text = Spaced("En iyi eşleşmeler");
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
                var h = Text(Spaced(title), 10.5, 140, FontWeights.SemiBold);
                h.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                h.Margin = new Thickness(16, shown.Count == 0 ? 0 : 12, 0, 4);
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
                b.Background = on ? new LinearGradientBrush(Color.FromArgb(90, 0x7C, 0x8C, 0xFF), Color.FromArgb(14, 0xC0, 0x6C, 0xF0), 0) : (Brush)new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
                b.Tag = (byte)(on ? 34 : 0);
                b.BorderThickness = new Thickness(on ? 1 : 0);
                b.BorderBrush = Holo(170);
                if (on) b.BringIntoView();
            }
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && powerMenu.IsVisible) { powerMenu.Visibility = Visibility.Collapsed; e.Handled = true; return; }
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
            if (show) { powerMenu.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))); Stagger(((StackPanel)powerMenu.Child).Children, 30, 0); }
        }

        Border PowerItem(string glyph, string text, Action a)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 9, 12, 9) };
            sp.Children.Add(Icon(glyph, 14));
            var t = Text(text, 13, 235); t.Margin = new Thickness(12, 0, 0, 0);
            sp.Children.Add(t);
            return Clickable(sp, () => { powerMenu.Visibility = Visibility.Collapsed; Run(a); }, 8, 0, 30);
        }

        Border Round(string glyph, string tip, Action a)
        {
            var b = Clickable(new Border { Width = 38, Height = 38, Child = Icon(glyph, 15, 225) }, a, 19, 0, 30);
            b.ToolTip = tip;
            b.Margin = new Thickness(4, 0, 0, 0);
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