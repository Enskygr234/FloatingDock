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

        const string GBluetooth = "\uE702", GQuiet = "\uE708", GNight = "\uF08C", GBright = "\uE706", GApps = "\uE713",
                     GPrev = "\uE892", GNext = "\uE893", GPlay = "\uE768", GPause = "\uE769", GMusic = "\uE8D6", GPlug = "\uE83E";

        readonly QuickTile wifiTile, soundTile, btTile, dndTile;
        readonly TextBlock wifiTileSub = TileLabel(), soundTileSub = TileLabel(), btSub = TileLabel("Bluetooth");
        readonly TextBlock wifiTileIcon = Icon(GWifi, 16), soundTileIcon = Icon(GVol3, 16), btIcon = Icon(GBluetooth, 16);
        readonly GlowSlider bright = new GlowSlider("Parlaklık");
        readonly TextBlock brightText = Text("", Theme.Body, Theme.TextSecondary);
        readonly Grid brightRow = new Grid { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        readonly DispatcherTimer brightApply = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        readonly TextBlock batteryText = Text("", Theme.Body, Theme.TextSecondary), batteryIcon = Icon("", 14, Theme.TextSecondary);
        Border batteryBtn;
        // medya karti
        readonly Border mediaCard;
        readonly Border mediaArt = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(Theme.RadiusMd), ClipToBounds = true };
        readonly TextBlock mediaTitle = Text("", Theme.Body, Theme.TextPrimary, FontWeights.SemiBold), mediaArtist = Text("", Theme.Caption, Theme.TextSecondary);
        readonly TextBlock mediaPlayIcon = Icon(GPlay, 14);
        Radio btRadio;
        /// <summary>Dock baglar: FloatingDock ayarlar panelini acar.</summary>
        public Action OpenSettings;
        readonly Toggle wifiToggle = new Toggle();
        readonly StackPanel netList = new StackPanel();
        readonly TextBlock listStatus = Text("", Theme.Caption, Theme.TextTertiary);
        readonly GlowSlider volume = new GlowSlider();
        readonly TextBlock volIcon = Icon(GVol3, 17), volText = Text("", Theme.Body, Theme.TextSecondary), deviceText = Text("", Theme.Caption, Theme.TextTertiary);
        readonly TextBlock refreshIcon = Icon(GRefresh, 13, Theme.TextSecondary);
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

            // ---- ust: Win11 tarzi hizli ayar kutucuklari (3 x 2) ----
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(-4, -4, -4, 0) };
            wifiTile = new QuickTile(wifiTileIcon, wifiTileSub, "Wi-Fi", () => SetWifi(!wifiOn));
            btTile = new QuickTile(btIcon, btSub, "Bluetooth", () => _ = ToggleBluetooth());
            dndTile = new QuickTile(Icon(GQuiet, 16), TileLabel("Rahatsız etme"), "Rahatsız etme", () =>
            {
                Settings.Current.DoNotDisturb = !Settings.Current.DoNotDisturb; Settings.Current.Save(); dndTile.Set(Settings.Current.DoNotDisturb);
            });
            soundTile = new QuickTile(soundTileIcon, soundTileSub, "Sessize al", () => { Audio.Muted = !Audio.Muted; SyncAudio(); });
            var night = new QuickTile(Icon(GNight, 16), TileLabel("Gece ışığı"), "Gece ışığı ayarları", () => { Launch("ms-settings:nightlight"); HideFlyout(); });
            var appSet = new QuickTile(Icon(GApps, 16), TileLabel("Ayarlar"), "FloatingDock ayarları", () => { HideFlyout(); OpenSettings?.Invoke(); });
            var snip = new QuickTile(Icon("\uE8C6", 16), TileLabel("Ekran alıntısı"), "Ekran alıntısı (Win+Shift+S)", () => { HideFlyout(); Later(() => Launch("ms-screenclip:")); });
            var cast = new QuickTile(Icon("\uEC15", 16), TileLabel("Yansıt"), "Kablosuz ekrana yansıt (Win+K)", () => { HideFlyout(); Later(() => Keys(0x5B, 0x4B)); });
            var access = new QuickTile(Icon("\uE776", 16), TileLabel("Erişilebilirlik"), "Erişilebilirlik ayarları", () => { Launch("ms-settings:easeofaccess"); HideFlyout(); });
            foreach (var t in new[] { wifiTile, btTile, dndTile, soundTile, night, snip, cast, access, appSet }) tiles.Children.Add(t.Root);
            root.Children.Add(tiles);

            // ---- ses ----
            var volRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            volRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            volRow.ColumnDefinitions.Add(new ColumnDefinition());
            volRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            var muteBtn = Clickable(new Border { Width = 34, Height = 34, Child = volIcon }, () => { Audio.Muted = !Audio.Muted; SyncAudio(); });
            muteBtn.ToolTip = "Sessize al / aç";
            System.Windows.Automation.AutomationProperties.SetName(muteBtn, "Sessize al");
            volRow.Children.Add(muteBtn);
            volume.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(volume, 1); volRow.Children.Add(volume);
            volText.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(volText, 2); volRow.Children.Add(volText);
            volume.ValueChanged += v => { Audio.Volume = (float)v; if (v > 0 && Audio.Muted) Audio.Muted = false; SyncAudio(false); };
            root.Children.Add(volRow);
            deviceText.Margin = new Thickness(44, -2, 0, 0);
            root.Children.Add(deviceText);

            // ---- parlaklik (destekleyen ekranlarda) ----
            brightRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            brightRow.ColumnDefinitions.Add(new ColumnDefinition());
            brightRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            brightRow.Children.Add(new Border { Width = 34, Height = 34, Child = Icon(GBright, 16) });
            bright.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(bright, 1); brightRow.Children.Add(bright);
            brightText.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(brightText, 2); brightRow.Children.Add(brightText);
            bright.ValueChanged += v => { brightText.Text = "%" + (int)Math.Round(v * 100); brightApply.Stop(); brightApply.Start(); };
            brightApply.Tick += (s, e) => { brightApply.Stop(); int v = (int)Math.Round(bright.Value * 100); Task.Run(() => Brightness.Set(v)); };
            root.Children.Add(brightRow);

            // ---- su an calan (medya) ----
            var mg = new Grid { Margin = new Thickness(10) };
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mg.ColumnDefinitions.Add(new ColumnDefinition());
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mg.Children.Add(mediaArt);
            var mt = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            mt.Children.Add(mediaTitle); mt.Children.Add(mediaArtist);
            Grid.SetColumn(mt, 1); mg.Children.Add(mt);
            // kapak / sarki adi: buyuk oynatici
            foreach (var el in new FrameworkElement[] { mediaArt, mt })
            {
                el.Cursor = System.Windows.Input.Cursors.Hand;
                el.ToolTip = "Oynatıcıyı aç";
                el.MouseLeftButtonUp += (s, e) => { e.Handled = true; HideFlyout(); Hub.OpenPlayer?.Invoke(); };
            }
            var mb = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            mb.Children.Add(IconButton(GPrev, "Önceki", () => Hub.Media.Prev(), 32, 12));
            var play = Clickable(new Border { Width = 36, Height = 36, Child = mediaPlayIcon }, () => Hub.Media.PlayPause(), 18);
            play.ToolTip = "Oynat / duraklat";
            System.Windows.Automation.AutomationProperties.SetName(play, "Oynat / duraklat");
            play.Margin = new Thickness(2, 0, 2, 0);
            mb.Children.Add(play);
            mb.Children.Add(IconButton(GNext, "Sonraki", () => Hub.Media.Next(), 32, 12));
            Grid.SetColumn(mb, 2); mg.Children.Add(mb);
            mediaCard = Card(mg, 0);
            mediaCard.Margin = new Thickness(0, 12, 0, 0);
            mediaCard.Visibility = Visibility.Collapsed;
            root.Children.Add(mediaCard);
            Hub.MediaChanged += m => Dispatcher.BeginInvoke(new Action(() => ShowMedia(m)));

            // ---- Wi-Fi baslik ----
            root.Children.Add(Divider(14, 6));
            var head = new Grid { Margin = new Thickness(2, 8, 0, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ht = new StackPanel { Orientation = Orientation.Horizontal };
            ht.Children.Add(SectionTitle("Wi-Fi ağları"));
            listStatus.Margin = new Thickness(10, 0, 0, 0);
            ht.Children.Add(listStatus);
            head.Children.Add(ht);
            refreshIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            refreshIcon.RenderTransform = new RotateTransform();
            var refresh = Clickable(new Border { Width = 32, Height = 32, Child = refreshIcon }, () => _ = ScanAsync());
            refresh.ToolTip = "Yeniden tara";
            System.Windows.Automation.AutomationProperties.SetName(refresh, "Yeniden tara");
            refresh.Margin = new Thickness(0, 0, 8, 0);
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
            root.Children.Add(Card(sv));

            // ---- alt bilgi ----
            var foot = new Grid { Margin = new Thickness(-6, 10, -6, -6) };
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var linkBtn = LinkButton(GSettings, "Ağ ve internet ayarları", () => { Launch("ms-settings:network"); HideFlyout(); });
            foot.Children.Add(linkBtn);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            var bp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 7, 10, 7) };
            bp.Children.Add(batteryIcon); batteryText.Margin = new Thickness(6, 0, 0, 0); bp.Children.Add(batteryText);
            batteryBtn = Clickable(bp, () => { Launch("ms-settings:batterysaver"); HideFlyout(); });
            batteryBtn.ToolTip = "Pil ayarları";
            batteryBtn.Visibility = Visibility.Collapsed;
            right.Children.Add(batteryBtn);
            right.Children.Add(IconButton("\uE7F5", "Ses ayarları", () => { Launch("ms-settings:sound"); HideFlyout(); }));
            Grid.SetColumn(right, 1); foot.Children.Add(right);
            root.Children.Add(foot);

            SetBody(root);
            poll.Tick += (s, e) => { SyncAudio(); UpdateBattery(); _ = ReloadAsync(); };
        }

        /// <summary>Panel kapandiktan sonra calistir (ekran alintisi paneli yakalamasin).</summary>
        void Later(Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += (s, e) => { t.Stop(); a(); };
            t.Start();
        }

        static void Keys(params byte[] keys)
        {
            foreach (var k in keys) Native.keybd_event(k, 0, 0, UIntPtr.Zero);
            for (int i = keys.Length - 1; i >= 0; i--) Native.keybd_event(keys[i], 0, 2, UIntPtr.Zero);
        }

        static TextBlock TileLabel(string t = "")
        {
            var l = Text(t, Theme.Caption, Theme.TextPrimary);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.Margin = new Thickness(0, 6, 0, 0);
            return l;
        }

        static void SetTileActive(QuickTile t, bool active) => t.Set(active);

        /// <summary>Win11 hizli ayar dugmesi: ustte hap seklinde ikon dugmesi, altinda etiket. Aktifken vurgu rengiyle dolar.</summary>
        class QuickTile
        {
            public readonly StackPanel Root = new StackPanel { Margin = new Thickness(4, 4, 4, 8) };
            readonly Border pill;
            readonly TextBlock icon;
            public QuickTile(TextBlock icon, TextBlock label, string name, Action click)
            {
                this.icon = icon;
                pill = Clickable(new Border { Height = 46, Child = icon }, click, Theme.RadiusMd, Theme.SurfaceIdle, Theme.SurfaceHover);
                pill.BorderBrush = Theme.Ink(Theme.StrokeSubtle);
                pill.ToolTip = name;
                System.Windows.Automation.AutomationProperties.SetName(pill, name);
                Root.Children.Add(pill);
                Root.Children.Add(label);
            }
            public void Set(bool active)
            {
                pill.Background = active ? Theme.Brush(Theme.Accent) : Theme.Ink(Theme.SurfaceIdle);
                pill.Tag = active ? (byte)0 : Theme.SurfaceIdle;
                if (!pill.IsKeyboardFocused) pill.BorderBrush = active ? Brushes.Transparent : (Brush)Theme.Ink(Theme.StrokeSubtle);
                icon.Foreground = active ? Theme.Brush(Theme.OnAccent) : Theme.Fg();
            }
        }

        // ---------------- Bluetooth / parlaklik / pil / medya ----------------
        async Task InitBluetoothAsync()
        {
            try
            {
                if (btRadio == null)
                {
                    if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) { btTile.Root.Visibility = Visibility.Collapsed; return; }
                    btRadio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
                }
                if (btRadio == null) { btSub.Text = "Bluetooth yok"; btTile.Set(false); return; }
                bool on = btRadio.State == RadioState.On;
                btSub.Text = "Bluetooth"; btTile.Set(on);
            }
            catch { }
        }

        async Task ToggleBluetooth()
        {
            if (btRadio == null) { Launch("ms-settings:bluetooth"); HideFlyout(); return; }
            bool on = btRadio.State != RadioState.On;
            btTile.Set(on);
            try { if (await btRadio.SetStateAsync(on ? RadioState.On : RadioState.Off) != RadioAccessStatus.Allowed) Launch("ms-settings:bluetooth"); } catch { }
            await InitBluetoothAsync();
        }

        async Task InitBrightnessAsync()
        {
            int v = await Task.Run(Brightness.Get);
            if (v < 0) { brightRow.Visibility = Visibility.Collapsed; return; }
            brightRow.Visibility = Visibility.Visible;
            if (!bright.Dragging) bright.Value = v / 100.0;
            brightText.Text = "%" + v;
        }

        void UpdateBattery()
        {
            if (!Battery.HasBattery) { batteryBtn.Visibility = Visibility.Collapsed; return; }
            int p = Battery.Percent;
            batteryBtn.Visibility = Visibility.Visible;
            batteryText.Text = "%" + p;
            // Segoe Fluent pil ikonlari: EBA0 (bos) .. EBAA (dolu); sarj: EBAB..EBB5
            int step = Math.Clamp((int)Math.Round(p / 10.0), 0, 10);
            batteryIcon.Text = ((char)((Battery.Charging ? 0xEBAB : 0xEBA0) + step)).ToString();
            batteryText.Foreground = p <= 15 && !Battery.Charging ? Theme.Brush(Theme.Danger) : Theme.Fg(Theme.TextSecondary);
        }

        void ShowMedia(MediaInfo m)
        {
            if (m == null) { mediaCard.Visibility = Visibility.Collapsed; return; }
            mediaCard.Visibility = Visibility.Visible;
            mediaTitle.Text = m.Title;
            mediaArtist.Text = m.Artist;
            mediaArtist.Visibility = string.IsNullOrEmpty(m.Artist) ? Visibility.Collapsed : Visibility.Visible;
            mediaPlayIcon.Text = m.Playing ? GPause : GPlay;
            System.Windows.Media.Imaging.BitmapImage img = null;
            if (m.Art != null)
                try
                {
                    img = new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit(); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.DecodePixelWidth = 96;
                    img.StreamSource = new System.IO.MemoryStream(m.Art); img.EndInit(); img.Freeze();
                }
                catch { img = null; }
            if (img != null) { mediaArt.Child = null; mediaArt.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill }; }
            else { mediaArt.Background = Theme.Ink(Theme.SurfaceHover); mediaArt.Child = Icon(GMusic, 18, Theme.TextSecondary); }
        }

        // ---------------- acilis / kapanis ----------------
        protected override void OnOpening()
        {
            expanded = TestExpand; errorSsid = null;
            SyncAudio();
            dndTile.Set(Settings.Current.DoNotDisturb);
            UpdateBattery();
            ShowMedia(Hub.CurrentMedia);
            _ = InitBluetoothAsync();
            _ = InitBrightnessAsync();
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
                soundTileSub.Text = "Ses yok"; volText.Text = "—"; volume.IsEnabled = false;
                SetTileActive(soundTile, false);
                return;
            }
            if (moveSlider && !volume.Dragging) volume.Value = v;
            int pct = (int)Math.Round(v * 100);
            string g = muted || pct == 0 ? GMute : pct < 34 ? GVol1 : pct < 67 ? GVol2 : GVol3;
            volIcon.Text = g; soundTileIcon.Text = g;
            volText.Text = "%" + pct;
            soundTileSub.Text = muted ? "Sessiz" : "Ses açık";
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
            if (!hasWifi) wifiTileSub.Text = "Wi-Fi yok";
            else if (!wifiOn) wifiTileSub.Text = "Wi-Fi kapalı";
            else wifiTileSub.Text = cur?.Ssid ?? "Wi-Fi";
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
            var tb = Text(t, Theme.Body, Theme.TextTertiary);
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
            sig.Children.Add(Icon(GWifi, 16, 45));                 // soluk tam cubuk
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
            txt.Children.Add(Text(n.Ssid, Theme.BodyLarge, Theme.TextPrimary, n.Connected ? FontWeights.SemiBold : FontWeights.Normal));
            string sub = n.Connected ? "Bağlı" + (n.Secured ? ", güvenli" : "")
                       : busySsid == n.Ssid ? "Bağlanıyor…"
                       : errorSsid == n.Ssid ? errorText
                       : n.HasProfile ? "Kayıtlı" + (n.Secured ? ", güvenli" : "")
                       : n.Secured ? "Güvenli" : "Açık ağ";
            var subTb = Text(sub, Theme.Caption, Theme.TextSecondary);
            if (errorSsid == n.Ssid && !n.Connected) subTb.Foreground = Theme.Brush(Theme.Danger);
            if (n.Connected) subTb.Foreground = Theme.Brush(Theme.Success);
            txt.Children.Add(subTb);
            Grid.SetColumn(txt, 1); g.Children.Add(txt);

            if (n.Connected)
            {
                var ok = Icon(GCheck, 13); ok.Foreground = Theme.Brush(Theme.Success);
                Grid.SetColumn(ok, 2); g.Children.Add(ok);
            }
            else
            {
                var pct = Text("%" + n.Signal, Theme.Caption, Theme.TextTertiary);
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
            }, Theme.RadiusMd, (byte)(isOpen || n.Connected ? Theme.SurfaceIdle + 4 : 0), Theme.SurfaceHover);
            row.Margin = new Thickness(0, 1, 0, 1);
            System.Windows.Automation.AutomationProperties.SetName(row, n.Ssid + ", " + sub);
            if (isOpen) row.BorderBrush = Theme.Ink(Theme.StrokeDefault);
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
                var t = Text("Bu ağ kurumsal oturum açma istiyor; Windows ayarlarından bağlan.", Theme.Caption + 0.5, Theme.TextSecondary);
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
                    FontSize = Theme.BodyLarge,
                    FontFamily = UiFont,
                    Foreground = Theme.Fg(),
                    CaretBrush = Theme.Fg(),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(2, 6, 2, 6),
                    SelectionBrush = Theme.Brush(Accent)
                };
                System.Windows.Automation.AutomationProperties.SetName(pwBox, "Ağ güvenlik anahtarı");
                var hint = Text("Ağ güvenlik anahtarı", Theme.Body, Theme.TextTertiary);
                hint.IsHitTestVisible = false;
                hint.Margin = new Thickness(4, 0, 0, 0);
                pwBox.PasswordChanged += (s, e) => hint.Visibility = pwBox.Password.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
                pwBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) DoConnect(n); };
                var box = new Grid();
                box.Children.Add(pwBox); box.Children.Add(hint);
                // Win11 metin kutusu: koyu dolgu, ince kenar; odakta kenar vurgu rengine doner
                var field = new Border
                {
                    CornerRadius = new CornerRadius(Theme.RadiusSm),
                    Background = Theme.Brush(Theme.Field),
                    BorderBrush = Theme.Ink(Theme.StrokeDefault),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 1, 8, 0),
                    Margin = new Thickness(0, 0, 0, 10),
                    Child = box
                };
                pwBox.GotKeyboardFocus += (s, e) => { field.BorderBrush = Theme.Brush(Accent); };
                pwBox.LostKeyboardFocus += (s, e) => { field.BorderBrush = Theme.Ink(Theme.StrokeDefault); };
                p.Children.Add(field);
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
