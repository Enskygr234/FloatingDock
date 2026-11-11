using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>
    /// Masaustu bilesenleri (kil): birincil ekranin saginda hava, calan muzik ve "Bugun" kartlari.
    /// Pencerelerin her zaman arkasinda durur (z-sirasinin en altinda), odak calmaz, Alt-Tab'de gorunmez.
    /// </summary>
    class DesktopWidgets : Window
    {
        const double CardWidth = 312;
        static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        readonly DockWindow dock;
        readonly StackPanel stack = new StackPanel();
        readonly DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly Action weatherHandler;
        readonly Action<MediaInfo> mediaHandler;
        readonly Action timerHandler;
        IntPtr hwnd;

        // muzik karti: canli ilerleme icin
        Border progFill;
        Grid progTrack;
        TextBlock todayBody;

        public DesktopWidgets(DockWindow dock)
        {
            this.dock = dock;
            Title = "FloatingDock Widgets";
            Glass.SetupClay(this);
            Topmost = false;
            TextElement.SetFontFamily(this, Theme.Ui);
            TextElement.SetFontWeight(this, Theme.Regular);
            SizeToContent = SizeToContent.Height;
            Width = CardWidth + 2 * Theme.ShadowPad;
            stack.Margin = new Thickness(Theme.ShadowPad);
            Content = stack;

            weatherHandler = () => Dispatcher.BeginInvoke(new Action(Build));
            mediaHandler = m => Dispatcher.BeginInvoke(new Action(Build));
            timerHandler = () => Dispatcher.BeginInvoke(new Action(UpdateToday));
            Weather.Changed += weatherHandler;
            Hub.MediaChanged += mediaHandler;
            IslandTimer.Changed += timerHandler;
            tick.Tick += (s, e) => { UpdateProgress(); UpdateToday(); };
            tick.Start();
            Build();

            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Glass.NoActivate(hwnd);
                HwndSource.FromHwnd(hwnd).AddHook(WndProc);
                Place();
            };
            SizeChanged += (s, e) => Place();
            Closed += (s, e) =>
            {
                tick.Stop();
                Weather.Changed -= weatherHandler;
                Hub.MediaChanged -= mediaHandler;
                IslandTimer.Changed -= timerHandler;
            };
        }

        /// <summary>Ekranin sag ustune, ust cubugun altina yerles.</summary>
        public void Place()
        {
            if (hwnd == IntPtr.Zero) return;
            var scr = dock.Screen;
            Screens.Place(this, scr, sc => (scr.Bounds.right - (ActualWidth - Theme.ShadowPad + 24) * sc,
                                            scr.Bounds.top + (TopBar.BarTop + TopBar.BarHeight + 24 - Theme.ShadowPad) * sc));
            SetWindowPos(hwnd, (IntPtr)1, 0, 0, 0, 0, 0x1 | 0x2 | 0x10); // HWND_BOTTOM
        }

        // her zaman en altta: baska bir pencere one gelse de z-sirasi degismesin
        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0046) // WM_WINDOWPOSCHANGING
            {
                var wp = Marshal.PtrToStructure<WINDOWPOS>(l);
                if ((wp.flags & 0x4) == 0) { wp.hwndInsertAfter = (IntPtr)1; Marshal.StructureToPtr(wp, l, false); } // SWP_NOZORDER degilse en alta
            }
            return IntPtr.Zero;
        }

        // ---------------- kartlar ----------------
        void Build()
        {
            stack.Children.Clear();
            progFill = null; progTrack = null;
            var w = Weather.Current;
            if (w != null) stack.Children.Add(WeatherCard(w));
            var m = Hub.CurrentMedia;
            if (m != null) stack.Children.Add(MusicCard(m));
            stack.Children.Add(TodayCard());
            UpdateToday();
            UpdateProgress();
        }

        Grid Card(UIElement content, Color fill, Action click, string name, double padding = 20)
        {
            var g = Theme.Clay(content, Theme.Brush(fill), Theme.RadiusXl, new Thickness(padding), true, 30, 10);
            g.Margin = new Thickness(0, 0, 0, 16);
            if (click != null)
            {
                g.Cursor = System.Windows.Input.Cursors.Hand;
                g.MouseLeftButtonUp += (s, e) => { if (!e.Handled) click(); };
                g.ToolTip = name;
                System.Windows.Automation.AutomationProperties.SetName(g, name);
            }
            return g;
        }

        static TextBlock T(string s, double size, FontWeight w, Brush fg) => new TextBlock { Text = s, FontSize = size, FontWeight = w, Foreground = fg, FontFamily = Theme.Ui, TextTrimming = TextTrimming.CharacterEllipsis };

        UIElement WeatherCard(WeatherData w)
        {
            var (fill, ink) = Theme.PastelColors(Theme.Pastel.Sky);
            var inkB = Theme.Brush(ink);
            var sp = new StackPanel();
            var top = new Grid();
            var left = new StackPanel();
            left.Children.Add(T(w.Place, 13, Theme.Bold, inkB));
            var temp = T(Weather.Temp(w.Temp), 56, Theme.Heavy, inkB);
            temp.Margin = new Thickness(0, -2, 0, -6);
            left.Children.Add(temp);
            var today = w.Days.Count > 0 ? w.Days[0] : null;
            left.Children.Add(T(Weather.Describe(w.Code) + (today != null ? " · ↑" + Weather.Temp(today.Max) + " ↓" + Weather.Temp(today.Min) : ""), 14, Theme.Bold, inkB));
            top.Children.Add(left);
            var ic = Weather.Icon(w.Code, w.IsDay, 64, inkB);
            ic.HorizontalAlignment = HorizontalAlignment.Right; ic.VerticalAlignment = VerticalAlignment.Top;
            top.Children.Add(ic);
            sp.Children.Add(top);
            var nc = Weather.NowcastText(w);
            if (nc != null)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 14, Foreground = inkB, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                row.Children.Add(T(nc, 13, Theme.Bold, inkB));
                sp.Children.Add(new Border { Margin = new Thickness(0, 14, 0, 0), CornerRadius = new CornerRadius(Theme.RadiusMd), Background = new SolidColorBrush(Color.FromArgb(Theme.Dark ? (byte)40 : (byte)180, 255, 255, 255)), Padding = new Thickness(12, 9, 12, 10), Child = row });
            }
            return Card(sp, fill, () => CalendarFlyout.OpenWeather?.Invoke(), "Hava durumu: " + w.Place + ", " + Weather.Temp(w.Temp));
        }

        UIElement MusicCard(MediaInfo m)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var art = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(Theme.RadiusLg), Background = new LinearGradientBrush(Color.FromRgb(0xF4, 0x72, 0xB6), Theme.Accent, 45), Effect = Theme.ClayShadow(14, 5) };
            var img = m.Art != null ? IslandWindow.Decode(m.Art, 144) : null;
            if (img != null) art.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            g.Children.Add(art);
            var right = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(T(m.Title, 16, Theme.Heavy, Theme.Fg()));
            right.Children.Add(T(m.Artist + " · " + IslandWindow.FriendlySource(m.SourceId), 13, Theme.Regular, Theme.Fg(Theme.TextSecondary)));
            progTrack = new Grid { Height = 6, Margin = new Thickness(0, 10, 0, 0) };
            progTrack.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Theme.Ink(Theme.StrokeDefault) });
            progFill = new Border { CornerRadius = new CornerRadius(3), Background = Theme.Brush(Theme.Accent), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            progTrack.Children.Add(progFill);
            right.Children.Add(progTrack);
            var ctl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            ctl.Children.Add(Btn("", "Önceki", () => Hub.Media.Prev(), false));
            ctl.Children.Add(Btn(m.Playing ? "" : "", "Oynat / duraklat", () => Hub.Media.PlayPause(), true));
            ctl.Children.Add(Btn("", "Sonraki", () => Hub.Media.Next(), false));
            right.Children.Add(ctl);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            return Card(g, Theme.PanelTint, () => Hub.OpenPlayer?.Invoke(), "Şimdi çalıyor: " + m.Title, 18);
        }

        Border Btn(string glyph, string tip, Action a, bool primary)
        {
            var t = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = primary ? 13 : 14, Foreground = primary ? Theme.Brush(Theme.OnAccent) : Theme.Fg(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = Flyout.Clickable(new Border { Width = 36, Height = 36, Child = t }, a, 18, primary ? (byte)0 : Theme.SurfaceIdle);
            if (primary) b.Background = Theme.Brush(Theme.Accent);
            b.Margin = new Thickness(0, 0, 6, 0);
            b.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.MouseLeftButtonUp += (s, e) => e.Handled = true; // kartin tiklamasi (oynaticiyi ac) tetiklenmesin
            return b;
        }

        UIElement TodayCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(T("Bugün", 15, Theme.Heavy, Theme.Fg()));
            todayBody = new TextBlock { Margin = new Thickness(0, 10, 0, 0), FontFamily = Theme.Ui, FontSize = 13.5, FontWeight = Theme.Bold, Foreground = Theme.Fg(Theme.TextSecondary), TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
            sp.Children.Add(todayBody);
            return Card(sp, Theme.PanelTint, () => dock.BarOpen("calendar", null), "Takvim ve bildirimler", 18);
        }

        void UpdateToday()
        {
            if (todayBody == null) return;
            var lines = new System.Collections.Generic.List<string>();
            lines.Add(DateTime.Now.ToString("dddd, d MMMM", Tr));
            if (IslandTimer.Active) lines.Add(IslandTimer.Label + " · " + IslandTimer.Format(IslandTimer.Value) + (IslandTimer.Paused ? " (duraklatıldı)" : ""));
            if (Hub.Unread > 0) lines.Add(Hub.Unread + " okunmamış bildirim");
            if (Battery.HasBattery) lines.Add("Pil %" + Battery.Percent + (Battery.Charging ? " · şarj oluyor" : Battery.Remaining is TimeSpan r ? " · " + (int)r.TotalHours + " sa " + r.Minutes + " dk" : ""));
            if (lines.Count == 1) lines.Add("Sakin bir gün: bildirim yok.");
            var text = string.Join("\n", lines);
            if (todayBody.Text != text) todayBody.Text = text;
        }

        void UpdateProgress()
        {
            var m = Hub.CurrentMedia;
            if (progFill == null || progTrack == null || m == null || m.Duration.TotalSeconds < 1) return;
            progFill.Width = Math.Max(0, progTrack.ActualWidth * Math.Min(1, m.LivePosition.TotalSeconds / m.Duration.TotalSeconds));
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
