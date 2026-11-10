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
        FrameworkElement compactView, expandedView, notifView;
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
                Hub.LocalToast += n => Dispatcher.BeginInvoke(new Action(() => Enqueue(n)));
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
            host.Children.Add(compactView);
            host.Children.Add(expandedView);
            host.Children.Add(notifView);
            foreach (var v in new[] { compactView, expandedView, notifView }) { v.Visibility = Visibility.Collapsed; v.Opacity = 0; }
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

            g.MouseLeftButtonUp += (s, e) => { var click = note?.Click; note = null; NextNote(); ApplyState(); click?.Invoke(); };
            return g;
        }

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
            string s;
            if (fullscreen) s = "none";
            else if (note != null) s = "notif";
            else if (cur != null && (cur.Playing || hovering || (DateTime.Now - pausedAt).TotalSeconds < 8)) s = hovering ? "expanded" : "compact";
            else s = "none";
            if (s == state) return;
            state = s;

            double w = 0, h = 0, r = 20;
            if (s == "compact") { w = 330; h = 40; r = 20; }
            else if (s == "expanded") { w = 430; h = 140; r = 32; }
            else if (s == "notif") { w = 430; h = 86; r = 30; }

            var dur = TimeSpan.FromMilliseconds(w > 0 ? 460 : 260);
            IEasingFunction ease = w > 0 ? new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseIn };
            pill.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
            pill.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
            pill.BeginAnimation(OpacityProperty, new DoubleAnimation(w > 0 ? 1 : 0, TimeSpan.FromMilliseconds(w > 0 ? 160 : 280)));
            pill.CornerRadius = new CornerRadius(r);
            pill.IsHitTestVisible = w > 0;

            Fade(compactView, s == "compact");
            Fade(expandedView, s == "expanded");
            Fade(notifView, s == "notif");
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
            ApplyState();
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

        static string FriendlySource(string id)
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

        static BitmapImage Decode(byte[] bytes, int width)
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
        static Color Dominant(byte[] bytes)
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
            var path = "shell:AppsFolder\\" + n.AppId;
            var name = n.Source ?? Native.DisplayName(path);
            Hub.AddNotification(new NotificationEntry
            {
                Info = n, Time = DateTime.Now,
                AppName = !string.IsNullOrEmpty(name) ? name : FriendlyAppId(n.AppId),
                IconPath = n.Source != null ? Environment.ProcessPath : string.IsNullOrEmpty(n.AppId) ? null : path
            });
            Banners.Ensure(n.AppId);
            if (Settings.Current.DoNotDisturb) return;
            queue.Enqueue(n);
            if (queue.Count > 5) queue.Dequeue();
            if (note == null) NextNote();
            ApplyState();
        }

        void NextNote()
        {
            if (queue.Count == 0) return;
            note = queue.Dequeue();
            nTitle.Text = note.Title;
            nBody.Text = note.Body;
            nBody.Visibility = string.IsNullOrEmpty(note.Body) ? Visibility.Collapsed : Visibility.Visible;
            var path = "shell:AppsFolder\\" + note.AppId;
            var name = note.Source ?? Native.DisplayName(path);
            nApp.Text = !string.IsNullOrEmpty(name) ? name : FriendlyAppId(note.AppId);
            nIcon.Source = note.Source != null ? icon(Environment.ProcessPath) : string.IsNullOrEmpty(note.AppId) ? null : icon(path);
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
