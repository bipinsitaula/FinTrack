using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace FinanceTracker.ViewModels;

// ===================== Accounts =====================
/// <summary>A KPI card's trend line and month-on-month change.</summary>
public record Kpi(IReadOnlyList<double> Trend, string ChangeText, string ChangeColor);

/// <summary>One row in the Accounts filter panel. Selecting it tells the page which filter is now active.</summary>
public partial class FilterOption : ObservableObject
{
    private readonly Action<FilterOption> _picked;
    public FilterOption(string key, string label, Action<FilterOption> picked) { Key = key; Label = label; _picked = picked; }
    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isSelected;
    partial void OnIsSelectedChanged(bool value) { if (value) _picked(this); }
}

public partial class AccountsViewModel : PageViewModel
{
    public AccountType[] AccountTypes { get; } = { AccountType.Savings, AccountType.Current, AccountType.Bank, AccountType.EWallet, AccountType.Cash, AccountType.CreditCard };

    [ObservableProperty] private List<Account> _accounts = new();
    [ObservableProperty] private Account? _selected;
    [ObservableProperty] private decimal _assets;
    [ObservableProperty] private decimal _liabilities;
    [ObservableProperty] private decimal _netWorth;
    [ObservableProperty] private Kpi? _assetsKpi;
    [ObservableProperty] private Kpi? _liabilitiesKpi;
    [ObservableProperty] private Kpi? _netKpi;

    // view toggle and sorting
    [NotifyPropertyChangedFor(nameof(IsListView))]
    [ObservableProperty] private bool _isGridView = true;
    public bool IsListView
    {
        get => !IsGridView;
        set => IsGridView = !value;
    }
    // left-hand filter panel
    private List<Account> _all = new();
    public List<FilterOption> TypeFilters { get; }
    public List<FilterOption> BalanceFilters { get; }
    private string _typeKey = "All";
    private string _balanceKey = "Any";
    [ObservableProperty] private string _showing = "";
    public bool HasFilters => _typeKey != "All" || _balanceKey != "Any";

    public string[] SortOptions { get; } = { "Balance (High → Low)", "Balance (Low → High)", "Name (A → Z)", "Type" };
    [ObservableProperty] private string _selectedSort = "Balance (High → Low)";

    // new account
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private AccountType _type = AccountType.Savings;
    [ObservableProperty] private decimal _opening;
    [ObservableProperty] private decimal _rate;
    [ObservableProperty] private int _dueDay;
    [ObservableProperty] private decimal _limit;
    [ObservableProperty] private string? _cardColor;
    private int _editAccountId;

    public IReadOnlyList<string> ColorChoices => Services.CardColors.Palette;

    // transfer
    [ObservableProperty] private Account? _from;
    [ObservableProperty] private Account? _to;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string _note = "";

    public AccountsViewModel()
    {
        TypeFilters = new()
        {
            new("All", "All accounts", PickType) { IsSelected = true },
            new("Savings", "Savings", PickType), new("Current", "Current", PickType), new("Bank", "Other bank", PickType),
            new("EWallet", "E-wallets", PickType), new("Cash", "Cash", PickType), new("CreditCard", "Credit cards", PickType),
        };
        BalanceFilters = new()
        {
            new("Any", "Any balance", PickBalance) { IsSelected = true },
            new("Positive", "Has money", PickBalance), new("Owed", "Owed", PickBalance), new("Empty", "Empty", PickBalance),
        };
        Load();
    }

    private void PickType(FilterOption o) { _typeKey = o.Key; Refilter(); }
    private void PickBalance(FilterOption o) { _balanceKey = o.Key; Refilter(); }

    [RelayCommand]
    private void ClearFilters()
    {
        TypeFilters[0].IsSelected = true;
        BalanceFilters[0].IsSelected = true;
    }

