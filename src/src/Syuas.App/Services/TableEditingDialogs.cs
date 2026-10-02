using System.Windows;
using Syuas.App.Views;
using Syuas.Core.ViewModels;

namespace Syuas.App.Services;

public sealed class TableEditingDialogs(Window owner) : ITableEditingDialogs
{
    public void Show(TableDesignerViewModel model) => new TableDesignerDialog(model) { Owner = owner }.ShowDialog();
}
