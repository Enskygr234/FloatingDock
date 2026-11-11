using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>Ekranin ust ortasinda: calan medya ve gelen bildirimler (Dynamic Island tarzi).</summary>
    class IslandWindow : Window
    {
        const double WinW = 520, WinH = 190;
        static readonly Color DefaultAccent = Theme.AccentOnDark;
        static readonly FontFamily UiFont = Theme.Ui;
        static readonly FontFamily IconFont = Theme.Icons;
        static readonly Brush Dim = Theme.OnDark(190);

        readonly Func<string, ImageSource> icon;
        readonly MediaWatcher media = Hub.Media;
        readonly NotificationWatcher notifs = new NotificationWatcher();
        readonly SolidColorBrush accent = new SolidColorBrush(DefaultAccent);
        IntPtr hwnd;

        Border pill;
        FrameworkElement compactView, expandedView, notifView, osdView, batteryView, timerView, timerXView;
        readonly Dictionary<string, FrameworkElement> views = new Dictionary<string, FrameworkElement>();
        // ses / parlaklik gostergesi
        TextBlock osdGlyph, osdText;
        Border osdTrack, osdFill;
        float lastVol = -2; bool lastMute; int lastBright = -2; int tickNo; bool brightBusy;
        DateTime osdUntil = DateTime.MinValue;
        // pil
        Border batFill; TextBlock batTitle, batPct;
        bool? lastCharging; int lowWarned = 101;
        DateTime batUntil = DateTime.MinValue;
        // zamanlayici
        TextBlock tLabel, tTime, txLabel, txTime, txPause;
        FrameworkElement txAdd;
        Border txTrack, txFill;
        Border cArt, eArt, eTrack, eFill, nIconBox;
        TextBlock cText, eSource, eTitle, eArtist, ePos, eDur, ePlay, nApp, nTitle, nBody;
        Grid eProgress;
        Image nIcon;
        readonly List<Rectangle> eqBars = new List<Rectangle>();

        MediaInfo cur;
        DateTime pausedAt = DateTime.MinValue;
        NotificationInfo note;
        DateTime noteUntil;
        readonly Queue<NotificationInfo> queue = new Queue<NotificationInfo>();
        bool hovering, fullscreen;
        string lastLoggedError;
        string state = "";

        public IslandWindow(Func<string, ImageSource> icon)
        {
            this.icon = icon;
            Title = "FloatingDockIsland";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Width = WinW; Height = WinH;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            Content = BuildUi();
            Left = Math.Round((SystemParameters.PrimaryScreenWidth - WinW) / 2);
            Top = 0;

            SourceInitialized += (s, e) => { hwnd = new WindowInteropHelper(this).Handle; Glass.NoActivate(hwnd); };
            Loaded += async (s, e) =>
            {
                media.Changed += m => Dispatcher.BeginInvoke(new Action(() => OnMedia(m)));
                try { await media.StartAsync(); } catch { }
                StartNotificationPolling();
            };
            // eski tip tepsi balonlari (Shell_NotifyIcon NIF_INFO): artik Windows degil biz gosteriyoruz
            Tray.Balloon += b => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (string.IsNullOrWhiteSpace(b.Title) && string.IsNullOrWhiteSpace(b.Info)) return;
                Enqueue(new NotificationInfo
                {
                    Title = string.IsNullOrWhiteSpace(b.Title) ? b.Info : b.Title,
                    Body = string.IsNullOrWhiteSpace(b.Title) ? "" : b.Info,
                    AppName = b.NotifyIcon?.Title?.Split('\n')[0] ?? "Bildirim",
                    Image = b.NotifyIcon?.Icon ?? b.Icon,
                    OnClick = () => { try { b.Click(); } catch { } }
                });
            }));
            // hava durumu: yagis yaklasiyor (rahatsiz etme acikken yalniz gecmise yazilir)
            Weather.RainAlert += (title, text) => Dispatcher.BeginInvoke(new Action(() =>
                Enqueue(new NotificationInfo { Title = title, Body = text, AppName = "Hava durumu", Glyph = "" })));
            // dock'taki Spotify ikonunda tekerlek: yalniz o uygulamanin sesi
            Hub.AppVolumeShown += (app, v) => Dispatcher.BeginInvoke(new Action(() =>
                ShowOsd(v <= 0.001f ? "" : "", v, app + " %" + (int)Math.Round(v * 100))));
            IslandTimer.Changed += () => { UpdateTimer(); ApplyState(); };
            IslandTimer.Finished += () =>
            {
                var label = IslandTimer.Label;
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
                ShowLocal(new NotificationInfo { Title = "Süre doldu", Body = label, AppName = "Zamanlayıcı", Glyph = "" }, 10);
            };

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += (s, e) => Tick();
            t.Start();
            ApplyState();
        }

        // ---------------- Arayuz ----------------
        UIElement BuildUi()
        {
            pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 6, 6, 10)),
                BorderBrush = Theme.OnDark(30),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 0),
                Width = 0, Height = 0, Opacity = 0,
                ClipToBounds = true,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black }
            };
            pill.MouseEnter += (s, e) => { hovering = true; ApplyState(); };
            pill.MouseLeave += (s, e) => { hovering = false; ApplyState(); };

            var host = new Grid();
            compactView = BuildCompact();
            expandedView = BuildExpanded();
            notifView = BuildNotif();
            osdView = BuildOsd();
            batteryView = BuildBattery();
            timerView = BuildTimer();
            timerXView = BuildTimerExpanded();
            views["compact"] = compactView; views["expanded"] = expandedView; views["notif"] = notifView;
            views["osd"] = osdView; views["battery"] = batteryView; views["timer"] = timerView; views["timerx"] = timerXView;
            foreach (var v in views.Values) { host.Children.Add(v); v.Visibility = Visibility.Collapsed; v.Opacity = 0; }
            pill.Child = host;
            return new Grid { Children = { pill } };
        }

        FrameworkElement BuildCompact()
        {
            var g = new Grid { Width = 330, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            cArt = ArtBox(26, 7);
            cArt.Margin = new Thickness(7, 0, 0, 0);
            g.Children.Add(cArt);

            cText = Text(12.5, Brushes.White);
            cText.Margin = new Thickness(10, 0, 8, 0);
            Grid.SetColumn(cText, 1);
            g.Children.Add(cText);

            var eq = new StackPanel { Orientation = Orientation.Horizontal, Height = 16, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var _ in new[] { 0, 1, 2, 3 })
            {
                var r = new Rectangle { Width = 3, Height = 4, RadiusX = 1.5, RadiusY = 1.5, Fill = accent, Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Bottom };
                eqBars.Add(r);
                eq.Children.Add(r);
            }
            Grid.SetColumn(eq, 2);
            g.Children.Add(eq);
            return g;
        }

        FrameworkElement BuildExpanded()
        {
            var g = new Grid { Width = 430, Height = 140, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            eArt = ArtBox(84, 16);
            eArt.Margin = new Thickness(18, 0, 0, 0);
            g.Children.Add(eArt);

            var right = new StackPanel { Margin = new Thickness(16, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
            eSource = Text(11, Dim);
            eTitle = Text(15, Brushes.White); eTitle.FontWeight = FontWeights.SemiBold; eTitle.Margin = new Thickness(0, 1, 0, 0);
            eArtist = Text(12, Dim);
            right.Children.Add(eSource);
            right.Children.Add(eTitle);
            right.Children.Add(eArtist);

            eProgress = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            eProgress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            eProgress.ColumnDefinitions.Add(new ColumnDefinition());
            eProgress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            ePos = Text(10.5, Dim); ePos.HorizontalAlignment = HorizontalAlignment.Left;
            eDur = Text(10.5, Dim); eDur.HorizontalAlignment = HorizontalAlignment.Right;
            eTrack = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.OnDark(40), VerticalAlignment = VerticalAlignment.Center };
            eFill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            eTrack.Child = eFill;
            Grid.SetColumn(eTrack, 1); Grid.SetColumn(eDur, 2);
            eProgress.Children.Add(ePos); eProgress.Children.Add(eTrack); eProgress.Children.Add(eDur);
            right.Children.Add(eProgress);

            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            controls.Children.Add(IconButton("", 14, () => media.Prev(), out _));
            controls.Children.Add(IconButton("", 18, () => media.PlayPause(), out ePlay));
            controls.Children.Add(IconButton("", 14, () => media.Next(), out _));
            right.Children.Add(controls);

            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            // kapaga ya da sarki adina tikla: buyuk oynatici paneli
            foreach (var el in new FrameworkElement[] { eArt, eTitle, eArtist, eSource })
            {
                el.Cursor = System.Windows.Input.Cursors.Hand;
                el.ToolTip = "Oynatıcıyı aç";
                el.MouseLeftButtonUp += (s, e) => { e.Handled = true; Hub.OpenPlayer?.Invoke(); };
            }
            return g;
        }

        FrameworkElement BuildNotif()
        {
            var g = new Grid { Width = 430, Height = 86, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            nIconBox = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(Theme.RadiusLg), Background = Theme.OnDark(18), Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            nIcon = new Image { Width = 30, Height = 30 };
            RenderOptions.SetBitmapScalingMode(nIcon, BitmapScalingMode.HighQuality);
            nIconBox.Child = nIcon;
            g.Children.Add(nIconBox);

            var right = new StackPanel { Margin = new Thickness(14, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center };
            var head = new DockPanel();
            var now = Text(11, Dim); now.Text = "şimdi";
            DockPanel.SetDock(now, Dock.Right);
            head.Children.Add(now);
            nApp = Text(11, Dim);
            head.Children.Add(nApp);
            nTitle = Text(13.5, Brushes.White); nTitle.FontWeight = FontWeights.SemiBold; nTitle.Margin = new Thickness(0, 1, 0, 0);
            nBody = Text(12, Theme.OnDark(190));
            nBody.TextWrapping = TextWrapping.Wrap; nBody.MaxHeight = 34;
            right.Children.Add(head); right.Children.Add(nTitle); right.Children.Add(nBody);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);

            g.MouseLeftButtonUp += (s, e) => { var click = note?.OnClick; note = null; click?.Invoke(); NextNote(); ApplyState(); };
            return g;
        }

        // ---- ses / parlaklik gostergesi ----
        FrameworkElement BuildOsd()
        {
            var g = new Grid { Width = 300, Height = 44, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            osdGlyph = new TextBlock { FontFamily = IconFont, FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            g.Children.Add(osdGlyph);
            osdTrack = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.OnDark(40), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            osdFill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Brush(DefaultAccent), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            osdTrack.Child = osdFill;
            Grid.SetColumn(osdTrack, 1); g.Children.Add(osdTrack);
            osdText = Text(12.5, Brushes.White);
            osdText.HorizontalAlignment = HorizontalAlignment.Center;
            System.Windows.Documents.Typography.SetNumeralAlignment(osdText, FontNumeralAlignment.Tabular);
            Grid.SetColumn(osdText, 2); g.Children.Add(osdText);
            return g;
        }

        void ShowOsd(string glyph, double value, string text)
        {
            osdGlyph.Text = glyph;
            osdText.Text = text;
            void Size()
            {
                double w = Math.Max(0, osdTrack.ActualWidth * Math.Clamp(value, 0, 1));
                osdFill.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(120)));
            }
            if (osdTrack.ActualWidth > 0) Size();
            else Dispatcher.BeginInvoke(new Action(Size), DispatcherPriority.Loaded);
            osdUntil = DateTime.Now.AddMilliseconds(1600);
            ApplyState();
        }

        static string VolumeGlyph(float v, bool muted) => muted || v <= 0.001f ? "" : v < 0.34f ? "" : v < 0.67f ? "" : "";

        // ---- pil ----
        FrameworkElement BuildBattery()
        {
            var g = new Grid { Width = 300, Height = 44, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // cizilmis pil: govde + uc + dolan ic kisim
            var body = new Grid { Width = 34, Height = 17, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            var shell = new Border { CornerRadius = new CornerRadius(4), BorderBrush = Theme.OnDark(150), BorderThickness = new Thickness(1.4), Padding = new Thickness(2) };
            batFill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = Theme.Brush(Color.FromRgb(0x5E, 0xD6, 0x8C)) };
            shell.Child = batFill;
            body.Children.Add(shell);
            var nub = new Border { Width = 2, Height = 6, CornerRadius = new CornerRadius(0, 1.5, 1.5, 0), Background = Theme.OnDark(150), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0.5, 0, 0, 0) };
            Grid.SetColumn(nub, 1); body.Children.Add(nub);
            g.Children.Add(body);
            batTitle = Text(13, Brushes.White);
            batTitle.FontWeight = FontWeights.SemiBold;
            batTitle.Margin = new Thickness(12, 0, 8, 0);
            Grid.SetColumn(batTitle, 1); g.Children.Add(batTitle);
            batPct = Text(13, Brushes.White);
            batPct.Margin = new Thickness(0, 0, 18, 0);
            Grid.SetColumn(batPct, 2); g.Children.Add(batPct);
            return g;
        }

        void ShowBattery(int pct, bool charging, double seconds)
        {
            batTitle.Text = charging ? "Şarj oluyor" : "Pil azaldı";
            batPct.Text = "%" + pct;
            var color = charging ? Color.FromRgb(0x5E, 0xD6, 0x8C) : pct <= 10 ? Color.FromRgb(0xFF, 0x6B, 0x6B) : Color.FromRgb(0xFF, 0xC8, 0x57);
            batFill.Background = new SolidColorBrush(color);
            batPct.Foreground = new SolidColorBrush(charging ? Colors.White : color);
            double full = 34 - 3 - 2.8 - 4; // govde ic genisligi
            double to = Math.Max(2, full * pct / 100.0);
            // sarjda dolma animasyonu: bostan yuzdeye
            batFill.BeginAnimation(WidthProperty, new DoubleAnimation(charging ? 0 : to, to, TimeSpan.FromMilliseconds(charging ? 900 : 1))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = TimeSpan.FromMilliseconds(250) });
            batUntil = DateTime.Now.AddSeconds(seconds);
            ApplyState();
        }

        void CheckBattery()
        {
            if (!Settings.Current.IslandBattery || !Battery.HasBattery) return;
            bool ch = Battery.Charging;
            int p = Battery.Percent;
            if (p < 0) return;
            if (lastCharging == false && ch) ShowBattery(p, true, 3);
            if (ch) lowWarned = 101; // sarjdayken uyari hakki yenilenir
            else if ((p <= 10 && lowWarned > 10) || (p <= 20 && lowWarned > 20))
            {
                lowWarned = p <= 10 ? 10 : 20;
                if (lastCharging != null) ShowBattery(p, false, 5);
            }
            lastCharging = ch;
        }

        // ---- zamanlayici ----
        FrameworkElement BuildTimer()
        {
            var g = new Grid { Width = 250, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 14, Foreground = accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            g.Children.Add(ic);
            tLabel = Text(12, Dim); tLabel.Margin = new Thickness(10, 0, 8, 0);
            Grid.SetColumn(tLabel, 1); g.Children.Add(tLabel);
            tTime = Text(14, Brushes.White); tTime.FontWeight = FontWeights.SemiBold; tTime.Margin = new Thickness(0, 0, 16, 0);
            System.Windows.Documents.Typography.SetNumeralAlignment(tTime, FontNumeralAlignment.Tabular);
            Grid.SetColumn(tTime, 2); g.Children.Add(tTime);
            return g;
        }

        FrameworkElement BuildTimerExpanded()
        {
            var g = new Grid { Width = 380, Height = 112, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 8, 0) };
            txLabel = Text(11.5, Dim);
            txTime = Text(34, Brushes.White);
            txTime.FontFamily = Theme.Display;
            txTime.FontWeight = FontWeight.FromOpenTypeWeight(350);
            System.Windows.Documents.Typography.SetNumeralAlignment(txTime, FontNumeralAlignment.Tabular);
            left.Children.Add(txLabel);
            left.Children.Add(txTime);
            txTrack = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.OnDark(40), Margin = new Thickness(0, 6, 0, 0), Width = 170, HorizontalAlignment = HorizontalAlignment.Left };
            txFill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            txTrack.Child = txFill;
            left.Children.Add(txTrack);
            g.Children.Add(left);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            txAdd = IconButton("+1", 13, IslandTimer.AddMinute, out var addText);
            addText.FontFamily = UiFont; addText.FontWeight = FontWeights.SemiBold;
            ((FrameworkElement)txAdd).ToolTip = "1 dakika ekle";
            btns.Children.Add(txAdd);
            btns.Children.Add(IconButton("", 16, IslandTimer.TogglePause, out txPause));
            var stop = IconButton("", 15, IslandTimer.Stop, out _);
            stop.ToolTip = "Durdur";
            btns.Children.Add(stop);
            Grid.SetColumn(btns, 1); g.Children.Add(btns);
            return g;
        }

        void UpdateTimer()
        {
            if (!IslandTimer.Active) return;
            var t = IslandFormat(IslandTimer.Value);
            tTime.Text = t; txTime.Text = t;
            var label = IslandTimer.Label + (IslandTimer.Paused ? " · duraklatıldı" : "");
            tLabel.Text = label; txLabel.Text = label;
            txPause.Text = IslandTimer.Paused ? "" : "";
            bool cd = IslandTimer.Mode == IslandTimer.Kind.Countdown;
            txAdd.Visibility = cd ? Visibility.Visible : Visibility.Collapsed;
            txTrack.Visibility = cd ? Visibility.Visible : Visibility.Collapsed;
            if (cd) txFill.Width = txTrack.Width * IslandTimer.Progress;
        }

        static string IslandFormat(TimeSpan t) => IslandTimer.Format(t);

        static TextBlock Text(double size, Brush fg) => new TextBlock
        {
            FontSize = size, Foreground = fg, FontFamily = UiFont,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };

        Border ArtBox(double size, double radius) => new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius),
            Background = accent, VerticalAlignment = VerticalAlignment.Center
        };

        FrameworkElement IconButton(string glyph, double size, Action click, out TextBlock tb)
        {
            tb = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = 40, Height = 30, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = tb, Margin = new Thickness(4, 0, 4, 0) };
            var st = new ScaleTransform(1, 1);
            b.RenderTransformOrigin = new Point(0.5, 0.5); b.RenderTransform = st;
            b.MouseEnter += (s, e) => b.Background = Theme.OnDark(24);
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (s, e) => { st.ScaleX = st.ScaleY = 0.88; };
            b.MouseLeftButtonUp += (s, e) => { st.ScaleX = st.ScaleY = 1; click(); };
            return b;
        }

        // ---------------- Durum makinesi ----------------
        void ApplyState()
        {
            // oncelik: bildirim > ses/parlaklik > pil > zamanlayici > medya
            string s;
            var now = DateTime.Now;
            if (fullscreen) s = "none";
            else if (note != null) s = "notif";
            else if (now < osdUntil) s = "osd";
            else if (now < batUntil) s = "battery";
            else if (IslandTimer.Active) s = hovering ? "timerx" : "timer";
            else if (cur != null && (cur.Playing || hovering || (now - pausedAt).TotalSeconds < 8)) s = hovering ? "expanded" : "compact";
            else s = "none";
            if (s == state) return;
            state = s;
            if (s == "timer" || s == "timerx") UpdateTimer();

            double w = 0, h = 0, r = 20;
            if (s == "compact") { w = 330; h = 40; r = 20; }
            else if (s == "expanded") { w = 430; h = 140; r = 32; }
            else if (s == "notif") { w = 430; h = 86; r = 30; }
            else if (s == "osd" || s == "battery") { w = 300; h = 44; r = 22; }
            else if (s == "timer") { w = 250; h = 40; r = 20; }
            else if (s == "timerx") { w = 380; h = 112; r = 30; }

            var dur = TimeSpan.FromMilliseconds(w > 0 ? 460 : 260);
            IEasingFunction ease = w > 0 ? new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseIn };
            pill.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
            pill.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
            pill.BeginAnimation(OpacityProperty, new DoubleAnimation(w > 0 ? 1 : 0, TimeSpan.FromMilliseconds(w > 0 ? 160 : 280)));
            pill.CornerRadius = new CornerRadius(r);
            pill.IsHitTestVisible = w > 0;

            foreach (var kv in views) Fade(kv.Value, kv.Key == s);
        }

        static void Fade(FrameworkElement v, bool on)
        {
            if (on)
            {
                v.Visibility = Visibility.Visible;
                v.BeginAnimation(OpacityProperty, null);
                v.Opacity = 0;
                v.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(110) });
            }
            else
            {
                v.BeginAnimation(OpacityProperty, null);
                v.Opacity = 0;
                v.Visibility = Visibility.Collapsed;
            }
        }

        void Tick()
        {
            fullscreen = IsForegroundFullscreen();
            if (note != null && !hovering && DateTime.Now > noteUntil) { note = null; NextNote(); }
            UpdateProgress();
            IslandTimer.Tick();
            if (state == "timer" || state == "timerx") UpdateTimer();
            tickNo++;
            if (Settings.Current.IslandOsd) CheckOsd();
            if (tickNo % 4 == 0) CheckBattery();
            ApplyState();
        }

        /// <summary>Ses (her tik) ve dizustu parlakligi (saniyede bir, arka planda) degisti mi?</summary>
        void CheckOsd()
        {
            // kendi panelimizdeki kaydiriciyla oynaniyorsa gosterge cikmasin
            var fg = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(fg, out uint pid);
            bool ours = pid == (uint)Environment.ProcessId;

            if (Audio.TryRead(out var v, out var m))
            {
                bool changed = lastVol > -2 && (Math.Abs(v - lastVol) > 0.004f || m != lastMute);
                lastVol = v; lastMute = m;
                if (changed && !ours && !fullscreen) ShowOsd(VolumeGlyph(v, m), m ? 0 : v, m ? "Sessiz" : ((int)Math.Round(v * 100)).ToString());
            }
            if (tickNo % 4 == 0 && !brightBusy && lastBright != -1)
            {
                brightBusy = true;
                System.Threading.Tasks.Task.Run(Brightness.GetWmi).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                {
                    brightBusy = false;
                    int b = t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? t.Result : -1;
                    bool changed = lastBright >= 0 && b >= 0 && b != lastBright;
                    lastBright = b; // -1: dizustu ekran yok, bir daha sorma
                    if (changed && !IsOurForeground() && !fullscreen) ShowOsd("", b / 100.0, b.ToString());
                })));
            }
        }

        static bool IsOurForeground()
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
            return pid == (uint)Environment.ProcessId;
        }

        // ---------------- Medya ----------------
        void OnMedia(MediaInfo m)
        {
            bool wasPlaying = cur != null && cur.Playing;
            if (m != null && wasPlaying && !m.Playing) pausedAt = DateTime.Now;
            cur = m;
            if (m != null)
            {
                cText.Text = string.IsNullOrEmpty(m.Artist) ? m.Title : m.Title + "  ·  " + m.Artist;
                eTitle.Text = m.Title;
                eArtist.Text = m.Artist;
                eSource.Text = FriendlySource(m.SourceId);
                ePlay.Text = m.Playing ? "" : "";

                var img = Decode(m.Art, 168);
                SetArt(cArt, img, 13);
                SetArt(eArt, img, 34);
                var c = img != null ? Dominant(m.Art) : DefaultAccent;
                accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(500)));
                SetEqualizer(m.Playing);
                eProgress.Visibility = m.Duration.TotalSeconds > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateProgress();
            ApplyState();
        }

        static void SetArt(Border box, ImageSource img, double glyphSize)
        {
            if (img != null)
            {
                box.Child = null;
                box.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            }
            else
            {
                box.ClearValue(Border.BackgroundProperty);
                box.Background = Theme.OnDark(24);
                box.Child = new TextBlock { Text = "", FontFamily = IconFont, FontSize = glyphSize * 0.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
        }

        void SetEqualizer(bool playing)
        {
            double[] secs = { 0.42, 0.58, 0.36, 0.5 };
            for (int i = 0; i < eqBars.Count; i++)
            {
                var bar = eqBars[i];
                if (playing)
                {
                    var a = new DoubleAnimation(3, 15, TimeSpan.FromSeconds(secs[i]))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                        BeginTime = TimeSpan.FromMilliseconds(i * 90)
                    };
                    bar.BeginAnimation(HeightProperty, a);
                }
                else bar.BeginAnimation(HeightProperty, new DoubleAnimation(3, TimeSpan.FromMilliseconds(250)));
            }
        }

        void UpdateProgress()
        {
            if (cur == null || state != "expanded" || cur.Duration.TotalSeconds <= 1) return;
            var pos = cur.Position;
            if (cur.Playing && cur.Updated > DateTimeOffset.MinValue) pos += DateTimeOffset.Now - cur.Updated;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (pos > cur.Duration) pos = cur.Duration;
            ePos.Text = Fmt(pos);
            eDur.Text = Fmt(cur.Duration);
            eFill.Width = Math.Max(0, eTrack.ActualWidth * pos.TotalSeconds / cur.Duration.TotalSeconds);
        }

        static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        internal static string FriendlySource(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var low = id.ToLowerInvariant();
            if (low.Contains("spotify")) return "Spotify";
            if (low.Contains("msedge")) return "Microsoft Edge";
            if (low.Contains("chrome")) return "Google Chrome";
            if (low.Contains("firefox")) return "Firefox";
            if (low.Contains("opera")) return "Opera";
            if (low.Contains("brave")) return "Brave";
            var n = Native.DisplayName("shell:AppsFolder\\" + id);
            return !string.IsNullOrEmpty(n) ? n : id.Replace(".exe", "");
        }

        internal static BitmapImage Decode(byte[] bytes, int width)
        {
            if (bytes == null) return null;
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.DecodePixelWidth = width;
                b.StreamSource = new MemoryStream(bytes);
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch { return null; }
        }

        /// <summary>Kapak gorselinin baskin rengini cikarir (ekolayzer ve ilerleme cubugu icin).</summary>
        internal static Color Dominant(byte[] bytes)
        {
            try
            {
                var small = Decode(bytes, 12);
                var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);
                double best = -1; Color pick = DefaultAccent;
                double sr = 0, sg = 0, sb = 0; int n = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    int b = px[i], g = px[i + 1], r = px[i + 2];
                    int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                    double sat = mx == 0 ? 0 : (mx - mn) / (double)mx;
                    double score = sat * (mx / 255.0);
                    if (score > best) { best = score; pick = Color.FromRgb((byte)r, (byte)g, (byte)b); }
                    sr += r; sg += g; sb += b; n++;
                }
                if (best < 0.15) return DefaultAccent; // gri/siyah-beyaz kapaklar
                int m = Math.Max(pick.R, Math.Max(pick.G, pick.B));
                double k = 235.0 / Math.Max(1, m);
                return Color.FromRgb((byte)Math.Min(255, pick.R * k), (byte)Math.Min(255, pick.G * k), (byte)Math.Min(255, pick.B * k));
            }
            catch { return DefaultAccent; }
        }

        // ---------------- Bildirimler ----------------
        void StartNotificationPolling()
        {
            var timer = new System.Threading.Timer(_ =>
            {
                var list = notifs.Poll();
                if (notifs.LastError != null && notifs.LastError != lastLoggedError)
                {
                    lastLoggedError = notifs.LastError;
                    Program.Log(new Exception("Bildirim okuyucu: " + notifs.LastError));
                }
                if (list.Count > 0)
                    Dispatcher.BeginInvoke(new Action(() => { foreach (var n in list) Enqueue(n); }));
            }, null, 500, 1500);
            Closed += (s, e) => timer.Dispose();
        }

        void Enqueue(NotificationInfo n)
        {
            // her bildirim gecmise yazilir (takvim paneli); rahatsiz etme acikken adada gosterilmez
            AddToHistory(n);
            // "Windows balonlarini gizle" acikken yeni gelen uygulamanin balonunu da kapat
            if (!string.IsNullOrEmpty(n.AppId)) Banners.Track(n.AppId);
            if (Settings.Current.DoNotDisturb) return;
            queue.Enqueue(n);
            if (queue.Count > 5) queue.Dequeue();
            if (note == null) NextNote();
            ApplyState();
        }

        /// <summary>Kullanicinin kendi baslattigi seyler (zamanlayici): rahatsiz etme acik olsa da gosterilir.</summary>
        void ShowLocal(NotificationInfo n, double seconds)
        {
            AddToHistory(n);
            note = null;
            queue.Clear();
            queue.Enqueue(n);
            NextNote();
            noteUntil = DateTime.Now.AddSeconds(seconds);
            ApplyState();
        }

        void AddToHistory(NotificationInfo n)
        {
            string path = string.IsNullOrEmpty(n.AppId) ? null : "shell:AppsFolder\\" + n.AppId;
            Hub.AddNotification(new NotificationEntry
            {
                Info = n, Time = DateTime.Now,
                AppName = NoteAppName(n),
                IconPath = path
            });
        }

        static string NoteAppName(NotificationInfo n)
        {
            if (!string.IsNullOrEmpty(n.AppName)) return n.AppName;
            var name = string.IsNullOrEmpty(n.AppId) ? null : Native.DisplayName("shell:AppsFolder\\" + n.AppId);
            return !string.IsNullOrEmpty(name) ? name : FriendlyAppId(n.AppId);
        }

        void NextNote()
        {
            if (queue.Count == 0) return;
            note = queue.Dequeue();
            nTitle.Text = note.Title;
            nBody.Text = note.Body;
            nBody.Visibility = string.IsNullOrEmpty(note.Body) ? Visibility.Collapsed : Visibility.Visible;
            nApp.Text = NoteAppName(note);
            if (note.Glyph != null)
                nIconBox.Child = new TextBlock { Text = note.Glyph, FontFamily = IconFont, FontSize = 20, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            else
            {
                nIconBox.Child = nIcon;
                nIcon.Source = note.Image ?? (string.IsNullOrEmpty(note.AppId) ? null : icon("shell:AppsFolder\\" + note.AppId));
            }
            noteUntil = DateTime.Now.AddSeconds(6);
            if (state == "notif") { state = ""; } // ayni durumda yeni icerik: animasyonu tazele
        }

        internal static string FriendlyAppId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Bildirim";
            var s = id.Split('!')[0];
            var parts = s.Split('.');
            return parts[parts.Length - 1].Replace("_", " ");
        }

        // ---------------- Tam ekran ----------------
        bool IsForegroundFullscreen()
        {
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == hwnd) return false;
            var cls = new StringBuilder(64);
            Native.GetClassName(fg, cls, 64);
            var c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
            if (Native.IsZoomed(fg)) return false; // buyutulmus pencere tam ekran sayilmaz
            if (!Native.GetWindowRect(fg, out var r)) return false;
            int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
            return r.left <= 0 && r.top <= 0 && r.right >= sw && r.bottom >= sh;
        }
    }
}