    private void Refilter()
    {
        OnPropertyChanged(nameof(HasFilters));
        if (_all.Count == 0 && Accounts.Count == 0) return;
        var shown = _all.Where(a => (_typeKey == "All" || a.Type.ToString() == _typeKey) && _balanceKey switch
        {
            "Positive" => a.Balance > 0,
            "Owed" => a.Balance < 0,
            "Empty" => a.Balance == 0,
            _ => true,
        }).ToList();
        Accounts = Sorted(shown);
        Showing = shown.Count == _all.Count ? $"{_all.Count} accounts" : $"Showing {shown.Count} of {_all.Count}";
    }

    private void Load()
    {
        using var db = new AppDb();
        var list = FinanceService.LoadAccounts(db);
        var totalAssets = list.Where(a => a.Balance > 0).Sum(a => a.Balance);
        foreach (var a in list)
            a.ShareLabel = a.Balance < 0 ? "Owed" : a.Balance == 0 || totalAssets <= 0 ? "Empty" : $"{a.Balance / totalAssets * 100:N0}% of assets";
        _all = list;
        foreach (var f in TypeFilters) f.Count = f.Key == "All" ? list.Count : list.Count(a => a.Type.ToString() == f.Key);
        foreach (var f in BalanceFilters) f.Count = f.Key switch
        {
            "Positive" => list.Count(a => a.Balance > 0), "Owed" => list.Count(a => a.Balance < 0),
            "Empty" => list.Count(a => a.Balance == 0), _ => list.Count,
        };
        Refilter();

        var w = FinanceService.NetWorth(db);
        Assets = w.Assets; Liabilities = w.Liabilities; NetWorth = w.NetWorth;

        var trend = FinanceService.AccountTrends(db, 6);
        AssetsKpi = MakeKpi(trend.Select(t => (double)t.Assets).ToList(), upIsGood: true);
        LiabilitiesKpi = MakeKpi(trend.Select(t => (double)t.Liabilities).ToList(), upIsGood: false);
        NetKpi = MakeKpi(trend.Select(t => (double)(t.Assets - t.Liabilities)).ToList(), upIsGood: true);
    }

    private static Kpi MakeKpi(List<double> trend, bool upIsGood)
    {
        var cur = trend.Count > 0 ? trend[^1] : 0;
        var prev = trend.Count > 1 ? trend[^2] : cur;
        var pct = Math.Abs(prev) < 0.005 ? 0 : (cur - prev) / Math.Abs(prev) * 100;
        var up = pct > 0.05;
        var down = pct < -0.05;
        var arrow = up ? "↑" : down ? "↓" : "→";
        var good = (up && upIsGood) || (down && !upIsGood);
        var bad = (up && !upIsGood) || (down && upIsGood);
        return new Kpi(trend, $"{arrow} {Math.Abs(pct):N1}%", good ? "#059669" : bad ? "#E11D48" : "#64748B");
    }

    private List<Account> Sorted(List<Account> list) => SelectedSort switch
    {
        "Balance (Low → High)" => list.OrderBy(a => a.Balance).ToList(),
        "Name (A → Z)" => list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        "Type" => list.OrderBy(a => a.Type).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        _ => list.OrderByDescending(a => a.Balance).ToList(),
    };

    partial void OnSelectedSortChanged(string value) => Accounts = Sorted(Accounts);



    /// <summary>Opens the Income form with the selected account chosen, then refreshes the balances.</summary>
    [RelayCommand]
    private void AddMoney(Account? account)
    {
        var income = new IncomeViewModel();
        income.Preselect((account ?? Selected)?.Id);
        income.OpenAddCommand.Execute(null);
        Load();
    }

    [RelayCommand]
    private void ResetCardColor() => CardColor = null;

    [RelayCommand]
    private void OpenEditAccount(Account? account)
    {
        if (account == null) return;
        _editAccountId = account.Id;
        Name = account.Name; Rate = account.InterestRate; CardColor = account.Color;
        OpenForm("Edit account", "Change the name, interest rate or the color of the card.", "Save changes", "EditAccountForm", SaveAccountEditCommand);
    }

