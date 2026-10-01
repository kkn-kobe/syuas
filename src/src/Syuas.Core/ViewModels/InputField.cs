namespace Syuas.Core.ViewModels;

public sealed class InputField : ObservableObject
{
    private string value;
    public InputField(string key, string label, string value = "", bool multiline = false,
        string[]? choices = null, bool editableChoice = false, bool file = false, bool advanced = false)
    {
        Key = key; Label = label; this.value = value; Multiline = multiline;
        Choices = choices ?? []; EditableChoice = editableChoice; IsFile = file; Advanced = advanced;
    }
    public string Key { get; }
    public string Label { get; }
    public string Value { get => value; set { this.value = value; Changed(); } }
    public bool Multiline { get; }
    public string[] Choices { get; }
    public bool HasChoices => Choices.Length > 0;
    public bool IsText => !HasChoices;
    public bool EditableChoice { get; }
    public bool IsFile { get; }
    public bool Advanced { get; }
    public RelayCommand? BrowseCommand { get; set; }
}
