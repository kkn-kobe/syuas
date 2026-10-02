using System.Windows;
using Syuas.Core.Models;
using Syuas.Core.ViewModels;

namespace Syuas.App.Views;

public partial class SaveConflictDialog : Window
{
    private readonly SaveConflictViewModel model;
    public SaveConflictDialog(SaveConflictViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
    }

    public SaveConflictDecision Decision { get; private set; } = SaveConflictDecision.Cancel;
    private void OnPrimary(object sender, RoutedEventArgs e) => Complete(model.PrimaryDecision);
    private void OnSaveAs(object sender, RoutedEventArgs e) => Complete(SaveConflictDecision.SaveAs);
    private void Complete(SaveConflictDecision decision) { Decision = decision; DialogResult = true; }
}
