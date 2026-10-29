using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.ViewModels;

// ===================== Loans & debts =====================
public partial class LoansViewModel : PageViewModel
{
    public Array LoanKinds { get; } = Enum.GetValues<LoanKind>();
    public Array Directions { get; } = Enum.GetValues<DebtDirection>();

    [ObservableProperty] private List<Loan> _loans = new();
    [ObservableProperty] private Loan? _selectedLoan;
    [ObservableProperty] private List<LoanPayment> _schedule = new();
    [ObservableProperty] private LoanPayment? _selectedPayment;
    [ObservableProperty] private List<Account> _accounts = new();
    [ObservableProperty] private Account? _payFrom;
    [ObservableProperty] private decimal _totalRemaining;
    [ObservableProperty] private decimal _totalEmi;

    // new loan
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private LoanKind _kind = LoanKind.Emi;
    [ObservableProperty] private decimal _principal;
    [ObservableProperty] private decimal _rate;
    [ObservableProperty] private int _tenure = 12;
    [ObservableProperty] private DateTime _start = DateTime.Today;
    [ObservableProperty] private string _preview = "";

    // informal debts
    [ObservableProperty] private List<Debt> _debts = new();
    [ObservableProperty] private Debt? _selectedDebt;
    [ObservableProperty] private string _person = "";
    [ObservableProperty] private DebtDirection _direction = DebtDirection.Gave;
    [ObservableProperty] private decimal _debtAmount;
    [ObservableProperty] private string _dueText = "";
    [ObservableProperty] private string _debtNote = "";
    [ObservableProperty] private decimal _repayAmount;
    [ObservableProperty] private decimal _owedToMe;
    [ObservableProperty] private decimal _iOwe;

    // planner
    [ObservableProperty] private decimal _extraPool = 2000;
    [ObservableProperty] private string _avalancheText = "";
    [ObservableProperty] private string _snowballText = "";
    [ObservableProperty] private string _plannerVerdict = "";
    [ObservableProperty] private string _plannerDebts = "";

    // prepayment
    [ObservableProperty] private decimal _extraMonthly;
    [ObservableProperty] private decimal _lumpSum;
    [ObservableProperty] private string _prepayText = "Select a loan on the Loans tab, then enter an extra amount.";

    private int _editLoanId;
    [ObservableProperty] private Loan? _payLoan;
    [ObservableProperty] private LoanPayment? _payInstalment;

    public LoansViewModel() => Load();

    [RelayCommand]
    private void OpenAddLoan()
    {
        Name = ""; Kind = LoanKind.Emi; Principal = 0; Rate = 0; Tenure = 12; Start = DateTime.Today; Preview = "";
        OpenForm("Add a loan", "An EMI loan gets a full amortization schedule.", "Add loan", "LoanForm", AddLoanCommand);
    }

    [RelayCommand]
    private void OpenAddDebt()
    {
        Person = ""; Direction = DebtDirection.Gave; DebtAmount = 0; DueText = ""; DebtNote = "";
        OpenForm("Add friends & family loan", "", "Add entry", "DebtForm", AddDebtCommand);
    }

    [RelayCommand]
    private void OpenRepay()
    {
        if (SelectedDebt == null) { Ui.Error("Select an entry in the table first."); return; }
        RepayAmount = 0;
        OpenForm("Record a repayment", "Partial repayments are allowed.", "Record repayment", "RepayForm", RepayCommand);
    }

    partial void OnSelectedLoanChanged(Loan? value)
    {
        Schedule = value?.Payments.OrderBy(p => p.Number).ToList() ?? new();
    }

    partial void OnPrincipalChanged(decimal value) => UpdatePreview();
    partial void OnRateChanged(decimal value) => UpdatePreview();
    partial void OnTenureChanged(int value) => UpdatePreview();
    partial void OnKindChanged(LoanKind value) => UpdatePreview();

    private void UpdatePreview()
    {
        if (Principal <= 0 || Tenure <= 0) { Preview = ""; return; }
        if (Kind == LoanKind.Emi)
        {
            var emi = LoanCalculator.Emi(Principal, Rate, Tenure);
            Preview = $"EMI {emi:N2} × {Tenure} months · total interest {emi * Tenure - Principal:N2}";
        }
        else
        {
            var s = LoanCalculator.BulletSchedule(Principal, Rate, Tenure, Start)[0];
            Preview = $"Pay {s.Amount:N2} on {DateDisplay.Full(s.DueDate)} (interest {s.InterestPart:N2})";
        }
    }

