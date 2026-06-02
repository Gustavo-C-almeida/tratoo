// ── Meus projetos (painel do contratante) ────────────────────────────────────

const root = () => document.getElementById('meus-projetos-root');

function escHtml(str) {
    if (!str) return '';
    return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

function formatarMoeda(v) {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(v);
}

function formatarData(d) {
    return new Date(d).toLocaleDateString('pt-BR');
}

const STATUS_LABEL = {
    Rascunho: 'Rascunho',
    Aberto: 'Aberto',
    EmAndamento: 'Em andamento',
    Concluido: 'Concluído',
    Cancelado: 'Cancelado',
    Disputa: 'Disputa',
    Expirado: 'Expirado'
};

const STATUS_CLASS = {
    Rascunho: 'rascunho',
    Aberto: 'aberto',
    EmAndamento: 'emandamento',
    Concluido: 'concluido',
    Cancelado: 'cancelado',
    Disputa: 'cancelado',
    Expirado: 'cancelado'
};

async function carregarProjetos() {
    root().innerHTML = `
    <div class="cp-header">
        <h1>Meus Projetos</h1>
        <p>Gerencie seus projetos publicados e rascunhos.</p>
    </div>
    <div class="cp-container">
        <div class="mp-header">
            <h2 id="txt-total">Carregando...</h2>
            <div id="btn-novo-projeto-container"></div>
        </div>
        <div id="msg-global"></div>
        <div id="lista-projetos">
            <div style="padding:40px;text-align:center;color:#94a3b8">Carregando seus projetos...</div>
        </div>
    </div>`;

    let projetos;
    try {
        projetos = await api.get('/api/me/projects');
    } catch (err) {
        if (err?.status === 401) {
            document.getElementById('lista-projetos').innerHTML =
                '<p style="color:#dc2626;text-align:center">Você precisa estar autenticado para ver seus projetos.</p>';
            return;
        }
        document.getElementById('lista-projetos').innerHTML =
            '<p style="color:#dc2626;text-align:center">Erro ao carregar projetos.</p>';
        return;
    }

    const totalEl = document.getElementById('txt-total');
    if (totalEl) totalEl.textContent = `${projetos.length} projeto${projetos.length !== 1 ? 's' : ''}`;

    const lista = document.getElementById('lista-projetos');
    const btnContainer = document.getElementById('btn-novo-projeto-container');

    if (!projetos.length) {
        // Caso não tenha projetos: mostra apenas "Criar meu primeiro projeto"
        lista.innerHTML = `
            <div style="text-align:center;padding:60px;color:#94a3b8">
                <p style="font-size:1.1rem;margin-bottom:12px">Você ainda não tem projetos.</p>
                <a href="criar-projeto.html" class="btn-novo-projeto">Criar meu primeiro projeto</a>
            </div>`;

        // Garante que o botão "Novo projeto" não seja exibido
        if (btnContainer) {
            btnContainer.innerHTML = '';
        }
        return;
    }

    // Caso tenha projetos: mostra o botão "Novo projeto" no header
    if (btnContainer) {
        btnContainer.innerHTML = '<a href="criar-projeto.html" class="btn-novo-projeto">Novo projeto</a>';
    }

    lista.innerHTML = projetos.map(renderCard).join('');

    // Eventos de ação
    lista.querySelectorAll('.btn-publicar-mini').forEach(btn => {
        btn.addEventListener('click', () => publicarProjeto(btn.dataset.id));
    });
    lista.querySelectorAll('.btn-cancelar-mini').forEach(btn => {
        btn.addEventListener('click', () => cancelarProjeto(btn.dataset.id));
    });
}

function renderCard(p) {
    const statusClass = STATUS_CLASS[p.status] || 'rascunho';
    const statusLabel = STATUS_LABEL[p.status] || p.status;

    const acoes = [];
    if (p.status === 'Rascunho') {
        acoes.push(`<button class="btn-publicar-mini" data-id="${p.id}">Publicar</button>`);
    }
    if (p.status === 'Aberto' || p.status === 'Rascunho') {
        acoes.push(`<button class="btn-cancelar-mini" data-id="${p.id}">Cancelar</button>`);
    }
    if (p.status === 'Aberto' || p.status === 'EmAndamento') {
        acoes.push(`<a class="btn-ver-propostas" href="../projetos/detalhe.html?id=${p.id}">Ver projeto</a>`);
        acoes.push(`<a class="btn-ver-propostas" href="propostas-projeto.html?id=${p.id}">Propostas (${p.totalPropostas})</a>`);
    }

    return `
    <div class="mp-card" id="card-${p.id}">
        <div class="mp-card-info">
            <h3>${escHtml(p.titulo)}</h3>
            <div class="mp-meta">
                <span>${formatarMoeda(p.orcamentoMin)} – ${formatarMoeda(p.orcamentoMax)}</span>
                <span>Prazo: ${formatarData(p.prazoEntrega)}</span>
                <span>${p.totalPropostas} proposta${p.totalPropostas !== 1 ? 's' : ''}</span>
                <span>Criado em ${formatarData(p.publicadoEm || p.criadoEm)}</span>
            </div>
        </div>
        <div style="display:flex;align-items:center;gap:12px;flex-shrink:0;">
            <span class="mp-status ${statusClass}">${escHtml(statusLabel)}</span>
            <div class="mp-actions">${acoes.join('')}</div>
        </div>
    </div>`;
}

async function publicarProjeto(id) {
    const msgEl = document.getElementById('msg-global');
    msgEl.innerHTML = '';
    try {
        await api.post(`/api/projects/${id}/publish`, {});
        msgEl.innerHTML = `<div class="msg-feedback sucesso">Projeto publicado com sucesso!</div>`;
        carregarProjetos();
    } catch (err) {
        const texto = err?.data?.mensagem || 'Erro ao publicar projeto.';
        msgEl.innerHTML = `<div class="msg-feedback erro">${escHtml(texto)}</div>`;
    }
}

async function cancelarProjeto(id) {
    if (!confirm('Tem certeza que deseja cancelar este projeto?')) return;
    const msgEl = document.getElementById('msg-global');
    msgEl.innerHTML = '';
    try {
        await api.delete(`/api/projects/${id}`);
        msgEl.innerHTML = `<div class="msg-feedback sucesso">Projeto cancelado.</div>`;
        carregarProjetos();
    } catch (err) {
        const texto = err?.data?.mensagem || 'Erro ao cancelar projeto.';
        msgEl.innerHTML = `<div class="msg-feedback erro">${escHtml(texto)}</div>`;
    }
}

// Inicia
carregarProjetos();