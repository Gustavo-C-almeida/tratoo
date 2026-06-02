// ── Utilitários ──────────────────────────────────────────────────────────────

const root = () => document.getElementById('perfil-root');

function nivelLabel(n) {
    const m = { 1: 'Básico', 2: 'Iniciante', 3: 'Intermediário', 4: 'Avançado', 5: 'Especialista' };
    return m[n] ?? `Nível ${n}`;
}

function formatarData(iso) {
    if (!iso) return '';
    return new Date(iso).toLocaleDateString('pt-BR', { month: 'short', year: 'numeric' });
}

function iniciais(nome) {
    return (nome ?? '?').split(' ').slice(0, 2).map(p => p[0]).join('').toUpperCase();
}

function escHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

// ── Detecta o ID do prestador pela URL ────────────────────────────────────────
// Suporta:
//   - /prestadores/{id}/perfil.html → extrai {id} do pathname
//   - /pages/prestador/perfil.html?id={id} → extrai ?id do query string
//   - /pages/prestador/perfil.html (sem id) → carrega próprio perfil autenticado

function obterPrestadorId() {
    // Tenta extrair do pathname: /prestadores/{id}/perfil.html
    const pathMatch = window.location.pathname.match(/\/prestadores\/(\d+)\/perfil\.html/i);
    if (pathMatch) {
        return parseInt(pathMatch[1]);
    }

    // Fallback: tenta extrair do query parameter ?id=X
    const params = new URLSearchParams(window.location.search);
    if (params.get('id')) {
        return parseInt(params.get('id'));
    }

    // Sem ID → carrega o próprio perfil autenticado
    return null;
}

// ── Carrega perfil público ────────────────────────────────────────────────────

async function carregarPerfil(prestadorId) {
    root().innerHTML = '<p style="text-align:center;padding:60px;color:#94a3b8">Carregando perfil...</p>';

    let dados;
    try {
        if (prestadorId) {
            dados = await api.get(`/prestadores/${prestadorId}/perfil`);
        } else {
            // Modo "meu perfil" — usa rota autenticada
            dados = await api.get('/prestadores/me/perfil');
        }
    } catch (err) {
        root().innerHTML = `<p style="text-align:center;padding:60px;color:#dc2626">
            Perfil não encontrado ou você não está autenticado.</p>`;
        return;
    }

    const ehProprietario = !prestadorId;
    renderizarPerfil(dados, ehProprietario);
}

// ── Renderização principal ────────────────────────────────────────────────────

function renderizarPerfil(d, ehProprietario) {
    const linksExtras = renderizarLinksExtras(d.outrosLinks);
    const completudePct = d.porcentagemCompleto ?? 0;
    const dicaCompletude = completudePct < 100
        ? sugerirProximoPasso(d)
        : 'Perfil completo!';

    root().innerHTML = `
    <div class="perfil-page">

        <!-- HEADER -->
        <div class="perfil-header">
            ${ehProprietario ? `<div class="perfil-acoes">
                <a href="editar-perfil.html" class="btn-editar">Editar perfil</a>
            </div>` : ''}

            ${d.fotoUrl
            ? `<img class="perfil-avatar" src="${escHtml(d.fotoUrl)}" alt="Foto de ${escHtml(d.nome)}">`
            : `<div class="perfil-avatar-placeholder">${iniciais(d.nome)}</div>`
        }

            <div class="perfil-header-info">
                <h1 class="perfil-nome">${escHtml(d.nome)}</h1>
                ${d.tituloProfissional ? `<p class="perfil-titulo">${escHtml(d.tituloProfissional)}</p>` : ''}
                ${(d.localizacaoCidade || d.localizacaoEstado)
            ? `<p class="perfil-localizacao">${escHtml([d.localizacaoCidade, d.localizacaoEstado].filter(Boolean).join(', '))}</p>`
            : ''}

                <div class="perfil-tags">
                    ${d.disponivel ? '<span class="perfil-tag">Disponível</span>' : '<span class="perfil-tag">Indisponível</span>'}
                    ${d.nivelVerificacao ? '<span class="perfil-tag perfil-tag--verified">Identidade verificada</span>' : ''}
                    ${d.valorHora ? `<span class="perfil-tag">R$ ${Number(d.valorHora).toFixed(2).replace('.', ',')}/h</span>` : ''}
                </div>

                <div class="perfil-links">
                    ${d.linkedinUrl ? `<a href="${escHtml(d.linkedinUrl)}" target="_blank" rel="noopener" class="perfil-link">LinkedIn</a>` : ''}
                    ${d.gitHubUrl ? `<a href="${escHtml(d.gitHubUrl)}"   target="_blank" rel="noopener" class="perfil-link">GitHub</a>` : ''}
                    ${d.emailContato ? `<a href="mailto:${escHtml(d.emailContato)}" class="perfil-link">${escHtml(d.emailContato)}</a>` : ''}
                    ${linksExtras}
                </div>
            </div>
        </div>

        <!-- COMPLETUDE (só visível para o dono) -->
        ${ehProprietario ? `
        <div class="perfil-completude">
            <div class="perfil-completude__header">
                <span class="perfil-completude__label">Completude do perfil</span>
                <span class="perfil-completude__pct">${completudePct}%</span>
            </div>
            <div class="perfil-completude__barra">
                <div class="perfil-completude__progresso" style="width:${completudePct}%"></div>
            </div>
            <p class="perfil-completude__dica">${escHtml(dicaCompletude)}</p>
        </div>` : ''}

        <!-- BIO -->
        ${d.bio || d.descricao ? `
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Sobre mim</h2>
            <p class="perfil-bio">${escHtml(d.bio ?? d.descricao)}</p>
        </div>` : ''}

        <!-- COMPETÊNCIAS -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">
                Competências
                ${ehProprietario ? `<button class="perfil-secao__add" onclick="abrirFormCompetencia()">+ Adicionar</button>` : ''}
            </h2>
            <div id="competencia-form-wrap"></div>
            <div id="competencia-lista" class="competencia-lista">
                ${renderizarCompetencias(d.competencias, ehProprietario)}
            </div>
        </div>

        <!-- PORTFÓLIO -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">
                Portfólio
                ${ehProprietario ? `<button class="perfil-secao__add" onclick="abrirFormPortfolio()">+ Adicionar</button>` : ''}
            </h2>
            <div id="portfolio-form-wrap"></div>
            <div id="portfolio-grid" class="portfolio-grid">
                ${renderizarPortfolio(d.portfolio, ehProprietario)}
            </div>
        </div>

        <!-- EXPERIÊNCIAS -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">
                Experiência Profissional
                ${ehProprietario ? `<button class="perfil-secao__add" onclick="abrirFormExperiencia()">+ Adicionar</button>` : ''}
            </h2>
            <div id="experiencia-form-wrap"></div>
            <div id="experiencia-lista">
                ${renderizarExperiencias(d.experiencias, ehProprietario)}
            </div>
        </div>

        <!-- CERTIFICAÇÕES -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">
                Certificações
                ${ehProprietario ? `<button class="perfil-secao__add" onclick="abrirFormCertificacao()">+ Adicionar</button>` : ''}
            </h2>
            <div id="certificacao-form-wrap"></div>
            <div id="certificacao-lista">
                ${renderizarCertificacoes(d.certificacoes, ehProprietario)}
            </div>
        </div>

        <!-- AVALIAÇÕES (injetado assincronamente) -->
        <div id="reputacao-secao"></div>

        <!-- PROFISSIONAIS SIMILARES (injetado assincronamente, só em perfis de terceiros) -->
        <div id="similares-secao"></div>

    </div>`;

    // Guarda dados em memória para mutações
    window._perfilDados = d;
    window._ehProprietario = ehProprietario;

    // Carrega reputação assincronamente (usa userId do prestador)
    if (d.id) carregarReputacao(d.id);

    // Profissionais similares — só faz sentido ao visualizar o perfil de outra pessoa
    if (!ehProprietario && d.id) carregarSimilares(d.id);
}

