using System.IO;
using System.Windows;

namespace Syuas.App.Views;

public partial class RecoveryHelpDialog : Window
{
    public RecoveryHelpDialog()
    {
        InitializeComponent();
        using var stream = typeof(RecoveryHelpDialog).Assembly.GetManifestResourceStream("Syuas.RecoveryGuide.txt")
            ?? throw new InvalidOperationException("利用説明のリソースがありません。");
        using var reader = new StreamReader(stream);
        GuideText.Text = reader.ReadToEnd();
        GuideText.CaretIndex = 0;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