    [RelayCommand]
    private void SaveAccountEdit() => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(Name)) { Ui.Error("Enter an account name."); return; }
        using var db = new AppDb();
        var acct = db.Accounts.Find(_editAccountId)!;
        acct.Name = Name.Trim();
        acct.InterestRate = Rate;
        acct.Color = string.IsNullOrWhiteSpace(CardColor) ? null : CardColor;
        db.SaveChanges();
        Status = $"'{acct.Name}' was updated.";
        Load();
    });

    [RelayCommand]
    private void OpenAddAccount()
    {
        Name = ""; Type = AccountType.Savings; Opening = 0; Rate = 0; DueDay = 0; Limit = 0; CardColor = null;
        new Views.AddAccountDialog(this).ShowDialog();
    }

    /// <summary>Called by the Add account pop-up; returns true when saved so the pop-up can close.</summary>
    public bool TryAddAccount()
    {
        if (string.IsNullOrWhiteSpace(Name)) { Ui.Error("Enter an account name."); return false; }
        if (Type == AccountType.CreditCard && (DueDay < 0 || DueDay > 28)) { Ui.Error("The card due day must be between 1 and 28."); return false; }
        try
        {
            using var db = new AppDb();
            db.Accounts.Add(new Account
            {
                Name = Name.Trim(), Type = Type, OpeningBalance = Type == AccountType.CreditCard ? -Math.Abs(Opening) : Opening,
                InterestRate = Rate, DueDay = DueDay, CreditLimit = Limit,
                Color = string.IsNullOrWhiteSpace(CardColor) ? null : CardColor,
            });
            db.SaveChanges();
            Status = $"'{Name.Trim()}' was added.";
            Load();
            return true;
        }
        catch (Exception ex) { Ui.Error(ex.InnerException?.Message ?? ex.Message); return false; }
    }

    [RelayCommand]
    private void DeleteAccount(Account? account) => Run(() =>
    {
        var target = account ?? Selected;
        if (target == null) { Ui.Error("Select an account card first."); return; }
        if (!Ui.Confirm($"Delete '{target.Name}' and all of its transactions?")) return;
        using var db = new AppDb();
        db.Accounts.Remove(db.Accounts.Find(target.Id)!);
        db.SaveChanges();
        Selected = null;
        Load();
    });

    [RelayCommand]
    private void OpenTransfer()
    {
        if (Accounts.Count < 2) { Ui.Info("Add at least two accounts before making a transfer.", "Not enough accounts"); return; }
        From = Accounts[0];
        To = Accounts[1];
        Amount = 0; Note = ""; Date = DateTime.Today;
        new Views.TransferDialog(this).ShowDialog();
    }

    /// <summary>Called by the Transfer pop-up; returns true when recorded so the pop-up can close.</summary>
    public bool TryTransfer()
    {
        if (From == null || To == null || From.Id == To.Id) { Ui.Error("Pick two different accounts."); return false; }
        if (Amount <= 0) { Ui.Error("Enter an amount greater than zero."); return false; }
        try
        {
            using var db = new AppDb();
            FinanceService.AddTransfer(db, From.Id, To.Id, Amount, Date, Note);
            Status = $"Moved NPR {Amount:N2} from {From.Name} to {To.Name}.";
            Load();
            return true;
        }
        catch (Exception ex) { Ui.Error(ex.InnerException?.Message ?? ex.Message); return false; }
    }
}

// ===================== Transactions =====================
public class SplitLine
{
    public Category? Category { get; set; }
    public decimal Amount { get; set; }
}

public partial class TransactionsViewModel : PageViewModel
{
    public Array Kinds { get; } = new[] { TxKind.Expense, TxKind.Income };
    public Array Cycles { get; } = Enum.GetValues<BillCycle>();

