using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.ViewModels;

/// <summary>Money coming in. Each entry is a normal Income transaction, so it also appears under Transactions.</summary>
public partial class IncomeViewModel : PageViewModel
{
    [ObservableProperty] private List<Transaction> _items = new();
    [ObservableProperty] private Transaction? _selected;
    [ObservableProperty] private List<Account> _accounts = new();
    [ObservableProperty] private List<Category> _categories = new();
    [ObservableProperty] private decimal _thisMonth;
    [ObservableProperty] private decimal _lastMonth;
    [ObservableProperty] private decimal _total;

    // add-income form
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private Account? _account;
    [ObservableProperty] private Category? _category;
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string _note = "";

    public IncomeViewModel() => Load();

    private void Load()
    {
        using var db = new AppDb();
        // Credit cards are not somewhere income is received.
        Accounts = FinanceService.LoadAccounts(db).Where(a => a.Type != AccountType.CreditCard).ToList();
        Categories = FinanceService.LoadCategories(db, true);
        Account = Accounts.FirstOrDefault(a => a.Id == Account?.Id) ?? Accounts.FirstOrDefault();
        Category = Categories.FirstOrDefault(c => c.Id == Category?.Id) ?? DefaultSource();

        Items = db.Transactions.Include(t => t.Account).Include(t => t.Category).AsNoTracking()
            .Where(t => t.Kind == TxKind.Income).ToList()
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToList();
        ThisMonth = FinanceService.MonthTotals(db, DateTime.Today).Income;
        LastMonth = FinanceService.MonthTotals(db, DateDisplay.AddMonths(DateTime.Today, -1)).Income;
        Total = Items.Sum(t => t.Amount);
    }

    private Category? DefaultSource() => Categories.FirstOrDefault(c => c.Name == "Salary") ?? Categories.FirstOrDefault();

    /// <summary>Lets the Accounts page open the add-income form with an account already chosen.</summary>
    public void Preselect(int? accountId)
    {
        if (accountId is int id) Account = Accounts.FirstOrDefault(a => a.Id == id) ?? Account;
    }

    [RelayCommand]
    private void OpenAdd()
    {
        Amount = 0; Note = ""; Date = DateTime.Today;
        Category = DefaultSource();
        OpenForm("Add income", "Money received into a bank, wallet or cash.", "Add income", "IncomeForm", AddIncomeCommand);
    }

    [RelayCommand]
    private void AddIncome() => Run(() =>
    {
        if (Account == null) { Ui.Error("Choose the account the money went into."); return; }
        if (Amount <= 0) { Ui.Error("Enter an amount greater than zero."); return; }
        using var db = new AppDb();
        db.Transactions.Add(new Transaction
        {
            AccountId = Account.Id, CategoryId = Category?.Id, Amount = Amount, Date = Date, Kind = TxKind.Income,
            Note = Note.Trim(),
        });
        db.SaveChanges();
        var added = Amount;
        var where = Account.Name;
        Load();
        Status = $"Added NPR {added:N2} to {where}.";
    });

    [RelayCommand]
    private void DeleteSelected() => Run(() =>
    {
        if (Selected == null) { Ui.Error("Select an income entry in the table first."); return; }
        if (!Ui.Confirm($"Delete this income of NPR {Selected.Amount:N2} from {Selected.AccountName}? The account balance goes down by the same amount.")) return;
        using var db = new AppDb();
        db.Transactions.Remove(db.Transactions.Find(Selected.Id)!);
        db.SaveChanges();
        Selected = null;
        Load();
        Status = "Income entry deleted.";
    });
}