// ── Profissionais Similares ──────────────────────────────────────────────────
// GET /api/busca/prestadores/{id}/similares — descoberta de talentos relacionados.

async function carregarSimilares(prestadorId) {
    const secao = document.getElementById('similares-secao');
    if (!secao) return;

    // Skeleton enquanto carrega
    secao.innerHTML = `
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Profissionais Similares</h2>
            <div class="similares-grid">
                ${Array(3).fill(`<div class="similar-card similar-card--skeleton"></div>`).join('')}
            </div>
        </div>`;

    let lista;
    try {
        lista = await api.get(`/api/busca/prestadores/${prestadorId}/similares?quantidade=6`);
    } catch {
        secao.innerHTML = '';   // falha silenciosa — seção é complementar
        return;
    }

    const arr = (Array.isArray(lista) ? lista : []).filter(p => p.id !== prestadorId);
    if (!arr.length) { secao.innerHTML = ''; return; }

    secao.innerHTML = `
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Profissionais Similares</h2>
            <p class="similares-sub">Perfis com competências e experiência parecidas</p>
            <div class="similares-grid">
                ${arr.map(renderSimilarCard).join('')}
            </div>
        </div>`;

    secao.querySelectorAll('.similar-card').forEach(card => {
        card.addEventListener('click', () => {
            window.location.href = `perfil.html?id=${card.dataset.id}`;
        });
    });
}

function renderSimilarCard(p) {
    const foto = p.fotoUrl
        ? `<img src="${escHtml(p.fotoUrl)}" alt="${escHtml(p.nome)}" class="similar-card__foto">`
        : `<div class="similar-card__foto similar-card__foto--ph">${iniciais(p.nome)}</div>`;

    const compat = p.similaridade > 0
        ? `<span class="similar-card__match">${Math.round(p.similaridade * 100)}% compatível</span>`
        : '';

    const rating = p.mediaAvaliacoes > 0
        ? `<span class="similar-card__rating">★ ${Number(p.mediaAvaliacoes).toFixed(1)} <small>(${p.totalAvaliacoes})</small></span>`
        : '';

    const verif = p.nivelVerificacao >= 2
        ? '<span class="similar-card__verif" title="Identidade verificada">✓</span>' : '';

    const skills = (p.competencias || []).slice(0, 3)
        .map(s => `<span class="similar-card__skill">${escHtml(s)}</span>`).join('');

    return `
    <article class="similar-card" data-id="${p.id}" role="button" tabindex="0"
        aria-label="Ver perfil de ${escHtml(p.nome)}">
        ${foto}
        <div class="similar-card__body">
            <p class="similar-card__nome">${escHtml(p.nome)} ${verif}</p>
            ${p.tituloProfissional ? `<p class="similar-card__titulo">${escHtml(p.tituloProfissional)}</p>` : ''}
            <div class="similar-card__meta">${compat}${rating}</div>
            ${skills ? `<div class="similar-card__skills">${skills}</div>` : ''}
        </div>
    </article>`;
}

// ── Helpers de renderização ───────────────────────────────────────────────────

function renderizarLinksExtras(json) {
    if (!json) return '';
    try {
        const links = JSON.parse(json);
        return links.map(l =>
            `<a href="${escHtml(l.url)}" target="_blank" rel="noopener" class="perfil-link">${escHtml(l.titulo)}</a>`
        ).join('');
    } catch { return ''; }
}

