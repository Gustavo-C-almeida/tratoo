// ── Perfil Contratante ────────────────────────────────────────────────────────
// Público por ID (?id=123) ou próprio perfil quando autenticado como contratante.

const root = () => document.getElementById('perfil-root');

function escHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

function iniciais(nome) {
    return (nome ?? '?').split(' ').slice(0, 2).map(p => p[0]).join('').toUpperCase();
}

function formatarMeses(criadoEm) {
    if (!criadoEm) return null;
    const meses = Math.floor((Date.now() - new Date(criadoEm)) / (1000 * 60 * 60 * 24 * 30));
    if (meses < 1) return 'Novo';
    return meses === 1 ? '1 mês' : `${meses} meses`;
}

function formatarMoeda(valor) {
    if (valor == null) return '—';
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 }).format(valor);
}

// ── Detecta o ID do contratante pela URL ──────────────────────────────────────

function obterContratanteId() {
    const pathMatch = window.location.pathname.match(/\/contratantes\/(\d+)\/perfil\.html/i);
    if (pathMatch) return parseInt(pathMatch[1]);

    const params = new URLSearchParams(window.location.search);
    if (params.get('id')) return parseInt(params.get('id'));

    return null;
}

// ── Carrega perfil ────────────────────────────────────────────────────────────

async function carregarPerfil() {
    root().innerHTML = '<p style="text-align:center;padding:60px;color:#94a3b8">Carregando perfil...</p>';

    const contratanteId = obterContratanteId();

    let dados;
    try {
        if (contratanteId) {
            dados = await api.get(`/contratantes/${contratanteId}/perfil`);
        } else {
            dados = await api.get('/contratantes/me/perfil');
        }
    } catch (err) {
        root().innerHTML = `<p style="text-align:center;padding:60px;color:#dc2626">
            Perfil não encontrado ou você não está autenticado.</p>`;
        return;
    }

    const ehProprietario = !contratanteId;
    renderizarPerfil(dados, ehProprietario);
}

// ── Renderização principal ────────────────────────────────────────────────────

function renderizarPerfil(d, ehProprietario) {
    const membro = formatarMeses(d.criadoEm);
    const completudePct = d.porcentagemCompletude ?? null;

    root().innerHTML = `
    <div class="perfil-page">

        <!-- HEADER -->
        <div class="perfil-header">
            ${ehProprietario ? `<div class="perfil-acoes">
                <a href="editar-perfil.html" class="btn-editar">Editar perfil</a>
            </div>` : ''}

            ${d.logoUrl
                ? `<img class="perfil-avatar" src="${escHtml(d.logoUrl)}" alt="Foto de ${escHtml(d.nome)}">`
                : `<div class="perfil-avatar-placeholder">${iniciais(d.nome)}</div>`
            }

            <div class="perfil-header-info">
                <h1 class="perfil-nome">${escHtml(d.nome)}</h1>

                ${d.nomeEmpresa
                    ? `<p class="perfil-titulo">${escHtml(d.nomeEmpresa)}</p>`
                    : d.segmento
                        ? `<p class="perfil-titulo">${escHtml(d.segmento)}</p>`
                        : ''
                }

                ${(d.localizacaoCidade || d.localizacaoEstado)
                    ? `<p class="perfil-localizacao">${escHtml([d.localizacaoCidade, d.localizacaoEstado].filter(Boolean).join(', '))}</p>`
                    : ''
                }

                <div class="perfil-tags">
                    ${d.empresaVerificada ? '<span class="perfil-tag perfil-tag--verified">Verificado</span>' : ''}
                    ${d.segmento ? `<span class="perfil-tag">${escHtml(d.segmento)}</span>` : ''}
                    ${d.idade != null ? `<span class="perfil-tag">${d.idade} anos</span>` : ''}
                    ${membro ? `<span class="perfil-tag">${membro} na plataforma</span>` : ''}
                </div>

                <div class="perfil-links">
                    ${d.siteUrl ? `<a href="${escHtml(d.siteUrl)}" target="_blank" rel="noopener" class="perfil-link">Website</a>` : ''}
                    ${d.linkedinUrl ? `<a href="${escHtml(d.linkedinUrl)}" target="_blank" rel="noopener" class="perfil-link">LinkedIn</a>` : ''}
                </div>
            </div>
        </div>

        <!-- COMPLETUDE (só para o dono) -->
        ${ehProprietario && completudePct !== null ? `
        <div class="perfil-completude">
            <div class="perfil-completude__header">
                <span class="perfil-completude__label">Completude do perfil</span>
                <span class="perfil-completude__pct">${completudePct}%</span>
            </div>
            <div class="perfil-completude__barra">
                <div class="perfil-completude__progresso" style="width:${completudePct}%"></div>
            </div>
            ${d.proximoPassoCompletude ? `<p class="perfil-completude__dica">${escHtml(d.proximoPassoCompletude)}</p>` : ''}
        </div>` : ''}

        <!-- BIO -->
        ${d.descricao ? `
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Sobre</h2>
            <p class="perfil-bio">${escHtml(d.descricao)}</p>
        </div>` : ''}

        <!-- MÉTRICAS -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Atividade na plataforma</h2>
            <div class="metricas-grid">
                <div class="metrica-item">
                    <span class="metrica-valor">${d.totalProjetosPublicados ?? 0}</span>
                    <span class="metrica-label">Projetos publicados</span>
                </div>
                <div class="metrica-item">
                    <span class="metrica-valor">${d.totalProjetosConcluidos ?? 0}</span>
                    <span class="metrica-label">Projetos concluídos</span>
                </div>
                <div class="metrica-item">
                    <span class="metrica-valor metrica-valor--${(d.taxaConclusao ?? 0) >= 80 ? 'success' : (d.taxaConclusao ?? 0) >= 50 ? 'warning' : 'muted'}">${d.taxaConclusao ?? 0}%</span>
                    <span class="metrica-label">Taxa de conclusão</span>
                    <div class="metrica-barra-wrap">
                        <div class="metrica-barra" style="width:${d.taxaConclusao ?? 0}%"></div>
                    </div>
                </div>
                ${d.valorMedioProjetos ? `
                <div class="metrica-item">
                    <span class="metrica-valor">${formatarMoeda(d.valorMedioProjetos)}</span>
                    <span class="metrica-label">Valor médio por projeto</span>
                </div>` : ''}
            </div>
        </div>

        <!-- PROJETOS RECENTES -->
        ${renderizarProjetos(d.ultimosProjetos)}

        <!-- AVALIAÇÕES (injetado assincronamente) -->
        <div id="reputacao-secao"></div>

    </div>`;

    if (d.id) carregarReputacao(d.id);
}

