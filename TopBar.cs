using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FloatingDock
{
    /// <summary>
    /// Kil kabugun ust kismi: iki kucuk ada (her ekranda, o ekranin dock'una bagli).
    /// Sol ada: Baslat + ondeki uygulama + sanal masaustleri. Sag ada: hava, tepsi, hizli ayarlar, saat, okunmamis bildirim.
    /// Ortada muzik / bildirim adasi (ayri pencere) durur. Adalarin arasi tiklamayi gecirir.
    /// Ekranin ust seridini Windows'a "ayrilmis alan" (appbar) olarak bildirir: buyutulmus pencereler cubugun altinda baslar.
    /// </summary>
    class TopBar : Window
    {
        public const double BarTop = 8, BarHeight = 40, Side = 12;
        static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        static readonly List<TopBar> all = new List<TopBar>();

        readonly DockWindow dock;
        IntPtr hwnd;
        double scale = 1;
        uint callbackMsg;
        bool appBar;

        readonly TextBlock appName = new TextBlock(), appSub = new TextBlock();
        readonly StackPanel desks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        readonly TextBlock time = new TextBlock(), date = new TextBlock(), unread = new TextBlock();
        readonly Border unreadBadge;
        readonly TextBlock volGlyph, wifiGlyph;
        readonly StackPanel battery = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock batPct = new TextBlock();
        readonly Border batFill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        readonly Border weatherChip;
        readonly StackPanel weatherBox = new StackPanel { Orientation = Orientation.Horizontal };
        readonly TextBlock weatherTemp = new TextBlock();
        readonly Border weatherIcon = new Border { Width = 20, Height = 20 };
        public FrameworkElement WeatherAnchor => weatherChip;
        readonly DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        readonly Action weatherHandler, historyHandler, prefsHandler;

        /// <summary>Ayarlardaki ac/kapa secenekleri (uygulama adi, masaustleri, hava, tarih, saniye): yeniden baslatmadan uygulanir.</summary>
        void ApplyPrefs()
        {
            var s = Settings.Current;
            appName.Visibility = s.TopBarAppName ? Visibility.Visible : Visibility.Collapsed;
            if (!s.TopBarAppName) appSub.Visibility = Visibility.Collapsed;
            else appSub.Visibility = appSub.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            desks.Visibility = s.TopBarDesktops ? Visibility.Visible : Visibility.Collapsed;
            date.Visibility = s.TopBarDate ? Visibility.Visible : Visibility.Collapsed;
            lastSecond = -1;
            ShowWeather();
        }

        public TopBar(DockWindow dock)
        {
            this.dock = dock;
            Title = "FloatingDock TopBar";
            Glass.SetupClay(this);
            TextElement.SetFontFamily(this, Theme.Ui);
            TextElement.SetFontWeight(this, Theme.Regular);
            Height = BarTop + BarHeight + Theme.ShadowPad;
            Width = 800;

            // ---- sol ada: Baslat + ondeki uygulama + masaustleri ----
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 8, 0) };
            var spark = new Path
            {
                Data = Geometry.Parse("M12 2c1.2 4.6 2.4 5.8 7 7-4.6 1.2-5.8 2.4-7 7-1.2-4.6-2.4-5.8-7-7 4.6-1.2 5.8-2.4 7-7z M18.5 16a2.5 2.5 0 1 1 0 5 2.5 2.5 0 1 1 0-5z"),
                Fill = Theme.Brush(Theme.OnAccent), Width = 15, Height = 15, Stretch = Stretch.Uniform
            };
            var startTile = new Grid { Width = 30, Height = 30 };
            startTile.Children.Add(new Border { CornerRadius = new CornerRadius(15), Background = Theme.Brush(Theme.Accent), Effect = Theme.ClayShadow(8, 2) });
            startTile.Children.Add(Theme.ClayLight(15));
            startTile.Children.Add(spark);
            left.Children.Add(Chip(startTile, "Başlat", () => dock.BarOpen("start", null), 15, 0));
            appName.FontWeight = Theme.Heavy; appName.FontSize = 13.5; appName.Foreground = Theme.Fg(); appName.TextTrimming = TextTrimming.CharacterEllipsis; appName.MaxWidth = 220;
            appName.VerticalAlignment = VerticalAlignment.Center; appName.Margin = new Thickness(10, 0, 0, 1);
            appSub.FontWeight = Theme.Bold; appSub.FontSize = 12.5; appSub.Foreground = Theme.Fg(Theme.TextTertiary); appSub.VerticalAlignment = VerticalAlignment.Center; appSub.Margin = new Thickness(6, 0, 0, 1);
            left.Children.Add(appName);
            left.Children.Add(appSub);
            desks.Margin = new Thickness(10, 0, 0, 0);
            left.Children.Add(desks);

            // ---- sag ada: hava | tepsi | hizli ayarlar | saat + bildirim ----
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0) };
            var (skyFill, skyInk) = Theme.PastelColors(Theme.Pastel.Sky);
            weatherTemp.FontWeight = Theme.Heavy; weatherTemp.FontSize = 13; weatherTemp.Foreground = Theme.Brush(skyInk); weatherTemp.Margin = new Thickness(5, 0, 0, 0);
            weatherTemp.VerticalAlignment = VerticalAlignment.Center;
            weatherIcon.Width = weatherIcon.Height = 17;
            weatherBox.Children.Add(weatherIcon); weatherBox.Children.Add(weatherTemp);
            weatherChip = Chip(Pad(weatherBox, 10), "Hava durumu", () => dock.BarOpen("weather", weatherChip), 15, 0);
            weatherChip.Background = Theme.Brush(skyFill); weatherChip.Height = 30;
            weatherChip.Visibility = Visibility.Collapsed;
            right.Children.Add(weatherChip);

            if (Settings.Current.TrayButton)
            {
                Border tray = null;
                tray = Chip(new Border { Width = 30, Height = 30, Child = Glyph("", 11) }, "Arka plan uygulamaları", () => dock.BarOpen("tray", tray), 15, 0);
                right.Children.Add(Gap(tray));
            }

            var qs = new StackPanel { Orientation = Orientation.Horizontal };
            wifiGlyph = Glyph("", 14); qs.Children.Add(wifiGlyph);
            volGlyph = Glyph("", 14); volGlyph.Margin = new Thickness(10, 0, 0, 0); qs.Children.Add(volGlyph);
            // pil (yalniz dizustu): cizilmis govde + yuzde
            var body = new Border { Width = 20, Height = 11, CornerRadius = new CornerRadius(3.5), BorderThickness = new Thickness(1.6), BorderBrush = Theme.Fg(), Padding = new Thickness(1.6), Child = batFill, VerticalAlignment = VerticalAlignment.Center };
            battery.Children.Add(body);
            battery.Children.Add(new Border { Width = 2, Height = 4, CornerRadius = new CornerRadius(0, 1, 1, 0), Background = Theme.Fg(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 0, 0) });
            batPct.FontWeight = Theme.Heavy; batPct.FontSize = 12.5; batPct.Foreground = Theme.Fg(); batPct.Margin = new Thickness(4, 0, 0, 0); batPct.VerticalAlignment = VerticalAlignment.Center;
            battery.Children.Add(batPct);
            battery.Margin = new Thickness(10, 0, 0, 0);
            qs.Children.Add(battery);
            Border qsChip = null;
            qsChip = Chip(Pad(qs, 11), "Hızlı ayarlar: Wi-Fi, ses, pil", () => dock.BarOpen("qs", qsChip), 15, 0);
            qsChip.Height = 30;
            right.Children.Add(Gap(qsChip));

            // saat tek satir: "10:32 · Çar 11 Kas" + okunmamis sayisi
            var clock = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            time.FontWeight = Theme.Heavy; time.FontSize = 13.5; time.Foreground = Theme.Fg(); time.VerticalAlignment = VerticalAlignment.Center;
            Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
            date.FontWeight = Theme.Bold; date.FontSize = 12.5; date.Foreground = Theme.Fg(Theme.TextSecondary); date.VerticalAlignment = VerticalAlignment.Center; date.Margin = new Thickness(7, 0, 0, 0);
            clock.Children.Add(time); clock.Children.Add(date);
            unread.FontWeight = Theme.Heavy; unread.FontSize = 11.5; unread.Foreground = Brushes.White; unread.HorizontalAlignment = HorizontalAlignment.Center; unread.VerticalAlignment = VerticalAlignment.Center;
            unreadBadge = new Border { MinWidth = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.FromRgb(0xBE, 0x18, 0x5D)), Child = unread, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            clock.Children.Add(unreadBadge);
            Border clockChip = null;
            clockChip = Chip(Pad(clock, 11), "Takvim ve bildirimler", () => dock.BarOpen("calendar", clockChip), 15, 0);
            clockChip.Height = 30;
            right.Children.Add(Gap(clockChip));
            if (Settings.Current.TopBarStyle == "bar")
            {
                // tek tam cubuk: sol ve sag gruplar ayni hapin iki ucunda
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(left);
                Grid.SetColumn(right, 2); g.Children.Add(right);
                var full = Island(g);
                full.Margin = new Thickness(Side, BarTop, Side, Theme.ShadowPad);
                Content = new Grid { Children = { full } };
            }
            else
            {
                var leftIsland = Island(left);
                leftIsland.HorizontalAlignment = HorizontalAlignment.Left;
                leftIsland.Margin = new Thickness(Side, BarTop, 0, Theme.ShadowPad);
                var rightIsland = Island(right);
                rightIsland.HorizontalAlignment = HorizontalAlignment.Right;
                rightIsland.Margin = new Thickness(0, BarTop, Side, Theme.ShadowPad);
                // adalarin arasi seffaf ve tiklamayi gecirir (Background yok)
                Content = new Grid { Children = { leftIsland, rightIsland } };
            }

            prefsHandler = () => Dispatcher.BeginInvoke(new Action(ApplyPrefs));
            Settings.Current.Changed += prefsHandler;
            ApplyPrefs();
            weatherHandler = () => Dispatcher.BeginInvoke(new Action(ShowWeather));
            Weather.Changed += weatherHandler;
            historyHandler = () => Dispatcher.BeginInvoke(new Action(ShowUnread));
            Hub.HistoryChanged += historyHandler;
            ShowWeather(); ShowUnread();
            tick.Tick += (s, e) => Tick();
            tick.Start();
            Tick();

            SourceInitialized += OnSourceInitialized;
            Closed += (s, e) =>
            {
                tick.Stop();
                Weather.Changed -= weatherHandler;
                Hub.HistoryChanged -= historyHandler;
                Settings.Current.Changed -= prefsHandler;
                RemoveAppBar();
                all.Remove(this);
            };
            all.Add(this);
        }

        // ---------------- parcalar ----------------
        static TextBlock Glyph(string g, double size) => new TextBlock
        {
            Text = g, FontFamily = Theme.Icons, FontSize = size, Foreground = Theme.Fg(), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };

        /// <summary>Kucuk kil ada: beyaz dolgu, yumusak golge, tam yuvarlak uclar.</summary>
        static Grid Island(UIElement content)
        {
            var g = new Grid { Height = BarHeight, VerticalAlignment = VerticalAlignment.Top };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(BarHeight / 2), Background = Theme.Brush(Theme.DockFill), Effect = Theme.ClayShadow(18, 5) });
            g.Children.Add(Theme.ClayLight(BarHeight / 2));
            g.Children.Add(content);
            return g;
        }

        static UIElement Pad(UIElement c, double h) => new Border { Padding = new Thickness(h, 0, h, 0), Child = c, VerticalAlignment = VerticalAlignment.Center };
        static UIElement Gap(UIElement c) { ((FrameworkElement)c).Margin = new Thickness(4, 0, 0, 0); return c; }

        /// <summary>Cubuktaki tiklanabilir kapsul (Flyout.Clickable: uzerine gelince aydinlanir, basinca ezilir, klavyeyle de calisir).</summary>
        static Border Chip(UIElement content, string name, Action click, double radius, byte idle)
        {
            var b = Flyout.Clickable(content, click, radius, idle);
            b.VerticalAlignment = VerticalAlignment.Center;
            b.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            return b;
        }

        // ---------------- canli bilgiler ----------------
        int lastSecond = -1;

        void Tick()
        {
            var n = DateTime.Now;
            if (n.Second != lastSecond)
            {
                lastSecond = n.Second;
                time.Text = n.ToString(Settings.Current.TopBarSeconds ? "HH:mm:ss" : "HH:mm", Tr);
                date.Text = n.ToString("ddd d MMM", Tr);
                if (Audio.TryRead(out float v, out bool m)) volGlyph.Text = m || v <= 0.001f ? "" : v < 0.34f ? "" : v < 0.67f ? "" : "";
                if (Battery.HasBattery)
                {
                    int p = Math.Max(0, Battery.Percent);
                    battery.Visibility = Visibility.Visible;
                    batPct.Text = p.ToString();
                    batFill.Width = Math.Max(2, 14 * p / 100.0);
                    batFill.Background = Theme.Brush(Battery.Charging || p > 20 ? Theme.Success : Theme.Danger);
                }
                else battery.Visibility = Visibility.Collapsed;
                RenderDesks();
            }
            RenderApp();
        }

        void ShowWeather()
        {
            var w = Weather.Current;
            weatherChip.Visibility = w != null && Settings.Current.TopBarWeather ? Visibility.Visible : Visibility.Collapsed;
            if (w == null) return;
            weatherIcon.Child = Weather.Icon(w.Code, w.IsDay, 20, Theme.Brush(Theme.PastelColors(Theme.Pastel.Sky).ink));
            weatherTemp.Text = Weather.Temp(w.Temp);
            var nc = Weather.NowcastText(w);
            weatherChip.ToolTip = w.Place + " · " + Weather.Describe(w.Code) + (nc != null ? "\n" + nc : "");
        }

        void ShowUnread()
        {
            int u = Hub.Unread;
            unreadBadge.Visibility = u > 0 ? Visibility.Visible : Visibility.Collapsed;
            unread.Text = u > 99 ? "99+" : u.ToString();
        }

        IntPtr lastFg;
        string lastTitle;

        /// <summary>Ondeki uygulamanin adi ve acik pencere sayisi.</summary>
        void RenderApp()
        {
            var fg = Native.GetForegroundWindow();
            if (fg == lastFg && appName.Text.Length > 0) return;
            lastFg = fg;
            string name = "Masaüstü", sub = "";
            try
            {
                Native.GetWindowThreadProcessId(fg, out uint pid);
                // kendi panellerimiz (Baslat, hizli ayarlar...) one gelince onceki uygulamanin adi kalsin
                if (pid == (uint)Environment.ProcessId && appName.Text.Length > 0) return;
                var cls = new StringBuilder(64); Native.GetClassName(fg, cls, 64);
                var c = cls.ToString();
                bool shell = c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || pid == (uint)Environment.ProcessId;
                if (!shell && fg != IntPtr.Zero)
                {
                    using var p = Process.GetProcessById((int)pid);
                    string exe = null;
                    try { exe = p.MainModule?.FileName; } catch { }
                    var vi = exe != null ? FileVersionInfo.GetVersionInfo(exe) : null;
                    var desc = vi?.FileDescription;
                    // "Windows Terminal Host" gibi yardimci surec adlari yerine urun adi ("Windows Terminal")
                    if (vi != null && (string.IsNullOrWhiteSpace(desc) || desc.Contains("Host")) && !string.IsNullOrWhiteSpace(vi.ProductName) && !vi.ProductName.Contains("Operating System"))
                        desc = vi.ProductName;
                    name = !string.IsNullOrWhiteSpace(desc) ? desc : p.ProcessName;
                    int count = DockWindow.WindowCountOf(exe);
                    sub = count > 1 ? count + " pencere" : "";
                }
            }
            catch { }
            if (name == lastTitle && appSub.Text == sub) return;
            lastTitle = name;
            appName.Text = name;
            appSub.Text = sub;
            appSub.Visibility = sub.Length > 0 && Settings.Current.TopBarAppName ? Visibility.Visible : Visibility.Collapsed;
        }

        static string TitleOf(IntPtr h)
        {
            int len = Native.GetWindowTextLength(h);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 1);
            Native.GetWindowText(h, sb, sb.Capacity);
            var t = sb.ToString();
            return t.Length > 40 ? t.Substring(0, 39) + "…" : t;
        }

        // ---------------- sanal masaustleri ----------------
        string deskSig;

        void RenderDesks()
        {
            var (count, current) = VirtualDesktops.Read();
            var sig = count + ":" + current;
            if (sig == deskSig) return;
            deskSig = sig;
            desks.Children.Clear();
            var (lf, li) = Theme.PastelColors(Theme.Pastel.Lilac);
            for (int i = 0; i < count; i++)
            {
                int k = i;
                bool on = i == current;
                var t = new TextBlock { Text = (i + 1).ToString(), FontWeight = on ? Theme.Heavy : Theme.Bold, FontSize = 13, Foreground = on ? Theme.Brush(li) : Theme.Fg(Theme.TextSecondary), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                t.FontSize = 12;
                var b = Flyout.Clickable(new Border { MinWidth = on ? 32 : 24, Height = 24, Child = t }, () => VirtualDesktops.SwitchTo(k), 12, 0);
                if (on) b.Background = Theme.Brush(lf);
                else b.Background = Theme.Ink(Theme.SurfaceIdle);
                b.Tag = on ? (byte)0 : Theme.SurfaceIdle;
                b.Margin = new Thickness(0, 0, 4, 0);
                b.ToolTip = "Masaüstü " + (i + 1) + (on ? " (şu an)" : "");
                System.Windows.Automation.AutomationProperties.SetName(b, "Masaüstü " + (i + 1) + (on ? ", etkin" : ""));
                desks.Children.Add(b);
            }
            var add = Flyout.Clickable(new Border { Width = 24, Height = 24, Child = Glyph("", 10) }, VirtualDesktops.New, 12, 0);
            add.ToolTip = "Yeni masaüstü (Ctrl+Win+D)";
            System.Windows.Automation.AutomationProperties.SetName(add, "Yeni masaüstü");
            desks.Children.Add(add);
        }

        // ---------------- konum ve ayrilmis alan ----------------
        void OnSourceInitialized(object s, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Glass.NoActivate(hwnd);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            callbackMsg = Native.RegisterWindowMessage("FloatingDockTopBar");
            Place();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            scale = newDpi.DpiScaleX;
            Place();
        }

        /// <summary>Kendi ekraninin ustune yerles ve o seridi ayir.</summary>
        public void Place()
        {
            if (hwnd == IntPtr.Zero) return;
            var scr = dock.Screen;
            Screens.MoveTo(hwnd, scr.Bounds.left + 1, scr.Bounds.top + 1);
            scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Width = scr.Width / scale;
            UpdateLayout();
            Screens.MoveTo(hwnd, scr.Bounds.left, scr.Bounds.top);
            SetAppBar(scr);
        }

        /// <summary>Cubugun alt kenari (fiziksel piksel): paneller bunun altinda acilir.</summary>
        public double BottomPx => dock.Screen.Bounds.top + (BarTop + BarHeight) * scale;

        void SetAppBar(Screen scr)
        {
            var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = hwnd, uCallbackMessage = callbackMsg };
            if (!appBar) { Native.SHAppBarMessage(Native.ABM_NEW, ref abd); appBar = true; }
            int reserve = (int)Math.Ceiling((BarTop + BarHeight + 6) * scale);
            abd.uEdge = 1; // ABE_TOP
            abd.rc = new Native.RECT { left = scr.Bounds.left, right = scr.Bounds.right, top = scr.Bounds.top, bottom = scr.Bounds.top + reserve };
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
            abd.rc.bottom = abd.rc.top + reserve;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
        }

        void RemoveAppBar()
        {
            if (!appBar || hwnd == IntPtr.Zero) return;
            appBar = false;
            var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = hwnd };
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
        }

        /// <summary>Cokme / kapanista ayrilmis alanlari birak (Windows ust seridi geri kullansin).</summary>
        public static void ReleaseAll() { foreach (var b in all.ToList()) b.RemoveAppBar(); }

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (callbackMsg != 0 && msg == callbackMsg)
            {
                int code = (int)w;
                if (code == 1) SetAppBar(dock.Screen);       // ABN_POSCHANGED
                else if (code == 2) Topmost = l == IntPtr.Zero; // ABN_FULLSCREENAPP: tam ekran oyun/videoda ustte durma
            }
            return IntPtr.Zero;
        }
    }

    /// <summary>Windows sanal masaustleri: sayi ve etkin olan kayit defterinden okunur, gecis klavye kisayoluyla yapilir.</summary>
    static class VirtualDesktops
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

        public static (int count, int current) Read()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(Key);
                var ids = k?.GetValue("VirtualDesktopIDs") as byte[];
                var cur = k?.GetValue("CurrentVirtualDesktop") as byte[];
                if (cur == null)
                {
                    // Windows 10: etkin masaustu oturum anahtarinda
                    using var si = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\" + Process.GetCurrentProcess().SessionId + @"\VirtualDesktops");
                    cur = si?.GetValue("CurrentVirtualDesktop") as byte[];
                }
                if (ids == null || ids.Length < 16) return (1, 0);
                int n = ids.Length / 16, idx = 0;
                if (cur != null && cur.Length == 16)
                    for (int i = 0; i < n; i++)
                        if (ids.Skip(i * 16).Take(16).SequenceEqual(cur)) { idx = i; break; }
                return (n, idx);
            }
            catch { return (1, 0); }
        }

        public static void SwitchTo(int target)
        {
            var (_, cur) = Read();
            int d = target - cur;
            if (d == 0) return;
            byte arrow = d > 0 ? (byte)0x27 : (byte)0x25; // sag / sol ok
            for (int i = 0; i < Math.Abs(d); i++) Keys(0x11, 0x5B, arrow); // Ctrl+Win+ok
        }

        public static void New() => Keys(0x11, 0x5B, 0x44); // Ctrl+Win+D

        static void Keys(params byte[] keys)
        {
            foreach (var k in keys) Native.keybd_event(k, 0, 0, UIntPtr.Zero);
            for (int i = keys.Length - 1; i >= 0; i--) Native.keybd_event(keys[i], 0, 2, UIntPtr.Zero);
        }
    }
}