function renderizarCompetencias(lista, ehProprietario) {
    if (!lista?.length) return '<span class="empty-state">Nenhuma competência cadastrada.</span>';
    return lista.map(c => `
        <div class="competencia-item" id="comp-${c.id}">
            ${escHtml(c.nome)}
            <span class="competencia-nivel nivel-${c.nivel}">${nivelLabel(c.nivel)}</span>
            ${ehProprietario ? `
                <button class="btn-icon btn-icon--danger" onclick="removerCompetencia(${c.id})" title="Remover">✕</button>
            ` : ''}
        </div>
    `).join('');
}

function renderizarBadgesCompetencias(competencias) {
    if (!competencias?.length) return '';
    return `<div class="comp-badges">${competencias.map(c =>
        `<span class="comp-badge nivel-${c.nivel}" title="${nivelLabel(c.nivel)}">${escHtml(c.nome)}</span>`
    ).join('')}</div>`;
}

function renderizarPortfolio(lista, ehProprietario) {
    if (!lista?.length) return '<span class="empty-state">Nenhum item de portfólio.</span>';
    return lista.map(p => `
        <div class="portfolio-card" id="port-${p.id}">
            <div class="portfolio-card__thumb">
                ${p.arquivoUrl ? '📄' : '🔗'}
            </div>
            <div class="portfolio-card__body">
                <p class="portfolio-card__titulo">${escHtml(p.titulo)}</p>
                ${p.descricao ? `<p class="portfolio-card__desc">${escHtml(p.descricao)}</p>` : ''}
                ${p.linkExterno
            ? `<a href="${escHtml(p.linkExterno)}" target="_blank" rel="noopener" class="portfolio-card__link">Ver projeto</a>`
            : p.arquivoUrl
                ? `<a href="${escHtml(p.arquivoUrl)}" target="_blank" rel="noopener" class="portfolio-card__link">Ver PDF</a>`
                : ''}
                ${renderizarBadgesCompetencias(p.competencias)}
                <div id="comp-mgr-port-${p.id}"></div>
                ${ehProprietario ? `
                    <div style="margin-top:8px;display:flex;gap:6px;flex-wrap:wrap">
                        <button class="btn-icon" onclick="editarPortfolioItem(${p.id})">Editar</button>
                        <button class="btn-icon btn-icon--secondary" onclick="toggleGerenciarCompetencias('port', ${p.id})">Competências</button>
                        <button class="btn-icon btn-icon--danger" onclick="removerPortfolioItem(${p.id})">Remover</button>
                    </div>` : ''}
            </div>
        </div>
    `).join('');
}

function renderizarExperiencias(lista, ehProprietario) {
    if (!lista?.length) return '<span class="empty-state">Nenhuma experiência cadastrada.</span>';
    return lista.map(e => {
        const inicio = formatarData(e.dataInicio);
        const fim = e.empregoAtual ? 'Atualmente' : formatarData(e.dataFim);
        return `
        <div class="timeline-item" id="exp-${e.id}">
            <div class="timeline-item__info">
                <p class="timeline-item__cargo">${escHtml(e.cargo)}</p>
                <p class="timeline-item__empresa">${escHtml(e.empresa)}${e.local ? ` · ${escHtml(e.local)}` : ''}</p>
                <p class="timeline-item__periodo">${inicio}${fim ? ` → ${fim}` : ''}${e.tipoContrato ? ` · ${escHtml(e.tipoContrato)}` : ''}</p>
                ${e.atividades ? `<p class="timeline-item__desc">${escHtml(e.atividades)}</p>` : ''}
                ${renderizarBadgesCompetencias(e.competencias)}
                <div id="comp-mgr-exp-${e.id}"></div>
            </div>
            ${ehProprietario ? `
                <div class="timeline-item__actions">
                    <button class="btn-icon" onclick="editarExperiencia(${e.id})">Editar</button>
                    <button class="btn-icon btn-icon--secondary" onclick="toggleGerenciarCompetencias('exp', ${e.id})">Competências</button>
                    <button class="btn-icon btn-icon--danger" onclick="removerExperiencia(${e.id})">Remover</button>
                </div>` : ''}
        </div>`;
    }).join('');
}

function renderizarCertificacoes(lista, ehProprietario) {
    if (!lista?.length) return '<span class="empty-state">Nenhuma certificação cadastrada.</span>';
    return lista.map(c => `
        <div class="timeline-item" id="cert-${c.id}">
            <div class="timeline-item__info">
                <p class="timeline-item__cargo">${escHtml(c.nome)}</p>
                <p class="timeline-item__empresa">${escHtml(c.instituicao)}</p>
                <p class="timeline-item__periodo">Emitido em ${formatarData(c.dataEmissao)}${c.dataValidade ? ` · Válido até ${formatarData(c.dataValidade)}` : ''}</p>
                ${c.linkVerificacao ? `<a href="${escHtml(c.linkVerificacao)}" target="_blank" rel="noopener" class="perfil-link" style="font-size:12px">Verificar certificado</a>` : ''}
                ${renderizarBadgesCompetencias(c.competencias)}
                <div id="comp-mgr-cert-${c.id}"></div>
            </div>
            ${ehProprietario ? `
                <div class="timeline-item__actions">
                    <button class="btn-icon" onclick="editarCertificacao(${c.id})">Editar</button>
                    <button class="btn-icon btn-icon--secondary" onclick="toggleGerenciarCompetencias('cert', ${c.id})">Competências</button>
                    <button class="btn-icon btn-icon--danger" onclick="removerCertificacao(${c.id})">Remover</button>
                </div>` : ''}
        </div>
    `).join('');
}

