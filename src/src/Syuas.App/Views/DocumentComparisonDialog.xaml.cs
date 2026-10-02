using System.Windows;
using Syuas.App.Highlighting;
using Syuas.Core.Models;

namespace Syuas.App.Views;

public partial class DocumentComparisonDialog : Window
{
    public DocumentComparisonDialog(DocumentComparison comparison)
    {
        InitializeComponent();
        DataContext = comparison;
        LocalEditor.Text = comparison.EditorText;
        DiskEditor.Text = comparison.DiskSnapshot.Text;
        LocalEditor.SyntaxHighlighting = AsciiDocHighlighting.Load();
        DiskEditor.SyntaxHighlighting = LocalEditor.SyntaxHighlighting;
    }
}
