using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FloatingDock
{
    /// <summary>Winter Developer logosu: tacli "W". Baslat dugmesi, Kisisellestirme Merkezi ve duvar kagitlarinda kullanilir.</summary>
    static class WinterLogo
    {
        /// <summary>24x24 kutuda: ustte uc uclu tac, altta kalin W.</summary>
        public const string PathData =
            "M7.2 6.2 L8.6 2.6 L10.6 4.8 L12 1.6 L13.4 4.8 L15.4 2.6 L16.8 6.2 Z " +
            "M1.6 7.4 L6.4 7.4 L9 15.2 L12 8.6 L15 15.2 L17.6 7.4 L22.4 7.4 L17.2 22.4 L13.6 22.4 L12 18.2 L10.4 22.4 L6.8 22.4 Z";

        static Geometry geometry;
        public static Geometry Geometry
        {
            get
            {
                if (geometry == null) { geometry = System.Windows.Media.Geometry.Parse(PathData); geometry.Freeze(); }
                return geometry;
            }
        }

        public static Path Create(double size, Brush fill) => new Path
        {
            Data = Geometry, Fill = fill, Width = size, Height = size, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
    }
}