function sugerirProximoPasso(d) {
    if (!d.fotoUrl) return 'Adicione uma foto de perfil (+10%).';
    if (!d.tituloProfissional) return 'Preencha seu título profissional (+10%).';
    if ((d.bio ?? d.descricao ?? '').length < 100) return 'Escreva uma bio com pelo menos 100 caracteres (+15%).';
    if ((d.competencias?.length ?? 0) < 3) return 'Adicione pelo menos 3 competências (+15%).';
    if (!(d.portfolio?.length >= 1)) return 'Adicione um item ao portfólio (+20%).';
    if (!(d.experiencias?.length >= 1)) return 'Adicione uma experiência profissional (+15%).';
    if (!(d.certificacoes?.length >= 1)) return 'Adicione uma certificação (+10%).';
    if (!d.valorHora) return 'Defina sua taxa por hora (+5%).';
    return '';
}

// ── Competências ─────────────────────────────────────────────────────────────

function abrirFormCompetencia() {
    const wrap = document.getElementById('competencia-form-wrap');
    wrap.innerHTML = `
        <div class="form-inline">
            <p class="form-inline__titulo">Nova competência</p>
            <div class="form-row">
                <div class="form-group">
                    <label>Nome da competência</label>
                    <input type="text" id="comp-nome" placeholder="Ex: JavaScript" maxlength="80">
                </div>
                <div class="form-group" style="max-width:140px">
                    <label>Nível</label>
                    <select id="comp-nivel">
                        <option value="1">Básico</option>
                        <option value="2">Iniciante</option>
                        <option value="3" selected>Intermediário</option>
                        <option value="4">Avançado</option>
                        <option value="5">Especialista</option>
                    </select>
                </div>
            </div>
            <p id="comp-erro" class="form-erro" hidden></p>
            <div class="form-acoes">
                <button class="btn-cancelar" onclick="fecharFormCompetencia()">Cancelar</button>
                <button class="btn-salvar" id="btn-salvar-comp" onclick="salvarCompetencia()">Salvar</button>
            </div>
        </div>`;
}

function fecharFormCompetencia() {
    document.getElementById('competencia-form-wrap').innerHTML = '';
}

async function salvarCompetencia() {
    const nome = document.getElementById('comp-nome').value.trim();
    const nivel = parseInt(document.getElementById('comp-nivel').value);
    const btn = document.getElementById('btn-salvar-comp');
    const erro = document.getElementById('comp-erro');
    erro.hidden = true;

    if (!nome) { erro.textContent = 'Informe o nome da competência.'; erro.hidden = false; return; }

    const prestadorId = window._perfilDados?.id;
    btn.disabled = true; btn.textContent = 'Salvando...';

    try {
        await api.post(`/prestadores/${prestadorId}/competencias`, { nome, nivel });
        fecharFormCompetencia();
        await recarregar();
    } catch (e) {
        erro.textContent = e?.data?.mensagem ?? 'Erro ao salvar.';
        erro.hidden = false;
        btn.disabled = false; btn.textContent = 'Salvar';
    }
}

async function removerCompetencia(id) {
    if (!confirm('Remover esta competência?\n\nATENÇÃO: Esta competência será removida de todas as experiências, certificações e portfólios onde estiver vinculada.')) return;
    const prestadorId = window._perfilDados?.id;
    try {
        // A API já deve remover automaticamente os relacionamentos
        await api.delete(`/prestadores/${prestadorId}/competencias/${id}`);
        await recarregar();
    } catch { alert('Erro ao remover competência.'); }
}

// ── Portfólio ─────────────────────────────────────────────────────────────────

function abrirFormPortfolio(item) {
    const wrap = document.getElementById('portfolio-form-wrap');
    const edit = !!item;
    const descLen = (item?.descricao ?? '').length;
    wrap.innerHTML = `
        <div class="form-inline">
            <p class="form-inline__titulo">${edit ? 'Editar' : 'Novo'} item de portfólio</p>
            <div class="form-group">
                <label>Título <span style="color:#dc2626">*</span> <span style="font-size:11px;color:#94a3b8">(máx. 120 caracteres)</span></label>
                <input type="text" id="port-titulo" value="${escHtml(item?.titulo ?? '')}" maxlength="120" placeholder="Ex: Sistema de Agendamento">
                <span id="port-titulo-count" style="font-size:11px;color:#94a3b8">${(item?.titulo ?? '').length}/120</span>
            </div>
            <div class="form-group">
                <label>Descrição <span style="font-size:11px;color:#94a3b8">(máx. 500 caracteres)</span></label>
                <textarea id="port-desc" maxlength="500" placeholder="Descreva o projeto...">${escHtml(item?.descricao ?? '')}</textarea>
                <span id="port-desc-count" style="font-size:11px;color:#94a3b8">${descLen}/500</span>
            </div>
            <div class="form-group">
                <label>Link externo</label>
                <input type="url" id="port-link" value="${escHtml(item?.linkExterno ?? '')}" placeholder="https://github.com/...">
            </div>
            <div class="form-group">
                <label>Upload PDF (máx. 10 MB)</label>
                <input type="file" id="port-arquivo" accept=".pdf">
                ${item?.arquivoUrl ? `<a href="${escHtml(item.arquivoUrl)}" target="_blank" style="font-size:12px;color:#004584">Ver PDF atual</a>` : ''}
            </div>
            <p id="port-erro" class="form-erro" hidden></p>
            <div class="form-acoes">
                <button class="btn-cancelar" onclick="fecharFormPortfolio()">Cancelar</button>
                <button class="btn-salvar" id="btn-salvar-port" onclick="salvarPortfolio(${edit ? item.id : 'null'}, ${edit ? `'${item.arquivoUrl ?? ''}'` : 'null'})">Salvar</button>
            </div>
        </div>`;

    // Contadores de caracteres
    document.getElementById('port-titulo').addEventListener('input', function () {
        document.getElementById('port-titulo-count').textContent = `${this.value.length}/120`;
    });
    document.getElementById('port-desc').addEventListener('input', function () {
        document.getElementById('port-desc-count').textContent = `${this.value.length}/500`;
    });
}

