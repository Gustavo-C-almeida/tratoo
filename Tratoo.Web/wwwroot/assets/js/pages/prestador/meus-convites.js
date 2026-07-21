import { api } from '/assets/js/services/api.js';
// ── Meus Convites (prestador) ────────────────────────────────────────────────

const root = () => document.getElementById('convites-root');

function esc(str) {
    if (!str) return '';
    return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function moeda(v) {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(v ?? 0);
}

function dataFmt(d) {
    if (!d) return '—';
    return new Date(d).toLocaleDateString('pt-BR');
}

const STATUS_LABEL = {
    Pendente: 'Pendente',
    Aceito: 'Aceito',
    Recusado: 'Recusado',
    Expirado: 'Expirado'
};

const STATUS_COR = {
    Pendente: 'pendente',
    Aceito: 'aceito',
    Recusado: 'recusado',
    Expirado: 'expirado'
};

let filtroAtual = 'pendentes';
let _convites = [];

function tempoDesdeConvite(criadoEm) {
    if (!criadoEm) return '';
    const agora = Date.now();
    const criado = new Date(criadoEm).getTime();
    const diff = agora - criado;
    const dias = Math.floor(diff / 86400000);
    if (dias === 0) return 'Hoje';
    if (dias === 1) return 'Ontem';
    if (dias < 7) return `${dias} dias atrás`;
    if (dias < 30) return `${Math.floor(dias / 7)} semana${Math.floor(dias / 7) > 1 ? 's' : ''} atrás`;
    return dataFmt(criadoEm);
}

async function carregar() {
    root().innerHTML = '<div class="msg-centro">Carregando convites...</div>';

    try {
        _convites = await api.get('/api/me/convites');
    } catch {
        root().innerHTML = `
        <div class="msg-centro">
            <p class="erro">Faça login para ver seus convites.</p>
            <a href="../auth/login.html" class="btn-enviar">Entrar</a>
        </div>`;
        return;
    }

    renderLista();
}

function renderLista() {
    const pendentes = _convites.filter(c => c.status === 'Pendente');
    const respondidos = _convites.filter(c => c.status !== 'Pendente');

    if (!_convites.length) {
        root().innerHTML = `
        <div class="convites-container">
            <h1>Meus Convites</h1>
            <p class="convites-subtitle">Convites recebidos de contratantes para seus projetos.</p>
            <p class="hint center" style="padding:40px">Você ainda não recebeu nenhum convite.</p>
            <div style="text-align:center">
                <a href="../projetos/index.html" class="btn-enviar">Explorar projetos</a>
            </div>
        </div>`;
        return;
    }

    root().innerHTML = `
    <div class="convites-container">
        <h1>Meus Convites</h1>
        <p class="convites-subtitle">Convites recebidos de contratantes para seus projetos.</p>

        <div class="convites-filtros">
            <button class="${filtroAtual === 'pendentes' ? 'ativo' : ''}" data-filtro="pendentes">
                Pendentes (${pendentes.length})
            </button>
            <button class="${filtroAtual === 'respondidos' ? 'ativo' : ''}" data-filtro="respondidos">
                Respondidos (${respondidos.length})
            </button>
            <button class="${filtroAtual === 'todos' ? 'ativo' : ''}" data-filtro="todos">
                Todos (${_convites.length})
            </button>
        </div>

        <div id="lista-pendentes" style="${filtroAtual !== 'pendentes' ? 'display:none' : ''}">
            ${pendentes.length
                ? pendentes.map(renderCard).join('')
                : '<p class="hint center" style="padding:24px">Nenhum convite pendente.</p>'}
        </div>
        <div id="lista-respondidos" style="${filtroAtual !== 'respondidos' ? 'display:none' : ''}">
            ${respondidos.length
                ? respondidos.map(renderCard).join('')
                : '<p class="hint center" style="padding:24px">Nenhum convite respondido.</p>'}
        </div>
        <div id="lista-todos" style="${filtroAtual !== 'todos' ? 'display:none' : ''}">
            ${_convites.map(renderCard).join('')}
        </div>
    </div>

    <!-- Modal de recusa -->
    <div class="modal-overlay" id="modal-recusa" style="display:none">
        <div class="modal-card" style="max-width:480px">
            <h2>Recusar convite</h2>
            <p style="color:var(--text-secondary);font-size:var(--text-sm);margin-bottom:var(--space-md)">
                Informe o motivo da recusa (opcional, mas ajuda o contratante).
            </p>
            <div class="modal-campo">
                <label for="motivo-recusa">Motivo</label>
                <textarea id="motivo-recusa" rows="3" placeholder="Ex: Não tenho disponibilidade no momento..."></textarea>
            </div>
            <div style="display:flex;gap:var(--space-sm);justify-content:flex-end;margin-top:var(--space-lg)">
                <button class="btn-ver-projeto" onclick="fecharModalRecusa()">Cancelar</button>
                <button class="btn-recusar" id="btn-confirmar-recusa" onclick="confirmarRecusa()">Confirmar recusa</button>
            </div>
        </div>
    </div>`;

    // Filtro tabs
    document.querySelectorAll('.convites-filtros button').forEach(btn => {
        btn.addEventListener('click', () => {
            filtroAtual = btn.dataset.filtro;
            document.querySelectorAll('.convites-filtros button').forEach(b => b.classList.remove('ativo'));
            btn.classList.add('ativo');
            document.getElementById('lista-pendentes').style.display = filtroAtual === 'pendentes' ? '' : 'none';
            document.getElementById('lista-respondidos').style.display = filtroAtual === 'respondidos' ? '' : 'none';
            document.getElementById('lista-todos').style.display = filtroAtual === 'todos' ? '' : 'none';
        });
    });
}

function renderCard(c) {
    const isPendente = c.status === 'Pendente';

    return `
    <div class="convite-card" data-convite-id="${c.id}">
        <div class="convite-card-header">
            <h3>
                <a href="../projetos/detalhe.html?id=${c.projetoId}">${esc(c.projetoTitulo)}</a>
            </h3>
            <span class="convite-status-badge ${STATUS_COR[c.status] || 'pendente'}">
                ${STATUS_LABEL[c.status] || c.status}
            </span>
        </div>

        <div class="convite-card-body">
            <div class="convite-info-item">
                <div class="label">Contratante</div>
                <div class="valor">
                    ${c.contratanteId
                        ? `<a href="/pages/contratante/perfil.html?id=${c.contratanteId}" class="link-perfil-contratante">${esc(c.contratanteNome)}</a>`
                        : esc(c.contratanteNome)
                    }
                </div>
            </div>
            <div class="convite-info-item">
                <div class="label">Recebido em</div>
                <div class="valor">${tempoDesdeConvite(c.criadoEm)}</div>
            </div>
            <div class="convite-info-item">
                <div class="label">Orçamento do projeto</div>
                <div class="valor">${moeda(c.orcamentoMin)} — ${moeda(c.orcamentoMax)}</div>
            </div>
            ${c.orcamentoSugerido ? `
            <div class="convite-info-item">
                <div class="label">Orçamento sugerido</div>
                <div class="valor">${moeda(c.orcamentoSugerido)}</div>
            </div>` : ''}
            ${c.prazoDesejado ? `
            <div class="convite-info-item">
                <div class="label">Prazo desejado</div>
                <div class="valor">${dataFmt(c.prazoDesejado)}</div>
            </div>` : ''}
            ${c.respondidoEm ? `
            <div class="convite-info-item">
                <div class="label">Respondido em</div>
                <div class="valor">${dataFmt(c.respondidoEm)}</div>
            </div>` : ''}
        </div>

        <div class="convite-mensagem">
            "${esc(c.mensagemInicial)}"
        </div>

        ${c.motivoRecusa ? `
        <div class="convite-mensagem" style="border-left-color:var(--error)">
            <strong>Motivo da recusa:</strong> ${esc(c.motivoRecusa)}
        </div>` : ''}

        <div class="convite-card-acoes">
            <a href="../projetos/detalhe.html?id=${c.projetoId}" class="btn-ver-projeto">Ver projeto</a>
            ${isPendente ? `
                <button class="btn-convidar" onclick="aceitarConvite('${c.id}')">Aceitar convite</button>
                <button class="btn-recusar" onclick="abrirModalRecusa('${c.id}')">Recusar</button>
            ` : ''}
        </div>
    </div>`;
}

// ── Aceitar convite ──────────────────────────────────────────────────────────

async function aceitarConvite(conviteId) {
    const card = document.querySelector(`[data-convite-id="${conviteId}"]`);
    const btnAceitar = card?.querySelector('.btn-convidar');
    if (btnAceitar) {
        btnAceitar.disabled = true;
        btnAceitar.textContent = 'Aceitando...';
    }

    try {
        const atualizado = await api.put(`/api/convites/${conviteId}/aceitar`);

        // Atualiza cache local
        const idx = _convites.findIndex(c => c.id === conviteId);
        if (idx >= 0) _convites[idx] = atualizado;

        renderLista();
    } catch (err) {
        const msg = err?.data?.mensagem || 'Erro ao aceitar convite.';
        alert(msg);
        if (btnAceitar) {
            btnAceitar.disabled = false;
            btnAceitar.textContent = 'Aceitar convite';
        }
    }
}

// ── Recusar convite (modal) ──────────────────────────────────────────────────

let _conviteRecusaId = null;

function abrirModalRecusa(conviteId) {
    _conviteRecusaId = conviteId;
    document.getElementById('motivo-recusa').value = '';
    document.getElementById('modal-recusa').style.display = 'flex';
}

function fecharModalRecusa() {
    _conviteRecusaId = null;
    document.getElementById('modal-recusa').style.display = 'none';
}

async function confirmarRecusa() {
    if (!_conviteRecusaId) return;

    const motivo = document.getElementById('motivo-recusa').value.trim();
    const btn = document.getElementById('btn-confirmar-recusa');
    btn.disabled = true;
    btn.textContent = 'Recusando...';

    try {
        const atualizado = await api.put(`/api/convites/${_conviteRecusaId}/recusar`, {
            motivo: motivo || null
        });

        // Atualiza cache local
        const idx = _convites.findIndex(c => c.id === _conviteRecusaId);
        if (idx >= 0) _convites[idx] = atualizado;

        fecharModalRecusa();
        renderLista();
    } catch (err) {
        const msg = err?.data?.mensagem || 'Erro ao recusar convite.';
        alert(msg);
        btn.disabled = false;
        btn.textContent = 'Confirmar recusa';
    }
}

// Fechar modal ao clicar fora
document.addEventListener('click', (e) => {
    const modal = document.getElementById('modal-recusa');
    if (modal && e.target === modal) {
        fecharModalRecusa();
    }
});

document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') fecharModalRecusa();
});

carregar();
