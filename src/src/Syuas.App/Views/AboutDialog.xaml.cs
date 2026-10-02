using System.Windows;

namespace Syuas.App.Views;

public partial class AboutDialog : Window
{
    public AboutDialog() => InitializeComponent();

    private void OnLicenses(object sender, RoutedEventArgs e) => new LicenseDialog { Owner = this }.ShowDialog();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
