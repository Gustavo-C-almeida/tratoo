// ── core/app.js — bootstrap único da aplicação (ES module) ───────────────────
// Carregado como `<script type="module">` em TODA página. Responsável por:
//   1. Registrar os Custom Elements compartilhados (<tratoo-header>, <tratoo-footer>).
//   2. Injetar componentes menores ainda baseados em fetch (ex.: cadastro).
//   3. Expor o contrato de inicialização de página `onReady()`.
//
// Substitui o antigo trio de scripts clássicos (app.js + header.js +
// loadComponents.js) que dependiam da ordem de inclusão no <head>/<body>.

import { TratooHeader } from '/assets/js/components/tratoo-header.js';
import { TratooFooter } from '/assets/js/components/tratoo-footer.js';

// ── Bootstrap JS (bundle UMD, vendorizado) ───────────────────────────────────
// Injetado dinamicamente para não exigir uma tag <script> em cada uma das ~36
// páginas. Os componentes interativos (dropdown, offcanvas, modal, collapse)
// funcionam por data-api delegado no document, então elementos injetados
// depois — como o próprio <tratoo-header> — também são cobertos.
//
// Vendorizado (e não CDN) porque o CSP da API é `script-src 'self'`: script de
// terceiro seria bloqueado. Ver Program.cs.
function carregarBootstrapJs() {
    if (document.getElementById('bootstrap-bundle')) return Promise.resolve();
    return new Promise(resolve => {
        const s = document.createElement('script');
        s.id = 'bootstrap-bundle';
        s.src = '/assets/vendor/bootstrap/bootstrap.bundle.min.js';
        // Resolve mesmo em erro: a página deve renderizar sem os comportamentos
        // interativos em vez de travar o boot.
        s.onload = resolve;
        s.onerror = resolve;
        document.head.appendChild(s);
    });
}

/** Promise resolvida quando o bundle do Bootstrap terminou de carregar. */
export const bootstrapReady = carregarBootstrapJs();

// ── Registro idempotente dos Custom Elements ─────────────────────────────────
if (!customElements.get('tratoo-header')) {
    customElements.define('tratoo-header', TratooHeader);
}
if (!customElements.get('tratoo-footer')) {
    customElements.define('tratoo-footer', TratooFooter);
}

// ── loadComponent — injeta um fragmento HTML dentro de um elemento por id ─────
// Mantido para componentes menores ainda não migrados para Custom Element
// (migração incremental — ver CONTRIBUTING.md). No-op quando o alvo não existe.
export function loadComponent(id, url) {
    const el = document.getElementById(id);
    if (!el) return Promise.resolve();
    return fetch(url)
        .then(r => r.text())
        .then(html => { el.innerHTML = html; })
        .catch(() => {});
}

// Formulário de cadastro (contratante/prestador) — injetado em #cadastro quando
// presente. No-op nas demais páginas.
loadComponent('cadastro', '/components/auth/cadastro.html');

// ── onReady — contrato único de inicialização de página ──────────────────────
// Cada page-script chama `onReady(initPage)` em vez de registrar seu próprio
// listener de DOMContentLoaded. Executa imediatamente se o DOM já estiver pronto.
export function onReady(fn) {
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', fn, { once: true });
    } else {
        fn();
    }
}
