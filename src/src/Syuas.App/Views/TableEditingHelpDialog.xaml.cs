using System.IO;
using System.Windows;

namespace Syuas.App.Views;

public partial class TableEditingHelpDialog : Window
{
    public TableEditingHelpDialog()
    {
        InitializeComponent();
        using var stream = typeof(TableEditingHelpDialog).Assembly.GetManifestResourceStream("Syuas.TableReeditingGuide.txt")
            ?? throw new InvalidOperationException("利用説明のリソースがありません。");
        using var reader = new StreamReader(stream);
        GuideText.Text = reader.ReadToEnd();
        GuideText.CaretIndex = 0;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
