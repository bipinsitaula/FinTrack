using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace FinanceTracker.ViewModels;

// ===================== Bills & subscriptions, insurance =====================
public partial class BillsViewModel : PageViewModel
{
    public Array Cycles { get; } = Enum.GetValues<BillCycle>();

    [ObservableProperty] private List<Subscription> _subs = new();
    [ObservableProperty] private Subscription? _selected;
    [ObservableProperty] private List<Subscription> _top = new();
    [ObservableProperty] private List<Account> _accounts = new();
    [ObservableProperty] private Account? _payFrom;
    [ObservableProperty] private decimal _monthlyTotal;
    [ObservableProperty] private decimal _yearlyTotal;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private BillCycle _cycle = BillCycle.Monthly;
    [ObservableProperty] private DateTime _nextDue = DateTime.Today.AddDays(7);
    [ObservableProperty] private int _remind = 3;

    // insurance
    [ObservableProperty] private List<Insurance> _policies = new();
    [ObservableProperty] private Insurance? _selectedPolicy;
    [ObservableProperty] private string _policy = "";
    [ObservableProperty] private string _provider = "";
    [ObservableProperty] private decimal _premium;
    [ObservableProperty] private BillCycle _premiumCycle = BillCycle.Yearly;
    [ObservableProperty] private DateTime _policyDue = DateTime.Today.AddMonths(1);
    [ObservableProperty] private decimal _coverage;

    public BillsViewModel() => Load();

    [RelayCommand]
    private void OpenAddBill()
    {
        Name = ""; Amount = 0; Cycle = BillCycle.Monthly; NextDue = DateTime.Today.AddDays(7); Remind = 3;
        OpenForm("Add a bill or subscription", "", "Add bill", "BillForm", AddCommand);
    }

    [RelayCommand]
    private void OpenAddPolicy()
    {
        Policy = ""; Provider = ""; Premium = 0; Coverage = 0; PremiumCycle = BillCycle.Yearly; PolicyDue = DateTime.Today.AddMonths(1);
        OpenForm("Add an insurance policy", "", "Add policy", "PolicyForm", AddPolicyCommand);
    }

    private void Load()
    {
        using var db = new AppDb();
        Accounts = FinanceService.LoadAccounts(db).Where(a => a.Type != AccountType.CreditCard).ToList();
        PayFrom ??= Accounts.FirstOrDefault();
        Subs = db.Subscriptions.AsNoTracking().Where(s => s.Active).ToList().OrderBy(s => s.NextDue).ToList();
        Top = Subs.OrderByDescending(s => s.MonthlyCost).Take(5).ToList();
        MonthlyTotal = Subs.Sum(s => s.MonthlyCost);
        YearlyTotal = Subs.Sum(s => s.YearlyCost);
        Policies = db.Insurances.AsNoTracking().ToList().OrderBy(p => p.DueDate).ToList();
    }

