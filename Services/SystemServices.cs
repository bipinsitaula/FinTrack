using System.Security.Cryptography;
using ClosedXML.Excel;
using FinanceTracker.Data;
using FinanceTracker.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FinanceTracker.Services;

public static class SecurityService
{
    private static string Hash(string pin, byte[] salt) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32));

    public static bool HasPin()
    {
        using var db = new AppDb();
        return !string.IsNullOrEmpty(FinanceService.GetSetting(db, "PinHash"));
    }

    /// <summary>True on first run, before the user has either set a PIN or chosen to skip.</summary>
    public static bool NeedsFirstRunSetup()
    {
        using var db = new AppDb();
        return string.IsNullOrEmpty(FinanceService.GetSetting(db, "PinHash")) && FinanceService.GetSetting(db, "PinPrompted") == null;
    }

    public static void MarkSetupPrompted()
    {
        using var db = new AppDb();
        FinanceService.SetSetting(db, "PinPrompted", "1");
    }

    public static void SetPin(string pin)
    {
        using var db = new AppDb();
        if (string.IsNullOrEmpty(pin))
        {
            db.Settings.RemoveRange(db.Settings.Where(s => s.Key == "PinHash" || s.Key == "PinSalt"));
            db.SaveChanges();
            return;
        }
        var salt = RandomNumberGenerator.GetBytes(16);
        FinanceService.SetSetting(db, "PinSalt", Convert.ToBase64String(salt));
        FinanceService.SetSetting(db, "PinHash", Hash(pin, salt));
    }

    public static bool Verify(string pin)
    {
        using var db = new AppDb();
        var h = FinanceService.GetSetting(db, "PinHash");
        var s = FinanceService.GetSetting(db, "PinSalt");
        if (h == null || s == null) return true;
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(h), Convert.FromBase64String(Hash(pin, Convert.FromBase64String(s))));
    }
}

public static class ProfileService
{
    public static event Action? Changed;

    public static string GetName()
    {
        using var db = new AppDb();
        return FinanceService.GetSetting(db, "UserName") ?? "";
    }

    public static void SetName(string name)
    {
        using var db = new AppDb();
        FinanceService.SetSetting(db, "UserName", name.Trim());
        Changed?.Invoke();
    }

    public static string Greeting()
    {
        var h = DateTime.Now.Hour;
        return h < 5 ? "Good evening" : h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : "Good evening";
    }
}

public static class BackupService
{
    private static readonly byte[] Magic = "FTBK1"u8.ToArray();

    /// <summary>Copies the live database (via SQLite online backup) and optionally encrypts it with AES-256-GCM.</summary>
    public static void Backup(string destination, string? password)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ft_{Guid.NewGuid():N}.db");
        using (var src = new SqliteConnection($"Data Source={AppDb.DbPath}"))
        using (var dst = new SqliteConnection($"Data Source={tmp}"))
        {
            src.Open(); dst.Open();
            src.BackupDatabase(dst);
        }
        SqliteConnection.ClearAllPools();
        try
        {
            if (string.IsNullOrEmpty(password)) { File.Copy(tmp, destination, true); return; }
            var data = File.ReadAllBytes(tmp);
            var salt = RandomNumberGenerator.GetBytes(16);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 200_000, HashAlgorithmName.SHA256, 32);
            var cipher = new byte[data.Length];
            var tag = new byte[16];
            using (var gcm = new AesGcm(key, 16)) gcm.Encrypt(nonce, data, cipher, tag);
            using var fs = File.Create(destination);
            fs.Write(Magic); fs.Write(salt); fs.Write(nonce); fs.Write(tag); fs.Write(cipher);
        }
        finally { File.Delete(tmp); }
    }

    public static void Restore(string source, string? password)
    {
        var bytes = File.ReadAllBytes(source);
        if (bytes.Length > Magic.Length && bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("This backup is password protected.");
            var o = Magic.Length;
            var salt = bytes[o..(o + 16)]; o += 16;
            var nonce = bytes[o..(o + 12)]; o += 12;
            var tag = bytes[o..(o + 16)]; o += 16;
            var cipher = bytes[o..];
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 200_000, HashAlgorithmName.SHA256, 32);
            var plain = new byte[cipher.Length];
            try { using var gcm = new AesGcm(key, 16); gcm.Decrypt(nonce, cipher, tag, plain); }
            catch (CryptographicException) { throw new InvalidOperationException("Wrong password or corrupted backup."); }
            bytes = plain;
        }
        else if (!bytes.AsSpan(0, Math.Min(15, bytes.Length)).SequenceEqual("SQLite format 3"u8[..Math.Min(15, bytes.Length)]))
            throw new InvalidOperationException("Not a valid backup file.");

        SqliteConnection.ClearAllPools();
        File.Copy(AppDb.DbPath, AppDb.DbPath + ".before-restore", true);
        File.WriteAllBytes(AppDb.DbPath, bytes);
    }
}

