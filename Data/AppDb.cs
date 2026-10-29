using FinanceTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Data;

public class AppDb : DbContext
{
    public static string DataDir { get; } = Environment.GetEnvironmentVariable("FT_DATA") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FinanceTracker");
    public static string DbPath { get; } = Path.Combine(DataDir, "finance.db");
    public static string ReceiptDir { get; } = Path.Combine(DataDir, "receipts");

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<RecurringIncome> RecurringIncomes => Set<RecurringIncome>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<LoanPayment> LoanPayments => Set<LoanPayment>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<DebtRepayment> DebtRepayments => Set<DebtRepayment>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalContribution> GoalContributions => Set<GoalContribution>();
    public DbSet<Deposit> Deposits => Set<Deposit>();
    public DbSet<Insurance> Insurances => Set<Insurance>();
    public DbSet<Investment> Investments => Set<Investment>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnConfiguring(DbContextOptionsBuilder o) => o.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Setting>().HasKey(s => s.Key);
        b.Entity<Loan>().HasMany(l => l.Payments).WithOne().HasForeignKey(p => p.LoanId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Debt>().HasMany(d => d.Repayments).WithOne().HasForeignKey(r => r.DebtId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Goal>().HasMany(g => g.Contributions).WithOne().HasForeignKey(c => c.GoalId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Transaction>().HasOne(t => t.Account).WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Transaction>().HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Budget>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
    }

    public static void Initialize()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ReceiptDir);
        using var db = new AppDb();
        db.Database.EnsureCreated();
        if (!db.Categories.Any()) Seed(db);
    }

    private static void Seed(AppDb db)
    {
        db.Accounts.Add(new Account { Name = "Cash in Hand", Type = AccountType.Cash });

        var expense = new (string Name, string[] Subs, bool Deductible)[]
        {
            ("Food", new[] { "Groceries", "Dining Out" }, false),
            ("Rent", Array.Empty<string>(), false),
            ("Transport", new[] { "Fuel", "Public Transport" }, false),
            ("Education", Array.Empty<string>(), true),
            ("Health", Array.Empty<string>(), true),
            ("Entertainment", Array.Empty<string>(), false),
            ("Utilities", new[] { "Electricity", "Water", "Internet", "Phone" }, false),
            ("Shopping", Array.Empty<string>(), false),
            ("Insurance", Array.Empty<string>(), true),
            ("Subscriptions", Array.Empty<string>(), false),
            ("Loan Repayment", Array.Empty<string>(), false),
            ("Other", Array.Empty<string>(), false),
        };
        foreach (var (name, subs, ded) in expense)
        {
            var parent = new Category { Name = name, TaxDeductible = ded };
            db.Categories.Add(parent);
            db.SaveChanges();
            foreach (var s in subs) db.Categories.Add(new Category { Name = s, ParentId = parent.Id, TaxDeductible = ded });
        }
        foreach (var n in new[] { "Salary", "Freelance", "Rent Received", "Interest", "Other Income" })
            db.Categories.Add(new Category { Name = n, IsIncome = true });

        db.Settings.Add(new Setting { Key = "UsdRate", Value = "133.5" });
        db.Settings.Add(new Setting { Key = "InrRate", Value = "1.6" });
        db.SaveChanges();
    }
}