    [RelayCommand]
    private void Add() => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(Name) || Amount <= 0) { Ui.Error("Enter a name and amount."); return; }
        using var db = new AppDb();
        db.Subscriptions.Add(new Subscription { Name = Name.Trim(), Amount = Amount, Cycle = Cycle, NextDue = NextDue, RemindDaysBefore = Remind });
        db.SaveChanges();
        Name = ""; Amount = 0;
        Load();
    });

    [RelayCommand]
    private void MarkPaid() => Run(() =>
    {
        if (Selected == null || PayFrom == null) { Ui.Error("Select a bill and an account to pay from."); return; }
        if (!Ui.Confirm($"Pay {Selected.Name} ({Selected.Amount:N2}) from {PayFrom.Name}? A transaction will be recorded and the next due date moves forward.",
                        "Mark bill paid", "Pay now")) return;
        using var db = new AppDb();
        FinanceService.PaySubscription(db, Selected.Id, PayFrom.Id);
        Load();
        Status = "Paid. A transaction was created and the next due date moved forward.";
    });

    [RelayCommand]
    private void Delete() => Run(() =>
    {
        if (Selected == null) return;
        if (!Ui.Confirm($"Delete '{Selected.Name}' from your subscriptions and bills?")) return;
        using var db = new AppDb();
        db.Subscriptions.Remove(db.Subscriptions.Find(Selected.Id)!);
        db.SaveChanges();
        Load();
    });

    [RelayCommand]
    private void AddPolicy() => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(Policy) || Premium <= 0) { Ui.Error("Enter the policy name and premium."); return; }
        using var db = new AppDb();
        db.Insurances.Add(new Insurance { Policy = Policy.Trim(), Provider = Provider, Premium = Premium, Cycle = PremiumCycle, DueDate = PolicyDue, Coverage = Coverage });
        db.SaveChanges();
        Policy = ""; Provider = ""; Premium = 0; Coverage = 0;
        Load();
    });

    [RelayCommand]
    private void PayPremium() => Run(() =>
    {
        if (SelectedPolicy == null || PayFrom == null) { Ui.Error("Select a policy and an account."); return; }
        if (!Ui.Confirm($"Pay the {SelectedPolicy.Premium:N2} premium for '{SelectedPolicy.Policy}' from {PayFrom.Name}?", "Pay premium", "Pay now")) return;
        using var db = new AppDb();
        var p = db.Insurances.Find(SelectedPolicy.Id)!;
        db.Transactions.Add(new Transaction
        {
            AccountId = PayFrom.Id, Amount = p.Premium, Date = DateTime.Today, Kind = TxKind.Expense,
            CategoryId = db.Categories.FirstOrDefault(c => c.Name == "Insurance")?.Id, Note = $"{p.Policy} premium",
        });
        p.DueDate = FinanceService.Advance(p.DueDate, p.Cycle);
        db.SaveChanges();
        Load();
        Status = "Premium paid and recorded.";
    });

    [RelayCommand]
    private void DeletePolicy() => Run(() =>
    {
        if (SelectedPolicy == null) return;
        if (!Ui.Confirm($"Delete the policy '{SelectedPolicy.Policy}'?")) return;
        using var db = new AppDb();
        db.Insurances.Remove(db.Insurances.Find(SelectedPolicy.Id)!);
        db.SaveChanges();
        Load();
    });
}

// ===================== Goals & deposits =====================
public partial class GoalsViewModel : PageViewModel
{
    public Array DepositKinds { get; } = Enum.GetValues<DepositKind>();

    [ObservableProperty] private List<Goal> _goals = new();
    [ObservableProperty] private Goal? _selected;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private decimal _target;
    [ObservableProperty] private DateTime _deadline = DateTime.Today.AddYears(1);
    [ObservableProperty] private decimal _contribution;

    [ObservableProperty] private List<Deposit> _deposits = new();
    [ObservableProperty] private Deposit? _selectedDeposit;
    [ObservableProperty] private string _depName = "";
    [ObservableProperty] private DepositKind _depKind = DepositKind.FixedDeposit;
    [ObservableProperty] private decimal _depAmount;
    [ObservableProperty] private decimal _depRate = 8;
    [ObservableProperty] private int _depTerm = 12;
    [ObservableProperty] private DateTime _depStart = DateTime.Today;
    [ObservableProperty] private string _depPreview = "";

    // savings rate
    [ObservableProperty] private decimal _rateIncome;
    [ObservableProperty] private decimal _rateExpense;
    [ObservableProperty] private string _rateText = "";

    public GoalsViewModel()
    {
        using var db = new AppDb();
        (RateIncome, RateExpense) = FinanceService.MonthTotals(db, DateTime.Today);
        Load();
        CalcRate();
    }

    [RelayCommand]
    private void OpenAddGoal()
    {
        Name = ""; Target = 0; Deadline = DateTime.Today.AddYears(1);
        OpenForm("Create a goal", "", "Create goal", "GoalForm", AddGoalCommand);
    }

