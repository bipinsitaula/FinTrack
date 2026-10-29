using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceTracker.Models;

public enum AccountType { Bank, EWallet, Cash, CreditCard }
public enum TxKind { Income, Expense, TransferIn, TransferOut }
public enum LoanKind { Emi, Fixed }
public enum DebtDirection { Gave, Took }
public enum BillCycle { Weekly, Monthly, Quarterly, Yearly }
public enum DepositKind { FixedDeposit, RecurringDeposit }
public enum InvestmentType { Shares, MutualFund, Gold, Other }

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal InterestRate { get; set; }
    /// <summary>Credit card payment due day of month (1-28); 0 when unused.</summary>
    public int DueDay { get; set; }
    public decimal CreditLimit { get; set; }

    [NotMapped] public decimal Balance { get; set; }
    [NotMapped] public string TypeLabel => Type.ToString();
    public override string ToString() => Name;
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public bool IsIncome { get; set; }
    public bool TaxDeductible { get; set; }
    [NotMapped] public string FullName { get; set; } = "";
    public override string ToString() => string.IsNullOrEmpty(FullName) ? Name : FullName;
}

public class Transaction
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public TxKind Kind { get; set; }
    public int AccountId { get; set; }
    public Account? Account { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Note { get; set; } = "";
    public string? ReceiptPath { get; set; }
    /// <summary>Links the two legs of a transfer.</summary>
    public Guid? TransferGroup { get; set; }
    /// <summary>Links the lines of a split bill.</summary>
    public Guid? SplitGroup { get; set; }

    [NotMapped] public decimal Signed => Kind is TxKind.Income or TxKind.TransferIn ? Amount : -Amount;
    [NotMapped] public string AccountName => Account?.Name ?? "";
    [NotMapped] public string CategoryName => Category?.Name ?? (Kind is TxKind.TransferIn or TxKind.TransferOut ? "Transfer" : "");
}

public class Budget
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public decimal MonthlyLimit { get; set; }
}

public class RecurringIncome
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public int AccountId { get; set; }
    public int? CategoryId { get; set; }
    public BillCycle Cycle { get; set; } = BillCycle.Monthly;
    public DateTime NextDate { get; set; } = DateTime.Today;
    public bool Active { get; set; } = true;
}

public class Loan
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public LoanKind Kind { get; set; }
    public decimal Principal { get; set; }
    public decimal AnnualRate { get; set; }
    public int TenureMonths { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public decimal Emi { get; set; }
    public List<LoanPayment> Payments { get; set; } = new();

    [NotMapped] public DateTime MaturityDate => StartDate.AddMonths(TenureMonths);
    [NotMapped] public decimal RemainingPrincipal => Payments.Where(p => !p.Paid).Sum(p => p.PrincipalPart);
    [NotMapped] public int PaidCount => Payments.Count(p => p.Paid);
    [NotMapped] public string PaidLabel => $"{PaidCount} / {Payments.Count}";
    [NotMapped] public string NextDueAlt => NextDue is DateTime d ? Services.DateDisplay.Alt(d) : "";
    [NotMapped] public DateTime? NextDue => Payments.Where(p => !p.Paid).OrderBy(p => p.DueDate).Select(p => (DateTime?)p.DueDate).FirstOrDefault();
}

public class LoanPayment
{
    public int Id { get; set; }
    public int LoanId { get; set; }
    public int Number { get; set; }
    public DateTime DueDate { get; set; }
    public decimal PrincipalPart { get; set; }
    public decimal InterestPart { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public bool Paid { get; set; }
    public DateTime? PaidDate { get; set; }
}

/// <summary>Informal loan between you and a friend or family member.</summary>
public class Debt
{
    public int Id { get; set; }
    public string Person { get; set; } = "";
    public DebtDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime? DueDate { get; set; }
    public string Note { get; set; } = "";
    public List<DebtRepayment> Repayments { get; set; } = new();

    [NotMapped] public string DueAlt => DueDate is DateTime d ? Services.DateDisplay.Alt(d) : "";
    [NotMapped] public decimal Repaid => Repayments.Sum(r => r.Amount);
    [NotMapped] public decimal Outstanding => Amount - Repaid;
}

public class DebtRepayment
{
    public int Id { get; set; }
    public int DebtId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
}

public class Subscription
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public BillCycle Cycle { get; set; } = BillCycle.Monthly;
    public DateTime NextDue { get; set; } = DateTime.Today;
    public int RemindDaysBefore { get; set; } = 3;
    public int? AccountId { get; set; }
    public int? CategoryId { get; set; }
    public bool Active { get; set; } = true;

    [NotMapped] public decimal MonthlyCost => Cycle switch
    {
        BillCycle.Weekly => Amount * 52m / 12m,
        BillCycle.Monthly => Amount,
        BillCycle.Quarterly => Amount / 3m,
        _ => Amount / 12m,
    };
    [NotMapped] public decimal YearlyCost => MonthlyCost * 12m;
}

public class Goal
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Target { get; set; }
    public DateTime Deadline { get; set; } = DateTime.Today.AddYears(1);
    public List<GoalContribution> Contributions { get; set; } = new();

    [NotMapped] public decimal Saved => Contributions.Sum(c => c.Amount);
    [NotMapped] public double Progress => Target <= 0 ? 0 : (double)Math.Min(100m, Saved / Target * 100m);
    [NotMapped] public decimal MonthlyNeeded
    {
        get
        {
            var months = Math.Max(1, (int)Math.Ceiling((Deadline - DateTime.Today).TotalDays / 30.4375));
            return Math.Max(0, Target - Saved) / months;
        }
    }
}

public class GoalContribution
{
    public int Id { get; set; }
    public int GoalId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
}

public class Deposit
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DepositKind Kind { get; set; }
    /// <summary>Lump sum for FD, monthly installment for RD.</summary>
    public decimal Amount { get; set; }
    public decimal AnnualRate { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public int TermMonths { get; set; } = 12;

    [NotMapped] public DateTime MaturityDate => StartDate.AddMonths(TermMonths);
    [NotMapped] public int MonthsElapsed => Math.Max(0, (DateTime.Today.Year - StartDate.Year) * 12 + DateTime.Today.Month - StartDate.Month);
    [NotMapped] public decimal Invested => Kind == DepositKind.FixedDeposit ? Amount : Amount * Math.Min(TermMonths, MonthsElapsed + 1);
    [NotMapped] public decimal MaturityAmount => Services.LoanCalculator.DepositMaturity(Kind, Amount, AnnualRate, TermMonths);
}

public class Insurance
{
    public int Id { get; set; }
    public string Policy { get; set; } = "";
    public string Provider { get; set; } = "";
    public decimal Premium { get; set; }
    public BillCycle Cycle { get; set; } = BillCycle.Yearly;
    public DateTime DueDate { get; set; } = DateTime.Today.AddMonths(1);
    public decimal Coverage { get; set; }
}

public class Investment
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public InvestmentType Type { get; set; }
    public decimal PurchaseValue { get; set; }
    public decimal CurrentValue { get; set; }
    public DateTime PurchaseDate { get; set; } = DateTime.Today;

    [NotMapped] public decimal Gain => CurrentValue - PurchaseValue;
    [NotMapped] public decimal GainPct => PurchaseValue == 0 ? 0 : Gain / PurchaseValue * 100m;
}

public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