// ── Projetos recentes ─────────────────────────────────────────────────────────

const STATUS_PROJETO_LABEL = {
    Rascunho:    'Rascunho',
    Aberto:      'Aberto',
    EmAndamento: 'Em andamento',
    Concluido:   'Concluído',
    Cancelado:   'Cancelado',
    Disputa:     'Disputa',
    Expirado:    'Expirado',
};

const STATUS_PROJETO_CLASS = {
    Aberto:      'status--aberto',
    EmAndamento: 'status--andamento',
    Concluido:   'status--concluido',
    Cancelado:   'status--cancelado',
    Expirado:    'status--cancelado',
    Disputa:     'status--andamento',
};

function formatarDataCurta(iso) {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short', year: 'numeric' });
}

function renderizarProjetos(lista) {
    if (!lista || !lista.length) return '';

    const itens = lista.map(p => {
        const status = STATUS_PROJETO_LABEL[p.status] ?? p.status;
        const cls    = STATUS_PROJETO_CLASS[p.status] ?? '';
        const desc   = p.descricao
            ? escHtml(p.descricao.length > 120 ? p.descricao.slice(0, 120) + '…' : p.descricao)
            : '';
        const orcamento = p.orcamentoMin && p.orcamentoMax
            ? `${formatarMoeda(p.orcamentoMin)} – ${formatarMoeda(p.orcamentoMax)}`
            : '';

        return `
        <a href="/pages/projetos/detalhe.html?id=${p.id}" class="projeto-card">
            <div class="projeto-card__header">
                <span class="projeto-card__titulo">${escHtml(p.titulo)}</span>
                <span class="projeto-card__status ${cls}">${status}</span>
            </div>
            ${desc ? `<p class="projeto-card__desc">${desc}</p>` : ''}
            <div class="projeto-card__meta">
                ${orcamento ? `<span>💰 ${orcamento}</span>` : ''}
                <span>📅 Prazo: ${formatarDataCurta(p.prazoEntrega)}</span>
                <span>📬 ${p.totalPropostas} proposta${p.totalPropostas !== 1 ? 's' : ''}</span>
            </div>
        </a>`;
    }).join('');

    return `
    <div class="perfil-secao">
        <h2 class="perfil-secao__titulo">Projetos recentes</h2>
        <div class="projetos-lista">${itens}</div>
    </div>`;
}

// ── Reputação (reutiliza os mesmos endpoints do perfil prestador) ─────────────

