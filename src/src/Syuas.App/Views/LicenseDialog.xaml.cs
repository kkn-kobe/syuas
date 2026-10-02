using System.Windows;
using System.Windows.Controls;
using Syuas.App.ViewModels;

namespace Syuas.App.Views;

public partial class LicenseDialog : Window
{
    public LicenseDialog()
    {
        InitializeComponent();
        DataContext = new LicenseViewModel();
    }

    private void OnLicenseTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox text) return;
        text.CaretIndex = 0;
        text.ScrollToHome();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
