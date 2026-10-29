using FinanceTracker.Data;
using FinanceTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Services;

public record BudgetStatus(string Category, decimal Limit, decimal Spent)
{
    public double Percent => Limit <= 0 ? 0 : (double)(Spent / Limit * 100m);
    public double BarValue => Math.Min(100, Percent);
    public string State => Percent >= 100 ? "Over budget" : Percent >= 80 ? "Nearing limit" : "On track";
}
public record DueItem(DateTime Date, string Title, decimal Amount, string Kind);
public record Alert(string Level, string Message);
public record CategorySlice(string Name, decimal Amount);
public record HealthScore(int Total, int Savings, int Debt, int Emergency, int Budget, double SavingsRate, double DebtToIncome, double EmergencyMonths, double BudgetAdherence);
public record ForecastMonth(DateTime Month, decimal Income, decimal Emis, decimal Subscriptions, decimal Closing);
public record NetWorthSummary(decimal Assets, decimal Liabilities)
{
    public decimal NetWorth => Assets - Liabilities;
}

public static class FinanceService
{
    public const string Currency = "NPR";

    // ---------- settings ----------
    public static string? GetSetting(AppDb db, string key) => db.Settings.Find(key)?.Value;

    public static void SetSetting(AppDb db, string key, string value)
    {
        var s = db.Settings.Find(key);
        if (s == null) db.Settings.Add(new Setting { Key = key, Value = value });
        else s.Value = value;
        db.SaveChanges();
    }

    // ---------- categories ----------
    public static List<Category> LoadCategories(AppDb db, bool? income = null)
    {
        var all = db.Categories.AsNoTracking().ToList();
        var byId = all.ToDictionary(c => c.Id);
        foreach (var c in all)
            c.FullName = c.ParentId is int p && byId.TryGetValue(p, out var parent) ? $"{parent.Name} › {c.Name}" : c.Name;
        return all.Where(c => income == null || c.IsIncome == income).OrderBy(c => c.FullName).ToList();
    }

    // ---------- accounts ----------
    public static List<Account> LoadAccounts(AppDb db)
    {
        var accounts = db.Accounts.AsNoTracking().OrderBy(a => a.Type).ThenBy(a => a.Name).ToList();
        var sums = db.Transactions.AsNoTracking().ToList()
            .GroupBy(t => t.AccountId).ToDictionary(g => g.Key, g => g.Sum(t => t.Signed));
        foreach (var a in accounts)
            a.Balance = a.OpeningBalance + (sums.TryGetValue(a.Id, out var s) ? s : 0);
        return accounts;
    }

    public static void AddTransfer(AppDb db, int fromId, int toId, decimal amount, DateTime date, string note)
    {
        var g = Guid.NewGuid();
        db.Transactions.Add(new Transaction { AccountId = fromId, Amount = amount, Date = date, Kind = TxKind.TransferOut, Note = note, TransferGroup = g });
        db.Transactions.Add(new Transaction { AccountId = toId, Amount = amount, Date = date, Kind = TxKind.TransferIn, Note = note, TransferGroup = g });
        db.SaveChanges();
    }

    public static void AddSplit(AppDb db, int accountId, DateTime date, string note, IEnumerable<(int CategoryId, decimal Amount)> lines, string? receipt)
    {
        var g = Guid.NewGuid();
        foreach (var (cat, amt) in lines)
            db.Transactions.Add(new Transaction { AccountId = accountId, CategoryId = cat, Amount = amt, Date = date, Kind = TxKind.Expense, Note = note, SplitGroup = g, ReceiptPath = receipt });
        db.SaveChanges();
    }

    public static string? StoreReceipt(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return null;
        var dest = Path.Combine(AppDb.ReceiptDir, $"{Guid.NewGuid():N}{Path.GetExtension(source)}");
        File.Copy(source, dest);
        return dest;
    }

