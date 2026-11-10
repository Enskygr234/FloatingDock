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
    }

    class DockWindow : Window
    {
        static readonly Color Accent = Theme.Accent;
        static double ItemSize => Settings.Current.ItemSize;
        const double DockMargin = 10;
        // hapin cevresindeki seffaf pay: ustte buyuyen ikonlar, altta ve yanlarda golge icin
        const double PadTop = 18, PadSide = 18, PadBottom = 16, PillRadius = 20;
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
        string lastSig = "";
        static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        readonly PreviewWindow preview;
        StackPanel row;
        // buyume (magnification) animasyonu icin ikonlar
        readonly List<(FrameworkElement el, ScaleTransform mag)> fixedMag = new List<(FrameworkElement, ScaleTransform)>();
        readonly List<(FrameworkElement el, ScaleTransform mag)> appMag = new List<(FrameworkElement, ScaleTransform)>();
        const double MagMax = 0.32, MagSpread = 58;

        public DockWindow()
        {
            Title = "FloatingDock";
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
                () => Top + PadTop,
                Activate,
                GetIcon,
                () => { lastSig = ""; Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background); });

            LoadPinned();
            Content = BuildUi();
            Width = 400; Height = 54;
            SourceInitialized += OnSourceInitialized;
            SizeChanged += (s, e) => Reposition();
            Closed += (s, e) => { preview.Close(); control.Close(); calendar.Close(); start.Close(); settingsFly.Close(); winKey?.Stop(); RestoreTaskbar(); };
            settingsFly.SetWinKey = SetWinKey;
            settingsFly.CloseDock = Close;
            control.OpenSettings = () => Toggle(settingsFly, null);
            Settings.Current.Changed += () => { if (!Settings.Current.AutoHide && dockHidden) SlideTo(false); };

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            t.Tick += (s, e) => Refresh();
            t.Start();
            Refresh();
        }

        // ---------------- Arayuz ----------------
        UIElement BuildUi()
        {
            row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 6), Background = Brushes.Transparent };
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
                ReloadPinned();
            };
            row.MouseLeave += (s, e) => { ResetMagnify(); preview.HoverOther(); };
            row.Children.Add(MakeItem(StartIcon(), "Başlat", () => Toggle(start, null), 0));
            row.Children.Add(MakeItem(Glyph("", 17), "Ara", () => Toggle(start, null), 0));
            if (Settings.Current.TaskViewButton)
                row.Children.Add(MakeItem(Glyph("\uE7C4", 17), "Görev görünümü", () => SendKeys(VK_LWIN, 0x09), 0));      // Win+Tab
            if (Glass.IsWin11 && Settings.Current.WidgetsButton)
                row.Children.Add(MakeItem(Glyph("\uECA5", 17), "Widget'lar", () => SendKeys(VK_LWIN, 0x57), 0));         // Win+W
            row.Children.Add(Sep());
            appsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(appsPanel);
            row.Children.Add(Sep());

            var qs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            qs.Children.Add(Glyph("", 15));
            qs.Children.Add(new Border { Width = 14 });
            qs.Children.Add(Glyph("", 15));
            FrameworkElement qsPill = null, clockPill = null;
            qsPill = MakePill(qs, "Wi-Fi ve ses", () => Toggle(control, qsPill));

            var clock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            timeText = new TextBlock { Foreground = Theme.Fg(), FontSize = Theme.Body, FontWeight = FontWeights.SemiBold, FontFamily = Theme.Ui, HorizontalAlignment = HorizontalAlignment.Right };
            dateText = new TextBlock { Foreground = Theme.Fg(Theme.TextSecondary), FontSize = Theme.Caption - 0.5, FontFamily = Theme.Ui, HorizontalAlignment = HorizontalAlignment.Right };
            clock.Children.Add(timeText);
            clock.Children.Add(dateText);
            var clockHost = new Grid();
            clockHost.Children.Add(clock);
            var unreadDot = new System.Windows.Shapes.Ellipse
            {
                Width = 7, Height = 7, Fill = Theme.Brush(Theme.Accent), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, -9, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false
            };
            clockHost.Children.Add(unreadDot);
            Hub.HistoryChanged += () => Dispatcher.BeginInvoke(new Action(() => unreadDot.Visibility = Hub.Unread > 0 ? Visibility.Visible : Visibility.Collapsed));
            clockPill = MakePill(clockHost, "Takvim ve bildirimler", () => Toggle(calendar, clockPill));
            // durum simgeleri + saat: hapin icinde ayri, hafif zeminli bir kapsul
            var tray = new StackPanel { Orientation = Orientation.Horizontal };
            tray.Children.Add(qsPill);
            tray.Children.Add(clockPill);
            row.Children.Add(new Border
            {
                Child = tray, CornerRadius = new CornerRadius(PillRadius - 8), Background = Theme.Ink((byte)(Theme.SurfaceIdle - 2)),
                BorderBrush = Theme.Ink(Theme.StrokeSubtle), BorderThickness = new Thickness(1), Margin = new Thickness(2, 1, 0, 1), Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            });
            UpdateClock();

            // yuzen hap: golge katmani + dolgu + ince kenar + ustte isik cizgisi
            var pill = new Grid { Margin = new Thickness(PadSide, PadTop, PadSide, PadBottom) };
            pill.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(PillRadius), Background = Theme.Brush(Theme.DockFill), IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 5, Direction = 270, Opacity = Theme.Dark ? 0.45 : 0.18 }
            });
            pill.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(PillRadius), Background = Theme.Brush(Theme.DockFill),
                BorderBrush = Theme.Ink(Theme.StrokeDefault), BorderThickness = new Thickness(1)
            });
            pill.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(PillRadius), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(1), IsHitTestVisible = false,
                BorderBrush = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb((byte)(Theme.Dark ? 34 : 120), 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                }, 0)
            });
            pill.Children.Add(row);
            return new Grid { Background = Brushes.Transparent, Children = { pill }, ContextMenu = BuildMenu() };
        }

        // ---------------- Kendi panellerimiz (hizli ayarlar / takvim) ----------------
        readonly ControlCenter control = new ControlCenter();
        readonly CalendarFlyout calendar = new CalendarFlyout();
        readonly StartMenu start = new StartMenu();
        readonly SettingsFlyout settingsFly = new SettingsFlyout();
        WinKeyHook winKey;
        bool AnyFlyoutOpen => control.IsOpen || calendar.IsOpen || start.IsOpen || settingsFly.IsOpen || itemMenuOpen;
        bool itemMenuOpen;

        void Toggle(Flyout f, FrameworkElement anchor)
        {
            preview.HidePreview();
            if (f.IsOpen || f.JustClosed) { f.HideFlyout(); return; }
            foreach (var other in new Flyout[] { control, calendar, start, settingsFly }) if (other != f) other.HideFlyout();
            // Baslat menusu ekranin ortasinda, digerleri kendi dugmesinin ustunde acilir
            double x = anchor == null ? Native.GetSystemMetrics(0) / 2.0 : anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0)).X;
            f.ShowAt(x, ShownTop() + PadTop);
        }

        void SetWinKey(bool on)
        {
            WinKeyHook.Enabled = on;
            if (on)
            {
                winKey ??= new WinKeyHook(() => Dispatcher.BeginInvoke(new Action(() => Toggle(start, null))));
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
            Add("Dock'u kapat (eski görev çubuğuna dön)", Close);
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

        FrameworkElement MakeItem(UIElement content, string tip, Action click, int indicator, string appKey = null, bool hasWindows = false)
        {
            var grid = new Grid { Width = ItemSize, Height = ItemSize, Margin = new Thickness(2, 0, 2, 0), Background = Brushes.Transparent, ToolTip = tip };
            var bg = new Border { CornerRadius = new CornerRadius(Theme.RadiusLg) };
            if (indicator == 2) { bg.Background = Theme.Ink(Theme.SurfaceHover); bg.Tag = bg.Background; } // on plandaki uygulama
            grid.Children.Add(bg);
            System.Windows.Automation.AutomationProperties.SetName(grid, tip);
            var st = new ScaleTransform(1, 1);   // basma efekti
            var mag = new ScaleTransform(1, 1);  // buyume efekti
            var tg = new TransformGroup();
            tg.Children.Add(mag);
            tg.Children.Add(st);
            var holder = new ContentPresenter
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.72),
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
                    Height = indicator == 2 ? 3 : 4,
                    Width = indicator == 2 ? 14 : 4,
                    CornerRadius = new CornerRadius(2),
                    Background = indicator == 2 ? Theme.Brush(Accent) : Theme.Ink(Theme.TextTertiary),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, -1)
                });
            }
            Hook(grid, bg, st, click);
            return grid;
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
            el.MouseLeftButtonDown += (s, e) => { bg.Background = Theme.Ink(Theme.SurfacePressed); Anim(st, 0.9); };
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
            Width = 1,
            Height = 22,
            Margin = new Thickness(7, 0, 7, 0),
            Background = Theme.Ink(Theme.StrokeSubtle),
            VerticalAlignment = VerticalAlignment.Center
        };

        void UpdateClock()
        {
            var n = DateTime.Now;
            timeText.Text = n.ToString("HH:mm", Tr);
            dateText.Text = n.ToString("d MMM ddd", Tr);
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

        void ReloadPinned()
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
            ReloadPinned();
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
            ReloadPinned();
        }

        void CancelDrag()
        {
            dragging = false;
            if (dragEl != null) { dragEl.Opacity = 1; Panel.SetZIndex(dragEl, 0); }
            foreach (UIElement c in appsPanel.Children) c.RenderTransform = null;
            dragKey = null;
            if (row.IsMouseCaptured) row.ReleaseMouseCapture();
        }

        List<(IntPtr h, string exe, string title)> EnumAppWindows()
        {
            var list = new List<(IntPtr, string, string)>();
            var exeCache = new Dictionary<uint, string>();
            uint me = (uint)Environment.ProcessId;
            Native.EnumWindows((h, l) =>
            {
                if (h == hwnd || !Native.IsWindowVisible(h)) return true;
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

        void Refresh()
        {
            UpdateClock();
            if (hwnd != IntPtr.Zero) KeepTaskbarHidden();
            var wins = EnumAppWindows();
            var fg = Native.GetForegroundWindow();
            var entries = pinned.Select(p => new AppEntry { Key = p.Key, Name = p.Name, LaunchPath = p.LaunchPath, IconPath = p.IconPath, Pinned = true, Extra = p.Extra }).ToList();
            foreach (var w in wins)
            {
                var e = entries.FirstOrDefault(x => string.Equals(x.Key, w.exe, StringComparison.OrdinalIgnoreCase));
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
            var sig = string.Join("|", entries.Select(e => e.Key + ":" + e.Windows.Count + (e.Windows.Contains(fg) ? "*" : "")));
            // pencere sirasini da sakla (tiklama davranisi icin) ama UI'yi sadece gorunum degisince yenile
            currentEntries = entries;
            if (dragging || sig == lastSig) return; // surukleme sirasinda ikonlari yeniden kurma
            lastSig = sig;

            appsPanel.Children.Clear();
            appMag.Clear();
            foreach (var e in entries)
            {
                var img = new Image { Source = GetIcon(e.IconPath), Width = Settings.Current.IconSize, Height = Settings.Current.IconSize };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                int ind = e.Windows.Count == 0 ? 0 : (e.Windows.Contains(fg) ? 2 : 1);
                string key = e.Key;
                var item = MakeItem(img, e.Name, () => OnAppClick(key), ind, key, e.Windows.Count > 0);
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
            Refresh();
            var e = currentEntries.FirstOrDefault(x => x.Key == key);
            if (e == null) return;
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
            scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

            taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
            callbackMsg = Native.RegisterWindowMessage("FloatingDockAppBar");
            HideTaskbar();
            if (WinKeyHook.Enabled) SetWinKey(true);
            // Not: ekran alani ayrilmiyor (appbar yok) -> buyutulmus pencereler ekranin en altina kadar uzanir.
            Reposition();
            var ah = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            ah.Tick += (s2, e2) => AutoHideTick();
            ah.Start();
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
            tray = Native.FindWindow("Shell_TrayWnd", null);
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

            // Windows 10: gorev cubugu otomatik gizleye alinip hemen saklaninca Explorer calisma alanini
            // guncellemiyor, alttaki ~40px bos kaliyor ve buyutulmus pencereler oraya inmiyor.
            // Calisma alanini tam ekran yapiyoruz; Explorer gec gelip geri alirsa birkac kez daha deniyoruz.
            EnsureFullWorkArea();
            foreach (var ms in new[] { 400, 1500, 4000 })
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                t.Tick += (s, e) => { t.Stop(); EnsureFullWorkArea(); };
                t.Start();
            }
        }

        Native.RECT? origWorkArea;

        /// <summary>Ana ekranin calisma alani ekranin tamami degilse tam ekrana ayarlar (buyutulmus pencereler yeniden boyutlanir).</summary>
        void EnsureFullWorkArea()
        {
            if (restored) return;
            try
            {
                int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
                var cur = new Native.RECT();
                if (!Native.SystemParametersInfo(Native.SPI_GETWORKAREA, 0, ref cur, 0)) return;
                if (cur.left == 0 && cur.top == 0 && cur.right == sw && cur.bottom == sh) return;
                origWorkArea ??= cur;
                var full = new Native.RECT { left = 0, top = 0, right = sw, bottom = sh };
                Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref full, Native.SPIF_SENDCHANGE);
            }
            catch { }
        }

        void KeepTaskbarHidden()
        {
            if (restored) return;
            var t = Native.FindWindow("Shell_TrayWnd", null);
            if (t != IntPtr.Zero && Native.IsWindowVisible(t)) Native.ShowWindow(t, 0);
            EnsureFullWorkArea();
        }

        public void RestoreTaskbar()
        {
            if (restored) return;
            restored = true;
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    var a = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = hwnd };
                    Native.SHAppBarMessage(Native.ABM_REMOVE, ref a);
                }
                tray = Native.FindWindow("Shell_TrayWnd", null);
                if (tray != IntPtr.Zero)
                {
                    int st = origTrayState < 0 ? Native.ABS_ALWAYSONTOP : origTrayState;
                    var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = tray, lParam = (IntPtr)st };
                    Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
                    Native.ShowWindow(tray, 5);
                }
                // calisma alanini biz degistirdiysek eski haline dondur (gorev cubugu yeniden yer kaplasin)
                if (origWorkArea is Native.RECT wa)
                    Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref wa, Native.SPIF_SENDCHANGE);
                if (File.Exists(StateFile)) File.Delete(StateFile);
            }
            catch { }
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

        void Reposition()
        {
            if (hwnd == IntPtr.Zero || ActualWidth <= 0) return;
            double sw = Native.GetSystemMetrics(0) / scale;
            Left = Math.Round((sw - ActualWidth) / 2);
            BeginAnimation(TopProperty, null);
            Top = dockHidden ? HiddenTop() : ShownTop();
        }

        // ---------------- Akilli gizleme ----------------
        bool dockHidden;
        DateTime lastWanted = DateTime.Now;

        double ScreenBottomDip() => Native.GetSystemMetrics(1) / scale;
        double ShownTop() => Math.Round(ScreenBottomDip() - DockMargin - ActualHeight + PadBottom);
        double HiddenTop() => Math.Round(ScreenBottomDip() + 6);

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
            var a = new DoubleAnimation(hide ? HiddenTop() : ShownTop(), TimeSpan.FromMilliseconds(hide ? 200 : 260))
            {
                EasingFunction = hide ? (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseIn }
                                      : new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(TopProperty, a);
        }

        bool ShouldShow()
        {
            if (!Settings.Current.AutoHide) return true;
            // dock ile etkilesim suruyorsa her zaman goster
            if (dragging || IsMouseOver || preview.IsVisible || AnyFlyoutOpen) return true;
            if ((Content as FrameworkElement)?.ContextMenu?.IsOpen == true) return true;

            int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
            Native.GetCursorPos(out var c);
            var dr = new Native.RECT
            {
                left = (int)((Left + PadSide) * scale),
                right = (int)((Left + ActualWidth - PadSide) * scale),
                top = (int)((ShownTop() + PadTop) * scale),
                bottom = (int)((ShownTop() + ActualHeight - PadBottom) * scale)
            };
            bool cursorOnPrimary = c.x >= 0 && c.x < sw;

            var fg = Native.GetForegroundWindow();
            bool fgValid = false, fullscreen = false;
            Native.RECT fr = default;
            if (fg != IntPtr.Zero && fg != hwnd && Native.IsWindowVisible(fg) && !Native.IsIconic(fg))
            {
                var cls = new StringBuilder(64);
                Native.GetClassName(fg, cls, 64);
                var cn = cls.ToString();
                if (cn == "Windows.UI.Core.CoreWindow" || cn == "XamlExplorerHostIslandWindow") return true; // Baslat / arama / hizli ayarlar acik
                if (cn != "Progman" && cn != "WorkerW" && cn != "Shell_TrayWnd" && Native.GetWindowRect(fg, out fr))
                {
                    fgValid = true;
                    fullscreen = !Native.IsZoomed(fg) && fr.left <= 0 && fr.top <= 0 && fr.right >= sw && fr.bottom >= sh;
                }
            }

            // ekranin alt kenarina dokununca ortaya cik (tam ekran oyun/videoda degil)
            if (cursorOnPrimary && !fullscreen && c.y >= sh - 2) return true;
            // gorunurken fare dock seridinde ise acik kal
            if (!dockHidden && c.x >= dr.left - 10 && c.x <= dr.right + 10 && c.y >= dr.top - 20 && c.y <= sh) return true;
            if (!fgValid) return true; // masaustu: goster

            // on plandaki pencere dock'un alanina degiyor mu?
            bool overlap = fr.left < dr.right && fr.right > dr.left && fr.top < dr.bottom && fr.bottom > dr.top;
            return !overlap;
        }
    }
}