    [ObservableProperty] private List<Account> _accounts = new();
    [ObservableProperty] private List<Category> _expenseCategories = new();
    [ObservableProperty] private List<Category> _incomeCategories = new();
    [ObservableProperty] private List<Category> _allCategories = new();
    [ObservableProperty] private List<Transaction> _items = new();
    [ObservableProperty] private Transaction? _selected;
    [ObservableProperty] private decimal _filteredIncome;
    [ObservableProperty] private decimal _filteredExpense;
    [ObservableProperty] private int _txCount;
    [ObservableProperty] private decimal _netBalance;
    [ObservableProperty] private int _activeFilters;

    // filters
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private Account? _filterAccount;
    [ObservableProperty] private Category? _filterCategory;
    [ObservableProperty] private string _fromText = "";
    [ObservableProperty] private string _toText = "";

    // quick add
    [ObservableProperty] private TxKind _kind = TxKind.Expense;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private Account? _account;
    [ObservableProperty] private Category? _category;
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _receipt = "";

    // split
    public ObservableCollection<SplitLine> Lines { get; } = new();
    [ObservableProperty] private decimal _splitTotal;

    // recurring income
    [ObservableProperty] private List<RecurringIncome> _recurring = new();
    [ObservableProperty] private RecurringIncome? _selectedRecurring;
    [ObservableProperty] private string _recName = "";
    [ObservableProperty] private decimal _recAmount;
    [ObservableProperty] private Account? _recAccount;
    [ObservableProperty] private Category? _recCategory;
    [ObservableProperty] private BillCycle _recCycle = BillCycle.Monthly;
    [ObservableProperty] private DateTime _recNext = DateTime.Today;

    public TransactionsViewModel()
    {
        LoadLookups();
        Lines.Add(new SplitLine());
        Lines.Add(new SplitLine());
        Refresh();
    }

    [RelayCommand]
    private void OpenAdd()
    {
        Kind = TxKind.Expense; Amount = 0; Category = null; Note = ""; Receipt = ""; Date = DateTime.Today;
        Account ??= Accounts.FirstOrDefault();
        OpenForm("Add transaction", "Record income or an expense.", "Save transaction", "TransactionForm", AddCommand);
    }

    [RelayCommand]
    private void OpenSplit()
    {
        Lines.Clear(); Lines.Add(new SplitLine()); Lines.Add(new SplitLine());
        Note = ""; Receipt = ""; Date = DateTime.Today;
        Account ??= Accounts.FirstOrDefault();
        OpenForm("Split a bill", "Spread one payment across several categories.", "Save split bill", "SplitForm", SaveSplitCommand);
    }

    [RelayCommand]
    private void OpenRecurring()
    {
        RecName = ""; RecAmount = 0; RecNext = DateTime.Today; RecCycle = BillCycle.Monthly;
        RecAccount ??= Accounts.FirstOrDefault();
        OpenForm("Add recurring income", "", "Add income", "RecurringForm", AddRecurringCommand);
    }

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnFilterAccountChanged(Account? value) => Refresh();
    partial void OnFilterCategoryChanged(Category? value) => Refresh();
    partial void OnFromTextChanged(string value) => Refresh();
    partial void OnToTextChanged(string value) => Refresh();

    private void LoadLookups()
    {
        using var db = new AppDb();
        Accounts = FinanceService.LoadAccounts(db);
        Account = Accounts.FirstOrDefault();
        RecAccount = Accounts.FirstOrDefault();
        var all = FinanceService.LoadCategories(db);
        AllCategories = all;
        ExpenseCategories = all.Where(c => !c.IsIncome).ToList();
        IncomeCategories = all.Where(c => c.IsIncome).ToList();
        Recurring = db.RecurringIncomes.AsNoTracking().ToList();
    }