function fecharFormPortfolio() {
    document.getElementById('portfolio-form-wrap').innerHTML = '';
}

function editarPortfolioItem(id) {
    const item = window._perfilDados?.portfolio?.find(p => p.id === id);
    if (item) abrirFormPortfolio(item);
}

async function salvarPortfolio(portfolioId, arquivoUrlExistente) {
    const titulo = document.getElementById('port-titulo').value.trim();
    const descricao = document.getElementById('port-desc').value.trim();
    const linkExterno = document.getElementById('port-link').value.trim();
    const arquivo = document.getElementById('port-arquivo').files[0];
    const btn = document.getElementById('btn-salvar-port');
    const erro = document.getElementById('port-erro');
    erro.hidden = true;

    if (!titulo) {
        erro.textContent = 'O título é obrigatório.';
        erro.hidden = false; return;
    }
    if (titulo.length > 120) {
        erro.textContent = 'Título deve ter no máximo 120 caracteres.';
        erro.hidden = false; return;
    }
    if (descricao.length > 500) {
        erro.textContent = 'Descrição deve ter no máximo 500 caracteres.';
        erro.hidden = false; return;
    }
    if (!linkExterno && !arquivo && !arquivoUrlExistente) {
        erro.textContent = 'Informe um link externo ou faça upload de um arquivo PDF.';
        erro.hidden = false; return;
    }
    // Validação de tamanho do PDF client-side (10 MB)
    if (arquivo && arquivo.size > 10 * 1024 * 1024) {
        erro.textContent = 'O arquivo PDF deve ter no máximo 10 MB.';
        erro.hidden = false; return;
    }

    btn.disabled = true; btn.textContent = 'Salvando...';

    const form = new FormData();
    form.append('titulo', titulo);
    if (descricao) form.append('descricao', descricao);
    if (linkExterno) form.append('linkExterno', linkExterno);
    if (arquivo) form.append('arquivo', arquivo);
    if (arquivoUrlExistente && !arquivo) form.append('arquivoUrlExistente', arquivoUrlExistente);

    try {
        if (portfolioId) {
            await api.uploadPut(`/prestadores/me/portfolio/${portfolioId}`, form);
        } else {
            await api.uploadPost('/prestadores/me/portfolio', form);
        }
        fecharFormPortfolio();
        await recarregar();
    } catch (e) {
        erro.textContent = e?.data?.mensagem ?? 'Erro ao salvar.';
        erro.hidden = false;
        btn.disabled = false; btn.textContent = 'Salvar';
    }
}

async function removerPortfolioItem(id) {
    if (!confirm('Remover este item do portfólio?\n\nATENÇÃO: Todos os relacionamentos de competências deste item serão removidos.')) return;
    try {
        await api.delete(`/prestadores/me/portfolio/${id}`);
        await recarregar();
    } catch { alert('Erro ao remover.'); }
}

// ── Experiências ─────────────────────────────────────────────────────────────

function abrirFormExperiencia(item) {
    const wrap = document.getElementById('experiencia-form-wrap');
    const edit = !!item;
    wrap.innerHTML = `
        <div class="form-inline">
            <p class="form-inline__titulo">${edit ? 'Editar' : 'Nova'} experiência</p>
            <div class="form-row">
                <div class="form-group">
                    <label>Cargo *</label>
                    <input type="text" id="exp-cargo" value="${escHtml(item?.cargo ?? '')}" placeholder="Ex: Desenvolvedor Full-Stack">
                </div>
                <div class="form-group">
                    <label>Empresa *</label>
                    <input type="text" id="exp-empresa" value="${escHtml(item?.empresa ?? '')}" placeholder="Ex: Google">
                </div>
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>Início *</label>
                    <input type="date" id="exp-inicio" value="${item?.dataInicio ? item.dataInicio.split('T')[0] : ''}">
                </div>
                <div class="form-group">
                    <label>Término</label>
                    <input type="date" id="exp-fim" value="${item?.dataFim ? item.dataFim.split('T')[0] : ''}" ${item?.empregoAtual ? 'disabled' : ''}>
                </div>
            </div>
            <div class="form-group">
                <label style="flex-direction:row;align-items:center;gap:6px">
                    <input type="checkbox" id="exp-atual" ${item?.empregoAtual ? 'checked' : ''} onchange="toggleEmpregoAtual()">
                    Emprego atual
                </label>
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>Local</label>
                    <input type="text" id="exp-local" value="${escHtml(item?.local ?? '')}" placeholder="Ex: Remoto / São Paulo, SP">
                </div>
                <div class="form-group">
                    <label>Tipo de contrato</label>
                    <select id="exp-contrato">
                        ${['', 'CLT', 'PJ', 'Freelance', 'Estágio', 'Temporário'].map(t =>
        `<option value="${t}" ${item?.tipoContrato === t ? 'selected' : ''}>${t || '— selecione —'}</option>`
    ).join('')}
                    </select>
                </div>
            </div>
            <div class="form-group">
                <label>Atividades / Descrição</label>
                <textarea id="exp-atividades" placeholder="Descreva suas principais responsabilidades...">${escHtml(item?.atividades ?? '')}</textarea>
            </div>
            <p id="exp-erro" class="form-erro" hidden></p>
            <div class="form-acoes">
                <button class="btn-cancelar" onclick="fecharFormExperiencia()">Cancelar</button>
                <button class="btn-salvar" id="btn-salvar-exp" onclick="salvarExperiencia(${edit ? item.id : 'null'})">Salvar</button>
            </div>
        </div>`;
}

