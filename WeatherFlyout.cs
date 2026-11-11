using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FloatingDock
{
    /// <summary>Dock'taki hava durumuna tiklayinca acilan panel: simdi, onumuzdeki 2 saatin yagisi, saatlik ve 7 gunluk tahmin, ayrintilar.</summary>
    class WeatherFlyout : Flyout
    {
        static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        /// <summary>Dock baglar: ayarlar panelini ac.</summary>
        public Action OpenSettings;

        readonly WrapPanel places = new WrapPanel();
        readonly Border nowIcon = new Border { Width = 56, Height = 56, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock nowTemp = Text("", Theme.Hero, Theme.TextPrimary, FontWeight.FromOpenTypeWeight(350));
        readonly TextBlock nowDesc = Text("", Theme.BodyLarge, Theme.TextPrimary, FontWeights.SemiBold);
        readonly TextBlock nowSub = Text("", Theme.Caption, Theme.TextSecondary);
        readonly TextBlock nowPlace = Text("", Theme.Caption, Theme.TextTertiary);
        readonly TextBlock rainText = Text("", Theme.Body, Theme.TextPrimary);
        readonly Grid rainBars = new Grid { Height = 34, Margin = new Thickness(0, 8, 0, 0) };
        readonly StackPanel hours = new StackPanel { Orientation = Orientation.Horizontal };
        readonly ScrollViewer hoursScroll;
        readonly UniformGrid details = new UniformGrid { Columns = 2 };
        readonly StackPanel days = new StackPanel();
        readonly TextBlock footer = Text("", Theme.Caption, Theme.TextTertiary);
        readonly StackPanel body = new StackPanel();
        readonly TextBlock empty = Text("", Theme.Body, Theme.TextSecondary);

        public WeatherFlyout() : base("FloatingDockWeather")
        {
            Width = 384;
            var root = new StackPanel();

            // ---- yerler + yenile ----
            var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(places);
            var refresh = IconButton("", "Yenile", () => { footer.Text = "Yenileniyor…"; _ = Weather.RefreshAsync(); }, 30, 13);
            refresh.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(refresh, 1);
            head.Children.Add(refresh);
            root.Children.Add(head);

            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(2, 8, 2, 8);
            root.Children.Add(empty);

            // ---- simdi ----
            var now = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 0) };
            now.Children.Add(nowIcon);
            nowTemp.FontFamily = Theme.Display;
            nowTemp.Margin = new Thickness(0, -6, 14, -4);
            now.Children.Add(nowTemp);
            var nt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nt.Children.Add(nowDesc);
            nowSub.Margin = new Thickness(0, 2, 0, 0);
            nt.Children.Add(nowSub);
            nt.Children.Add(nowPlace);
            now.Children.Add(nt);
            body.Children.Add(now);

            // ---- onumuzdeki 2 saat ----
            var rainCard = new StackPanel();
            var rh = new DockPanel();
            var umbrella = Icon("", 14, Theme.TextSecondary);
            umbrella.Margin = new Thickness(0, 0, 8, 0);
            rh.Children.Add(umbrella);
            rh.Children.Add(rainText);
            rainCard.Children.Add(rh);
            rainCard.Children.Add(rainBars);
            var axis = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var (txt, al) in new[] { ("Şimdi", HorizontalAlignment.Left), ("1 sa", HorizontalAlignment.Center), ("2 sa", HorizontalAlignment.Right) })
                axis.Children.Add(new TextBlock { Text = txt, FontFamily = Theme.Ui, FontSize = Theme.Caption - 1, Foreground = Theme.Fg(Theme.TextTertiary), HorizontalAlignment = al });
            rainCard.Children.Add(axis);
            var rc = Card(rainCard, 12);
            rc.Margin = new Thickness(0, 14, 0, 0);
            body.Children.Add(rc);

            // ---- saatlik ----
            body.Children.Add(Section("Saatlik"));
            hoursScroll = new ScrollViewer
            {
                Content = hours, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.HorizontalOnly, Margin = new Thickness(-4, 0, -4, 0)
            };
            // fare tekerlegi saatleri yatay kaydirsin (paneli asagi degil)
            hoursScroll.PreviewMouseWheel += (s, e) =>
            {
                e.Handled = true;
                hoursScroll.ScrollToHorizontalOffset(hoursScroll.HorizontalOffset - e.Delta * 0.6);
            };
            body.Children.Add(hoursScroll);

            // ---- ayrintilar ----
            body.Children.Add(Section("Ayrıntılar"));
            details.Margin = new Thickness(-3, 0, -3, 0);
            body.Children.Add(details);

            // ---- 7 gun ----
            body.Children.Add(Section("7 gün"));
            body.Children.Add(days);

            root.Children.Add(body);

            root.Children.Add(Divider(12, 6));
            var foot = new Grid { Margin = new Thickness(-6, 0, -6, -6) };
            footer.Margin = new Thickness(8, 0, 0, 0);
            footer.TextTrimming = TextTrimming.CharacterEllipsis;
            foot.Children.Add(footer);
            var set = LinkButton("", "Konum ve uyarılar", () => { HideFlyout(); OpenSettings?.Invoke(); });
            set.HorizontalAlignment = HorizontalAlignment.Right;
            foot.Children.Add(set);
            root.Children.Add(foot);

            SetBody(root, scroll: true);
            Weather.Changed += () => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) Render(); }));
        }

        protected override void OnOpening()
        {
            Render();
            // veri yarim saatten eskiyse sessizce tazele
            var d = Weather.Current;
            if (Weather.Enabled && (d == null || (DateTime.Now - d.Fetched).TotalMinutes > 30)) _ = Weather.RefreshAsync();
            hoursScroll.ScrollToHorizontalOffset(0);
        }

        static UIElement Section(string title)
        {
            var t = SectionTitle(title);
            ((FrameworkElement)t).Margin = new Thickness(2, 16, 0, 8);
            return t;
        }

        // ================= cizim =================
        void Render()
        {
            RenderPlaces();
            var d = Weather.Current;
            bool has = d != null;
            body.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            empty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            if (!has)
            {
                empty.Text = !Weather.Enabled ? "Hava durumu kapalı. Konumunu kullanabilir ya da ayarlardan bir şehir ekleyebilirsin."
                           : Weather.LastError != null ? "Hava durumu alınamadı: " + Weather.LastError
                           : "Hava durumu yükleniyor…";
                footer.Text = "open-meteo.com";
                return;
            }

            var today = d.Days.FirstOrDefault(x => x.Date.Date == d.Now.Date);
            nowIcon.Child = Weather.Icon(d.Code, d.IsDay, 56, Theme.Fg());
            nowTemp.Text = Weather.Temp(d.Temp);
            nowDesc.Text = Weather.Describe(d.Code);
            nowSub.Text = "Hissedilen " + Weather.Temp(d.Feels) + (today != null ? "  ·  ↑" + Weather.Temp(today.Max) + "  ↓" + Weather.Temp(today.Min) : "");
            nowPlace.Text = d.Place;

            RenderRain(d);
            RenderHours(d);
            RenderDetails(d, today);
            RenderDays(d);
            footer.Text = "open-meteo.com · " + d.Fetched.ToString("HH:mm", Tr) + " itibarıyla";
        }

        void RenderPlaces()
        {
            places.Children.Clear();
            var s = Settings.Current;
            places.Children.Add(Chip("", s.WeatherAuto ? (string.IsNullOrEmpty(s.WeatherPlace) ? "Konumum" : s.WeatherPlace) : "Konumum", s.WeatherAuto,
                async () =>
                {
                    if (s.WeatherAuto) return;
                    footer.Text = "Konum alınıyor…";
                    var err = await Weather.UseAutoAsync();
                    if (err != null) { footer.Text = err; empty.Text = err; }
                    Render();
                }, null, "Windows konum servisiyle bulunduğun yer"));
            var saved = Weather.Saved;
            // ayarlardan secilmis ama listede olmayan sehir (onceki surumden gelen): etkin olarak goster
            if (!s.WeatherAuto && !string.IsNullOrWhiteSpace(s.WeatherCity) && !saved.Any(Weather.IsActive))
                places.Children.Add(Chip(null, string.IsNullOrEmpty(s.WeatherPlace) ? s.WeatherCity : s.WeatherPlace, true, () => { }, null, null));
            foreach (var p in saved)
            {
                var place = p;
                places.Children.Add(Chip(null, p.Name, Weather.IsActive(p), () => { Weather.UsePlace(place); Render(); },
                    () => Weather.RemovePlace(place), "Sağ tık: listeden kaldır"));
            }
        }

        Border Chip(string glyph, string text, bool on, Action click, Action remove, string tip)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 4, 11, 5) };
            if (glyph != null)
            {
                var ic = Icon(glyph, 11, on ? (byte?)null : Theme.TextSecondary);
                if (on) ic.Foreground = Theme.Brush(Theme.OnAccent);
                ic.Margin = new Thickness(0, 1, 6, 0);
                sp.Children.Add(ic);
            }
            var t = Text(text, Theme.Caption + 0.5, on ? (byte?)null : Theme.TextSecondary, on ? FontWeights.SemiBold : FontWeights.Normal);
            if (on) t.Foreground = Theme.Brush(Theme.OnAccent);
            t.MaxWidth = 130;
            sp.Children.Add(t);
            var b = Clickable(sp, click, 14, on ? (byte)0 : Theme.SurfaceIdle);
            if (on) b.Background = Theme.Brush(Theme.Accent); // vurgu fircasi "ink" degil: uzerine gelince solma animasyonu ona dokunmaz
            b.Margin = new Thickness(0, 0, 6, 6);
            b.ToolTip = tip;
            if (remove != null)
            {
                var m = new ContextMenu();
                var mi = new MenuItem { Header = "Listeden kaldır" };
                mi.Click += (s, e) => remove();
                m.Items.Add(mi);
                b.ContextMenu = m;
            }
            return b;
        }

        void RenderRain(WeatherData d)
        {
            rainText.Text = Weather.NowcastText(d) ?? "Kısa vadeli yağış verisi yok";
            rainBars.Children.Clear();
            rainBars.ColumnDefinitions.Clear();
            int n = d.Minutely.Length;
            if (n == 0) { rainBars.Visibility = Visibility.Collapsed; return; }
            rainBars.Visibility = Visibility.Visible;
            // 2 mm / 15 dk siddetli yagis sayilir: cubuk tam boy
            for (int i = 0; i < n; i++)
            {
                rainBars.ColumnDefinitions.Add(new ColumnDefinition());
                double v = d.Minutely[i];
                double h = v < 0.05 ? 3 : Math.Max(6, Math.Min(1, Math.Sqrt(v / 2.0)) * 34);
                var bar = new Border
                {
                    Height = h, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 0),
                    CornerRadius = new CornerRadius(3), Background = v < 0.05 ? Theme.Ink(Theme.StrokeSubtle) : Theme.Brush(Theme.Accent),
                    ToolTip = d.MinutelyStart.AddMinutes(15 * i).ToString("HH:mm", Tr) + " · " + v.ToString("0.0", Tr) + " mm"
                };
                Grid.SetColumn(bar, i);
                rainBars.Children.Add(bar);
            }
        }

        void RenderHours(WeatherData d)
        {
            hours.Children.Clear();
            var now = d.Now;
            foreach (var h in d.Hours.Where(x => x.Time.AddHours(1) > now).Take(24))
            {
                bool first = h.Time <= now;
                var col = new StackPanel { Width = 52, Margin = new Thickness(0, 0, 0, 0) };
                var label = Text(first ? "Şimdi" : h.Time.ToString("HH", Tr), Theme.Caption, first ? Theme.TextPrimary : Theme.TextTertiary, FontWeights.SemiBold);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                col.Children.Add(label);
                var ic = Weather.Icon(h.Code, h.IsDay, 24, Theme.Fg(Theme.TextSecondary));
                ic.Margin = new Thickness(0, 6, 0, 5);
                ic.ToolTip = Weather.Describe(h.Code);
                col.Children.Add(ic);
                var t = Text(Weather.Temp(h.Temp), Theme.Body, Theme.TextPrimary);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                col.Children.Add(t);
                // yagis olasiligi: anlamliysa vurgu renginde
                var r = new TextBlock
                {
                    Text = h.RainChance >= 20 ? "%" + h.RainChance : " ", FontFamily = Theme.Ui, FontSize = Theme.Caption - 0.5,
                    Foreground = Theme.Brush(Theme.Accent), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0)
                };
                col.Children.Add(r);
                var cell = new Border { Child = col, Padding = new Thickness(0, 6, 0, 6), CornerRadius = new CornerRadius(Theme.RadiusMd) };
                if (first) cell.Background = Theme.Ink(Theme.SurfaceIdle);
                hours.Children.Add(cell);
            }
        }

        void RenderDetails(WeatherData d, WeatherDay today)
        {
            details.Children.Clear();
            details.Children.Add(Tile("Nem", "%" + Math.Round(d.Humidity), HumidityHint(d.Humidity)));
            details.Children.Add(Tile("Rüzgâr", Math.Round(d.Wind) + " km/sa", Weather.Compass(d.WindDir) + " yönünden", WindArrow(d.WindDir)));
            details.Children.Add(Tile("UV indeksi", d.Uv.ToString("0", Tr) + " · " + Weather.UvText(d.Uv),
                today != null ? "Bugün en fazla " + today.UvMax.ToString("0", Tr) : null));
            if (d.Aqi >= 0)
                details.Children.Add(Tile("Hava kalitesi", d.Aqi + " · " + Weather.AqiText(d.Aqi), d.Pm25 >= 0 ? "PM2.5: " + d.Pm25.ToString("0", Tr) + " µg/m³" : null));
            if (today != null && today.Sunrise != DateTime.MinValue)
            {
                details.Children.Add(Tile("Gün doğumu", today.Sunrise.ToString("HH:mm", Tr), null));
                details.Children.Add(Tile("Gün batımı", today.Sunset.ToString("HH:mm", Tr), DaylightHint(today)));
            }
            details.Children.Add(Tile("Basınç", Math.Round(d.Pressure) + " hPa", null));
            if (today != null) details.Children.Add(Tile("Yağış olasılığı", "%" + today.RainChance, "Bugün en yüksek"));
        }

        static string HumidityHint(double h) => h < 30 ? "Kuru" : h < 60 ? "Rahat" : h < 80 ? "Nemli" : "Çok nemli";

        static string DaylightHint(WeatherDay t)
        {
            var len = t.Sunset - t.Sunrise;
            return "Gün ışığı " + (int)len.TotalHours + " sa " + len.Minutes + " dk";
        }

        static FrameworkElement WindArrow(double fromDeg)
        {
            // ruzgarin gittigi yonu gosteren ok (geldigi yon + 180)
            var p = new Path
            {
                Data = Geometry.Parse("M6,0 L11,11 L6,8.5 L1,11 Z"), Fill = Theme.Fg(Theme.TextSecondary), Width = 12, Height = 12, Stretch = Stretch.Fill,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(fromDeg + 180)
            };
            return new Border { Width = 14, Height = 14, Child = p };
        }

        /// <summary>Ayrinti kutucugu: baslik, buyuk deger (yaninda istege bagli isaret), kucuk aciklama.</summary>
        Border Tile(string title, string value, string hint, FrameworkElement mark = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(Text(title, Theme.Caption, Theme.TextTertiary, FontWeights.SemiBold));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
            row.Children.Add(Text(value, Theme.Subtitle, Theme.TextPrimary, FontWeights.SemiBold));
            if (mark != null) { mark.Margin = new Thickness(8, 0, 0, 0); mark.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(mark); }
            sp.Children.Add(row);
            if (hint != null)
            {
                var h = Text(hint, Theme.Caption, Theme.TextSecondary);
                h.Margin = new Thickness(0, 1, 0, 0);
                sp.Children.Add(h);
            }
            var c = Card(sp, 10);
            c.Margin = new Thickness(3);
            return c;
        }

        void RenderDays(WeatherData d)
        {
            days.Children.Clear();
            if (d.Days.Count == 0) return;
            double lo = d.Days.Min(x => x.Min), hi = d.Days.Max(x => x.Max), span = Math.Max(1, hi - lo);
            foreach (var day in d.Days)
            {
                var g = new Grid { Margin = new Thickness(2, 3, 2, 3), Height = 30 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                var todayLoc = d.Now.Date;
                var name = day.Date.Date == todayLoc ? "Bugün" : day.Date.Date == todayLoc.AddDays(1) ? "Yarın" : day.Date.ToString("dddd", Tr);
                g.Children.Add(Text(name, Theme.Body, day.Date.Date == todayLoc ? Theme.TextPrimary : Theme.TextSecondary));
                var ic = Weather.Icon(day.Code, true, 20, Theme.Fg(Theme.TextSecondary));
                ic.ToolTip = Weather.Describe(day.Code);
                ic.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetColumn(ic, 1); g.Children.Add(ic);
                var rain = Text(day.RainChance >= 20 ? "%" + day.RainChance : "", Theme.Caption, null);
                rain.Foreground = Theme.Brush(Theme.Accent);
                Grid.SetColumn(rain, 2); g.Children.Add(rain);
                var min = Text(Weather.Temp(day.Min), Theme.Body, Theme.TextTertiary);
                min.HorizontalAlignment = HorizontalAlignment.Right; min.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(min, 3); g.Children.Add(min);
                // sicaklik araligi cubugu: haftanin en soguk-en sicak araliginda bu gunun yeri
                var track = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Theme.Ink(Theme.StrokeSubtle) });
                var fill = new Border { CornerRadius = new CornerRadius(2), Background = Theme.Brush(Theme.Accent), HorizontalAlignment = HorizontalAlignment.Left };
                track.Children.Add(fill);
                track.SizeChanged += (s, e) =>
                {
                    double w = e.NewSize.Width;
                    fill.Margin = new Thickness(w * (day.Min - lo) / span, 0, 0, 0);
                    fill.Width = Math.Max(4, w * (day.Max - day.Min) / span);
                };
                Grid.SetColumn(track, 4); g.Children.Add(track);
                var max = Text(Weather.Temp(day.Max), Theme.Body, Theme.TextPrimary);
                max.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(max, 5); g.Children.Add(max);
                days.Children.Add(g);
            }
        }
    }
}
