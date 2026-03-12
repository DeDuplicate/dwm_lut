using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LutCreator
{
    public class KnobControl : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(KnobControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register("Minimum", typeof(double), typeof(KnobControl),
                new PropertyMetadata(-100.0, OnRangeChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(double), typeof(KnobControl),
                new PropertyMetadata(100.0, OnRangeChanged));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(KnobControl),
                new PropertyMetadata("", OnVisualChanged));

        public static readonly DependencyProperty KnobColorProperty =
            DependencyProperty.Register("KnobColor", typeof(Color), typeof(KnobControl),
                new PropertyMetadata(Color.FromRgb(0x4A, 0x9E, 0xFF), OnVisualChanged));

        public static readonly RoutedEvent ValueChangedEvent =
            EventManager.RegisterRoutedEvent("ValueChanged", RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<double>), typeof(KnobControl));

        public event RoutedPropertyChangedEventHandler<double> ValueChanged
        {
            add => AddHandler(ValueChangedEvent, value);
            remove => RemoveHandler(ValueChangedEvent, value);
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public Color KnobColor
        {
            get => (Color)GetValue(KnobColorProperty);
            set => SetValue(KnobColorProperty, value);
        }

        private bool _isDragging;
        private Point _dragStart;
        private double _dragStartValue;

        // Sweep: 270 degrees, from -135 to +135
        private const double SweepDeg = 270;
        private const double StartAngle = -135 - 90; // Offset for top = 0

        public KnobControl()
        {
            Width = 64;
            Height = 80; // Extra space for label and value
            Cursor = Cursors.Hand;
            ClipToBounds = false;
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var knob = (KnobControl)d;
            knob.InvalidateVisual();
            knob.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(
                (double)e.OldValue, (double)e.NewValue, ValueChangedEvent));
        }

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((KnobControl)d).InvalidateVisual();
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((KnobControl)d).InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth > 0 ? ActualWidth : Width;
            var h = ActualHeight > 0 ? ActualHeight : Height;
            var knobSize = Math.Min(w, 56);
            var cx = w / 2;
            var cy = knobSize / 2 + 2;
            var r = knobSize / 2 - 3;

            var accentColor = KnobColor;
            var accentBrush = new SolidColorBrush(accentColor);
            accentBrush.Freeze();

            var trackPen = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x55)), 3.5);
            trackPen.Brush.Freeze();
            trackPen.Freeze();

            var fillPen = new Pen(accentBrush, 3.5);
            fillPen.Freeze();

            // Draw track arc (full sweep)
            DrawArc(dc, cx, cy, r, -135, 135, trackPen);

            // Draw fill arc (from center to current value)
            double t = (Maximum != Minimum) ? (Value - Minimum) / (Maximum - Minimum) : 0.5;
            double currentAngle = -135 + t * SweepDeg;
            double centerAngle = 0; // 0 = center of range (for centered knobs)

            // If min is negative and max is positive, draw from center
            if (Minimum < 0 && Maximum > 0)
            {
                double zeroT = (0 - Minimum) / (Maximum - Minimum);
                centerAngle = -135 + zeroT * SweepDeg;
            }
            else
            {
                centerAngle = -135; // Draw from start
            }

            if (Math.Abs(currentAngle - centerAngle) > 0.5)
            {
                double a1 = Math.Min(centerAngle, currentAngle);
                double a2 = Math.Max(centerAngle, currentAngle);
                DrawArc(dc, cx, cy, r, a1, a2, fillPen);
            }

            // Draw indicator dot
            double indicatorAngle = (currentAngle + StartAngle + 90) * Math.PI / 180;
            double dotR = r - 9;
            var dotX = cx + Math.Cos(indicatorAngle) * dotR;
            var dotY = cy + Math.Sin(indicatorAngle) * dotR;

            dc.DrawEllipse(Brushes.White, null, new Point(dotX, dotY), 3, 3);

            // Draw center dot
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x55)), null,
                new Point(cx, cy), 2.5, 2.5);

            // Draw value text
            var valueText = new FormattedText(
                ((int)Math.Round(Value)).ToString(),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10, new SolidColorBrush(Color.FromRgb(0x88, 0x90, 0xA8)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(valueText, new Point(cx - valueText.Width / 2, cy + r + 3));

            // Draw label text
            var labelText = new FormattedText(
                Label ?? "",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                9, new SolidColorBrush(Color.FromRgb(0x5A, 0x62, 0x80)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(labelText, new Point(cx - labelText.Width / 2, cy + r + 16));
        }

        private void DrawArc(DrawingContext dc, double cx, double cy, double r,
            double startDeg, double endDeg, Pen pen)
        {
            // Convert to radians with offset (top = -90)
            double startRad = (startDeg - 90) * Math.PI / 180;
            double endRad = (endDeg - 90) * Math.PI / 180;

            var start = new Point(cx + Math.Cos(startRad) * r, cy + Math.Sin(startRad) * r);
            var end = new Point(cx + Math.Cos(endRad) * r, cy + Math.Sin(endRad) * r);

            double sweepAngle = endDeg - startDeg;
            bool isLargeArc = sweepAngle > 180;

            var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0,
                isLargeArc, SweepDirection.Clockwise, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            dc.DrawGeometry(null, pen, geometry);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click: reset to center (0 for bipolar, minimum for unipolar)
                Value = (Minimum < 0 && Maximum > 0) ? 0 : Minimum;
                e.Handled = true;
                return;
            }

            _isDragging = true;
            _dragStart = e.GetPosition(this);
            _dragStartValue = Value;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_isDragging) return;

            var pos = e.GetPosition(this);
            double deltaY = _dragStart.Y - pos.Y;
            double range = Maximum - Minimum;
            double sensitivity = range / 120;
            double newValue = _dragStartValue + deltaY * sensitivity;
            Value = Math.Max(Minimum, Math.Min(Maximum, Math.Round(newValue)));
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            double step = (Maximum - Minimum) / 100;
            Value = Math.Max(Minimum, Math.Min(Maximum,
                Math.Round(Value + Math.Sign(e.Delta) * step * 3)));
            e.Handled = true;
        }

    }
}
