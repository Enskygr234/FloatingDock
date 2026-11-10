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
        public static readonly Color Accent = Theme.Accent;
        public static readonly FontFamily UiFont = Theme.Ui;
        public static readonly FontFamily IconFont = Theme.Icons;
        protected IntPtr hwnd;
        protected double scale = 1;
        readonly ScaleTransform pop = new ScaleTransform(1, 1);
        readonly TranslateTransform slide = new TranslateTransform();
        DateTime hiddenAt = DateTime.MinValue;
        double anchorBottom;

        protected Flyout(string title)
        {
            Title = title;
            Glass.Setup(this);
            SizeToContent = SizeToContent.Height;
            Width = 360;
            FontFamily = Theme.Ui;
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Glass.ApplyFocusable(hwnd);
                scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            };
            Deactivated += (s, e) => HideFlyout();
            // icerik yuksekligi degisirse alt kenar dock'un ustunde sabit kalsin
            SizeChanged += (s, e) => { if (IsVisible && anchorBottom > 0 && e.HeightChanged) Top = Math.Round(anchorBottom - ActualHeight); };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) HideFlyout(); };
        }

        /// <summary>Panel govdesi: akrilik cam + koyu ton + ust kenarda ince isik cizgisi. Dekoratif hareket yok.</summary>
        protected void SetBody(UIElement body)
        {
            var tg = new TransformGroup();
            tg.Children.Add(pop);
            tg.Children.Add(slide);

            var root = new Grid { ClipToBounds = true, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = tg };
            root.Children.Add(new Border { Background = Theme.Brush(Theme.PanelTint) });
            // camin ust kenarinda cok hafif isik: derinlik hissi verir, dikkat cekmez
            root.Children.Add(new Border
            {
                Height = 1, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(40, 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                }, 0)
            });
            root.Children.Add(new Border { Padding = new Thickness(Theme.Pad), Child = body });
            // Windows 10 pencere kenarligi cizmez: ince kenar cizgisini kendimiz ekleriz
            if (!Glass.IsWin11) root.Children.Add(new Border { BorderBrush = Theme.Ink(Theme.StrokeDefault), BorderThickness = new Thickness(1), IsHitTestVisible = false });
            Content = root;
        }

        /// <summary>Sade bolum basligi: cumle duzeninde, yari kalin, ikincil renk.</summary>
        public static UIElement SectionTitle(string text, out TextBlock label)
        {
            label = Text(text, Theme.Body, Theme.TextSecondary, FontWeights.SemiBold);
            return label;
        }
        public static UIElement SectionTitle(string text) => SectionTitle(text, out _);

        /// <summary>Vurgu dolgusu (eski degrade cagrilariyla uyumlu; artik duz renk).</summary>
        public static Brush AccentFill() => Theme.Brush(Theme.Accent);

        /// <summary>Elemanlar sirayla, hafifce yukari kayarak belirir (azaltilmis harekette aninda).</summary>
        public static void Stagger(System.Collections.IEnumerable items, int stepMs = 16, int startMs = 40)
        {
            if (!Theme.Motion) return;
            int i = 0;
            foreach (var o in items)
            {
                if (!(o is UIElement el) || el.Visibility != Visibility.Visible) continue;
                var t = new TranslateTransform(0, 6);
                el.RenderTransform = el.RenderTransform is ScaleTransform st ? new TransformGroup { Children = { st, t } } : t;
                var begin = TimeSpan.FromMilliseconds(startMs + Math.Min(i, 14) * stepMs);
                el.Opacity = 0;
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Theme.Normal) { BeginTime = begin });
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, Theme.Slow) { BeginTime = begin, EasingFunction = Theme.EaseOut });
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
            // panel, dugmenin bulundugu ekranin icinde kalsin (birden cok monitor)
            var (mon, _) = Native.MonitorAt((int)anchorCenterPx, (int)(dockTopDip * scale) - 4);
            double monL = mon.left / scale, monR = mon.right / scale;
            double left = anchorCenterPx / scale - ActualWidth / 2;
            Left = Math.Round(Math.Max(monL + 10, Math.Min(monR - ActualWidth - 10, left)));
            anchorBottom = dockTopDip - 10;
            Top = Math.Round(anchorBottom - ActualHeight);
            ForceForeground();
            var ease = Theme.EaseOut;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Theme.D(Theme.Fast)));
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Theme.Motion ? 12 : 0, 0, Theme.D(Theme.Slow)) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(Theme.Motion ? 0.98 : 1, 1, Theme.D(Theme.Slow)) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(Theme.Motion ? 0.98 : 1, 1, Theme.D(Theme.Slow)) { EasingFunction = ease });
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
            BeginAnimation(OpacityProperty, null);
            Hide();
        }

        // ================= ortak kontroller =================
        public static TextBlock Text(string t, double size = Theme.Body, byte? alpha = null, FontWeight? w = null) => new TextBlock
        {
            Text = t,
            FontSize = size,
            FontFamily = Theme.Ui,
            FontWeight = w ?? FontWeights.Normal,
            Foreground = Theme.Fg(alpha ?? Theme.TextPrimary),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        public static new TextBlock Icon(string g, double size = 16, byte? alpha = null) => new TextBlock
        {
            Text = g,
            FontFamily = Theme.Icons,
            FontSize = size,
            Foreground = Theme.Fg(alpha ?? Theme.TextPrimary),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        /// <summary>Uzerine gelince aydinlanan, basinca hafifce kuculen kutu. Klavyeyle de (Tab + Enter/Bosluk) kullanilabilir.</summary>
        public static Border Clickable(UIElement child, Action click, double radius = Theme.RadiusMd, byte idle = 0, byte? hoverAlpha = null)
        {
            byte hover = hoverAlpha ?? Theme.SurfaceHover;
            var b = new Border
            {
                CornerRadius = new CornerRadius(radius),
                Background = Theme.Ink(idle),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Child = child,
                Cursor = Cursors.Hand,
                Focusable = true,
                FocusVisualStyle = null,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            b.Tag = idle;
            void Fade(byte a)
            {
                if (!(b.Background is SolidColorBrush sb) || sb.IsFrozen || !Theme.IsInk(sb.Color)) return;
                var ink = Theme.InkBase;
                sb.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(a, ink.R, ink.G, ink.B), Theme.D(Theme.Fast)));
            }
            b.MouseEnter += (s, e) => Fade(hover);
            b.MouseLeave += (s, e) => { Fade((byte)b.Tag); Press(b, 1); };
            b.MouseLeftButtonDown += (s, e) => { Press(b, 0.97); e.Handled = true; };
            b.MouseLeftButtonUp += (s, e) => { Press(b, 1); e.Handled = true; click?.Invoke(); };
            // klavye odagi: gorunur vurgu halkasi
            Brush keep = null;
            b.GotKeyboardFocus += (s, e) => { keep = b.BorderBrush; b.BorderBrush = Theme.Brush(Theme.Accent); };
            b.LostKeyboardFocus += (s, e) => { if (keep != null) b.BorderBrush = keep; };
            b.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; click?.Invoke(); } };
            return b;
        }

        static void Press(Border b, double to)
        {
            var st = b.RenderTransform as ScaleTransform ?? (b.RenderTransform as TransformGroup)?.Children.OfType<ScaleTransform>().FirstOrDefault();
            if (st == null) return;
            var a = new DoubleAnimation(to, Theme.D(Theme.Fast)) { EasingFunction = Theme.EaseOut };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        /// <summary>Ana dugme: duz buz mavisi, koyu yazi.</summary>
        public static Border PrimaryButton(string text, Action click)
        {
            var b = Clickable(new TextBlock
            {
                Text = text, FontFamily = Theme.Ui, FontSize = Theme.Body, FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Brush(Theme.OnAccent), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 6, 16, 7)
            }, click, Theme.RadiusMd);
            var fill = new SolidColorBrush(Theme.Accent);
            b.Background = fill;
            b.MinHeight = 32;
            b.MouseEnter += (s, e) => fill.Color = Theme.AccentHover;
            b.MouseLeave += (s, e) => fill.Color = Theme.Accent;
            return b;
        }

        /// <summary>Ikincil dugme: cam yuzey + ince kenar.</summary>
        public static Border SecondaryButton(string text, Action click)
        {
            var b = Clickable(new TextBlock
            {
                Text = text, FontFamily = Theme.Ui, FontSize = Theme.Body, Foreground = Theme.Fg(),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(14, 6, 14, 7)
            }, click, Theme.RadiusMd, (byte)(Theme.SurfaceIdle + 6), (byte)(Theme.SurfaceHover + 8));
            b.BorderBrush = Theme.Ink(Theme.StrokeSubtle);
            b.MinHeight = 32;
            return b;
        }

        /// <summary>Ikon + metin satiri seklinde sade baglanti dugmesi (paneller altindaki "... ayarlari").</summary>
        public static Border LinkButton(string glyph, string text, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 7, 12, 7) };
            sp.Children.Add(Icon(glyph, 14, Theme.TextSecondary));
            var t = Text(text, Theme.Body, Theme.TextSecondary);
            t.Margin = new Thickness(10, 0, 0, 0);
            sp.Children.Add(t);
            var b = Clickable(sp, click);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            return b;
        }

        /// <summary>Yalniz ikonlu kare dugme (min. 32x32 tiklama alani + ipucu).</summary>
        public static Border IconButton(string glyph, string tip, Action click, double size = 32, double glyphSize = 14)
        {
            var b = Clickable(new Border { Width = size, Height = size, Child = Icon(glyph, glyphSize, Theme.TextSecondary) }, click, Theme.RadiusMd);
            b.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            return b;
        }

        /// <summary>Ince ayirici cizgi.</summary>
        public static Border Divider(double top = 12, double bottom = 12) => new Border
        {
            Height = 1, Margin = new Thickness(0, top, 0, bottom), Background = Theme.Ink(Theme.StrokeSubtle), IsHitTestVisible = false
        };

        /// <summary>Bolumlu secici (Sistem | Koyu | Acik gibi). Secili bolum vurgu rengiyle dolar.</summary>
        public static UIElement Segmented(string[] labels, int selected, Action<int> changed)
        {
            var host = new Border
            {
                CornerRadius = new CornerRadius(Theme.RadiusMd), Background = Theme.Ink(Theme.SurfaceIdle),
                BorderBrush = Theme.Ink(Theme.StrokeSubtle), BorderThickness = new Thickness(1), Padding = new Thickness(2)
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            host.Child = sp;
            var parts = new Border[labels.Length];
            void Paint()
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    bool on = i == selected;
                    parts[i].Background = on ? Theme.Brush(Theme.Accent) : (Brush)Theme.Ink(0);
                    parts[i].Tag = (byte)0;
                    ((TextBlock)parts[i].Child).Foreground = on ? Theme.Brush(Theme.OnAccent) : Theme.Fg(Theme.TextSecondary);
                    ((TextBlock)parts[i].Child).FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var t = new TextBlock { Text = labels[i], FontFamily = Theme.Ui, FontSize = Theme.Caption + 0.5, Margin = new Thickness(10, 5, 10, 6), HorizontalAlignment = HorizontalAlignment.Center };
                parts[i] = Clickable(t, () => { if (selected == idx) return; selected = idx; Paint(); changed(idx); }, Theme.RadiusSm);
                System.Windows.Automation.AutomationProperties.SetName(parts[i], labels[i]);
                sp.Children.Add(parts[i]);
            }
            Paint();
            return host;
        }

        /// <summary>Gruplanmis icerik icin ince kenarli kart.</summary>
        public static Border Card(UIElement child, double padding = 4) => new Border
        {
            CornerRadius = new CornerRadius(Theme.RadiusLg),
            Background = Theme.Ink(6),
            BorderBrush = Theme.Ink(Theme.StrokeSubtle),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(padding),
            Child = child
        };
    }

    /// <summary>Win11 tarzi acma-kapama anahtari.</summary>
    class Toggle : Grid
    {
        readonly Border track = new Border { Width = 40, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        readonly Ellipse knob = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0) };
        readonly TranslateTransform move = new TranslateTransform();
        bool on;
        public event Action<bool> Changed;

        public Toggle(string name = null)
        {
            name ??= L.T("Aç / kapat", "On / off");
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            Focusable = true;
            knob.RenderTransform = move;
            Children.Add(track);
            Children.Add(knob);
            Apply(false);
            MouseLeftButtonUp += (s, e) => { e.Handled = true; Flip(); };
            MouseLeftButtonDown += (s, e) => e.Handled = true;
            KeyDown += (s, e) => { if (e.Key == Key.Space || e.Key == Key.Enter) { e.Handled = true; Flip(); } };
            System.Windows.Automation.AutomationProperties.SetName(this, name);
        }

        void Flip() { IsOn = !on; Changed?.Invoke(on); }

        public bool IsOn { get => on; set { if (value != on) { on = value; Apply(true); } } }

        void Apply(bool animate)
        {
            track.Background = on ? Theme.Brush(Theme.Accent) : Theme.Ink(8);
            track.BorderBrush = on ? Brushes.Transparent : (Brush)Theme.Ink(Theme.TextTertiary);
            knob.Fill = on ? Theme.Brush(Theme.OnAccent) : Theme.Ink(Theme.TextSecondary);
            var a = new DoubleAnimation(on ? 20 : 0, animate ? Theme.D(Theme.Normal) : new Duration(TimeSpan.Zero)) { EasingFunction = Theme.EaseOut };
            move.BeginAnimation(TranslateTransform.XProperty, a);
        }
    }

    /// <summary>Ince dolgulu kaydirici (0..1).</summary>
    class GlowSlider : Grid
    {
        readonly Border track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Ink(40), VerticalAlignment = VerticalAlignment.Center };
        readonly Border fill = new Border
        {
            Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Background = Theme.Brush(Theme.Accent)
        };
        readonly Grid thumb = new Grid { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        readonly Ellipse dot = new Ellipse { Width = 10, Height = 10, Fill = Theme.Brush(Theme.Accent) };
        readonly ScaleTransform dotScale = new ScaleTransform(1, 1);
        double value;
        bool drag;
        public event Action<double> ValueChanged;

        public GlowSlider(string name = null)
        {
            name ??= L.T("Ses düzeyi", "Volume");
            Height = 28;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            Focusable = true;
            // Win11 tarzi: koyu halka icinde vurgu noktasi, uzerine gelince buyur
            thumb.Children.Add(new Ellipse { Fill = Theme.Brush(Theme.Raised), Stroke = Theme.Ink(Theme.StrokeDefault), StrokeThickness = 1 });
            dot.RenderTransformOrigin = new Point(0.5, 0.5);
            dot.RenderTransform = dotScale;
            thumb.Children.Add(dot);
            Children.Add(track); Children.Add(fill); Children.Add(thumb);
            SizeChanged += (s, e) => Layout();
            MouseEnter += (s, e) => Grow(1.25);
            MouseLeave += (s, e) => { if (!drag) Grow(1); };
            MouseLeftButtonDown += (s, e) => { drag = true; Focus(); CaptureMouse(); SetFrom(e.GetPosition(this).X); Grow(0.85); e.Handled = true; };
            MouseMove += (s, e) => { if (drag) SetFrom(e.GetPosition(this).X); };
            MouseLeftButtonUp += (s, e) => { drag = false; ReleaseMouseCapture(); Grow(IsMouseOver ? 1.25 : 1); e.Handled = true; };
            MouseWheel += (s, e) => { Step(e.Delta > 0 ? 0.02 : -0.02); e.Handled = true; };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Right || e.Key == Key.Up) { Step(0.02); e.Handled = true; }
                else if (e.Key == Key.Left || e.Key == Key.Down) { Step(-0.02); e.Handled = true; }
            };
            System.Windows.Automation.AutomationProperties.SetName(this, name);
        }

        void Step(double d) { Value = value + d; ValueChanged?.Invoke(value); }

        void Grow(double s)
        {
            var a = new DoubleAnimation(s, Theme.D(Theme.Fast)) { EasingFunction = Theme.EaseOut };
            dotScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            dotScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
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
