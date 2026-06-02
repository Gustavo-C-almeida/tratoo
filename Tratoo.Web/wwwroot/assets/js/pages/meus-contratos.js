// Página: /pages/me/contratos.html
// Lista todos os contratos do usuário logado (contratante ou prestador).

const root = document.getElementById('contratos-root');

const STATUS_LABEL = {
    Gerado: 'Aguardando assinaturas',
    AguardandoAssinatura: 'Aguardando outra parte',
    Ativo: 'Em vigor',
    Encerrado: 'Encerrado',
    Cancelado: 'Cancelado / Expirado'
};

const STATUS_CLASS = {
    Gerado: 'badge-gerado',
    AguardandoAssinatura: 'badge-aguardando',
    Ativo: 'badge-ativo',
    Encerrado: 'badge-encerrado',
    Cancelado: 'badge-cancelado'
};

async function init() {
    root.innerHTML = '<p class="loading-msg">Carregando contratos...</p>';

    try {
        const contratos = await api.get('/api/me/contratos');
        renderLista(contratos);
    } catch (err) {
        if (err?.status === 401) {
            location.href = '/pages/auth/login.html';
        } else {
            root.innerHTML = `<p class="erro-msg">${err?.data?.mensagem || 'Erro ao carregar contratos.'}</p>`;
        }
    }
}

function renderLista(contratos) {
    if (!contratos.length) {
        root.innerHTML = `
            <div class="contratos-wrapper">
                <h1>Meus Contratos</h1>
                <p class="vazio-msg">Nenhum contrato encontrado. Contratos são gerados automaticamente ao aceitar uma proposta.</p>
            </div>`;
        return;
    }

    root.innerHTML = `
        <div class="contratos-wrapper">
            <h1>Meus Contratos</h1>
            <div class="contratos-lista">
                ${contratos.map(renderCard).join('')}
            </div>
        </div>`;
}

function renderCard(c) {
    const valorFormatado = c.valorTotal
        ? c.valorTotal.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })
        : '—';

    const dataTermino = c.dataTermino && c.dataTermino !== '0001-01-01T00:00:00'
        ? new Date(c.dataTermino).toLocaleDateString('pt-BR')
        : '—';

    const expiraEm = new Date(c.expiraEm).toLocaleDateString('pt-BR');

    const acoesPendentes = [];
    if (!c.assinadoPorMim && (c.status === 'Gerado' || c.status === 'AguardandoAssinatura')) {
        acoesPendentes.push(`<span class="pendencia-badge">⚡ Assinatura pendente</span>`);
    }

    return `
        <div class="contrato-card">
            <div class="contrato-card-header">
                <div>
                    <h2 class="projeto-titulo">${escapeHtml(c.projetoTitulo)}</h2>
                    <p class="partes-resumo">
                        ${c.contratanteId
                            ? `<a href="/pages/contratante/perfil.html?id=${c.contratanteId}" class="link-perfil-contratante">${escapeHtml(c.contratanteNome)}</a>`
                            : escapeHtml(c.contratanteNome)
                        } ×
                        ${c.prestadorId
                            ? `<a href="/pages/prestador/perfil.html?id=${c.prestadorId}" class="link-perfil-prestador">${escapeHtml(c.prestadorNome)}</a>`
                            : escapeHtml(c.prestadorNome)
                        }
                    </p>
                </div>
                <span class="status-badge ${STATUS_CLASS[c.status] || ''}">${STATUS_LABEL[c.status] || c.status}</span>
            </div>

            <div class="contrato-card-info">
                <span>Valor: <strong>${valorFormatado}</strong></span>
                <span>Término: ${dataTermino}</span>
                <span>Prazo assinatura: ${expiraEm}</span>
            </div>

            <div class="contrato-card-assinaturas">
                <span class="${c.assinadoPorMim ? 'assinado' : 'pendente'}">
                    Você: ${c.assinadoPorMim ? '✔ Assinado' : '⏳ Pendente'}
                </span>
                <span class="${c.assinadoPelaOutraParte ? 'assinado' : 'pendente'}">
                    Outra parte: ${c.assinadoPelaOutraParte ? '✔ Assinado' : '⏳ Pendente'}
                </span>
            </div>

            ${acoesPendentes.length ? `<div class="acoes-pendentes">${acoesPendentes.join('')}</div>` : ''}

            <div class="contrato-card-footer">
                <a href="/pages/contrato/detalhe.html?id=${c.id}" class="btn-ver-contrato">Ver contrato</a>
            </div>
        </div>`;
}

function escapeHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

init();
