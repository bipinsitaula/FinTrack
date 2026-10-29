using System.Windows;
using System.Windows.Media;
using FinanceTracker.Data;

namespace FinanceTracker.Services;

/// <summary>Light and dark palettes. Switching replaces the themed brushes in the application resources; every view reads them with DynamicResource.</summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, string> Light = new()
    {
        ["BgBrush"] = "#F8FAFC", ["SurfaceBrush"] = "#FFFFFF", ["Surface2Brush"] = "#F1F5F9",
        ["BorderBrush"] = "#E2E8F0", ["SoftBorderBrush"] = "#F1F5F9",
        ["AccentSoftBrush"] = "#EFF6FF", ["AccentDarkBrush"] = "#BFDBFE", ["AccentTextBrush"] = "#1E3A8A",
        ["InkBrush"] = "#0F172A", ["TextBrush"] = "#0F172A", ["MutedBrush"] = "#64748B", ["FaintBrush"] = "#94A3B8",
        ["PillBrush"] = "#0F172A", ["InputBgBrush"] = "#F8FAFC", ["InputBorderBrush"] = "#CBD5E1",
        ["GoodSoftBrush"] = "#ECFDF5", ["DangerSoftBrush"] = "#FFF1F2", ["WarnSoftBrush"] = "#FFFBEB",
    };

    private static readonly Dictionary<string, string> Dark = new()
    {
        ["BgBrush"] = "#0B1220", ["SurfaceBrush"] = "#111B30", ["Surface2Brush"] = "#1A2742",
        ["BorderBrush"] = "#243352", ["SoftBorderBrush"] = "#1A2742",
        ["AccentSoftBrush"] = "#14264F", ["AccentDarkBrush"] = "#1E3A8A", ["AccentTextBrush"] = "#BFDBFE",
        ["InkBrush"] = "#E6EDF8", ["TextBrush"] = "#E6EDF8", ["MutedBrush"] = "#9AA9C4", ["FaintBrush"] = "#6B7B99",
        ["PillBrush"] = "#2A3A5E", ["InputBgBrush"] = "#0D1628", ["InputBorderBrush"] = "#2B3B5E",
        ["GoodSoftBrush"] = "#0B3A2C", ["DangerSoftBrush"] = "#3F1020", ["WarnSoftBrush"] = "#3D2A08",
    };

    public static bool IsDark { get; private set; }

    public static void Load()
    {
        try
        {
            using var db = new AppDb();
            Apply(FinanceService.GetSetting(db, "Theme") == "Dark");
        }
        catch { Apply(false); }
    }

    public static void Set(bool dark)
    {
        Apply(dark);
        try { using var db = new AppDb(); FinanceService.SetSetting(db, "Theme", dark ? "Dark" : "Light"); } catch { }
    }

    private static void Apply(bool dark)
    {
        IsDark = dark;
        var res = Application.Current.Resources;
        foreach (var (key, hex) in dark ? Dark : Light)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            res[key] = brush;
        }
    }
}
