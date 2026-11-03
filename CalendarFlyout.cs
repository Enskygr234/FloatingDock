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
        readonly TextBlock bigTime = Text("", 44, 255, FontWeights.Light), seconds = Text("", 20, 140, FontWeights.Light);
        readonly TextBlock longDate = Text("", 13.5, 200), monthTitle = Text("", 15, 255, FontWeights.SemiBold);
        readonly UniformGrid days = new UniformGrid { Columns = 7, Rows = 6 };
        readonly DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        DateTime month;

        public CalendarFlyout() : base("FloatingDockCalendar")
        {
            Width = 336;
            var root = new StackPanel();

            // saat
            var clock = new StackPanel { Orientation = Orientation.Horizontal };
            bigTime.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            clock.Children.Add(bigTime);
            seconds.VerticalAlignment = VerticalAlignment.Bottom;
            seconds.Margin = new Thickness(6, 0, 0, 9);
            seconds.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            clock.Children.Add(seconds);
            root.Children.Add(clock);
            longDate.Margin = new Thickness(2, -4, 0, 0);
            root.Children.Add(longDate);

            // ince holografik cizgi
            root.Children.Add(new Border
            {
                Height = 1, Margin = new Thickness(0, 14, 0, 12),
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 0x6F, 0xD6, 0xFF), 0),
                    new GradientStop(Color.FromArgb(160, 0x8A, 0x7C, 0xFF), 0.5),
                    new GradientStop(Color.FromArgb(0, 0xC0, 0x6C, 0xF0), 1)
                }, 0)
            });

            // ay basligi
            var head = new Grid { Margin = new Thickness(2, 0, 0, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleBtn = Clickable(new Border { Padding = new Thickness(6, 4, 6, 4), Child = monthTitle }, () => Go(DateTime.Today), 8, 0, 24);
            titleBtn.HorizontalAlignment = HorizontalAlignment.Left;
            titleBtn.ToolTip = "Bugüne dön";
            head.Children.Add(titleBtn);
            var prev = Clickable(new Border { Width = 30, Height = 30, Child = Icon("", 12) }, () => Go(month.AddMonths(-1)), 15, 0, 28);
            var next = Clickable(new Border { Width = 30, Height = 30, Child = Icon("", 12) }, () => Go(month.AddMonths(1)), 15, 0, 28);
            Grid.SetColumn(prev, 1); Grid.SetColumn(next, 2);
            head.Children.Add(prev); head.Children.Add(next);
            root.Children.Add(head);

            // gun adlari
            var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var n in new[] { "Pt", "Sa", "Ça", "Pe", "Cu", "Ct", "Pz" })
            {
                var t = Text(n, 11.5, 130, FontWeights.SemiBold);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                names.Children.Add(t);
            }
            root.Children.Add(names);
            root.Children.Add(days);
            days.MouseWheel += (s, e) => Go(month.AddMonths(e.Delta > 0 ? -1 : 1));

            // alt
            var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 12, 6) };
            foot.Children.Add(Icon("", 13, 210));
            foot.Children.Add(new Border { Width = 8 });
            foot.Children.Add(Text("Tarih ve saat ayarları", 12.5, 220));
            var fb = Clickable(foot, () => { try { Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true }); } catch { } HideFlyout(); }, 8, 0, 26);
            fb.HorizontalAlignment = HorizontalAlignment.Left;
            fb.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(fb);

            SetBody(root);
            tick.Tick += (s, e) => UpdateClock();
        }

        protected override void OnOpening()
        {
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
                var t = Text(d.Day.ToString(), 13, (byte)(inMonth ? (weekend ? 175 : 235) : 70), isToday ? FontWeights.Bold : FontWeights.Normal);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Child = t, Margin = new Thickness(0, 1, 0, 1) };
                if (isToday)
                {
                    cell.Background = new LinearGradientBrush(Color.FromRgb(0x6F, 0x8C, 0xFF), Color.FromRgb(0xA0, 0x6C, 0xF5), 45);
                    cell.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.85 };
                    t.Foreground = Brushes.White;
                }
                else
                {
                    cell.Background = Brushes.Transparent;
                    cell.MouseEnter += (s, e) => cell.Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
                    cell.MouseLeave += (s, e) => cell.Background = Brushes.Transparent;
                }
                days.Children.Add(cell);
            }
            if (animate)
            {
                days.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));
            }
        }
    }
}
