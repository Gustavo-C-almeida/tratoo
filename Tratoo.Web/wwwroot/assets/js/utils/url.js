// ── utils/url.js — leitura segura de parâmetros da URL (ES module)
// IDs da query string entram em chamadas à API e em links; só aceitamos GUID,
// o formato de todos os IDs do backend. Qualquer outra coisa vira null, e a
// página trata como "ID não informado".
//
//   import { guidDaUrl } from '/assets/js/utils/url.js';
//   const contratoId = guidDaUrl('id');

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function ehGuid(valor) {
    return typeof valor === 'string' && GUID.test(valor);
}

export function guidDaUrl(nome) {
    const valor = new URLSearchParams(location.search).get(nome);
    return ehGuid(valor) ? valor : null;
}
