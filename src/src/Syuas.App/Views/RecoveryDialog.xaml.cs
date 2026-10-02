using System.Windows;
using Syuas.Core.Models;
using Syuas.Core.ViewModels;

namespace Syuas.App.Views;

public partial class RecoveryDialog : Window
{
    private readonly RecoveryListViewModel model;
    public RecoveryDialog(RecoveryListViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
    }
    public RecoveryKey? SelectedKey => model.Selected?.Candidate.Key;
    public bool DiscardRequested { get; private set; }
    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (model.CanRestore) DialogResult = true;
    }
    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        if (model.Selected is not { } selected) return;
        if (MessageBox.Show(this, $"「{selected.Name}」の復元用コピーを破棄しますか？\nこの操作は取り消せません。元ファイルは変更しません。",
            "復元データの破棄", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        DiscardRequested = true;
        DialogResult = true;
    }
}
