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
        public List<IntPtr> Windows = new List<IntPtr>();
    }

    class DockWindow : Window
    {
        static readonly Color Accent = Color.FromRgb(0x8A, 0x7C, 0xFF);
        const double ItemSize = 44, DockMargin = 10;
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
            Glass.Setup(this);
            preview = new PreviewWindow(
                key => currentEntries.FirstOrDefault(x => x.Key == key),
                () => Top,
                Activate,
                GetIcon,
                () => { lastSig = ""; Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background); });

            LoadPinned();
            Content = BuildUi();
            Width = 400; Height = 54;
            SourceInitialized += OnSourceInitialized;
            SizeChanged += (s, e) => Reposition();
            Closed += (s, e) => { preview.Close(); control.Close(); calendar.Close(); RestoreTaskbar(); };

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            t.Tick += (s, e) => Refresh();
            t.Start();
            Refresh();
        }

        // ---------------- Arayuz ----------------
        UIElement BuildUi()
        {
            row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 5, 8, 5), Background = Brushes.Transparent };
            row.MouseMove += (s, e) => Magnify(e.GetPosition(row).X);
            row.MouseLeave += (s, e) => { ResetMagnify(); preview.HoverOther(); };
            row.Children.Add(MakeItem(StartIcon(), "Başlat", () => SendKeys(VK_LWIN), 0));
            row.Children.Add(MakeItem(Glyph("", 17), "Ara", () => SendKeys(VK_LWIN, 0x53), 0));
            row.Children.Add(Sep());
            appsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(appsPanel);
            row.Children.Add(Sep());

            var qs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            qs.Children.Add(Glyph("", 15));
            qs.Children.Add(new Border { Width = 12 });
            qs.Children.Add(Glyph("", 15));
            FrameworkElement qsPill = null, clockPill = null;
            qsPill = MakePill(qs, "Wi-Fi ve ses", () => Toggle(control, qsPill));
            row.Children.Add(qsPill);

            var font = new FontFamily("Segoe UI Variable Text, Segoe UI");
            var clock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            timeText = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, FontFamily = font, HorizontalAlignment = HorizontalAlignment.Right };
            dateText = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), FontSize = 11, FontFamily = font, HorizontalAlignment = HorizontalAlignment.Right };
            clock.Children.Add(timeText);
            clock.Children.Add(dateText);
            clockPill = MakePill(clock, "Takvim", () => Toggle(calendar, clockPill));
            row.Children.Add(clockPill);
            UpdateClock();

            return new Border { Background = Brushes.Transparent, Child = row, ContextMenu = BuildMenu() };
        }

        // ---------------- Kendi panellerimiz (hizli ayarlar / takvim) ----------------
        readonly ControlCenter control = new ControlCenter();
        readonly CalendarFlyout calendar = new CalendarFlyout();
        bool AnyFlyoutOpen => control.IsOpen || calendar.IsOpen;

        void Toggle(Flyout f, FrameworkElement anchor)
        {
            preview.HidePreview();
            if (f.IsOpen || f.JustClosed) { f.HideFlyout(); return; }
            (f == control ? (Flyout)calendar : control).HideFlyout();
            var c = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
            f.ShowAt(c.X, ShownTop());
        }

        ContextMenu BuildMenu()
        {
            var m = new ContextMenu();
            void Add(string text, Action a) { var i = new MenuItem { Header = text }; i.Click += (s, e) => a(); m.Items.Add(i); }
            Add("Görev Yöneticisi", () => Launch("taskmgr.exe"));
            Add("Dosya Gezgini", () => Launch("explorer.exe"));
            Add("Kişiselleştirme ayarları", () => Launch("ms-settings:personalization"));
            m.Items.Add(new Separator());
            var auto = new MenuItem { Header = "Windows açılışında başlat", IsCheckable = true, IsChecked = AutoStart.IsEnabled };
            auto.Click += (s, e) => { AutoStart.Set(auto.IsChecked); auto.IsChecked = AutoStart.IsEnabled; };
            m.Items.Add(auto);
            m.Items.Add(new Separator());
            Add("Dock'u kapat (eski görev çubuğuna dön)", Close);
            return m;
        }

        void Magnify(double mouseX)
        {
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
            var bg = new Border { CornerRadius = new CornerRadius(8) };
            grid.Children.Add(bg);
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
                    Height = 3,
                    Width = indicator == 2 ? 16 : 6,
                    CornerRadius = new CornerRadius(1.5),
                    Background = indicator == 2 ? new SolidColorBrush(Accent) : new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 1)
                });
            }
            Hook(grid, bg, st, click);
            return grid;
        }

        FrameworkElement MakePill(UIElement content, string tip, Action click)
        {
            var grid = new Grid { Height = ItemSize, Margin = new Thickness(2, 0, 2, 0), Background = Brushes.Transparent, ToolTip = tip };
            var bg = new Border { CornerRadius = new CornerRadius(8) };
            grid.Children.Add(bg);
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
            el.MouseEnter += (s, e) => bg.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            el.MouseLeave += (s, e) => { bg.Background = Brushes.Transparent; Anim(st, 1); };
            el.MouseLeftButtonDown += (s, e) => Anim(st, 0.86);
            el.MouseLeftButtonUp += (s, e) => { Anim(st, 1); click?.Invoke(); };
        }

        static void Anim(ScaleTransform st, double to)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        static TextBlock Glyph(string g, double size) => new TextBlock
        {
            Text = g,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = size,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        static UIElement StartIcon()
        {
            var c = new Canvas { Width = 22, Height = 22 };
            var brushes = new[]
            {
                new LinearGradientBrush(Color.FromRgb(0x6F, 0xD6, 0xFF), Color.FromRgb(0x6E, 0x9C, 0xFF), 45),
                new LinearGradientBrush(Color.FromRgb(0x7E, 0xA8, 0xFF), Color.FromRgb(0x8A, 0x7C, 0xFF), 45),
                new LinearGradientBrush(Color.FromRgb(0x72, 0xA0, 0xFF), Color.FromRgb(0x8A, 0x7C, 0xFF), 45),
                new LinearGradientBrush(Color.FromRgb(0x96, 0x80, 0xFF), Color.FromRgb(0xC0, 0x6C, 0xF0), 45),
            };
            for (int i = 0; i < 4; i++)
            {
                var r = new Rectangle { Width = 10, Height = 10, RadiusX = 2.5, RadiusY = 2.5, Fill = brushes[i] };
                Canvas.SetLeft(r, (i % 2) * 12);
                Canvas.SetTop(r, (i / 2) * 12);
                c.Children.Add(r);
            }
            return c;
        }

        static UIElement Sep() => new Border
        {
            Width = 1,
            Height = 24,
            Margin = new Thickness(6, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
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
                pinned.Add(new AppEntry { Key = string.IsNullOrEmpty(target) ? lnk : target, Name = name, LaunchPath = lnk, IconPath = lnk, Pinned = true });
            }
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
            var entries = pinned.Select(p => new AppEntry { Key = p.Key, Name = p.Name, LaunchPath = p.LaunchPath, IconPath = p.IconPath, Pinned = true }).ToList();
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
            var sig = string.Join("|", entries.Select(e => e.Key + ":" + e.Windows.Count + (e.Windows.Contains(fg) ? "*" : "")));
            // pencere sirasini da sakla (tiklama davranisi icin) ama UI'yi sadece gorunum degisince yenile
            currentEntries = entries;
            if (sig == lastSig) return;
            lastSig = sig;

            appsPanel.Children.Clear();
            appMag.Clear();
            foreach (var e in entries)
            {
                var img = new Image { Source = GetIcon(e.IconPath), Width = 28, Height = 28 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                int ind = e.Windows.Count == 0 ? 0 : (e.Windows.Contains(fg) ? 2 : 1);
                string key = e.Key;
                appsPanel.Children.Add(MakeItem(img, e.Name, () => OnAppClick(key), ind, key, e.Windows.Count > 0));
            }
            FitToContent();
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
            Glass.Apply(hwnd);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

            taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
            callbackMsg = Native.RegisterWindowMessage("FloatingDockAppBar");
            HideTaskbar();
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
        }

        void KeepTaskbarHidden()
        {
            if (restored) return;
            var t = Native.FindWindow("Shell_TrayWnd", null);
            if (t != IntPtr.Zero && Native.IsWindowVisible(t)) Native.ShowWindow(t, 0);
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
        double ShownTop() => Math.Round(ScreenBottomDip() - DockMargin - ActualHeight);
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
            // dock ile etkilesim suruyorsa her zaman goster
            if (IsMouseOver || preview.IsVisible || AnyFlyoutOpen) return true;
            if ((Content as FrameworkElement)?.ContextMenu?.IsOpen == true) return true;

            int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
            Native.GetCursorPos(out var c);
            var dr = new Native.RECT
            {
                left = (int)(Left * scale),
                right = (int)((Left + ActualWidth) * scale),
                top = (int)(ShownTop() * scale),
                bottom = (int)((ShownTop() + ActualHeight) * scale)
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
