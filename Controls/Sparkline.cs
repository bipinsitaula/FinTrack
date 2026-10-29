using System.Windows;
using System.Windows.Media;

namespace FinanceTracker.Controls;

/// <summary>A small smoothed trend line with a soft gradient underneath, for KPI cards.</summary>
public class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(Sparkline),
        new FrameworkPropertyMetadata(Colors.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var data = Values?.ToArray();
        if (data == null || data.Length < 2 || ActualWidth < 10 || ActualHeight < 10) return;

        const double pad = 5;
        double w = ActualWidth, h = ActualHeight;
        double min = data.Min(), max = data.Max();
        var range = max - min;
        var pts = new Point[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            var x = pad + i * (w - 2 * pad) / (data.Length - 1);
            var t = range < 1e-9 ? 0.5 : (data[i] - min) / range;
            pts[i] = new Point(x, pad + (h - 2 * pad) * (1 - t));
        }

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(pts[0], false, false);
            for (var i = 0; i < pts.Length - 1; i++)
            {
                var mx = (pts[i].X + pts[i + 1].X) / 2;
                g.BezierTo(new Point(mx, pts[i].Y), new Point(mx, pts[i + 1].Y), pts[i + 1], true, true);
            }
        }
        line.Freeze();

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(pts[0], true, true);
            for (var i = 0; i < pts.Length - 1; i++)
            {
                var mx = (pts[i].X + pts[i + 1].X) / 2;
                g.BezierTo(new Point(mx, pts[i].Y), new Point(mx, pts[i + 1].Y), pts[i + 1], true, true);
            }
            g.LineTo(new Point(pts[^1].X, h), false, false);
            g.LineTo(new Point(pts[0].X, h), false, false);
        }
        area.Freeze();

        var fill = new LinearGradientBrush(Color.FromArgb(70, Color.R, Color.G, Color.B), Color.FromArgb(0, Color.R, Color.G, Color.B), 90);
        fill.Freeze();
        var stroke = new SolidColorBrush(Color);
        stroke.Freeze();
        var pen = new Pen(stroke, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();

        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, line);
        dc.DrawEllipse(Brushes.White, new Pen(stroke, 2), pts[^1], 3.5, 3.5);
    }
}