    [RelayCommand]
    private void OpenContribute()
    {
        if (Selected == null) { Ui.Error("Select a goal first."); return; }
        Contribution = 0;
        OpenForm("Add money to goal", "", "Add money", "ContributeForm", ContributeCommand);
    }

    [RelayCommand]
    private void OpenAddDeposit()
    {
        DepName = ""; DepAmount = 0; DepRate = 8; DepTerm = 12; DepStart = DateTime.Today; DepKind = DepositKind.FixedDeposit; DepPreview = "";
        OpenForm("Add a fixed or recurring deposit", "", "Add deposit", "DepositForm", AddDepositCommand);
    }

    partial void OnRateIncomeChanged(decimal value) => CalcRate();
    partial void OnRateExpenseChanged(decimal value) => CalcRate();
    partial void OnDepAmountChanged(decimal value) => CalcDeposit();
    partial void OnDepRateChanged(decimal value) => CalcDeposit();
    partial void OnDepTermChanged(int value) => CalcDeposit();
    partial void OnDepKindChanged(DepositKind value) => CalcDeposit();

    private void CalcRate()
    {
        if (RateIncome <= 0) { RateText = "Enter your monthly income."; return; }
        var saved = RateIncome - RateExpense;
        var pct = saved / RateIncome * 100;
        RateText = $"Savings rate {pct:N1}% · saving {saved:N0} a month · {(pct >= 20 ? "great, you are above the 20% target" : pct >= 10 ? "decent, aim for 20%" : "low, try trimming expenses")}";
    }

    private void CalcDeposit()
    {
        if (DepAmount <= 0 || DepTerm <= 0) { DepPreview = ""; return; }
        var m = LoanCalculator.DepositMaturity(DepKind, DepAmount, DepRate, DepTerm);
        var invested = DepKind == DepositKind.FixedDeposit ? DepAmount : DepAmount * DepTerm;
        DepPreview = $"Matures at {m:N0} (interest {m - invested:N0})";
    }

    private void Load()
    {
        using var db = new AppDb();
        Goals = db.Goals.Include(g => g.Contributions).AsNoTracking().OrderBy(g => g.Deadline).ToList();
        Deposits = db.Deposits.AsNoTracking().ToList().OrderBy(d => d.MaturityDate).ToList();
    }

    [RelayCommand]
    private void AddGoal() => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(Name) || Target <= 0) { Ui.Error("Enter a goal name and target."); return; }
        using var db = new AppDb();
        db.Goals.Add(new Goal { Name = Name.Trim(), Target = Target, Deadline = Deadline });
        db.SaveChanges();
        Name = ""; Target = 0;
        Load();
    });

    [RelayCommand]
    private void Contribute() => Run(() =>
    {
        if (Selected == null || Contribution <= 0) { Ui.Error("Select a goal and enter an amount."); return; }
        using var db = new AppDb();
        db.GoalContributions.Add(new GoalContribution { GoalId = Selected.Id, Amount = Contribution });
        db.SaveChanges();
        Contribution = 0;
        Load();
    });

    [RelayCommand]
    private void DeleteGoal() => Run(() =>
    {
        if (Selected == null || !Ui.Confirm($"Delete goal '{Selected.Name}'?")) return;
        using var db = new AppDb();
        db.Goals.Remove(db.Goals.Find(Selected.Id)!);
        db.SaveChanges();
        Load();
    });

    [RelayCommand]
    private void AddDeposit() => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(DepName) || DepAmount <= 0) { Ui.Error("Enter a name and amount."); return; }
        using var db = new AppDb();
        db.Deposits.Add(new Deposit { Name = DepName.Trim(), Kind = DepKind, Amount = DepAmount, AnnualRate = DepRate, TermMonths = DepTerm, StartDate = DepStart });
        db.SaveChanges();
        DepName = ""; DepAmount = 0;
        Load();
    });

    [RelayCommand]
    private void DeleteDeposit() => Run(() =>
    {
        if (SelectedDeposit == null) return;
        if (!Ui.Confirm($"Delete the deposit '{SelectedDeposit.Name}'?")) return;
        using var db = new AppDb();
        db.Deposits.Remove(db.Deposits.Find(SelectedDeposit.Id)!);
        db.SaveChanges();
        Load();
    });
}

