// ── utils/aviso.js — aviso discreto de instabilidade (ES module) ─────────────
// Uma faixa fixa no rodapé, sem bloquear a página: "Conexão instável, tentando de
// novo…". Usado quando o servidor responde 503 transitório (ex.: sessão não pôde
// ser conferida) — o usuário NÃO é deslogado, só avisado.

import { el } from '/assets/js/utils/dom.js';

let faixa = null;

export function mostrarAviso(texto) {
    if (!faixa) {
        faixa = el('div', {
            class: 'alert alert-warning shadow-sm py-2 px-3 small mb-0 position-fixed bottom-0 start-50 translate-middle-x',
            role: 'status',
            'aria-live': 'polite',
            style: 'z-index: 1090; margin-bottom: 1rem;'
        });
        document.body.append(faixa);
    }
    faixa.textContent = texto;
    faixa.hidden = false;
}

export function esconderAviso() {
    if (faixa) faixa.hidden = true;
}
