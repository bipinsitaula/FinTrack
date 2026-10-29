using System.Windows;
using System.Windows.Input;

namespace FinanceTracker;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Controls.Dimmer.Target = Scrim;

        // Ctrl+K jumps to the search box from anywhere.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        };
    }
}
