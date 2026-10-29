using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FinanceTracker.Data;
using FinanceTracker.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace FinanceTracker;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            try { File.AppendAllText(Path.Combine(AppDb.DataDir, "error.log"), $"[{DateTime.Now:s}] {args.Exception}\n\n"); } catch { }
            try { ViewModels.Ui.Error("Something went wrong: " + (args.Exception.InnerException ?? args.Exception).Message); } catch { }
            args.Handled = true;
        };
        Controls.SmoothScroll.Install();
        EventManager.RegisterClassHandler(typeof(TabControl), Selector.SelectionChangedEvent, new SelectionChangedEventHandler(TabFade));
        try
        {
            LiveCharts.Configure(c => c.AddSkiaSharp().AddDefaultMappers().AddLightTheme());
            AppDb.Initialize();
            using (var db = new AppDb()) FinanceService.PostRecurringIncome(db);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open the database:\n{ex.Message}", "Finance Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (SecurityService.HasPin() || SecurityService.NeedsFirstRunSetup())
        {
            var lockWin = new LockWindow();
            if (lockWin.ShowDialog() != true) { Shutdown(); return; }
        }

        var main = new MainWindow();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = main;
        main.Show();
        NotificationService.NotifyDue();
    }

    /// <summary>Fades and nudges tab content when the user switches tabs.</summary>
    private static void TabFade(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tc || !ReferenceEquals(e.OriginalSource, tc)) return;
        if (tc.Template.FindName("PART_SelectedContentHost", tc) is not ContentPresenter host) return;
        host.RenderTransform = new TranslateTransform();
        host.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        host.RenderTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
