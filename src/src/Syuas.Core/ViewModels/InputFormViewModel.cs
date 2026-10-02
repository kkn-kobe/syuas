using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Core.ViewModels;

public sealed class InputFormViewModel : ObservableObject
{
    private readonly AssistanceKind kind;
    private readonly InsertionContext context;
    private readonly List<InputField> fields = [];
    private readonly DocumentStructure structure;
    private ReferenceCandidate? selectedReference;
    public InputFormViewModel(AssistanceKind kind, InsertionContext context, Action<InputField>? browse = null)
    {
        this.kind = kind;
        this.context = context;
        structure = DocumentStructureParser.Parse(context.DocumentText);
        Title = kind switch
        {
            AssistanceKind.Heading => "見出し", AssistanceKind.Image => "画像", AssistanceKind.Include => "外部ファイル参照",
            AssistanceKind.Link => "リンク", AssistanceKind.CrossReference => "相互参照 (xref)", AssistanceKind.Anchor => "アンカー / ID",
            AssistanceKind.SourceBlock => "ソースブロック", AssistanceKind.Admonition => "Admonition",
            _ => throw new ArgumentException("ダイアログを使用しない操作です。")
        };
        InsertCommand = new(_ => CloseRequested?.Invoke(this, EventArgs.Empty), _ => Snippet is not null);
        switch (kind)
        {
            case AssistanceKind.Heading:
                var title = AsciiDocHeadingGenerator.StripMarker(context.Text);
                var oldLevel = context.Text.Length - title.Length - 2;
                Add(new("Level", "見出しレベル（1〜5）", Math.Clamp(oldLevel, 1, 5).ToString(), choices: ["1", "2", "3", "4", "5"]));
                Add(new("Title", "見出し文字列", title));
                break;
            case AssistanceKind.Image:
            case AssistanceKind.Include:
                Add(new("File", kind == AssistanceKind.Image ? "画像ファイル" : "参照ファイル", file: true));
                Add(new("PathMode", "パス指定方式", "相対パス", choices: ["相対パス", "絶対パス"]));
                if (kind == AssistanceKind.Image)
                {
                    Add(new("Mode", "画像の種類", "Block", choices: ["Block", "Inline"]));
                    Add(new("Alt", "Alt Text", context.Text));
                    Add(new("Title", "タイトル（任意）"));
                    Add(new("Width", "幅（例: 640 / 50%）"));
                    Add(new("Height", "高さ（任意）"));
                    Add(new("Id", "ID（任意）"));
                }
                else
                {
                    Add(new("Lines", "lines（例: 1..5;8..10）", advanced: true));
                    Add(new("Tag", "tag（linesと併用不可）", advanced: true));
                    Add(new("Offset", "leveloffset（例: +1）", advanced: true));
                    Add(new("Tags", "tags（例: intro;usage / **;!internal）", advanced: true));
                    Add(new("Indent", "indent（0以上の整数）", advanced: true));
                    Add(new("Encoding", "encoding（文字コード）", choices: ["", "UTF-8", "UTF-16", "Shift_JIS", "Windows-31J"], editableChoice: true, advanced: true));
                    Add(new("Optional", "ファイルがなくてもエラーにしない（optional）", "いいえ", choices: ["いいえ", "はい"], advanced: true));
                }
                break;
            case AssistanceKind.Link:
            case AssistanceKind.CrossReference:
                Add(new("Target", kind == AssistanceKind.Link ? "URL" : "Target（ID / 文書.adoc#ID）"));
                Add(new("Text", "表示文字列", context.Text));
                break;
            case AssistanceKind.Anchor:
                var bases = new[] { context.Text }.Concat(structure.Headings.OrderBy(h => Math.Abs(h.Offset - context.Start)).Select(h => h.Title))
                    .Where(t => !string.IsNullOrWhiteSpace(t)).DefaultIfEmpty("section");
                var suggestions = bases.Select(t => DocumentStructureParser.CreateId(t, "", "-"))
                    .Select(id => char.IsDigit(id[0]) ? "section-" + id : id)
                    .Select(id => DocumentStructureParser.UniqueId(id, structure.References.Select(r => r.Target), "-"))
                    .Distinct().Take(30).ToArray();
                Add(new("Id", "ID（候補から選択または自由入力）", suggestions[0], choices: suggestions, editableChoice: true));
                break;
            case AssistanceKind.SourceBlock:
                Add(new("Language", "プログラミング言語（自由入力可）", "C#", choices: ["C#", "C", "C++", "Java", "JavaScript", "TypeScript", "Python", "XML", "JSON", "YAML", "SQL", "Bash", "PowerShell"], editableChoice: true));
                Add(new("Text", "ソーステキスト", context.Text, multiline: true));
                break;
            case AssistanceKind.Admonition:
                Add(new("Type", "種類", "NOTE", choices: Enum.GetNames<AdmonitionKind>()));
                Add(new("Mode", "形式", "自動", choices: ["自動", "ブロック"]));
                Add(new("Text", "テキスト", context.Text, multiline: true));
                break;
        }
        foreach (var field in fields)
        {
            if (field.IsFile) field.BrowseCommand = new(_ => browse?.Invoke(field));
            field.PropertyChanged += (_, _) => Refresh();
        }
        Refresh();
    }

