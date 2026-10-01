using System.IO;
using System.Windows;
using Microsoft.Win32;
using Syuas.App.Views;
using Syuas.Core.Models;
using Syuas.Core.ViewModels;

namespace Syuas.App.Services;

public sealed class InputAssistanceDialogs(Window owner) : IInputAssistanceDialogs
{
    public InsertionSnippet? Show(AssistanceKind kind, InsertionContext context)
    {
        InputDialog? dialog = null;
        var model = new InputFormViewModel(kind, context, field =>
        {
            var picker = new OpenFileDialog
            {
                Filter = kind == AssistanceKind.Image ? "画像|*.png;*.jpg;*.jpeg;*.gif;*.svg;*.bmp;*.webp|すべてのファイル|*.*" : "すべてのファイル|*.*",
                CheckFileExists = true,
                InitialDirectory = context.DocumentPath is null ? "" : Path.GetDirectoryName(context.DocumentPath)
            };
            if (picker.ShowDialog(dialog ?? owner) == true) field.Value = picker.FileName;
        });
        dialog = new InputDialog(model) { Owner = owner };
        return dialog.ShowDialog() == true ? model.Snippet : null;
    }
}
