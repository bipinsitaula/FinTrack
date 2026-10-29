using System.Globalization;
using System.Windows.Data;

namespace FinanceTracker.Services;

/// <summary>
/// One place that decides how dates are shown: English (AD) or Nepali (BS), following the AD/BS switch in the header.
/// Date entry fields always use AD (yyyy-mm-dd); only displayed dates follow the switch.
/// </summary>
public static class DateDisplay
{
    public static bool UseBs { get; set; }

    private static string Ad(DateTime d, string format) => d.ToString(format, CultureInfo.CurrentCulture);

    /// <summary>"29 Oct 2026" or "12 Kartik 2083".</summary>
    public static string Full(DateTime d)
    {
        if (UseBs && NepaliCalendar.ToBs(d) is { } b) return $"{b.Day} {NepaliCalendar.MonthNames[b.Month - 1]} {b.Year}";
        return Ad(d, "dd MMM yyyy");
    }

    /// <summary>"29 Oct" or "12 Kartik".</summary>
    public static string Short(DateTime d)
    {
        if (UseBs && NepaliCalendar.ToBs(d) is { } b) return $"{b.Day} {NepaliCalendar.MonthNames[b.Month - 1]}";
        return Ad(d, "dd MMM");
    }

    public static string Month(DateTime d)
    {
        if (UseBs && NepaliCalendar.ToBs(d) is { } b) return NepaliCalendar.MonthNames[b.Month - 1];
        return Ad(d, "MMM");
    }

    public static string Day(DateTime d)
    {
        if (UseBs && NepaliCalendar.ToBs(d) is { } b) return b.Day.ToString();
        return Ad(d, "dd");
    }

    /// <summary>First and last day (inclusive) of the AD or BS month that contains the date.</summary>
    public static (DateTime Start, DateTime End) MonthRange(DateTime d)
    {
        if (UseBs && NepaliCalendar.ToBs(d) is { } b && NepaliCalendar.FromBs(b.Year, b.Month, 1) is { } s)
            return (s, s.AddDays(NepaliCalendar.DaysInMonth(b.Year, b.Month) - 1));
        var start = new DateTime(d.Year, d.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    /// <summary>First day of the month that is <paramref name="months"/> months away from the month containing the date.</summary>
    public static DateTime AddMonths(DateTime d, int months)
    {
        if (UseBs && NepaliCalendar.ToBs(d) is { } b)
        {
            var total = b.Year * 12 + (b.Month - 1) + months;
            if (NepaliCalendar.FromBs(total / 12, total % 12 + 1, 1) is { } s) return s;
        }
        return new DateTime(d.Year, d.Month, 1).AddMonths(months);
    }

    public static bool SameMonth(DateTime month, DateTime other)
    {
        var r = MonthRange(month);
        return other.Date >= r.Start && other.Date <= r.End;
    }

    /// <summary>"October 2026" / "Oct 2026" or "Kartik 2083".</summary>
    public static string MonthYear(DateTime d, bool full)
    {
        if (UseBs && NepaliCalendar.ToBs(d) is { } b) return $"{NepaliCalendar.MonthNames[b.Month - 1]} {b.Year}";
        return Ad(d, full ? "MMMM yyyy" : "MMM yyyy");
    }

    public static int YearOf(DateTime d) => UseBs && NepaliCalendar.ToBs(d) is { } b ? b.Year : d.Year;

    /// <summary>The date in the other calendar, for the small second line ("12 Kartik 2083 BS" or "29 Oct 2026 AD").</summary>
    public static string Alt(DateTime d)
    {
        if (UseBs) return Ad(d, "dd MMM yyyy") + " AD";
        var bs = NepaliCalendar.Format(d);
        return bs.Length == 0 ? "" : bs + " BS";
    }
}

public class DateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime d) return null;
        return (parameter as string) switch
        {
            "month" => DateDisplay.Month(d),
            "day" => DateDisplay.Day(d),
            "monthyear" => DateDisplay.MonthYear(d, false),
            "short" => DateDisplay.Short(d),
            _ => DateDisplay.Full(d),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
