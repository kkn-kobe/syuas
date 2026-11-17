// Only this trusted page executes Mermaid. Its sandbox has no same-origin
// permission; all document scripts remain disabled in the visible preview.
const config = {
  startOnLoad: false,
  securityLevel: 'strict',
  suppressErrorRendering: true,
  maxTextSize: 20000,
  maxEdges: 200,
  theme: 'default',
  look: 'classic',
  layout: 'dagre',
  htmlLabels: false,
  fontFamily: 'Yu Gothic UI, Meiryo, Segoe UI, sans-serif',
  secure: ['secure', 'securityLevel', 'startOnLoad', 'maxTextSize', 'maxEdges',
    'suppressErrorRendering', 'dompurifyConfig', 'themeCSS', 'htmlLabels', 'layout', 'fontFamily'],
  flowchart: { htmlLabels: false }
};
let busy = false;
window.addEventListener('message', async event => {
  if (event.source !== parent || event.origin !== 'https://app.syuas.local' || busy) return;
  const request = event.data;
  if (!request || typeof request.source !== 'string' || typeof request.token !== 'string') return;
  const response = { id: request.id, token: request.token };
  busy = true;
  try {
    if (request.source.length > config.maxTextSize || /^\s*---/.test(request.source) || /%%\s*\{/.test(request.source))
      throw new Error('図の定義または設定が対応範囲外です。');
    mermaid.initialize(config);
    const result = await mermaid.render(`diagram-${request.id}`, request.source);
    // Mermaid's HTML-oriented SVG can use xlink without declaring its namespace.
    // Supply an enclosing namespace scope before parsing as a standalone image.
    const doc = new DOMParser().parseFromString(
      `<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">${result.svg}</svg>`, 'image/svg+xml');
    const svg = doc.documentElement.firstElementChild;
    if (doc.querySelector('parsererror') || svg?.localName !== 'svg') throw new Error('SVG を生成できませんでした。');
    // Additional static-image restrictions beyond Mermaid's sanitization.
    doc.querySelectorAll('script,foreignObject,image,iframe,object,embed,animate,animateMotion,animateTransform,set').forEach(node => node.remove());
    for (const node of doc.querySelectorAll('*')) {
      for (const attr of [...node.attributes]) {
        if (/^on/i.test(attr.name) || (/^(?:href|xlink:href)$/i.test(attr.name) && !attr.value.startsWith('#')))
          node.removeAttribute(attr.name);
      }
    }
    // Use the diagram's natural dimensions; CSS may shrink it to fit the pane.
    const bounds = svg.getAttribute('viewBox')?.trim().split(/[\s,]+/).map(Number);
    if (bounds?.length === 4 && bounds.every(Number.isFinite) && bounds[2] > 0 && bounds[3] > 0) {
      svg.setAttribute('width', String(bounds[2]));
      svg.setAttribute('height', String(bounds[3]));
    }
    response.svg = new XMLSerializer().serializeToString(svg);
  } catch (error) {
    response.error = String(error.message ?? error).slice(0, 600);
  } finally {
    document.body.replaceChildren();
    busy = false;
  }
  parent.postMessage(response, 'https://app.syuas.local');
});
