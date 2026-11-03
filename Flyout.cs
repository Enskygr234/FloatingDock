using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FloatingDock
{
    /// <summary>Dock'un ustunde acilan, odak alabilen (sifre yazilabilen) akrilik cam panel tabani.</summary>
    abstract class Flyout : Window
    {
        public static readonly Color Accent = Color.FromRgb(0x8A, 0x7C, 0xFF);
        public static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        protected IntPtr hwnd;
        protected double scale = 1;
        readonly ScaleTransform pop = new ScaleTransform(1, 1);
        readonly TranslateTransform slide = new TranslateTransform();
        DateTime hiddenAt = DateTime.MinValue;

        protected Flyout(string title)
        {
            Title = title;
            Glass.Setup(this);
            SizeToContent = SizeToContent.Height;
            Width = 360;
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Glass.ApplyFocusable(hwnd);
                scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            };
            Deactivated += (s, e) => HideFlyout();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) HideFlyout(); };
        }

        protected void SetBody(UIElement body)
        {
            var tg = new TransformGroup();
            tg.Children.Add(pop);
            tg.Children.Add(slide);
            Content = new Border
            {
                // akriligin ustune hafif mor-lacivert bir ton
                Background = new LinearGradientBrush(Color.FromArgb(70, 40, 34, 90), Color.FromArgb(40, 8, 12, 30), 90),
                Padding = new Thickness(16),
                Child = body,
                RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = tg
            };
        }

        public bool IsOpen => IsVisible;
        /// <summary>Panel az once (dock dugmesine tiklarken) kapandiysa tekrar acma: tikla-kapat davranisi.</summary>
        public bool JustClosed => (DateTime.Now - hiddenAt).TotalMilliseconds < 300;

        protected virtual void OnOpening() { }
        protected virtual void OnClosing() { }

        /// <summary>anchorRightPx: panelin hizalanacagi sag kenar (fiziksel piksel), dockTopDip: dock'un ust kenari.</summary>
        public void ShowAt(double anchorCenterPx, double dockTopDip)
        {
            OnOpening();
            if (!IsVisible) { Opacity = 0; Show(); }
            UpdateLayout();
            double sw = Native.GetSystemMetrics(0) / scale;
            double left = anchorCenterPx / scale - ActualWidth / 2;
            Left = Math.Round(Math.Max(10, Math.Min(sw - ActualWidth - 10, left)));
            Top = Math.Round(dockTopDip - ActualHeight - 10);
            Activate();
            Native.SetForegroundWindow(hwnd);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }

        public void HideFlyout()
        {
            if (!IsVisible) return;
            hiddenAt = DateTime.Now;
            OnClosing();
            BeginAnimation(OpacityProperty, null);
            Hide();
        }

        // ================= ortak, modern kontroller =================
        public static TextBlock Text(string t, double size = 13, byte alpha = 255, FontWeight? w = null) => new TextBlock
        {
            Text = t,
            FontSize = size,
            FontFamily = UiFont,
            FontWeight = w ?? FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        public static new TextBlock Icon(string g, double size = 16, byte alpha = 255) => new TextBlock
        {
            Text = g,
            FontFamily = IconFont,
            FontSize = size,
            Foreground = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        /// <summary>Uzerine gelince parlayan, basinca kuculen yuvarlak kutu.</summary>
        public static Border Clickable(UIElement child, Action click, double radius = 10, byte idle = 14, byte hover = 32)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(radius),
                Background = new SolidColorBrush(Color.FromArgb(idle, 255, 255, 255)),
                Child = child,
                Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            b.Tag = idle;
            b.MouseEnter += (s, e) => { if (b.Background is SolidColorBrush sb && !sb.IsFrozen && sb.Color.R == 255) sb.Color = Color.FromArgb(hover, 255, 255, 255); };
            b.MouseLeave += (s, e) => { if (b.Background is SolidColorBrush sb && !sb.IsFrozen && sb.Color.R == 255) sb.Color = Color.FromArgb((byte)b.Tag, 255, 255, 255); Press(b, 1); };
            b.MouseLeftButtonDown += (s, e) => { Press(b, 0.96); e.Handled = true; };
            b.MouseLeftButtonUp += (s, e) => { Press(b, 1); e.Handled = true; click?.Invoke(); };
            return b;
        }

        static void Press(Border b, double to)
        {
            var st = (ScaleTransform)b.RenderTransform;
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(110));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        /// <summary>Holografik vurgulu ana dugme.</summary>
        public static Border PrimaryButton(string text, Action click)
        {
            var b = Clickable(new TextBlock
            {
                Text = text, FontFamily = UiFont, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 7, 16, 7)
            }, click, 8);
            b.Background = new LinearGradientBrush(Color.FromRgb(0x6F, 0x8C, 0xFF), Color.FromRgb(0xA0, 0x6C, 0xF5), 0);
            return b;
        }

        public static Border SecondaryButton(string text, Action click) => Clickable(new TextBlock
        {
            Text = text, FontFamily = UiFont, FontSize = 13, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(14, 7, 14, 7)
        }, click, 8, 22, 40);
    }

    /// <summary>iOS/Win11 tarzi acma-kapama anahtari.</summary>
    class Toggle : Grid
    {
        readonly Border track = new Border { Width = 40, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        readonly Ellipse knob = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0) };
        readonly TranslateTransform move = new TranslateTransform();
        bool on;
        public event Action<bool> Changed;

        public Toggle()
        {
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            knob.RenderTransform = move;
            Children.Add(track);
            Children.Add(knob);
            Apply(false);
            MouseLeftButtonUp += (s, e) => { e.Handled = true; IsOn = !on; Changed?.Invoke(on); };
            MouseLeftButtonDown += (s, e) => e.Handled = true;
        }

        public bool IsOn { get => on; set { if (value != on) { on = value; Apply(true); } } }

        void Apply(bool animate)
        {
            track.Background = on ? new LinearGradientBrush(Color.FromRgb(0x6F, 0x8C, 0xFF), Color.FromRgb(0xA0, 0x6C, 0xF5), 0)
                                  : (Brush)new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            track.BorderBrush = new SolidColorBrush(on ? Colors.Transparent : Color.FromArgb(150, 255, 255, 255));
            knob.Fill = on ? Brushes.White : new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
            var a = new DoubleAnimation(on ? 20 : 0, TimeSpan.FromMilliseconds(animate ? 160 : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            move.BeginAnimation(TranslateTransform.XProperty, a);
        }
    }

    /// <summary>Ince, parlayan dolgulu kaydirici (0..1).</summary>
    class GlowSlider : Grid
    {
        readonly Border track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        readonly Border fill = new Border
        {
            Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush(Color.FromRgb(0x6F, 0xD6, 0xFF), Color.FromRgb(0xA0, 0x6C, 0xF5), 0)
        };
        readonly Ellipse thumb = new Ellipse
        {
            Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x8A, 0x7C, 0xFF), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 }
        };
        double value;
        bool drag;
        public event Action<double> ValueChanged;

        public GlowSlider()
        {
            Height = 28;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            Children.Add(track); Children.Add(fill); Children.Add(thumb);
            SizeChanged += (s, e) => Layout();
            MouseLeftButtonDown += (s, e) => { drag = true; CaptureMouse(); SetFrom(e.GetPosition(this).X); e.Handled = true; };
            MouseMove += (s, e) => { if (drag) SetFrom(e.GetPosition(this).X); };
            MouseLeftButtonUp += (s, e) => { drag = false; ReleaseMouseCapture(); e.Handled = true; };
            MouseWheel += (s, e) => { Value = value + (e.Delta > 0 ? 0.02 : -0.02); ValueChanged?.Invoke(value); e.Handled = true; };
        }

        public bool Dragging => drag;
        public double Value { get => value; set { this.value = Math.Clamp(value, 0, 1); Layout(); } }

        void SetFrom(double x)
        {
            double w = Math.Max(1, ActualWidth - thumb.Width);
            Value = (x - thumb.Width / 2) / w;
            ValueChanged?.Invoke(value);
        }

        void Layout()
        {
            double w = Math.Max(0, ActualWidth - thumb.Width);
            thumb.Margin = new Thickness(value * w, 0, 0, 0);
            fill.Width = value * w + thumb.Width / 2;
        }
    }
}