// ===================== Investments, tax, bill split, currency =====================
public record TaxLine(string Item, decimal Amount);

public partial class InvestmentsViewModel : PageViewModel
{
    public Array Types { get; } = Enum.GetValues<InvestmentType>();

    [ObservableProperty] private List<Investment> _items = new();
    [ObservableProperty] private Investment? _selected;
    [ObservableProperty] private decimal _totalPurchase;
    [ObservableProperty] private decimal _totalCurrent;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private InvestmentType _type = InvestmentType.Shares;
    [ObservableProperty] private decimal _purchase;
    [ObservableProperty] private decimal _current;
    [ObservableProperty] private decimal _newValue;

    // tax
    [ObservableProperty] private int _taxYear = DateDisplay.YearOf(DateTime.Today);
    public string TaxYearLabel => DateDisplay.UseBs ? "Year (BS)" : "Year (AD)";
    [ObservableProperty] private decimal _taxIncome;
    [ObservableProperty] private decimal _taxDeductions;
    [ObservableProperty] private decimal _taxable;
    [ObservableProperty] private List<TaxLine> _taxLines = new();

    // bill split
    [ObservableProperty] private string _billTitle = "Group dinner";
    [ObservableProperty] private decimal _billTotal;
    [ObservableProperty] private string _people = "";
    [ObservableProperty] private string _splitResult = "";

    // currency
    [ObservableProperty] private decimal _fxAmount = 100;
    [ObservableProperty] private string _fxFrom = "USD";
    [ObservableProperty] private string _fxTo = "NPR";
    [ObservableProperty] private string _fxResult = "";
    public string[] Currencies { get; } = { "NPR", "USD", "INR" };

    public InvestmentsViewModel() { Load(); CalcTax(); CalcFx(); }

    [RelayCommand]
    private void OpenAddInvestment()
    {
        Name = ""; Type = InvestmentType.Shares; Purchase = 0; Current = 0;
        OpenForm("Add an investment", "Shares, mutual funds, gold and more.", "Add investment", "InvestmentForm", AddCommand);
    }

    [RelayCommand]
    private void OpenUpdateValue()
    {
        if (Selected == null) { Ui.Error("Select an investment first."); return; }
        NewValue = Selected.CurrentValue;
        OpenForm("Update current value", "", "Update value", "ValueForm", UpdateValueCommand);
    }

    [RelayCommand]
    private void OpenSplitBill()
    {
        BillTitle = "Group dinner"; BillTotal = 0; People = "";
        OpenForm("Split a bill", "", "Split equally", "SplitBillForm", SplitBillCommand);
    }

    partial void OnTaxYearChanged(int value) => CalcTax();
    partial void OnFxAmountChanged(decimal value) => CalcFx();
    partial void OnFxFromChanged(string value) => CalcFx();
    partial void OnFxToChanged(string value) => CalcFx();

    private void Load()
    {
        using var db = new AppDb();
        Items = db.Investments.AsNoTracking().OrderBy(i => i.Name).ToList();
        TotalPurchase = Items.Sum(i => i.PurchaseValue);
        TotalCurrent = Items.Sum(i => i.CurrentValue);
    }