    private void Load()
    {
        var keep = SelectedLoan?.Id;
        using var db = new AppDb();
        Accounts = FinanceService.LoadAccounts(db).Where(a => a.Type != AccountType.CreditCard).ToList();
        PayFrom ??= Accounts.FirstOrDefault();
        Loans = db.Loans.Include(l => l.Payments).AsNoTracking().OrderBy(l => l.Name).ToList();
        SelectedLoan = Loans.FirstOrDefault(l => l.Id == keep);
        TotalRemaining = Loans.Sum(l => l.RemainingPrincipal);
        TotalEmi = Loans.Where(l => l.Kind == LoanKind.Emi && l.PaidCount < l.Payments.Count).Sum(l => l.Emi);

        Debts = db.Debts.Include(d => d.Repayments).AsNoTracking().OrderBy(d => d.DueDate ?? DateTime.MaxValue).ToList();
        OwedToMe = Debts.Where(d => d.Direction == DebtDirection.Gave).Sum(d => d.Outstanding);
        IOwe = Debts.Where(d => d.Direction == DebtDirection.Took).Sum(d => d.Outstanding);
        RunPlanner(db);
    }

    [RelayCommand]
    private void AddLoan() => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(Name) || Principal <= 0 || Tenure <= 0) { Ui.Error("Enter a name, principal and tenure."); return; }
        using var db = new AppDb();
        var loan = new Loan
        {
            Name = Name.Trim(), Kind = Kind, Principal = Principal, AnnualRate = Rate, TenureMonths = Tenure, StartDate = Start,
        };
        if (Kind == LoanKind.Emi)
        {
            loan.Emi = LoanCalculator.Emi(Principal, Rate, Tenure);
            loan.Payments = LoanCalculator.Schedule(Principal, Rate, Tenure, Start);
        }
        else
        {
            loan.Payments = LoanCalculator.BulletSchedule(Principal, Rate, Tenure, Start);
            loan.Emi = loan.Payments[0].Amount;
        }
        db.Loans.Add(loan);
        db.SaveChanges();
        Name = ""; Principal = 0; Rate = 0;
        Load();
        Status = "Loan added with a full schedule.";
    });

    [RelayCommand]
    private void DeleteLoan(Loan? loan) => Run(() =>
    {
        if (loan == null) return;
        if (!Ui.Confirm($"Delete the loan '{loan.Name}'? Its schedule and payment history are removed. Expenses already recorded stay in your transactions.")) return;
        using var db = new AppDb();
        db.Loans.Remove(db.Loans.Find(loan.Id)!);
        db.SaveChanges();
        if (SelectedLoan?.Id == loan.Id) SelectedLoan = null;
        Load();
        Status = $"'{loan.Name}' was deleted.";
    });

    [RelayCommand]
    private void OpenEditLoan(Loan? loan)
    {
        if (loan == null) return;
        _editLoanId = loan.Id;
        Name = loan.Name; Kind = loan.Kind; Principal = loan.Principal; Rate = loan.AnnualRate; Tenure = loan.TenureMonths; Start = loan.StartDate;
        OpenForm("Edit loan", "Changing the amounts rebuilds the schedule. Instalments already paid stay paid.", "Save changes", "LoanForm", SaveLoanEditCommand);
    }

    [RelayCommand]
    private void SaveLoanEdit() => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(Name) || Principal <= 0 || Tenure <= 0) { Ui.Error("Enter a name, principal and tenure."); return; }
        using var db = new AppDb();
        var loan = db.Loans.Include(l => l.Payments).First(l => l.Id == _editLoanId);
        var paid = loan.Payments.Where(p => p.Paid).OrderBy(p => p.Number).ToList();

        loan.Name = Name.Trim(); loan.Kind = Kind; loan.Principal = Principal; loan.AnnualRate = Rate; loan.TenureMonths = Tenure; loan.StartDate = Start;
        var schedule = Kind == LoanKind.Emi
            ? LoanCalculator.Schedule(Principal, Rate, Tenure, Start)
            : LoanCalculator.BulletSchedule(Principal, Rate, Tenure, Start);
        for (var i = 0; i < Math.Min(paid.Count, schedule.Count); i++) { schedule[i].Paid = true; schedule[i].PaidDate = paid[i].PaidDate; }
        loan.Emi = Kind == LoanKind.Emi ? LoanCalculator.Emi(Principal, Rate, Tenure) : schedule[0].Amount;

        db.LoanPayments.RemoveRange(loan.Payments.ToList());
        loan.Payments.Clear();
        foreach (var p in schedule) loan.Payments.Add(p);
        db.SaveChanges();
        Load();
        Status = $"'{loan.Name}' was updated.";
    });

    [RelayCommand]
    private void OpenPayInstalment(Loan? loan)
    {
        if (loan == null) return;
        var next = loan.Payments.Where(p => !p.Paid).OrderBy(p => p.Number).FirstOrDefault();
        if (next == null) { Ui.Info($"'{loan.Name}' is fully paid.", "Nothing left to pay"); return; }
        PayLoan = loan;
        PayInstalment = next;
        PayFrom ??= Accounts.FirstOrDefault();
        OpenForm("Mark instalment paid", "", "Mark paid", "PayInstalmentForm", ConfirmPayInstalmentCommand);
    }

    [RelayCommand]
    private void ConfirmPayInstalment() => Run(() =>
    {
        if (PayInstalment == null || PayFrom == null) { Ui.Error("Choose the account to pay from."); return; }
        using var db = new AppDb();
        FinanceService.PayLoanInstallment(db, PayInstalment.Id, PayFrom.Id);
        var name = PayLoan?.Name;
        Load();
        Status = $"Instalment #{PayInstalment.Number} of '{name}' marked paid.";
    });


    // ----- informal debts -----
    [RelayCommand]
    private void AddDebt() => Run(() =>
    {
        if (string.IsNullOrWhiteSpace(Person) || DebtAmount <= 0) { Ui.Error("Enter a person and amount."); return; }
        using var db = new AppDb();
        db.Debts.Add(new Debt
        {
            Person = Person.Trim(), Direction = Direction, Amount = DebtAmount, Note = DebtNote,
            DueDate = DateTime.TryParse(DueText, out var d) ? d : null,
        });
        db.SaveChanges();
        Person = ""; DebtAmount = 0; DebtNote = ""; DueText = "";
        Load();
    });

    [RelayCommand]
    private void Repay() => Run(() =>
    {
        if (SelectedDebt == null || RepayAmount <= 0) { Ui.Error("Select an entry and enter the amount repaid."); return; }
        if (RepayAmount > SelectedDebt.Outstanding) { Ui.Error($"Only {SelectedDebt.Outstanding:N2} is outstanding."); return; }
        using var db = new AppDb();
        db.DebtRepayments.Add(new DebtRepayment { DebtId = SelectedDebt.Id, Amount = RepayAmount });
        db.SaveChanges();
        RepayAmount = 0;
        Load();
    });

    [RelayCommand]
    private void DeleteDebt() => Run(() =>
    {
        if (SelectedDebt == null) return;
        if (!Ui.Confirm($"Delete the entry for '{SelectedDebt.Person}' ({SelectedDebt.Outstanding:N2} outstanding)?")) return;
        using var db = new AppDb();
        db.Debts.Remove(db.Debts.Find(SelectedDebt.Id)!);
        db.SaveChanges();
        Load();
    });

    // ----- planner -----
    partial void OnExtraPoolChanged(decimal value) { using var db = new AppDb(); RunPlanner(db); }

    private void RunPlanner(AppDb db)
    {
        var debts = Loans.Where(l => l.Kind == LoanKind.Emi && l.RemainingPrincipal > 0)
            .Select(l => new PayoffDebt(l.Name, l.RemainingPrincipal, l.AnnualRate, l.Emi)).ToList();
        foreach (var c in FinanceService.LoadAccounts(db).Where(a => a.Type == AccountType.CreditCard && a.Balance < 0))
            debts.Add(new PayoffDebt(c.Name, -c.Balance, c.InterestRate, Math.Max(500, Math.Round(-c.Balance * 0.05m))));
        if (debts.Count == 0)
        {
            AvalancheText = SnowballText = PlannerVerdict = "";
            PlannerDebts = "No EMI loans or credit card balances to plan.";
            return;
        }
        PlannerDebts = $"{debts.Count} debts · {debts.Sum(d => d.Balance):N0} owed · minimums {debts.Sum(d => d.MinPayment):N0}/month";
        var av = LoanCalculator.Payoff("Avalanche", debts, ExtraPool);
        var sn = LoanCalculator.Payoff("Snowball", debts, ExtraPool);
        AvalancheText = $"{av.Months} months · interest {av.TotalInterest:N0}";
        SnowballText = $"{sn.Months} months · interest {sn.TotalInterest:N0}";
        var diff = sn.TotalInterest - av.TotalInterest;
        PlannerVerdict = diff > 1
            ? $"Avalanche saves {diff:N0} in interest. Snowball clears the smallest debt first for quick wins."
            : "Both strategies cost about the same here, so pick the one that keeps you motivated.";
    }

    // ----- prepayment -----
    [RelayCommand]
    private void Simulate()
    {
        if (SelectedLoan == null || SelectedLoan.Kind != LoanKind.Emi) { PrepayText = "Select an EMI loan on the Loans tab first."; return; }
        var l = SelectedLoan;
        var bal = l.RemainingPrincipal;
        if (bal <= 0) { PrepayText = "This loan is fully paid."; return; }
        var r = LoanCalculator.Prepay(bal, l.AnnualRate, l.Emi, ExtraMonthly, LumpSum);
        PrepayText =
            $"Remaining principal: {bal:N0}\n" +
            $"As scheduled: {r.BaseMonths} months, {r.BaseInterest:N0} interest\n" +
            $"With prepayment: {r.NewMonths} months, {r.NewInterest:N0} interest\n" +
            $"You save {r.InterestSaved:N0} in interest and finish {r.MonthsSaved} months sooner.";
    }
}
