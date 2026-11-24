using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace FloatingDock
{
    /// <summary>
    /// Kisisellestirme Merkezi'nin sagindaki canli onizleme: duvar kagidi, ust cubuk, ada, masaustu bilesenleri, dock ve
    /// (Baslat kategorisinde) Baslat menusu. Kaydedilmis ayarlardan degil, merkezde o an secilenlerden cizilir;
    /// tema / palet / vurgu degisikligi uygulama yeniden baslamadan burada gorunur.
    /// </summary>
    class LivePreview : Border
    {
        const double W = 1280, H = 720;
        readonly Grid canvas = new Grid { Width = W, Height = H, ClipToBounds = true, Background = Brushes.Black };
        /// <summary>"desktop" | "dock" | "start" | "anim" | "phone" | "widgets" | "colors"</summary>
        public string Mode = "desktop";
        /// <summary>Galeride secili (henuz uygulanmamis) duvar kagidi.</summary>
        public string Wallpaper;
        FrameworkElement startPanel, startShade;

        static ImageSource windowsWallpaper;
        static bool windowsWallpaperLoaded;
        static string primaryMonitor;

        public LivePreview()
        {
            CornerRadius = new CornerRadius(18);
            canvas.Clip = new RectangleGeometry(new Rect(0, 0, W, H), 30, 30);
            Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Direction = 270, Opacity = Theme.Dark ? 0.5 : 0.18, Color = Theme.Shadow };
        }

        public static string PrimaryMonitor => primaryMonitor ??= Wallpapers.Monitors().FirstOrDefault()?.Id ?? "";

        /// <summary>Su anki Windows duvar kagidi (Winter secilmemisse onizlemede o gorunur).</summary>
        static ImageSource WindowsWallpaper()
        {
            if (windowsWallpaperLoaded) return windowsWallpaper;
            windowsWallpaperLoaded = true;
            try
            {
                var p = Wallpapers.WindowsWallpaper(PrimaryMonitor);
                if (!string.IsNullOrEmpty(p)) windowsWallpaper = Wallpapers.LoadImage(p, 640);
            }
            catch (Exception ex) { Program.Log(ex); }
            return windowsWallpaper;
        }

        static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
        static SolidColorBrush B(Color c) => new SolidColorBrush(c);

        static TextBlock T(string s, double size, Color c, FontWeight w) => new TextBlock
        {
            Text = s, FontSize = size, Foreground = B(c), FontWeight = w, FontFamily = Theme.Ui, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        static TextBlock G(string glyph, double size, Color c) => new TextBlock
        {
            Text = glyph, FontFamily = Theme.Icons, FontSize = size, Foreground = B(c),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };

        static DropShadowEffect Shadow(bool dark, double blur = 24, double depth = 8) =>
            new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = dark ? 0.5 : 0.2, Color = dark ? Colors.Black : Color.FromRgb(0x0F, 0x3D, 0x47) };

        public void Rebuild()
        {
            var s = Settings.Current;
            var (dark, bg, card, ink, accent, onAccent, pastels) = Theme.PreviewColors(s.Theme, s.Palette, s.Accent);
            canvas.Children.Clear();
            startPanel = startShade = null;

            // ---- duvar kagidi ----
            var id = Wallpaper ?? Wallpapers.Selected(PrimaryMonitor);
            var img = id != null ? Wallpapers.Thumbnail(id, 640) : WindowsWallpaper();
            if (img == null) img = Wallpapers.Thumbnail("winter-night", 640);
            var fit = s.WallpaperFit;
            var wall = new Image
            {
                Source = img, Stretch = fit == "fit" ? Stretch.Uniform : fit == "center" ? Stretch.Uniform : Stretch.UniformToFill,
                Width = fit == "center" ? W * 0.62 : W, Height = fit == "center" ? H * 0.62 : H,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(wall, BitmapScalingMode.HighQuality);
            canvas.Children.Add(wall);

            // ---- ust cubuk: sol ve sag ada ----
            Color barFill = A(card, 246), ink2 = A(ink, (byte)(dark ? 205 : 210)), ink3 = A(ink, (byte)(dark ? 165 : 182));
            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
            var startDot = new Grid { Width = 30, Height = 30 };
            startDot.Children.Add(new Border { CornerRadius = new CornerRadius(15), Background = B(accent) });
            startDot.Children.Add(s.StartLogo == "classic" ? (UIElement)G("", 13, onAccent) : WinterLogo.Create(17, B(onAccent)));
            left.Children.Add(startDot);
            var app = T("Dosya Gezgini", 14, ink, Theme.Heavy); app.Margin = new Thickness(10, 0, 0, 0); left.Children.Add(app);
            var sub = T("· 2", 13, ink3, Theme.Bold); sub.Margin = new Thickness(6, 0, 8, 0); left.Children.Add(sub);
            canvas.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(21), Background = B(barFill), Child = left, Effect = Shadow(dark, 16, 4),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 12, 0, 0)
            });
            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 6, 14, 6) };
            var sky = new Border { CornerRadius = new CornerRadius(15), Background = B(pastels[1]), Padding = new Thickness(10, 0, 10, 0), Height = 30 };
            sky.Child = T("12°", 13, dark ? Colors.White : Color.FromRgb(0x0B, 0x4A, 0x6F), Theme.Heavy);
            right.Children.Add(sky);
            var wf = G("", 14, ink); wf.Margin = new Thickness(14, 0, 0, 0); right.Children.Add(wf);
            var vl = G("", 14, ink); vl.Margin = new Thickness(10, 0, 0, 0); right.Children.Add(vl);
            var clock = T("21:47", 14, ink, Theme.Heavy); clock.Margin = new Thickness(14, 0, 0, 0); right.Children.Add(clock);
            var date = T("24 Kas", 13, ink3, Theme.Bold); date.Margin = new Thickness(6, 0, 0, 0); right.Children.Add(date);
            canvas.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(21), Background = B(barFill), Child = right, Effect = Shadow(dark, 16, 4),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 14, 0)
            });

            // ---- ada ----
            var isl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 8, 18, 8) };
            if (Mode == "phone")
            {
                var ph = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(10), Background = B(Color.FromRgb(0x1E, 0x6F, 0xD9)), Child = G("", 15, Colors.White) };
                isl.Children.Add(ph);
                var tx = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
                tx.Children.Add(T(s.PhoneIsland ? "Telefon Bağlantısı · Ayşe" : "Telefon bildirimleri adada gizli", 13, Colors.White, Theme.Heavy));
                tx.Children.Add(T(s.PhoneIsland ? "Akşam görüşelim mi?" : "Takvim panelindeki geçmişte birikir", 12, Color.FromRgb(0xC9, 0xCC, 0xD6), Theme.Bold));
                isl.Children.Add(tx);
            }
            else
            {
                for (int i = 0; i < 4; i++) isl.Children.Add(new Border { Width = 4, Height = 8 + (i * 7 % 13), Margin = new Thickness(2, 0, 2, 0), CornerRadius = new CornerRadius(2), Background = B(accent), VerticalAlignment = VerticalAlignment.Center });
                var song = T("Dream Bigger", 13, Colors.White, Theme.Heavy); song.Margin = new Thickness(10, 0, 0, 0);
                isl.Children.Add(song);
            }
            var island = new Border
            {
                CornerRadius = new CornerRadius(22), Background = B(Color.FromRgb(0x12, 0x12, 0x16)), Child = isl, Effect = Shadow(true, 18, 4),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 0, 0)
            };
            canvas.Children.Add(island);
            if (Mode == "phone") Ring(island, accent);

            // ---- masaustu bilesenleri ----
            if (s.DesktopWidgets && (s.WidgetWeather || s.WidgetMusic || s.WidgetToday))
            {
                var stack = new StackPanel
                {
                    Width = 290, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(28, 78, 28, 0),
                    HorizontalAlignment = s.WidgetSide == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right
                };
                Border Card(UIElement c, Color fill) => new Border { CornerRadius = new CornerRadius(26), Background = B(fill), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14), Child = c, Effect = Shadow(dark, 26, 8) };
                Color skyInk = dark ? Colors.White : Color.FromRgb(0x0B, 0x4A, 0x6F);
                if (s.WidgetWeather)
                {
                    var g = new StackPanel();
                    g.Children.Add(T("İstanbul", 12, skyInk, Theme.Bold));
                    g.Children.Add(T("12°", 44, skyInk, Theme.Heavy));
                    g.Children.Add(T("Parçalı bulutlu · ↑14° ↓8°", 12.5, skyInk, Theme.Bold));
                    stack.Children.Add(Card(g, pastels[1]));
                }
                if (s.WidgetMusic)
                {
                    var g = new StackPanel { Orientation = Orientation.Horizontal };
                    g.Children.Add(new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(16), Background = new LinearGradientBrush(Color.FromRgb(0xF4, 0x72, 0xB6), accent, 45) });
                    var tx = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    tx.Children.Add(T("Dream Bigger", 15, ink, Theme.Heavy));
                    tx.Children.Add(T("Winter Developer", 12.5, ink2, Theme.Bold));
                    var bar = new Grid { Height = 5, Width = 150, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                    bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = B(A(ink, 40)) });
                    bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = B(accent), Width = 60, HorizontalAlignment = HorizontalAlignment.Left });
                    tx.Children.Add(bar);
                    g.Children.Add(tx);
                    stack.Children.Add(Card(g, A(card, 250)));
                }
                if (s.WidgetToday)
                {
                    var g = new StackPanel();
                    g.Children.Add(T("Bugün", 14, ink, Theme.Heavy));
                    var b = T("Salı, 24 Kasım\n2 okunmamış bildirim", 12.5, ink2, Theme.Bold); b.Margin = new Thickness(0, 6, 0, 0); b.TextWrapping = TextWrapping.Wrap;
                    g.Children.Add(b);
                    stack.Children.Add(Card(g, A(card, 250)));
                }
                canvas.Children.Add(stack);
                if (Mode == "widgets") Ring(stack, accent);
            }

            // ---- dock ----
            double item = s.DockSize == "small" ? 36 : s.DockSize == "large" ? 52 : 44, tile = item + 14;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 8) };
            Color raised = dark ? Color.FromRgb((byte)Math.Min(255, card.R + 15), (byte)Math.Min(255, card.G + 15), (byte)Math.Min(255, card.B + 15)) : Colors.White;
            var glyphs = new[] { "", "", "", "", "", "" };
            var tiles = new List<FrameworkElement>();
            FrameworkElement Tile(Color fill, UIElement content, bool running)
            {
                var g = new Grid { Width = tile, Margin = new Thickness(3, 0, 3, 0) };
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(tile) });
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
                g.Children.Add(new Border { CornerRadius = new CornerRadius(tile * 0.32), Background = B(fill), Child = content, Effect = Shadow(dark, 10, 3) });
                if (running)
                {
                    var dot = new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = B(accent), VerticalAlignment = VerticalAlignment.Bottom };
                    Grid.SetRow(dot, 1); g.Children.Add(dot);
                }
                tiles.Add(g);
                return g;
            }
            var all = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Width = item * 0.5, Height = item * 0.5 };
            for (int i = 0; i < 4; i++) all.Children.Add(new Border { CornerRadius = new CornerRadius(3), Margin = new Thickness(1.5), Background = B(i % 2 == 0 ? accent : A(ink, 120)) });
            row.Children.Add(Tile(raised, all, false));
            row.Children.Add(new Border { Width = 1, Height = tile * 0.6, Margin = new Thickness(6, 0, 6, 8), Background = B(A(ink, 40)), VerticalAlignment = VerticalAlignment.Center });
            for (int i = 0; i < glyphs.Length; i++)
            {
                var fill = s.DockTiles == "white" ? (dark ? raised : Colors.White) : pastels[i % pastels.Length];
                var gc = s.DockTiles == "white" ? accent : (dark ? Colors.White : A(ink, 220));
                row.Children.Add(Tile(fill, G(glyphs[i], item * 0.48, gc), i < 3));
            }
            row.Children.Add(new Border { Width = 1, Height = tile * 0.6, Margin = new Thickness(6, 0, 6, 8), Background = B(A(ink, 40)), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(Tile(raised, G("", item * 0.45, ink), false));
            if (s.Magnify)
            {
                // fare ucuncu uygulamanin uzerinde: komsulariyla birlikte buyur
                double mag = Math.Clamp(s.MagnifyAmount, 0.05, 0.7);
                int c = 4;
                for (int i = 0; i < tiles.Count; i++)
                {
                    double d = Math.Abs(i - c), k = 1 + mag * Math.Exp(-(d * d) / 2.2);
                    tiles[i].RenderTransformOrigin = new Point(0.5, 1);
                    tiles[i].RenderTransform = new ScaleTransform(k, k);
                }
            }
            var dock = new Border
            {
                CornerRadius = new CornerRadius(tile / 2 + 12), Background = B(A(card, 246)), Child = row, Effect = Shadow(dark, 30, 10),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, s.AutoHide && Mode == "dock" ? 14 : 18)
            };
            canvas.Children.Add(dock);
            if (Mode == "dock") Ring(dock, accent);

            // ---- Baslat menusu ----
            if (Mode == "start" || Mode == "anim") BuildStart(s, dark, bg, card, ink, accent, onAccent, pastels, img);
        }

        /// <summary>Onizlemede ilgili parcayi kesik cizgili vurgu cercevesiyle goster.</summary>
        void Ring(FrameworkElement target, Color accent)
        {
            var r = new Border
            {
                BorderBrush = B(A(accent, 230)), BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(30), IsHitTestVisible = false,
                HorizontalAlignment = target.HorizontalAlignment, VerticalAlignment = target.VerticalAlignment
            };
            target.SizeChanged += (s, e) =>
            {
                r.Width = e.NewSize.Width + 20; r.Height = e.NewSize.Height + 20;
                var m = target.Margin;
                r.Margin = new Thickness(Math.Max(0, m.Left - 10), Math.Max(0, m.Top - 10), Math.Max(0, m.Right - 10), Math.Max(0, m.Bottom - 10));
            };
            canvas.Children.Add(r);
        }

        void BuildStart(Settings s, bool dark, Color bg, Color card, Color ink, Color accent, Color onAccent, Color[] pastels, ImageSource wallImg)
        {
            var tint = StartMenu.TintColor(s.StartTint, dark, bg, accent);
            byte alpha = (byte)Math.Round(255 * Math.Clamp(s.StartOpacity, 30, 100) / 100.0);
            bool full = s.StartWidth >= 100 && s.StartHeight >= 100;
            Color ink2 = A(ink, (byte)(dark ? 205 : 210)), ink3 = A(ink, (byte)(dark ? 165 : 182));

            // arka plan: bulaniklik (akrilik) + ton
            var shade = new Grid();
            if (s.StartBlur && wallImg != null)
                shade.Children.Add(new Image { Source = wallImg, Stretch = Stretch.UniformToFill, Effect = new BlurEffect { Radius = 26 }, Margin = new Thickness(-30) });
            shade.Children.Add(new Border { Background = full ? B(A(tint, alpha)) : B(Color.FromArgb(dark ? (byte)70 : (byte)40, 0, 0, 0)) });
            canvas.Children.Add(shade);
            startShade = shade;

            double pw = full ? W : Math.Max(380, (W - 32) * s.StartWidth / 100.0);
            double ph = full ? H : Math.Max(300, Math.Min(H - 136, H * s.StartHeight / 100.0));
            var panel = new Border
            {
                Width = pw, Height = ph,
                Background = full ? null : B(A(tint, Math.Max(alpha, (byte)200))),
                CornerRadius = new CornerRadius(full ? 0 : Math.Clamp(s.StartRadius, 0, 48)),
                Effect = full ? null : Shadow(dark, 40, 12),
                HorizontalAlignment = full ? HorizontalAlignment.Stretch : s.StartPosition == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                VerticalAlignment = full ? VerticalAlignment.Stretch : s.StartPosition == "center" ? VerticalAlignment.Center : VerticalAlignment.Bottom,
                Margin = full ? new Thickness(0) : new Thickness(16, 56, 16, 80)
            };
            var shell = new Grid();
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            shell.ColumnDefinitions.Add(new ColumnDefinition());
            if (s.StartSidebar)
            {
                var rail = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(full ? 22 : 12, 0, 4, 0) };
                var lg = WinterLogo.Create(22, B(accent)); lg.Margin = new Thickness(0, 0, 0, 12); rail.Children.Add(lg);
                foreach (var gl in new[] { "", "", "", "", "", "", "", "", "", "" })
                {
                    var b = new Border { Width = 46, Height = 38, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 1, 0, 1), Child = G(gl, 15, ink2) };
                    if (gl == "") b.Background = B(A(ink, (byte)(dark ? 38 : 34)));
                    rail.Children.Add(b);
                }
                shell.Children.Add(rail);
            }
            var content = new Grid { Margin = full ? new Thickness(s.StartSidebar ? 30 : 80, 40, 80, 26) : new Thickness(s.StartSidebar ? 12 : 28, 22, 28, 16) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(content, 1);
            shell.Children.Add(content);

            var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            if (s.StartShowClock)
            {
                var c = T("21:47", full ? 46 : 36, ink, Theme.Heavy); c.HorizontalAlignment = HorizontalAlignment.Center; head.Children.Add(c);
                var gr = T("İyi akşamlar · Salı 24 Kasım", 13, ink2, Theme.Bold); gr.HorizontalAlignment = HorizontalAlignment.Center; head.Children.Add(gr);
            }
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 0) };
            sp.Children.Add(G("", 14, accent));
            var ph0 = T("Yazmaya başla…", 13.5, ink3, Theme.Bold); ph0.Margin = new Thickness(10, 0, 0, 0); sp.Children.Add(ph0);
            head.Children.Add(new Border
            {
                Width = Math.Min(420, pw - 120), Height = 40, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 14, 0, 0),
                Background = B(dark ? bg : Colors.White), Child = sp, Effect = Shadow(dark, 12, 3)
            });
            content.Children.Add(head);

            // sabitlenenler: alana gore sutun / satir
            double availW = pw - (s.StartSidebar ? 80 : 0) - (full ? 160 : 60);
            double availH = ph - (s.StartShowClock ? (full ? 120 : 100) : 60) - 70 - (full ? 66 : 38);
            int cols = (int)Math.Clamp(Math.Floor(availW / 92), 3, 6), rows = (int)Math.Clamp(Math.Floor(availH / 96), 1, 3);
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = cols, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var names = new[] { "Edge", "Gezgin", "Ayarlar", "Store", "Fotoğraf", "Spotify", "Discord", "Steam", "Code", "Not Defteri", "Terminal", "Paint", "WhatsApp", "Word", "Excel", "Chrome", "Saat", "Hesap" };
            var gls = new[] { "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" };
            for (int i = 0; i < cols * rows; i++)
            {
                var t = new StackPanel { Margin = new Thickness(8, 4, 8, 6) };
                t.Children.Add(new Border
                {
                    Width = 58, Height = 58, CornerRadius = new CornerRadius(19), Background = B(pastels[i % pastels.Length]),
                    Child = G(gls[i % gls.Length], 24, dark ? Colors.White : A(ink, 220)), Effect = Shadow(dark, 12, 4)
                });
                var l = T(names[i % names.Length], 11.5, ink, Theme.Bold); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 6, 0, 0);
                t.Children.Add(l);
                grid.Children.Add(t);
            }
            Grid.SetRow(grid, 1);
            content.Children.Add(grid);

            var foot = new Grid();
            var who = new StackPanel { Orientation = Orientation.Horizontal };
            who.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = B(accent), Child = T("K", 13, onAccent, Theme.Heavy) });
            ((TextBlock)((Border)who.Children[0]).Child).HorizontalAlignment = HorizontalAlignment.Center;
            var un = T("Kullanıcı", 13, ink, Theme.Bold); un.Margin = new Thickness(8, 0, 0, 0); who.Children.Add(un);
            foot.Children.Add(who);
            var pr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var map = new Dictionary<string, string> { ["lock"] = "", ["sleep"] = "", ["hibernate"] = "", ["signout"] = "", ["restart"] = "", ["shutdown"] = "" };
            pr.Children.Add(new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Margin = new Thickness(6, 0, 0, 0), Background = B(A(ink, 16)), Child = G("", 13, ink) });
            foreach (var k in new[] { "lock", "sleep", "hibernate", "signout", "restart", "shutdown" }.Where(k => s.PowerButtons.Contains(k)))
                pr.Children.Add(new Border
                {
                    Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Margin = new Thickness(6, 0, 0, 0),
                    Background = B(k == "shutdown" ? pastels[5] : A(ink, 16)), Child = G(map[k], 13, ink)
                });
            foot.Children.Add(pr);
            Grid.SetRow(foot, 2);
            content.Children.Add(foot);

            panel.Child = shell;
            canvas.Children.Add(panel);
            startPanel = panel;
        }

        /// <summary>Baslat acilis animasyonunu onizlemede oynat (secili hareket ve hizla).</summary>
        public void Play()
        {
            if (startPanel == null) return;
            var s = Settings.Current;
            double ms = 280 * Math.Clamp(s.AnimSpeed, 0.4, 2);
            bool on = s.Animations && s.StartAnimation != "none";
            var d = new Duration(TimeSpan.FromMilliseconds(on ? ms : 0));
            var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
            startPanel.BeginAnimation(OpacityProperty, null);
            startShade.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0 : 1, 1, new Duration(TimeSpan.FromMilliseconds(on ? ms * 0.7 : 0))));
            if (!on) { startPanel.RenderTransform = null; return; }
            switch (s.StartAnimation)
            {
                case "slide":
                    var tt = new TranslateTransform(0, 70); startPanel.RenderTransform = tt;
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(70, 0, d) { EasingFunction = ease });
                    startPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d));
                    break;
                case "fade":
                    startPanel.RenderTransform = null;
                    startPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d));
                    break;
                default:
                    var st = new ScaleTransform(1.08, 1.08); startPanel.RenderTransformOrigin = new Point(0.5, 0.5); startPanel.RenderTransform = st;
                    var a = new DoubleAnimation(1.08, 1, d) { EasingFunction = ease };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                    startPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d));
                    break;
            }
        }
    }
}
