using System.Windows;

namespace Syuas.App.Views;

public partial class AboutDialog : Window
{
    public AboutDialog() => InitializeComponent();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
