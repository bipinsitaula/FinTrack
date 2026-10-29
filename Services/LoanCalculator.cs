using FinanceTracker.Models;

namespace FinanceTracker.Services;

public record PayoffResult(string Strategy, int Months, decimal TotalInterest);
public record PrepaymentResult(decimal BaseInterest, decimal NewInterest, int BaseMonths, int NewMonths)
{
    public decimal InterestSaved => BaseInterest - NewInterest;
    public int MonthsSaved => BaseMonths - NewMonths;
}
public record PayoffDebt(string Name, decimal Balance, decimal AnnualRate, decimal MinPayment);

public static class LoanCalculator
{
    public static decimal Emi(decimal principal, decimal annualRatePct, int months)
    {
        if (months <= 0) return principal;
        var r = (double)annualRatePct / 12.0 / 100.0;
        if (r == 0) return Math.Round(principal / months, 2);
        var p = (double)principal;
        var f = Math.Pow(1 + r, months);
        return Math.Round((decimal)(p * r * f / (f - 1)), 2);
    }

    public static List<LoanPayment> Schedule(decimal principal, decimal annualRatePct, int months, DateTime start)
    {
        var list = new List<LoanPayment>();
        var emi = Emi(principal, annualRatePct, months);
        var r = annualRatePct / 12m / 100m;
        var bal = principal;
        for (var i = 1; i <= months; i++)
        {
            var interest = Math.Round(bal * r, 2);
            var princ = i == months ? bal : Math.Round(emi - interest, 2);
            bal = Math.Max(0, bal - princ);
            list.Add(new LoanPayment
            {
                Number = i,
                DueDate = start.AddMonths(i),
                PrincipalPart = princ,
                InterestPart = interest,
                Amount = princ + interest,
                BalanceAfter = bal,
            });
        }
        return list;
    }

    /// <summary>Fixed/bullet loan: simple interest, everything repaid at maturity.</summary>
    public static List<LoanPayment> BulletSchedule(decimal principal, decimal annualRatePct, int months, DateTime start)
    {
        var interest = Math.Round(principal * annualRatePct / 100m * months / 12m, 2);
        return new List<LoanPayment>
        {
            new() { Number = 1, DueDate = start.AddMonths(months), PrincipalPart = principal,
                    InterestPart = interest, Amount = principal + interest, BalanceAfter = 0 }
        };
    }

    public static decimal DepositMaturity(DepositKind kind, decimal amount, decimal annualRatePct, int months)
    {
        var r = (double)annualRatePct / 100.0;
        if (kind == DepositKind.FixedDeposit)
            return Math.Round((decimal)((double)amount * Math.Pow(1 + r / 4, 4 * months / 12.0)), 2); // quarterly compounding
        // RD: monthly installments, quarterly compounding approximated with monthly equivalent rate
        var mr = Math.Pow(1 + r / 4, 4.0 / 12.0) - 1;
        double total = 0;
        for (var i = 0; i < months; i++) total += (double)amount * Math.Pow(1 + mr, months - i);
        return Math.Round((decimal)total, 2);
    }

    /// <summary>Months to clear and total interest paid for one loan at a given monthly payment.</summary>
    public static (int Months, decimal Interest) Amortize(decimal balance, decimal annualRatePct, decimal payment, decimal lumpSum = 0)
    {
        var bal = Math.Max(0, balance - lumpSum);
        var r = annualRatePct / 12m / 100m;
        decimal interest = 0;
        var months = 0;
        while (bal > 0.005m && months < 1200)
        {
            var i = Math.Round(bal * r, 2);
            if (payment <= i) return (1200, interest + i * 1200); // never clears
            interest += i;
            bal = bal + i - payment;
            if (bal < 0) bal = 0;
            months++;
        }
        return (months, interest);
    }

    public static PrepaymentResult Prepay(decimal balance, decimal annualRatePct, decimal emi, decimal extraMonthly, decimal lumpSum)
    {
        var (bm, bi) = Amortize(balance, annualRatePct, emi);
        var (nm, ni) = Amortize(balance, annualRatePct, emi + extraMonthly, lumpSum);
        return new PrepaymentResult(bi, ni, bm, nm);
    }

    /// <summary>Simulates paying all minimums plus a pool of extra money rolled onto one target debt at a time.</summary>
    public static PayoffResult Payoff(string strategy, IEnumerable<PayoffDebt> debts, decimal extra)
    {
        var d = debts.Select(x => new { x.Name, x.AnnualRate, x.MinPayment, Bal = x.Balance }).ToList();
        var bals = d.Select(x => x.Bal).ToArray();
        decimal totalInterest = 0;
        var months = 0;
        while (bals.Any(b => b > 0.005m) && months < 1200)
        {
            months++;
            for (var i = 0; i < d.Count; i++)
                if (bals[i] > 0)
                {
                    var interest = Math.Round(bals[i] * d[i].AnnualRate / 12m / 100m, 2);
                    bals[i] += interest;
                    totalInterest += interest;
                }
            var pool = extra;
            for (var i = 0; i < d.Count; i++)
            {
                if (bals[i] <= 0) { pool += d[i].MinPayment; continue; }
                var pay = Math.Min(bals[i], d[i].MinPayment);
                bals[i] -= pay;
            }
            var order = Enumerable.Range(0, d.Count).Where(i => bals[i] > 0.005m);
            order = strategy == "Avalanche"
                ? order.OrderByDescending(i => d[i].AnnualRate)
                : order.OrderBy(i => bals[i]);
            foreach (var i in order)
            {
                if (pool <= 0) break;
                var pay = Math.Min(bals[i], pool);
                bals[i] -= pay;
                pool -= pay;
            }
        }
        return new PayoffResult(strategy, months, totalInterest);
    }
}
