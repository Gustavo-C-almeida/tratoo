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

// ── Labels de exibição ────────────────────────────────────────────────────────

const TIPO_PESSOA_LABEL = {
    PessoaJuridica: 'Empresa',
    PessoaFisica:   'Pessoa Física',
};

const DISPONIBILIDADE_LABEL = {
    AceitandoPrestadores: 'Aceitando prestadores',
    Pausado:              'Pausado no momento',
};

const TAMANHO_EQUIPE_LABEL = {
    SoloPJ:         'Solo / PJ',
    MicroEmpresa:   'Microempresa',
    PequenoEmpresa: 'Pequena empresa',
    MediaEmpresa:   'Média empresa',
};

const SEGMENTO_ICONE = {
    'Tecnologia':      'fa-microchip',
    'Saúde':           'fa-heart-pulse',
    'Educação':        'fa-graduation-cap',
    'Construção':      'fa-hard-hat',
    'Finanças':        'fa-chart-line',
    'Varejo':          'fa-store',
    'Logística':       'fa-truck',
    'Marketing':       'fa-bullhorn',
    'Jurídico':        'fa-scale-balanced',
    'Alimentação':     'fa-utensils',
};

function iconeSegmento(segmento) {
    if (!segmento) return 'fa-briefcase';
    for (const [key, icon] of Object.entries(SEGMENTO_ICONE)) {
        if (segmento.toLowerCase().includes(key.toLowerCase())) return icon;
    }
    return 'fa-briefcase';
}

// ── Estrelas ──────────────────────────────────────────────────────────────────

function renderEstrelas(nota, tamanho = '') {
    return [1,2,3,4,5].map(i =>
        `<span class="rep-estrela ${i <= Math.round(nota) ? 'rep-estrela--ativa' : ''} ${tamanho}">
            <i class="fa-solid fa-star" aria-hidden="true"></i>
        </span>`
    ).join('');
}

// ── Renderização principal ────────────────────────────────────────────────────

