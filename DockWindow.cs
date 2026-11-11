using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace FloatingDock
{
    class AppEntry
    {
        public string Key, Name, LaunchPath, IconPath;
        public bool Pinned;
        public string Extra; // surukle-birak ile eklendiyse dosya yolu
        public List<IntPtr> Windows = new List<IntPtr>();
        /// <summary>Ekran basina dock'ta: ayni uygulamanin baska ekranlardaki pencereleri.</summary>
        public List<IntPtr> Elsewhere = new List<IntPtr>();
        /// <summary>Okunmamis rozeti ("3", "•") ya da null.</summary>
        public string Badge;
        /// <summary>Bu uygulamada su an muzik caliyor.</summary>
        public bool Playing;
    }

    class DockWindow : Window
    {
        static readonly Color Accent = Theme.Accent;
        static double ItemSize => Settings.Current.ItemSize;
        const double DockMargin = 10;
        // hapin cevresindeki seffaf pay: ustte buyuyen ikonlar, altta ve yanlarda golge icin
        // hapin cevresindeki seffaf pay: ustte buyuyen ikonlar, yanlarda ve altta kil golgesi
        const double PadTop = 24, PadSide = 28, PadBottom = 26;
        /// <summary>Uygulama kutusunun kenari (ayar: kucuk / orta / buyuk).</summary>
        static double TileSize => ItemSize + 14;
        static double PillRadius => TileSize / 2 + 12;
        const byte VK_LWIN = 0x5B;
        static readonly string StateDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatingDock");
        static readonly string StateFile = System.IO.Path.Combine(StateDir, "taskbar-state.txt");

        IntPtr hwnd, tray;
        int origTrayState = -1;
        bool restored;
        uint callbackMsg, taskbarCreatedMsg;
        int appBarBottomPx;
        double scale = 1;
        StackPanel appsPanel;
        TextBlock timeText, dateText;
        readonly List<AppEntry> pinned = new List<AppEntry>();
        readonly Dictionary<string, ImageSource> iconCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Theme.Pastel> pastelCache = new Dictionary<string, Theme.Pastel>(StringComparer.OrdinalIgnoreCase);
        string lastSig = "";
        static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        readonly PreviewWindow preview;
        StackPanel row;
        // buyume (magnification) animasyonu icin ikonlar
        readonly List<(FrameworkElement el, ScaleTransform mag)> fixedMag = new List<(FrameworkElement, ScaleTransform)>();
        readonly List<(FrameworkElement el, ScaleTransform mag)> appMag = new List<(FrameworkElement, ScaleTransform)>();
        const double MagMax = 0.32, MagSpread = 58;

        /// <summary>Bu dock'un bulundugu ekran.</summary>
        public Screen Screen => screen;
        Screen screen;
        /// <summary>Ana dock (birincil ekran): paneller, Windows tusu ve gorev cubugu ondan yonetilir.</summary>
        readonly bool main;
        readonly List<DispatcherTimer> timers = new List<DispatcherTimer>();
        Action historyHandler, settingsHandler, weatherHandler;
        /// <summary>Bu ekranin ust cubugu (Baslat, ondeki uygulama, saat, durum).</summary>
        TopBar bar;
        /// <summary>Masaustu bilesenleri (yalniz ana dock'ta).</summary>
        DesktopWidgets widgets;

        void SyncWidgets()
        {
            if (!main || !IsLoaded) return;
            if (Settings.Current.DesktopWidgets && widgets == null) { widgets = new DesktopWidgets(this); widgets.Show(); }
            else if (!Settings.Current.DesktopWidgets && widgets != null) { widgets.Close(); widgets = null; }
        }

        public DockWindow(Screen screen, bool main)
        {
            this.screen = screen;
            this.main = main;
            Title = main ? "FloatingDock" : "FloatingDock " + screen.Device;
            // DWM'in sabit 8px koseli dikdortgeni yerine kendi cizdigimiz yuvarlak hap: seffaf, katmanli pencere
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            preview = new PreviewWindow(
                key => currentEntries.FirstOrDefault(x => x.Key == key),
                () => Screens.WindowRect(hwnd).top + PadTop * scale,
                Activate,
                GetIcon,
                () => { lastSig = ""; Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background); });

            LoadPinned();
            Content = BuildUi();
            Width = 400; Height = 54;
            SourceInitialized += OnSourceInitialized;
            bar = new TopBar(this);
            Loaded += (s, e) => { bar.Show(); SyncWidgets(); };
            SizeChanged += (s, e) => Reposition();
            Closed += (s, e) =>
            {
                foreach (var tm in timers) tm.Stop();
                Hub.HistoryChanged -= historyHandler;
                Weather.Changed -= weatherHandler;
                Settings.Current.Changed -= settingsHandler;
                preview.Close();
                bar?.Close();
                widgets?.Close();
                if (!main) return;
                control.Close(); calendar.Close(); start.Close(); settingsFly.Close(); trayFly.Close(); weatherFly.Close(); playerFly.Close(); winKey?.Stop(); RestoreTaskbar();
            };
            if (main)
            {
                settingsFly.SetWinKey = SetWinKey;
                settingsFly.CloseDock = Close;
                // ayarlar kutucugu: hizli ayarlarin acildigi ekranda acilsin
                control.OpenSettings = () => (activeDock ?? this).Toggle(settingsFly, null);
                weatherFly.OpenSettings = () => (activeDock ?? this).Toggle(settingsFly, null);
                // takvimdeki hava durumuna tiklayinca ayrinti paneli ayni ekranda acilsin
                CalendarFlyout.OpenWeather = () => { var d = activeDock ?? this; d.BarOpen("weather", d.bar?.WeatherAnchor); };
                // ada / hizli ayarlar / dock: oynatici farenin oldugu ekranda acilsin
                Hub.OpenPlayer = () => Dispatcher.BeginInvoke(new Action(() => DockManager.AtCursor().BarOpen("player", null)));
                // yeni bildirim / rozet ya da sarki degisti: ikonlari yenile
                Badges.Changed += () => Dispatcher.BeginInvoke(new Action(() => { foreach (var d in DockManager.All) d.Invalidate(); }));
            }
            settingsHandler = () => { if (!Settings.Current.AutoHide && dockHidden) SlideTo(false); SyncWidgets(); };
            Settings.Current.Changed += settingsHandler;

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            t.Tick += (s, e) => Refresh();
            t.Start();
            timers.Add(t);
            Refresh();
        }

        /// <summary>Dock'u baska bir ekrana (ya da degisen cozunurluge) tasir.</summary>
        public void SetScreen(Screen s)
        {
            bool moved = !Screens.Same(s, screen) || s.Bounds.left != screen.Bounds.left || s.Bounds.top != screen.Bounds.top;
            screen = s;
            if (hwnd == IntPtr.Zero) return;
            if (moved)
            {
                // once hedef ekrana al ki WPF oranin DPI'ina gecsin, sonra yerlestir
                Screens.MoveTo(hwnd, s.Work.left + 1, s.Work.top + 1);
                scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                preview.HidePreview();
            }
            Reposition();
            bar?.Place();
            widgets?.Place();
        }

        /// <summary>Bir sonraki yenilemede ikonlari bastan kur.</summary>
        public void Invalidate() { lastSig = ""; }

        // ---------------- Arayuz ----------------
        UIElement BuildUi()
        {
            row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 12, 14, 6), Background = Brushes.Transparent };
            row.MouseMove += (s, e) => { if (!dragging) Magnify(e.GetPosition(row).X); };
            row.PreviewMouseMove += OnDragMove;
            row.PreviewMouseLeftButtonUp += OnDragEnd;
            row.LostMouseCapture += (s, e) => { if (dragging) CancelDrag(); };
            // dosya / uygulama birakinca dock'a sabitle
            row.AllowDrop = true;
            row.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
            row.Drop += (s, e) =>
            {
                if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
                var st = Settings.Current;
                foreach (var f in files) if (!st.DockExtra.Contains(f, StringComparer.OrdinalIgnoreCase)) st.DockExtra.Add(f);
                st.Save();
                DockManager.ReloadPinned();
            };
            row.MouseLeave += (s, e) => { ResetMagnify(); preview.HoverOther(); };
            // kil kabukta Baslat, saat ve durum simgeleri ust cubukta; dock'ta yalniz uygulamalar
            row.Children.Add(MakeItem(AllAppsIcon(), "Tüm uygulamalar", () => Toggle(start, null), 0, tile: Theme.Raised));
            row.Children.Add(Sep());
            appsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(appsPanel);
            row.Children.Add(Sep());
            row.Children.Add(MakeItem(Glyph("", 20), "FloatingDock ayarları", () => Toggle(settingsFly, null), 0, tile: Theme.Raised));
            timeText = new TextBlock(); dateText = new TextBlock();
            weatherHandler = () => { }; historyHandler = () => { };

            // kil hap: yumusak dis golge + dolgu + ust parlama / alt ic golge
            var pill = new Grid { Margin = new Thickness(PadSide, PadTop, PadSide, PadBottom) };
            pill.Children.Add(new Border { CornerRadius = new CornerRadius(PillRadius), Background = Theme.Brush(Theme.DockFill), IsHitTestVisible = false, Effect = Theme.ClayShadow(30, 10) });
            pill.Children.Add(new Border { CornerRadius = new CornerRadius(PillRadius), Background = Theme.Brush(Theme.DockFill) });
            pill.Children.Add(Theme.ClayLight(PillRadius));
            pill.Children.Add(row);
            return new Grid { Background = Brushes.Transparent, Children = { pill }, ContextMenu = BuildMenu() };
        }

        // ---------------- Kendi panellerimiz (hizli ayarlar / takvim) ----------------
        // tum dock'lar ayni panelleri paylasir; panel, tiklanan dock'un ekraninda acilir
        static readonly ControlCenter control = new ControlCenter();
        static readonly CalendarFlyout calendar = new CalendarFlyout();
        static readonly StartMenu start = new StartMenu();
        static readonly SettingsFlyout settingsFly = new SettingsFlyout();
        static readonly TrayFlyout trayFly = new TrayFlyout();
        static readonly WeatherFlyout weatherFly = new WeatherFlyout();
        static readonly PlayerFlyout playerFly = new PlayerFlyout();
        static DockWindow activeDock;
        static WinKeyHook winKey;
        /// <summary>Bu dock'un ekraninda acik bir panel ya da bu dock'un menusu var mi?</summary>
        bool AnyFlyoutOpen => itemMenuOpen || new Flyout[] { control, calendar, start, settingsFly, trayFly, weatherFly, playerFly }.Any(f => f.IsOpen && (f.Screen == null || Screens.Same(f.Screen, screen)));
        bool itemMenuOpen;

        public void Toggle(Flyout f, FrameworkElement anchor)
        {
            preview.HidePreview();
            // ayni panel baska ekranda aciksa kapatma, bu ekrana tasi
            if ((f.IsOpen || f.JustClosed) && (f.Screen == null || Screens.Same(f.Screen, screen))) { f.HideFlyout(); return; }
            foreach (var other in new Flyout[] { control, calendar, start, settingsFly, trayFly, weatherFly, playerFly }) if (other != f) other.HideFlyout();
            activeDock = this;
            if (dockHidden) SlideTo(false);
            // Baslat menusu ekranin ortasinda, digerleri kendi dugmesinin ustunde acilir
            if (f is StartMenu) { f.ShowFull(screen); return; }
            double x = anchor == null ? screen.CenterX : anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0)).X;
            f.ShowAt(screen, x, ShownTopPx() + PadTop * scale);
        }

        /// <summary>Ust cubuktan acilan paneller: cubugun hemen altinda, dugmesinin hizasinda acilir.</summary>
        public void BarOpen(string what, FrameworkElement anchor)
        {
            Flyout f = what switch
            {
                "start" => start, "weather" => weatherFly, "tray" => trayFly, "qs" => control,
                "calendar" => calendar, "player" => playerFly, "settings" => settingsFly, _ => null
            };
            if (f == null) return;
            if (f is StartMenu) { Toggle(f, null); return; }
            preview.HidePreview();
            if ((f.IsOpen || f.JustClosed) && (f.Screen == null || Screens.Same(f.Screen, screen))) { f.HideFlyout(); return; }
            foreach (var other in new Flyout[] { control, calendar, start, settingsFly, trayFly, weatherFly, playerFly }) if (other != f) other.HideFlyout();
            activeDock = this;
            double x = anchor == null || PresentationSource.FromVisual(anchor) == null ? screen.CenterX : anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0)).X;
            f.ShowBelow(screen, x, bar?.BottomPx ?? screen.Bounds.top);
        }

        /// <summary>Bu exe'nin acik pencere sayisi (ust cubuk icin).</summary>
        public static int WindowCountOf(string exe) =>
            string.IsNullOrEmpty(exe) || sharedWins == null ? 0 : sharedWins.Count(w => string.Equals(w.exe, exe, StringComparison.OrdinalIgnoreCase));

        void SetWinKey(bool on)
        {
            WinKeyHook.Enabled = on;
            if (on)
            {
                // Windows tusu: Baslat menusu farenin bulundugu ekranda acilir
                winKey ??= new WinKeyHook(() => Dispatcher.BeginInvoke(new Action(() => DockManager.AtCursor().Toggle(start, null))));
                winKey.Start();
            }
            else winKey?.Stop();
        }

        ContextMenu BuildMenu()
        {
            var m = new ContextMenu();
            void Add(string text, Action a) { var i = new MenuItem { Header = text }; i.Click += (s, e) => a(); m.Items.Add(i); }
            Add("FloatingDock ayarları…", () => Toggle(settingsFly, null));
            m.Items.Add(new Separator());
            Add("Görev Yöneticisi", () => Launch("taskmgr.exe"));
            Add("Dosya Gezgini", () => Launch("explorer.exe"));
            Add("Kişiselleştirme ayarları", () => Launch("ms-settings:personalization"));
            m.Items.Add(new Separator());
            var auto = new MenuItem { Header = "Windows açılışında başlat", IsCheckable = true, IsChecked = AutoStart.IsEnabled };
            auto.Click += (s, e) => { AutoStart.Set(auto.IsChecked); auto.IsChecked = AutoStart.IsEnabled; };
            m.Items.Add(auto);
            var wk = new MenuItem { Header = "Windows tuşu bu Başlat menüsünü açsın", IsCheckable = true, IsChecked = WinKeyHook.Enabled };
            wk.Click += (s, e) => SetWinKey(wk.IsChecked);
            m.Items.Add(wk);
            m.Items.Add(new Separator());
            Add("Dock'u kapat (eski görev çubuğuna dön)", () => DockManager.Main.Close());
            return m;
        }

        void Magnify(double mouseX)
        {
            if (!Theme.Motion || !Settings.Current.Magnify) return; // azaltilmis hareket / ayar kapali: ikonlar buyumez
            foreach (var (el, mag) in fixedMag.Concat(appMag))
            {
                if (PresentationSource.FromVisual(el) == null) continue;
                double cx = el.TranslatePoint(new Point(el.ActualWidth / 2, 0), row).X;
                double d = mouseX - cx;
                double s = 1 + MagMax * Math.Exp(-(d * d) / (2 * MagSpread * MagSpread));
                mag.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                mag.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                mag.ScaleX = s; mag.ScaleY = s;
            }
        }

        void ResetMagnify()
        {
            foreach (var (_, mag) in fixedMag.Concat(appMag))
            {
                var a = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                mag.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                mag.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }

        /// <summary>Dock ogesi: kil kutu (pastel dolgu) icinde ikon; altinda on plan cizgisi ya da calisiyor noktasi.</summary>
        FrameworkElement MakeItem(UIElement content, string tip, Action click, int indicator, string appKey = null, bool hasWindows = false, Color? tile = null)
        {
            double ts = TileSize, r = Math.Round(ts * 0.36);
            var grid = new Grid { Width = ts, Height = ts + 12, Margin = new Thickness(4, 0, 4, 0), Background = Brushes.Transparent, ToolTip = tip };
            System.Windows.Automation.AutomationProperties.SetName(grid, tip);
            var st = new ScaleTransform(1, 1);   // basma efekti (kil: ezilir)
            var mag = new ScaleTransform(1, 1);  // buyume efekti
            var lift = new TranslateTransform(0, indicator == 2 ? -3 : 0); // on plandaki uygulama hafifce yukselir
            var tg = new TransformGroup();
            tg.Children.Add(mag);
            tg.Children.Add(st);
            tg.Children.Add(lift);
            var box = new Grid { Width = ts, Height = ts };
            box.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(r), Background = Theme.Brush(tile ?? Theme.Raised),
                Effect = Theme.ClayShadow(indicator == 2 ? 18 : 12, indicator == 2 ? 7 : 4, indicator == 2 ? (Theme.Dark ? 0.6 : 0.28) : -1)
            });
            box.Children.Add(Theme.ClayLight(r));
            var bg = new Border { CornerRadius = new CornerRadius(r) };   // uzerine gelme / basma katmani
            box.Children.Add(bg);
            box.Children.Add(new ContentPresenter { Content = content, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var holder = new ContentPresenter
            {
                Content = box,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new Point(0.5, 0.85),
                RenderTransform = tg
            };
            grid.Children.Add(holder);
            (appKey != null ? appMag : fixedMag).Add((grid, mag));
            if (appKey != null && hasWindows)
            {
                grid.ToolTip = null; // basliklar onizlemede gorunur
                grid.MouseEnter += (s, e) => preview.HoverApp(appKey, grid);
            }
            else grid.MouseEnter += (s, e) => preview.HoverOther();
            if (indicator > 0)
            {
                grid.Children.Add(new Border
                {
                    Height = indicator == 2 ? 5 : 6,
                    Width = indicator == 2 ? 18 : 6,
                    CornerRadius = new CornerRadius(3),
                    Background = indicator == 2 ? Theme.Brush(Accent) : Theme.Ink(Theme.TextTertiary),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 1)
                });
            }
            Hook(grid, bg, st, click);
            return grid;
        }

        /// <summary>"Tum uygulamalar": dort renkli kucuk kare.</summary>
        static UIElement AllAppsIcon()
        {
            var g = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Width = 22, Height = 22 };
            foreach (var p in new[] { Theme.Pastel.Lilac, Theme.Pastel.Pink, Theme.Pastel.Mint, Theme.Pastel.Butter })
                g.Children.Add(new Border { CornerRadius = new CornerRadius(3.5), Margin = new Thickness(1.5), Background = Theme.Brush(Theme.Dark ? Theme.PastelColors(p).ink : Theme.PastelColors(p).ink) , Opacity = 0.85 });
            return g;
        }

        FrameworkElement MakePill(UIElement content, string tip, Action click)
        {
            var grid = new Grid { Height = ItemSize, Margin = new Thickness(2, 0, 2, 0), Background = Brushes.Transparent, ToolTip = tip };
            var bg = new Border { CornerRadius = new CornerRadius(PillRadius - 10) };
            grid.Children.Add(bg);
            System.Windows.Automation.AutomationProperties.SetName(grid, tip);
            var st = new ScaleTransform(1, 1);
            grid.Children.Add(new ContentPresenter
            {
                Content = content,
                Margin = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = st
            });
            Hook(grid, bg, st, click);
            return grid;
        }

        static void Hook(FrameworkElement el, Border bg, ScaleTransform st, Action click)
        {
            el.MouseEnter += (s, e) => bg.Background = Theme.Ink(Theme.SurfaceHover);
            el.MouseLeave += (s, e) => { bg.Background = bg.Tag as Brush ?? Brushes.Transparent; Anim(st, 1); };
            el.MouseLeftButtonDown += (s, e) => { bg.Background = Theme.Ink(Theme.SurfacePressed); Anim(st, 0.92); };
            el.MouseLeftButtonUp += (s, e) => { bg.Background = Theme.Ink(Theme.SurfaceHover); Anim(st, 1); click?.Invoke(); };
        }

        static void Anim(ScaleTransform st, double to)
        {
            var a = new DoubleAnimation(to, Theme.D(Theme.Fast)) { EasingFunction = Theme.EaseOut };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        static TextBlock Glyph(string g, double size) => new TextBlock
        {
            Text = g,
            FontFamily = Theme.Icons,
            FontSize = size,
            Foreground = Theme.Fg(),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        static UIElement StartIcon()
        {
            // vurgu renginde dolu, yuvarlak kose bir kare; icinde koyu (OnAccent) dort kucuk kare
            var c = new Canvas { Width = 14, Height = 14 };
            var o = new[] { 1.0, 0.75, 0.75, 1.0 };
            for (int i = 0; i < 4; i++)
            {
                var r = new Rectangle { Width = 6, Height = 6, RadiusX = 1.5, RadiusY = 1.5, Fill = Theme.Brush(Theme.OnAccent), Opacity = o[i] };
                Canvas.SetLeft(r, (i % 2) * 8);
                Canvas.SetTop(r, (i / 2) * 8);
                c.Children.Add(r);
            }
            double sz = Math.Round(Settings.Current.ItemSize * 0.7);
            return new Border
            {
                Width = sz, Height = sz, CornerRadius = new CornerRadius(sz * 0.3), Background = Theme.Brush(Theme.Accent),
                Child = new Viewbox { Width = sz * 0.5, Height = sz * 0.5, Child = c }
            };
        }

        static UIElement Sep() => new Border
        {
            Width = 3,
            Height = 34,
            CornerRadius = new CornerRadius(1.5),
            Margin = new Thickness(8, 0, 8, 12),
            Background = Theme.Ink(Theme.StrokeSubtle),
            VerticalAlignment = VerticalAlignment.Center
        };

        void UpdateClock()
        {
            var n = DateTime.Now;
            timeText.Text = n.ToString("HH:mm", Tr);
            dateText.Text = n.ToString("d MMM ddd", Tr);
        }

        /// <summary>Medyayi bu exe mi caliyor? (Spotify.exe ya da magaza Spotify'i)</summary>
        static bool IsMediaApp(string exePath, MediaInfo m)
        {
            var proc = m?.ProcessName;
            return proc != null && !string.IsNullOrEmpty(exePath) &&
                   string.Equals(System.IO.Path.GetFileNameWithoutExtension(exePath), proc, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Ikonun sag ust kosesinde kirmizi sayi (ya da nokta).</summary>
        static FrameworkElement BadgeView(string text)
        {
            bool dot = text == "•";
            var b = new Border
            {
                MinWidth = dot ? 9 : 16, Height = dot ? 9 : 16, CornerRadius = new CornerRadius(8), Padding = new Thickness(dot ? 0 : 4, 0, dot ? 0 : 4, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0x3B, 0x3B)), BorderBrush = Theme.Brush(Theme.DockFill), BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, dot ? -10 : -14, dot ? -12 : -18, 0), IsHitTestVisible = false
            };
            if (!dot) b.Child = new TextBlock
            {
                Text = text, FontFamily = Theme.Ui, FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0)
            };
            return b;
        }

        /// <summary>Muzik calan uygulamanin ikonunda sol altta kucuk, oynayan ekolayzer.</summary>
        static FrameworkElement PlayingView()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            var host = new Border
            {
                Child = sp, Background = Theme.Brush(Theme.DockFill), CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Height = 12,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-14, 0, 0, -12), IsHitTestVisible = false
            };
            var rnd = new Random();
            for (int i = 0; i < 3; i++)
            {
                var bar = new Border { Width = 2, Height = 7, Margin = new Thickness(0.5, 0, 0.5, 0), CornerRadius = new CornerRadius(1), Background = Theme.Brush(Theme.Accent), VerticalAlignment = VerticalAlignment.Bottom };
                if (Theme.Motion)
                    bar.BeginAnimation(HeightProperty, new DoubleAnimation(2.5, 8, TimeSpan.FromMilliseconds(320 + rnd.Next(260)))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                sp.Children.Add(bar);
            }
            return host;
        }

        // ---------------- Uygulamalar ----------------
        void LoadPinned()
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (!Directory.Exists(dir)) return;
            dynamic sh = null;
            try { sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); } catch { }
            foreach (var lnk in Directory.GetFiles(dir, "*.lnk").OrderBy(f => f))
            {
                string target = "";
                try { if (sh != null) target = (string)sh.CreateShortcut(lnk).TargetPath; } catch { }
                var name = System.IO.Path.GetFileNameWithoutExtension(lnk);
                if (string.IsNullOrEmpty(target) && (name.Contains("Explorer") || name.Contains("Gezgin")))
                    target = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                var key = string.IsNullOrEmpty(target) ? lnk : target;
                if (Settings.Current.DockHidden.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
                pinned.Add(new AppEntry { Key = key, Name = name, LaunchPath = lnk, IconPath = lnk, Pinned = true });
            }
            // surukle-birak ile eklenenler
            foreach (var path in Settings.Current.DockExtra)
            {
                if (!File.Exists(path) && !Directory.Exists(path)) continue;
                string key = path;
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    try { var t = (string)sh?.CreateShortcut(path).TargetPath; if (!string.IsNullOrEmpty(t)) key = t; } catch { }
                if (pinned.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))) continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                try { if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { var d = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(d)) name = d; } } catch { }
                pinned.Add(new AppEntry { Key = key, Name = name, LaunchPath = path, IconPath = path, Pinned = true, Extra = path });
            }
            // kullanicinin surukleyerek verdigi sira
            var order = Settings.Current.DockOrder;
            var sorted = pinned.OrderBy(x => { int i = order.FindIndex(k => string.Equals(k, x.Key, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }).ToList();
            pinned.Clear(); pinned.AddRange(sorted);
        }

        public void ReloadPinned()
        {
            pinned.Clear();
            LoadPinned();
            lastSig = "";
            Refresh();
        }

        // ---------------- uygulama sag tik menusu ----------------
        ContextMenu AppMenu(AppEntry e)
        {
            var m = new ContextMenu();
            string key = e.Key, launch = e.LaunchPath, extra = e.Extra;
            bool isPinned = e.Pinned;
            var wins = e.Windows.ToList();
            void Add(string text, Action a) { var i = new MenuItem { Header = text }; i.Click += (s, ev) => a(); m.Items.Add(i); }
            Add(wins.Count > 0 ? "Yeni pencere" : "Aç", () => Launch(launch));
            Add(isPinned ? "Dock'tan kaldır" : "Dock'a sabitle", () => SetPinned(key, launch, extra, !isPinned));
            if (IsMediaApp(key, Hub.CurrentMedia))
            {
                m.Items.Add(new Separator());
                Add("Şimdi çalan…", () => Toggle(playerFly, null));
                Add(Hub.CurrentMedia?.Playing == true ? "Duraklat" : "Oynat", () => Hub.Media.PlayPause());
                Add("Sonraki şarkı", () => Hub.Media.Next());
            }
            if (e.Badge != null) Add("Rozeti temizle", () => { Badges.Seen(key, wins.Select(Badges.WindowAumid)); lastSig = ""; });
            if (wins.Count > 0)
            {
                // Windows'un kendi yaslama ozelligi: pencereyi one getir, sonra kisayolu gonder
                var target = wins.Contains(Native.GetForegroundWindow()) ? Native.GetForegroundWindow() : wins[0];
                m.Items.Add(new Separator());
                if (Glass.IsWin11) Add("Yaslama düzenleri…", () => SnapWith(target, 0x5A));   // Win+Z
                Add("Sola yasla", () => SnapWith(target, 0x25));                                 // Win+Sol
                Add("Sağa yasla", () => SnapWith(target, 0x27));                                 // Win+Sag
                Add(Native.IsZoomed(target) ? "Önceki boyutuna getir" : "Ekranı kapla", () => { Activate(target); Native.ShowWindow(target, Native.IsZoomed(target) ? 9 : 3); });
                m.Items.Add(new Separator());
                Add(wins.Count == 1 ? "Pencereyi kapat" : "Tüm pencereleri kapat (" + wins.Count + ")",
                    () => { foreach (var h in wins) Native.PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); });
            }
            return m;
        }

        /// <summary>Pencereyi one getirip Windows yaslama kisayolunu (Win+tus) gonderir.</summary>
        void SnapWith(IntPtr h, byte key)
        {
            Activate(h);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            t.Tick += (s, e) => { t.Stop(); SendKeys(VK_LWIN, key); };
            t.Start();
        }

        void SetPinned(string key, string launch, string extra, bool pin)
        {
            var st = Settings.Current;
            if (pin)
            {
                st.DockHidden.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                if (!pinned.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)) && !st.DockExtra.Contains(launch, StringComparer.OrdinalIgnoreCase))
                    st.DockExtra.Add(launch);
            }
            else if (extra != null) st.DockExtra.RemoveAll(k => string.Equals(k, extra, StringComparison.OrdinalIgnoreCase));
            else st.DockHidden.Add(key);
            st.Save();
            DockManager.ReloadPinned();
        }

        // ---------------- surukleyerek siralama ----------------
        string dragKey;
        FrameworkElement dragEl;
        Point dragStart;
        bool dragging;
        int dropIndex;

        void OnDragMove(object s, MouseEventArgs e)
        {
            if (dragKey == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { dragKey = null; return; }
            var p = e.GetPosition(row);
            if (!dragging)
            {
                if (Math.Abs(p.X - dragStart.X) < 6) return;
                dragging = true;
                preview.HidePreview();
                ResetMagnify();
                dragEl.Opacity = 0.85;
                dropIndex = appsPanel.Children.IndexOf(dragEl);
                Panel.SetZIndex(dragEl, 10);
                dragEl.RenderTransform = new TranslateTransform();
                row.CaptureMouse();
            }
            double dx = p.X - dragStart.X;
            ((TranslateTransform)dragEl.RenderTransform).X = dx;
            // hedef yuva: bir ikonun genisligi kadar kaydikca bir sira ilerler
            var items = appsPanel.Children.OfType<FrameworkElement>().ToList();
            int from = items.IndexOf(dragEl);
            double w = dragEl.ActualWidth + dragEl.Margin.Left + dragEl.Margin.Right;
            int to = Math.Clamp((int)Math.Round(from + dx / w), 0, items.Count - 1);
            if (to != dropIndex)
            {
                dropIndex = to;
                // aradaki ikonlar bos yuvaya dogru kayar
                for (int j = 0; j < items.Count; j++)
                {
                    if (items[j] == dragEl) continue;
                    double shift = from < to && j > from && j <= to ? -w : from > to && j >= to && j < from ? w : 0;
                    if (!(items[j].RenderTransform is TranslateTransform tt)) items[j].RenderTransform = tt = new TranslateTransform();
                    tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(shift, Theme.D(Theme.Normal)) { EasingFunction = Theme.EaseOut });
                }
            }
            e.Handled = true;
        }

        void OnDragEnd(object s, MouseButtonEventArgs e)
        {
            if (!dragging) { dragKey = null; return; }
            e.Handled = true; // tiklama sayilmasin
            var key = dragKey;
            CancelDrag();
            var keys = currentEntries.Select(x => x.Key).ToList();
            var moved = currentEntries.FirstOrDefault(x => x.Key == key);
            if (moved == null) return;
            keys.Remove(key);
            keys.Insert(Math.Clamp(dropIndex, 0, keys.Count), key);
            var st = Settings.Current;
            // surukenen uygulama sabit degilse sabitle
            if (!moved.Pinned && !st.DockExtra.Contains(moved.LaunchPath, StringComparer.OrdinalIgnoreCase)) st.DockExtra.Add(moved.LaunchPath);
            st.DockHidden.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            st.DockOrder = keys;
            st.Save();
            DockManager.ReloadPinned();
        }

        void CancelDrag()
        {
            dragging = false;
            if (dragEl != null) { dragEl.Opacity = 1; Panel.SetZIndex(dragEl, 0); }
            foreach (UIElement c in appsPanel.Children) c.RenderTransform = null;
            dragKey = null;
            if (row.IsMouseCaptured) row.ReleaseMouseCapture();
        }

        // tum dock'lar ayni pencere listesini kullanir: her dock'un kendi taramasini yapmasina gerek yok
        static List<(IntPtr h, string exe, string title, string screen)> sharedWins;
        static DateTime sharedAt;

        static List<(IntPtr h, string exe, string title, string screen)> SharedAppWindows(bool force)
        {
            if (!force && sharedWins != null && (DateTime.Now - sharedAt).TotalMilliseconds < 300) return sharedWins;
            var screens = Screens.All();
            sharedWins = EnumAppWindows().Select(w => (w.h, w.exe, w.title, Screens.FromWindow(w.h, screens).Device)).ToList();
            sharedAt = DateTime.Now;
            return sharedWins;
        }

        static List<(IntPtr h, string exe, string title)> EnumAppWindows()
        {
            var list = new List<(IntPtr, string, string)>();
            var exeCache = new Dictionary<uint, string>();
            uint me = (uint)Environment.ProcessId;
            Native.EnumWindows((h, l) =>
            {
                if (!Native.IsWindowVisible(h)) return true;
                long ex = (long)Native.GetWindowLongPtr(h, -20);
                bool appWindow = (ex & 0x40000) != 0;
                if ((ex & 0x80) != 0 && !appWindow) return true;
                if (Native.GetWindow(h, 4) != IntPtr.Zero && !appWindow) return true;
                if (Native.DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
                int len = Native.GetWindowTextLength(h);
                if (len == 0) return true;
                var cls = new StringBuilder(256);
                Native.GetClassName(h, cls, 256);
                var c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return true;
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid == me) return true;
                if (!exeCache.TryGetValue(pid, out var exe)) { exe = ExePath(pid); exeCache[pid] = exe; }
                if (exe == null) return true;
                var sb = new StringBuilder(len + 1);
                Native.GetWindowText(h, sb, sb.Capacity);
                list.Add((h, exe, sb.ToString()));
                return true;
            }, IntPtr.Zero);
            return list;
        }

        static string ExePath(uint pid)
        {
            var h = Native.OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int sz = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref sz) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(h); }
        }

        void Refresh() => Refresh(false);

        void Refresh(bool force)
        {
            UpdateClock();
            if (hwnd != IntPtr.Zero && main) KeepTaskbarHidden();
            var wins = SharedAppWindows(force);
            var fg = Native.GetForegroundWindow();
            // birden fazla dock varsa her biri yalniz kendi ekranindaki pencereleri gosterir
            bool perScreen = Settings.Current.DockAppsPerScreen && DockManager.Multi;
            var entries = pinned.Select(p => new AppEntry { Key = p.Key, Name = p.Name, LaunchPath = p.LaunchPath, IconPath = p.IconPath, Pinned = true, Extra = p.Extra }).ToList();
            foreach (var w in wins)
            {
                var e = entries.FirstOrDefault(x => string.Equals(x.Key, w.exe, StringComparison.OrdinalIgnoreCase));
                if (perScreen && w.screen != screen.Device)
                {
                    // baska ekrandaki pencere: sabitlenmis uygulamaya "orada acik" diye not et, yeni ikon ekleme
                    e?.Elsewhere.Add(w.h);
                    continue;
                }
                if (e == null)
                {
                    string name = w.title;
                    try { var d = FileVersionInfo.GetVersionInfo(w.exe).FileDescription; if (!string.IsNullOrWhiteSpace(d)) name = d; } catch { }
                    e = new AppEntry { Key = w.exe, Name = name, LaunchPath = w.exe, IconPath = w.exe };
                    entries.Add(e);
                }
                e.Windows.Add(w.h);
            }
            // kullanicinin surukleyerek verdigi sira: sabit olsun olmasin tum uygulamalara uygulanir
            var order = Settings.Current.DockOrder;
            entries = entries.Select((x, n) => (x, n))
                .OrderBy(t => { int i = order.FindIndex(k => string.Equals(k, t.x.Key, StringComparison.OrdinalIgnoreCase)); return i < 0 ? order.Count + t.n : i; })
                .Select(t => t.x).ToList();
            // rozetler: ondeki uygulamanin bildirim sayaci sifirlanir (bakiyorsun)
            var media = Hub.CurrentMedia;
            foreach (var e in entries)
            {
                var ids = e.Windows.Concat(e.Elsewhere).Select(Badges.WindowAumid).ToList();
                if (e.Windows.Contains(fg)) Badges.Seen(e.Key, ids);
                e.Badge = Badges.For(e.Key, ids);
                e.Playing = media != null && media.Playing && IsMediaApp(e.Key, media);
            }
            var sig = string.Join("|", entries.Select(e => e.Key + ":" + e.Windows.Count + (e.Windows.Contains(fg) ? "*" : "") + (e.Badge ?? "") + (e.Playing ? "~" : "")));
            // pencere sirasini da sakla (tiklama davranisi icin) ama UI'yi sadece gorunum degisince yenile
            currentEntries = entries;
            if (dragging || sig == lastSig) return; // surukleme sirasinda ikonlari yeniden kurma
            lastSig = sig;

            appsPanel.Children.Clear();
            appMag.Clear();
            foreach (var e in entries)
            {
                var src = GetIcon(e.IconPath);
                var img = new Image { Source = src, Width = Settings.Current.IconSize + 4, Height = Settings.Current.IconSize + 4 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                int ind = e.Windows.Count == 0 ? 0 : (e.Windows.Contains(fg) ? 2 : 1);
                string key = e.Key;
                // ikon + rozet + calma gostergesi: buyume efektiyle birlikte olceklensin diye ayni kapta
                var icon = new Grid();
                icon.Children.Add(img);
                if (e.Badge != null) icon.Children.Add(BadgeView(e.Badge));
                if (e.Playing) icon.Children.Add(PlayingView());
                if (!pastelCache.TryGetValue(e.IconPath, out var pastel)) pastelCache[e.IconPath] = pastel = Theme.PastelOf(src);
                var fill = Settings.Current.DockTiles == "white" ? Theme.Raised : Theme.PastelColors(pastel).fill;
                var item = MakeItem(icon, e.Badge != null ? e.Name + " (" + e.Badge + " okunmamış)" : e.Name, () => OnAppClick(key), ind, key, e.Windows.Count > 0, fill);
                if (IsMediaApp(key, media))
                {
                    // calan uygulamanin (Spotify) ikonunda fare tekerlegi: yalniz onun sesi
                    item.MouseWheel += (s, ev) =>
                    {
                        var m = Hub.CurrentMedia;
                        var proc = m?.ProcessName;
                        if (proc == null) return;
                        float v = Audio.GetAppVolume(proc);
                        if (v < 0) return;
                        v = (float)Math.Clamp(Math.Round((v + (ev.Delta > 0 ? 0.05 : -0.05)) * 20) / 20, 0, 1);
                        Audio.SetAppVolume(proc, v);
                        Hub.ShowAppVolume(IslandWindow.FriendlySource(m.SourceId), v);
                        ev.Handled = true;
                    };
                }
                item.PreviewMouseLeftButtonDown += (s, ev) => { dragKey = key; dragEl = item; dragStart = ev.GetPosition(row); };
                item.ContextMenu = AppMenu(e);
                item.ContextMenu.Opened += (s, ev) => { itemMenuOpen = true; preview.HidePreview(); };
                item.ContextMenu.Closed += (s, ev) => itemMenuOpen = false;
                appsPanel.Children.Add(item);
            }
            FitToContent();
            // yeni ikonlar yerlesince genisligi bir kez daha olc (saat kesilmesin)
            Dispatcher.BeginInvoke(new Action(FitToContent), DispatcherPriority.Loaded);
            // fare dock uzerindeyse buyumeyi yeni ikonlara da uygula
            if (row.IsMouseOver) { UpdateLayout(); Magnify(Mouse.GetPosition(row).X); }
        }

        /// <summary>Yeni gorunen ogeler yerlestikten sonra genisligi olc (saat kesilmesin).</summary>
        void FitLater()
        {
            FitToContent();
            Dispatcher.BeginInvoke(new Action(FitToContent), DispatcherPriority.Loaded);
        }

        void FitToContent()
        {
            if (!(Content is FrameworkElement c)) return;
            c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Width = Math.Ceiling(c.DesiredSize.Width);
            Height = Math.Ceiling(c.DesiredSize.Height);
        }

        List<AppEntry> currentEntries = new List<AppEntry>();

        void OnAppClick(string key)
        {
            preview.HidePreview();
            // en guncel pencere listesini kullan
            lastSig = "";
            Refresh(true);
            var e = currentEntries.FirstOrDefault(x => x.Key == key);
            if (e == null) return;
            // bu ekranda penceresi yok ama baska ekranda acik: yeni kopya baslatmak yerine oradakini one getir
            if (e.Windows.Count == 0 && e.Elsewhere.Count > 0) { Activate(e.Elsewhere[0]); lastSig = ""; return; }
            if (e.Windows.Count == 0) { Launch(e.LaunchPath); return; }
            var fg = Native.GetForegroundWindow();
            int idx = e.Windows.IndexOf(fg);
            if (idx >= 0)
            {
                if (e.Windows.Count == 1) Native.ShowWindow(fg, 6); // simge durumuna kucult
                else Activate(e.Windows[e.Windows.Count - 1]);      // ayni uygulamanin diger penceresine gec
            }
            else Activate(e.Windows[0]);
            lastSig = "";
        }

        static void Activate(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, 9);
            if (!Native.SetForegroundWindow(h) || Native.GetForegroundWindow() != h)
                Native.SwitchToThisWindow(h, true);
        }

        static void Launch(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        static void SendKeys(params byte[] keys)
        {
            foreach (var k in keys) Native.keybd_event(k, 0, 0, UIntPtr.Zero);
            for (int i = keys.Length - 1; i >= 0; i--) Native.keybd_event(keys[i], 0, 2, UIntPtr.Zero);
        }

        public ImageSource GetIcon(string path)
        {
            if (iconCache.TryGetValue(path, out var cached)) return cached;
            ImageSource img = null;
            try
            {
                Native.SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(Native.IShellItemImageFactory).GUID, out var f);
                if (f.GetImage(new Native.SIZE { cx = 64, cy = 64 }, 0x4 | 0x1, out IntPtr hbm) == 0 && hbm != IntPtr.Zero)
                {
                    img = FromHBitmap(hbm);
                    Native.DeleteObject(hbm);
                }
                Marshal.ReleaseComObject(f);
            }
            catch { }
            iconCache[path] = img;
            return img;
        }

        static BitmapSource FromHBitmap(IntPtr hbm)
        {
            Native.GetObject(hbm, Marshal.SizeOf<Native.BITMAP>(), out var bm);
            int w = bm.bmWidth, h = bm.bmHeight;
            var bi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var bits = new byte[w * h * 4];
            var dc = Native.GetDC(IntPtr.Zero);
            Native.GetDIBits(dc, hbm, 0, (uint)h, bits, ref bi, 0);
            Native.ReleaseDC(IntPtr.Zero, dc);
            bool anyAlpha = false;
            for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;
            var bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bits, w * 4);
            bs.Freeze();
            return bs;
        }

        // ---------------- Pencere / gorev cubugu ----------------
        void OnSourceInitialized(object s, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Glass.NoActivate(hwnd);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            // gosterilmeden once kendi ekranina al: WPF o ekranin DPI'ini kullansin
            Screens.MoveTo(hwnd, screen.Work.left + 1, screen.Work.top + 1);
            scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

            if (main)
            {
                taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
                callbackMsg = Native.RegisterWindowMessage("FloatingDockAppBar");
                HideTaskbar();
                // tepsi sunucusu kendi "Shell_TrayWnd" penceresini acar ve gorev cubugu mesajlarini o alir:
                // Explorer'in cubugunu gizledikten SONRA baslat
                if (Settings.Current.TrayButton) Tray.Start();
                if (WinKeyHook.Enabled) SetWinKey(true);
            }
            // Not: ekran alani ayrilmiyor (appbar yok) -> buyutulmus pencereler ekranin en altina kadar uzanir.
            Reposition();
            var ah = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            ah.Tick += (s2, e2) => AutoHideTick();
            ah.Start();
            timers.Add(ah);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            scale = newDpi.DpiScaleX;
            Reposition();
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (taskbarCreatedMsg != 0 && msg == taskbarCreatedMsg)
            {
                // Explorer yeniden basladi: gorev cubugunu tekrar gizle
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                t.Tick += (s, e) => { t.Stop(); HideTaskbar(); Reposition(); };
                t.Start();
            }
            else if (callbackMsg != 0 && msg == callbackMsg)
            {
                int code = (int)w;
                if (code == 1) { SetAppBarPos(); Reposition(); }   // ABN_POSCHANGED
                else if (code == 2) Topmost = l == IntPtr.Zero;    // ABN_FULLSCREENAPP: tam ekran oyun/video ustunde durma
            }
            else if (msg == 0x0021) { handled = true; return (IntPtr)3; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            return IntPtr.Zero;
        }

        void HideTaskbar()
        {
            tray = Native.ExplorerTray();
            if (tray == IntPtr.Zero) return;
            var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = tray };
            if (origTrayState < 0)
            {
                Directory.CreateDirectory(StateDir);
                if (File.Exists(StateFile) && int.TryParse(File.ReadAllText(StateFile).Trim(), out int saved)) origTrayState = saved;
                else
                {
                    origTrayState = (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd);
                    File.WriteAllText(StateFile, origTrayState.ToString());
                }
            }
            abd.lParam = (IntPtr)Native.ABS_AUTOHIDE;
            Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
            Native.ShowWindow(tray, 0);
        }

        void KeepTaskbarHidden()
        {
            if (restored) return;
            var t = Native.ExplorerTray();
            if (t != IntPtr.Zero && Native.IsWindowVisible(t)) Native.ShowWindow(t, 0);
            if (hideSecondary) foreach (var s in SecondaryTrays()) if (Native.IsWindowVisible(s)) Native.ShowWindow(s, 0);
        }

        // ---------------- diger ekranlardaki Windows gorev cubuklari ----------------
        bool hideSecondary;

        static List<IntPtr> SecondaryTrays()
        {
            var list = new List<IntPtr>();
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(h);
            return list;
        }

        /// <summary>Her ekranda dock varsa diger ekranlarin gorev cubuklarini gizle; yoksa geri getir.</summary>
        public void SyncSecondaryTaskbars(bool hide)
        {
            if (!main || restored) return;
            hideSecondary = hide;
            if (hide) KeepTaskbarHidden();
            else ShowSecondaryTaskbars();
        }

        void ShowSecondaryTaskbars()
        {
            // yalniz bizim gizlediklerimizi degil hepsini goster: onceki oturum yarida kaldiysa da geri gelsinler
            foreach (var s in SecondaryTrays()) if (!Native.IsWindowVisible(s)) Native.ShowWindow(s, 5);
        }

        public void RestoreTaskbar()
        {
            try { TopBar.ReleaseAll(); } catch { }
            if (restored) return;
            restored = true;
            // once tepsi sunucumuzu kapat: asagidaki gorev cubugu mesajlari yeniden Explorer'a gitsin,
            // TaskbarCreated yayiniyla da ikonlar Windows tepsisine donsun
            try { Tray.Stop(); } catch { }
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    var a = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = hwnd };
                    Native.SHAppBarMessage(Native.ABM_REMOVE, ref a);
                }
                tray = Native.ExplorerTray();
                if (tray != IntPtr.Zero)
                {
                    int st = origTrayState < 0 ? Native.ABS_ALWAYSONTOP : origTrayState;
                    var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = tray, lParam = (IntPtr)st };
                    Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
                    Native.ShowWindow(tray, 5);
                }
                ShowSecondaryTaskbars();
                if (File.Exists(StateFile)) File.Delete(StateFile);
            }
            catch { }
            // gizledigimiz Windows bildirim balonlari geri gelsin (ayar acik kalir, sonraki acilista yeniden uygulanir)
            try { Banners.Restore(true); } catch { }
        }

        void RegisterAppBar()
        {
            var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = hwnd, uCallbackMessage = callbackMsg };
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
            Native.SHAppBarMessage(Native.ABM_NEW, ref abd);
            SetAppBarPos();
        }

        void SetAppBarPos()
        {
            if (hwnd == IntPtr.Zero) return;
            int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
            int reserve = (int)Math.Ceiling((ItemSize + 10 + DockMargin + 6) * scale);
            var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = hwnd, uEdge = Native.ABE_BOTTOM };
            abd.rc = new Native.RECT { left = 0, right = sw, top = sh - reserve, bottom = sh };
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
            abd.rc.top = abd.rc.bottom - reserve;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
            appBarBottomPx = abd.rc.bottom;
        }

        // ---------------- Konum (fiziksel piksel) ----------------
        // Ekranlarin DPI'i farkli olabilir: WPF'in Left/Top'una guvenmek yerine pencereyi SetWindowPos ile piksel olarak yerlestiririz.
        // Kayma animasyonu bu ozellik uzerinden calisir; her degisimde pencere tasinir.
        static readonly DependencyProperty PxTopProperty = DependencyProperty.Register("PxTop", typeof(double), typeof(DockWindow),
            new PropertyMetadata(0.0, (d, e) => ((DockWindow)d).ApplyPos()));
        double pxLeft;

        void ApplyPos() => Screens.MoveTo(hwnd, pxLeft, (double)GetValue(PxTopProperty));

        void Reposition()
        {
            if (hwnd == IntPtr.Zero || ActualWidth <= 0) return;
            pxLeft = Math.Round(screen.CenterX - ActualWidth * scale / 2);
            BeginAnimation(PxTopProperty, null);
            BeginAnimation(OpacityProperty, null);
            SetValue(PxTopProperty, dockHidden ? HiddenTopPx() : ShownTopPx());
            Opacity = dockHidden && FadeWhenHidden ? 0 : 1;
            ApplyPos();
        }

        // ---------------- Akilli gizleme ----------------
        bool dockHidden;
        DateTime lastWanted = DateTime.Now;

        double ShownTopPx() => Math.Round(screen.Bounds.bottom - (DockMargin - PadBottom + ActualHeight) * scale);
        double HiddenTopPx() => Math.Round(screen.Bounds.bottom + 6 * scale);
        /// <summary>Altta baska ekran varsa dock asagi kayinca orada gorunur: kayarken saydamlasarak kaybolsun.</summary>
        bool FadeWhenHidden => Screens.HasScreenBelow(screen, Screens.All());

        void AutoHideTick()
        {
            if (hwnd == IntPtr.Zero || ActualHeight <= 0) return;
            if (ShouldShow())
            {
                lastWanted = DateTime.Now;
                if (dockHidden) SlideTo(false);
            }
            else if (!dockHidden && (DateTime.Now - lastWanted).TotalMilliseconds > 450) SlideTo(true);
        }

        void SlideTo(bool hide)
        {
            dockHidden = hide;
            if (hide) preview.HidePreview();
            var dur = TimeSpan.FromMilliseconds(hide ? 200 : 260);
            var a = new DoubleAnimation(hide ? HiddenTopPx() : ShownTopPx(), dur)
            {
                EasingFunction = hide ? (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseIn }
                                      : new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(PxTopProperty, a);
            if (FadeWhenHidden) BeginAnimation(OpacityProperty, new DoubleAnimation(hide ? 0 : 1, dur));
            else { BeginAnimation(OpacityProperty, null); Opacity = 1; }
        }

        bool ShouldShow()
        {
            if (!Settings.Current.AutoHide) return true;
            // dock ile etkilesim suruyorsa her zaman goster
            if (dragging || IsMouseOver || preview.IsVisible || AnyFlyoutOpen) return true;
            if ((Content as FrameworkElement)?.ContextMenu?.IsOpen == true) return true;

            var b = screen.Bounds;
            Native.GetCursorPos(out var c);
            double shown = ShownTopPx();
            var dr = new Native.RECT
            {
                left = (int)(pxLeft + PadSide * scale),
                right = (int)(pxLeft + (ActualWidth - PadSide) * scale),
                top = (int)(shown + PadTop * scale),
                bottom = (int)(shown + (ActualHeight - PadBottom) * scale)
            };
            bool cursorHere = c.x >= b.left && c.x < b.right && c.y >= b.top && c.y < b.bottom;

            var fg = Native.GetForegroundWindow();
            bool fgValid = false, fullscreen = false;
            Native.RECT fr = default;
            if (fg != IntPtr.Zero && fg != hwnd && Native.IsWindowVisible(fg) && !Native.IsIconic(fg))
            {
                var cls = new StringBuilder(64);
                Native.GetClassName(fg, cls, 64);
                var cn = cls.ToString();
                if (cn == "Windows.UI.Core.CoreWindow" || cn == "XamlExplorerHostIslandWindow") return true; // Baslat / arama / hizli ayarlar acik
                if (cn != "Progman" && cn != "WorkerW" && cn != "Shell_TrayWnd" && cn != "Shell_SecondaryTrayWnd" && Native.GetWindowRect(fg, out fr))
                {
                    fgValid = true;
                    // bu ekrani tamamen kaplayan (buyutulmemis) pencere: tam ekran oyun / video
                    fullscreen = !Native.IsZoomed(fg) && fr.left <= b.left && fr.top <= b.top && fr.right >= b.right && fr.bottom >= b.bottom;
                }
            }

            // bu ekranin alt kenarina dokununca ortaya cik (tam ekran oyun/videoda degil)
            if (cursorHere && !fullscreen && c.y >= b.bottom - 2) return true;
            // gorunurken fare dock seridinde ise acik kal
            if (!dockHidden && c.x >= dr.left - 10 && c.x <= dr.right + 10 && c.y >= dr.top - 20 && c.y <= b.bottom) return true;
            if (!fgValid) return true; // masaustu: goster

            // on plandaki pencere dock'un alanina degiyor mu?
            bool overlap = fr.left < dr.right && fr.right > dr.left && fr.top < dr.bottom && fr.bottom > dr.top;
            return !overlap;
        }
    }
}