function fecharFormExperiencia() {
    document.getElementById('experiencia-form-wrap').innerHTML = '';
}

function toggleEmpregoAtual() {
    const check = document.getElementById('exp-atual');
    const fim = document.getElementById('exp-fim');
    fim.disabled = check.checked;
    if (check.checked) fim.value = '';
}

function editarExperiencia(id) {
    const item = window._perfilDados?.experiencias?.find(e => e.id === id);
    if (item) abrirFormExperiencia(item);
}

async function salvarExperiencia(expId) {
    const cargo = document.getElementById('exp-cargo').value.trim();
    const empresa = document.getElementById('exp-empresa').value.trim();
    const dataInicio = document.getElementById('exp-inicio').value;
    const dataFim = document.getElementById('exp-fim').value || null;
    const empregoAtual = document.getElementById('exp-atual').checked;
    const local = document.getElementById('exp-local').value.trim();
    const tipoContrato = document.getElementById('exp-contrato').value || null;
    const atividades = document.getElementById('exp-atividades').value.trim();
    const btn = document.getElementById('btn-salvar-exp');
    const erro = document.getElementById('exp-erro');
    erro.hidden = true;

    if (!cargo || !empresa || !dataInicio) {
        erro.textContent = 'Cargo, empresa e data de início são obrigatórios.';
        erro.hidden = false;
        return;
    }

    btn.disabled = true; btn.textContent = 'Salvando...';

    const body = { cargo, empresa, dataInicio, dataFim, empregoAtual, local: local || null, tipoContrato, atividades: atividades || null };

    try {
        if (expId) {
            await api.put(`/prestadores/me/experiencias/${expId}`, body);
        } else {
            await api.post('/prestadores/me/experiencias', body);
        }
        fecharFormExperiencia();
        await recarregar();
    } catch (e) {
        erro.textContent = e?.data?.mensagem ?? 'Erro ao salvar.';
        erro.hidden = false;
        btn.disabled = false; btn.textContent = 'Salvar';
    }
}

async function removerExperiencia(id) {
    if (!confirm('Remover esta experiência?\n\nATENÇÃO: Todos os relacionamentos de competências desta experiência serão removidos.')) return;
    try {
        await api.delete(`/prestadores/me/experiencias/${id}`);
        await recarregar();
    } catch { alert('Erro ao remover.'); }
}

// ── Certificações ─────────────────────────────────────────────────────────────

function abrirFormCertificacao(item) {
    const wrap = document.getElementById('certificacao-form-wrap');
    const edit = !!item;
    wrap.innerHTML = `
        <div class="form-inline">
            <p class="form-inline__titulo">${edit ? 'Editar' : 'Nova'} certificação</p>
            <div class="form-group">
                <label>Nome da certificação *</label>
                <input type="text" id="cert-nome" value="${escHtml(item?.nome ?? '')}" placeholder="Ex: AWS Certified Developer">
            </div>
            <div class="form-group">
                <label>Instituição *</label>
                <input type="text" id="cert-instituicao" value="${escHtml(item?.instituicao ?? '')}" placeholder="Ex: Amazon Web Services">
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>Data de emissão *</label>
                    <input type="date" id="cert-emissao" value="${item?.dataEmissao ? item.dataEmissao.split('T')[0] : ''}">
                </div>
                <div class="form-group">
                    <label>Validade (opcional)</label>
                    <input type="date" id="cert-validade" value="${item?.dataValidade ? item.dataValidade.split('T')[0] : ''}">
                </div>
            </div>
            <div class="form-group">
                <label>Link de verificação</label>
                <input type="url" id="cert-link" value="${escHtml(item?.linkVerificacao ?? '')}" placeholder="https://...">
            </div>
            <p id="cert-erro" class="form-erro" hidden></p>
            <div class="form-acoes">
                <button class="btn-cancelar" onclick="fecharFormCertificacao()">Cancelar</button>
                <button class="btn-salvar" id="btn-salvar-cert" onclick="salvarCertificacao(${edit ? item.id : 'null'})">Salvar</button>
            </div>
        </div>`;
}

function fecharFormCertificacao() {
    document.getElementById('certificacao-form-wrap').innerHTML = '';
}

function editarCertificacao(id) {
    const item = window._perfilDados?.certificacoes?.find(c => c.id === id);
    if (item) abrirFormCertificacao(item);
}

async function salvarCertificacao(certId) {
    const nome = document.getElementById('cert-nome').value.trim();
    const instituicao = document.getElementById('cert-instituicao').value.trim();
    const dataEmissao = document.getElementById('cert-emissao').value;
    const dataValidade = document.getElementById('cert-validade').value || null;
    const linkVerificacao = document.getElementById('cert-link').value.trim() || null;
    const btn = document.getElementById('btn-salvar-cert');
    const erro = document.getElementById('cert-erro');
    erro.hidden = true;

    if (!nome || !instituicao || !dataEmissao) {
        erro.textContent = 'Nome, instituição e data de emissão são obrigatórios.';
        erro.hidden = false;
        return;
    }

    btn.disabled = true; btn.textContent = 'Salvando...';

    const body = { nome, instituicao, dataEmissao, dataValidade, linkVerificacao };

    try {
        if (certId) {
            await api.put(`/prestadores/me/certificacoes/${certId}`, body);
        } else {
            await api.post('/prestadores/me/certificacoes', body);
        }
        fecharFormCertificacao();
        await recarregar();
    } catch (e) {
        erro.textContent = e?.data?.mensagem ?? 'Erro ao salvar.';
        erro.hidden = false;
        btn.disabled = false; btn.textContent = 'Salvar';
    }
}

