import { convert, MemoryLogger, LoggerManager } from './asciidoctor.js';

const frame = document.getElementById('preview');
let latestVersion = 0;
let queue = Promise.resolve();
const send = message => window.chrome?.webview?.postMessage(message);
const css = `body{font:15px/1.65 "Segoe UI","Yu Gothic UI",sans-serif;color:#253247;margin:24px;overflow-wrap:anywhere}
h1,h2,h3,h4,h5,h6{line-height:1.3;color:#174a8b;margin:1.4em 0 .6em}h1{font-size:28px}h2{font-size:23px;border-bottom:1px solid #dce3ec;padding-bottom:8px}
a{color:#185fa9}pre{padding:16px;background:#edf2f8;overflow:auto}code{font-family:Consolas,monospace}img{max-width:100%;height:auto}
table{border-collapse:collapse;width:100%;margin:16px 0}td,th{border:1px solid #cbd5e1;padding:8px;text-align:left}th{background:#e9eff7}
td p{margin:0}.admonitionblock td.icon{width:80px;font-weight:bold}.admonitionblock{background:#f2f6fb}blockquote{border-left:4px solid #cbd5e1;padding-left:16px}
.title{font-weight:600;color:#536579}hr{border:0;border-top:1px solid #dce3ec}`;

window.renderPreview = request => {
  latestVersion = request.version;
  queue = queue.catch(() => {}).then(async () => {
    if (request.version !== latestVersion) return;
    const logger = new MemoryLogger();
    LoggerManager.setLogger(logger);
    try {
      const html = await convert(request.source, {
        safe: request.saved ? 'server' : 'secure', backend: 'html5', standalone: false,
        base_dir: 'https://document.syuas.local',
        attributes: { showtitle: true, 'max-include-depth': 10, 'webfonts!': '', 'source-highlighter!': '' }
      });
      if (request.version !== latestVersion) return;
      frame.srcdoc = `<!doctype html><html lang="ja"><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https://document.syuas.local data:; style-src 'unsafe-inline'; base-uri https://document.syuas.local; form-action 'none';">
<base href="https://document.syuas.local/"><style>${css}</style></head><body>${html}</body></html>`;
      const messages = logger.getMessages().map(entry => entry.getText()).join('\n');
      send({ type: 'rendered', version: request.version, message: messages });
    } catch (error) {
      if (request.version === latestVersion) send({ type: 'error', version: request.version, message: String(error.message ?? error) });
    }
  });
};

frame.addEventListener('load', () => {
  const doc = frame.contentDocument;
  if (!doc) return;
  doc.addEventListener('click', event => {
    const link = event.target.closest?.('a');
    if (!link) return;
    event.preventDefault();
    const href = link.getAttribute('href');
    if (href?.startsWith('#')) {
      try { doc.getElementById(decodeURIComponent(href.slice(1)))?.scrollIntoView(); } catch { /* Invalid fragment. */ }
    }
  });
});
send({ type: 'ready' });
