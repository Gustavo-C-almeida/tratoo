// ── Propostas do projeto (contratante) - Design Moderno ──────────────────────

const root = () => document.getElementById('root');

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

function dataHoraFmt(d) {
    if (!d) return '—';
    return new Date(d).toLocaleDateString('pt-BR', {
        day: '2-digit',
        month: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
    });
}

const STATUS_LABEL = {
    Draft: 'Rascunho', Submitted: 'Aguardando análise',
    EmNegociacao: 'Em negociação', Aceita: 'Aceita',
    Recusada: 'Recusada', Expirada: 'Expirada', Convertida: 'Convertida'
};

const STATUS_ICON = {
    Draft: '<i class="fa-solid fa-pen-to-square"></i>', Submitted: '<i class="fa-solid fa-hourglass-half"></i>',
    EmNegociacao: '<i class="fa-solid fa-handshake"></i>', Aceita: '<i class="fa-solid fa-circle-check"></i>',
    Recusada: '<i class="fa-solid fa-circle-xmark"></i>', Expirada: '<i class="fa-solid fa-hourglass-end"></i>', Convertida: '<i class="fa-solid fa-arrows-rotate"></i>'
};

let projetoId = null;
let projetoAtual = null;
let propostasAtuais = [];

async function carregar() {
    projetoId = new URLSearchParams(window.location.search).get('id');
    if (!projetoId) {
        root().innerHTML = '<div class="error-state"><span class="error-icon"><i class="fa-solid fa-triangle-exclamation"></i></span><p class="erro-msg">ID do projeto não informado.</p></div>';
        return;
    }

    root().innerHTML = '<div class="loading-state"><div class="loading-spinner-modern"></div><p>Carregando propostas...</p></div>';

    try {
        const [projeto, propostas] = await Promise.all([
            api.get(`/api/projects/${projetoId}`),
            api.get(`/api/projects/${projetoId}/proposals`)
        ]);

        projetoAtual = projeto;
        propostasAtuais = propostas;

        document.title = `Propostas — ${projeto.titulo} — Tratoo`;
        render(projeto, propostas);
    } catch (err) {
        root().innerHTML = `<div class="error-state"><span class="error-icon"><i class="fa-solid fa-circle-xmark"></i></span><p class="erro-msg">${esc(err?.data?.mensagem || 'Erro ao carregar propostas.')}</p></div>`;
    }
}

let ordenacao = 'recente';
let filtroStatus = 'todas';
let filtroOrigem = 'todas';

function ordenarPropostas(lista) {
    const copia = [...lista];
    switch (ordenacao) {
        case 'menor-valor':
            return copia.sort((a, b) => (a.valorTotal ?? 0) - (b.valorTotal ?? 0));
        case 'maior-valor':
            return copia.sort((a, b) => (b.valorTotal ?? 0) - (a.valorTotal ?? 0));
        case 'menor-prazo':
            return copia.sort((a, b) => new Date(a.prazoTotal) - new Date(b.prazoTotal));
        default:
            return copia.sort((a, b) => new Date(b.criadoEm) - new Date(a.criadoEm));
    }
}

function filtrarPropostas(lista) {
    let resultado = lista;
    if (filtroStatus !== 'todas')
        resultado = resultado.filter(p => p.status === filtroStatus);
    if (filtroOrigem === 'convidado')
        resultado = resultado.filter(p => p.foiConvidado);
    else if (filtroOrigem === 'espontaneo')
        resultado = resultado.filter(p => !p.foiConvidado);
    return resultado;
}

