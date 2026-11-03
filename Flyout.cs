using System;
using System.Linq;
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

        // ---- "2060" katmanlari: kayan aurora isiklari + holografik kenar cizgileri ----
        readonly TranslateTransform auroraA = new TranslateTransform(), auroraB = new TranslateTransform(), auroraC = new TranslateTransform();
        readonly TranslateTransform shimmer = new TranslateTransform();

        static Ellipse Blob(Color c, double size, byte alpha, TranslateTransform t, HorizontalAlignment h, VerticalAlignment v, Thickness m) => new Ellipse
        {
            Width = size, Height = size, HorizontalAlignment = h, VerticalAlignment = v, Margin = m, RenderTransform = t, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0),
                new GradientStop(Color.FromArgb((byte)(alpha / 3), c.R, c.G, c.B), 0.45),
                new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)
            })
        };

        protected void SetBody(UIElement body)
        {
            var tg = new TransformGroup();
            tg.Children.Add(pop);
            tg.Children.Add(slide);

            var root = new Grid { ClipToBounds = true, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = tg };
            // koyu, derin cam tonu
            root.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(150, 14, 12, 34), Color.FromArgb(120, 4, 8, 22), 90) });
            // aurora: mor, camgobegi, pembe isik bulutlari yavasca suzulur
            root.Children.Add(Blob(Color.FromRgb(0x8A, 0x7C, 0xFF), 380, 120, auroraA, HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(-140, -170, 0, 0)));
            root.Children.Add(Blob(Color.FromRgb(0x4F, 0xD8, 0xFF), 320, 80, auroraB, HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -120, -120, 0)));
            root.Children.Add(Blob(Color.FromRgb(0xE0, 0x6C, 0xF0), 300, 55, auroraC, HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -110, -150)));
            // ince izgara dokusu (cok soluk)
            root.Children.Add(new Border { Background = GridBrush(), Opacity = 0.5, IsHitTestVisible = false });
            // ustte holografik cizgi + uzerinde kayan parilti
            var top = new Grid { Height = 1.2, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true, IsHitTestVisible = false };
            top.Children.Add(new Border { Background = Holo(150) });
            top.Children.Add(new Border
            {
                Width = 140, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = shimmer,
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                }, 0)
            });
            root.Children.Add(top);
            root.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = Holo(50), IsHitTestVisible = false });
            root.Children.Add(new Border { Padding = new Thickness(18), Child = body });
            Content = root;
        }

        static Brush GridBrush()
        {
            var g = new DrawingGroup();
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)), 1);
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 28, 28))));
            g.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(28, 0))));
            g.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(0, 28))));
            var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 28, 28), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            // izgara yukaridan asagi solsun
            return b;
        }

        public static LinearGradientBrush Holo(byte a = 255) => new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(0, 0x6F, 0xD6, 0xFF), 0),
            new GradientStop(Color.FromArgb(a, 0x6F, 0xD6, 0xFF), 0.2),
            new GradientStop(Color.FromArgb(a, 0x8A, 0x7C, 0xFF), 0.5),
            new GradientStop(Color.FromArgb(a, 0xE0, 0x6C, 0xF0), 0.8),
            new GradientStop(Color.FromArgb(0, 0xE0, 0x6C, 0xF0), 1)
        }, 0);

        public static LinearGradientBrush AccentGradient(double angle = 45) => new LinearGradientBrush(Color.FromRgb(0x5F, 0xC8, 0xFF), Color.FromRgb(0xA8, 0x6C, 0xF5), angle);

        void StartAmbient()
        {
            static void Drift(TranslateTransform t, double dx, double dy, double secs)
            {
                var e = new SineEase { EasingMode = EasingMode.EaseInOut };
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, TimeSpan.FromSeconds(secs)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = e });
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dy, TimeSpan.FromSeconds(secs * 1.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = e });
            }
            Drift(auroraA, 90, 50, 7);
            Drift(auroraB, -80, 60, 9);
            Drift(auroraC, -70, -60, 8);
            shimmer.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-160, ActualWidth + 20, TimeSpan.FromSeconds(3.2)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }

        void StopAmbient()
        {
            foreach (var t in new[] { auroraA, auroraB, auroraC })
            {
                t.BeginAnimation(TranslateTransform.XProperty, null);
                t.BeginAnimation(TranslateTransform.YProperty, null);
            }
            shimmer.BeginAnimation(TranslateTransform.XProperty, null);
        }

        /// <summary>HUD tarzi bolum basligi: parlayan nokta + aralikli BUYUK harfler.</summary>
        public static UIElement HudTitle(string text, out TextBlock label)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new Ellipse
            {
                Width = 6, Height = 6, Fill = AccentGradient(), VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x6F, 0xD6, 0xFF), BlurRadius = 8, ShadowDepth = 0, Opacity = 1 }
            });
            label = new TextBlock
            {
                Text = Spaced(text), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(185, 210, 220, 255)), Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(label);
            return sp;
        }
        public static UIElement HudTitle(string text) => HudTitle(text, out _);

        public static string Spaced(string s) => s.ToUpper(new System.Globalization.CultureInfo("tr-TR")); // mono yazi tipi zaten aralikli gorunur

        /// <summary>Elemanlar sirayla, asagidan yukari suzulerek belirir.</summary>
        public static void Stagger(System.Collections.IEnumerable items, int stepMs = 22, int startMs = 60)
        {
            int i = 0;
            foreach (var o in items)
            {
                if (!(o is UIElement el) || el.Visibility != Visibility.Visible) continue;
                var t = new TranslateTransform(0, 10);
                el.RenderTransform = el.RenderTransform is ScaleTransform st ? new TransformGroup { Children = { st, t } } : t;
                var begin = TimeSpan.FromMilliseconds(startMs + i * stepMs);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                el.Opacity = 0;
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = begin });
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(340)) { BeginTime = begin, EasingFunction = ease });
                i++;
            }
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
            ForceForeground();
            StartAmbient();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);

        /// <summary>Windows bazen arka plandaki programin one gelmesini engeller (orn. Windows tusuyla acilinca):
        /// on plandaki pencerenin girdi kuyruguna gecici baglanarak odagi al.</summary>
        void ForceForeground()
        {
            Activate();
            if (Native.SetForegroundWindow(hwnd) && Native.GetForegroundWindow() == hwnd) return;
            var fg = Native.GetForegroundWindow();
            uint fgThread = Native.GetWindowThreadProcessId(fg, out _), me = GetCurrentThreadId();
            if (fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true))
            {
                BringWindowToTop(hwnd);
                Native.SetForegroundWindow(hwnd);
                AttachThreadInput(me, fgThread, false);
            }
            Activate();
        }

        public void HideFlyout()
        {
            if (!IsVisible) return;
            hiddenAt = DateTime.Now;
            OnClosing();
            StopAmbient();
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
            var st = b.RenderTransform as ScaleTransform ?? (b.RenderTransform as TransformGroup)?.Children.OfType<ScaleTransform>().FirstOrDefault();
            if (st == null) return;
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
