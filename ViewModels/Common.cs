using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FinanceTracker.ViewModels;

public static class Ui
{
    public static void Info(string message, string title = "Done") =>
        new Views.ConfirmDialog(title, message, "OK", Views.DialogKind.Success, false).ShowDialog();

    /// <summary>Counts warnings shown, so pop-up forms can tell whether a save succeeded.</summary>
    public static int ErrorCount { get; private set; }

    public static void Error(string message)
    {
        ErrorCount++;
        new Views.ConfirmDialog("Check this first", message, "OK", Views.DialogKind.Warning, false).ShowDialog();
    }

    /// <summary>Styled yes/no dialog. Messages that start with Delete or Remove get the red destructive style.</summary>
    public static bool Confirm(string message, string? title = null, string? confirmText = null)
    {
        var destructive = message.StartsWith("Delete", StringComparison.OrdinalIgnoreCase)
                          || message.StartsWith("Remove", StringComparison.OrdinalIgnoreCase);
        return new Views.ConfirmDialog(
            title ?? (destructive ? "Are you sure?" : "Please confirm"),
            message,
            confirmText ?? (destructive ? "Delete" : "Confirm"),
            destructive ? Views.DialogKind.Danger : Views.DialogKind.Question,
            true).ShowDialog() == true;
    }
}

public abstract partial class PageViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "";

    protected void OpenForm(string title, string subtitle, string primaryText, string templateKey, System.Windows.Input.ICommand save, bool showCancel = true) =>
        new Views.FormDialog(title, subtitle, primaryText, templateKey, this, save, showCancel).ShowDialog();

    protected void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Ui.Error(ex.InnerException?.Message ?? ex.Message); }
    }
}

public class PageEqualsConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        Equals(value as string, parameter as string);

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is true ? parameter : System.Windows.Data.Binding.DoNothing;
}

/// <summary>Turns a row's zero-based AlternationIndex into the 1-based serial number shown in the SN column.</summary>
public class RowNumberConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is int i && i >= 0 ? (i + 1).ToString() : "";

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}

public partial class MainViewModel : ObservableObject
{
    private static readonly Dictionary<string, (string Title, string Subtitle)> Titles = new()
    {
        ["Dashboard"] = ("Dashboard", "Welcome back, here is your financial overview."),
        ["Accounts"] = ("Accounts", "Manage your banks, wallets, cash and cards."),
        ["Transactions"] = ("Transactions", "Add, search and filter your complete history."),
        ["Budgets"] = ("Budgets", "Track your monthly spending limits by category."),
        ["Loans"] = ("Loans & Debts", "Manage your liabilities and payoff plans."),
        ["Bills"] = ("Bills & Insurance", "Never miss a payment with due-date tracking."),
        ["Goals"] = ("Savings & Goals", "Visualize progress towards your financial targets."),
        ["Invest"] = ("Investments & Tools", "Portfolio, tax summary, bill splitting and currency."),
        ["Insights"] = ("Insights", "Health score, cash flow forecast and spending patterns."),
        ["Settings"] = ("Settings & Backup", "Reports, backups, app lock and exchange rates."),
    };

    [ObservableProperty] private object? _current;
    [ObservableProperty] private string _page = "Dashboard";
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(Initial))]
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _title = "Dashboard";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _hasAlerts;
    [ObservableProperty] private string _calendar = "AD";

    public string DisplayName => string.IsNullOrWhiteSpace(UserName) ? "My Finances" : UserName.Trim();
    public string Initial => string.IsNullOrWhiteSpace(UserName) ? "F" : UserName.Trim()[..1].ToUpperInvariant();

    public MainViewModel()
    {
        UserName = Services.ProfileService.GetName();
        using (var db = new Data.AppDb())
            _calendar = Services.FinanceService.GetSetting(db, "DateMode") == "BS" ? "BS" : "AD";
        Services.DateDisplay.UseBs = _calendar == "BS";
        Navigate("Dashboard");
        Services.ProfileService.Changed += () =>
        {
            UserName = Services.ProfileService.GetName();
            if (Page == "Dashboard") Subtitle = DashboardSubtitle();
        };
    }

    private string DashboardSubtitle() =>
        string.IsNullOrWhiteSpace(UserName)
            ? "Welcome back, here is your financial overview."
            : $"{Services.ProfileService.Greeting()}, {UserName.Trim()}. Here is your financial overview.";

    partial void OnPageChanged(string value) => Navigate(value);

    partial void OnCalendarChanged(string value)
    {
        Services.DateDisplay.UseBs = value == "BS";
        using (var db = new Data.AppDb()) Services.FinanceService.SetSetting(db, "DateMode", value);
        Navigate(Page);   // rebuild the current page so every date re-renders in the chosen calendar
    }

    private void Navigate(string page, string? search = null)
    {
        Title = Titles[page].Title;
        Subtitle = page == "Dashboard" ? DashboardSubtitle() : Titles[page].Subtitle;
        Current = page switch
        {
            "Dashboard" => new DashboardViewModel(),
            "Accounts" => new AccountsViewModel(),
            "Transactions" => new TransactionsViewModel { Search = search ?? "" },
            "Budgets" => new BudgetsViewModel(),
            "Loans" => new LoansViewModel(),
            "Bills" => new BillsViewModel(),
            "Goals" => new GoalsViewModel(),
            "Invest" => new InvestmentsViewModel(),
            "Insights" => new InsightsViewModel(),
            "Settings" => new SettingsViewModel(),
            _ => Current,
        };
        using var db = new Data.AppDb();
        HasAlerts = Services.FinanceService.Alerts(db).Any(a => a.Level != "Info");
    }

    [RelayCommand]
    private void Search()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        var text = SearchText.Trim();
        SearchText = "";
        Page = "Transactions";          // syncs the sidebar; Navigate runs via OnPageChanged
        Navigate("Transactions", text); // re-run with the search applied
    }

    [RelayCommand]
    private void Logout()
    {
        var main = Application.Current.MainWindow;
        if (!Services.SecurityService.HasPin())
        {
            if (!Ui.Confirm("No PIN is set, so logging out closes the app. Set a PIN in Settings & Backup if you want a lock screen instead.", "Log out?", "Close app")) return;
            Application.Current.Shutdown();
            return;
        }
        if (!Ui.Confirm("You will need your PIN to open FinTrack again.", "Log out?", "Log out")) return;

        main.Hide();
        var lockWindow = new FinanceTracker.LockWindow();
        if (lockWindow.ShowDialog() == true)
        {
            Navigate("Dashboard");
            Page = "Dashboard";
            main.Show();
            main.Activate();
        }
        else Application.Current.Shutdown();
    }
    [RelayCommand] private void ShowAlerts() => Page = "Dashboard";
}
