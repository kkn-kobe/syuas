using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Syuas.App.Highlighting;

public static class AsciiDocHighlighting
{
    public static IHighlightingDefinition Load()
    {
        using var stream = typeof(AsciiDocHighlighting).Assembly.GetManifestResourceStream("Syuas.App.Highlighting.AsciiDoc.xshd")
            ?? throw new InvalidOperationException("AsciiDoc highlight resource is missing.");
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
