using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FinanceTracker.Controls;

/// <summary>Dims the main window behind a modal dialog.</summary>
public static class Dimmer
{
    public static Border? Target { get; set; }
    private static int _depth;

    public static void Show()
    {
        if (Target == null || _depth++ > 0) return;
        Target.BeginAnimation(UIElement.OpacityProperty, null);
        Target.Opacity = 0;
        Target.Visibility = Visibility.Visible;
        Target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    public static void Hide()
    {
        if (Target == null || --_depth > 0) return;
        _depth = 0;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => { if (_depth == 0 && Target != null) Target.Visibility = Visibility.Collapsed; };
        Target.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}

/// <summary>Borderless, rounded, animated modal window. Root of every pop-up in the app.</summary>
public class DialogBase : Window
{
    public DialogBase()
    {
        if (Application.Current.TryFindResource(typeof(Window)) is Style windowStyle) Style = windowStyle;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsLoaded && !ReferenceEquals(w, this))
                    ?? Application.Current.MainWindow;
        if (owner is { IsLoaded: true } && !ReferenceEquals(owner, this)) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not (TextBox or PasswordBox or ComboBox or Button) && e.ButtonState == MouseButtonState.Pressed)
                try { DragMove(); } catch (InvalidOperationException) { }
        };
        Loaded += (_, _) =>
        {
            if (Content is not FrameworkElement fe) return;
            fe.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(0.93, 0.93);
            fe.RenderTransform = scale;
            var pop = new DoubleAnimation(0.93, 1, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        };
    }

    public new bool? ShowDialog()
    {
        Dimmer.Show();
        try { return base.ShowDialog(); }
        finally { Dimmer.Hide(); }
    }
}
