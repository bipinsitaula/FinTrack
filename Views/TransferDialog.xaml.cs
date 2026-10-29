using System.Windows;
using FinanceTracker.Controls;
using FinanceTracker.ViewModels;

namespace FinanceTracker.Views;

public partial class TransferDialog : DialogBase
{
    public TransferDialog(AccountsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) => { AmountBox.Focus(); AmountBox.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ((System.Windows.Controls.Button)sender).Focus();  // commit pending text-box edits
        if (((AccountsViewModel)DataContext).TryTransfer()) DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