    private void Refresh()
    {
        ActiveFilters = (FilterAccount != null ? 1 : 0) + (FilterCategory != null ? 1 : 0)
                        + (DateTime.TryParse(FromText, out _) ? 1 : 0) + (DateTime.TryParse(ToText, out _) ? 1 : 0);
        using var db = new AppDb();
        var q = db.Transactions.Include(t => t.Account).Include(t => t.Category).AsNoTracking().ToList().AsEnumerable();
        if (FilterAccount != null) q = q.Where(t => t.AccountId == FilterAccount.Id);
        if (FilterCategory != null) q = q.Where(t => t.CategoryId == FilterCategory.Id);
        if (DateTime.TryParse(FromText, out var f)) q = q.Where(t => t.Date >= f);
        if (DateTime.TryParse(ToText, out var to)) q = q.Where(t => t.Date <= to);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var s = Search.Trim();
            q = q.Where(t => t.Note.Contains(s, StringComparison.OrdinalIgnoreCase)
                || t.CategoryName.Contains(s, StringComparison.OrdinalIgnoreCase)
                || t.AccountName.Contains(s, StringComparison.OrdinalIgnoreCase)
                || t.Amount.ToString().Contains(s));
        }
        var list = q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToList();
        Items = list;
        FilteredIncome = list.Where(t => t.Kind == TxKind.Income).Sum(t => t.Amount);
        FilteredExpense = list.Where(t => t.Kind == TxKind.Expense).Sum(t => t.Amount);
        TxCount = list.Count;
        NetBalance = FilteredIncome - FilteredExpense;
    }

    [RelayCommand]
    private void OpenFilter() => OpenForm("Filter transactions", "", "Show results", "FilterForm", ApplyFiltersCommand, showCancel: false);

    [RelayCommand] private void ApplyFilters() => Refresh();

    [RelayCommand] private void ClearFilters() { FilterAccount = null; FilterCategory = null; FromText = ""; ToText = ""; }

    [RelayCommand]
    private void BrowseReceipt()
    {
        var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.pdf|All files|*.*" };
        if (dlg.ShowDialog() == true) Receipt = dlg.FileName;
    }

    [RelayCommand]
    private void Add() => Run(() =>
    {
        if (Account == null) { Ui.Error("Choose an account."); return; }
        if (Amount <= 0) { Ui.Error("Enter an amount greater than zero."); return; }
        using var db = new AppDb();
        db.Transactions.Add(new Transaction
        {
            AccountId = Account.Id, CategoryId = Category?.Id, Amount = Amount, Date = Date, Kind = Kind,
            Note = Note, ReceiptPath = FinanceService.StoreReceipt(Receipt),
        });
        db.SaveChanges();
        Amount = 0; Note = ""; Receipt = "";
        LoadLookups(); Refresh();
        Status = "Transaction saved.";
    });

    [RelayCommand] private void AddLine() => Lines.Add(new SplitLine());
    [RelayCommand] private void RemoveLine(SplitLine line) { if (Lines.Count > 1) Lines.Remove(line); }

    [RelayCommand]
    private void SaveSplit() => Run(() =>
    {
        var valid = Lines.Where(l => l.Category != null && l.Amount > 0).ToList();
        if (Account == null || valid.Count < 2) { Ui.Error("Choose an account and at least two lines with a category and amount."); return; }
        using var db = new AppDb();
        FinanceService.AddSplit(db, Account.Id, Date, Note, valid.Select(l => (l.Category!.Id, l.Amount)), FinanceService.StoreReceipt(Receipt));
        SplitTotal = valid.Sum(l => l.Amount);
        Lines.Clear(); Lines.Add(new SplitLine()); Lines.Add(new SplitLine());
        Note = ""; Receipt = "";
        LoadLookups(); Refresh();
        Status = $"Split bill saved ({SplitTotal:N2}).";
    });

    [RelayCommand]
    private void OpenReceipt(Transaction? t)
    {
        if (t == null || !t.HasReceipt) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(t.ReceiptPath!) { UseShellExecute = true }); }
        catch (Exception ex) { Ui.Error("Could not open the receipt: " + ex.Message); }
    }

    [RelayCommand]
    private void DeleteTransaction(Transaction? t)
    {
        if (t == null) return;
        Selected = t;
        DeleteSelected();
    }

    [RelayCommand]
    private void DeleteSelected() => Run(() =>
    {
        if (Selected == null) return;
        if (!Ui.Confirm(Selected.TransferGroup != null ? "Delete this transfer (both legs)?" : Selected.SplitGroup != null ? "Delete this whole split bill?" : "Delete this transaction?")) return;
        using var db = new AppDb();
        var t = Selected;
        var victims = t.TransferGroup != null ? db.Transactions.Where(x => x.TransferGroup == t.TransferGroup)
                    : t.SplitGroup != null ? db.Transactions.Where(x => x.SplitGroup == t.SplitGroup)
                    : db.Transactions.Where(x => x.Id == t.Id);
        db.Transactions.RemoveRange(victims.ToList());
        db.SaveChanges();
        Refresh();
    });

    [RelayCommand]
    private void AddRecurring() => Run(() =>
    {
        if (RecAccount == null || RecAmount <= 0 || string.IsNullOrWhiteSpace(RecName)) { Ui.Error("Enter a name, amount and account."); return; }
        using var db = new AppDb();
        db.RecurringIncomes.Add(new RecurringIncome
        {
            Name = RecName.Trim(), Amount = RecAmount, AccountId = RecAccount.Id, CategoryId = RecCategory?.Id,
            Cycle = RecCycle, NextDate = RecNext,
        });
        db.SaveChanges();
        var posted = FinanceService.PostRecurringIncome(db);
        RecName = ""; RecAmount = 0;
        LoadLookups(); Refresh();
        Status = posted > 0 ? $"Saved. {posted} past occurrence(s) were posted to your account." : "Recurring income saved.";
    });

    [RelayCommand]
    private void DeleteRecurring() => Run(() =>
    {
        if (SelectedRecurring == null) return;
        if (!Ui.Confirm($"Delete the recurring income '{SelectedRecurring.Name}'? Past payments stay in your history.")) return;
        using var db = new AppDb();
        db.RecurringIncomes.Remove(db.RecurringIncomes.Find(SelectedRecurring.Id)!);
        db.SaveChanges();
        LoadLookups();
    });
}

