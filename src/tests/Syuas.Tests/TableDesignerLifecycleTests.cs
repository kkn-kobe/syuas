using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.App.Views;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class TableDesignerLifecycleTests
{
    [Theory]
    [InlineData("\n", "apply")]
    [InlineData("\r\n", "apply")]
    [InlineData("\n", "cancel")]
    [InlineData("\n", "close")]
    public void NewTableModalHistoryOnlyEntersDocumentAfterInsert(string nl, string action) => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        var original = "前の本文" + nl + nl + "後の本文";
        editor.Load(original);
        editor.Select(original.IndexOf("後", StringComparison.Ordinal), 0);
        var insertion = new EditorInsertionService(editor);
        var context = insertion.Capture(AssistanceKind.Table, null);
        var model = new TableDesignerViewModel(nl);
        var result = Show(model, dialog =>
        {
            var title = (TextBox)dialog.FindName("TitleBox");
            model.BeginTextEdit(new(new(TableInputKind.Title), 0, 0));
            title.Text = "新"; title.Text = "新しい表";
            model.Cells[0].Text = "本文";
            model.AddRowCommand.Execute(null);
            ApplicationCommands.Undo.Execute(null, dialog);
            ApplicationCommands.Redo.Execute(null, dialog);
            Assert.Equal(original, editor.Text);
            Assert.False(editor.CanUndo); Assert.False(editor.IsModified);
            if (action == "apply") model.ConfirmCommand.Execute(null);
            else if (action == "cancel") Invoke((Button)dialog.FindName("CancelButton"));
            else dialog.Close();
        });
        if (result == true) insertion.Apply(context, model.Snippet!);
        Assert.Equal(action == "apply", result == true);
        if (action == "apply")
        {
            var updated = editor.Text;
            Assert.Contains(".新しい表" + nl, updated);
            Assert.Contains("|本文", updated);
            Assert.EndsWith(nl + nl + "後の本文", updated);
            editor.Undo(); Assert.Equal(original, editor.Text); Assert.False(editor.CanUndo); Assert.False(editor.IsModified);
            editor.Redo(); Assert.Equal(updated, editor.Text);
        }
        else { Assert.Equal(original, editor.Text); Assert.False(editor.CanUndo); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedApplyKeepsModalAndHistoryUntilUndoCorrectsOrUserCancels(bool cancel) => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load(TableLocatorTests.Table);
        using var documents = new DocumentSessionController(editor, new Utf8FileService());
        var service = new TableEditingService(editor, () => documents.Session);
        var context = service.BeginEdit().Context!;
        var model = new TableDesignerViewModel(definition: context.Definition, applyEdit: () => service.Apply(context));
        var result = Show(model, dialog =>
        {
            model.Title = "採用するタイトル";
            model.Cells[0].Text = "include::unsupported.adoc[]";
            model.ConfirmCommand.Execute(null);
            Assert.True(dialog.IsVisible);
            Assert.Contains("include", model.Error);
            Assert.Equal(2, model.UndoCount);
            Assert.Equal(TableLocatorTests.Table, editor.Text);
            ApplicationCommands.Undo.Execute(null, dialog);
            Assert.Empty(model.Error);
            Assert.Equal("日本語 😀", model.Cells[0].Text);
            ApplicationCommands.Redo.Execute(null, dialog);
            model.ConfirmCommand.Execute(null);
            Assert.True(dialog.IsVisible);
            Assert.Equal(2, model.UndoCount);
            ApplicationCommands.Undo.Execute(null, dialog);
            if (cancel) Invoke((Button)dialog.FindName("CancelButton"));
            else model.ConfirmCommand.Execute(null);
        });
        Assert.Equal(!cancel, result == true);
        if (!cancel)
        {
            Assert.StartsWith(".採用するタイトル", editor.Text);
            editor.Undo();
        }
        Assert.Equal(TableLocatorTests.Table, editor.Text);
        Assert.False(editor.CanUndo); Assert.False(editor.IsModified);
    });

    [Fact]
    public void HelpOpensFromDesignerWithoutLosingEditsOrHistory() => Sta.Run(() =>
    {
        var model = new TableDesignerViewModel();
        Exception? failure = null;
        Show(model, dialog =>
        {
            model.Title = "説明を読んでも保持する";
            var helpButton = (Button)dialog.FindName("HelpButton");
            model.SetTextComposition(true);
            Assert.False(helpButton.IsEnabled);
            model.SetTextComposition(false);
            Invoke(helpButton);
            dialog.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    var help = Assert.Single(dialog.OwnedWindows.OfType<TableEditingHelpDialog>());
                    var text = ((TextBox)help.FindName("GuideText")).Text;
                    Assert.Contains("結合をUndo", text);
                    Assert.Contains("Ctrl+Shift+Z", text);
                    Assert.Contains("100操作", text);
                    Assert.Contains("未適用", text);
                    help.Close();
                    Assert.True(dialog.IsVisible);
                    Assert.Equal("説明を読んでも保持する", model.Title);
                    Assert.Equal(1, model.UndoCount);
                    ApplicationCommands.Undo.Execute(null, dialog);
                    Assert.Empty(model.Title);
                }
                catch (Exception e) { failure = e; }
                finally { dialog.Close(); }
            }));
        });
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    });

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static bool? Show(TableDesignerViewModel model, Action<TableDesignerDialog> action)
    {
        var dialog = new TableDesignerDialog(model) { Opacity = 0, ShowActivated = false };
        Exception? failure = null;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => { failure ??= new TimeoutException("Designer did not close."); dialog.Close(); };
        dialog.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            try { action(dialog); }
            catch (Exception e) { failure = e; dialog.Close(); }
        }));
        timeout.Start();
        bool? result;
        try { result = dialog.ShowDialog(); }
        finally { timeout.Stop(); dialog.Close(); }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
}
