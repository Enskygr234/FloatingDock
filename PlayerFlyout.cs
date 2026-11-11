using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>
    /// "Simdi caliyor" oynatici: buyuk kapak, kapagin renginde arka plan, surukle-atla ilerleme cubugu,
    /// karistir / tekrarla, yalniz calan uygulamanin (or. Spotify) ses duzeyi. Windows medya oturumu uzerinden calisir.
    /// </summary>
    class PlayerFlyout : Flyout
    {
        const string GPlay = "", GPause = "", GPrev = "", GNext = "", GShuffle = "",
                     GRepeatAll = "", GRepeatOne = "", GVolume = "", GMute = "", GOpen = "";

        readonly SolidColorBrush tint = new SolidColorBrush(Theme.Accent);
        readonly Image backdrop = new Image { Stretch = Stretch.UniformToFill, Opacity = Theme.Dark ? 0.55 : 0.45, Effect = new BlurEffect { Radius = 60, KernelType = KernelType.Gaussian } };
        readonly Border cover = new Border { Width = 264, Height = 264, CornerRadius = new CornerRadius(Theme.RadiusLg), HorizontalAlignment = HorizontalAlignment.Center };
        readonly TextBlock source = Text("", Theme.Caption, Theme.TextSecondary, FontWeights.SemiBold);
        readonly TextBlock title = Text("", Theme.Subtitle + 2, Theme.TextPrimary, FontWeights.SemiBold);
        readonly TextBlock artist = Text("", Theme.Body, Theme.TextSecondary);
        readonly TextBlock pos = Text("0:00", Theme.Caption - 0.5, Theme.TextTertiary), dur = Text("0:00", Theme.Caption - 0.5, Theme.TextTertiary);
        readonly Scrub seek, volume;
        readonly TextBlock playIcon = Icon(GPlay, 18), shuffleIcon = Icon(GShuffle, 14), repeatIcon = Icon(GRepeatAll, 14), volIcon = Icon(GVolume, 14, Theme.TextSecondary);
        readonly Border shuffleBtn, repeatBtn, prevBtn, nextBtn, playBtn;
        readonly Ellipse shuffleDot = Dot(), repeatDot = Dot();
        readonly Grid volumeRow;
        readonly TextBlock volLabel = Text("", Theme.Caption, Theme.TextSecondary), volPct = Text("", Theme.Caption, Theme.TextTertiary);
        readonly TextBlock empty = Text("Şu an çalan bir şey yok. Spotify'da bir şarkı başlat.", Theme.Body, Theme.TextSecondary);
        readonly StackPanel body = new StackPanel();
        readonly Border openBtn;
        readonly DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        MediaInfo cur;
        string coverKey;
        DateTime volTouched = DateTime.MinValue;

        public PlayerFlyout() : base("FloatingDockPlayer")
        {
            Width = 340;
            var root = new Grid();

            // kapagin bulaniklastirilmis hali + kapak renginde hafif ton: panel o sarkinin rengini alir
            var bg = new Grid { ClipToBounds = true, Margin = new Thickness(-Theme.Pad), IsHitTestVisible = false };
            bg.Children.Add(new Border { Background = tint, Opacity = Theme.Dark ? 0.16 : 0.10 });
            bg.Children.Add(backdrop);
            bg.Children.Add(new Border
            {
                Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Theme.Dark ? Color.FromArgb(150, 0, 0, 0) : Color.FromArgb(150, 255, 255, 255), 90)
            });
            root.Children.Add(bg);

            var sp = new StackPanel();
            // kaynak (Spotify / tarayici) + uygulamada ac
            var head = new Grid { Margin = new Thickness(2, 0, 0, 10) };
            var srcRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var eq = Icon("", 12, Theme.TextSecondary);
            eq.Margin = new Thickness(0, 0, 6, 0);
            srcRow.Children.Add(eq);
            srcRow.Children.Add(source);
            head.Children.Add(srcRow);
            openBtn = IconButton(GOpen, "Uygulamada aç", OpenSource, 30, 12);
            openBtn.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(openBtn);
            sp.Children.Add(head);

            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(2, 30, 2, 30);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            sp.Children.Add(empty);

            // kapak
            cover.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = Theme.Dark ? 0.5 : 0.25, Color = Colors.Black };
            body.Children.Add(cover);

            // baslik + sanatci
            var tt = new StackPanel { Margin = new Thickness(2, 16, 2, 0) };
            title.ToolTip = null;
            tt.Children.Add(title);
            artist.Margin = new Thickness(0, 2, 0, 0);
            tt.Children.Add(artist);
            body.Children.Add(tt);

            // ilerleme
            seek = new Scrub(tint) { Margin = new Thickness(0, 12, 0, 0) };
            seek.Changing += v => { if (cur != null) pos.Text = Fmt(TimeSpan.FromTicks((long)(cur.Duration.Ticks * v))); };
            seek.Committed += v => { if (cur != null && cur.CanSeek) Hub.Media.SeekTo(TimeSpan.FromTicks((long)(cur.Duration.Ticks * v))); };
            body.Children.Add(seek);
            var times = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            dur.HorizontalAlignment = HorizontalAlignment.Right;
            times.Children.Add(pos); times.Children.Add(dur);
            body.Children.Add(times);

            // kontroller: karistir  onceki  [oynat]  sonraki  tekrarla
            var ctl = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            for (int i = 0; i < 5; i++) ctl.ColumnDefinitions.Add(new ColumnDefinition());
            shuffleBtn = Toggleish(shuffleIcon, shuffleDot, "Karıştır", () => Hub.Media.ToggleShuffle());
            prevBtn = IconButton(GPrev, "Önceki", () => Hub.Media.Prev(), 40, 16);
            playIcon.Foreground = Theme.Brush(Theme.OnAccent);
            playBtn = Clickable(new Border { Width = 52, Height = 52, Child = playIcon }, () => Hub.Media.PlayPause(), 26);
            playBtn.Background = tint;
            playBtn.ToolTip = "Oynat / duraklat";
            nextBtn = IconButton(GNext, "Sonraki", () => Hub.Media.Next(), 40, 16);
            repeatBtn = Toggleish(repeatIcon, repeatDot, "Tekrarla", () => Hub.Media.CycleRepeat());
            var row = new FrameworkElement[] { shuffleBtn, prevBtn, playBtn, nextBtn, repeatBtn };
            for (int i = 0; i < row.Length; i++)
            {
                row[i].HorizontalAlignment = HorizontalAlignment.Center;
                row[i].VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(row[i], i);
                ctl.Children.Add(row[i]);
            }
            body.Children.Add(ctl);

            // yalniz bu uygulamanin sesi
            volumeRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            volumeRow.ColumnDefinitions.Add(new ColumnDefinition());
            volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            var mute = Clickable(new Border { Width = 28, Height = 28, Child = volIcon }, ToggleMute, 14);
            mute.ToolTip = "Sessize al";
            volumeRow.Children.Add(mute);
            volume = new Scrub(tint) { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, LiveCommit = true };
            volume.Committed += v => SetVolume((float)v);
            Grid.SetColumn(volume, 1);
            volumeRow.Children.Add(volume);
            volPct.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(volPct, 2);
            volumeRow.Children.Add(volPct);
            body.Children.Add(volumeRow);
            volLabel.Margin = new Thickness(36, 0, 0, 0);
            body.Children.Add(volLabel);

            sp.Children.Add(body);
            root.Children.Add(sp);
            SetBody(root);

            Hub.MediaChanged += m => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) Render(m); }));
            tick.Tick += (s, e) => Tick();
            // panel acikken klavye: Bosluk oynat/duraklat, oklar onceki/sonraki
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Space) { Hub.Media.PlayPause(); e.Handled = true; }
                else if (e.Key == Key.Right) { Hub.Media.Next(); e.Handled = true; }
                else if (e.Key == Key.Left) { Hub.Media.Prev(); e.Handled = true; }
            };
        }

        protected override void OnOpening()
        {
            Render(Hub.CurrentMedia);
            tick.Start();
        }

        protected override void OnClosing() => tick.Stop();

        static Ellipse Dot() => new Ellipse { Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3), Visibility = Visibility.Hidden };

        /// <summary>Acik/kapali durumu altindaki noktayla gosterilen ikon dugmesi.</summary>
        Border Toggleish(TextBlock icon, Ellipse dot, string tip, Action click)
        {
            dot.Fill = tint;
            var g = new Grid { Width = 40, Height = 40 };
            g.Children.Add(icon);
            g.Children.Add(dot);
            var b = Clickable(g, click, 20);
            b.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            return b;
        }

        // ================= cizim =================
        void Render(MediaInfo m)
        {
            cur = m;
            bool has = m != null;
            body.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            empty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            openBtn.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            if (!has) { source.Text = "Şimdi çalıyor"; backdrop.Source = null; return; }

            source.Text = (m.Playing ? "Çalıyor · " : "Duraklatıldı · ") + IslandWindow.FriendlySource(m.SourceId);
            title.Text = m.Title;
            artist.Text = string.IsNullOrEmpty(m.Album) || m.Album == m.Title ? m.Artist : m.Artist + " · " + m.Album;
            artist.ToolTip = artist.Text;
            title.ToolTip = m.Title;

            var key = m.SourceId + "|" + m.Title + "|" + m.Artist + "|" + (m.Art?.Length ?? 0);
            if (key != coverKey)
            {
                coverKey = key;
                var img = m.Art != null ? IslandWindow.Decode(m.Art, 528) : null;
                if (img != null)
                {
                    cover.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                    cover.Child = null;
                    backdrop.Source = img;
                    var c = IslandWindow.Dominant(m.Art);
                    tint.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(500)));
                }
                else
                {
                    cover.Background = Theme.Ink(Theme.SurfaceIdle);
                    cover.Child = Icon("", 64, Theme.TextTertiary);
                    backdrop.Source = null;
                    tint.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Theme.Accent, TimeSpan.FromMilliseconds(500)));
                }
                if (Theme.Motion && IsVisible)
                {
                    var st = new ScaleTransform(0.96, 0.96);
                    cover.RenderTransformOrigin = new Point(0.5, 0.5);
                    cover.RenderTransform = st;
                    var a = new DoubleAnimation(0.96, 1, Theme.D(Theme.Slow)) { EasingFunction = Theme.EaseOut };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                }
            }

            playIcon.Text = m.Playing ? GPause : GPlay;
            prevBtn.Opacity = m.CanPrev ? 1 : 0.4;
            nextBtn.Opacity = m.CanNext ? 1 : 0.4;
            seek.IsEnabled = m.CanSeek;
            seek.Visibility = m.Duration.TotalSeconds > 1 ? Visibility.Visible : Visibility.Hidden;
            dur.Text = m.Duration.TotalSeconds > 1 ? Fmt(m.Duration) : "";

            // uygulama karistir / tekrarla bildirmiyorsa dugmeler soluk durur
            shuffleBtn.Opacity = m.CanShuffle ? 1 : 0.35;
            shuffleBtn.IsEnabled = m.CanShuffle;
            bool shuf = m.Shuffle == true;
            shuffleIcon.Foreground = shuf ? tint : Theme.Fg(Theme.TextSecondary);
            shuffleDot.Visibility = shuf ? Visibility.Visible : Visibility.Hidden;
            repeatBtn.Opacity = m.CanRepeat ? 1 : 0.35;
            repeatBtn.IsEnabled = m.CanRepeat;
            repeatIcon.Text = m.Repeat == 1 ? GRepeatOne : GRepeatAll;
            repeatIcon.Foreground = m.Repeat > 0 ? tint : Theme.Fg(Theme.TextSecondary);
            repeatDot.Visibility = m.Repeat > 0 ? Visibility.Visible : Visibility.Hidden;
            shuffleBtn.ToolTip = shuf ? "Karıştır: açık" : "Karıştır";
            repeatBtn.ToolTip = m.Repeat == 1 ? "Tekrarla: bu şarkı" : m.Repeat == 2 ? "Tekrarla: liste" : "Tekrarla";

            RenderVolume();
            Tick();
        }

        void Tick()
        {
            var m = cur;
            if (m == null) return;
            if (!seek.Dragging && m.Duration.TotalSeconds > 1)
            {
                var p = m.LivePosition;
                pos.Text = Fmt(p);
                seek.Value = p.TotalMilliseconds / m.Duration.TotalMilliseconds;
            }
            // ses baska yerden (Windows karistiricisi) degistirilmis olabilir: arada bir oku
            if (!volume.Dragging && (DateTime.Now - volTouched).TotalSeconds > 1.5 && DateTime.Now.Millisecond < 250) RenderVolume();
        }

        void RenderVolume()
        {
            var proc = cur?.ProcessName;
            float v = proc == null ? -1 : Audio.GetAppVolume(proc);
            volumeRow.Visibility = v < 0 ? Visibility.Collapsed : Visibility.Visible;
            volLabel.Visibility = volumeRow.Visibility;
            if (v < 0) return;
            if (!volume.Dragging) volume.Value = v;
            volPct.Text = "%" + (int)Math.Round(v * 100);
            volIcon.Text = v <= 0.001f ? GMute : GVolume;
            volLabel.Text = "Yalnızca " + IslandWindow.FriendlySource(cur.SourceId) + " sesi";
        }

        float lastNonZero = 0.6f;

        void SetVolume(float v)
        {
            var proc = cur?.ProcessName;
            if (proc == null) return;
            volTouched = DateTime.Now;
            if (v > 0.01f) lastNonZero = v;
            Audio.SetAppVolume(proc, v);
            volPct.Text = "%" + (int)Math.Round(v * 100);
            volIcon.Text = v <= 0.001f ? GMute : GVolume;
        }

        void ToggleMute()
        {
            var proc = cur?.ProcessName;
            if (proc == null) return;
            float v = Audio.GetAppVolume(proc);
            SetVolume(v > 0.001f ? 0 : lastNonZero);
            volume.Value = v > 0.001f ? 0 : lastNonZero;
        }

        /// <summary>Calan uygulamayi one getir (penceresi yoksa Spotify'i ac).</summary>
        void OpenSource()
        {
            var m = cur;
            HideFlyout();
            if (m == null) return;
            var proc = m.ProcessName;
            if (proc != null)
            {
                foreach (var p in Process.GetProcessesByName(proc))
                {
                    var h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;
                    if (Native.IsIconic(h)) Native.ShowWindow(h, 9);
                    Native.SetForegroundWindow(h);
                    return;
                }
            }
            try { Process.Start(new ProcessStartInfo(m.IsSpotify ? "spotify:" : "shell:AppsFolder\\" + m.SourceId) { UseShellExecute = true }); } catch { }
        }

        static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00");
    }

    /// <summary>Ince, tiklanip suruklenebilen cubuk (ilerleme / ses). Uzerine gelince kalinlasir ve tutamac gorunur.</summary>
    class Scrub : Grid
    {
        readonly Border fill, track;
        readonly Ellipse thumb;
        double value;
        public bool Dragging { get; private set; }
        /// <summary>Surukleme sirasinda her adimda Committed cagrilsin (ses icin); kapaliysa yalniz birakinca (konum icin).</summary>
        public bool LiveCommit;
        public event Action<double> Changing, Committed;

        public Scrub(Brush accent)
        {
            Height = 18;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Ink(Theme.StrokeDefault), VerticalAlignment = VerticalAlignment.Center };
            fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
            thumb = new Ellipse { Width = 12, Height = 12, Fill = Theme.Fg(), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false,
                                  Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.35, Color = Colors.Black } };
            Children.Add(track); Children.Add(fill); Children.Add(thumb);
            SizeChanged += (s, e) => Layout();
            MouseEnter += (s, e) => Hover(true);
            MouseLeave += (s, e) => { if (!Dragging) Hover(false); };
            MouseLeftButtonDown += (s, e) => { if (!IsEnabled) return; Dragging = true; CaptureMouse(); Drag(e.GetPosition(this).X); e.Handled = true; };
            MouseMove += (s, e) => { if (Dragging) Drag(e.GetPosition(this).X); };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!Dragging) return;
                Dragging = false;
                ReleaseMouseCapture();
                if (!IsMouseOver) Hover(false);
                Committed?.Invoke(value);
                e.Handled = true;
            };
            // fare tekerlegi: %5 adim
            MouseWheel += (s, e) => { if (!IsEnabled) return; Value = value + (e.Delta > 0 ? 0.05 : -0.05); Committed?.Invoke(value); e.Handled = true; };
        }

        public double Value
        {
            get => value;
            set { this.value = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1); Layout(); }
        }

        void Drag(double x)
        {
            Value = ActualWidth > 0 ? x / ActualWidth : 0;
            Changing?.Invoke(value);
            if (LiveCommit) Committed?.Invoke(value);
        }

        void Hover(bool on)
        {
            double h = on ? 6 : 4;
            track.Height = fill.Height = h;
            track.CornerRadius = fill.CornerRadius = new CornerRadius(h / 2);
            thumb.Opacity = on && IsEnabled ? 1 : 0;
        }

        void Layout()
        {
            double w = ActualWidth;
            fill.Width = Math.Max(0, w * value);
            thumb.Margin = new Thickness(Math.Max(0, Math.Min(w - 12, w * value - 6)), 0, 0, 0);
        }
    }
}
