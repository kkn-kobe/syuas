namespace Syuas.Core.ViewModels;

public interface ITableEditingDialogs
{
    // The model applies changes before requesting close. Rejected edits stay in this dialog.
    void Show(TableDesignerViewModel model);
}
