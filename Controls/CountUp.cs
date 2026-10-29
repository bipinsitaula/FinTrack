using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace FinanceTracker.Controls;

/// <summary>A TextBlock that counts up to its value, so KPI numbers animate in.</summary>
public class CountUp : TextBlock
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(CountUp), new PropertyMetadata(0.0, OnValueChanged));
    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(CountUp), new PropertyMetadata("{0:N0}", (d, _) => ((CountUp)d).Render()));
    private static readonly DependencyProperty DisplayProperty = DependencyProperty.Register(
        "Display", typeof(double), typeof(CountUp), new PropertyMetadata(0.0, (d, _) => ((CountUp)d).Render()));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (CountUp)d;
        var anim = new DoubleAnimation((double)e.NewValue, TimeSpan.FromMilliseconds(900))
        {
            From = (double)c.GetValue(DisplayProperty),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        c.BeginAnimation(DisplayProperty, anim);
    }

    private void Render()
    {
        try { Text = string.Format(Format, (decimal)(double)GetValue(DisplayProperty)); }
        catch (FormatException) { Text = Value.ToString("N0"); }
    }
}
