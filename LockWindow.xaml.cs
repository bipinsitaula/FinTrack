using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FinanceTracker.Services;

namespace FinanceTracker;

public partial class LockWindow : Window
{
    private readonly bool _setup;
    private int _attempts;
    private int _lockSeconds;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public LockWindow()
    {
        InitializeComponent();
        _setup = !SecurityService.HasPin();
        if (_setup)
        {
            Heading.Text = "Let's set you up";
            Hint.Text = "Tell us your name and choose a PIN of at least 4 digits. You will enter the PIN every time the app opens.";
            NamePanel.Visibility = Visibility.Visible;
            NameBox.Text = ProfileService.GetName();
            Pin.MaxLength = 12;
            ConfirmPanel.Visibility = Visibility.Visible;
            Primary.Content = "Set PIN";
            Skip.Visibility = Visibility.Visible;
        }
        if (!_setup)
        {
            var name = ProfileService.GetName();
            if (name.Length > 0) Heading.Text = $"Welcome back, {name}";
        }
        _timer.Tick += (_, _) =>
        {
            if (--_lockSeconds <= 0) { _timer.Stop(); SetLocked(false); Message.Text = ""; }
            else Message.Text = $"Too many attempts. Try again in {_lockSeconds}s.";
        };
        Loaded += (_, _) => { if (_setup) NameBox.Focus(); else Pin.Focus(); };
    }

    private void Shake()
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
        double[] xs = { 0, -14, 12, -9, 6, -3, 0 };
        for (var i = 0; i < xs.Length; i++)
            a.KeyFrames.Add(new LinearDoubleKeyFrame(xs[i], KeyTime.FromPercent((i + 1) / (double)xs.Length)));
        CardShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, a);
    }

    private void SetLocked(bool locked)
    {
        Pin.IsEnabled = Primary.IsEnabled = !locked;
        if (!locked) { _attempts = 0; Pin.Focus(); }
    }

    private void Name_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Pin.Focus();
    }

    private void Input_Changed(object sender, RoutedEventArgs e) { if (_lockSeconds == 0) Message.Text = ""; }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_setup && sender == Pin) Confirm.Focus();
        else Primary_Click(sender, e);
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_setup) { CreatePin(); return; }

        if (SecurityService.Verify(Pin.Password)) { DialogResult = true; return; }
        Pin.Clear();
        if (++_attempts >= 5)
        {
            _lockSeconds = 30;
            SetLocked(true);
            Message.Text = "Too many attempts. Try again in 30s.";
            _timer.Start();
        }
        else Message.Text = $"Wrong PIN. {5 - _attempts} attempt(s) left.";
        Shake();
    }

    private void CreatePin()
    {
        var pin = Pin.Password;
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { Message.Text = "Please enter your name."; Shake(); NameBox.Focus(); return; }
        if (pin.Length < 4 || !pin.All(char.IsDigit)) { Message.Text = "PIN must be at least 4 digits (numbers only)."; Shake(); return; }
        if (pin != Confirm.Password) { Message.Text = "The two PINs do not match."; Confirm.Clear(); Confirm.Focus(); Shake(); return; }
        SecurityService.SetPin(pin);
        ProfileService.SetName(NameBox.Text);
        SecurityService.MarkSetupPrompted();
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(NameBox.Text)) ProfileService.SetName(NameBox.Text);
        SecurityService.MarkSetupPrompted();
        DialogResult = true;
    }
}
