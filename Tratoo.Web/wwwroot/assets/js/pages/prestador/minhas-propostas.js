import { api } from '/assets/js/services/api.js';
// ── Minhas propostas (prestador) ─────────────────────────────────────────────

const root = () => document.getElementById('propostas-root');

function esc(str) {
    if (!str) return '';
    return String(str).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

function moeda(v) {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(v ?? 0);
}

function dataFmt(d) {
    if (!d) return '—';
    return new Date(d).toLocaleDateString('pt-BR');
}

const STATUS_LABEL = {
    Draft:'Rascunho', Submitted:'Aguardando análise',
    EmNegociacao:'Em negociação', Aceita:'Aceita',
    Recusada:'Recusada', Expirada:'Expirada', Convertida:'Convertida'
};

const STATUS_COR = {
    Draft:'cinza', Submitted:'azul', EmNegociacao:'amarelo',
    Aceita:'verde', Recusada:'vermelho', Expirada:'cinza', Convertida:'verde'
};

let filtroAtual = 'ativas';

function tempoRestante(validoAte) {
    if (!validoAte) return '';
    const agora = Date.now();
    const expira = new Date(validoAte).getTime();
    const diff = expira - agora;
    if (diff <= 0) return '<span class="validade-expirada">Expirada</span>';
    const dias = Math.floor(diff / 86400000);
    if (dias > 7) return `<span class="validade-ok">Expira em ${dias} dias</span>`;
    if (dias >= 1) return `<span class="validade-alerta">Expira em ${dias} dia${dias > 1 ? 's' : ''}</span>`;
    const horas = Math.floor(diff / 3600000);
    return `<span class="validade-alerta">Expira em ${horas}h</span>`;
}

async function carregar() {
    root().innerHTML = '<div class="msg-centro">Carregando propostas...</div>';

    let propostas;
    try {
        propostas = await api.get('/api/me/propostas');
    } catch {
        root().innerHTML = `
        <div class="msg-centro">
            <p class="erro">Faça login para ver suas propostas.</p>
            <a href="../auth/login.html" class="btn-enviar">Entrar</a>
        </div>`;
        return;
    }

    if (!propostas.length) {
        root().innerHTML = `
        <div class="page-wrap">
            <div class="page-header">
                <h1>Minhas propostas</h1>
            </div>
            <p class="hint center" style="padding:40px">Você ainda não enviou nenhuma proposta.</p>
            <div style="text-align:center">
                <a href="../projetos/index.html" class="btn-enviar">Explorar projetos</a>
            </div>
        </div>`;
        return;
    }

    const ativas = propostas.filter(p => !['Recusada','Expirada','Convertida'].includes(p.status));
    const encerradas = propostas.filter(p => ['Recusada','Expirada','Convertida'].includes(p.status));

    root().innerHTML = `
    <div class="page-wrap">
        <div class="page-header">
            <h1>Minhas propostas</h1>
            <div style="display:flex;gap:0.5rem">
                <a href="../../pages/me/contratos.html" class="btn-secundario">Meus contratos</a>
                <a href="../projetos/index.html" class="btn-secundario">Explorar projetos</a>
            </div>
        </div>

        <div class="filtro-tabs">
            <button class="filtro-tab ${filtroAtual === 'ativas' ? 'ativo' : ''}" data-filtro="ativas">
                Ativas (${ativas.length})
            </button>
            <button class="filtro-tab ${filtroAtual === 'encerradas' ? 'ativo' : ''}" data-filtro="encerradas">
                Encerradas (${encerradas.length})
            </button>
        </div>

        <div id="lista-ativas" class="propostas-lista" style="${filtroAtual !== 'ativas' ? 'display:none' : ''}">
            ${ativas.length ? ativas.map(renderCard).join('') : '<p class="hint center" style="padding:24px">Nenhuma proposta ativa.</p>'}
        </div>
        <div id="lista-encerradas" class="propostas-lista" style="${filtroAtual !== 'encerradas' ? 'display:none' : ''}">
            ${encerradas.length ? encerradas.map(renderCard).join('') : '<p class="hint center" style="padding:24px">Nenhuma proposta encerrada.</p>'}
        </div>
    </div>`;

    document.querySelectorAll('.filtro-tab').forEach(btn => {
        btn.addEventListener('click', () => {
            filtroAtual = btn.dataset.filtro;
            document.querySelectorAll('.filtro-tab').forEach(b => b.classList.remove('ativo'));
            btn.classList.add('ativo');
            document.getElementById('lista-ativas').style.display = filtroAtual === 'ativas' ? '' : 'none';
            document.getElementById('lista-encerradas').style.display = filtroAtual === 'encerradas' ? '' : 'none';
        });
    });
}

function renderCard(p) {
    const validade = tempoRestante(p.validoAte);
    return `
    <a href="../proposta/detalhe.html?id=${p.id}" class="proposta-card">
        <div class="proposta-card-header">
            <div class="proposta-projeto">${esc(p.projetoTitulo)}</div>
            <span class="status-badge status-${(STATUS_COR[p.status] || 'cinza')}">${STATUS_LABEL[p.status] || p.status}</span>
        </div>
        <div class="proposta-card-meta">
            <span>${moeda(p.valorTotal)}</span>
            <span>Prazo: ${dataFmt(p.prazoTotal)}</span>
            <span>Revisões: ${p.revisoesInclusas ?? '—'}</span>
        </div>
        <div class="proposta-objetivo">${esc(p.objetivo)}</div>
        ${validade ? `<div class="proposta-validade">${validade}</div>` : ''}
    </a>`;
}

carregar();
