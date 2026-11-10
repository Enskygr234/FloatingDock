using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FloatingDock
{
    /// <summary>Dock'taki ˄ dugmesinin actigi panel: arka plan uygulamalarinin tepsi simgeleri.</summary>
    class TrayFlyout : Flyout
    {
        readonly WrapPanel grid = new WrapPanel { Orientation = Orientation.Horizontal };
        readonly TextBlock empty = Text(L.T("Arka planda simgesi olan uygulama yok", "No background apps with icons"), Theme.Caption, Theme.TextTertiary);

        public TrayFlyout() : base("FloatingDockTray")
        {
            Width = 6 * 44 + 2 * Theme.Pad + 4;
            var root = new StackPanel();
            root.Children.Add(SectionTitle(L.T("Arka plan uygulamaları", "Background apps")));
            root.Children.Add(new Border { Height = 8 });
            empty.Margin = new Thickness(2, 4, 0, 4);
            empty.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(empty);
            root.Children.Add(grid);
            SetBody(root);
            TrayHost.Changed += () => { if (IsVisible) Render(); };
        }

        protected override void OnOpening() => Render();

        void Render()
        {
            grid.Children.Clear();
            var icons = TrayHost.Icons.Where(i => !i.Hidden && i.Image != null).ToList();
            empty.Visibility = icons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var i in icons) grid.Children.Add(Item(i));
        }

        UIElement Item(TrayIcon icon)
        {
            var img = new Image { Source = icon.Image, Width = 20, Height = 20 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var box = new Border
            {
                Width = 40, Height = 40, Margin = new Thickness(2), CornerRadius = new CornerRadius(Theme.RadiusMd),
                Background = Theme.Ink(0), Child = img, Cursor = Cursors.Hand,
                ToolTip = string.IsNullOrWhiteSpace(icon.Tip) ? null : icon.Tip
            };
            System.Windows.Automation.AutomationProperties.SetName(box, string.IsNullOrWhiteSpace(icon.Tip) ? L.T("Tepsi simgesi", "Tray icon") : icon.Tip);
            box.MouseEnter += (s, e) => { box.Background = Theme.Ink(Theme.SurfaceHover); TrayHost.Hover(icon); };
            box.MouseLeave += (s, e) => box.Background = Theme.Ink(0);
            box.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                box.Background = Theme.Ink(Theme.SurfacePressed);
                if (e.ClickCount == 2) { HideFlyout(); TrayHost.DoubleClick(icon); }
            };
            box.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (!IsVisible) return;
                box.Background = Theme.Ink(Theme.SurfaceHover);
                // panel acik kalir: eski uygulamalar cift tiklamayla acilir. Uygulama one gelince panel kendiliginden kapanir.
                TrayHost.LeftClick(icon);
            };
            box.MouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                HideFlyout();
                TrayHost.RightClick(icon);
            };
            return box;
        }
    }
}
