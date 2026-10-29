using System.Windows;
using System.Windows.Media;
using FinanceTracker.Models;

namespace FinanceTracker.Services;

/// <summary>Colours for the account cards. An account's own colour wins; otherwise banks are dark slate, cards blue and the rest white.</summary>
public static class CardColors
{
    public static readonly string[] Palette =
    {
        "#0F172A", "#2563EB", "#4F46E5", "#7C3AED", "#DB2777", "#E11D48",
        "#EA580C", "#D97706", "#059669", "#0D9488", "#0284C7", "#374151",
    };

    private static readonly Color Ink = (Color)ColorConverter.ConvertFromString("#0F172A");

    private static string? DefaultFor(AccountType type) => type switch
    {
        AccountType.Bank => "#0F172A",
        AccountType.Savings => "#0D9488",
        AccountType.Current => "#4F46E5",
        AccountType.CreditCard => "#2563EB",
        _ => null,
    };

    private static string? Effective(Account a) => string.IsNullOrWhiteSpace(a.Color) ? DefaultFor(a.Type) : a.Color;

    private static Color? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return null; }
    }

    private static Color Darken(Color c, double factor) =>
        Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    public static Brush Background(Account a)
    {
        if (Parse(Effective(a)) is not { } c) return Brushes.White;
        var brush = new LinearGradientBrush(c, Darken(c, 0.72), new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>White text on dark cards, dark text on light ones.</summary>
    public static Brush Foreground(Account a)
    {
        if (Parse(Effective(a)) is not { } c) return new SolidColorBrush(Ink);
        var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luminance < 0.62 ? Brushes.White : new SolidColorBrush(Ink);
    }

    public static Brush Border(Account a) =>
        Parse(Effective(a)) == null ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")) : Brushes.Transparent;

    /// <summary>Translucent fill for the icon circle and badge: frosted white on colored cards, light grey on white ones.</summary>
    public static Brush Chip(Account a)
    {
        var brush = Parse(Effective(a)) == null
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"))
            : new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        brush.Freeze();
        return brush;
    }
}