    // ---------- net worth ----------
    public static NetWorthSummary NetWorth(AppDb db)
    {
        var accounts = LoadAccounts(db);
        var assets = accounts.Where(a => a.Balance > 0).Sum(a => a.Balance);
        var liabilities = -accounts.Where(a => a.Balance < 0).Sum(a => a.Balance);

        assets += db.Deposits.AsNoTracking().ToList().Sum(d => d.Invested);
        assets += db.Investments.AsNoTracking().Sum(i => i.CurrentValue);
        foreach (var debt in db.Debts.Include(d => d.Repayments).AsNoTracking().ToList())
        {
            if (debt.Direction == DebtDirection.Gave) assets += debt.Outstanding;
            else liabilities += debt.Outstanding;
        }
        liabilities += db.Loans.Include(l => l.Payments).AsNoTracking().ToList().Sum(l => l.RemainingPrincipal);
        return new NetWorthSummary(assets, liabilities);
    }

    /// <summary>Month-end net worth for the last N months. Account balances are historical; other assets and debts are held at today's value.</summary>
    public static List<(DateTime Month, decimal Value)> NetWorthTrend(AppDb db, int months = 12)
    {
        var accounts = db.Accounts.AsNoTracking().ToList();
        var txs = db.Transactions.AsNoTracking().ToList();
        var now = NetWorth(db);
        var currentAccountNet = accounts.Sum(a => a.OpeningBalance) + txs.Sum(t => t.Signed);
        var otherNet = now.NetWorth - currentAccountNet;
        var result = new List<(DateTime, decimal)>();
        for (var i = months - 1; i >= 0; i--)
        {
            var monthStart = DateDisplay.AddMonths(DateTime.Today, -i);
            var end = DateDisplay.MonthRange(monthStart).End;
            var acct = accounts.Sum(a => a.OpeningBalance) + txs.Where(t => t.Date <= end).Sum(t => t.Signed);
            result.Add((monthStart, acct + otherNet));
        }
        return result;
    }

    /// <summary>Assets and liabilities at the end of each of the last N months. Account balances are historical; the rest is held at today's value.</summary>
    public static List<(decimal Assets, decimal Liabilities)> AccountTrends(AppDb db, int months = 6)
    {
        var accounts = db.Accounts.AsNoTracking().ToList();
        var txs = db.Transactions.AsNoTracking().ToList();
        decimal Balance(Account a, DateTime upTo) => a.OpeningBalance + txs.Where(t => t.AccountId == a.Id && t.Date.Date <= upTo.Date).Sum(t => t.Signed);

        var now = NetWorth(db);
        var todayBalances = accounts.Select(a => Balance(a, DateTime.Today)).ToList();
        var assetsConst = now.Assets - todayBalances.Where(b => b > 0).Sum();
        var liabilitiesConst = now.Liabilities + todayBalances.Where(b => b < 0).Sum();

        var result = new List<(decimal, decimal)>();
        for (var i = months - 1; i >= 0; i--)
        {
            var end = i == 0 ? DateTime.Today : DateDisplay.MonthRange(DateDisplay.AddMonths(DateTime.Today, -i)).End;
            var balances = accounts.Select(a => Balance(a, end)).ToList();
            result.Add((balances.Where(b => b > 0).Sum() + assetsConst, -balances.Where(b => b < 0).Sum() + liabilitiesConst));
        }
        return result;
    }

    // ---------- monthly numbers ----------
    private static IEnumerable<Transaction> InMonth(IEnumerable<Transaction> txs, DateTime month) =>
        txs.Where(t => DateDisplay.SameMonth(month, t.Date));

    public static (decimal Income, decimal Expense) MonthTotals(AppDb db, DateTime month)
    {
        var m = InMonth(db.Transactions.AsNoTracking().ToList(), month).ToList();
        return (m.Where(t => t.Kind == TxKind.Income).Sum(t => t.Amount), m.Where(t => t.Kind == TxKind.Expense).Sum(t => t.Amount));
    }

