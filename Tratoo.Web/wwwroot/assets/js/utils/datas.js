// ── utils/datas.js — exibição de datas de calendário ─────────────────
// Prazo de entrega, início de experiência, emissão de certificado e afins são
// um DIA, sem horário: a API os guarda à meia-noite UTC. Exibir no fuso do
// navegador mostraria o dia anterior no Brasil, então o dia é lido em UTC.
//
// Instantes reais (criadoEm, expiraEm, pagoEm...) continuam usando o fuso do
// navegador: `new Date(iso).toLocaleDateString('pt-BR')`.

/**
 * @param {string|null|undefined} iso  data ISO vinda da API ou de um input date
 * @param {Intl.DateTimeFormatOptions} [opcoes]
 * @returns {string} a data em pt-BR, ou '—' quando não há data
 */
export function formatarDiaCalendario(iso, opcoes = {}) {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString('pt-BR', { timeZone: 'UTC', ...opcoes });
}
