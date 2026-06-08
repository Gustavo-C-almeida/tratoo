// ── Listagem pública de projetos (US-05) — com busca semântica IA ────────────

const root = () => document.getElementById('projetos-root');

// Estado atual da busca
const estado = {
    q: '',
    categoria: '',
    orcamentoMin: '',
    orcamentoMax: '',
    nivelFreelancer: '',
    prazoAte: '',
    orderBy: 'recentes',
    page: 1,
    pageSize: 20,
    usandoIA: false,        // indica se a busca atual usou o endpoint semântico
    modoRecomendado: false  // feed personalizado do prestador (sem query/filtros)
};

// Usuário logado (null = anônimo). Carregado uma vez no init.
let usuarioAtual = null;

function ehPrestadorLogado() {
    return (usuarioAtual?.tipo || '').toLowerCase() === 'prestador';
}

// Sem nenhum filtro aplicado -> elegível para feed de recomendações
function semFiltrosAplicados() {
    return !estado.categoria && !estado.orcamentoMin && !estado.orcamentoMax
        && !estado.nivelFreelancer && !estado.prazoAte;
}

function escHtml(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

function formatarMoeda(v) {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(v);
}

function formatarData(d) {
    return new Date(d).toLocaleDateString('pt-BR');
}

function labelCategoria(c) {
    const map = {
        TI: 'Desenvolvimento de Software', Design: 'Design & UX/UI',
        Marketing: 'Marketing Digital', Redacao: 'Redação & Conteúdo',
        Video: 'Edição de Vídeo', Dados: 'Dados & BI', Traducao: 'Tradução',
        Suporte: 'Suporte & Assistência Virtual', Consultoria: 'Consultoria',
        Juridico: 'Jurídico', Outros: 'Outros'
    };
    return map[c] || c;
}

function labelNivel(n) {
    if (!n) return null;
    const map = { Junior: 'Júnior', Pleno: 'Pleno', Senior: 'Sênior' };
    return map[n] || n;
}

function tempoAtras(d) {
    if (!d) return null;
    const ms = Date.now() - new Date(d).getTime();
    const min = Math.floor(ms / 60000);
    if (min < 60) return `${Math.max(min, 1)} min atrás`;
    const h = Math.floor(min / 60);
    if (h < 24) return `${h}h atrás`;
    const dias = Math.floor(h / 24);
    if (dias < 30) return `${dias} dia${dias !== 1 ? 's' : ''} atrás`;
    const meses = Math.floor(dias / 30);
    return `${meses} ${meses !== 1 ? 'meses' : 'mês'} atrás`;
}

// ── Render da página completa ─────────────────────────────────────────────────
function renderPagina() {
    root().innerHTML = `
    <div class="projetos-header">
        <h1>Projetos Dispon&iacute;veis</h1>
        <p>Descreva o que voc&ecirc; sabe fazer e nossa IA encontra os projetos ideais</p>
        <div class="busca-bar">
            <input id="inp-busca" type="text" placeholder="Ex: &quot;design para e-commerce&quot;" value="${escHtml(estado.q)}">
            <button id="btn-buscar">Buscar com IA</button>
        </div>
    </div>

    <div class="projetos-layout">
        <!-- Sidebar de filtros -->
        <aside class="filtros-sidebar">
            <h3>Filtros</h3>

            <div class="filtro-grupo">
                <label>Categoria</label>
                <select id="fil-categoria">
                    <option value="">Todas</option>
                    <option value="TI">Desenvolvimento de Software</option>
                    <option value="Design">Design & UX/UI</option>
                    <option value="Marketing">Marketing Digital</option>
                    <option value="Redacao">Redação & Conteúdo</option>
                    <option value="Video">Edição de Vídeo</option>
                    <option value="Dados">Dados & BI</option>
                    <option value="Traducao">Tradução</option>
                    <option value="Suporte">Suporte & Assistência Virtual</option>
                    <option value="Consultoria">Consultoria</option>
                    <option value="Juridico">Jurídico</option>
                    <option value="Outros">Outros</option>
                </select>
            </div>

            <div class="filtro-grupo">
                <label>Orçamento mínimo (R$)</label>
                <input id="fil-orc-min" type="number" min="0" placeholder="ex: 500">
            </div>

            <div class="filtro-grupo">
                <label>Orçamento máximo (R$)</label>
                <input id="fil-orc-max" type="number" min="0" placeholder="ex: 5000">
            </div>

            <div class="filtro-grupo">
                <label>Nível desejado</label>
                <select id="fil-nivel">
                    <option value="">Qualquer</option>
                    <option value="Junior">Júnior</option>
                    <option value="Pleno">Pleno</option>
                    <option value="Senior">Sênior</option>
                </select>
            </div>

            <div class="filtro-grupo">
                <label>Prazo até</label>
                <input id="fil-prazo" type="date">
            </div>

            <button class="btn-limpar" id="btn-limpar">Limpar filtros</button>
        </aside>

        <!-- Resultados -->
        <div class="projetos-resultados">
            <div class="resultados-topo">
                <p id="txt-total">Carregando...</p>
                <select id="sel-order">
                    <option value="recentes">Mais recentes</option>
                    <option value="relevancia">Mais relevantes</option>
                    <option value="menorOrcamento">Menor orçamento</option>
                    <option value="maiorOrcamento">Maior orçamento</option>
                </select>
            </div>
            <div id="lista-projetos"></div>
            <div id="paginacao" class="paginacao"></div>
        </div>
    </div>`;

    // Restaurar valores dos filtros
    document.getElementById('fil-categoria').value = estado.categoria;
    document.getElementById('fil-orc-min').value = estado.orcamentoMin;
    document.getElementById('fil-orc-max').value = estado.orcamentoMax;
    document.getElementById('fil-nivel').value = estado.nivelFreelancer;
    document.getElementById('fil-prazo').value = estado.prazoAte;
    document.getElementById('sel-order').value = estado.orderBy;

    // Eventos
    document.getElementById('btn-buscar').addEventListener('click', () => {
        estado.q = document.getElementById('inp-busca').value.trim();
        estado.page = 1;
        buscarProjetos();
    });
    document.getElementById('inp-busca').addEventListener('keydown', e => {
        if (e.key === 'Enter') document.getElementById('btn-buscar').click();
    });
    document.getElementById('sel-order').addEventListener('change', e => {
        estado.orderBy = e.target.value;
        estado.page = 1;
        buscarProjetos();
    });

    ['fil-categoria', 'fil-orc-min', 'fil-orc-max', 'fil-nivel', 'fil-prazo'].forEach(id => {
        document.getElementById(id).addEventListener('change', aplicarFiltros);
    });

    document.getElementById('btn-limpar').addEventListener('click', () => {
        estado.q = '';
        estado.categoria = '';
        estado.orcamentoMin = '';
        estado.orcamentoMax = '';
        estado.nivelFreelancer = '';
        estado.prazoAte = '';
        estado.page = 1;
        renderPagina();
        buscarProjetos();
    });

    buscarProjetos();
}

function aplicarFiltros() {
    estado.categoria      = document.getElementById('fil-categoria').value;
    estado.orcamentoMin   = document.getElementById('fil-orc-min').value;
    estado.orcamentoMax   = document.getElementById('fil-orc-max').value;
    estado.nivelFreelancer = document.getElementById('fil-nivel').value;
    estado.prazoAte       = document.getElementById('fil-prazo').value;
    estado.page = 1;
    buscarProjetos();
}

async function buscarProjetos() {
    const lista = document.getElementById('lista-projetos');
    if (!lista) return;

    // Feed personalizado: prestador logado, sem termo de busca e sem filtros.
    const usarRecomendados = !estado.q && ehPrestadorLogado() && semFiltrosAplicados();

    lista.innerHTML = `<div style="padding:40px;text-align:center;color:#94a3b8">
        <div class="loading-spinner" style="margin:0 auto 12px"></div>
        <p>${estado.q ? 'Buscando com IA...' : usarRecomendados ? 'Selecionando oportunidades para você...' : 'Carregando projetos...'}</p>
    </div>`;

    // Decide qual endpoint usar: semântico (com query), recomendado (prestador) ou tradicional
    const usarBuscaIA = !!estado.q;
    estado.usandoIA = usarBuscaIA || usarRecomendados;
    estado.modoRecomendado = usarRecomendados;

    let itens = [];
    let total = 0;
    let pagAtual = estado.page;
    let totalPaginas = 1;

    try {
        if (usarRecomendados) {
            // Feed personalizado com base no perfil do prestador — retorna array plano (até 50)
            const arr = await api.get('/api/busca/projetos/recomendados');
            itens = Array.isArray(arr) ? arr : [];
            total = itens.length;
            totalPaginas = 1;   // endpoint não pagina
        } else if (usarBuscaIA) {
            // Busca semântica — retorna array plano
            const params = new URLSearchParams();
            params.set('q', estado.q);
            params.set('page', estado.page);
            params.set('pageSize', estado.pageSize);

            const arr = await api.get(`/api/busca/projetos?${params}`);
            itens = Array.isArray(arr) ? arr : [];
            total = itens.length;
            // Pagination simples: se recebeu pageSize resultados, pode haver mais
            totalPaginas = itens.length >= estado.pageSize ? pagAtual + 1 : pagAtual;
        } else {
            // Busca tradicional com filtros — retorna { total, itens, pagina, totalPaginas }
            const params = new URLSearchParams();
            if (estado.categoria)     params.set('categoria', estado.categoria);
            if (estado.orcamentoMin)  params.set('orcamentoMin', estado.orcamentoMin);
            if (estado.orcamentoMax)  params.set('orcamentoMax', estado.orcamentoMax);
            if (estado.nivelFreelancer) params.set('nivelFreelancer', estado.nivelFreelancer);
            if (estado.prazoAte)      params.set('prazoAte', estado.prazoAte);
            params.set('orderBy', estado.orderBy);
            params.set('page', estado.page);

            const dados = await api.get(`/api/projects?${params}`);
            itens = dados.itens || [];
            total = dados.total || 0;
            pagAtual = dados.pagina || 1;
            totalPaginas = dados.totalPaginas || 1;
        }
    } catch {
        lista.innerHTML = '<div class="msg-centro erro">Erro ao carregar projetos. Tente novamente.</div>';
        return;
    }

    const totalEl = document.getElementById('txt-total');
    if (totalEl) {
        if (estado.modoRecomendado) {
            totalEl.innerHTML = `<span class="ia-badge">IA</span> ${total} oportunidade${total !== 1 ? 's' : ''} recomendada${total !== 1 ? 's' : ''} para voc&ecirc;`;
        } else {
            const badge = usarBuscaIA ? '<span class="ia-badge">IA</span> ' : '';
            totalEl.innerHTML = `${badge}${total} projeto${total !== 1 ? 's' : ''} encontrado${total !== 1 ? 's' : ''}`;
        }
    }

    if (!itens.length) {
        // No modo recomendado, perfil incompleto/sem indexação pode não gerar matches.
        const msg = estado.modoRecomendado
            ? `<p style="font-size:1.1rem;margin-bottom:8px;font-weight:600;color:#1E293B">Ainda não temos recomendações para você</p>
               <p>Complete seu perfil e adicione competências para recebermos oportunidades sob medida — ou use a busca acima.</p>`
            : `<p style="font-size:1.1rem;margin-bottom:8px;font-weight:600;color:#1E293B">Nenhum projeto encontrado</p>
               <p>Tente descrever de outra forma ou ajustar os filtros.</p>`;
        lista.innerHTML = `
            <div class="msg-centro">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#94A3B8" stroke-width="1.5" style="margin-bottom:12px"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
                ${msg}
            </div>`;
        document.getElementById('paginacao').innerHTML = '';
        return;
    }

    // Banner do feed personalizado
    const banner = estado.modoRecomendado
        ? `<div class="feed-recomendado-banner">
               <strong>Feed personalizado</strong> — projetos ordenados pela compatibilidade com o seu perfil e competências.
           </div>`
        : '';

    lista.innerHTML = banner + itens.map(p => renderCard(p, usarBuscaIA || estado.modoRecomendado)).join('');
    renderPaginacao(pagAtual, totalPaginas);

    // Navegação ao clicar num card
    lista.querySelectorAll('.projeto-card').forEach(card => {
        card.addEventListener('click', () => {
            const id = card.dataset.id;
            window.location.href = `detalhe.html?id=${id}`;
        });
    });
}

function renderCard(p, fromIA) {
    // Query terms para highlight de habilidades
    const queryTerms = (estado.q || '').toLowerCase().split(/\s+/).filter(Boolean);

    const habs = p.habilidades || [];
    const tags = habs.slice(0, 6).map(h => {
        const isMatch = fromIA && queryTerms.some(t => h.toLowerCase().includes(t));
        return `<span class="tag habilidade${isMatch ? ' tag--match' : ''}">${escHtml(h)}</span>`;
    }).join('');

    const nivelTag = p.nivelFreelancer
        ? `<span class="tag nivel">${escHtml(labelNivel(p.nivelFreelancer))}</span>` : '';

    const badgeNovo = p.contratanteNovo
        ? `<span class="badge-novo">Novo</span>` : '';

    // Descrição: semântico usa 'descricao', tradicional usa 'descricaoResumida'
    const desc = p.descricaoResumida || p.descricao || '';

    // Similaridade badge (apenas busca IA)
    const simBadge = fromIA && p.similaridade > 0
        ? `<span class="tag tag--sim">${Math.round(p.similaridade * 100)}% match</span>` : '';

    return `
    <article class="projeto-card" data-id="${p.id}" role="button" tabindex="0"
        aria-label="Ver projeto: ${escHtml(p.titulo)}">
        <div class="projeto-card-topo">
            <h3>${escHtml(p.titulo)}</h3>
            <span class="projeto-orcamento">
                ${formatarMoeda(p.orcamentoMin)} &ndash; ${formatarMoeda(p.orcamentoMax)}
            </span>
        </div>
        <p class="projeto-descricao">${escHtml(desc)}</p>
        <div class="projeto-tags">
            <span class="tag categoria">${escHtml(labelCategoria(p.categoria))}</span>
            ${nivelTag}
            ${simBadge}
            ${tags}
        </div>
        <div class="projeto-footer">
            <span class="contratante-info">
                ${badgeNovo}
                ${p.contratanteNome
                    ? (p.contratanteId
                        ? `<a href="/pages/contratante/perfil.html?id=${p.contratanteId}" class="link-perfil-contratante">${escHtml(p.contratanteNome)}</a>`
                        : escHtml(p.contratanteNome))
                    : ''}
            </span>
            <span>Prazo: ${formatarData(p.prazoEntrega)}</span>
            <span class="proposta-count">${p.totalPropostas} proposta${p.totalPropostas !== 1 ? 's' : ''}</span>
            ${p.publicadoEm ? `<span class="tempo-publicado">${tempoAtras(p.publicadoEm) || formatarData(p.publicadoEm)}</span>` : ''}
        </div>
    </article>`;
}

function renderPaginacao(pagAtual, totalPaginas) {
    const pag = document.getElementById('paginacao');
    if (!pag || totalPaginas <= 1) { if (pag) pag.innerHTML = ''; return; }

    const btns = [];

    btns.push(`<button ${pagAtual <= 1 ? 'disabled' : ''} data-page="${pagAtual - 1}">&#8592; Anterior</button>`);

    const inicio = Math.max(1, pagAtual - 2);
    const fim    = Math.min(totalPaginas, pagAtual + 2);

    if (inicio > 1) btns.push(`<button data-page="1">1</button>`);
    if (inicio > 2) btns.push(`<span style="padding:8px 4px;color:#94a3b8">…</span>`);

    for (let i = inicio; i <= fim; i++) {
        btns.push(`<button class="${i === pagAtual ? 'ativo' : ''}" data-page="${i}">${i}</button>`);
    }

    if (fim < totalPaginas - 1) btns.push(`<span style="padding:8px 4px;color:#94a3b8">…</span>`);
    if (fim < totalPaginas) btns.push(`<button data-page="${totalPaginas}">${totalPaginas}</button>`);

    btns.push(`<button ${pagAtual >= totalPaginas ? 'disabled' : ''} data-page="${pagAtual + 1}">Próxima &#8594;</button>`);

    pag.innerHTML = btns.join('');

    pag.querySelectorAll('button[data-page]').forEach(btn => {
        btn.addEventListener('click', () => {
            estado.page = parseInt(btn.dataset.page);
            buscarProjetos();
            window.scrollTo({ top: 0, behavior: 'smooth' });
        });
    });
}

// Inicia — detecta o usuário logado para decidir entre feed personalizado e listagem.
// auth-guard.js já chamou GET /api/me e populou window.__tratooUser; reaproveitamos
// esse valor para evitar uma 2ª chamada idêntica ao endpoint.
async function obterUsuarioLogado() {
    if (window.__tratooUser) return window.__tratooUser;
    // Aguarda o guard resolver (até ~3s) antes de desistir
    for (let i = 0; i < 30 && !window.__tratooUser; i++) {
        await new Promise(r => setTimeout(r, 100));
    }
    return window.__tratooUser || null;
}

(async function init() {
    usuarioAtual = await obterUsuarioLogado();
    renderPagina();
})();
