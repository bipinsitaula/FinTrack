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
            "short" => DateDisplay.Short(d),
            _ => DateDisplay.Full(d),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
