using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Radios;

namespace FloatingDock
{
    /// <summary>Kendi hizli ayarlar panelimiz: Wi-Fi aglari (sifreyle baglanma), ses seviyesi.</summary>
    class ControlCenter : Flyout
    {
        // Segoe Fluent Icons
        const string GWifi = "", GWifi3 = "", GWifi2 = "", GWifi1 = "", GLock = "",
                     GVol0 = "", GVol1 = "", GVol2 = "", GVol3 = "", GMute = "",
                     GSettings = "", GRefresh = "", GCheck = "";

        readonly Border wifiTile, soundTile;
        readonly TextBlock wifiTileSub = Text("", 12, 190), soundTileSub = Text("", 12, 190);
        readonly TextBlock wifiTileIcon = Icon(GWifi, 18), soundTileIcon = Icon(GVol3, 18);
        readonly Toggle wifiToggle = new Toggle();
        readonly StackPanel netList = new StackPanel();
        readonly TextBlock listStatus = Text("", 12, 150);
        readonly GlowSlider volume = new GlowSlider();
        readonly TextBlock volIcon = Icon(GVol3, 17), volText = Text("", 12, 200), deviceText = Text("", 11.5, 140);
        readonly TextBlock refreshIcon = Icon(GRefresh, 13, 200);
        readonly DispatcherTimer poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

        List<WifiNetwork> nets = new List<WifiNetwork>();
        string expanded;          // acilmis (sifre/baglan) satirin SSID'si
        string busySsid;          // baglanilan ag
        string errorSsid, errorText;
        bool wifiOn = true, hasWifi;
        Radio radio;
        public string TestExpand; // sadece --panel testi icin

        public ControlCenter() : base("FloatingDockControlCenter")
        {
            var root = new StackPanel();

            // ---- ust: iki buyuk kutucuk ----
            var tiles = new Grid();
            tiles.ColumnDefinitions.Add(new ColumnDefinition());
            tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            tiles.ColumnDefinitions.Add(new ColumnDefinition());
            wifiTile = Tile(wifiTileIcon, "Wi-Fi", wifiTileSub, () => SetWifi(!wifiOn));
            soundTile = Tile(soundTileIcon, "Ses", soundTileSub, () => { Audio.Muted = !Audio.Muted; SyncAudio(); });
            Grid.SetColumn(soundTile, 2);
            tiles.Children.Add(wifiTile); tiles.Children.Add(soundTile);
            root.Children.Add(tiles);

            // ---- ses ----
            var volRow = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            volRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            volRow.ColumnDefinitions.Add(new ColumnDefinition());
            volRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            var muteBtn = Clickable(new Border { Width = 34, Height = 34, Child = volIcon }, () => { Audio.Muted = !Audio.Muted; SyncAudio(); }, 17, 0, 28);
            volRow.Children.Add(muteBtn);
            volume.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(volume, 1); volRow.Children.Add(volume);
            volText.HorizontalAlignment = HorizontalAlignment.Right;
            volText.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            Grid.SetColumn(volText, 2); volRow.Children.Add(volText);
            volume.ValueChanged += v => { Audio.Volume = (float)v; if (v > 0 && Audio.Muted) Audio.Muted = false; SyncAudio(false); };
            root.Children.Add(volRow);
            deviceText.Margin = new Thickness(42, -2, 0, 0);
            root.Children.Add(deviceText);

            // ---- Wi-Fi baslik ----
            var head = new Grid { Margin = new Thickness(2, 18, 2, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ht = new StackPanel { Orientation = Orientation.Horizontal };
            ht.Children.Add(HudTitle("Wi-Fi ağları"));
            listStatus.Margin = new Thickness(10, 0, 0, 0);
            listStatus.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            ht.Children.Add(listStatus);
            head.Children.Add(ht);
            refreshIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            refreshIcon.RenderTransform = new RotateTransform();
            var refresh = Clickable(new Border { Width = 28, Height = 28, Child = refreshIcon }, () => _ = ScanAsync(), 14, 0, 28);
            refresh.ToolTip = "Yeniden tara";
            refresh.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(refresh, 1); head.Children.Add(refresh);
            Grid.SetColumn(wifiToggle, 2); head.Children.Add(wifiToggle);
            wifiToggle.Changed += on => SetWifi(on);
            root.Children.Add(head);

            // ---- ag listesi ----
            var sv = new ScrollViewer
            {
                Content = netList,
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.VerticalOnly
            };
            root.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Child = sv
            });

            // ---- alt bilgi ----
            var foot = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var link = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 12, 6) };
            link.Children.Add(Icon(GSettings, 13, 210));
            link.Children.Add(new Border { Width = 8 });
            link.Children.Add(Text("Ağ ve internet ayarları", 12.5, 220));
            var linkBtn = Clickable(link, () => { Launch("ms-settings:network"); HideFlyout(); }, 8, 0, 26);
            linkBtn.HorizontalAlignment = HorizontalAlignment.Left;
            foot.Children.Add(linkBtn);
            var soundLink = Clickable(new Border { Padding = new Thickness(8, 6, 8, 6), Child = Icon("", 14, 210) }, () => { Launch("ms-settings:sound"); HideFlyout(); }, 8, 0, 26);
            soundLink.ToolTip = "Ses ayarları";
            Grid.SetColumn(soundLink, 1); foot.Children.Add(soundLink);
            root.Children.Add(foot);