async function carregarReputacao(usuarioId) {
    const secao = document.getElementById('reputacao-secao');
    if (!secao) return;

    try {
        const [rep, avs] = await Promise.allSettled([
            api.get(`/api/usuarios/${usuarioId}/reputacao`),
            api.get(`/api/usuarios/${usuarioId}/avaliacoes?pagina=1&tamanho=5`)
        ]);

        const reputacao = rep.status === 'fulfilled' ? rep.value : null;
        const avsData   = avs.status === 'fulfilled'  ? avs.value  : null;

        if (!reputacao || reputacao.publica === false) return;
        if (avsData && avsData.avaliacoesVisiveis === false) return;
        if (!reputacao.totalAvaliacoes) return;

        const avaliacoes = avsData?.avaliacoes ?? (Array.isArray(avsData) ? avsData : []);

        const estrelas = (nota) => [1,2,3,4,5]
            .map(i => `<span class="rep-estrela ${i <= nota ? 'rep-estrela--ativa' : ''}">★</span>`)
            .join('');

        const barras = reputacao.distribuicao
            ? [5,4,3,2,1].map(n => {
                const cnt = reputacao.distribuicao[n] ?? 0;
                const pct = reputacao.totalAvaliacoes > 0 ? Math.round(cnt / reputacao.totalAvaliacoes * 100) : 0;
                return `
                <div class="rep-dist-row">
                    <span class="rep-dist-label">${n}★</span>
                    <div class="rep-dist-barra-wrap"><div class="rep-dist-barra" style="width:${pct}%"></div></div>
                    <span class="rep-dist-cnt">${cnt}</span>
                </div>`;
            }).join('')
            : '';

        const cards = avaliacoes.length
            ? avaliacoes.map(av => `
                <div class="rep-av-card">
                    <div class="rep-av-header">
                        <div class="rep-av-autor">
                            ${av.avaliadorFotoUrl
                                ? `<img src="${escHtml(av.avaliadorFotoUrl)}" class="rep-av-foto" alt="">`
                                : `<div class="rep-av-foto-placeholder">${(av.avaliadorNome ?? '?')[0].toUpperCase()}</div>`
                            }
                            <div class="rep-av-autor-info">
                                ${av.avaliadorId
                                    ? (() => {
                                        const url = av.avaliadorEhContratante
                                            ? `/pages/contratante/perfil.html?id=${av.avaliadorId}`
                                            : `/pages/prestador/perfil.html?id=${av.avaliadorId}`;
                                        return `<a href="${url}" class="rep-av-nome-link">${escHtml(av.avaliadorNome ?? 'Anônimo')}</a>`;
                                    })()
                                    : `<span class="rep-av-nome">${escHtml(av.avaliadorNome ?? 'Anônimo')}</span>`
                                }
                                ${av.projetoId
                                    ? `<a href="/pages/projetos/detalhe.html?id=${av.projetoId}" class="rep-av-projeto-link"><span class="rep-av-projeto-icon">📁</span>${escHtml(av.projetoTitulo ?? 'Projeto')}</a>`
                                    : av.projetoTitulo
                                        ? `<span class="rep-av-projeto">${escHtml(av.projetoTitulo)}</span>`
                                        : ''
                                }
                            </div>
                        </div>
                        <div class="rep-av-estrelas">${estrelas(av.nota)}</div>
                    </div>
                    ${av.comentario ? `<p class="rep-av-comentario">${escHtml(av.comentario)}</p>` : ''}
                    ${av.respostaPublica ? `
                        <div class="rep-av-resposta">
                            <span class="rep-av-resposta-label">Resposta do contratante</span>
                            <p class="rep-av-resposta-texto">${escHtml(av.respostaPublica)}</p>
                        </div>` : ''}
                    ${av.publicadaEm ? `<span class="rep-av-data">${new Date(av.publicadaEm).toLocaleDateString('pt-BR', { month: 'short', year: 'numeric' })}</span>` : ''}
                </div>`).join('')
            : '<p class="rep-vazio">Nenhuma avaliação pública ainda.</p>';

        secao.innerHTML = `
            <div class="perfil-secao">
                <h2 class="perfil-secao__titulo">Avaliações</h2>
                <div class="rep-resumo">
                    <div class="rep-media-wrap">
                        <span class="rep-media-numero">${Number(reputacao.mediaGeral).toFixed(1)}</span>
                        <div class="rep-media-estrelas">${estrelas(Math.round(reputacao.mediaGeral))}</div>
                        <span class="rep-media-total">${reputacao.totalAvaliacoes} avaliação${reputacao.totalAvaliacoes !== 1 ? 'ões' : ''}</span>
                    </div>
                    <div class="rep-dist">${barras}</div>
                </div>
                <div class="rep-av-lista">${cards}</div>
            </div>`;

    } catch {
        // falha silenciosa — seção de avaliações é complementar
    }
}

// ── Init ──────────────────────────────────────────────────────────────────────
carregarPerfil();