function render(projeto, propostas) {
    const ativas = propostas.filter(p => !['Recusada', 'Expirada', 'Convertida'].includes(p.status));
    const encerradas = propostas.filter(p => ['Recusada', 'Expirada', 'Convertida'].includes(p.status));

    const ativasFiltradas = filtrarPropostas(ativas);
    const encerradasFiltradas = filtrarPropostas(encerradas);

    const ativasOrdenadas = ordenarPropostas(ativasFiltradas);
    const encerradasOrdenadas = ordenarPropostas(encerradasFiltradas);

    // Estatísticas
    const totalPropostas = propostas.length;
    const totalConvidados = propostas.filter(p => p.foiConvidado).length;
    const valorMedio = ativas.length > 0
        ? ativas.reduce((sum, p) => sum + (p.valorTotal || 0), 0) / ativas.length
        : 0;
    const prazoMedio = ativas.length > 0
        ? Math.ceil(ativas.reduce((sum, p) => {
            const prazo = new Date(p.prazoTotal) - new Date();
            return sum + (prazo > 0 ? prazo : 0);
        }, 0) / ativas.length / (1000 * 60 * 60 * 24))
        : 0;

    root().innerHTML = `
    <div class="propostas-page-wrap">
        <!-- Header Premium -->
        <div class="propostas-header-premium">
            <div class="propostas-header-content">
                <div class="propostas-breadcrumb">
                    <a href="meus-projetos.html" class="breadcrumb-link-modern">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M19 12H5M12 19l-7-7 7-7"/>
                        </svg>
                        Meus projetos
                    </a>
                    <span class="breadcrumb-separator">/</span>
                    <span class="breadcrumb-current">${esc(projeto.titulo)}</span>
                </div>
                <div class="propostas-title-section">
                    <h1 class="propostas-title">Propostas Recebidas</h1>
                    <p class="propostas-subtitle">Gerencie as propostas enviadas para o seu projeto</p>
                </div>
                <a href="../projetos/detalhe.html?id=${projeto.id}" class="btn-projeto-modern">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/>
                        <circle cx="12" cy="12" r="3"/>
                    </svg>
                    Ver projeto
                </a>
            </div>
        </div>

        <!-- Stats Cards -->
        <div class="stats-grid-modern">
            <div class="stat-card-modern">
                <div class="stat-icon purple"><i class="fa-solid fa-chart-column"></i></div>
                <div class="stat-info">
                    <span class="stat-value">${totalPropostas}</span>
                    <span class="stat-label">Total de propostas</span>
                </div>
            </div>
            <div class="stat-card-modern">
                <div class="stat-icon green"><i class="fa-solid fa-money-bill-wave"></i></div>
                <div class="stat-info">
                    <span class="stat-value">${moeda(valorMedio)}</span>
                    <span class="stat-label">Valor médio</span>
                </div>
            </div>
            <div class="stat-card-modern">
                <div class="stat-icon orange"><i class="fa-solid fa-calendar-days"></i></div>
                <div class="stat-info">
                    <span class="stat-value">${prazoMedio > 0 ? `${prazoMedio} dias` : '—'}</span>
                    <span class="stat-label">Prazo médio</span>
                </div>
            </div>
            <div class="stat-card-modern">
                <div class="stat-icon blue"><i class="fa-solid fa-star"></i></div>
                <div class="stat-info">
                    <span class="stat-value">${ativas.length}</span>
                    <span class="stat-label">Ativas</span>
                </div>
            </div>
            <div class="stat-card-modern">
                <div class="stat-icon violet"><i class="fa-solid fa-envelope-open-text"></i></div>
                <div class="stat-info">
                    <span class="stat-value">${totalConvidados}</span>
                    <span class="stat-label">Convidados</span>
                </div>
            </div>
        </div>

        ${!propostas.length ? `
        <div class="empty-state-modern">
            <div class="empty-icon"><i class="fa-solid fa-inbox"></i></div>
            <h3>Nenhuma proposta recebida</h3>
            <p>Ainda não há propostas para este projeto. Compartilhe o projeto para atrair profissionais.</p>
        </div>
        ` : `
        <!-- Filtros e Ordenação -->
        <div class="filtros-ordenacao-bar">
            <div class="filtros-status">
                <button class="filtro-btn ${filtroStatus === 'todas' ? 'active' : ''}" data-filtro="todas">
                    <span><i class="fa-solid fa-clipboard-list"></i></span> Todas
                </button>
                <button class="filtro-btn ${filtroStatus === 'Submitted' ? 'active' : ''}" data-filtro="Submitted">
                    <span><i class="fa-solid fa-hourglass-half"></i></span> Aguardando
                </button>
                <button class="filtro-btn ${filtroStatus === 'EmNegociacao' ? 'active' : ''}" data-filtro="EmNegociacao">
                    <span><i class="fa-solid fa-handshake"></i></span> Em negociação
                </button>
                <button class="filtro-btn ${filtroStatus === 'Aceita' ? 'active' : ''}" data-filtro="Aceita">
                    <span><i class="fa-solid fa-circle-check"></i></span> Aceitas
                </button>
            </div>
            <div class="filtros-origem">
                <button class="filtro-origem-btn ${filtroOrigem === 'todas' ? 'active' : ''}" data-origem="todas">
                    <i class="fa-solid fa-users"></i> Todos
                </button>
                <button class="filtro-origem-btn ${filtroOrigem === 'convidado' ? 'active' : ''}" data-origem="convidado">
                    <i class="fa-solid fa-envelope-open-text"></i> Convidados
                </button>
                <button class="filtro-origem-btn ${filtroOrigem === 'espontaneo' ? 'active' : ''}" data-origem="espontaneo">
                    <i class="fa-solid fa-magnifying-glass"></i> Espontâneos
                </button>
            </div>
            <div class="ordenacao-wrapper">
                <label class="ordenacao-label">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M3 6h18M6 12h12M10 18h4"/>
                    </svg>
                    Ordenar por:
                </label>
                <select id="ordenar-propostas" class="select-ordenar-modern">
                    <option value="recente" ${ordenacao === 'recente' ? 'selected' : ''}>Mais recente</option>
                    <option value="menor-valor" ${ordenacao === 'menor-valor' ? 'selected' : ''}>Menor valor</option>
                    <option value="maior-valor" ${ordenacao === 'maior-valor' ? 'selected' : ''}>Maior valor</option>
                    <option value="menor-prazo" ${ordenacao === 'menor-prazo' ? 'selected' : ''}>Menor prazo</option>
                </select>
            </div>
        </div>

        <!-- Propostas Ativas -->
        ${ativasOrdenadas.length ? `
        <div class="propostas-section">
            <div class="section-header">
                <h2>Propostas Ativas</h2>
                <span class="section-count">${ativasOrdenadas.length}</span>
            </div>
            <div class="propostas-grid-modern">
                ${ativasOrdenadas.map(p => renderCardModerno(p, false)).join('')}
            </div>
        </div>
        ` : filtroStatus !== 'todas' ? '' : `
        <div class="empty-state-modern small">
            <div class="empty-icon"><i class="fa-solid fa-inbox"></i></div>
            <p>Nenhuma proposta ativa no momento</p>
        </div>
        `}

        <!-- Propostas Encerradas -->
        ${encerradasOrdenadas.length ? `
        <div class="propostas-section encerradas">
            <div class="section-header">
                <h2>Propostas Encerradas</h2>
                <span class="section-count">${encerradasOrdenadas.length}</span>
            </div>
            <div class="propostas-grid-modern">
                ${encerradasOrdenadas.map(p => renderCardModerno(p, true)).join('')}
            </div>
        </div>
        ` : ''}
        `}
    </div>`;

    // Bind eventos
    const selectOrdenar = document.getElementById('ordenar-propostas');
    if (selectOrdenar) {
        selectOrdenar.addEventListener('change', () => {
            ordenacao = selectOrdenar.value;
            render(projetoAtual, propostasAtuais);
        });
    }

    document.querySelectorAll('.filtro-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            filtroStatus = btn.dataset.filtro;
            render(projetoAtual, propostasAtuais);
        });
    });

    document.querySelectorAll('.filtro-origem-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            filtroOrigem = btn.dataset.origem;
            render(projetoAtual, propostasAtuais);
        });
    });
}

