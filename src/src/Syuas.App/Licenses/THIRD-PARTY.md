# Third-party licenses distributed with SYUAS

Keep this directory with the application when redistributing SYUAS. The original
license and notice texts are provided in English without translation.

## AvalonEdit 6.3.1.120

- Purpose: source text editor.
- License: MIT; see [AvalonEdit-LICENSE.txt](AvalonEdit-LICENSE.txt).
- Package: https://www.nuget.org/packages/AvalonEdit/6.3.1.120
- License source: https://github.com/icsharpcode/AvalonEdit/blob/862415d51eddc9eac93f462dbc522ffbf929cd52/LICENSE
- The source commit above is recorded in the NuGet package's repository metadata.
- Additional copyright attribution from the package metadata:
  2000-2025 AlphaSierraPapa for the SharpDevelop Team.

## Microsoft.Web.WebView2 SDK 1.0.4191.47

- Purpose: WebView2 integration for HTML preview.
- License: BSD 3-Clause; see [WebView2-LICENSE.txt](WebView2-LICENSE.txt).
- Third-party notices: [WebView2-NOTICE.txt](WebView2-NOTICE.txt).
- Package: https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4191.47
- The license and notice files are copied unchanged from `LICENSE.txt` and
  `NOTICE.txt` in that exact NuGet package. The notices include Antlr3.Runtime
  and StringTemplate4; retain the supplied notice file in full.
- These files cover the SDK package. The separately installed Microsoft Edge
  WebView2 Runtime has its own Microsoft license terms.

## Asciidoctor.js 4.1.0

The bundled HTML converter's MIT license and provenance are provided separately
in [ASCIIDOCTOR-LICENSE.txt](../PreviewAssets/ASCIIDOCTOR-LICENSE.txt) and
[THIRD-PARTY.md](../PreviewAssets/THIRD-PARTY.md). Keep those files with the
application as well.

## Maintenance

When updating a dependency, check the license and notices for the exact new
version, update the bundled documents and version/source information here, and
verify that both build and publish outputs contain these files. This directory
is copied automatically by `Syuas.App.csproj`.

SYUAS itself is licensed under the MIT License, Copyright (c) 2026 KUBOYAMA Kyota;
see [LICENSE](../LICENSE) in the distribution (the repository root `LICENSE` in
source checkouts). Third-party components retain their respective licenses.
The application's license dialog embeds the same license and notice files for
offline viewing. If distributing additional runtimes, retain their own applicable
licenses and third-party notices.
