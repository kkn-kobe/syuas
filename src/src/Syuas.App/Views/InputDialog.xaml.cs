using System.Windows;
using Syuas.Core.ViewModels;

namespace Syuas.App.Views;

public partial class InputDialog : Window
{
    public InputDialog(InputFormViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += OnInsert;
        Closed += (_, _) => viewModel.CloseRequested -= OnInsert;
    }
    private void OnInsert(object? sender, EventArgs e) => DialogResult = true;
}
