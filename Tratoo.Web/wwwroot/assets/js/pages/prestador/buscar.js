// ── Buscar Profissionais — busca semântica com IA ────────────────────────────

const root = () => document.getElementById('busca-root');

const estado = {
    q: '',
    categoria: '',
    apenasVerificados: false,
    avaliacaoMin: '',
    page: 1,
    pageSize: 12
};

// ── Helpers ──────────────────────────────────────────────────────────────────

function esc(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;').replace(/</g, '&lt;')
        .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function moeda(v) {
    if (!v && v !== 0) return null;
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(v);
}

function estrelas(nota) {
    const n = Math.round(nota || 0);
    return [1,2,3,4,5].map(i =>
        `<span class="bp-star${i <= n ? ' bp-star--on' : ''}">&#9733;</span>`
    ).join('');
}

// ── Render página ────────────────────────────────────────────────────────────

function renderPagina() {
    root().innerHTML = `
    <section class="bp-hero">
        <div class="bp-hero__inner">
            <h1 class="bp-hero__title">Encontre o profissional ideal</h1>
            <p class="bp-hero__sub">Descreva o que voc&ecirc; precisa e nossa IA encontra os melhores perfis</p>
            <div class="bp-search">
                <div class="bp-search__icon">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
                </div>
                <input id="inp-q" type="text" class="bp-search__input"
                    placeholder="Descreva o que voc\u00EA precisa..."
                    value="${esc(estado.q)}" autocomplete="off">
                <button id="btn-buscar" class="bp-search__btn">Buscar</button>
            </div>
            <p class="bp-hero__examples">Ex: &ldquo;designer para criar identidade visual&rdquo; &middot; &ldquo;social media para gerenciar meu Instagram&rdquo; &middot; &ldquo;editor de vídeo para o YouTube&rdquo;</p>
        </div>
    </section>

    <div class="bp-layout">
        <aside class="bp-filters">
            <h3 class="bp-filters__title">Filtros</h3>

            <div class="bp-fg">
                <label>Categoria</label>
                <select id="fil-cat">
                    <option value="">Todas</option>
                    <option value="TI">Desenvolvimento de Software</option>
                    <option value="Design">Design & UX/UI</option>
                    <option value="Marketing">Marketing Digital</option>
                    <option value="Redacao">Reda\u00E7\u00E3o & Conte\u00FAdo</option>
                    <option value="Video">Edi\u00E7\u00E3o de V\u00EDdeo</option>
                    <option value="Dados">Dados & BI</option>
                    <option value="Traducao">Tradu\u00E7\u00E3o</option>
                    <option value="Suporte">Suporte & Assist\u00EAncia Virtual</option>
                    <option value="Consultoria">Consultoria</option>
                    <option value="Juridico">Jur\u00EDdico</option>
                    <option value="Outros">Outros</option>
                </select>
            </div>

            <div class="bp-fg">
                <label>Avalia\u00E7\u00E3o m\u00EDnima</label>
                <select id="fil-aval">
                    <option value="">Qualquer</option>
                    <option value="3">3+ estrelas</option>
                    <option value="4">4+ estrelas</option>
                    <option value="4.5">4.5+ estrelas</option>
                </select>
            </div>

            <div class="bp-fg bp-fg--check">
                <label>
                    <input type="checkbox" id="fil-verif">
                    <span>Apenas verificados</span>
                </label>
            </div>

            <button class="bp-filters__clear" id="btn-limpar">Limpar filtros</button>
        </aside>

        <main class="bp-results">
            <div class="bp-results__bar">
                <p id="txt-total" class="bp-results__count"></p>
            </div>
            <div id="lista" class="bp-grid"></div>
            <div id="paginacao" class="bp-pag"></div>
        </main>
    </div>`;

    // Restaurar filtros
    document.getElementById('fil-cat').value = estado.categoria;
    document.getElementById('fil-aval').value = estado.avaliacaoMin;
    document.getElementById('fil-verif').checked = estado.apenasVerificados;

    // Eventos
    document.getElementById('btn-buscar').addEventListener('click', executarBusca);
    document.getElementById('inp-q').addEventListener('keydown', e => {
        if (e.key === 'Enter') executarBusca();
    });
    ['fil-cat', 'fil-aval'].forEach(id => {
        document.getElementById(id).addEventListener('change', aplicarFiltros);
    });
    document.getElementById('fil-verif').addEventListener('change', aplicarFiltros);
    document.getElementById('btn-limpar').addEventListener('click', limparFiltros);

    buscar();
}

function executarBusca() {
    estado.q = document.getElementById('inp-q').value.trim();
    estado.page = 1;
    buscar();
}

function aplicarFiltros() {
    estado.categoria = document.getElementById('fil-cat').value;
    estado.avaliacaoMin = document.getElementById('fil-aval').value;
    estado.apenasVerificados = document.getElementById('fil-verif').checked;
    estado.page = 1;
    buscar();
}

function limparFiltros() {
    estado.q = '';
    estado.categoria = '';
    estado.avaliacaoMin = '';
    estado.apenasVerificados = false;
    estado.page = 1;
    renderPagina();
}

// ── API call ─────────────────────────────────────────────────────────────────

async function buscar() {
    const lista = document.getElementById('lista');
    if (!lista) return;
    lista.innerHTML = '<div class="bp-loading"><div class="bp-spinner"></div><p>Buscando profissionais...</p></div>';

    const p = new URLSearchParams();
    if (estado.q) p.set('q', estado.q);
    if (estado.categoria) p.set('categoria', estado.categoria);
    if (estado.apenasVerificados) p.set('apenasVerificados', 'true');
    if (estado.avaliacaoMin) p.set('avaliacaoMin', estado.avaliacaoMin);
    p.set('page', estado.page);
    p.set('pageSize', estado.pageSize);

    let dados;
    try {
        dados = await api.get(`/api/busca/prestadores?${p}`);
    } catch {
        lista.innerHTML = '<div class="bp-empty bp-empty--erro">Erro ao buscar. Tente novamente.</div>';
        return;
    }

    const arr = Array.isArray(dados) ? dados : [];
    const total = document.getElementById('txt-total');
    if (total) total.textContent = `${arr.length} profissiona${arr.length !== 1 ? 'is' : 'l'} encontrado${arr.length !== 1 ? 's' : ''}`;

    if (!arr.length) {
        lista.innerHTML = `
            <div class="bp-empty">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#94A3B8" stroke-width="1.5"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
                <h3>Nenhum profissional encontrado</h3>
                <p>Tente ajustar sua busca ou os filtros aplicados.</p>
            </div>`;
        document.getElementById('paginacao').innerHTML = '';
        return;
    }

    lista.innerHTML = arr.map(p => renderCard(p, estado.q)).join('');

    // Click handlers
    lista.querySelectorAll('.bp-card').forEach(card => {
        card.addEventListener('click', () => {
            location.href = `/pages/prestador/perfil.html?id=${card.dataset.id}`;
        });
    });

    // Pagination
    renderPag(arr.length);
}

// ── Card do prestador ────────────────────────────────────────────────────────

function renderCard(p, query) {
    const foto = p.fotoUrl
        ? `<img src="${esc(p.fotoUrl)}" alt="${esc(p.nome)}" class="bp-card__foto">`
        : `<div class="bp-card__foto bp-card__foto--placeholder">${(p.nome || '?')[0].toUpperCase()}</div>`;

    const verificado = p.nivelVerificacao >= 2
        ? '<span class="bp-badge bp-badge--verif" title="Perfil verificado">&#10003; Verificado</span>'
        : '';

    const rating = p.mediaAvaliacoes > 0
        ? `<div class="bp-card__rating">
                ${estrelas(p.mediaAvaliacoes)}
                <span class="bp-card__rating-num">${Number(p.mediaAvaliacoes).toFixed(1)}</span>
                <span class="bp-card__rating-cnt">(${p.totalAvaliacoes})</span>
           </div>`
        : '<span class="bp-card__novo">Novo na plataforma</span>';

    // Highlight skills matching query
    const queryTerms = (query || '').toLowerCase().split(/\s+/).filter(Boolean);
    const skills = (p.competencias || []).slice(0, 5).map(s => {
        const isMatch = queryTerms.some(t => s.toLowerCase().includes(t));
        return `<span class="bp-skill${isMatch ? ' bp-skill--match' : ''}">${esc(s)}</span>`;
    }).join('');

    const bio = p.bio ? `<p class="bp-card__bio">${esc(p.bio)}</p>` : '';

    return `
    <article class="bp-card" data-id="${p.id}" role="button" tabindex="0" aria-label="Ver perfil de ${esc(p.nome)}">
        <div class="bp-card__top">
            ${foto}
            <div class="bp-card__info">
                <h3 class="bp-card__nome">${esc(p.nome)}</h3>
                <p class="bp-card__titulo">${esc(p.tituloProfissional || '')}</p>
                ${verificado}
            </div>
        </div>
        ${rating}
        ${bio}
        ${skills ? `<div class="bp-card__skills">${skills}</div>` : ''}
        <div class="bp-card__footer">
            ${p.contratosEncerrados > 0 ? `<span class="bp-card__projetos">${p.contratosEncerrados} projeto${p.contratosEncerrados !== 1 ? 's' : ''}</span>` : ''}
        </div>
    </article>`;
}

// ── Pagination ───────────────────────────────────────────────────────────────

function renderPag(count) {
    const pag = document.getElementById('paginacao');
    if (!pag) return;

    // Simple: if we got a full page, show next
    const hasMore = count >= estado.pageSize;
    const hasPrev = estado.page > 1;

    if (!hasMore && !hasPrev) { pag.innerHTML = ''; return; }

    pag.innerHTML = `
        <button class="bp-pag__btn" ${!hasPrev ? 'disabled' : ''} data-dir="-1"><i class="fa-solid fa-arrow-left"></i> Anterior</button>
        <span class="bp-pag__num">P\u00E1gina ${estado.page}</span>
        <button class="bp-pag__btn" ${!hasMore ? 'disabled' : ''} data-dir="1">Pr\u00F3xima <i class="fa-solid fa-arrow-right"></i></button>
    `;

    pag.querySelectorAll('button[data-dir]').forEach(btn => {
        btn.addEventListener('click', () => {
            estado.page += parseInt(btn.dataset.dir);
            buscar();
            window.scrollTo({ top: 0, behavior: 'smooth' });
        });
    });
}

// ── Init ─────────────────────────────────────────────────────────────────────
renderPagina();
