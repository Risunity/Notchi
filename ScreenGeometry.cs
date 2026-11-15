using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WindowsNotch
{
    internal static class ScreenGeometry
    {
        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public static bool TryGetCursor(
            Window window,
            out Point physical,
            out Point dip)
        {
            if (!GetCursorPos(out var point))
            {
                physical = default;
                dip = default;
                return false;
            }

            var (scaleX, scaleY) = GetDpiScale(window);
            physical = new Point(point.X, point.Y);
            dip = new Point(point.X / scaleX, point.Y / scaleY);
            return true;
        }

        public static Rect GetWorkArea(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return SystemParameters.WorkArea;

            return GetWorkArea(
                window,
                MonitorFromWindow(hwnd, MonitorDefaultToNearest));
        }

        public static Rect GetWorkAreaFromCursor(Window window)
        {
            if (!GetCursorPos(out var point)) return GetWorkArea(window);

            return GetWorkArea(
                window,
                MonitorFromPoint(point, MonitorDefaultToNearest));
        }

        public static Rect GetWorkAreaFromPoint(Window window, Point physical)
        {
            var point = new NativePoint
            {
                X = (int)physical.X,
                Y = (int)physical.Y
            };

            return GetWorkArea(
                window,
                MonitorFromPoint(point, MonitorDefaultToNearest));
        }

        private static Rect GetWorkArea(Window window, IntPtr monitor)
        {
            if (monitor == IntPtr.Zero) return SystemParameters.WorkArea;

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;

            var (scaleX, scaleY) = GetDpiScale(window);

            return new Rect(
                info.Work.Left / scaleX,
                info.Work.Top / scaleY,
                (info.Work.Right - info.Work.Left) / scaleX,
                (info.Work.Bottom - info.Work.Top) / scaleY);
        }

        private static (double X, double Y) GetDpiScale(Window window)
        {
            double scaleX = 1;
            double scaleY = 1;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi > 0)
                {
                    scaleX = dpi / 96.0;
                    scaleY = scaleX;
                }
            }

            if (scaleX <= 0 || scaleX == 1)
            {
                var visual = VisualTreeHelper.GetDpi(window);
                if (visual.DpiScaleX > 0) scaleX = visual.DpiScaleX;
                if (visual.DpiScaleY > 0) scaleY = visual.DpiScaleY;
            }

            if (scaleX <= 0) scaleX = 1;
            if (scaleY <= 0) scaleY = 1;

            return (scaleX, scaleY);
        }
    }
}
