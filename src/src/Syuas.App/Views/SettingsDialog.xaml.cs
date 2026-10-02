using System.Windows;
using Syuas.Core.ViewModels;

namespace Syuas.App.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(SettingsViewModel model) { InitializeComponent(); DataContext = model; }
    private void OnAccept(object sender, RoutedEventArgs e) => DialogResult = true;
}