    [RelayCommand]
    private void Add() => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(Name) || Purchase <= 0) { Ui.Error("Enter a name and purchase value."); return; }
        using var db = new AppDb();
        db.Investments.Add(new Investment { Name = Name.Trim(), Type = Type, PurchaseValue = Purchase, CurrentValue = Current > 0 ? Current : Purchase });
        db.SaveChanges();
        Name = ""; Purchase = 0; Current = 0;
        Load();
    });

    [RelayCommand]
    private void UpdateValue() => Run(() =>
    {
        if (Selected == null || NewValue <= 0) { Ui.Error("Select an investment and enter its current value."); return; }
        using var db = new AppDb();
        db.Investments.Find(Selected.Id)!.CurrentValue = NewValue;
        db.SaveChanges();
        NewValue = 0;
        Load();
    });

    [RelayCommand]
    private void Delete() => Run(() =>
    {
        if (Selected == null) return;
        if (!Ui.Confirm($"Delete the investment '{Selected.Name}'?")) return;
        using var db = new AppDb();
        db.Investments.Remove(db.Investments.Find(Selected.Id)!);
        db.SaveChanges();
        Load();
    });

    private void CalcTax()
    {
        using var db = new AppDb();
        var txs = db.Transactions.Include(t => t.Category).AsNoTracking().ToList().Where(t => DateDisplay.YearOf(t.Date) == TaxYear).ToList();
        TaxIncome = txs.Where(t => t.Kind == TxKind.Income).Sum(t => t.Amount);
        var cats = db.Categories.AsNoTracking().ToDictionary(c => c.Id);
        var lines = txs.Where(t => t.Kind == TxKind.Expense && t.CategoryId is int)
            .Where(t => cats.TryGetValue(t.CategoryId!.Value, out var c) && c.TaxDeductible)
            .GroupBy(t => t.CategoryName).Select(g => new TaxLine(g.Key, g.Sum(t => t.Amount))).OrderByDescending(l => l.Amount).ToList();
        TaxLines = lines;
        TaxDeductions = lines.Sum(l => l.Amount);
        Taxable = Math.Max(0, TaxIncome - TaxDeductions);
    }

    [RelayCommand]
    private void SplitBill() => Run(() =>
    {
        var names = People.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (BillTotal <= 0 || names.Count == 0) { Ui.Error("Enter the bill total and the names of the others, separated by commas."); return; }
        var share = Math.Round(BillTotal / (names.Count + 1), 2);
        using var db = new AppDb();
        foreach (var n in names)
            db.Debts.Add(new Debt { Person = n, Direction = DebtDirection.Gave, Amount = share, Note = $"{BillTitle} (equal split)", DueDate = DateTime.Today.AddDays(14) });
        db.SaveChanges();
        SplitResult = $"Each person pays {share:N2}. {names.Count} entries were added under 'Loans › Friends & family' so you can track who owes you.";
        People = "";
    });

    private decimal Rate(AppDb db, string cur) => cur switch
    {
        "USD" => decimal.TryParse(FinanceService.GetSetting(db, "UsdRate"), out var u) ? u : 133.5m,
        "INR" => decimal.TryParse(FinanceService.GetSetting(db, "InrRate"), out var i) ? i : 1.6m,
        _ => 1m,
    };

    private void CalcFx()
    {
        using var db = new AppDb();
        var npr = FxAmount * Rate(db, FxFrom);
        FxResult = $"{FxAmount:N2} {FxFrom} = {npr / Rate(db, FxTo):N2} {FxTo}";
    }
}

// ===================== Insights =====================
public class InsightsViewModel
{
    public HealthScore Health { get; }
    public List<ForecastMonth> Forecast { get; }
    public List<string> Anomalies { get; }
    public string Advice { get; }

    public InsightsViewModel()
    {
        using var db = new AppDb();
        Health = FinanceService.Health(db);
        Forecast = FinanceService.Forecast(db, 3);
        Anomalies = FinanceService.Anomalies(db);
        var weakest = new[] { ("saving more of your income", Health.Savings), ("lowering your EMI burden", Health.Debt), ("building an emergency fund of 6 months", Health.Emergency), ("keeping within your budgets", Health.Budget) }
            .OrderBy(x => x.Item2).First();
        Advice = Health.Total >= 80 ? "Excellent shape. Keep it up." : $"Biggest opportunity: {weakest.Item1}.";
    }
}