public static class NotificationService
{
    /// <summary>Shows Windows toasts for items due within their reminder window. Never throws.</summary>
    public static void NotifyDue()
    {
        try
        {
            using var db = new AppDb();
            var due = FinanceService.Upcoming(db, 3).Take(5).ToList();
            foreach (var d in due)
            {
                new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                    .AddText($"{d.Kind} due {d.Date:dd MMM}")
                    .AddText($"{d.Title} – {FinanceService.Currency} {d.Amount:N0}")
                    .Show();
            }
        }
        catch { /* toast infrastructure unavailable; the in-app alerts panel still shows everything */ }
    }
}

public static class ExportService
{
    static ExportService() => QuestPDF.Settings.License = LicenseType.Community;

    private record Report(DateTime Month, decimal Income, decimal Expense, List<CategorySlice> Spending, List<BudgetStatus> Budgets, NetWorthSummary Worth, HealthScore Health, List<Transaction> Txs);

    private static Report Build(DateTime month)
    {
        using var db = new AppDb();
        var (i, e) = FinanceService.MonthTotals(db, month);
        var txs = db.Transactions.Include(t => t.Account).Include(t => t.Category).AsNoTracking().ToList()
            .Where(t => t.Date.Year == month.Year && t.Date.Month == month.Month).OrderBy(t => t.Date).ToList();
        return new Report(month, i, e, FinanceService.SpendingByCategory(db, month), FinanceService.Budgets(db, month),
            FinanceService.NetWorth(db), FinanceService.Health(db), txs);
    }

