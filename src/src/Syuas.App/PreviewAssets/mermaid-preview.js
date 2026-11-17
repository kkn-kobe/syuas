import { Extensions } from './asciidoctor.js';

const MAX_DIAGRAMS = 32;
const MAX_TEXT = 20000;
let renderer;
let ready;
let serial = 0;
const pending = new Map();
const escape = value => String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

window.addEventListener('message', event => {
  if (!renderer || event.source !== renderer.contentWindow || event.origin !== 'null') return;
  const message = event.data;
  if (!message || typeof message !== 'object') return;
  const job = pending.get(message.id);
  if (!job || message.token !== job.token) return;
  pending.delete(message.id);
  clearTimeout(job.timer);
  if (message.error) job.reject(new Error(String(message.error)));
  else job.resolve(message.svg);
});

function resetRenderer() {
  renderer?.remove();
  renderer = undefined;
  ready = undefined;
  for (const job of pending.values()) {
    clearTimeout(job.timer);
    job.reject(new Error('図の描画がタイムアウトしました。プレビューを更新してください。'));
  }
  pending.clear();
}

async function render(source) {
  if (!renderer) {
    renderer = document.createElement('iframe');
    renderer.title = 'Mermaid 描画用';
    renderer.setAttribute('aria-hidden', 'true');
    // An opaque origin: the renderer cannot access the host or document DOM.
    renderer.setAttribute('sandbox', 'allow-scripts');
    renderer.style.cssText = 'position:fixed;left:-20000px;top:0;width:1200px;height:900px;visibility:hidden;border:0';
    ready = new Promise((resolve, reject) => {
      const timer = setTimeout(() => { resetRenderer(); reject(new Error('Mermaid を読み込めませんでした。')); }, 10000);
      renderer.addEventListener('load', () => { clearTimeout(timer); resolve(); }, { once: true });
    });
    renderer.src = 'mermaid-renderer.html';
    document.body.append(renderer);
  }
  await ready;
  const id = ++serial;
  const token = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(resetRenderer, 10000);
    pending.set(id, { token, resolve, reject, timer });
    renderer.contentWindow.postMessage({ id, token, source }, '*');
  });
}

// Register on each conversion, so includes and nested AsciiDoc blocks use the
// parser's normal rules and cannot reuse another document's placeholders.
export function createMermaidPreview() {
  const diagrams = [];
  const prefix = `syuas-mermaid-${crypto.randomUUID()}-`;
  const registry = Extensions.create();
  registry.block('mermaid', function () {
    this.onContext('listing');
    this.parseContentAs('raw');
    this.process(async function (parent, reader, attrs) {
      const source = (await reader.readLines()).join('\n');
      const id = prefix + diagrams.length;
      diagrams.push({ source, id });
      const title = attrs.title ? `<div class="title">${escape(attrs.title)}</div>` : '';
      return this.createBlock(parent, 'pass', `${title}<div id="${id}" class="syuas-mermaid"></div>`, {});
    });
  });
  return {
    registry,
    async finish(html, isCurrent) {
      const messages = [];
      for (let i = 0; i < diagrams.length; i++) {
        if (!isCurrent()) return { html, messages };
        const { source, id } = diagrams[i];
        let content;
        try {
          if (i >= MAX_DIAGRAMS) throw new Error(`1 文書の図は ${MAX_DIAGRAMS} 個までです。`);
          if (source.length > MAX_TEXT) throw new Error(`図の定義は ${MAX_TEXT.toLocaleString('ja-JP')} 文字までです。`);
          if (!source.trim()) throw new Error('図の定義を入力してください。');
          // Keep configuration under host control, including CSS, sanitization,
          // URLs and layout limits. Do not attempt to parse untrusted YAML here.
          if (/^\s*---/.test(source) || /%%\s*\{/.test(source))
            throw new Error('図内の設定（frontmatter・init 指令）には対応していません。');
          const svg = await render(source);
          if (typeof svg !== 'string' || svg.length > 4 * 1024 * 1024) throw new Error('描画結果が大きすぎます。');
          // SVG is an image document, never executable markup in the host.
          content = `<img class="mermaid-diagram" alt="Mermaid 図 ${i + 1}" src="data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg).replaceAll("'", '%27')}">`;
        } catch (error) {
          const message = `Mermaid 図 ${i + 1}: ${String(error.message ?? error).slice(0, 600)}`;
          messages.push(message);
          content = `<div class="mermaid-error" role="alert">${escape(message)}</div><pre>${escape(source.slice(0, MAX_TEXT))}</pre>`;
        }
        html = html.replace(`<div id="${id}" class="syuas-mermaid"></div>`, () => `<div class="syuas-mermaid">${content}</div>`);
      }
      return { html, messages };
    }
  };
}
