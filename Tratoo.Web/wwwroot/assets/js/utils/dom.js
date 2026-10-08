// ── utils/dom.js — montagem de DOM sem interpretar dados como HTML (ES module)
// Alternativa a template string + innerHTML para trechos que exibem dados do
// usuário, da URL ou da API: nada passa pelo parser de HTML — textos viram nós
// de texto (como textContent) e atributos são gravados com setAttribute.
//
//   import { el } from '/assets/js/utils/dom.js';
//   lista.append(el('li', { class: 'item', 'data-id': item.id }, item.nome));
//
// Atributos de URL (href, src) devem partir de um caminho fixo do próprio site,
// com os parâmetros em encodeURIComponent — nunca uma URL vinda inteira de fora.

const SVG_NS = 'http://www.w3.org/2000/svg';

// Cria um elemento HTML.
// - attrs: `true` gera atributo booleano (required, hidden...); false, null e
//   undefined omitem o atributo. Handlers inline (on*) são recusados — use
//   addEventListener.
// - filhos: Nodes entram como estão; strings/números viram texto; arrays são
//   achatados; false, null e undefined são ignorados (permite `cond && el(...)`).
export function el(tag, attrs = {}, ...filhos) {
    return montar(document.createElement(tag), attrs, filhos);
}

// Igual a el(), para elementos SVG (ícones inline).
export function svg(tag, attrs = {}, ...filhos) {
    return montar(document.createElementNS(SVG_NS, tag), attrs, filhos);
}

function montar(node, attrs, filhos) {
    for (const nome of Object.keys(attrs)) {
        const valor = attrs[nome];
        if (/^on/i.test(nome)) throw new Error(`dom.js: use addEventListener em vez do atributo "${nome}".`);
        if (valor === false || valor == null) continue;
        node.setAttribute(nome, valor === true ? '' : valor);
    }
    for (const filho of filhos.flat(Infinity)) {
        if (filho != null && filho !== false) node.append(filho);
    }
    return node;
}