// ===================== Budgets =====================
public partial class BudgetsViewModel : PageViewModel
{
    [ObservableProperty] private List<BudgetStatus> _statuses = new();
    [ObservableProperty] private List<Category> _categories = new();
    [ObservableProperty] private Category? _category;
    [ObservableProperty] private decimal _limit;
    [ObservableProperty] private BudgetStatus? _selected;

    public BudgetsViewModel() => Load();

    [RelayCommand]
    private void OpenSet()
    {
        Category = null; Limit = 0;
        OpenForm("Set a budget", "", "Save budget", "BudgetForm", SaveCommand);
    }

    private void Load()
    {
        using var db = new AppDb();
        Categories = FinanceService.LoadCategories(db, false);
        Statuses = FinanceService.Budgets(db, DateTime.Today);
    }

    [RelayCommand]
    private void Save() => Run(() =>
    {
        if (Category == null || Limit <= 0) { Ui.Error("Choose a category and a limit."); return; }
        using var db = new AppDb();
        var b = db.Budgets.FirstOrDefault(x => x.CategoryId == Category.Id);
        if (b == null) db.Budgets.Add(new Budget { CategoryId = Category.Id, MonthlyLimit = Limit });
        else b.MonthlyLimit = Limit;
        db.SaveChanges();
        Limit = 0;
        Load();
        Status = "Budget saved. You get warnings at 80% and 100%.";
    });

    [RelayCommand]
    private void Delete() => Run(() =>
    {
        if (Selected == null) return;
        if (!Ui.Confirm($"Delete the budget for '{Selected.Category}'?")) return;
        using var db = new AppDb();
        var cat = db.Categories.FirstOrDefault(c => c.Name == Selected.Category);
        var b = db.Budgets.FirstOrDefault(x => x.CategoryId == cat!.Id);
        if (b != null) { db.Budgets.Remove(b); db.SaveChanges(); }
        Load();
    });
}
