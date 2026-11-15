using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WindowsNotch
{
    internal static class UIHelper
    {
        public static bool IsInteractive(DependencyObject? obj)
        {
            while (obj != null)
            {
                if (obj is ButtonBase ||
                    obj is Slider ||
                    obj is Thumb ||
                    obj is TextBox ||
                    obj is ScrollBar)
                {
                    return true;
                }

                obj = VisualTreeHelper.GetParent(obj);
            }

            return false;
        }
    }
}
