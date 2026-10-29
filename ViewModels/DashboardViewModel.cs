using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace FinanceTracker.ViewModels;

public record RecentItem(string Title, string Sub, string AmountText, bool IsIncome);

public class DashboardViewModel
{
    private static readonly string[] Palette = { "#3B82F6", "#10B981", "#F59E0B", "#F43F5E", "#8B5CF6", "#06B6D4", "#F97316", "#64748B", "#14B8A6", "#EC4899" };
    private static readonly SKColor Muted = SKColor.Parse("#64748B");

    public decimal NetWorth { get; }
    public decimal TotalBalance { get; }
    public decimal TotalDebt { get; }
    public decimal Income { get; }
    public decimal Expense { get; }
    public decimal Saved => Income - Expense;
    public double IncomePct { get; }
    public double ExpensePct { get; }
    public HealthScore Health { get; }
    public List<DueItem> Upcoming { get; }
    public List<BudgetStatus> Budgets { get; }
    public List<Goal> Goals { get; }
    public List<Alert> Alerts { get; }
    public ISeries[] Donut { get; }
    public ISeries[] Trend { get; }
    public Axis[] TrendX { get; }
    public Axis[] TrendY { get; }
    public string MonthLabel { get; } = DateTime.Today.ToString("MMMM yyyy");
    public bool HasSpending { get; }
    public List<RecentItem> Recent { get; }
    public ISeries[] CashFlow { get; }
    public Axis[] CashX { get; }
    public Axis[] CashY { get; }

    public DashboardViewModel()
    {
        using var db = new AppDb();
        var worth = FinanceService.NetWorth(db);
        NetWorth = worth.NetWorth;
        TotalDebt = worth.Liabilities;
        TotalBalance = FinanceService.LoadAccounts(db).Sum(a => a.Balance);
        (Income, Expense) = FinanceService.MonthTotals(db, DateTime.Today);
        var max = (double)Math.Max(1, Math.Max(Income, Expense));
        IncomePct = (double)Income / max * 100;
        ExpensePct = (double)Expense / max * 100;
        Health = FinanceService.Health(db);
        Upcoming = FinanceService.Upcoming(db, 7);
        Budgets = FinanceService.Budgets(db, DateTime.Today);
        Goals = db.Goals.Include(g => g.Contributions).AsNoTracking().ToList();
        Alerts = FinanceService.Alerts(db);

        var spending = FinanceService.SpendingByCategory(db, DateTime.Today);
        HasSpending = spending.Count > 0;
        Donut = spending.Select((s, i) => (ISeries)new PieSeries<double>
        {
            Values = new[] { (double)s.Amount },
            Name = s.Name,
            InnerRadius = 60,
            Fill = new SolidColorPaint(SKColor.Parse(Palette[i % Palette.Length])),
        }).ToArray();

        Recent = db.Transactions.Include(t => t.Account).Include(t => t.Category).AsNoTracking().ToList()
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(6)
            .Select(t => new RecentItem(
                string.IsNullOrWhiteSpace(t.Note) ? (t.CategoryName == "" ? "Transaction" : t.CategoryName) : t.Note,
                $"{DateDisplay.Short(t.Date)} · {t.AccountName}",
                (t.Signed >= 0 ? "+" : "-") + t.Amount.ToString("N0"),
                t.Signed >= 0)).ToList();

        var months = Enumerable.Range(0, 6).Select(i => DateTime.Today.AddMonths(i - 5)).ToList();
        var totals = months.Select(m => FinanceService.MonthTotals(db, m)).ToList();
        CashFlow = new ISeries[]
        {
            new ColumnSeries<double> { Name = "Income", Values = totals.Select(t => (double)t.Income).ToArray(), Fill = new SolidColorPaint(SKColor.Parse("#34D399")), MaxBarWidth = 16, Rx = 4, Ry = 4 },
            new ColumnSeries<double> { Name = "Expenses", Values = totals.Select(t => (double)t.Expense).ToArray(), Fill = new SolidColorPaint(SKColor.Parse("#FB7185")), MaxBarWidth = 16, Rx = 4, Ry = 4 },
        };
        CashX = new[] { new Axis { Labels = months.Select(m => m.ToString("MMM")).ToArray(), LabelsPaint = new SolidColorPaint(Muted), TextSize = 12 } };
        CashY = new[] { new Axis { LabelsPaint = new SolidColorPaint(Muted), TextSize = 12, SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#E2E8F0"), 1), Labeler = v => v.ToString("N0"), MinLimit = 0 } };

        var trend = FinanceService.NetWorthTrend(db, 12);
        Trend = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = trend.Select(t => (double)t.Value).ToArray(),
                Stroke = new SolidColorPaint(SKColor.Parse("#2563EB"), 3),
                Fill = new SolidColorPaint(SKColor.Parse("#2563EB").WithAlpha(36)),
                GeometryStroke = new SolidColorPaint(SKColor.Parse("#2563EB"), 3),
                GeometryFill = new SolidColorPaint(SKColor.Parse("#FFFFFF")),
                GeometrySize = 8,
                Name = "Net worth",
            }
        };
        TrendX = new[] { new Axis { Labels = trend.Select(t => t.Month.ToString("MMM")).ToArray(), LabelsPaint = new SolidColorPaint(Muted), TextSize = 12 } };
        TrendY = new[]
        {
            new Axis
            {
                LabelsPaint = new SolidColorPaint(Muted), TextSize = 12,
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#E2E8F0"), 1),
                Labeler = v => v.ToString("N0"),
            }
        };
    }
}
