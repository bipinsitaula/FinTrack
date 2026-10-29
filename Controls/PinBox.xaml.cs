using System.Windows;
using System.Windows.Controls;

namespace FinanceTracker.Controls;

/// <summary>A PIN field with a show/hide eye. Exposes the same members the login and settings screens use on PasswordBox.</summary>
public partial class PinBox : UserControl
{
    private bool _syncing;

    public static readonly DependencyProperty InputAlignmentProperty = DependencyProperty.Register(
        nameof(InputAlignment), typeof(HorizontalAlignment), typeof(PinBox), new PropertyMetadata(HorizontalAlignment.Left, (d, _) => ((PinBox)d).ApplyPadding()));

    // Room for the eye button on the right; when centered, the same room on the left keeps the digits exactly centered.
    private void ApplyPadding()
    {
        var pad = InputAlignment == HorizontalAlignment.Center ? new Thickness(46, 10, 46, 10) : new Thickness(12, 10, 46, 10);
        Masked.Padding = pad;
        Plain.Padding = pad;
    }

    /// <summary>Alignment of the digits inside the field (kept separate so the control itself still fills its row).</summary>
    public HorizontalAlignment InputAlignment
    {
        get => (HorizontalAlignment)GetValue(InputAlignmentProperty);
        set => SetValue(InputAlignmentProperty, value);
    }

    public event RoutedEventHandler? PasswordChanged;

    public PinBox() => InitializeComponent();

    public string Password
    {
        get => Masked.Password;
        set => Masked.Password = value ?? "";
    }

    public int MaxLength
    {
        get => Masked.MaxLength;
        set { Masked.MaxLength = value; Plain.MaxLength = value; }
    }

    public void Clear() => Masked.Clear();

    public new bool Focus()
    {
        if (Eye.IsChecked == true)
        {
            Plain.CaretIndex = Plain.Text.Length;
            return Plain.Focus();
        }
        return Masked.Focus();
    }

    private void Masked_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        Plain.Text = Masked.Password;
        _syncing = false;
        PasswordChanged?.Invoke(this, e);
    }

    private void Plain_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        Masked.Password = Plain.Text;
        _syncing = false;
        PasswordChanged?.Invoke(this, e);
    }

    private void Eye_Click(object sender, RoutedEventArgs e)
    {
        var reveal = Eye.IsChecked == true;
        Plain.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
        Masked.Visibility = reveal ? Visibility.Collapsed : Visibility.Visible;
        Eye.ToolTip = reveal ? "Hide the PIN" : "Show the PIN";
        Focus();
    }
}