async function removerCertificacao(id) {
    if (!confirm('Remover esta certificação?\n\nATENÇÃO: Todos os relacionamentos de competências desta certificação serão removidos.')) return;
    try {
        await api.delete(`/prestadores/me/certificacoes/${id}`);
        await recarregar();
    } catch { alert('Erro ao remover.'); }
}

// ── Gerenciar competências por item ──────────────────────────────────────────
// tipo: 'exp' | 'cert' | 'port'

const _COMP_MGR_ROTAS = {
    exp: (itemId, compId) => ({ add: `/prestadores/me/experiencias/${itemId}/competencias/${compId}`, del: `/prestadores/me/experiencias/${itemId}/competencias/${compId}` }),
    cert: (itemId, compId) => ({ add: `/prestadores/me/certificacoes/${itemId}/competencias/${compId}`, del: `/prestadores/me/certificacoes/${itemId}/competencias/${compId}` }),
    port: (itemId, compId) => ({ add: `/prestadores/me/portfolio/${itemId}/competencias/${compId}`, del: `/prestadores/me/portfolio/${itemId}/competencias/${compId}` }),
};

const _COMP_MGR_FONTE = {
    exp: id => window._perfilDados?.experiencias?.find(e => e.id === id),
    cert: id => window._perfilDados?.certificacoes?.find(c => c.id === id),
    port: id => window._perfilDados?.portfolio?.find(p => p.id === id),
};

function toggleGerenciarCompetencias(tipo, itemId) {
    const mgrId = `comp-mgr-${tipo}-${itemId}`;
    const mgr = document.getElementById(mgrId);
    if (!mgr) return;
    if (mgr.innerHTML.trim()) { mgr.innerHTML = ''; return; }
    renderizarPainelCompetencias(tipo, itemId);
}

function renderizarPainelCompetencias(tipo, itemId) {
    const mgrId = `comp-mgr-${tipo}-${itemId}`;
    const mgr = document.getElementById(mgrId);
    if (!mgr) return;

    const item = _COMP_MGR_FONTE[tipo]?.(itemId);
    if (!item) return;

    const todasComps = window._perfilDados?.competencias ?? [];
    const vinculadas = item.competencias ?? [];
    const vinculadasIds = new Set(vinculadas.map(c => c.id));

    const disponiveisParaAdicionar = todasComps.filter(c => !vinculadasIds.has(c.id));

    const badgesHtml = vinculadas.length
        ? vinculadas.map(c => `
            <span class="comp-badge nivel-${c.nivel}" style="cursor:pointer" title="Clique para desvincular" onclick="desassociarCompetencia('${tipo}', ${itemId}, ${c.id}, this)">
                ${escHtml(c.nome)} ✕
            </span>`).join('')
        : '<span class="empty-state" style="font-size:12px;padding:0">Nenhuma competência vinculada.</span>';

    const selectHtml = disponiveisParaAdicionar.length
        ? `<div style="display:flex;gap:6px;align-items:center;flex-wrap:wrap;margin-top:8px">
            <select id="comp-add-select-${tipo}-${itemId}" class="competencia-select">
                <option value="">— selecionar competência —</option>
                ${disponiveisParaAdicionar.map(c => `<option value="${c.id}">${escHtml(c.nome)} (${nivelLabel(c.nivel)})</option>`).join('')}
            </select>
            <button class="btn-icon btn-icon--secondary" style="font-size:11px" onclick="associarCompetencia('${tipo}', ${itemId})">Vincular</button>
           </div>`
        : '<span style="font-size:12px;color:#94a3b8;margin-top:8px;display:block">✅ Todas as competências já estão vinculadas.</span>';

    mgr.innerHTML = `
        <div class="comp-mgr-painel">
            <p class="comp-mgr-painel__titulo">📌 Competências vinculadas</p>
            <div class="comp-badges" style="margin-bottom:4px">${badgesHtml}</div>
            ${selectHtml}
            <p id="comp-mgr-erro-${tipo}-${itemId}" class="form-erro" hidden></p>
        </div>`;
}

async function associarCompetencia(tipo, itemId) {
    const sel = document.getElementById(`comp-add-select-${tipo}-${itemId}`);
    const compId = parseInt(sel?.value);
    const erroEl = document.getElementById(`comp-mgr-erro-${tipo}-${itemId}`);
    if (erroEl) erroEl.hidden = true;

    if (!compId) {
        if (erroEl) { erroEl.textContent = 'Selecione uma competência.'; erroEl.hidden = false; }
        return;
    }

    const rota = _COMP_MGR_ROTAS[tipo]?.(itemId, compId);
    if (!rota) return;

    try {
        await api.post(rota.add, {});
        // Atualiza dados locais sem recarregar a página inteira
        const item = _COMP_MGR_FONTE[tipo]?.(itemId);
        const compOrigem = window._perfilDados?.competencias?.find(c => c.id === compId);
        if (item && compOrigem) {
            if (!item.competencias) item.competencias = [];
            if (!item.competencias.some(c => c.id === compId)) {
                item.competencias.push({ id: compOrigem.id, nome: compOrigem.nome, nivel: compOrigem.nivel });
            }
        }
        renderizarPainelCompetencias(tipo, itemId);
        _atualizarBadgesNaView(tipo, itemId);
    } catch (e) {
        if (erroEl) {
            erroEl.textContent = e?.data?.mensagem ?? 'Erro ao vincular competência.';
            erroEl.hidden = false;
            setTimeout(() => { if (erroEl) erroEl.hidden = true; }, 3000);
        }
    }
}

