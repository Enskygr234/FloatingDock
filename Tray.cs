using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ManagedShell;
using ManagedShell.WindowsTray;

namespace FloatingDock
{
    /// <summary>
    /// Sistem tepsisi sunucusu (ManagedShell). Uygulamalar Shell_NotifyIcon ile ikonlarini bize kaydeder;
    /// dock kapaninca "TaskbarCreated" yayiniyla ikonlar Windows gorev cubuguna geri doner.
    /// </summary>
    static class Tray
    {
        static ShellManager shell;
        public static bool Running => shell != null && !shell.NotificationArea.IsFailed;
        public static NotificationArea Area => shell?.NotificationArea;
        /// <summary>Eski tip tepsi balonu (NIF_INFO) geldi: adada gosterilir.</summary>
        public static event Action<NotificationBalloon> Balloon;
        /// <summary>Ikon listesi degisti (UI is parcaciginda).</summary>
        public static event Action Changed;

        public static void Start()
        {
            if (shell != null) return;
            try
            {
                var cfg = ShellManager.DefaultShellConfig;
                cfg.EnableTasksService = false;
                cfg.AutoStartTasksService = false;
                cfg.EnableTrayService = true;
                cfg.AutoStartTrayService = true;
                shell = new ShellManager(cfg);
                shell.NotificationArea.NotificationBalloonShown += (s, e) => { if (e?.Balloon != null) Balloon?.Invoke(e.Balloon); };
                shell.NotificationArea.TrayIcons.CollectionChanged += OnCollectionChanged;
            }
            catch (Exception ex) { Program.Log(ex); shell = null; }
        }

        static void OnCollectionChanged(object s, NotifyCollectionChangedEventArgs e)
        {
            // gizli/gorunur gecisleri de yakala
            if (e.NewItems != null) foreach (NotifyIcon i in e.NewItems) i.PropertyChanged += OnIconChanged;
            Changed?.Invoke();
        }

        static void OnIconChanged(object s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NotifyIcon.IsHidden)) Changed?.Invoke();
        }

        /// <summary>Gorunur ikonlar (uygulamanin gizledikleri haric).</summary>
        public static List<NotifyIcon> Icons()
        {
            try { return Area?.TrayIcons.Where(i => !i.IsHidden && i.Icon != null).ToList() ?? new List<NotifyIcon>(); }
            catch { return new List<NotifyIcon>(); }
        }

        public static void Stop()
        {
            var s = shell;
            shell = null;
            if (s == null) return;
            try { s.Dispose(); } catch (Exception ex) { Program.Log(ex); }
            // ikonlar Windows'un kendi tepsisine yeniden kaydolsun
            Native.BroadcastTaskbarCreated();
        }

        /// <summary>Imlec konumu, Shell_NotifyIcon geri cagrisinin bekledigi bicimde (MAKELPARAM, fiziksel piksel).</summary>
        public static uint CursorParam()
        {
            Native.GetCursorPos(out var p);
            return (uint)(((p.y & 0xFFFF) << 16) | (p.x & 0xFFFF));
        }
    }

    /// <summary>Dock'taki "⌃" dugmesinin actigi panel: arka planda calisan uygulamalarin tepsi ikonlari.</summary>
    class TrayFlyout : Flyout
    {
        const double Cell = 44;
        readonly WrapPanel grid = new WrapPanel();
        readonly TextBlock empty = Text("Arka planda çalışan uygulama yok", Theme.Body, Theme.TextTertiary);

        public TrayFlyout() : base("FloatingDockTray")
        {
            Width = 6 * (Cell + 4) + 2 * Theme.Pad + 2;
            var root = new StackPanel();
            var title = SectionTitle("Arka plan uygulamaları");
            ((FrameworkElement)title).Margin = new Thickness(2, 0, 0, 8);
            root.Children.Add(title);
            empty.Margin = new Thickness(2, 4, 0, 6);
            root.Children.Add(empty);
            root.Children.Add(grid);
            var hint = Text("Tıkla: aç · Sağ tık: uygulamanın menüsü", Theme.Caption, Theme.TextTertiary);
            hint.Margin = new Thickness(2, 10, 0, 0);
            root.Children.Add(hint);
            SetBody(root);
            Tray.Changed += () => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) Render(); }));
        }

        protected override void OnOpening() => Render();

        void Render()
        {
            grid.Children.Clear();
            var icons = Tray.Icons();
            empty.Text = Tray.Running ? "Arka planda çalışan uygulama yok" : "Sistem tepsisi başlatılamadı";
            empty.Visibility = icons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var icon in icons) grid.Children.Add(MakeCell(icon));
        }

        FrameworkElement MakeCell(NotifyIcon icon)
        {
            var img = new Image { Width = 20, Height = 20 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            img.SetBinding(Image.SourceProperty, new Binding(nameof(NotifyIcon.Icon)) { Source = icon, Mode = BindingMode.OneWay });
            var cell = new Border
            {
                Width = Cell, Height = Cell, Margin = new Thickness(2), CornerRadius = new CornerRadius(Theme.RadiusMd),
                Background = Theme.Ink(0), Child = img, Cursor = Cursors.Hand
            };
            var tip = new ToolTip();
            tip.SetBinding(ContentProperty, new Binding(nameof(NotifyIcon.Title)) { Source = icon, Mode = BindingMode.OneWay });
            cell.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(cell, icon.Title ?? "Tepsi ikonu");
            int dbl = (int)Native.GetDoubleClickTime();

            cell.MouseEnter += (s, e) =>
            {
                cell.Background = Theme.Ink(Theme.SurfaceHover);
                // uygulamalar menulerini ikonun konumuna gore acar (Shell_NotifyIconGetRect)
                try
                {
                    var tl = cell.PointToScreen(new Point(0, 0));
                    var br = cell.PointToScreen(new Point(cell.ActualWidth, cell.ActualHeight));
                    icon.Placement = new ManagedShell.Interop.NativeMethods.Rect { Left = (int)tl.X, Top = (int)tl.Y, Right = (int)br.X, Bottom = (int)br.Y };
                }
                catch { }
                Safe(() => icon.IconMouseEnter(Tray.CursorParam()));
            };
            cell.MouseLeave += (s, e) => { cell.Background = Theme.Ink(0); Safe(() => icon.IconMouseLeave(Tray.CursorParam())); };
            cell.MouseMove += (s, e) => Safe(() => icon.IconMouseMove(Tray.CursorParam()));
            cell.MouseDown += (s, e) => { e.Handled = true; cell.Background = Theme.Ink(Theme.SurfacePressed); Safe(() => icon.IconMouseDown(e.ChangedButton, Tray.CursorParam(), dbl)); };
            cell.MouseUp += (s, e) =>
            {
                e.Handled = true;
                cell.Background = Theme.Ink(Theme.SurfaceHover);
                Safe(() => icon.IconMouseUp(e.ChangedButton, Tray.CursorParam(), dbl));
            };
            return cell;
        }

        static void Safe(Action a) { try { a(); } catch (Exception ex) { Program.Log(ex); } }
    }
}