    public event EventHandler? CloseRequested;
    public IReadOnlyList<ReferenceCandidate> ReferenceCandidates => structure.References;
    public bool ShowReferences => kind == AssistanceKind.CrossReference;
    public ReferenceCandidate? SelectedReference
    {
        get => selectedReference;
        set
        {
            selectedReference = value; Changed();
            if (value is null) return;
            this["Target"].Value = value.Target;
            if (Value("Text").Length == 0) this["Text"].Value = value.Title;
        }
    }
    public string Title { get; }
    public IReadOnlyList<InputField> Fields => fields;
    public IEnumerable<InputField> BasicFields => fields.Where(f => !f.Advanced);
    public IEnumerable<InputField> AdvancedFields => fields.Where(f => f.Advanced);
    public bool HasAdvanced => fields.Any(f => f.Advanced);
    public string Note => kind is AssistanceKind.Image or AssistanceKind.Include
        ? context.DocumentPath is null ? "文書は未保存です。相対パスを選んでも絶対パスを挿入します。相対パスを使う場合は先に文書を保存してください。"
          : "相対パスは現在の文書のフォルダーを基準に生成します。"
        : "生成結果を確認して挿入します。選択中の文章は置き換えられます。";
    public string Error { get; private set; } = "";
    public string Preview => Snippet?.Text ?? "";
    public InsertionSnippet? Snippet { get; private set; }
    public RelayCommand InsertCommand { get; }
    public InputField this[string key] => fields.Single(f => f.Key == key);
    private string Value(string key) => this[key].Value;
    private void Add(InputField field) => fields.Add(field);

    private void Refresh()
    {
        try { Snippet = Generate(); Error = ""; }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        { Snippet = null; Error = e.Message; }
        Changed(nameof(Preview)); Changed(nameof(Error)); InsertCommand.Refresh();
    }

    private InsertionSnippet Generate()
    {
        string text;
        switch (kind)
        {
            case AssistanceKind.Heading:
                if (!int.TryParse(Value("Level"), out var level)) throw new ArgumentException("見出しレベルは1〜5を選択してください。");
                text = AsciiDocHeadingGenerator.Generate(new(level, Value("Title")));
                return new(text, text.Length, true);
            case AssistanceKind.Include:
                text = AsciiDocIncludeGenerator.Generate(new(Value("File"), context.DocumentPath, Value("PathMode") == "相対パス", Value("Lines"), Value("Tag"), Value("Offset"), Value("Tags"), Value("Indent"), Value("Encoding"), Value("Optional") == "はい"));
                return new(text, text.Length, true);
            case AssistanceKind.Image:
                var inline = Value("Mode") == "Inline";
                text = AsciiDocImageGenerator.Generate(new(Value("File"), context.DocumentPath, inline, Value("Alt"), Value("Title"), Value("Width"), Value("Height"), Value("Id"), Value("PathMode") == "相対パス"), context.NewLine);
                return new(text, Value("Alt").Length == 0 ? text.LastIndexOf("[\"", StringComparison.Ordinal) + 2 : text.Length, !inline);
            case AssistanceKind.Link:
            case AssistanceKind.CrossReference:
                text = AsciiDocLinkGenerator.Generate(new(Value("Target"), Value("Text")), kind == AssistanceKind.CrossReference);
                return new(text, Value("Text").Length == 0 ? text.IndexOf('[') + 1 : text.Length);
            case AssistanceKind.Anchor:
                if (structure.References.Any(r => r.Target == Value("Id"))) throw new ArgumentException("このIDは文書内で使用されています。別のIDを指定してください。");
                text = AsciiDocLinkGenerator.GenerateAnchor(Value("Id"));
                return new(text, text.Length);
            case AssistanceKind.SourceBlock:
                text = AsciiDocBlockGenerator.Generate(new SourceBlockDefinition(Value("Language"), Value("Text")), context.NewLine);
                return new(text, ContentStart(text, context.NewLine, true), true);
            case AssistanceKind.Admonition:
                if (!Enum.TryParse<AdmonitionKind>(Value("Type"), out var type)) throw new ArgumentException("Admonitionの種類を選択してください。");
                text = AsciiDocBlockGenerator.Generate(new AdmonitionDefinition(type, Value("Text"), Value("Mode") == "ブロック"), context.NewLine);
                return new(text, text.StartsWith('[') ? ContentStart(text, context.NewLine, true) : text.Length, true);
            default: throw new ArgumentOutOfRangeException();
        }
    }

    public static int ContentStart(string text, string newLine, bool hasHeader)
    {
        var start = text.IndexOf(newLine, StringComparison.Ordinal) + newLine.Length;
        return hasHeader ? text.IndexOf(newLine, start, StringComparison.Ordinal) + newLine.Length : start;
    }
}
