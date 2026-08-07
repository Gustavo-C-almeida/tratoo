import { api } from '/assets/js/services/api.js';
import { onReady } from '/assets/js/core/app.js';

// ── Buscar Profissionais — busca semântica com IA ────────────────────────────
// Markup construído com componentes do Bootstrap (card, form-control, offcanvas,
// pagination). Os IDs abaixo são o contrato com esta página e não devem mudar:
//   busca-root · inp-q · btn-buscar · fil-cat · fil-aval · fil-verif ·
//   btn-limpar · txt-total · lista · paginacao
// A classe `.bp-card` é mantida como HOOK do handler de clique dos cards.

const root = () => document.getElementById('busca-root');

const estado = {
    q: '',
    categoria: '',
    apenasVerificados: false,
    avaliacaoMin: '',
    page: 1,
    pageSize: 12
};

const CATEGORIAS = [
    ['TI',          'Desenvolvimento de Software'],
    ['Design',      'Design & UX/UI'],
    ['Marketing',   'Marketing Digital'],
    ['Redacao',     'Redação & Conteúdo'],
    ['Video',       'Edição de Vídeo'],
    ['Dados',       'Dados & BI'],
    ['Traducao',    'Tradução'],
    ['Suporte',     'Suporte & Assistência Virtual'],
    ['Consultoria', 'Consultoria'],
    ['Juridico',    'Jurídico'],
    ['Outros',      'Outros']
];

// ── Helpers ──────────────────────────────────────────────────────────────────