// ===================== Settings =====================
public partial class SettingsViewModel : PageViewModel
{
    [ObservableProperty] private DateTime _reportMonth = DateDisplay.MonthRange(DateTime.Today).Start;
    [ObservableProperty] private decimal _usd;
    [ObservableProperty] private decimal _inr;
    [ObservableProperty] private bool _hasPin = SecurityService.HasPin();
    [ObservableProperty] private string _userName = ProfileService.GetName();

    public string DataPath => Data.AppDb.DbPath;

    public SettingsViewModel()
    {
        using var db = new AppDb();
        Usd = decimal.TryParse(FinanceService.GetSetting(db, "UsdRate"), out var u) ? u : 133.5m;
        Inr = decimal.TryParse(FinanceService.GetSetting(db, "InrRate"), out var i) ? i : 1.6m;
    }

    [RelayCommand]
    private void SaveName() => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(UserName)) { Ui.Error("Please enter your name."); return; }
        ProfileService.SetName(UserName);
        Status = "Name saved.";
    });

    [RelayCommand]
    private void SaveRates() => Run(() =>
    {
        using var db = new AppDb();
        FinanceService.SetSetting(db, "UsdRate", Usd.ToString());
        FinanceService.SetSetting(db, "InrRate", Inr.ToString());
        Status = "Exchange rates saved (NPR per 1 USD / 1 INR).";
    });

    [RelayCommand]
    private void ExportPdf() => Run(() =>
    {
        var dlg = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = $"report-{ReportMonth:yyyy-MM}.pdf" };
        if (dlg.ShowDialog() != true) return;
        ExportService.MonthEndPdf(ReportMonth, dlg.FileName);
        Status = "PDF report saved.";
    });

    [RelayCommand]
    private void ExportExcel() => Run(() =>
    {
        var dlg = new SaveFileDialog { Filter = "Excel|*.xlsx", FileName = $"report-{ReportMonth:yyyy-MM}.xlsx" };
        if (dlg.ShowDialog() != true) return;
        ExportService.MonthEndExcel(ReportMonth, dlg.FileName);
        Status = "Excel report saved.";
    });

    public void Backup(string password) => Run(() =>
    {
        var dlg = new SaveFileDialog { Filter = "Finance backup|*.ftbak;*.db", FileName = $"finance-{DateTime.Today:yyyyMMdd}.{(string.IsNullOrEmpty(password) ? "db" : "ftbak")}" };
        if (dlg.ShowDialog() != true) return;
        BackupService.Backup(dlg.FileName, password);
        Status = string.IsNullOrEmpty(password) ? "Backup saved." : "Encrypted backup saved. Keep the password safe, it cannot be recovered.";
    });

    public void Restore(string password) => Run(() =>
    {
        var dlg = new OpenFileDialog { Filter = "Finance backup|*.ftbak;*.db|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        if (!Ui.Confirm("Restoring replaces ALL current data. A copy of the current file is kept as finance.db.before-restore. The app will close afterwards.", "Restore this backup?", "Restore")) return;
        BackupService.Restore(dlg.FileName, password);
        Ui.Info("Restore complete. The app will now close, please reopen it.");
        System.Windows.Application.Current.Shutdown();
    });

    public void SetPin(string pin) => Run(() =>
    {
        if (pin.Length > 0 && (pin.Length < 4 || !pin.All(char.IsDigit))) { Ui.Error("PIN must be at least 4 digits."); return; }
        if (pin.Length == 0 && HasPin && !Ui.Confirm("Remove the PIN lock? Anyone using this PC will be able to open your finances.", "Remove PIN lock?", "Remove")) return;
        SecurityService.SetPin(pin);
        HasPin = pin.Length > 0;
        Status = pin.Length > 0 ? "PIN lock enabled." : "PIN lock removed.";
    });
}
