using System.Windows;
using System.Windows.Media;
using FinanceTracker.Controls;

namespace FinanceTracker.Views;

public enum DialogKind { Question, Info, Success, Warning, Danger }

public partial class ConfirmDialog : DialogBase
{
    public ConfirmDialog(string title, string message, string confirmText, DialogKind kind, bool showCancel)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = message;
        OkButton.Content = confirmText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;

        var (glyph, soft, strong) = kind switch
        {
            DialogKind.Danger => ("", "#FFF1F2", "#E11D48"),
            DialogKind.Warning => ("", "#FFFBEB", "#F59E0B"),
            DialogKind.Success => ("", "#ECFDF5", "#059669"),
            DialogKind.Info => ("", "#EFF6FF", "#2563EB"),
            _ => ("", "#EFF6FF", "#2563EB"),
        };
        IconGlyph.Text = glyph;
        IconBubble.Background = Brush(soft);
        IconGlyph.Foreground = Brush(strong);
        if (kind is DialogKind.Danger or DialogKind.Warning) OkButton.Background = Brush(strong);
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