    public static List<CategorySlice> SpendingByCategory(AppDb db, DateTime month)
    {
        var cats = db.Categories.AsNoTracking().ToDictionary(c => c.Id);
        string TopName(int? id)
        {
            if (id is not int i || !cats.TryGetValue(i, out var c)) return "Uncategorised";
            return c.ParentId is int p && cats.TryGetValue(p, out var parent) ? parent.Name : c.Name;
        }
        return InMonth(db.Transactions.AsNoTracking().ToList(), month)
            .Where(t => t.Kind == TxKind.Expense)
            .GroupBy(t => TopName(t.CategoryId))
            .Select(g => new CategorySlice(g.Key, g.Sum(t => t.Amount)))
            .OrderByDescending(s => s.Amount).ToList();
    }

    public static List<BudgetStatus> Budgets(AppDb db, DateTime month)
    {
        var cats = db.Categories.AsNoTracking().ToDictionary(c => c.Id);
        var txs = InMonth(db.Transactions.AsNoTracking().ToList(), month).Where(t => t.Kind == TxKind.Expense).ToList();
        decimal Spent(int budgetCat) => txs.Where(t =>
            t.CategoryId == budgetCat ||
            (t.CategoryId is int c && cats.TryGetValue(c, out var cat) && cat.ParentId == budgetCat)).Sum(t => t.Amount);
        return db.Budgets.AsNoTracking().ToList()
            .Select(b => new BudgetStatus(cats.TryGetValue(b.CategoryId, out var c) ? c.Name : "?", b.MonthlyLimit, Spent(b.CategoryId)))
            .OrderByDescending(b => b.Percent).ToList();
    }

    // ---------- recurring / due items ----------
    public static DateTime Advance(DateTime d, BillCycle c) => c switch
    {
        BillCycle.Weekly => d.AddDays(7),
        BillCycle.Monthly => d.AddMonths(1),
        BillCycle.Quarterly => d.AddMonths(3),
        _ => d.AddYears(1),
    };

    /// <summary>Creates income transactions for recurring income that has fallen due since last run.</summary>
    public static int PostRecurringIncome(AppDb db)
    {
        var n = 0;
        foreach (var r in db.RecurringIncomes.Where(r => r.Active).ToList())
        {
            while (r.NextDate.Date <= DateTime.Today)
            {
                db.Transactions.Add(new Transaction { AccountId = r.AccountId, CategoryId = r.CategoryId, Amount = r.Amount, Date = r.NextDate.Date, Kind = TxKind.Income, Note = r.Name });
                r.NextDate = Advance(r.NextDate, r.Cycle);
                n++;
            }
        }
        db.SaveChanges();
        return n;
    }

    public static List<DueItem> Upcoming(AppDb db, int days)
    {
        var from = DateTime.Today;
        var to = from.AddDays(days);
        var items = new List<DueItem>();
        foreach (var l in db.Loans.Include(l => l.Payments).AsNoTracking().ToList())
            foreach (var p in l.Payments.Where(p => !p.Paid && p.DueDate.Date <= to))
                items.Add(new DueItem(p.DueDate, $"{l.Name} #{p.Number}", p.Amount, p.DueDate.Date < from ? "Overdue" : (l.Kind == LoanKind.Emi ? "EMI" : "Loan")));
        foreach (var s in db.Subscriptions.AsNoTracking().Where(s => s.Active).ToList().Where(s => s.NextDue.Date <= to))
            items.Add(new DueItem(s.NextDue, s.Name, s.Amount, s.NextDue.Date < from ? "Overdue" : "Bill"));
        foreach (var i in db.Insurances.AsNoTracking().ToList().Where(i => i.DueDate.Date <= to))
            items.Add(new DueItem(i.DueDate, i.Policy, i.Premium, "Insurance"));
        foreach (var d in db.Debts.Include(d => d.Repayments).AsNoTracking().ToList().Where(d => d.DueDate != null && d.Outstanding > 0 && d.DueDate.Value.Date <= to))
            items.Add(new DueItem(d.DueDate!.Value, d.Direction == DebtDirection.Took ? $"Repay {d.Person}" : $"Collect from {d.Person}", d.Outstanding, "Personal"));
        foreach (var a in db.Accounts.AsNoTracking().Where(a => a.Type == AccountType.CreditCard && a.DueDay > 0).ToList())
        {
            var due = new DateTime(from.Year, from.Month, Math.Min(a.DueDay, DateTime.DaysInMonth(from.Year, from.Month)));
            if (due < from) due = due.AddMonths(1);
            if (due <= to) items.Add(new DueItem(due, $"{a.Name} card bill", 0, "Card"));
        }
        return items.OrderBy(i => i.Date).ToList();
    }

