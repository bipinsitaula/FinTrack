using System.Windows;
using System.Windows.Input;
using FinanceTracker.Controls;
using FinanceTracker.ViewModels;

namespace FinanceTracker.Views;

/// <summary>
/// Generic pop-up for the app's add/edit forms. The fields come from a DataTemplate in Themes/Forms.xaml,
/// bound to the page view model; saving runs one of that view model's existing commands.
/// </summary>
public partial class FormDialog : DialogBase
{
    private readonly ICommand _save;

    public FormDialog(string title, string subtitle, string primaryText, string templateKey, object viewModel, ICommand save, bool showCancel = true)
    {
        InitializeComponent();
        _save = save;
        DataContext = viewModel;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        SubtitleText.Visibility = string.IsNullOrWhiteSpace(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = primaryText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        Body.Content = viewModel;
        Body.ContentTemplate = (DataTemplate)Application.Current.FindResource(templateKey);
        Loaded += (_, _) => Body.ApplyTemplate();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Move focus so pending text-box edits (including after pressing Enter) are committed first.
        PrimaryButton.Focus();
        var errorsBefore = Ui.ErrorCount;
        if (_save.CanExecute(null)) _save.Execute(null);
        // The commands report problems through Ui.Error and keep their own fields on failure, so stay open then.
        if (Ui.ErrorCount == errorsBefore) DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