async function desassociarCompetencia(tipo, itemId, compId, badgeEl) {
    const rota = _COMP_MGR_ROTAS[tipo]?.(itemId, compId);
    if (!rota) return;

    try {
        await api.delete(rota.del);
        const item = _COMP_MGR_FONTE[tipo]?.(itemId);
        if (item) {
            item.competencias = (item.competencias ?? []).filter(c => c.id !== compId);
        }
        renderizarPainelCompetencias(tipo, itemId);
        _atualizarBadgesNaView(tipo, itemId);

        // Feedback visual suave
        if (badgeEl) {
            badgeEl.style.transform = 'scale(0.9)';
            setTimeout(() => {
                if (badgeEl) badgeEl.style.transform = '';
            }, 200);
        }
    } catch (e) {
        alert('Erro ao desvincular competência.');
    }
}

function _atualizarBadgesNaView(tipo, itemId) {
    const item = _COMP_MGR_FONTE[tipo]?.(itemId);
    if (!item) return;

    // Encontra o elemento pai do card para atualizar os badges estáticos acima do painel
    const prefixo = { exp: 'exp', cert: 'cert', port: 'port' }[tipo];
    const cardEl = document.getElementById(`${prefixo}-${itemId}`);
    if (!cardEl) return;

    // Atualiza os badges que precedem o painel
    const badgesExistente = cardEl.querySelector('.comp-badges');
    if (badgesExistente) {
        const novoHtml = renderizarBadgesCompetencias(item.competencias ?? []);
        const tmp = document.createElement('div');
        tmp.innerHTML = novoHtml;
        const novoBadges = tmp.querySelector('.comp-badges');
        if (novoBadges && novoBadges.children.length > 0) {
            badgesExistente.replaceWith(novoBadges);
        } else if (novoBadges) {
            badgesExistente.remove();
        }
    }
}

// ── Reputação ─────────────────────────────────────────────────────────────────

async function carregarReputacao(usuarioId) {
    const secao = document.getElementById('reputacao-secao');
    if (!secao) return;

    try {
        const [rep, avs] = await Promise.allSettled([
            api.get(`/api/usuarios/${usuarioId}/reputacao`),
            api.get(`/api/usuarios/${usuarioId}/avaliacoes?pagina=1&tamanho=5`)
        ]);

        const reputacao = rep.status === 'fulfilled' ? rep.value : null;
        const avsData = avs.status === 'fulfilled' ? avs.value : null;

        // Se perfil privado ou sem avaliações, nada a exibir
        if (!reputacao || reputacao.publica === false) return;
        if (avsData && avsData.avaliacoesVisiveis === false) return;

        const avaliacoes = avsData?.avaliacoes ?? (Array.isArray(avsData) ? avsData : []);

        const estrelasSVG = (nota) => {
            return [1, 2, 3, 4, 5].map(i =>
                `<span class="rep-estrela ${i <= nota ? 'rep-estrela--ativa' : ''}">★</span>`
            ).join('');
        };

        const barras = reputacao.distribuicao
            ? [5, 4, 3, 2, 1].map(n => {
                const cnt = reputacao.distribuicao[n] ?? 0;
                const pct = reputacao.totalAvaliacoes > 0
                    ? Math.round((cnt / reputacao.totalAvaliacoes) * 100) : 0;
                return `
                    <div class="rep-dist-row">
                        <span class="rep-dist-label">${n}★</span>
                        <div class="rep-dist-barra-wrap">
                            <div class="rep-dist-barra" style="width:${pct}%"></div>
                        </div>
                        <span class="rep-dist-cnt">${cnt}</span>
                    </div>`;
            }).join('')
            : '';

        const cardsAvaliacoes = Array.isArray(avaliacoes) && avaliacoes.length
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
                        const perfilUrl = av.avaliadorEhContratante
                            ? `/pages/contratante/perfil.html?id=${av.avaliadorId}`
                            : `/pages/prestador/perfil.html?id=${av.avaliadorId}`;
                        return `<a href="${perfilUrl}" class="rep-av-nome-link" title="Ver perfil">${escHtml(av.avaliadorNome ?? 'Anônimo')}</a>`;
                    })()
                    : `<span class="rep-av-nome">${escHtml(av.avaliadorNome ?? 'Anônimo')}</span>`
                }
                                ${av.projetoId
                    ? `<a href="../projetos/detalhe.html?id=${av.projetoId}" class="rep-av-projeto-link" title="Ver projeto"><span class="rep-av-projeto-icon">📁</span>${escHtml(av.projetoTitulo ?? 'Projeto')}</a>`
                    : av.projetoTitulo
                        ? `<span class="rep-av-projeto">${escHtml(av.projetoTitulo)}</span>`
                        : ''
                }
                            </div>
                        </div>
                        <div class="rep-av-estrelas">${estrelasSVG(av.nota)}</div>
                    </div>
                    ${av.comentario ? `<p class="rep-av-comentario">${escHtml(av.comentario)}</p>` : ''}
                    ${av.respostaPublica ? `
                        <div class="rep-av-resposta">
                            <span class="rep-av-resposta-label">Resposta do profissional</span>
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
                        <div class="rep-media-estrelas">${estrelasSVG(Math.round(reputacao.mediaGeral))}</div>
                        <span class="rep-media-total">${reputacao.totalAvaliacoes} avaliação${reputacao.totalAvaliacoes !== 1 ? 'ões' : ''}</span>
                    </div>
                    <div class="rep-dist">
                        ${barras}
                    </div>
                </div>
                <div class="rep-lista">
                    ${cardsAvaliacoes}
                </div>
            </div>`;
    } catch {
        // Falha silenciosa — reputação é complementar
    }
}

// ── Reload ────────────────────────────────────────────────────────────────────

async function recarregar() {
    const prestadorId = obterPrestadorId();
    await carregarPerfil(prestadorId);
}

// ── Init ──────────────────────────────────────────────────────────────────────
carregarPerfil(obterPrestadorId());