function esc(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;').replace(/</g, '&lt;')
        .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function estrelas(nota) {
    const n = Math.round(nota || 0);
    return [1, 2, 3, 4, 5].map(i =>
        `<i class="fa-solid fa-star ${i <= n ? 'text-warning' : 'text-body-tertiary opacity-50'}"></i>`
    ).join('');
}

// ── Render da página ─────────────────────────────────────────────────────────

function renderPagina() {
    const opcoesCategoria = CATEGORIAS
        .map(([v, label]) => `<option value="${v}">${label}</option>`)
        .join('');

    // Os filtros vivem em um offcanvas-lg: viram gaveta no mobile e coluna
    // estática a partir de lg — mesmo markup, sem duplicar os campos.
    const filtros = `
        <div class="offcanvas-lg offcanvas-start" tabindex="-1" id="filtros-offcanvas"
             aria-labelledby="filtros-titulo">
            <div class="offcanvas-header">
                <h2 class="offcanvas-title h6 mb-0" id="filtros-titulo">Filtros</h2>
                <button type="button" class="btn-close" data-bs-dismiss="offcanvas"
                        data-bs-target="#filtros-offcanvas" aria-label="Fechar"></button>
            </div>

            <div class="offcanvas-body d-block">
                <div class="card">
                    <div class="card-body">
                        <h2 class="h6 fw-bold d-none d-lg-block mb-3">Filtros</h2>

                        <div class="mb-3">
                            <label class="form-label" for="fil-cat">Categoria</label>
                            <select class="form-select" id="fil-cat">
                                <option value="">Todas</option>
                                ${opcoesCategoria}
                            </select>
                        </div>

                        <div class="mb-3">
                            <label class="form-label" for="fil-aval">Avaliação mínima</label>
                            <select class="form-select" id="fil-aval">
                                <option value="">Qualquer</option>
                                <option value="3">3+ estrelas</option>
                                <option value="4">4+ estrelas</option>
                                <option value="4.5">4.5+ estrelas</option>
                            </select>
                        </div>

                        <div class="form-check form-switch mb-4">
                            <input class="form-check-input" type="checkbox" role="switch" id="fil-verif">
                            <label class="form-check-label" for="fil-verif">Apenas verificados</label>
                        </div>

                        <button class="btn btn-light w-100" id="btn-limpar" type="button">
                            Limpar filtros
                        </button>
                    </div>
                </div>
            </div>
        </div>`;

    root().innerHTML = `
    <section class="bp-hero">
        <div class="container">
            <div class="text-center mx-auto" style="max-width:680px">
                <h1 class="bp-hero__title">Encontre o profissional ideal</h1>
                <p class="bp-hero__sub">
                    Descreva o que você precisa e nossa IA encontra os melhores perfis
                </p>

                <div class="input-group input-group-lg bp-hero__search">
                    <span class="input-group-text bg-white border-end-0">
                        <i class="fa-solid fa-magnifying-glass text-body-tertiary" aria-hidden="true"></i>
                    </span>
                    <input id="inp-q" type="search"
                           class="form-control border-start-0 ps-0"
                           placeholder="Descreva o que você precisa..."
                           value="${esc(estado.q)}" autocomplete="off"
                           aria-label="Buscar profissionais">
                    <button id="btn-buscar" class="btn btn-primary px-4" type="button">Buscar</button>
                </div>

                <p class="bp-hero__examples">
                    Ex.: “designer para criar identidade visual” · “social media para
                    gerenciar meu Instagram” · “editor de vídeo para o YouTube”
                </p>
            </div>
        </div>
    </section>

    <div class="tratoo-page tratoo-page--wide">
        <div class="row g-4">
            <div class="col-lg-3">${filtros}</div>

            <div class="col-lg-9">
                <div class="d-flex align-items-center justify-content-between mb-3 gap-2">
                    <p id="txt-total" class="text-secondary small mb-0"></p>

                    <button class="btn btn-light btn-sm d-lg-none" type="button"
                            data-bs-toggle="offcanvas" data-bs-target="#filtros-offcanvas"
                            aria-controls="filtros-offcanvas">
                        <i class="fa-solid fa-sliders me-1" aria-hidden="true"></i>Filtros
                    </button>
                </div>

                <div id="lista" class="row g-3"></div>
                <nav id="paginacao" class="mt-4" aria-label="Paginação dos resultados"></nav>
            </div>
        </div>
    </div>`;

    // Restaura o estado dos filtros no markup recém-criado
    document.getElementById('fil-cat').value = estado.categoria;
    document.getElementById('fil-aval').value = estado.avaliacaoMin;
    document.getElementById('fil-verif').checked = estado.apenasVerificados;

    // Eventos
    document.getElementById('btn-buscar').addEventListener('click', executarBusca);
    document.getElementById('inp-q').addEventListener('keydown', e => {
        if (e.key === 'Enter') executarBusca();
    });
    ['fil-cat', 'fil-aval', 'fil-verif'].forEach(id => {
        document.getElementById(id).addEventListener('change', aplicarFiltros);
    });
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

// ── Chamada à API ────────────────────────────────────────────────────────────

async function buscar() {
    const lista = document.getElementById('lista');
    if (!lista) return;

    // Skeletons durante o carregamento — dão noção do layout que vai chegar,
    // em vez de um spinner solto que "pula" quando os cards aparecem.
    lista.innerHTML = Array.from({ length: 6 }).map(() => `
        <div class="col-12 col-md-6 col-xl-4">
            <div class="card h-100">
                <div class="card-body">
                    <div class="d-flex gap-3 mb-3">
                        <div class="skeleton rounded-circle" style="width:56px;height:56px"></div>
                        <div class="flex-grow-1">
                            <div class="skeleton mb-2" style="height:14px;width:60%"></div>
                            <div class="skeleton" style="height:12px;width:40%"></div>
                        </div>
                    </div>
                    <div class="skeleton mb-2" style="height:12px"></div>
                    <div class="skeleton" style="height:12px;width:80%"></div>
                </div>
            </div>
        </div>`).join('');

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
        lista.innerHTML = `
            <div class="col-12">
                <div class="alert alert-danger mb-0" role="alert">
                    Não foi possível carregar os profissionais. Tente novamente.
                </div>
            </div>`;
        document.getElementById('paginacao').innerHTML = '';
        return;
    }

    const arr = Array.isArray(dados) ? dados : [];
    const total = document.getElementById('txt-total');
    if (total) {
        total.textContent = arr.length === 1
            ? '1 profissional encontrado'
            : `${arr.length} profissionais encontrados`;
    }

    if (!arr.length) {
        lista.innerHTML = `
            <div class="col-12">
                <div class="empty-state">
                    <span class="empty-state__icon">
                        <i class="fa-solid fa-magnifying-glass" aria-hidden="true"></i>
                    </span>
                    <p class="empty-state__title">Nenhum profissional encontrado</p>
                    <p class="empty-state__text">
                        Tente descrever a necessidade com outras palavras ou remover alguns filtros.
                    </p>
                    <button class="btn btn-outline-primary" type="button" id="btn-limpar-vazio">
                        Limpar filtros
                    </button>
                </div>
            </div>`;
        // Sem handler inline (convenção do projeto — ver CONTRIBUTING.md)
        document.getElementById('btn-limpar-vazio')
            ?.addEventListener('click', limparFiltros);
        document.getElementById('paginacao').innerHTML = '';
        return;
    }

    lista.innerHTML = arr.map(prest => renderCard(prest, estado.q)).join('');

    // Navegação por clique e por teclado (o card tem role="button")
    lista.querySelectorAll('.bp-card').forEach(card => {
        const abrir = () => {
            location.href = `/pages/prestador/perfil.html?id=${card.dataset.id}`;
        };
        card.addEventListener('click', abrir);
        card.addEventListener('keydown', e => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                abrir();
            }
        });
    });

    renderPag(arr.length);
}

// ── Card do prestador ────────────────────────────────────────────────────────

