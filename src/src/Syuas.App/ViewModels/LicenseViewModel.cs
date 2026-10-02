using System.IO;
using Syuas.Core.ViewModels;

namespace Syuas.App.ViewModels;

public sealed record LicenseDocument(string Title, string Text);

public sealed record LicenseEntry(string Name, string Version, string LicenseName, IReadOnlyList<LicenseDocument> Documents)
{
    public string Summary => $"{Version} · {LicenseName}";
}

public sealed class LicenseViewModel : ObservableObject
{
    public IReadOnlyList<LicenseEntry> Entries { get; }
    private LicenseEntry _selectedEntry;
    private LicenseDocument _selectedDocument;

    public LicenseViewModel()
    {
        Entries = [
            new("SYUAS", typeof(LicenseViewModel).Assembly.GetName().Version?.ToString(3) ?? "", "MIT License",
                [Read("ライセンス", "SYUAS")]),
            new("Asciidoctor.js", "4.1.0", "MIT License", [Read("ライセンス", "Asciidoctor")]),
            new("AvalonEdit", "6.3.1.120", "MIT License", [Read("ライセンス", "AvalonEdit")]),
            new("WebView2 SDK", "1.0.4191.47", "BSD 3-Clause",
                [Read("ライセンス", "WebView2"), Read("第三者通知", "WebView2Notice")])
        ];
        _selectedEntry = Entries[0];
        _selectedDocument = _selectedEntry.Documents[0];
    }

    public LicenseEntry SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (value is null || value == _selectedEntry) return;
            _selectedEntry = value;
            Changed();
            SelectedDocument = value.Documents[0];
        }
    }

    public LicenseDocument SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (value is null || value == _selectedDocument) return;
            _selectedDocument = value;
            Changed();
        }
    }

    private static LicenseDocument Read(string title, string resource)
    {
        using var stream = typeof(LicenseViewModel).Assembly.GetManifestResourceStream($"Syuas.Licenses.{resource}.txt")
            ?? throw new InvalidOperationException($"ライセンスのリソースがありません: {resource}");
        using var reader = new StreamReader(stream);
        return new(title, reader.ReadToEnd());
    }
}