            SetBody(root);
            poll.Tick += (s, e) => { SyncAudio(); _ = ReloadAsync(); };
        }

        Border Tile(TextBlock icon, string title, TextBlock sub, Action click)
        {
            var g = new Grid { Margin = new Thickness(14, 12, 14, 12) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var circle = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Child = icon, Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)) };
            g.Children.Add(circle);
            var t = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(Text(title, 13.5, 255, FontWeights.SemiBold));
            t.Children.Add(sub);
            Grid.SetColumn(t, 1); g.Children.Add(t);
            var b = Clickable(g, click, 14, 18, 34);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            return b;
        }

        static void SetTileActive(Border tile, bool active)
        {
            var circle = (Border)((Grid)tile.Child).Children[0];
            circle.Background = active ? new LinearGradientBrush(Color.FromRgb(0x6F, 0x8C, 0xFF), Color.FromRgb(0xA0, 0x6C, 0xF5), 45)
                                       : (Brush)new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            tile.BorderBrush = active ? Holo(170) : (Brush)new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            circle.Effect = active ? new System.Windows.Media.Effects.DropShadowEffect { Color = Accent, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.8 } : null;
        }

        // ---------------- acilis / kapanis ----------------
        protected override void OnOpening()
        {
            expanded = TestExpand; errorSsid = null;
            SyncAudio();
            deviceText.Text = Audio.DeviceName ?? "";
            deviceText.Visibility = string.IsNullOrEmpty(deviceText.Text) ? Visibility.Collapsed : Visibility.Visible;
            hasWifi = Wlan.HasAdapter;
            nets = hasWifi ? Wlan.List() : new List<WifiNetwork>();
            wifiOn = !hasWifi || nets.Count > 0 || wifiOn;
            RenderList();
            UpdateWifiTile();
            poll.Start();
            _ = InitRadioAsync();
            _ = ScanAsync();
        }

        protected override void OnClosing() => poll.Stop();

        static void Launch(string s) { try { Process.Start(new ProcessStartInfo(s) { UseShellExecute = true }); } catch { } }

        // ---------------- ses ----------------
        void SyncAudio(bool moveSlider = true)
        {
            float v = Audio.Volume;
            bool muted = Audio.Muted;
            if (v < 0)
            {
                soundTileSub.Text = "Ses cihazı yok"; volText.Text = "—"; volume.IsEnabled = false;
                SetTileActive(soundTile, false);
                return;
            }
            if (moveSlider && !volume.Dragging) volume.Value = v;
            int pct = (int)Math.Round(v * 100);
            string g = muted || pct == 0 ? GMute : pct < 34 ? GVol1 : pct < 67 ? GVol2 : GVol3;
            volIcon.Text = g; soundTileIcon.Text = g;
            volText.Text = pct + "%";
            soundTileSub.Text = muted ? "Sessiz" : "%" + pct;
            volume.Opacity = muted ? 0.45 : 1;
            SetTileActive(soundTile, !muted);
        }

        // ---------------- Wi-Fi ----------------
        async Task InitRadioAsync()
        {
            try
            {
                if (radio == null)
                {
                    if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return;
                    radio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.WiFi);
                }
                if (radio != null) { wifiOn = radio.State == RadioState.On; UpdateWifiTile(); if (!wifiOn) RenderList(); }
            }
            catch { }
        }

        async void SetWifi(bool on)
        {
            if (!hasWifi) { Launch("ms-settings:network"); return; }
            wifiOn = on; UpdateWifiTile();
            if (!on) { nets.Clear(); RenderList(); }
            bool ok = false;
            try
            {
                if (radio == null) await InitRadioAsync();
                if (radio != null) ok = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
            }
            catch { }
            if (!ok) { Launch("ms-settings:network-wifi"); return; } // izin yoksa Windows ayarlarini ac
            if (on) { listStatus.Text = "açılıyor…"; await Task.Delay(2500); await ScanAsync(); }
        }

        void UpdateWifiTile()
        {
            wifiToggle.IsOn = wifiOn && hasWifi;
            var cur = nets.FirstOrDefault(n => n.Connected);
            if (!hasWifi) wifiTileSub.Text = "Adaptör yok";
            else if (!wifiOn) wifiTileSub.Text = "Kapalı";
            else wifiTileSub.Text = cur?.Ssid ?? "Bağlı değil";
            wifiTileIcon.Text = cur != null ? SignalGlyph(cur.Signal) : GWifi;
            wifiTileIcon.Opacity = wifiOn ? 1 : 0.5;
            SetTileActive(wifiTile, wifiOn && hasWifi);
        }

        async Task ScanAsync()
        {
            if (!hasWifi || !wifiOn) return;
            Spin(true);
            await Task.Run(Wlan.Scan);
            await Task.Delay(2600);   // tarama sonuclari birkac saniyede gelir
            await ReloadAsync();
            Spin(false);
        }

        async Task ReloadAsync()
        {
            if (!hasWifi || !IsVisible) return;
            var list = await Task.Run(Wlan.List);
            if (list.Count > 0 && !wifiOn) wifiOn = true;
            nets = list;
            UpdateWifiTile();
            // kullanici sifre yazarken listeyi yeniden kurma
            if (expanded == null && busySsid == null) RenderList();
        }

        void Spin(bool on)
        {
            var rt = (RotateTransform)refreshIcon.RenderTransform;
            if (on) rt.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            else rt.BeginAnimation(RotateTransform.AngleProperty, null);
            listStatus.Text = on ? "taranıyor…" : "";
        }

        static string SignalGlyph(int s) => s >= 75 ? GWifi : s >= 50 ? GWifi3 : s >= 25 ? GWifi2 : GWifi1;

        void RenderList()
        {
            netList.Children.Clear();
            if (!hasWifi) { netList.Children.Add(Empty("Bu bilgisayarda Wi-Fi adaptörü bulunamadı.")); return; }
            if (!wifiOn) { netList.Children.Add(Empty("Wi-Fi kapalı. Açmak için anahtarı kullan.")); return; }
            if (nets.Count == 0) { netList.Children.Add(Empty("Ağ aranıyor…")); return; }
            foreach (var n in nets) netList.Children.Add(Row(n));
        }

        static UIElement Empty(string t)
        {
            var tb = Text(t, 12.5, 160);
            tb.TextWrapping = TextWrapping.Wrap;
            tb.Margin = new Thickness(12, 14, 12, 14);
            return tb;
        }

        UIElement Row(WifiNetwork n)
        {
            bool isOpen = expanded == n.Ssid;
            var stack = new StackPanel();

            var g = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sig = new Grid();
            sig.Children.Add(Icon(GWifi, 16, 50));                 // soluk tam cubuk
            sig.Children.Add(Icon(SignalGlyph(n.Signal), 16));    // gercek seviye
            if (n.Secured)
            {
                var l = Icon(GLock, 8.5);
                l.HorizontalAlignment = HorizontalAlignment.Right; l.VerticalAlignment = VerticalAlignment.Bottom;
                l.Margin = new Thickness(0, 0, 2, 0);
                sig.Children.Add(l);
            }
            g.Children.Add(sig);

            var txt = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            txt.Children.Add(Text(n.Ssid, 13.5, 255, n.Connected ? FontWeights.SemiBold : FontWeights.Normal));
            string sub = n.Connected ? "Bağlı" + (n.Secured ? ", güvenli" : "")
                       : busySsid == n.Ssid ? "Bağlanıyor…"
                       : errorSsid == n.Ssid ? errorText
                       : n.HasProfile ? "Kayıtlı" + (n.Secured ? ", güvenli" : "")
                       : n.Secured ? "Güvenli" : "Açık ağ";
            var subTb = Text(sub, 11.5, 170);
            if (errorSsid == n.Ssid && !n.Connected) subTb.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0xA8));
            if (n.Connected) subTb.Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xE8, 0xC8));
            txt.Children.Add(subTb);
            Grid.SetColumn(txt, 1); g.Children.Add(txt);

            if (n.Connected)
            {
                var ok = Icon(GCheck, 13); ok.Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xE8, 0xC8));
                Grid.SetColumn(ok, 2); g.Children.Add(ok);
            }
            else
            {
                var pct = Text(n.Signal + "%", 11, 120);
                pct.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                Grid.SetColumn(pct, 2); g.Children.Add(pct);
            }
            stack.Children.Add(g);

            if (isOpen) stack.Children.Add(Details(n));

            var row = Clickable(stack, () =>
            {
                if (busySsid != null) return;
                expanded = isOpen ? null : n.Ssid;
                errorSsid = isOpen ? errorSsid : null;
                RenderList();
                FocusPassword();
            }, 10, (byte)(isOpen || n.Connected ? 22 : 0), 26);
            row.Margin = new Thickness(0, 1, 0, 1);
            if (isOpen) { row.BorderThickness = new Thickness(1); row.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0x8A, 0x7C, 0xFF)); }
            return row;
        }

        PasswordBox pwBox;

        UIElement Details(WifiNetwork n)
        {
            var p = new StackPanel { Margin = new Thickness(46, 0, 12, 12) };
            // satira tiklanmis gibi davranmasin
            p.MouseLeftButtonUp += (s, e) => e.Handled = true;
            p.MouseLeftButtonDown += (s, e) => e.Handled = true;
            pwBox = null;

            if (n.Connected)
            {
                var b = new WrapPanel();
                b.Children.Add(SecondaryButton("Bağlantıyı kes", () => { Wlan.Disconnect(); expanded = null; _ = Later(1200); }));
                p.Children.Add(b);
                return p;
            }
            if (!n.Supported)
            {
                var t = Text("Bu ağ kurumsal oturum açma istiyor; Windows ayarlarından bağlan.", 12, 180);
                t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 8);
                p.Children.Add(t);
                p.Children.Add(SecondaryButton("Ayarları aç", () => { Launch("ms-settings:network-wifi"); HideFlyout(); }));
                return p;
            }
            bool needPw = n.Secured && !n.HasProfile;
            if (needPw)
            {
                pwBox = new PasswordBox
                {
                    FontSize = 13.5,
                    FontFamily = UiFont,
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(2, 6, 2, 6),
                    SelectionBrush = new SolidColorBrush(Accent)
                };
                var hint = Text("Ağ güvenlik anahtarı", 13, 110);
                hint.IsHitTestVisible = false;
                hint.Margin = new Thickness(4, 0, 0, 0);
                pwBox.PasswordChanged += (s, e) => hint.Visibility = pwBox.Password.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
                pwBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) DoConnect(n); };
                var box = new Grid();
                box.Children.Add(pwBox); box.Children.Add(hint);
                p.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                    BorderBrush = new LinearGradientBrush(Color.FromRgb(0x6F, 0xD6, 0xFF), Color.FromRgb(0xA0, 0x6C, 0xF5), 0),
                    BorderThickness = new Thickness(0, 0, 0, 2),
                    Padding = new Thickness(8, 0, 8, 0),
                    Margin = new Thickness(0, 0, 0, 10),
                    Child = box
                });
            }
            var btns = new WrapPanel();
            btns.Children.Add(PrimaryButton("Bağlan", () => DoConnect(n)));
            if (n.HasProfile)
            {
                var f = SecondaryButton("Unut", () => { Wlan.Forget(n); expanded = null; _ = Later(600); });
                f.Margin = new Thickness(8, 0, 0, 0);
                btns.Children.Add(f);
            }
            p.Children.Add(btns);
            return p;
        }

        void FocusPassword()
        {
            if (pwBox == null) return;
            Dispatcher.BeginInvoke(new Action(() => { Activate(); pwBox?.Focus(); Keyboard.Focus(pwBox); }), DispatcherPriority.Input);
        }

        async void DoConnect(WifiNetwork n)
        {
            if (busySsid != null) return;
            string pw = pwBox?.Password;
            if (n.Secured && !n.HasProfile && (pw == null || pw.Length < 8))
            {
                errorSsid = n.Ssid; errorText = "Şifre en az 8 karakter olmalı";
                RenderList(); FocusPassword();
                return;
            }
            busySsid = n.Ssid; errorSsid = null; expanded = null;
            RenderList();
            bool ok = await Wlan.ConnectAsync(n, pw);
            busySsid = null;
            if (!ok)
            {
                errorSsid = n.Ssid;
                errorText = n.HasProfile ? "Bağlanılamadı" : "Bağlanılamadı — şifre yanlış olabilir";
                expanded = n.HasProfile ? null : n.Ssid;
            }
            nets = await Task.Run(Wlan.List);
            UpdateWifiTile();
            RenderList();
            if (expanded != null) FocusPassword();
        }

        async Task Later(int ms)
        {
            await Task.Delay(ms);
            nets = await Task.Run(Wlan.List);
            UpdateWifiTile();
            RenderList();
        }
    }
}