function renderCard(p, query) {
    const foto = p.fotoUrl
        ? `<img src="${esc(p.fotoUrl)}" alt="" class="bp-card__foto rounded-circle flex-shrink-0">`
        : `<span class="bp-card__foto bp-card__foto--placeholder rounded-circle flex-shrink-0">
               ${esc((p.nome || '?')[0].toUpperCase())}
           </span>`;

    const verificado = p.nivelVerificacao >= 2
        ? `<span class="badge badge-soft-success" title="Perfil verificado">
               <i class="fa-solid fa-circle-check me-1" aria-hidden="true"></i>Verificado
           </span>`
        : '';

    const rating = p.mediaAvaliacoes > 0
        ? `<div class="d-flex align-items-center gap-2 small mb-2">
               <span class="text-nowrap">${estrelas(p.mediaAvaliacoes)}</span>
               <span class="fw-bold">${Number(p.mediaAvaliacoes).toFixed(1)}</span>
               <span class="text-secondary">(${esc(p.totalAvaliacoes)})</span>
           </div>`
        : `<div class="mb-2"><span class="badge badge-soft-info">Novo na plataforma</span></div>`;

    // Destaca as competências que casam com os termos buscados
    const termos = (query || '').toLowerCase().split(/\s+/).filter(Boolean);
    const skills = (p.competencias || []).slice(0, 5).map(s => {
        const casa = termos.some(t => s.toLowerCase().includes(t));
        return `<span class="badge ${casa ? 'badge-soft-success' : 'badge-soft-neutral'}">${esc(s)}</span>`;
    }).join('');

    const projetos = p.contratosEncerrados > 0
        ? `<span class="text-secondary small">
               <i class="fa-solid fa-briefcase me-1" aria-hidden="true"></i>
               ${esc(p.contratosEncerrados)} projeto${p.contratosEncerrados !== 1 ? 's' : ''}
           </span>`
        : '';

    return `
    <div class="col-12 col-md-6 col-xl-4">
        <article class="card card-hover bp-card h-100" data-id="${esc(p.id)}"
                 role="button" tabindex="0" aria-label="Ver perfil de ${esc(p.nome)}">
            <div class="card-body d-flex flex-column">

                <div class="d-flex gap-3 mb-3">
                    ${foto}
                    <div class="bp-card__info">
                        <h2 class="h6 fw-bold mb-1 text-truncate">${esc(p.nome)}</h2>
                        <p class="small text-secondary mb-1">${esc(p.tituloProfissional || '')}</p>
                        ${verificado}
                    </div>
                </div>

                ${rating}

                ${p.bio ? `<p class="small text-secondary bp-card__bio">${esc(p.bio)}</p>` : ''}

                ${skills ? `<div class="d-flex flex-wrap gap-1 mb-3">${skills}</div>` : ''}

                <div class="mt-auto pt-2 border-top d-flex align-items-center justify-content-between">
                    ${projetos}
                    <span class="small fw-semibold text-primary ms-auto">
                        Ver perfil <i class="fa-solid fa-arrow-right ms-1" aria-hidden="true"></i>
                    </span>
                </div>

            </div>
        </article>
    </div>`;
}

// ── Paginação ────────────────────────────────────────────────────────────────

function renderPag(count) {
    const pag = document.getElementById('paginacao');
    if (!pag) return;

    // Heurística existente: página cheia ⇒ provavelmente há próxima
    const temProxima = count >= estado.pageSize;
    const temAnterior = estado.page > 1;

    if (!temProxima && !temAnterior) { pag.innerHTML = ''; return; }

    pag.innerHTML = `
        <ul class="pagination justify-content-center mb-0">
            <li class="page-item ${!temAnterior ? 'disabled' : ''}">
                <button class="page-link" type="button" data-dir="-1" ${!temAnterior ? 'disabled' : ''}>
                    <i class="fa-solid fa-arrow-left me-1" aria-hidden="true"></i>Anterior
                </button>
            </li>
            <li class="page-item disabled">
                <span class="page-link">Página ${estado.page}</span>
            </li>
            <li class="page-item ${!temProxima ? 'disabled' : ''}">
                <button class="page-link" type="button" data-dir="1" ${!temProxima ? 'disabled' : ''}>
                    Próxima<i class="fa-solid fa-arrow-right ms-1" aria-hidden="true"></i>
                </button>
            </li>
        </ul>`;

    pag.querySelectorAll('button[data-dir]').forEach(btn => {
        btn.addEventListener('click', () => {
            estado.page += parseInt(btn.dataset.dir, 10);
            buscar();
            window.scrollTo({ top: 0, behavior: 'smooth' });
        });
    });
}

// ── Init ─────────────────────────────────────────────────────────────────────
onReady(renderPagina);
