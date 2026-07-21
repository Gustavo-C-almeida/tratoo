import { api } from '/assets/js/services/api.js';
// ── Administração de Disputas — Lista + Filtros ─────────────────────────────

const root = () => document.getElementById('admin-root');

const STATUS_LABEL = {
    Aberta: 'Aberta',
    EmAnalise: 'Em análise',
    ResolvidaAFavorContratante: 'Resolvida — Contratante',
    ResolvidaAFavorPrestador: 'Resolvida — Prestador',
    Encerrada: 'Encerrada'
};

const STATUS_CLASS = {
    Aberta: 'badge-aberta',
    EmAnalise: 'badge-analise',
    ResolvidaAFavorContratante: 'badge-resolvida',
    ResolvidaAFavorPrestador: 'badge-resolvida',
    Encerrada: 'badge-encerrada'
};

function esc(s) {
    if (!s && s !== 0) return '';
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function moeda(v) {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(v ?? 0);
}

function dataHora(d) {
    if (!d) return '—';
    const iso = d.endsWith('Z') || d.includes('+') ? d : d + 'Z';
    const dt = new Date(iso);
    return dt.toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

let _todas = [];

async function carregar() {
    root().innerHTML = '<div class="admin-loading">Carregando disputas...</div>';
    try {
        _todas = await api.get('/api/admin/disputas');
    } catch (err) {
        root().innerHTML = `<div class="admin-erro">${esc(err?.data?.mensagem || 'Erro ao carregar disputas.')}</div>`;
        return;
    }
    render();
}

function render() {
    root().innerHTML = `
    <div class="admin-wrap">
        <div class="admin-header">
            <h1><i class="fa-solid fa-scale-balanced"></i> Administração de Disputas</h1>
            <p class="admin-sub">Analise e resolva disputas entre contratantes e prestadores.</p>
        </div>

        <div class="admin-filtros">
            <select id="f-status">
                <option value="">Todos os status</option>
                <option value="Aberta">Aberta</option>
                <option value="EmAnalise">Em análise</option>
                <option value="ResolvidaAFavorContratante">Resolvida — Contratante</option>
                <option value="ResolvidaAFavorPrestador">Resolvida — Prestador</option>
                <option value="Encerrada">Encerrada</option>
            </select>
            <input type="date" id="f-de" title="Aberta a partir de">
            <input type="date" id="f-ate" title="Aberta até">
            <input type="number" id="f-contratante" placeholder="ID contratante" min="1">
            <input type="number" id="f-prestador" placeholder="ID prestador" min="1">
            <input type="number" id="f-projeto" placeholder="ID projeto" min="1">
            <button id="btn-filtrar" class="admin-btn"><i class="fa-solid fa-magnifying-glass"></i> Filtrar</button>
            <button id="btn-limpar" class="admin-btn admin-btn--ghost">Limpar</button>
        </div>

        <div id="lista-disputas"></div>
    </div>`;

    document.getElementById('btn-filtrar').addEventListener('click', aplicarFiltros);
    document.getElementById('btn-limpar').addEventListener('click', limparFiltros);

    renderLista(_todas);
}

function renderLista(disputas) {
    const cont = document.getElementById('lista-disputas');
    if (!disputas.length) {
        cont.innerHTML = '<div class="admin-vazio">Nenhuma disputa encontrada com os filtros atuais.</div>';
        return;
    }

    cont.innerHTML = `
    <table class="admin-tabela">
        <thead>
            <tr>
                <th>Status</th>
                <th>Projeto</th>
                <th>Contratante</th>
                <th>Prestador</th>
                <th>Valor</th>
                <th>Aberta em</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            ${disputas.map(d => `
            <tr>
                <td><span class="admin-badge ${STATUS_CLASS[d.status] || ''}">${STATUS_LABEL[d.status] || d.status}</span></td>
                <td>${esc(d.projetoTitulo)} <span class="admin-id">#${d.projetoId}</span></td>
                <td>${esc(d.contratanteNome)} <span class="admin-id">#${d.contratanteId}</span></td>
                <td>${esc(d.prestadorNome)} <span class="admin-id">#${d.prestadorId}</span></td>
                <td>${moeda(d.valorNegociado)}</td>
                <td>${dataHora(d.abertoEm)}</td>
                <td><a class="admin-link" href="/pages/admin/disputa.html?id=${d.disputaId}">Analisar <i class="fa-solid fa-arrow-right"></i></a></td>
            </tr>`).join('')}
        </tbody>
    </table>`;
}

async function aplicarFiltros() {
    const params = new URLSearchParams();
    const status = document.getElementById('f-status').value;
    const de = document.getElementById('f-de').value;
    const ate = document.getElementById('f-ate').value;
    const contratante = document.getElementById('f-contratante').value;
    const prestador = document.getElementById('f-prestador').value;
    const projeto = document.getElementById('f-projeto').value;

    if (status) params.set('status', status);
    if (de) params.set('de', de);
    if (ate) {
        // Filtro "até" inclusivo: soma 1 dia (backend usa < ate)
        const dt = new Date(ate); dt.setDate(dt.getDate() + 1);
        params.set('ate', dt.toISOString().split('T')[0]);
    }
    if (contratante) params.set('contratanteId', contratante);
    if (prestador) params.set('prestadorId', prestador);
    if (projeto) params.set('projetoId', projeto);

    const cont = document.getElementById('lista-disputas');
    cont.innerHTML = '<div class="admin-loading">Filtrando...</div>';
    try {
        const res = await api.get(`/api/admin/disputas?${params.toString()}`);
        renderLista(res);
    } catch (err) {
        cont.innerHTML = `<div class="admin-erro">${esc(err?.data?.mensagem || 'Erro ao filtrar.')}</div>`;
    }
}

function limparFiltros() {
    ['f-status', 'f-de', 'f-ate', 'f-contratante', 'f-prestador', 'f-projeto'].forEach(id => {
        document.getElementById(id).value = '';
    });
    renderLista(_todas);
}

carregar();
