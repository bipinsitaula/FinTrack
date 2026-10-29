using System.Windows;
using System.Windows.Controls;
using FinanceTracker.ViewModels;

namespace FinanceTracker.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private SettingsViewModel Vm => (SettingsViewModel)DataContext;

    private void Backup_Click(object sender, RoutedEventArgs e) => Vm.Backup(BackupPassword.Password);

    private void Restore_Click(object sender, RoutedEventArgs e) => Vm.Restore(BackupPassword.Password);

    private void SavePin_Click(object sender, RoutedEventArgs e)
    {
        Vm.SetPin(NewPin.Password);
        NewPin.Clear();
    }
}