    public static void MonthEndPdf(DateTime month, string path)
    {
        var r = Build(month);
        Document.Create(c => c.Page(p =>
        {
            p.Margin(36);
            p.DefaultTextStyle(t => t.FontSize(10));
            p.Header().Column(h =>
            {
                h.Item().Text($"Month-end report – {month:MMMM yyyy}").FontSize(20).Bold();
                h.Item().Text($"All amounts in {FinanceService.Currency}").FontColor(Colors.Grey.Darken1);
            });
            p.Content().PaddingVertical(12).Column(col =>
            {
                col.Spacing(10);
                col.Item().Text($"Income {r.Income:N2}   Expenses {r.Expense:N2}   Saved {r.Income - r.Expense:N2}").Bold();
                col.Item().Text($"Net worth {r.Worth.NetWorth:N2} (assets {r.Worth.Assets:N2}, liabilities {r.Worth.Liabilities:N2})");
                col.Item().Text($"Financial health score: {r.Health.Total}/100");

                col.Item().Text("Spending by category").FontSize(14).Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.RelativeColumn(); });
                    foreach (var s in r.Spending) { t.Cell().Text(s.Name); t.Cell().AlignRight().Text(s.Amount.ToString("N2")); }
                });

                col.Item().Text("Budgets").FontSize(14).Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.RelativeColumn(); cd.RelativeColumn(); cd.RelativeColumn(); });
                    foreach (var b in r.Budgets)
                    {
                        t.Cell().Text(b.Category); t.Cell().AlignRight().Text(b.Spent.ToString("N2"));
                        t.Cell().AlignRight().Text(b.Limit.ToString("N2")); t.Cell().AlignRight().Text($"{b.Percent:N0}%");
                    }
                });

                col.Item().Text("Transactions").FontSize(14).Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.ConstantColumn(60); cd.RelativeColumn(2); cd.RelativeColumn(2); cd.RelativeColumn(3); cd.RelativeColumn(); });
                    foreach (var x in r.Txs)
                    {
                        t.Cell().Text(x.Date.ToString("dd MMM")); t.Cell().Text(x.AccountName);
                        t.Cell().Text(x.CategoryName); t.Cell().Text(x.Note);
                        t.Cell().AlignRight().Text(x.Signed.ToString("N2"));
                    }
                });
            });
            p.Footer().AlignCenter().Text(x => { x.Span("Page "); x.CurrentPageNumber(); });
        })).GeneratePdf(path);
    }

    public static void MonthEndExcel(DateTime month, string path)
    {
        var r = Build(month);
        using var wb = new XLWorkbook();
        var s = wb.Worksheets.Add("Summary");
        s.Cell(1, 1).Value = $"Month-end report – {month:MMMM yyyy}";
        s.Cell(1, 1).Style.Font.Bold = true;
        var rows = new (string, object)[]
        {
            ("Income", r.Income), ("Expenses", r.Expense), ("Saved", r.Income - r.Expense),
            ("Net worth", r.Worth.NetWorth), ("Assets", r.Worth.Assets), ("Liabilities", r.Worth.Liabilities),
            ("Health score", r.Health.Total),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            s.Cell(i + 3, 1).Value = rows[i].Item1;
            s.Cell(i + 3, 2).Value = XLCellValue.FromObject(rows[i].Item2);
        }

        var c = wb.Worksheets.Add("Categories");
        c.Cell(1, 1).Value = "Category"; c.Cell(1, 2).Value = "Spent";
        for (var i = 0; i < r.Spending.Count; i++) { c.Cell(i + 2, 1).Value = r.Spending[i].Name; c.Cell(i + 2, 2).Value = r.Spending[i].Amount; }

        var b = wb.Worksheets.Add("Budgets");
        b.Cell(1, 1).Value = "Category"; b.Cell(1, 2).Value = "Limit"; b.Cell(1, 3).Value = "Spent"; b.Cell(1, 4).Value = "%";
        for (var i = 0; i < r.Budgets.Count; i++)
        {
            b.Cell(i + 2, 1).Value = r.Budgets[i].Category; b.Cell(i + 2, 2).Value = r.Budgets[i].Limit;
            b.Cell(i + 2, 3).Value = r.Budgets[i].Spent; b.Cell(i + 2, 4).Value = r.Budgets[i].Percent;
        }

        var t = wb.Worksheets.Add("Transactions");
        string[] heads = { "Date", "Account", "Category", "Note", "Amount" };
        for (var i = 0; i < heads.Length; i++) t.Cell(1, i + 1).Value = heads[i];
        for (var i = 0; i < r.Txs.Count; i++)
        {
            var x = r.Txs[i];
            t.Cell(i + 2, 1).Value = x.Date; t.Cell(i + 2, 2).Value = x.AccountName;
            t.Cell(i + 2, 3).Value = x.CategoryName; t.Cell(i + 2, 4).Value = x.Note; t.Cell(i + 2, 5).Value = x.Signed;
        }
        foreach (var ws in wb.Worksheets) { ws.Row(1).Style.Font.Bold = true; ws.Columns().AdjustToContents(); }
        wb.SaveAs(path);
    }
}
