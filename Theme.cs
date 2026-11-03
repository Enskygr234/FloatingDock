using System;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace FloatingDock
{
    /// <summary>
    /// "Frost" tasarim sistemi: tum renk, yazi, kose ve hareket degerleri tek yerde.
    /// Koyu ve acik mod; vurgu rengi buz mavisi ya da Windows vurgu rengi.
    /// Degerler program basinda bir kez hesaplanir (Init); tema degisince uygulama kendini yeniden baslatir.
    /// </summary>
    static class Theme
    {
        public static bool Dark { get; private set; } = true;

        // ---------- renk: zemin ----------
        /// <summary>Akrilik camin ustune serilen ton (okunabilirlik icin).</summary>
        public static Color Tint { get; private set; }
        /// <summary>Opak acilir yuzey (menuler, guc menusu, ipuclari).</summary>
        public static Color Raised { get; private set; }
        /// <summary>Metin kutusu dolgusu.</summary>
        public static Color Field { get; private set; }
        /// <summary>Yuzey katmanlari icin "murekkep" rengi: koyu modda beyaz, acik modda siyah; alfa ile kullanilir.</summary>
        public static Color InkBase { get; private set; }

        // murekkep-alfa yuzey kademeleri
        public static byte SurfaceIdle => (byte)(Dark ? 12 : 10);
        public static byte SurfaceHover => (byte)(Dark ? 24 : 18);
        public static byte SurfacePressed => (byte)(Dark ? 8 : 6);
        public static byte SurfaceSelected => (byte)(Dark ? 30 : 24);
        public static byte StrokeSubtle => (byte)(Dark ? 18 : 22);
        public static byte StrokeDefault => (byte)(Dark ? 30 : 38);

        // ---------- renk: metin (WCAG AA ve uzeri) ----------
        public static byte TextPrimary => (byte)(Dark ? 245 : 230);
        public static byte TextSecondary => (byte)(Dark ? 190 : 165);
        public static byte TextTertiary => (byte)(Dark ? 140 : 125);
        public static byte TextDisabled => (byte)(Dark ? 90 : 80);

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
        static readonly Color Frost = Color.FromRgb(0x8F, 0xC4, 0xFF), FrostLight = Color.FromRgb(0x00, 0x5F, 0xB8);

        // ---------- yazi ----------
        public static readonly FontFamily Ui = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        /// <summary>Tip olcegi: caption 11.5, body 13, bodyLarge 14, subtitle 15, title 22, hero 44.</summary>
        public const double Caption = 11.5, Body = 13, BodyLarge = 14, Subtitle = 15, Title = 22, Hero = 44;

        // ---------- kose / bosluk ----------
        public const double RadiusSm = 6, RadiusMd = 8, RadiusLg = 12, RadiusXl = 16;
        public const double Pad = 16, Gap = 8;

        // ---------- hareket ----------
        public static readonly Duration Fast = TimeSpan.FromMilliseconds(120);
        public static readonly Duration Normal = TimeSpan.FromMilliseconds(200);
        public static readonly Duration Slow = TimeSpan.FromMilliseconds(280);
        public static IEasingFunction EaseOut => new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };

        /// <summary>Windows'ta "animasyon efektleri" kapaliysa hareketi atla (azaltilmis hareket).</summary>
        public static bool Motion => SystemParameters.ClientAreaAnimation;
        public static Duration D(Duration d) => Motion ? d : new Duration(TimeSpan.Zero);

        static Theme() => Init();

        /// <summary>Ayarlara ve Windows temasina gore paleti hesaplar.</summary>
        public static void Init()
        {
            var s = Settings.Current;
            Dark = s.Theme == "dark" || (s.Theme != "light" && SystemUsesDark());
            InkBase = Dark ? Colors.White : Color.FromRgb(0x10, 0x12, 0x18);
            Tint = Dark ? Color.FromArgb(200, 18, 20, 26) : Color.FromArgb(205, 246, 247, 250);
            Raised = Dark ? Color.FromArgb(250, 32, 35, 43) : Color.FromArgb(252, 252, 252, 253);
            Field = Dark ? Color.FromArgb(90, 0, 0, 0) : Color.FromArgb(200, 255, 255, 255);
            Success = Dark ? Color.FromRgb(0x7F, 0xDB, 0xA9) : Color.FromRgb(0x0F, 0x7B, 0x3F);
            Danger = Dark ? Color.FromRgb(0xFF, 0x8A, 0x8A) : Color.FromRgb(0xC4, 0x2B, 0x1C);

            Color? win = s.Accent == "windows" ? WindowsAccent(Dark) : null;
            Accent = win ?? (Dark ? Frost : FrostLight);
            AccentOnDark = (s.Accent == "windows" ? WindowsAccent(true) : null) ?? Frost;
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

        static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        static string InkHex(byte a) => Hex(Color.FromArgb(a, InkBase.R, InkBase.G, InkBase.B));

        /// <summary>
        /// Uygulama genelinde sag tik menulerini, ayiricilari ve ipuclarini temaya uygun cizer
        /// (WPF'in varsayilan acik renkli menuleri yerine).
        /// </summary>
        public static void InstallResources(Application app)
        {
            string raised = Hex(Raised), stroke = InkHex(StrokeDefault), fg = InkHex(TextPrimary), fg2 = InkHex(TextSecondary),
                   hover = InkHex(SurfaceHover), sep = InkHex(StrokeSubtle), acc = Hex(Accent), dis = InkHex(TextDisabled);
            var xaml = $@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Foreground' Value='{fg}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Margin='0,0,10,12' Background='{raised}' BorderBrush='{stroke}' BorderThickness='1' CornerRadius='8' Padding='4' MinWidth='200'>
            <Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='4' Direction='270' Opacity='0.35' Color='Black'/></Border.Effect>
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
          <Border x:Name='Bd' Background='Transparent' CornerRadius='4' Padding='10,7,14,7' MinHeight='32'>
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
          <Border Margin='0,0,8,10' Background='{raised}' BorderBrush='{stroke}' BorderThickness='1' CornerRadius='6' Padding='9,5,9,6'>
            <Border.Effect><DropShadowEffect BlurRadius='10' ShadowDepth='2' Direction='270' Opacity='0.3' Color='Black'/></Border.Effect>
            <ContentPresenter TextElement.Foreground='{fg}' TextElement.FontSize='12' TextElement.FontFamily='Segoe UI Variable Text, Segoe UI'/>
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
