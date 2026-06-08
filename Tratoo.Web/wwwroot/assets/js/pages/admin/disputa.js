// ── Administração de Disputas — Detalhe + Resolução ─────────────────────────

const root = () => document.getElementById('admin-root');
const disputaId = new URLSearchParams(location.search).get('id');

const STATUS_LABEL = {
    Aberta: 'Aberta',
    EmAnalise: 'Em análise',
    ResolvidaAFavorContratante: 'Resolvida — a favor do contratante',
    ResolvidaAFavorPrestador: 'Resolvida — a favor do prestador',
    Encerrada: 'Encerrada'
};

const ACAO_LABEL = {
    EntregaCriada: 'Entrega registrada',
    AnexoAdicionado: 'Anexo adicionado',
    LinkAdicionado: 'Link adicionado',
    EntregaAprovada: 'Entrega aprovada',
    EntregaRejeitada: 'Ajustes solicitados',
    PagamentoLiberado: 'Pagamento liberado',
    FalhaLiberacao: 'Falha na liberação',
    DisputaResolvida: 'Disputa resolvida'
};

function esc(s) {
    if (!s && s !== 0) return '';
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
function moeda(v) { return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(v ?? 0); }
function dataHora(d) {
    if (!d) return '—';
    const iso = d.endsWith('Z') || d.includes('+') ? d : d + 'Z';
    return new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

let _disputa = null;

async function carregar() {
    if (!disputaId) {
        root().innerHTML = '<div class="admin-erro">ID da disputa não informado.</div>';
        return;
    }
    root().innerHTML = '<div class="admin-loading">Carregando disputa...</div>';
    try {
        _disputa = await api.get(`/api/admin/disputas/${disputaId}`);
    } catch (err) {
        root().innerHTML = `<div class="admin-erro">${esc(err?.data?.mensagem || 'Disputa não encontrada.')}</div>`;
        return;
    }
    render();
}

function render() {
    const d = _disputa;
    const podeResolver = d.status === 'Aberta' || d.status === 'EmAnalise';

    const evidencias = (d.evidencias && d.evidencias.length)
        ? `<ul class="admin-evid">${d.evidencias.map(e => `<li>${esc(e)}</li>`).join('')}</ul>`
        : '<p class="admin-muted">Nenhuma evidência anexada.</p>';

    const historico = (d.historicoContrato && d.historicoContrato.length)
        ? d.historicoContrato.map(h => `
            <div class="admin-hist-item">
                <div class="admin-hist-top">
                    <strong>${ACAO_LABEL[h.acao] || h.acao}</strong>
                    <span class="admin-muted">${dataHora(h.dataEvento)}</span>
                </div>
                <p>${esc(h.descricao)}</p>
            </div>`).join('')
        : '<p class="admin-muted">Sem eventos registrados no contrato.</p>';

    root().innerHTML = `
    <div class="admin-wrap">
        <a class="admin-voltar" href="/pages/admin/disputas.html"><i class="fa-solid fa-arrow-left"></i> Voltar para a lista</a>

        <div class="admin-header">
            <h1><i class="fa-solid fa-scale-balanced"></i> Disputa</h1>
            <span class="admin-badge ${d.status.startsWith('Resolvida') ? 'badge-resolvida' : d.status === 'Aberta' ? 'badge-aberta' : 'badge-analise'}">
                ${STATUS_LABEL[d.status] || d.status}
            </span>
        </div>

        <div class="admin-grid">
            <!-- Projeto -->
            <section class="admin-card">
                <h2>Projeto</h2>
                <p><strong>${esc(d.projetoTitulo)}</strong> <span class="admin-id">#${d.projetoId}</span></p>
                <p class="admin-muted">${esc(d.projetoDescricao) || 'Sem descrição.'}</p>
            </section>

            <!-- Partes -->
            <section class="admin-card">
                <h2>Partes</h2>
                <p><i class="fa-solid fa-user-tie"></i> <strong>Contratante:</strong> ${esc(d.contratanteNome)} <span class="admin-id">#${d.contratanteId}</span></p>
                <p><i class="fa-solid fa-user-gear"></i> <strong>Prestador:</strong> ${esc(d.prestadorNome)} <span class="admin-id">#${d.prestadorId}</span></p>
            </section>

            <!-- Valores e estado -->
            <section class="admin-card">
                <h2>Valores e estado</h2>
                <p><strong>Valor negociado:</strong> ${moeda(d.valorNegociado)}</p>
                <p><strong>Status do pagamento:</strong> ${esc(d.statusPagamento)}</p>
                <p><strong>Status do contrato:</strong> ${esc(d.statusContrato || '—')}</p>
            </section>

            <!-- Disputa -->
            <section class="admin-card">
                <h2>Disputa</h2>
                <p><strong>Aberta em:</strong> ${dataHora(d.abertoEm)}</p>
                <p><strong>Aberta por (ID):</strong> ${d.abertoPorId}</p>
                ${d.resolvidaEm ? `<p><strong>Resolvida em:</strong> ${dataHora(d.resolvidaEm)} (admin #${d.resolvidoPorId})</p>` : ''}
            </section>
        </div>

        <!-- Motivo / descrição -->
        <section class="admin-card">
            <h2>Motivo / Descrição detalhada</h2>
            <p>${esc(d.descricao) || esc(d.motivo)}</p>
        </section>

        <!-- Evidências -->
        <section class="admin-card">
            <h2>Evidências anexadas</h2>
            ${evidencias}
        </section>

        <!-- Histórico do contrato -->
        <section class="admin-card">
            <h2>Histórico do contrato</h2>
            <div class="admin-hist">${historico}</div>
        </section>

        ${d.notaResolucao ? `
        <section class="admin-card admin-card--resolvida">
            <h2>Decisão registrada</h2>
            <p>${esc(d.notaResolucao)}</p>
        </section>` : ''}

        <!-- Resolução -->
        ${podeResolver ? `
        <section class="admin-card admin-card--acao">
            <h2>Resolver disputa</h2>
            <p class="admin-muted">A decisão é definitiva e não pode ser alterada. Informe a justificativa (mínimo 10 caracteres).</p>
            <textarea id="nota-resolucao" rows="4" maxlength="1000" placeholder="Justificativa da decisão..."></textarea>
            <p id="resolver-erro" class="admin-erro" hidden></p>
            <div class="admin-acoes">
                <button id="btn-favor-contratante" class="admin-btn admin-btn--danger">
                    <i class="fa-solid fa-rotate-left"></i> A favor do contratante (estornar + cancelar contrato)
                </button>
                <button id="btn-favor-prestador" class="admin-btn admin-btn--success">
                    <i class="fa-solid fa-check"></i> A favor do prestador (liberar + concluir contrato)
                </button>
            </div>
        </section>` : `
        <section class="admin-card">
            <p class="admin-muted"><i class="fa-solid fa-lock"></i> Esta disputa já foi resolvida e não pode ser alterada.</p>
        </section>`}
    </div>`;

    if (podeResolver) {
        document.getElementById('btn-favor-contratante').addEventListener('click', () => resolver(true));
        document.getElementById('btn-favor-prestador').addEventListener('click', () => resolver(false));
    }
}

async function resolver(favorContratante) {
    const erro = document.getElementById('resolver-erro');
    erro.hidden = true;
    const nota = document.getElementById('nota-resolucao').value.trim();

    if (nota.length < 10) {
        erro.textContent = 'Informe a justificativa da decisão (mínimo 10 caracteres).';
        erro.hidden = false;
        return;
    }

    const parte = favorContratante ? 'CONTRATANTE (estorno + contrato cancelado)' : 'PRESTADOR (liberação + contrato concluído)';
    if (!confirm(`Confirmar resolução a favor do ${parte}?\n\nEsta ação é DEFINITIVA e não pode ser desfeita.`)) return;

    const btnC = document.getElementById('btn-favor-contratante');
    const btnP = document.getElementById('btn-favor-prestador');
    btnC.disabled = true; btnP.disabled = true;

    try {
        await api.post(`/api/admin/disputas/${disputaId}/resolver`, {
            favorContratante,
            notaResolucao: nota
        });
        alert('Disputa resolvida com sucesso.');
        carregar();
    } catch (err) {
        erro.textContent = err?.data?.mensagem || 'Erro ao resolver a disputa.';
        erro.hidden = false;
        btnC.disabled = false; btnP.disabled = false;
    }
}

carregar();