    public static void PayLoanInstallment(AppDb db, int paymentId, int accountId)
    {
        var p = db.LoanPayments.Find(paymentId);
        if (p == null || p.Paid) return;
        var loan = db.Loans.Find(p.LoanId)!;
        var cat = db.Categories.FirstOrDefault(c => c.Name == "Loan Repayment")?.Id;
        p.Paid = true;
        p.PaidDate = DateTime.Today;
        db.Transactions.Add(new Transaction { AccountId = accountId, CategoryId = cat, Amount = p.Amount, Date = DateTime.Today, Kind = TxKind.Expense, Note = $"{loan.Name} installment #{p.Number}" });
        db.SaveChanges();
    }

    public static void PaySubscription(AppDb db, int subId, int accountId)
    {
        var s = db.Subscriptions.Find(subId);
        if (s == null) return;
        var cat = s.CategoryId ?? db.Categories.FirstOrDefault(c => c.Name == "Subscriptions")?.Id;
        db.Transactions.Add(new Transaction { AccountId = accountId, CategoryId = cat, Amount = s.Amount, Date = DateTime.Today, Kind = TxKind.Expense, Note = $"{s.Name} (bill)" });
        s.NextDue = Advance(s.NextDue, s.Cycle);
        db.SaveChanges();
    }

    // ---------- smart features ----------
    public static List<string> Anomalies(AppDb db)
    {
        var result = new List<string>();
        var now = DateTime.Today;
        var txs = db.Transactions.AsNoTracking().Where(t => t.Kind == TxKind.Expense).ToList();
        var cats = db.Categories.AsNoTracking().ToDictionary(c => c.Id);
        string Top(Transaction t) => t.CategoryId is int i && cats.TryGetValue(i, out var c)
            ? (c.ParentId is int p && cats.TryGetValue(p, out var pc) ? pc.Name : c.Name) : "Uncategorised";
        var current = txs.Where(t => DateDisplay.SameMonth(now, t.Date)).GroupBy(Top).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var prevMonths = Enumerable.Range(1, 3).Select(i => DateDisplay.AddMonths(now, -i)).ToList();
        foreach (var (cat, amount) in current)
        {
            var avg = prevMonths.Average(m => txs.Where(t => DateDisplay.SameMonth(m, t.Date) && Top(t) == cat).Sum(t => t.Amount));
            if (avg > 0 && amount > avg * 1.5m && amount - avg > 500)
                result.Add($"{cat}: {amount:N0} this month is {amount / avg * 100 - 100:N0}% above your 3-month average ({avg:N0}).");
        }
        return result;
    }

