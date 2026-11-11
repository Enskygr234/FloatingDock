using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FloatingDock
{
    /// <summary>Dock ikonunun ustunde acilan, uygulamanin pencerelerini canli gosteren cam panel.</summary>
    class PreviewWindow : Window
    {
        const double MaxThumbW = 220, MaxThumbH = 130;
        readonly Func<string, AppEntry> resolve;
        /// <summary>Dock hapinin ust kenari, fiziksel piksel.</summary>
        readonly Func<double> dockTop;
        readonly Action<IntPtr> activate;
        readonly Action afterChange;
        readonly Func<string, ImageSource> icon;
        IntPtr hwnd;
        double scale = 1;
        readonly List<IntPtr> thumbs = new List<IntPtr>();
        readonly List<(IntPtr thumb, FrameworkElement slot)> slots = new List<(IntPtr, FrameworkElement)>();
        readonly StackPanel cards = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };

        // hover takibi
        readonly DispatcherTimer showTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        readonly DispatcherTimer watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        string pendingKey, shownKey;
        FrameworkElement pendingAnchor, shownAnchor;
        int outsideTicks;

        public PreviewWindow(Func<string, AppEntry> resolve, Func<double> dockTop, Action<IntPtr> activate,
                             Func<string, ImageSource> icon, Action afterChange)
        {
            this.resolve = resolve; this.dockTop = dockTop; this.activate = activate; this.icon = icon; this.afterChange = afterChange;
            Title = "FloatingDockPreview";
            Glass.Setup(this);
            Content = new Border { Background = Theme.Brush(Theme.Tint), Child = cards };
            Width = 10; Height = 10;
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Glass.Apply(hwnd);
                scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            };
            showTimer.Tick += (s, e) =>
            {
                showTimer.Stop();
                if (pendingAnchor != null && CursorOver(pendingAnchor)) ShowFor(pendingKey, pendingAnchor);
            };
            watchTimer.Tick += (s, e) => Watch();
        }

        // ---- Dock'tan cagrilanlar ----
        public void HoverApp(string key, FrameworkElement anchor)
        {
            pendingKey = key; pendingAnchor = anchor;
            if (IsVisible) { showTimer.Stop(); if (key != shownKey) ShowFor(key, anchor); else shownAnchor = anchor; }
            else { showTimer.Stop(); showTimer.Start(); }
        }

        public void HoverOther()
        {
            pendingKey = null; pendingAnchor = null;
            showTimer.Stop();
        }

        public void HidePreview()
        {
            showTimer.Stop(); watchTimer.Stop();
            ClearThumbs();
            shownKey = null; shownAnchor = null;
            if (IsVisible) Hide();
        }

        // ---- Gosterim ----
        void ShowFor(string key, FrameworkElement anchor)
        {
            var entry = resolve(key);
            if (entry == null || entry.Windows.Count == 0) { HidePreview(); return; }
            if (hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
            if (hwnd == IntPtr.Zero) hwnd = new WindowInteropHelper(this).Handle;

            ClearThumbs();
            cards.Children.Clear();
            var appIcon = icon(entry.IconPath);
            foreach (var w in entry.Windows)
            {
                Native.DwmRegisterThumbnail(hwnd, w, out IntPtr thumb);
                double tw = MaxThumbW, th = MaxThumbH;
                if (thumb != IntPtr.Zero)
                {
                    thumbs.Add(thumb);
                    Native.DwmQueryThumbnailSourceSize(thumb, out var sz);
                    if (sz.cx > 0 && sz.cy > 0)
                    {
                        double k = Math.Min(MaxThumbW / sz.cx, MaxThumbH / sz.cy);
                        tw = Math.Max(60, Math.Round(sz.cx * k)); th = Math.Max(40, Math.Round(sz.cy * k));
                    }
                }
                var slot = new Grid { Width = tw, Height = th, Margin = new Thickness(0, 6, 0, 0) };
                slot.Children.Add(new Border { CornerRadius = new CornerRadius(Theme.RadiusSm), Background = Theme.Ink((byte)(Theme.SurfaceIdle + 6)) });
                slot.Children.Add(new Image { Source = appIcon, Width = 32, Height = 32, Opacity = 0.6 }); // kucultulmus pencereler icin yedek
                cards.Children.Add(MakeCard(w, appIcon, slot, tw));
                if (thumb != IntPtr.Zero) slots.Add((thumb, slot));
            }

            // boyut ve konum
            cards.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Width = Math.Ceiling(cards.DesiredSize.Width);
            Height = Math.Ceiling(cards.DesiredSize.Height);
            // konum: ikonun bulundugu ekranda, fiziksel pikselle (ekranlarin DPI'i farkli olabilir)
            var p = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
            var scr = Screens.FromPoint((int)p.X, (int)p.Y);

            bool wasVisible = IsVisible;
            if (!wasVisible) { Opacity = 0; Show(); }
            Screens.Place(this, scr, sc =>
            {
                scale = sc;
                double w = Width * sc, h = Height * sc, gap = 8 * sc;
                double left = Math.Max(scr.Bounds.left + gap, Math.Min(scr.Bounds.right - w - gap, p.X - w / 2));
                return (left, dockTop() - h - 10 * sc);
            });
            UpdateLayout();
            PlaceThumbs();
            if (!wasVisible) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

            shownKey = key; shownAnchor = anchor; outsideTicks = 0;
            watchTimer.Start();
        }

        FrameworkElement MakeCard(IntPtr win, ImageSource appIcon, FrameworkElement slot, double width)
        {
            var title = new StringBuilder(256);
            Native.GetWindowText(win, title, 256);

            var header = new DockPanel { Height = 22, LastChildFill = true };
            var close = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, ToolTip = "Kapat" };
            close.Child = new TextBlock
            {
                Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10,
                Foreground = Theme.Fg(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            close.MouseEnter += (s, e) => close.Background = Theme.Brush(Theme.DangerFill);
            close.MouseLeave += (s, e) => close.Background = Brushes.Transparent;
            close.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Native.PostMessage(win, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                HidePreview();
                afterChange();
            };
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
            var ic = new Image { Source = appIcon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
            RenderOptions.SetBitmapScalingMode(ic, BitmapScalingMode.HighQuality);
            DockPanel.SetDock(ic, Dock.Left);
            header.Children.Add(ic);
            header.Children.Add(new TextBlock
            {
                Text = title.ToString(), Foreground = Theme.Fg(), FontSize = 12,
                FontFamily = Theme.Ui,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            });

            var stack = new StackPanel { Width = width };
            stack.Children.Add(header);
            stack.Children.Add(slot);
            var card = new Border
            {
                Child = stack, Padding = new Thickness(8), Margin = new Thickness(2),
                CornerRadius = new CornerRadius(8), Background = Brushes.Transparent
            };
            card.MouseEnter += (s, e) => card.Background = Theme.Ink(Theme.SurfaceHover);
            card.MouseLeave += (s, e) => card.Background = Brushes.Transparent;
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (e.Handled) return;
                activate(win);
                HidePreview();
                afterChange();
            };
            return card;
        }

        void PlaceThumbs()
        {
            if (!(Content is Visual root)) return;
            foreach (var (thumb, slot) in slots)
            {
                var tl = slot.TransformToAncestor(root).Transform(new Point(0, 0));
                var props = new Native.DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = 0x1 | 0x4 | 0x8 | 0x10, // RECTDESTINATION | OPACITY | VISIBLE | SOURCECLIENTAREAONLY
                    rcDestination = new Native.RECT
                    {
                        left = (int)Math.Round(tl.X * scale),
                        top = (int)Math.Round(tl.Y * scale),
                        right = (int)Math.Round((tl.X + slot.ActualWidth) * scale),
                        bottom = (int)Math.Round((tl.Y + slot.ActualHeight) * scale)
                    },
                    opacity = 255, fVisible = true, fSourceClientAreaOnly = false
                };
                Native.DwmUpdateThumbnailProperties(thumb, ref props);
            }
        }

        void ClearThumbs()
        {
            foreach (var t in thumbs) Native.DwmUnregisterThumbnail(t);
            thumbs.Clear(); slots.Clear();
        }

        // Fare ne ikonda ne panelde ise kisa bir sure sonra kapat
        void Watch()
        {
            bool inside = CursorOver(this) || (shownAnchor != null && CursorOver(shownAnchor))
                          || (pendingAnchor != null && pendingKey == shownKey && CursorOver(pendingAnchor));
            if (inside) { outsideTicks = 0; return; }
            if (++outsideTicks >= 3) HidePreview();
        }

        bool CursorOver(FrameworkElement el)
        {
            try
            {
                if (!el.IsVisible || PresentationSource.FromVisual(el) == null) return false;
                Native.GetCursorPos(out var c);
                var tl = el.PointToScreen(new Point(0, 0));
                var br = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
                // ikon ile panel arasindaki bosluktan gecis icin ust kenara pay birak
                return c.x >= tl.X && c.x <= br.X && c.y >= tl.Y - 14 * scale && c.y <= br.Y + 14 * scale;
            }
            catch { return false; }
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            scale = newDpi.DpiScaleX;
        }
    }
}
