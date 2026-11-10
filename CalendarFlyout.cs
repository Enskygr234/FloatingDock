using System;
using System.Diagnostics;
using System.Globalization;
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
            nh.Children.Add(SectionTitle(L.T("Bildirimler", "Notifications")));
            clearBtn = Clickable(new TextBlock { Text = L.T("Tümünü temizle", "Clear all"), FontFamily = Theme.Ui, FontSize = Theme.Caption + 0.5, Foreground = Theme.Brush(Theme.Accent), Margin = new Thickness(8, 4, 8, 5) },
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

            // saat
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
            root.Children.Add(clock);
            longDate.Margin = new Thickness(2, -2, 0, 0);
            root.Children.Add(longDate);

            root.Children.Add(Divider(14, 10));

            // ay basligi
            var head = new Grid { Margin = new Thickness(2, 0, 0, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleBtn = Clickable(new Border { Padding = new Thickness(8, 5, 8, 5), Child = monthTitle }, () => Go(DateTime.Today));
            titleBtn.HorizontalAlignment = HorizontalAlignment.Left;
            titleBtn.ToolTip = L.T("Bugüne dön", "Back to today");
            head.Children.Add(titleBtn);
            var prev = IconButton("", L.T("Önceki ay", "Previous month"), () => Go(month.AddMonths(-1)), 32, 12);
            var next = IconButton("", L.T("Sonraki ay", "Next month"), () => Go(month.AddMonths(1)), 32, 12);
            Grid.SetColumn(prev, 1); Grid.SetColumn(next, 2);
            head.Children.Add(prev); head.Children.Add(next);
            root.Children.Add(head);

            // gun adlari
            var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var n in L.En ? new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" } : new[] { "Pt", "Sa", "Ça", "Pe", "Cu", "Ct", "Pz" })
            {
                var t = Text(n, Theme.Caption, Theme.TextTertiary, FontWeights.SemiBold);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                names.Children.Add(t);
            }
            root.Children.Add(names);
            root.Children.Add(days);
            days.MouseWheel += (s, e) => Go(month.AddMonths(e.Delta > 0 ? -1 : 1));

            // alt
            var fb = LinkButton("", L.T("Tarih ve saat ayarları", "Date and time settings"), () => { try { Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true }); } catch { } HideFlyout(); });
            fb.Margin = new Thickness(-6, 10, 0, -6);
            root.Children.Add(fb);

            SetBody(root);
            tick.Tick += (s, e) => UpdateClock();
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
                var e = Text(Settings.Current.DoNotDisturb ? L.T("Yeni bildirim yok · Rahatsız etme açık", "No new notifications · Do not disturb is on") : L.T("Yeni bildirim yok", "No new notifications"), Theme.Body, Theme.TextTertiary);
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
                if (n.IconPath != null)
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
                var x = IconButton("", L.T("Bildirimi kapat", "Dismiss"), () => Hub.Remove(entry), 28, 10);
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
            Hub.MarkRead();
            UpdateClock();
            Go(DateTime.Today, false);
            tick.Start();
        }

        protected override void OnClosing() => tick.Stop();

        void UpdateClock()
        {
            var n = DateTime.Now;
            bigTime.Text = n.ToString("HH:mm", L.Culture);
            seconds.Text = n.ToString("ss", L.Culture);
            longDate.Text = L.Cap(n.ToString(L.En ? "dddd, MMMM d, yyyy" : "d MMMM yyyy, dddd", L.Culture));
        }

        void Go(DateTime m, bool animate = true)
        {
            month = new DateTime(m.Year, m.Month, 1);
            monthTitle.Text = L.Cap(month.ToString("MMMM yyyy", L.Culture));
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