    public static List<ForecastMonth> Forecast(AppDb db, int months = 3)
    {
        var balance = LoadAccounts(db).Where(a => a.Type != AccountType.CreditCard).Sum(a => a.Balance);
        var incomes = db.RecurringIncomes.AsNoTracking().Where(r => r.Active).ToList();
        var loans = db.Loans.Include(l => l.Payments).AsNoTracking().ToList();
        var subs = db.Subscriptions.AsNoTracking().Where(s => s.Active).ToList();
        var result = new List<ForecastMonth>();
        var today = DateTime.Today;
        for (var i = 1; i <= months; i++)
        {
            var m = DateDisplay.AddMonths(today, i);
            var next = DateDisplay.AddMonths(today, i + 1);
            decimal Count(DateTime first, BillCycle c, decimal amt)
            {
                decimal total = 0;
                var d = first;
                while (d < m) d = Advance(d, c);
                while (d < next) { total += amt; d = Advance(d, c); }
                return total;
            }
            var inc = incomes.Sum(r => Count(r.NextDate, r.Cycle, r.Amount));
            var emis = loans.SelectMany(l => l.Payments).Where(p => !p.Paid && p.DueDate >= m && p.DueDate < next).Sum(p => p.Amount);
            var sub = subs.Sum(s => Count(s.NextDue, s.Cycle, s.Amount));
            balance += inc - emis - sub;
            result.Add(new ForecastMonth(m, inc, emis, sub, balance));
        }
        return result;
    }

    public static HealthScore Health(AppDb db)
    {
        var now = DateTime.Today;
        var (inc, exp) = MonthTotals(db, now);
        if (inc == 0) // fall back to last month if the month has just started
        {
            (inc, exp) = MonthTotals(db, DateDisplay.AddMonths(now, -1));
        }
        var savingsRate = inc <= 0 ? 0 : (double)((inc - exp) / inc);
        var savings = (int)Math.Round(Math.Clamp(savingsRate / 0.20, 0, 1) * 25);

        var emi = db.Loans.Include(l => l.Payments).AsNoTracking().ToList()
            .Where(l => l.Kind == LoanKind.Emi && l.PaidCount < l.Payments.Count).Sum(l => l.Emi);
        var dti = inc <= 0 ? (emi > 0 ? 1.0 : 0.0) : (double)(emi / inc);
        var debt = (int)Math.Round((1 - Math.Clamp((dti - 0.2) / 0.3, 0, 1)) * 25);

        var liquid = LoadAccounts(db).Where(a => a.Type != AccountType.CreditCard).Sum(a => Math.Max(0, a.Balance));
        var avgExp = Enumerable.Range(1, 3).Select(i => MonthTotals(db, DateDisplay.AddMonths(now, -i)).Expense).Where(e => e > 0).DefaultIfEmpty(exp).Average();
        var months = avgExp <= 0 ? 0 : (double)(liquid / avgExp);
        var emergency = (int)Math.Round(Math.Clamp(months / 6.0, 0, 1) * 25);

        var budgets = Budgets(db, now);
        var adherence = budgets.Count == 0 ? 1.0 : budgets.Count(b => b.Percent <= 100) / (double)budgets.Count;
        var budget = (int)Math.Round(adherence * 25);

        return new HealthScore(savings + debt + emergency + budget, savings, debt, emergency, budget, savingsRate, dti, months, adherence);
    }

    public static List<Alert> Alerts(AppDb db)
    {
        var list = new List<Alert>();
        foreach (var d in Upcoming(db, 0).Where(d => d.Date.Date < DateTime.Today && d.Kind == "Overdue"))
            list.Add(new Alert("Danger", $"Overdue: {d.Title} ({d.Amount:N0}) was due {DateDisplay.Short(d.Date)}"));
        foreach (var b in Budgets(db, DateTime.Today).Where(b => b.Percent >= 80))
            list.Add(new Alert(b.Percent >= 100 ? "Danger" : "Warn", $"{b.Category} budget at {b.Percent:N0}% ({b.Spent:N0} of {b.Limit:N0})"));
        foreach (var a in Anomalies(db)) list.Add(new Alert("Warn", "Unusual spending – " + a));
        foreach (var a in LoadAccounts(db).Where(a => a.Type != AccountType.CreditCard && a.Balance < 1000))
            list.Add(new Alert("Warn", $"Low balance: {a.Name} has {a.Balance:N0}"));
        foreach (var a in LoadAccounts(db).Where(a => a.Type == AccountType.CreditCard && a.Balance < 0))
            list.Add(new Alert("Info", $"Credit card {a.Name} outstanding: {-a.Balance:N0}"));
        return list;
    }
}