function renderCardModerno(p, opaco = false) {
    const statusKey = p.status;
    const statusLabel = STATUS_LABEL[statusKey] || statusKey;
    const statusIcon = STATUS_ICON[statusKey] || '<i class="fa-solid fa-file-lines"></i>';

    const corStatus = {
        Submitted: 'azul', EmNegociacao: 'amarelo', Aceita: 'verde',
        Recusada: 'vermelho', Expirada: 'cinza', Draft: 'cinza', Convertida: 'cinza'
    };

    const tempoDesdeEnvio = calcularTempoRelativo(p.criadoEm);
    const dataFormatada = formatarDataHoraUTC(p.criadoEm);
    const prazoRestante = calcularPrazoRestante(p.prazoTotal);

    return `
    <a href="../proposta/detalhe.html?id=${p.id}" class="proposta-card-modern ${opaco ? 'opaco' : ''}">
        <div class="proposta-card-gradient"></div>
        
        <div class="proposta-card-header-modern">
            <div class="prestador-info-area">
                <div class="prestador-avatar-mini">
                    ${p.prestadorNome?.charAt(0).toUpperCase() || 'P'}
                </div>
                <div class="prestador-detalhes">
                    <h3 class="prestador-nome">${esc(p.prestadorNome)}</h3>
                    ${p.prestadorTitulo ? `<p class="prestador-funcao">${esc(p.prestadorTitulo)}</p>` : ''}
                </div>
            </div>
            <div class="proposta-badges-area">
                ${p.foiConvidado ? `
                <span class="badge-convidado" title="Este prestador foi convidado diretamente para este projeto">
                    <i class="fa-solid fa-envelope-open-text" aria-hidden="true"></i> Convidado
                </span>` : ''}
                <div class="status-badge-modern ${corStatus[statusKey] || 'cinza'}">
                    <span class="status-icon">${statusIcon}</span>
                    <span class="status-text">${statusLabel}</span>
                </div>
            </div>
        </div>
        
        <div class="proposta-card-body">
            <p class="proposta-objetivo-modern">${esc(p.objetivo)}</p>
            
            <div class="proposta-meta-grid">
                <div class="meta-item-modern">
                    <span class="meta-icon"><i class="fa-solid fa-money-bill-wave"></i></span>
                    <div class="meta-content">
                        <span class="meta-label">Valor</span>
                        <strong class="meta-value">${moeda(p.valorTotal)}</strong>
                    </div>
                </div>
                <div class="meta-item-modern">
                    <span class="meta-icon"><i class="fa-solid fa-calendar-days"></i></span>
                    <div class="meta-content">
                        <span class="meta-label">Prazo</span>
                        <strong class="meta-value ${prazoRestante.classe}">${dataFmt(p.prazoTotal)}</strong>
                    </div>
                </div>
                <div class="meta-item-modern">
                    <span class="meta-icon"><i class="fa-solid fa-arrows-rotate"></i></span>
                    <div class="meta-content">
                        <span class="meta-label">Versão</span>
                        <strong class="meta-value">${p.versaoAtual || 1}</strong>
                    </div>
                </div>
                <div class="meta-item-modern">
                    <span class="meta-icon"><i class="fa-solid fa-stopwatch"></i></span>
                    <div class="meta-content">
                        <span class="meta-label">Enviada</span>
                        <strong class="meta-value" title="${tempoDesdeEnvio} (${dataFormatada} UTC)">${dataFormatada}</strong>
                    </div>
                </div>
            </div>
        </div>
        
        <div class="proposta-card-footer">
            <span class="ver-detalhe-link">
                Ver detalhes
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M5 12h14M12 5l7 7-7 7"/>
                </svg>
            </span>
        </div>
    </a>`;
}

