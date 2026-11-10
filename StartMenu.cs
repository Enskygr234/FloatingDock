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
        public string Name = "", Id = "", Kind = L.T("Uygulama", "App");
        public string IconPath;            // ikon icin kabuk yolu
        public Action Run;
    }

    /// <summary>Kendi Baslat menumuz: arama, sabitlenenler, son kullanilanlar, tum uygulamalar, guc.</summary>
    class StartMenu : Flyout
    {
        static readonly string PinFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatingDock", "start-pins.txt");
        const double ContentHeight = 470;

        readonly TextBox search = new TextBox();
        readonly TextBlock placeholder = Text(L.T("Uygulama, ayar veya dosya ara", "Search apps, settings and files"), Theme.BodyLarge, Theme.TextTertiary);
        readonly Grid middle = new Grid();
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
        readonly TextBlock allBtnText = Text(L.T("Tüm uygulamalar", "All apps"), Theme.Body, Theme.TextPrimary);
        readonly TextBlock allBtnIcon = Icon("", 10, Theme.TextSecondary);

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
            var name = Program.Demo ? L.T("Kullanıcı", "User") : UserName();

            // ---- ust: selamlama + HUD saat + avatar ----
            var head = new Grid { Margin = new Thickness(4, 0, 0, 16) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            greeting.FontFamily = Theme.Display;
            hl.Children.Add(greeting);
            hud.Margin = new Thickness(1, 2, 0, 0);
            hl.Children.Add(hud);
            head.Children.Add(hl);
            // avatar: duz vurgu dairesi, koyu bas harf
            var ring = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Theme.Brush(Theme.Accent),
                Child = new TextBlock { Text = name.Substring(0, 1).ToUpper(L.Culture), Foreground = Theme.Brush(Theme.OnAccent), FontSize = Theme.Subtitle, FontWeight = FontWeights.SemiBold, FontFamily = Theme.Ui, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            var ringBtn = Clickable(new Border { Padding = new Thickness(4), Child = ring }, () => Run(() => Shell("ms-settings:accounts")), 24);
            ringBtn.ToolTip = name + L.T(" — hesap ayarları", " — account settings");
            System.Windows.Automation.AutomationProperties.SetName(ringBtn, L.T("Hesap ayarları", "Account settings"));
            Grid.SetColumn(ringBtn, 1); head.Children.Add(ringBtn);
            stack.Children.Add(head);
            userName = name;

            // ---- arama: parlayan hap ----
            search.FontSize = Theme.BodyLarge; search.FontFamily = Theme.Ui; search.Foreground = Theme.Fg(); search.CaretBrush = Theme.Fg();
            search.Background = Brushes.Transparent; search.BorderThickness = new Thickness(0); search.Padding = new Thickness(0, 10, 0, 10);
            search.SelectionBrush = Theme.Brush(Accent);
            System.Windows.Automation.AutomationProperties.SetName(search, L.T("Ara", "Search"));
            search.VerticalContentAlignment = VerticalAlignment.Center;
            search.TextChanged += (s, e) => OnSearch();
            placeholder.IsHitTestVisible = false;
            var sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.ColumnDefinitions.Add(new ColumnDefinition());
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var mag = Icon("", 14, Theme.TextSecondary); mag.Margin = new Thickness(2, 0, 12, 0);
            sg.Children.Add(mag);
            var sbox = new Grid(); sbox.Children.Add(search); sbox.Children.Add(placeholder);
            Grid.SetColumn(sbox, 1); sg.Children.Add(sbox);
            // klavye ipucu: yalnizca yazarken gorunur
            var hint = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), VerticalAlignment = VerticalAlignment.Center,
                BorderBrush = Theme.Ink(Theme.StrokeDefault), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed,
                Child = new TextBlock { Text = "Enter", FontFamily = Theme.Ui, FontSize = Theme.Caption - 0.5, Foreground = Theme.Fg(Theme.TextTertiary) }
            };
            search.TextChanged += (s, e) => hint.Visibility = search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(hint, 2); sg.Children.Add(hint);
            // arama kutusu: koyu dolgu, ince kenar; odaktayken kenar vurgu rengine doner (parilti yok)
            var searchPill = new Border
            {
                CornerRadius = new CornerRadius(Theme.RadiusLg),
                Background = Theme.Brush(Theme.Field),
                BorderBrush = Theme.Ink(Theme.StrokeDefault),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 0, 10, 0),
                Margin = new Thickness(0, 0, 0, 20),
                Child = sg
            };
            search.GotKeyboardFocus += (s, e) => searchPill.BorderBrush = Theme.Brush(Accent);
            search.LostKeyboardFocus += (s, e) => searchPill.BorderBrush = Theme.Ink(Theme.StrokeDefault);
            stack.Children.Add(searchPill);

            // ---- bolum basligi + "Tum uygulamalar" cipi ----
            var secHead = new Grid { Margin = new Thickness(4, 0, 0, 8) };
            secHead.Children.Add(SectionTitle(L.T("Sabitlenenler", "Pinned"), out sectionTitle));
            sectionTitle.Foreground = Theme.Fg();
            var ab = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 5, 10, 5) };
            ab.Children.Add(allBtnText); ab.Children.Add(new Border { Width = 8 }); ab.Children.Add(allBtnIcon);
            allBtn = Clickable(ab, () => ShowAll(!showingAll), Theme.RadiusSm, Theme.SurfaceIdle, Theme.SurfaceHover);
            allBtn.BorderBrush = Theme.Ink(Theme.StrokeSubtle);
            allBtn.HorizontalAlignment = HorizontalAlignment.Right;
            secHead.Children.Add(allBtn);
            stack.Children.Add(secHead);

            home.Children.Add(pinGrid);
            var recHead = SectionTitle(L.T("Son kullanılanlar", "Recent"), out var recTitle);
            recTitle.Foreground = Theme.Fg();
            ((FrameworkElement)recHead).Margin = new Thickness(4, 14, 0, 8);
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
            status.Margin = new Thickness(4, 0, 0, 0);
            foot.Children.Add(status);
            var quick = new StackPanel { Orientation = Orientation.Horizontal };
            quick.Children.Add(Round("", L.T("Dosya Gezgini", "File Explorer"), () => Run(() => Shell("explorer.exe"))));
            quick.Children.Add(Round("", L.T("Görev Yöneticisi", "Task Manager"), () => Run(() => Shell("taskmgr.exe"))));
            quick.Children.Add(Round("", L.T("Ayarlar", "Settings"), () => Run(() => Shell("ms-settings:"))));
            var pw = Round("", L.T("Güç", "Power"), () => TogglePower());
            quick.Children.Add(pw);
            Grid.SetColumn(quick, 1); foot.Children.Add(quick);
            stack.Children.Add(Divider(14, 10));
            stack.Children.Add(foot);

            // ---- guc menusu (sag altta acilan kucuk panel) ----
            var pm = new StackPanel { Margin = new Thickness(6) };
            pm.Children.Add(PowerItem("", L.T("Kilitle", "Lock"), () => LockWorkStation()));
            pm.Children.Add(PowerItem("", L.T("Uyku", "Sleep"), () => SetSuspendState(false, false, false)));
            pm.Children.Add(PowerItem("", L.T("Oturumu kapat", "Sign out"), () => ExitWindowsEx(0, 0)));
            pm.Children.Add(PowerItem("", L.T("Yeniden başlat", "Restart"), () => Shell("shutdown.exe", "/r /t 0")));
            pm.Children.Add(PowerItem("", L.T("Bilgisayarı kapat", "Shut down"), () => Shell("shutdown.exe", "/s /t 0")));
            powerMenu = new Border
            {
                Width = 220,
                CornerRadius = new CornerRadius(Theme.RadiusLg),
                Background = Theme.Brush(Theme.Raised),
                BorderBrush = Theme.Ink(Theme.StrokeDefault),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 48),
                Visibility = Visibility.Collapsed,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.4 },
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
        readonly TextBlock greeting = Text("", Theme.Title, Theme.TextPrimary, FontWeights.SemiBold);
        readonly TextBlock hud = Text("", Theme.Body, Theme.TextSecondary);
        readonly TextBlock status = Text("", Theme.Caption, Theme.TextTertiary);
        readonly DispatcherTimer clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        string userName = "";

        void UpdateHeader()
        {
            var tr = L.Culture;
            var n = DateTime.Now;
            string hi = n.Hour >= 5 && n.Hour < 12 ? L.T("Günaydın", "Good morning") : n.Hour < 18 && n.Hour >= 12 ? L.T("İyi günler", "Good afternoon") : n.Hour >= 18 && n.Hour < 22 ? L.T("İyi akşamlar", "Good evening") : L.T("İyi geceler", "Good night");
            greeting.Text = hi + ", " + userName;
            hud.Text = L.Cap(n.ToString(L.En ? "dddd, MMMM d" : "dddd, d MMMM", tr)) + "  ·  " + n.ToString("HH:mm", tr);
            status.Text = apps.Count > 0 ? apps.Count + L.T(" uygulama yüklü", " apps installed") : "";
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
            Stagger(pinGrid.Children, 12, 40);
            Stagger(recentGrid.Children, 20, 160);
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
                                     .OrderBy(a => a.Name, StringComparer.Create(L.Culture, true)).ToList());
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
            // Win11 duzeni: cercevesiz ikon + alti etiket; tum kutu uzerine gelince aydinlanir, ikon hafifce yukselir
            var lift = new TranslateTransform();
            var icon = new Border
            {
                Width = 40, Height = 40, Child = AppImage(a, 32), HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransform = lift
            };
            var p = new StackPanel { Margin = new Thickness(4, 10, 4, 8) };
            p.Children.Add(icon);
            var t = Text(a.Name, Theme.Caption + 0.5, Theme.TextPrimary);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextAlignment = TextAlignment.Center;
            t.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(t);
            var b = Clickable(p, () => Run(a.Run), Theme.RadiusMd);
            b.Height = 88;
            b.Margin = new Thickness(2);
            b.ToolTip = a.Name;
            System.Windows.Automation.AutomationProperties.SetName(b, a.Name);
            b.ContextMenu = ItemMenu(a);
            void Hover(bool on) => lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(on && Theme.Motion ? -2 : 0, Theme.D(Theme.Normal)) { EasingFunction = Theme.EaseOut });
            b.MouseEnter += (s, e) => Hover(true);
            b.MouseLeave += (s, e) => Hover(false);
            return b;
        }

        ContextMenu ItemMenu(StartApp a)
        {
            if (a.Kind != L.T("Uygulama", "App")) return null;
            var m = new ContextMenu();
            var pin = new MenuItem();
            m.Opened += (s, e) => pin.Header = pins != null && pins.Contains(a.Id) ? L.T("Başlangıçtan kaldır", "Unpin from Start") : L.T("Başlangıca sabitle", "Pin to Start");
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
                    if (items.Count == 4) break;
                }
            }
            catch { }
            if (items.Count == 0)
            {
                var e = Text(L.T("Henüz yeni bir dosya açılmadı.", "No recent files yet."), Theme.Body, Theme.TextTertiary);
                e.Margin = new Thickness(4, 4, 0, 0);
                recentGrid.Children.Add(e);
                return;
            }
            foreach (var f in items)
            {
                var g = new Grid { Margin = new Thickness(10, 8, 10, 8) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var a = new StartApp { IconPath = f.FullName };
                g.Children.Add(new Border { Width = 32, Height = 32, Child = AppImage(a, 28) });
                var tp = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                tp.Children.Add(Text(Path.GetFileNameWithoutExtension(f.Name), Theme.Body, Theme.TextPrimary));
                tp.Children.Add(Text(Ago(f.LastWriteTime), Theme.Caption, Theme.TextTertiary));
                Grid.SetColumn(tp, 1); g.Children.Add(tp);
                var path = f.FullName;
                var card = Clickable(g, () => Run(() => Shell(path)), Theme.RadiusMd);
                card.Margin = new Thickness(2);
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
            if (d.TotalMinutes < 1) return L.T("Az önce", "Just now");
            if (d.TotalHours < 1) return (int)d.TotalMinutes + L.T(" dk önce", " min ago");
            if (d.TotalDays < 1) return (int)d.TotalHours + L.T(" saat önce", " h ago");
            if (d.TotalDays < 2) return L.T("Dün", "Yesterday");
            return t.ToString(L.En ? "MMMM d" : "d MMMM", L.Culture);
        }

        void ShowAll(bool on)
        {
            showingAll = on;
            allBtnText.Text = on ? L.T("Geri", "Back") : L.T("Tüm uygulamalar", "All apps");
            allBtnIcon.Text = on ? "" : "";
            if (on)
            {
                // L.T("Geri", "Back") yazisi solda ok ile
                var sp = (StackPanel)allBtn.Child;
                sp.Children.Clear(); sp.Children.Add(allBtnIcon); sp.Children.Add(new Border { Width = 8 }); sp.Children.Add(allBtnText);
                BuildAll();
            }
            else
            {
                var sp = (StackPanel)allBtn.Child;
                sp.Children.Clear(); sp.Children.Add(allBtnText); sp.Children.Add(new Border { Width = 8 }); sp.Children.Add(allBtnIcon);
            }
            sectionTitle.Text = on ? L.T("Tüm uygulamalar", "All apps") : L.T("Sabitlenenler", "Pinned");
            Swap(on ? (UIElement)allView : home);
        }

        void Swap(UIElement show)
        {
            // ana gorunum icerigi kadar yer kaplar; liste/sonuc gorunumleri sabit yukseklikte kaydirilir
            middle.Height = show == home ? double.NaN : ContentHeight;
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
            var tr = L.Culture;
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
            (L.T("Wi-Fi ayarları", "Wi-Fi settings"), "", "ms-settings:network-wifi"), (L.T("Bluetooth ve cihazlar", "Bluetooth & devices"), "", "ms-settings:bluetooth"),
            (L.T("Ekran ayarları", "Display settings"), "", "ms-settings:display"), (L.T("Ses ayarları", "Sound settings"), "", "ms-settings:sound"),
            ("Windows Update", "", "ms-settings:windowsupdate"), (L.T("Kişiselleştirme", "Personalization"), "", "ms-settings:personalization"),
            (L.T("Arka plan / duvar kağıdı", "Background / wallpaper"), "", "ms-settings:personalization-background"), (L.T("Renkler", "Colors"), "", "ms-settings:colors"),
            (L.T("Yüklü uygulamalar", "Installed apps"), "", "ms-settings:appsfeatures"), (L.T("Başlangıç uygulamaları", "Startup apps"), "", "ms-settings:startupapps"),
            (L.T("Bildirimler", "Notifications"), "", "ms-settings:notifications"), (L.T("Güç ve pil", "Power & battery"), "", "ms-settings:powersleep"),
            (L.T("Depolama", "Storage"), "", "ms-settings:storagesense"), (L.T("Tarih ve saat", "Date & time"), "", "ms-settings:dateandtime"),
            (L.T("Dil ve bölge", "Language & region"), "", "ms-settings:regionlanguage"), (L.T("Fare", "Mouse"), "", "ms-settings:mousetouchpad"),
            (L.T("Klavye", "Keyboard"), "", "ms-settings:typing"), (L.T("Yazıcılar ve tarayıcılar", "Printers & scanners"), "", "ms-settings:printers"),
            (L.T("Gizlilik ve güvenlik", "Privacy & security"), "", "ms-settings:privacy"), (L.T("Hesaplar", "Accounts"), "", "ms-settings:accounts"),
            (L.T("Ağ ve internet", "Network & internet"), "", "ms-settings:network"), (L.T("Varsayılan uygulamalar", "Default apps"), "", "ms-settings:defaultapps"),
            (L.T("Görev Yöneticisi", "Task Manager"), "", "taskmgr.exe"), (L.T("Denetim Masası", "Control Panel"), "", "control.exe"),
        };

        void OnSearch()
        {
            var q = search.Text.Trim();
            placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (q.Length == 0) { sectionTitle.Text = showingAll ? L.T("Tüm uygulamalar", "All apps") : L.T("Sabitlenenler", "Pinned"); Swap(showingAll ? (UIElement)allView : home); allBtn.Visibility = Visibility.Visible; return; }
            sectionTitle.Text = L.T("Sonuçlar", "Results");
            allBtn.Visibility = Visibility.Hidden;

            var tr = L.Culture;
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
                .Select(x => new StartApp { Name = x.st.name, Id = x.st.glyph, Kind = L.T("Ayar", "Setting"), Run = () => Shell(x.st.uri) }).ToList();
            var web = new StartApp { Name = L.En ? $"Search the web for \"{q}\"" : $"\"{q}\" için web'de ara", Id = "", Kind = L.T("Varsayılan tarayıcıda açılır", "Opens in your default browser"), Run = () => Shell("https://www.google.com/search?q=" + Uri.EscapeDataString(q)) };
            var files = new StartApp { Name = L.En ? $"Search files for \"{q}\"" : $"\"{q}\" için dosyalarda ara", Id = "", Kind = L.T("Dosya Gezgini araması", "File Explorer search"), Run = () => Shell("explorer.exe", "\"search-ms:query=" + q.Replace("\"", "") + "&crumb=location:" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\"") };

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
            Section(L.T("Uygulamalar", "Apps"), found);
            Section(L.T("Ayarlar", "Settings"), sets);
            Section(L.T("Diğer", "Other"), new[] { files, web });
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
            var b = IconButton(glyph, tip, a, 36, 15);
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