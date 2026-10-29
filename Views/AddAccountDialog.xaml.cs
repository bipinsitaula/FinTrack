using System.Windows;
using FinanceTracker.Controls;
using FinanceTracker.ViewModels;

namespace FinanceTracker.Views;

public partial class AddAccountDialog : DialogBase
{
    public AddAccountDialog(AccountsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ((System.Windows.Controls.Button)sender).Focus();  // commit pending text-box edits
        if (((AccountsViewModel)DataContext).TryAddAccount()) DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
