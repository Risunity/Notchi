using System;
using System.Windows;
using System.Windows.Input;

namespace WindowsNotch
{
    public sealed class WindowDragHelper
    {
        private const double SnapThreshold = 24.0;
        private const double SnapMargin = 8.0;

        private readonly Window _window;
        private readonly UIElement _dragElement;
        private readonly Func<DependencyObject?, bool>? _isInteractive;

        private bool _isDragging;
        private Point _startCursorDip;
        private Point _startWindowPos;

        public event EventHandler? DragEnded;

        public WindowDragHelper(Window window, UIElement dragElement, Func<DependencyObject?, bool>? isInteractive = null)
        {
            _window = window;
            _dragElement = dragElement;
            _isInteractive = isInteractive;

            _dragElement.MouseLeftButtonDown += OnMouseDown;
            _dragElement.MouseMove += OnMouseMove;
            _dragElement.MouseLeftButtonUp += OnMouseUp;
            _dragElement.LostMouseCapture += OnLostCapture;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (_isInteractive != null && _isInteractive(e.OriginalSource as DependencyObject)) return;

            if (!GetCursorPosDip(out var cursorDip)) return;

            _isDragging = true;
            _startCursorDip = cursorDip;
            _startWindowPos = new Point(_window.Left, _window.Top);

            _dragElement.CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag();
                return;
            }

            if (!ScreenGeometry.TryGetCursor(
                    _window,
                    out var rawCursorPoint,
                    out var cursorDip)) return;

            double deltaX = cursorDip.X - _startCursorDip.X;
            double deltaY = cursorDip.Y - _startCursorDip.Y;

            double targetLeft = _startWindowPos.X + deltaX;
            double targetTop = _startWindowPos.Y + deltaY;

            Rect workArea = ScreenGeometry.GetWorkAreaFromPoint(
                _window,
                rawCursorPoint);

            ApplySnapping(ref targetLeft, ref targetTop, _window.Width, _window.Height, workArea);

            _window.Left = targetLeft;
            _window.Top = targetTop;
            e.Handled = true;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                EndDrag();
                e.Handled = true;
            }
        }

        private void OnLostCapture(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                EndDrag();
            }
        }

        private void EndDrag()
        {
            if (!_isDragging) return;

            _isDragging = false;
            if (_dragElement.IsMouseCaptured)
            {
                _dragElement.ReleaseMouseCapture();
            }

            DragEnded?.Invoke(_window, EventArgs.Empty);
        }

        private static void ApplySnapping(ref double left, ref double top, double width, double height, Rect workArea)
        {
            double right = left + width;
            double bottom = top + height;

            // 1. Tepi kiri layar
            if (Math.Abs(left - (workArea.Left + SnapMargin)) <= SnapThreshold)
            {
                left = workArea.Left + SnapMargin;
            }
            else if (Math.Abs(left - workArea.Left) <= SnapThreshold)
            {
                left = workArea.Left;
            }

            // 2. Tepi kanan layar
            if (Math.Abs(right - (workArea.Right - SnapMargin)) <= SnapThreshold)
            {
                left = workArea.Right - SnapMargin - width;
            }
            else if (Math.Abs(right - workArea.Right) <= SnapThreshold)
            {
                left = workArea.Right - width;
            }

            // 3. Tepi atas layar
            if (Math.Abs(top - (workArea.Top + SnapMargin)) <= SnapThreshold)
            {
                top = workArea.Top + SnapMargin;
            }
            else if (Math.Abs(top - workArea.Top) <= SnapThreshold)
            {
                top = workArea.Top;
            }

            // 4. Tepi bawah layar
            if (Math.Abs(bottom - (workArea.Bottom - SnapMargin)) <= SnapThreshold)
            {
                top = workArea.Bottom - SnapMargin - height;
            }
            else if (Math.Abs(bottom - workArea.Bottom) <= SnapThreshold)
            {
                top = workArea.Bottom - height;
            }

            // 5. Magnet notch atas di tengah monitor
            if (Application.Current.MainWindow is MainWindow main && main.IsVisible)
            {
                Rect mainRect = new Rect(main.Left, main.Top, main.Width, main.Height);

                // Snap ke bawah Notch
                if (Math.Abs(top - (mainRect.Bottom + SnapMargin)) <= SnapThreshold &&
                    right >= mainRect.Left - 50 && left <= mainRect.Right + 50)
                {
                    top = mainRect.Bottom + SnapMargin;
                }

                // Snap rata tengah dengan Notch
                double mainCenterX = mainRect.Left + mainRect.Width / 2.0;
                double windowCenterX = left + width / 2.0;
                if (Math.Abs(windowCenterX - mainCenterX) <= SnapThreshold)
                {
                    left = mainCenterX - width / 2.0;
                }
                // Snap sisi kiri ke tepi kanan notch
                else if (Math.Abs(left - (mainRect.Right + SnapMargin)) <= SnapThreshold)
                {
                    left = mainRect.Right + SnapMargin;
                }
                // Snap sisi kanan ke tepi kiri notch
                else if (Math.Abs(right - (mainRect.Left - SnapMargin)) <= SnapThreshold)
                {
                    left = mainRect.Left - SnapMargin - width;
                }
            }
        }

        private bool GetCursorPosDip(out Point pointDip)
        {
            return ScreenGeometry.TryGetCursor(_window, out _, out pointDip);
        }
    }
}
