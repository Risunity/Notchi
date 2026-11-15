using System.Windows.Media;

namespace WindowsNotch
{
    internal static class FrozenBrushes
    {
        public static SolidColorBrush Create(byte r, byte g, byte b)
        {
            return Create(Color.FromRgb(r, g, b));
        }

        public static SolidColorBrush Create(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