function formatarDataHoraUTC(data) {
    if (!data) return '—';
    // Garante parsing correto de UTC (adiciona 'Z' se não estiver presente)
    const dataString = typeof data === 'string' ? data : String(data);
    const dataFormatada = dataString.includes('Z') || dataString.includes('+')
        ? new Date(dataString)
        : new Date(dataString + 'Z');

    if (isNaN(dataFormatada.getTime())) return '—';

    const dia = dataFormatada.getUTCDate();
    const mes = dataFormatada.getUTCMonth() + 1;
    const ano = dataFormatada.getUTCFullYear();
    const horas = String(dataFormatada.getUTCHours()).padStart(2, '0');
    const minutos = String(dataFormatada.getUTCMinutes()).padStart(2, '0');

    return `${dia}/${mes}/${ano} ${horas}:${minutos}`;
}

function calcularTempoRelativo(data) {
    if (!data) return '—';
    // Garante parsing correto de UTC
    const dataString = typeof data === 'string' ? data : String(data);
    const dataFormatada = dataString.includes('Z') || dataString.includes('+')
        ? new Date(dataString)
        : new Date(dataString + 'Z');

    if (isNaN(dataFormatada.getTime())) return '—';

    const diff = Date.now() - dataFormatada.getTime();
    const dias = Math.floor(diff / (1000 * 60 * 60 * 24));
    const horas = Math.floor(diff / (1000 * 60 * 60));
    const minutos = Math.floor(diff / (1000 * 60));

    if (minutos < 60) return `${Math.max(minutos, 0)} min atrás`;
    if (horas < 24) return `${horas} h atrás`;
    return `${dias} d atrás`;
}

function calcularPrazoRestante(prazoTotal) {
    if (!prazoTotal) return { texto: '—', classe: '' };
    const diff = new Date(prazoTotal).getTime() - Date.now();
    const dias = Math.ceil(diff / (1000 * 60 * 60 * 24));

    if (diff <= 0) return { texto: 'Expirado', classe: 'expirado' };
    if (dias <= 3) return { texto: `${dias} dias`, classe: 'urgente' };
    if (dias <= 7) return { texto: `${dias} dias`, classe: 'alerta' };
    return { texto: `${dias} dias`, classe: '' };
}

carregar();