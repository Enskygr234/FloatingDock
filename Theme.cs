using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace FloatingDock
{
    /// <summary>
    /// "Kil" tasarim sistemi, "Nane & deniz" paleti: yumusak 3B (claymorphism) — serin gri zemin, beyaz kabarik kartlar,
    /// camgobegi (teal) vurgu, nane / gok / buz tonlu kutular,
    /// buyuk yuvarlak koseler, Nunito yazi tipi. Tum renk, yazi, kose ve hareket degerleri tek yerde.
    /// Acik (varsayilan) ve koyu kil; vurgu rengi mor ya da Windows vurgu rengi.
    /// Degerler program basinda bir kez hesaplanir (Init); tema degisince uygulama kendini yeniden baslatir.
    /// </summary>
    static class Theme
    {
        public static bool Dark { get; private set; } = true;

        // ---------- renk: zemin ----------
        /// <summary>Akrilik camin ustune serilen ton (okunabilirlik icin).</summary>
        public static Color Tint { get; private set; }
        /// <summary>Acilir panellerin tonu: Mica secildiyse malzeme gorunsun diye daha seffaf.</summary>
        public static Color PanelTint { get; private set; }
        /// <summary>Opak acilir yuzey (menuler, guc menusu, ipuclari).</summary>
        public static Color Raised { get; private set; }
        /// <summary>Metin kutusu dolgusu.</summary>
        public static Color Field { get; private set; }
        /// <summary>Dock hapinin dolgusu (bulaniklik yok, bu yuzden panellerden biraz daha opak).</summary>
        public static Color DockFill { get; private set; }
        /// <summary>Kabugun zemin rengi (tam ekran Baslat, masaustu bilesenlerinin arkasi).</summary>
        public static Color Background { get; private set; }
        /// <summary>Kil golgesinin rengi: acikta koyu deniz tonu, koyuda siyah.</summary>
        public static Color Shadow { get; private set; }
        /// <summary>Yuzey katmanlari icin "murekkep" rengi: koyu modda sicak beyaz, acik modda koyu murdum; alfa ile kullanilir.</summary>
        public static Color InkBase { get; private set; }

        // murekkep-alfa yuzey kademeleri
        public static byte SurfaceIdle => (byte)(Dark ? 16 : 14);
        public static byte SurfaceHover => (byte)(Dark ? 30 : 26);
        public static byte SurfacePressed => (byte)(Dark ? 10 : 9);
        public static byte SurfaceSelected => (byte)(Dark ? 38 : 34);
        public static byte StrokeSubtle => (byte)(Dark ? 20 : 18);
        public static byte StrokeDefault => (byte)(Dark ? 34 : 30);

        // ---------- renk: metin (WCAG AA: lavanta zeminde hepsi 4.5:1 ustu) ----------
        public static byte TextPrimary => (byte)(Dark ? 250 : 255);
        public static byte TextSecondary => (byte)(Dark ? 205 : 210);
        public static byte TextTertiary => (byte)(Dark ? 165 : 182);
        public static byte TextDisabled => (byte)(Dark ? 95 : 110);

        // ---------- renk: vurgu ----------
        public static Color Accent { get; private set; }
        public static Color AccentHover { get; private set; }
        public static Color AccentMuted { get; private set; }
        /// <summary>Vurgu dolgusunun ustundeki metin/ikon rengi (kontrast otomatik secilir).</summary>
        public static Color OnAccent { get; private set; }
        /// <summary>Ada her zaman koyu oldugu icin onun vurgusu ayri.</summary>
        public static Color AccentOnDark { get; private set; }
        public static Color Success { get; private set; }
        public static Color Danger { get; private set; }
        public static readonly Color DangerFill = Color.FromRgb(0xC4, 0x2B, 0x1C);
        /// <summary>
        /// Vurgu secenekleri (acik zemin / koyu zemin tonu). Acik tonlarin hepsi beyaz uzerinde 4.5:1 ustu.
        /// "auto": paletin kendi vurgusu.
        /// </summary>
        public static readonly (string key, string name, Color light, Color dark)[] Accents =
        {
            ("teal",   "Teal",   Color.FromRgb(0x0E, 0x74, 0x90), Color.FromRgb(0x5E, 0xC8, 0xDA)),
            ("violet", "Mor",    Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0xA7, 0x8B, 0xFA)),
            ("coral",  "Mercan", Color.FromRgb(0xC2, 0x41, 0x0C), Color.FromRgb(0xFB, 0x92, 0x3C)),
            ("green",  "Yeşil",  Color.FromRgb(0x04, 0x78, 0x57), Color.FromRgb(0x34, 0xD3, 0x99)),
            ("rose",   "Gül",    Color.FromRgb(0xBE, 0x18, 0x5D), Color.FromRgb(0xF4, 0x72, 0xB6)),
        };

        /// <summary>Renk paletleri: zemin, kart, murekkep, golge, paletin kendi vurgusu ve 6 pastel kutu (dolgu, ustundeki yazi).</summary>
        sealed class PaletteDef
        {
            public string Accent;
            public Color Ink, Bg, Card, Shadow, DarkInk, DarkBg, DarkCard;
            public (Color fill, Color ink)[] Pastels;
        }

        public static readonly (string key, string name)[] Palettes = { ("mint", "Nane & deniz"), ("lavender", "Lavanta"), ("sand", "Sıcak kum") };

        static PaletteDef Palette(string key) => key switch
        {
            "lavender" => new PaletteDef
            {
                Accent = "violet",
                Ink = Color.FromRgb(0x33, 0x2F, 0x3A), Bg = Color.FromRgb(0xF4, 0xF1, 0xFA), Card = Color.FromRgb(0xFB, 0xF9, 0xFE), Shadow = Color.FromRgb(0x5B, 0x21, 0xB6),
                DarkInk = Color.FromRgb(0xF3, 0xEF, 0xFA), DarkBg = Color.FromRgb(0x22, 0x1F, 0x29), DarkCard = Color.FromRgb(0x2D, 0x29, 0x36),
                Pastels = new[]
                {
                    (Color.FromRgb(0xE6, 0xDC, 0xFB), Color.FromRgb(0x5B, 0x21, 0xB6)), (Color.FromRgb(0xD4, 0xE8, 0xFB), Color.FromRgb(0x1D, 0x4E, 0x89)),
                    (Color.FromRgb(0xCD, 0xEF, 0xD9), Color.FromRgb(0x06, 0x5F, 0x46)), (Color.FromRgb(0xFF, 0xEF, 0xB8), Color.FromRgb(0x92, 0x40, 0x0E)),
                    (Color.FromRgb(0xFF, 0xD9, 0xCC), Color.FromRgb(0x9A, 0x34, 0x12)), (Color.FromRgb(0xFB, 0xD5, 0xE5), Color.FromRgb(0x9D, 0x17, 0x4D)),
                }
            },
            "sand" => new PaletteDef
            {
                Accent = "coral",
                Ink = Color.FromRgb(0x3A, 0x2F, 0x27), Bg = Color.FromRgb(0xF6, 0xF1, 0xEA), Card = Color.FromRgb(0xFF, 0xFB, 0xF5), Shadow = Color.FromRgb(0x7C, 0x4A, 0x1E),
                DarkInk = Color.FromRgb(0xF7, 0xF0, 0xE8), DarkBg = Color.FromRgb(0x24, 0x1F, 0x1A), DarkCard = Color.FromRgb(0x30, 0x29, 0x22),
                Pastels = new[]
                {
                    (Color.FromRgb(0xEE, 0xE6, 0xDA), Color.FromRgb(0x5B, 0x47, 0x36)), (Color.FromRgb(0xDC, 0xE7, 0xEE), Color.FromRgb(0x2F, 0x4E, 0x63)),
                    (Color.FromRgb(0xDD, 0xEB, 0xD8), Color.FromRgb(0x3F, 0x5F, 0x2E)), (Color.FromRgb(0xF8, 0xE7, 0xC2), Color.FromRgb(0x85, 0x4D, 0x0E)),
                    (Color.FromRgb(0xF7, 0xD9, 0xC8), Color.FromRgb(0x9A, 0x34, 0x12)), (Color.FromRgb(0xF3, 0xDA, 0xD9), Color.FromRgb(0x8E, 0x2B, 0x3A)),
                }
            },
            _ => new PaletteDef
            {
                Accent = "teal",
                Ink = Color.FromRgb(0x1F, 0x2A, 0x2E), Bg = Color.FromRgb(0xF1, 0xF5, 0xF5), Card = Color.FromRgb(0xFC, 0xFE, 0xFE), Shadow = Color.FromRgb(0x0F, 0x3D, 0x47),
                DarkInk = Color.FromRgb(0xEE, 0xF5, 0xF6), DarkBg = Color.FromRgb(0x17, 0x1F, 0x22), DarkCard = Color.FromRgb(0x21, 0x2B, 0x2F),
                Pastels = new[]
                {
                    (Color.FromRgb(0xDC, 0xEF, 0xF2), Color.FromRgb(0x15, 0x5E, 0x75)), (Color.FromRgb(0xD3, 0xE8, 0xF6), Color.FromRgb(0x0B, 0x4A, 0x6F)),
                    (Color.FromRgb(0xCF, 0xF0, 0xE2), Color.FromRgb(0x06, 0x5F, 0x46)), (Color.FromRgb(0xFB, 0xEF, 0xC9), Color.FromRgb(0x85, 0x4D, 0x0E)),
                    (Color.FromRgb(0xFB, 0xDF, 0xD3), Color.FromRgb(0x9A, 0x34, 0x12)), (Color.FromRgb(0xF6, 0xDD, 0xE8), Color.FromRgb(0x9D, 0x17, 0x4D)),
                }
            },
        };

        /// <summary>Etkin paletin pastelleri (acik kil tonlari).</summary>
        static (Color fill, Color ink)[] PastelsLight = Palette("mint").Pastels;

        /// <summary>Ayar anahtarindan vurgu (bilinmeyen / eski "frost" degeri: paletin kendi vurgusu).</summary>
        public static Color AccentColor(string key, string palette, bool dark)
        {
            if (key == "auto" || key == "frost" || string.IsNullOrEmpty(key)) key = Palette(palette).Accent;
            foreach (var a in Accents) if (a.key == key) return dark ? a.dark : a.light;
            return dark ? Accents[0].dark : Accents[0].light;
        }
        public enum Pastel { Lilac, Sky, Mint, Butter, Peach, Pink }

        // ---------- yazi ----------
        /// <summary>Nunito: uygulamaya gomulu (Fonts klasoru, SIL OFL 1.1); yoksa Segoe UI.</summary>
        public static readonly FontFamily Ui = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Nunito, Segoe UI");
        public static readonly FontFamily Display = Ui;
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        /// <summary>Tip olcegi: caption 12.5, body 14, bodyLarge 15, subtitle 16, title 24, hero 48.</summary>
        public const double Caption = 12.5, Body = 14, BodyLarge = 15, Subtitle = 16, Title = 24, Hero = 48;
        /// <summary>Nunito kalinliklari: govde 600, vurgu 800, baslik 900.</summary>
        public static readonly FontWeight Regular = FontWeight.FromOpenTypeWeight(600), Bold = FontWeight.FromOpenTypeWeight(800), Heavy = FontWeight.FromOpenTypeWeight(900);

        // ---------- kose / bosluk ----------
        public const double RadiusSm = 10, RadiusMd = 14, RadiusLg = 20, RadiusXl = 28, RadiusPanel = 30;
        public const double Pad = 20, Gap = 10;
        /// <summary>Kil kartin golgesi icin pencere kenarinda birakilan seffaf pay.</summary>
        public const double ShadowPad = 26;

        // ---------- hareket ----------
        public static readonly Duration Fast = TimeSpan.FromMilliseconds(120);
        public static readonly Duration Normal = TimeSpan.FromMilliseconds(200);
        public static readonly Duration Slow = TimeSpan.FromMilliseconds(280);
        public static IEasingFunction EaseOut => new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        /// <summary>Kil yayi: hafifce tasip yerine oturur.</summary>
        public static IEasingFunction Spring => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };

        /// <summary>Windows'ta "animasyon efektleri" kapaliysa hareketi atla (azaltilmis hareket).</summary>
        public static bool Motion => SystemParameters.ClientAreaAnimation;
        public static Duration D(Duration d) => Motion ? d : new Duration(TimeSpan.Zero);

        static Theme() => Init();

        /// <summary>Ayarlara ve Windows temasina gore paleti hesaplar.</summary>
        public static void Init()
        {
            var s = Settings.Current;
            Dark = s.Theme == "dark" || (s.Theme != "light" && SystemUsesDark());
            // secili palet: zemin, kart, murekkep, golge ve pastel kutular (acik kil ya da koyu kil)
            var pal = Palette(s.Palette);
            PastelsLight = pal.Pastels;
            InkBase = Dark ? pal.DarkInk : pal.Ink;
            Background = Dark ? pal.DarkBg : pal.Bg;
            var card = Dark ? pal.DarkCard : pal.Card;
            Tint = Color.FromArgb(Dark ? (byte)250 : (byte)252, card.R, card.G, card.B);
            PanelTint = Tint;
            Raised = Dark ? Mix(card, Colors.White, 0.06) : Colors.White;
            DockFill = Color.FromArgb(246, card.R, card.G, card.B);
            Field = Dark ? Background : Colors.White;
            Success = Dark ? Color.FromRgb(0x6E, 0xE7, 0xB7) : Color.FromRgb(0x04, 0x78, 0x57);
            Danger = Dark ? Color.FromRgb(0xFD, 0xA4, 0xAF) : Color.FromRgb(0xBE, 0x12, 0x3C);
            Shadow = Dark ? Colors.Black : pal.Shadow;

            Color? win = s.Accent == "windows" ? WindowsAccent(Dark) : null;
            Accent = win ?? AccentColor(s.Accent, s.Palette, Dark);
            AccentOnDark = (s.Accent == "windows" ? WindowsAccent(true) : null) ?? AccentColor(s.Accent, s.Palette, true);
            AccentHover = Mix(Accent, Dark ? Colors.White : Colors.Black, 0.18);
            AccentMuted = Color.FromArgb(Dark ? (byte)46 : (byte)40, Accent.R, Accent.G, Accent.B);
            OnAccent = Luminance(Accent) > 0.35 ? Color.FromRgb(0x0B, 0x16, 0x26) : Colors.White;
        }

        static bool SystemUsesDark()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return !(k?.GetValue("AppsUseLightTheme") is int v && v == 1);
            }
            catch { return true; }
        }

        /// <summary>Windows vurgu renginin, zemine gore okunabilir tonu.</summary>
        static Color? WindowsAccent(bool forDark)
        {
            try
            {
                var ui = new global::Windows.UI.ViewManagement.UISettings();
                var c = ui.GetColorValue(forDark ? global::Windows.UI.ViewManagement.UIColorType.AccentLight2
                                                 : global::Windows.UI.ViewManagement.UIColorType.AccentDark1);
                return Color.FromRgb(c.R, c.G, c.B);
            }
            catch { return null; }
        }

        static double Luminance(Color c)
        {
            static double L(byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
        }

        static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        // ---------- yardimcilar ----------
        /// <summary>Yuzey katmani: murekkep rengi + alfa.</summary>
        public static SolidColorBrush Ink(byte a) => new SolidColorBrush(Color.FromArgb(a, InkBase.R, InkBase.G, InkBase.B));
        public static bool IsInk(Color c) => c.R == InkBase.R && c.G == InkBase.G && c.B == InkBase.B;
        public static SolidColorBrush Brush(Color c) => new SolidColorBrush(c);
        public static SolidColorBrush Fg() => Ink(TextPrimary);
        public static SolidColorBrush Fg(byte a) => Ink(a);
        /// <summary>Her zaman koyu kalan yuzeyler (ada) icin beyaz katman.</summary>
        public static SolidColorBrush OnDark(byte a) => new SolidColorBrush(Color.FromArgb(a, 255, 255, 255));

        // ---------- pastel ----------
        /// <summary>Pastel kutu dolgusu ve ustundeki murekkep (koyu kilde koyu dolgu + acik murekkep).</summary>
        public static (Color fill, Color ink) PastelColors(Pastel p)
        {
            var (f, i) = PastelsLight[(int)p];
            return Dark ? (Mix(i, Color.FromRgb(0x1E, 0x1A, 0x24), 0.62), f) : (f, i);
        }

        /// <summary>Bir rengin (or. uygulama ikonunun baskin rengi) tonuna en yakin pastel.</summary>
        public static Pastel PastelFor(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            if (d < 0.08) return Pastel.Lilac; // gri / siyah-beyaz ikonlar
            double h = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
            return h < 18 ? Pastel.Peach : h < 40 ? Pastel.Peach : h < 70 ? Pastel.Butter : h < 165 ? Pastel.Mint
                 : h < 250 ? Pastel.Sky : h < 300 ? Pastel.Lilac : h < 345 ? Pastel.Pink : Pastel.Peach;
        }

        /// <summary>Bir ikonun baskin (en doygun, parlak) rengi; gri ikonlarda null.</summary>
        public static Color? DominantOf(ImageSource src)
        {
            try
            {
                if (!(src is System.Windows.Media.Imaging.BitmapSource bs)) return null;
                var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);
                double best = -1; Color pick = Colors.Gray;
                int step = Math.Max(1, (w * h) / 600) * 4;
                for (int i = 0; i < px.Length; i += step)
                {
                    if (px[i + 3] < 160) continue; // seffaf kenarlar
                    int b = px[i], g = px[i + 1], r = px[i + 2];
                    int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                    double sat = mx == 0 ? 0 : (mx - mn) / (double)mx;
                    double score = sat * (mx / 255.0);
                    if (score > best) { best = score; pick = Color.FromRgb((byte)r, (byte)g, (byte)b); }
                }
                return best < 0.18 ? (Color?)null : pick;
            }
            catch { return null; }
        }

        /// <summary>Ikon icin pastel kutu rengi (gri ikonlar lila).</summary>
        public static Pastel PastelOf(ImageSource icon) => DominantOf(icon) is Color c ? PastelFor(c) : Pastel.Lilac;

        // ---------- kil yuzey ----------
        /// <summary>Kil golgesi: genis, yumusak, hafif asagi kaymis.</summary>
        public static System.Windows.Media.Effects.DropShadowEffect ClayShadow(double blur = 28, double depth = 10, double opacity = -1) => new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Shadow, BlurRadius = blur, ShadowDepth = depth, Direction = 270,
            Opacity = opacity >= 0 ? opacity : (Dark ? 0.45 : 0.16), RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
        };

        /// <summary>
        /// Kabarik kil yuzey: dolgu + dis golge + ustte ic parlama + altta ic golge (yumusak 3B).
        /// child kartin icerigi; donen Grid yerlestirilir.
        /// </summary>
        public static Grid Clay(UIElement child, Brush fill, double radius, Thickness padding, bool shadow = true, double blur = 28, double depth = 10)
        {
            var g = new Grid();
            var bg = new Border { CornerRadius = new CornerRadius(radius), Background = fill };
            if (shadow) bg.Effect = ClayShadow(blur, depth);
            g.Children.Add(bg);
            g.Children.Add(ClayLight(radius));
            g.Children.Add(new Border { Padding = padding, Child = child });
            return g;
        }

        /// <summary>Kil isigi: ustte ince parlak kenar, altta hafif ic golge (dolgunun ustune serilir).</summary>
        public static Border ClayLight(double radius) => new Border
        {
            CornerRadius = new CornerRadius(radius), IsHitTestVisible = false,
            BorderThickness = new Thickness(0, 2, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(Dark ? (byte)26 : (byte)230, 255, 255, 255)),
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.62),
                new GradientStop(Color.FromArgb(Dark ? (byte)40 : (byte)16, Shadow.R, Shadow.G, Shadow.B), 1)
            }, 90)
        };

        static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        static string InkHex(byte a) => Hex(Color.FromArgb(a, InkBase.R, InkBase.G, InkBase.B));

        /// <summary>
        /// Uygulama genelinde sag tik menulerini, ayiricilari ve ipuclarini temaya uygun cizer
        /// (WPF'in varsayilan acik renkli menuleri yerine).
        /// </summary>
        public static void InstallResources(Application app)
        {
            string raised = Hex(Raised), stroke = InkHex(StrokeDefault), fg = InkHex(TextPrimary), fg2 = InkHex(TextSecondary),
                   hover = InkHex(SurfaceHover), sep = InkHex(StrokeSubtle), acc = Hex(Accent), dis = InkHex(TextDisabled), shc = Hex(Color.FromRgb(Shadow.R, Shadow.G, Shadow.B)), shOp = Dark ? "0.5" : "0.18";
            var xaml = $@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='FontFamily' Value='pack://application:,,,/Fonts/#Nunito'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Foreground' Value='{fg}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Margin='0,0,10,12' Background='{raised}' BorderBrush='{stroke}' BorderThickness='1' CornerRadius='18' Padding='6' MinWidth='210'>
            <Border.Effect><DropShadowEffect BlurRadius='22' ShadowDepth='6' Direction='270' Opacity='{shOp}' Color='{shc}'/></Border.Effect>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{fg}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='12' Padding='10,8,14,8' MinHeight='36'>
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition Width='26'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
              <TextBlock x:Name='Check' Text='&#xE73E;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='12' Foreground='{acc}' VerticalAlignment='Center' Visibility='Collapsed'/>
              <ContentPresenter x:Name='Ic' ContentSource='Icon' VerticalAlignment='Center' TextElement.Foreground='{fg2}'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='{hover}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter TargetName='Check' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='{dis}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='{{x:Static MenuItem.SeparatorStyleKey}}' TargetType='Separator'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value><ControlTemplate TargetType='Separator'><Border Height='1' Margin='-4,4' Background='{sep}'/></ControlTemplate></Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ToolTip'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Margin='0,0,8,10' Background='{raised}' BorderBrush='{stroke}' BorderThickness='1' CornerRadius='12' Padding='11,6,11,7'>
            <Border.Effect><DropShadowEffect BlurRadius='10' ShadowDepth='2' Direction='270' Opacity='0.3' Color='Black'/></Border.Effect>
            <ContentPresenter TextElement.Foreground='{fg}' TextElement.FontSize='13' TextElement.FontWeight='SemiBold' TextElement.FontFamily='pack://application:,,,/Fonts/#Nunito'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
            try { app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml)); }
            catch (Exception ex) { Program.Log(ex); }
        }
    }
}
