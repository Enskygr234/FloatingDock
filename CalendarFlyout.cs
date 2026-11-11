using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>Saat dugmesinin actigi panel: buyuk saat + aylik takvim.</summary>
    class CalendarFlyout : Flyout
    {
        static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        readonly TextBlock bigTime = Text("", Theme.Hero, Theme.TextPrimary, FontWeight.FromOpenTypeWeight(350)), seconds = Text("", 20, Theme.TextTertiary, FontWeight.FromOpenTypeWeight(350));
        readonly TextBlock longDate = Text("", Theme.Body, Theme.TextSecondary), monthTitle = Text("", Theme.Subtitle, Theme.TextPrimary, FontWeights.SemiBold);
        readonly UniformGrid days = new UniformGrid { Columns = 7, Rows = 6 };
        readonly DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        DateTime month;

        public CalendarFlyout() : base("FloatingDockCalendar")
        {
            Width = 352;
            var root = new StackPanel();

            // ---- bildirim gecmisi ----
            var nh = new Grid { Margin = new Thickness(2, 0, 0, 6) };
            nh.Children.Add(SectionTitle("Bildirimler"));
            clearBtn = Clickable(new TextBlock { Text = "Tümünü temizle", FontFamily = Theme.Ui, FontSize = Theme.Caption + 0.5, Foreground = Theme.Brush(Theme.Accent), Margin = new Thickness(8, 4, 8, 5) },
                () => Hub.ClearHistory(), Theme.RadiusSm);
            clearBtn.HorizontalAlignment = HorizontalAlignment.Right;
            nh.Children.Add(clearBtn);
            root.Children.Add(nh);
            root.Children.Add(new ScrollViewer
            {
                Content = noteList, MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly
            });
            root.Children.Add(Divider(10, 12));
            Hub.HistoryChanged += () => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) RenderNotes(); }));

            // saat (solda) + simdiki hava (sagda)
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            var clock = new StackPanel { Orientation = Orientation.Horizontal };
            bigTime.FontFamily = Theme.Display;
            seconds.FontFamily = Theme.Display;
            // rakamlar sabit genislikte: saniye ilerlerken yazi kaymasin
            System.Windows.Documents.Typography.SetNumeralAlignment(bigTime, FontNumeralAlignment.Tabular);
            System.Windows.Documents.Typography.SetNumeralAlignment(seconds, FontNumeralAlignment.Tabular);
            clock.Children.Add(bigTime);
            seconds.VerticalAlignment = VerticalAlignment.Bottom;
            seconds.Margin = new Thickness(6, 0, 0, 9);
            clock.Children.Add(seconds);
            left.Children.Add(clock);
            longDate.Margin = new Thickness(2, -2, 0, 0);
            left.Children.Add(longDate);
            top.Children.Add(left);
            weatherNow = Clickable(BuildWeatherNow(), () =>
            {
                // ayrinti paneli (dock baglar); yoksa yalniz yenile
                if (OpenWeather != null) { HideFlyout(); OpenWeather(); }
                else { wDesc.Text = "Yenileniyor…"; _ = Weather.RefreshAsync(); }
            }, Theme.RadiusLg);
            weatherNow.HorizontalAlignment = HorizontalAlignment.Right;
            weatherNow.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(weatherNow, 1);
            top.Children.Add(weatherNow);
            root.Children.Add(top);
            hourly.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(hourly);
            forecast.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(forecast);
            Weather.Changed += () => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) RenderWeather(); }));

            // zamanlayici
            root.Children.Add(Divider(14, 10));
            root.Children.Add(BuildTimer());
            IslandTimer.Changed += () => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) RenderTimer(); }));

            root.Children.Add(Divider(12, 10));

            // ay basligi
            var head = new Grid { Margin = new Thickness(2, 0, 0, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleBtn = Clickable(new Border { Padding = new Thickness(8, 5, 8, 5), Child = monthTitle }, () => Go(DateTime.Today));
            titleBtn.HorizontalAlignment = HorizontalAlignment.Left;
            titleBtn.ToolTip = "Bugüne dön";
            head.Children.Add(titleBtn);
            var prev = IconButton("", "Önceki ay", () => Go(month.AddMonths(-1)), 32, 12);
            var next = IconButton("", "Sonraki ay", () => Go(month.AddMonths(1)), 32, 12);
            Grid.SetColumn(prev, 1); Grid.SetColumn(next, 2);
            head.Children.Add(prev); head.Children.Add(next);
            root.Children.Add(head);

            // gun adlari
            var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var n in new[] { "Pt", "Sa", "Ça", "Pe", "Cu", "Ct", "Pz" })
            {
                var t = Text(n, Theme.Caption, Theme.TextTertiary, FontWeights.SemiBold);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                names.Children.Add(t);
            }
            root.Children.Add(names);
            root.Children.Add(days);
            days.MouseWheel += (s, e) => { e.Handled = true; Go(month.AddMonths(e.Delta > 0 ? -1 : 1)); };

            // alt
            var fb = LinkButton("", "Tarih ve saat ayarları", () => { try { Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true }); } catch { } HideFlyout(); });
            fb.Margin = new Thickness(-6, 10, 0, -6);
            root.Children.Add(fb);

            SetBody(root, scroll: true);
            tick.Tick += (s, e) => { UpdateClock(); if (IslandTimer.Active) RenderTimerValue(); };
        }

        // ---------------- hava durumu ----------------
        Border weatherNow;
        readonly Border wIcon = new Border { Width = 38, Height = 38, Margin = new Thickness(0, 0, 10, 0) };
        readonly TextBlock wTemp = Text("", Theme.Title, Theme.TextPrimary, FontWeights.SemiBold);
        readonly TextBlock wDesc = Text("", Theme.Caption, Theme.TextSecondary);
        readonly TextBlock wPlace = Text("", Theme.Caption, Theme.TextTertiary);
        readonly UniformGrid forecast = new UniformGrid { Columns = 5 };
        /// <summary>Onumuzdeki 12 saat, 2 saat arayla.</summary>
        readonly UniformGrid hourly = new UniformGrid { Columns = 6 };
        /// <summary>Dock baglar: hava durumu ayrinti panelini ac.</summary>
        public static Action OpenWeather;

        UIElement BuildWeatherNow()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 6) };
            sp.Children.Add(wIcon);
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            wTemp.FontFamily = Theme.Display;
            wTemp.Margin = new Thickness(0, -4, 0, -2);
            txt.Children.Add(wTemp);
            wDesc.MaxWidth = 120;
            txt.Children.Add(wDesc);
            wPlace.MaxWidth = 120;
            txt.Children.Add(wPlace);
            sp.Children.Add(txt);
            return sp;
        }

        void RenderWeather()
        {
            bool on = Weather.Enabled;
            weatherNow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            var d = Weather.Current;
            forecast.Visibility = on && d != null && d.Days.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            hourly.Visibility = on && d != null && d.Hours.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            if (d == null)
            {
                wIcon.Child = null;
                wTemp.Text = "";
                wTemp.Visibility = Visibility.Collapsed;
                wDesc.Text = Weather.LastError != null ? "Hava durumu alınamadı" : "Hava durumu yükleniyor…";
                wPlace.Text = Settings.Current.WeatherCity;
                return;
            }
            wTemp.Visibility = Visibility.Visible;
            wIcon.Child = Weather.Icon(d.Code, d.IsDay, 38, Theme.Fg());
            wTemp.Text = Weather.Temp(d.Temp);
            wDesc.Text = Weather.Describe(d.Code);
            wPlace.Text = d.Place;
            var nc = Weather.NowcastText(d);
            weatherNow.ToolTip = $"{d.Place}: {Weather.Describe(d.Code)}, hissedilen {Weather.Temp(d.Feels)}, rüzgâr {Math.Round(d.Wind)} km/sa" +
                                 (nc != null ? "\n" + nc : "") + $"\n{d.Fetched:HH:mm} itibarıyla · ayrıntılar için tıkla";

            // saatlik: simdi + 2 saat arayla 5 nokta daha
            hourly.Children.Clear();
            var now = d.Now;
            var next = d.Hours.Where(h => h.Time.AddHours(1) > now).ToList();
            for (int i = 0; i < next.Count && hourly.Children.Count < 6; i += 2)
            {
                var h = next[i];
                var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var n = Text(i == 0 ? "Şimdi" : h.Time.ToString("HH:mm", Tr), Theme.Caption - 0.5, Theme.TextTertiary, FontWeights.SemiBold);
                n.HorizontalAlignment = HorizontalAlignment.Center;
                col.Children.Add(n);
                var hi = Weather.Icon(h.Code, h.IsDay, 20, Theme.Fg(Theme.TextSecondary));
                hi.Margin = new Thickness(0, 4, 0, 3);
                hi.ToolTip = Weather.Describe(h.Code) + (h.RainChance > 0 ? " · yağış %" + h.RainChance : "");
                col.Children.Add(hi);
                var t0 = Text(Weather.Temp(h.Temp), Theme.Caption + 0.5, Theme.TextPrimary);
                t0.HorizontalAlignment = HorizontalAlignment.Center;
                col.Children.Add(t0);
                hourly.Children.Add(col);
            }

            forecast.Children.Clear();
            foreach (var day in d.Days.Take(5))
            {
                var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var name = day.Date.Date == d.Now.Date ? "Bugün" : day.Date.ToString("ddd", Tr);
                var n = Text(name, Theme.Caption, Theme.TextTertiary, FontWeights.SemiBold);
                n.HorizontalAlignment = HorizontalAlignment.Center;
                col.Children.Add(n);
                var ic = Weather.Icon(day.Code, true, 22, Theme.Fg(Theme.TextSecondary));
                ic.Margin = new Thickness(0, 5, 0, 4);
                ic.ToolTip = Weather.Describe(day.Code);
                col.Children.Add(ic);
                var t = new TextBlock { FontFamily = Theme.Ui, FontSize = Theme.Caption + 0.5, HorizontalAlignment = HorizontalAlignment.Center };
                t.Inlines.Add(new System.Windows.Documents.Run(Weather.Temp(day.Max)) { Foreground = Theme.Fg() });
                t.Inlines.Add(new System.Windows.Documents.Run("  " + Weather.Temp(day.Min)) { Foreground = Theme.Fg(Theme.TextTertiary) });
                col.Children.Add(t);
                forecast.Children.Add(col);
            }
        }

        // ---------------- zamanlayici ----------------
        readonly WrapPanel timerChips = new WrapPanel();
        readonly Grid timerRunning = new Grid();
        readonly TextBlock timerValue = Text("", Theme.Subtitle, Theme.TextPrimary, FontWeights.SemiBold);
        readonly TextBlock timerLabel = Text("", Theme.Caption, Theme.TextSecondary);
        Border timerPause;

        UIElement BuildTimer()
        {
            var sp = new StackPanel();
            var title = SectionTitle("Zamanlayıcı");
            ((FrameworkElement)title).Margin = new Thickness(2, 0, 0, 8);
            sp.Children.Add(title);

            foreach (var (label, min) in new[] { ("1 dk", 1), ("5 dk", 5), ("10 dk", 10), ("25 dk", 25), ("Kronometre", 0) })
            {
                int m = min;
                var chip = Clickable(new TextBlock { Text = label, FontFamily = Theme.Ui, FontSize = Theme.Caption + 0.5, Foreground = Theme.Fg(), Margin = new Thickness(10, 5, 10, 6) },
                    () => { if (m == 0) IslandTimer.StartStopwatch(); else IslandTimer.StartCountdown(TimeSpan.FromMinutes(m)); },
                    Theme.RadiusSm, (byte)(Theme.SurfaceIdle + 4), (byte)(Theme.SurfaceHover + 6));
                chip.BorderBrush = Theme.Ink(Theme.StrokeSubtle);
                chip.Margin = new Thickness(0, 0, 6, 0);
                chip.ToolTip = m == 0 ? "Kronometreyi başlat (adada görünür)" : label + " geri sayım başlat (adada görünür)";
                timerChips.Children.Add(chip);
            }
            sp.Children.Add(timerChips);

            timerRunning.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timerRunning.ColumnDefinitions.Add(new ColumnDefinition());
            timerRunning.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = Icon("", 16, Theme.TextSecondary);
            ic.Foreground = Theme.Brush(Theme.Accent);
            ic.Margin = new Thickness(4, 0, 12, 0);
            timerRunning.Children.Add(ic);
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(timerValue, FontNumeralAlignment.Tabular);
            tx.Children.Add(timerValue);
            tx.Children.Add(timerLabel);
            Grid.SetColumn(tx, 1); timerRunning.Children.Add(tx);
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            timerPause = IconButton("", "Duraklat", IslandTimer.TogglePause, 32, 13);
            btns.Children.Add(timerPause);
            btns.Children.Add(IconButton("", "Durdur", IslandTimer.Stop, 32, 13));
            Grid.SetColumn(btns, 2); timerRunning.Children.Add(btns);
            sp.Children.Add(timerRunning);
            return sp;
        }

        void RenderTimer()
        {
            bool on = IslandTimer.Active;
            timerChips.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            timerRunning.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            bool p = IslandTimer.Paused;
            ((TextBlock)((Border)timerPause.Child).Child).Text = p ? "" : "";
            timerPause.ToolTip = p ? "Devam et" : "Duraklat";
            RenderTimerValue();
        }

        void RenderTimerValue()
        {
            timerValue.Text = IslandTimer.Format(IslandTimer.Value);
            timerLabel.Text = IslandTimer.Label + (IslandTimer.Paused ? " · duraklatıldı" : "");
        }

        readonly StackPanel noteList = new StackPanel();
        readonly Border clearBtn;

        void RenderNotes()
        {
            noteList.Children.Clear();
            var h = Hub.History;
            clearBtn.Visibility = h.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (h.Count == 0)
            {
                var e = Text(Settings.Current.DoNotDisturb ? "Yeni bildirim yok · Rahatsız etme açık" : "Yeni bildirim yok", Theme.Body, Theme.TextTertiary);
                e.Margin = new Thickness(2, 2, 0, 4);
                noteList.Children.Add(e);
                return;
            }
            foreach (var n in h)
            {
                var g = new Grid { Margin = new Thickness(10, 8, 6, 8) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var img = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                if (n.Info.Image != null) img.Source = n.Info.Image;
                else if (n.Info.Glyph != null) { var gl = Icon(n.Info.Glyph, 18, Theme.TextSecondary); gl.Foreground = Theme.Brush(Theme.Accent); gl.VerticalAlignment = VerticalAlignment.Top; gl.Margin = new Thickness(3, 4, 3, 0); g.Children.Add(gl); }
                else if (n.IconPath != null)
                {
                    var c = ShellIcons.Cached(n.IconPath);
                    if (c != null) img.Source = c;
                    else ShellIcons.GetAsync(n.IconPath).ContinueWith(t => img.Dispatcher.BeginInvoke(new Action(() => img.Source = t.Result)));
                }
                g.Children.Add(img);
                var tp = new StackPanel { Margin = new Thickness(12, 0, 6, 0) };
                tp.Children.Add(Text(n.AppName + "  ·  " + Hub.Ago(n.Time), Theme.Caption, Theme.TextTertiary));
                var title = Text(n.Info.Title, Theme.Body, Theme.TextPrimary, FontWeights.SemiBold);
                title.Margin = new Thickness(0, 2, 0, 0);
                tp.Children.Add(title);
                if (!string.IsNullOrEmpty(n.Info.Body))
                {
                    var b = Text(n.Info.Body, Theme.Caption + 0.5, Theme.TextSecondary);
                    b.TextWrapping = TextWrapping.Wrap; b.TextTrimming = TextTrimming.CharacterEllipsis; b.MaxHeight = 36;
                    tp.Children.Add(b);
                }
                Grid.SetColumn(tp, 1); g.Children.Add(tp);
                var entry = n;
                var x = IconButton("", "Bildirimi kapat", () => Hub.Remove(entry), 28, 10);
                x.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(x, 2); g.Children.Add(x);
                var card = Card(g, 0);
                card.Margin = new Thickness(0, 0, 0, 6);
                noteList.Children.Add(card);
            }
        }

        protected override void OnOpening()
        {
            RenderNotes();
            RenderWeather();
            RenderTimer();
            Hub.MarkRead();
            UpdateClock();
            Go(DateTime.Today, false);
            tick.Start();
        }

        protected override void OnClosing() => tick.Stop();

        void UpdateClock()
        {
            var n = DateTime.Now;
            bigTime.Text = n.ToString("HH:mm", Tr);
            seconds.Text = n.ToString("ss", Tr);
            var d = n.ToString("d MMMM yyyy, dddd", Tr);
            longDate.Text = char.ToUpper(d[0], Tr) + d.Substring(1);
        }

        void Go(DateTime m, bool animate = true)
        {
            month = new DateTime(m.Year, m.Month, 1);
            var mt = month.ToString("MMMM yyyy", Tr);
            monthTitle.Text = char.ToUpper(mt[0], Tr) + mt.Substring(1);
            days.Children.Clear();
            int offset = ((int)month.DayOfWeek + 6) % 7;   // Pazartesi = 0
            var start = month.AddDays(-offset);
            var today = DateTime.Today;
            for (int i = 0; i < 42; i++)
            {
                var d = start.AddDays(i);
                bool inMonth = d.Month == month.Month, isToday = d == today, weekend = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
                var t = Text(d.Day.ToString(), Theme.Body, (byte)(inMonth ? (weekend ? Theme.TextSecondary : Theme.TextPrimary) : Theme.TextDisabled), isToday ? FontWeights.SemiBold : FontWeights.Normal);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Child = t, Margin = new Thickness(0, 1, 0, 1) };
                if (isToday)
                {
                    cell.Background = Theme.Brush(Theme.Accent);
                    t.Foreground = Theme.Brush(Theme.OnAccent);
                }
                else
                {
                    cell.Background = Brushes.Transparent;
                    cell.MouseEnter += (s, e) => cell.Background = Theme.Ink(Theme.SurfaceHover);
                    cell.MouseLeave += (s, e) => cell.Background = Brushes.Transparent;
                }
                days.Children.Add(cell);
            }
            if (animate)
            {
                days.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, Theme.D(Theme.Normal)));
            }
        }
    }
}