function renderizarPerfil(d, ehProprietario) {
    const membro = formatarMeses(d.criadoEm);
    const completudePct = d.porcentagemCompletude ?? null;
    const tipoPessoaLabel = TIPO_PESSOA_LABEL[d.tipoPessoa] ?? null;
    const disponibilidadeLabel = DISPONIBILIDADE_LABEL[d.disponibilidade] ?? null;
    const tamanhoEquipeLabel = TAMANHO_EQUIPE_LABEL[d.tamanhoEquipe] ?? null;
    const notaMedia = d.mediaAvaliacoes ?? d.mediaGeral ?? null;

    // ── Selos automáticos ────────────────────────────────────────────────────
    const selos = [];
    if (d.empresaVerificada)                    selos.push({ icon: 'fa-shield-check',  texto: 'Verificado',               cls: 'selo--verified' });
    if (d.pagadorVerificado)                    selos.push({ icon: 'fa-circle-dollar-to-slot', texto: 'Pagador verificado', cls: 'selo--pagador' });
    if (d.totalContratosConcluidoss >= 5)       selos.push({ icon: 'fa-trophy',         texto: `${d.totalContratosConcluidoss}+ contratos`, cls: 'selo--trophy' });
    else if (d.totalContratosConcluidoss >= 1)  selos.push({ icon: 'fa-check-circle',   texto: `${d.totalContratosConcluidoss} contrato${d.totalContratosConcluidoss > 1 ? 's' : ''}`, cls: 'selo--check' });
    if (d.totalAvaliacoes >= 3 && notaMedia >= 4.5) selos.push({ icon: 'fa-star',      texto: 'Muito bem avaliado',       cls: 'selo--star' });

    const selosHtml = selos.map(s =>
        `<span class="perfil-selo ${s.cls}">
            <i class="fa-solid ${s.icon}" aria-hidden="true"></i> ${s.texto}
        </span>`
    ).join('');

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
                <div class="perfil-header-top">
                    <h1 class="perfil-nome">${escHtml(d.nome)}</h1>
                    ${disponibilidadeLabel ? `
                    <span class="badge-disponibilidade ${d.disponibilidade === 'AceitandoPrestadores' ? 'badge-disponibilidade--ativo' : 'badge-disponibilidade--pausado'}">
                        <i class="fa-solid ${d.disponibilidade === 'AceitandoPrestadores' ? 'fa-circle-check' : 'fa-circle-pause'}" aria-hidden="true"></i>
                        ${disponibilidadeLabel}
                    </span>` : ''}
                </div>

                ${d.nomeEmpresa
                    ? `<p class="perfil-titulo">${escHtml(d.nomeEmpresa)}${tipoPessoaLabel ? ` <span class="perfil-tipo-pessoa">${tipoPessoaLabel}</span>` : ''}</p>`
                    : d.segmento
                        ? `<p class="perfil-titulo"><i class="fa-solid ${iconeSegmento(d.segmento)}" aria-hidden="true"></i> ${escHtml(d.segmento)}${tipoPessoaLabel ? ` <span class="perfil-tipo-pessoa">${tipoPessoaLabel}</span>` : ''}</p>`
                        : tipoPessoaLabel
                            ? `<p class="perfil-titulo"><span class="perfil-tipo-pessoa">${tipoPessoaLabel}</span></p>`
                            : ''
                }

                ${(d.localizacaoCidade || d.localizacaoEstado)
                    ? `<p class="perfil-localizacao"><i class="fa-solid fa-location-dot" aria-hidden="true"></i> ${escHtml([d.localizacaoCidade, d.localizacaoEstado].filter(Boolean).join(', '))}</p>`
                    : ''
                }

                ${notaMedia && d.totalAvaliacoes > 0 ? `
                <div class="perfil-nota-header">
                    <div class="perfil-nota-estrelas">${renderEstrelas(notaMedia, 'rep-estrela--sm')}</div>
                    <span class="perfil-nota-valor">${Number(notaMedia).toFixed(1)}</span>
                    <span class="perfil-nota-total">(${d.totalAvaliacoes} avaliação${d.totalAvaliacoes !== 1 ? 'ões' : ''})</span>
                </div>` : ''}

                <div class="perfil-links">
                    ${d.siteUrl ? `<a href="${escHtml(d.siteUrl)}" target="_blank" rel="noopener" class="perfil-link"><i class="fa-solid fa-globe" aria-hidden="true"></i> Website</a>` : ''}
                    ${d.linkedinUrl ? `<a href="${escHtml(d.linkedinUrl)}" target="_blank" rel="noopener" class="perfil-link"><i class="fa-brands fa-linkedin" aria-hidden="true"></i> LinkedIn</a>` : ''}
                    ${d.emailContato ? `<a href="mailto:${escHtml(d.emailContato)}" class="perfil-link perfil-link--email"><i class="fa-solid fa-envelope" aria-hidden="true"></i> ${escHtml(d.emailContato)}</a>` : ''}
                </div>
            </div>
        </div>

        <!-- SELOS AUTOMÁTICOS -->
        ${selosHtml ? `<div class="perfil-selos">${selosHtml}</div>` : ''}

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

        <!-- DETALHES -->
        ${renderizarDetalhes(d, membro, tipoPessoaLabel, tamanhoEquipeLabel)}

        <!-- BIO -->
        ${d.descricao ? `
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Sobre</h2>
            <p class="perfil-bio">${escHtml(d.descricao)}</p>
        </div>` : ''}

        <!-- POR QUE TRABALHAR COMIGO -->
        ${d.porQueTrabalharComigo ? `
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo"><i class="fa-solid fa-handshake" aria-hidden="true"></i> Por que trabalhar comigo</h2>
            <div class="perfil-pq-trabalhar">${escHtml(d.porQueTrabalharComigo).replace(/\n/g, '<br>')}</div>
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
                    <span class="metrica-valor metrica-valor--${d.totalProjetosAtivos > 0 ? 'success' : 'muted'}">${d.totalProjetosAtivos ?? 0}</span>
                    <span class="metrica-label">Projetos em aberto</span>
                </div>
                <div class="metrica-item">
                    <span class="metrica-valor">${d.totalContratosConcluidoss ?? 0}</span>
                    <span class="metrica-label">Contratos concluídos</span>
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
                ${d.tempoMedioDecisaoDias != null ? `
                <div class="metrica-item">
                    <span class="metrica-valor">${d.tempoMedioDecisaoDias < 1 ? '< 1' : Math.round(d.tempoMedioDecisaoDias)} dia${Math.round(d.tempoMedioDecisaoDias) !== 1 ? 's' : ''}</span>
                    <span class="metrica-label">Tempo médio de decisão</span>
                </div>` : ''}
            </div>
        </div>

        <!-- AÇÃO RÁPIDA: VER PROJETOS ABERTOS -->
        ${d.totalProjetosAtivos > 0 ? `
        <div class="perfil-acao-rapida">
            <a href="/pages/projetos/busca.html?contratanteId=${d.id}" class="btn-projetos-abertos">
                <i class="fa-solid fa-folder-open" aria-hidden="true"></i>
                Ver ${d.totalProjetosAtivos} projeto${d.totalProjetosAtivos !== 1 ? 's' : ''} aberto${d.totalProjetosAtivos !== 1 ? 's' : ''} deste contratante
            </a>
        </div>` : ''}

        <!-- PROJETOS RECENTES -->
        ${renderizarProjetos(d.ultimosProjetos)}

        <!-- AVALIAÇÕES (injetado assincronamente) -->
        <div id="reputacao-secao"></div>

    </div>`;

    if (d.id) carregarReputacao(d.id);
}

// ── Detalhes do perfil ────────────────────────────────────────────────────────

function renderizarDetalhes(d, membro, tipoPessoaLabel, tamanhoEquipeLabel) {
    const itens = [];

    if (d.empresaVerificada)
        itens.push({ icon: 'fa-shield-check',            cls: 'detalhe--verified', valor: 'Identidade verificada' });
    if (d.pagadorVerificado)
        itens.push({ icon: 'fa-circle-dollar-to-slot',   cls: 'detalhe--pagador',  valor: 'Pagador verificado' });
    if (tipoPessoaLabel)
        itens.push({ icon: 'fa-id-card',                 cls: '',                  valor: tipoPessoaLabel });
    if (d.segmento && d.nomeEmpresa)
        itens.push({ icon: iconeSegmento(d.segmento),    cls: '',                  valor: escHtml(d.segmento) });
    if (tamanhoEquipeLabel)
        itens.push({ icon: 'fa-users',                   cls: '',                  valor: tamanhoEquipeLabel });
    if (d.anoAbertura)
        itens.push({ icon: 'fa-building',                cls: '',                  valor: `Fundada em ${d.anoAbertura}` });
    if (d.idade != null)
        itens.push({ icon: 'fa-user',                    cls: '',                  valor: `${d.idade} anos` });
    if (membro)
        itens.push({ icon: 'fa-clock',                   cls: '',                  valor: `${membro} na plataforma` });
    if (d.idiomasAceitos?.length)
        itens.push({ icon: 'fa-language',                cls: '',                  valor: d.idiomasAceitos.map(escHtml).join(' · ') });

    if (!itens.length) return '';

    const html = itens.map(item => `
        <div class="perfil-detalhe-item ${item.cls}">
            <i class="fa-solid ${item.icon}" aria-hidden="true"></i>
            <span>${item.valor}</span>
        </div>`).join('');

    return `
    <div class="perfil-detalhes">
        ${html}
    </div>`;
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

        const diasDecisao = p.diasAteContratacao != null
            ? `<span><i class="fa-solid fa-bolt"></i> Contratado em ${p.diasAteContratacao < 1 ? '< 1' : Math.round(p.diasAteContratacao)} dia${Math.round(p.diasAteContratacao) !== 1 ? 's' : ''}</span>`
            : '';

        return `
        <a href="/pages/projetos/detalhe.html?id=${p.id}" class="projeto-card">
            <div class="projeto-card__header">
                <span class="projeto-card__titulo">${escHtml(p.titulo)}</span>
                <span class="projeto-card__status ${cls}">${status}</span>
            </div>
            ${desc ? `<p class="projeto-card__desc">${desc}</p>` : ''}
            <div class="projeto-card__meta">
                ${orcamento ? `<span><i class="fa-solid fa-money-bill-wave"></i> ${orcamento}</span>` : ''}
                <span><i class="fa-solid fa-calendar-days"></i> Prazo: ${formatarDataCurta(p.prazoEntrega)}</span>
                <span><i class="fa-solid fa-envelope-open-text"></i> ${p.totalPropostas} proposta${p.totalPropostas !== 1 ? 's' : ''}</span>
                ${diasDecisao}
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
            .map(i => `<span class="rep-estrela ${i <= nota ? 'rep-estrela--ativa' : ''}"><i class="fa-solid fa-star"></i></span>`)
            .join('');

        const barras = reputacao.distribuicao
            ? [5,4,3,2,1].map(n => {
                const cnt = reputacao.distribuicao[n] ?? 0;
                const pct = reputacao.totalAvaliacoes > 0 ? Math.round(cnt / reputacao.totalAvaliacoes * 100) : 0;
                return `
                <div class="rep-dist-row">
                    <span class="rep-dist-label">${n}<i class="fa-solid fa-star"></i></span>
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
                                    ? `<a href="/pages/projetos/detalhe.html?id=${av.projetoId}" class="rep-av-projeto-link"><span class="rep-av-projeto-icon"><i class="fa-solid fa-folder"></i></span>${escHtml(av.projetoTitulo ?? 'Projeto')}</a>`
                                    : av.projetoTitulo
                                        ? `<span class="rep-av-projeto">${escHtml(av.projetoTitulo)}</span>`
                                        : ''
                                }
                            </div>
                        </div>
                        <div class="rep-av-estrelas">${estrelas(av.nota)}</div>
                    </div>
                    ${av.comentario ? `<p class="rep-av-comentario">${escHtml(av.comentario)}</p>` : ''}
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
