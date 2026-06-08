// ── Detalhe do projeto + proposta v2 + convites ─────────────────────────────

const root = () => document.getElementById('detalhe-root');

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

function dataISO(d) {
    return new Date(d).toISOString().split('T')[0];
}

const CATEGORIAS = {
    TI: 'Desenvolvimento de Software', Design: 'Design & UX/UI',
    Marketing: 'Marketing Digital', Redacao: 'Redação & Conteúdo',
    Video: 'Edição de Vídeo', Dados: 'Dados & BI', Traducao: 'Tradução',
    Suporte: 'Suporte & Assistência Virtual', Consultoria: 'Consultoria',
    Juridico: 'Jurídico', Outros: 'Outros'
};
const NIVEIS = { Junior: 'Júnior', Pleno: 'Pleno', Senior: 'Sênior' };
const IDIOMAS = { Portugues: 'Português', Ingles: 'Inglês', Espanhol: 'Espanhol' };

const STATUS_CONVITE_LABELS = {
    Pendente: 'Convite enviado',
    Aceito: 'Convite aceito',
    Recusado: 'Convite recusado',
    Expirado: 'Convite expirado'
};

let propostaAtualId = null;
let _matchTimer = null;
let _convitesProjeto = [];

// ────────────────────────────────────────────────────────────────────────────
// CARREGAMENTO
// ────────────────────────────────────────────────────────────────────────────
async function carregarDetalhe() {
    const id = new URLSearchParams(window.location.search).get('id');
    if (!id) { root().innerHTML = '<p class="msg-centro">ID do projeto não informado.</p>'; return; }

    root().innerHTML = '<div class="msg-centro">Carregando...</div>';

    let projeto;
    try { projeto = await api.get(`/api/projects/${id}`); }
    catch { root().innerHTML = '<p class="msg-centro erro">Projeto não encontrado.</p>'; return; }

    document.title = `${projeto.titulo} — Tratoo`;

    let usuarioId = null;
    let ehContratante = false;
    try {
        const me = await api.get('/api/me');
        usuarioId = me.id;
        ehContratante = usuarioId === projeto.contratanteId;
    } catch { }

    if (ehContratante) {
        try { _convitesProjeto = await api.get(`/api/projects/${id}/convites`); }
        catch { _convitesProjeto = []; }
    }

    renderDetalhe(projeto, ehContratante);
}

function renderDetalhe(p, ehContratante) {
    const tags = (p.habilidades || []).map(h => `<span class="tag">${esc(h)}</span>`).join('');
    const nivelInfo = p.nivelFreelancer
        ? `<span class="tag nivel">${esc(NIVEIS[p.nivelFreelancer] || p.nivelFreelancer)}</span>` : '';

    const layoutClass = ehContratante ? 'detalhe-layout contratante-view' : 'detalhe-layout';
    const linkVoltar = ehContratante ? '/pages/contratante/meus-projetos.html' : 'index.html';

    root().innerHTML = `
    <div class="detalhe-nav">
        <a href="${linkVoltar}"><i class="fa-solid fa-arrow-left"></i> Voltar aos projetos</a>
    </div>

    <div class="${layoutClass}">
        <main class="detalhe-main">
            <div class="detalhe-meta">
                <span class="tag categoria">${esc(CATEGORIAS[p.categoria] || p.categoria)}</span>
                ${nivelInfo}
                <span class="meta-info">Publicado em ${dataFmt(p.publicadoEm || p.criadoEm)}</span>
                <span class="meta-info">Idioma: ${esc(IDIOMAS[p.idioma] || p.idioma)}</span>
            </div>

            <h1>${esc(p.titulo)}</h1>
            <div class="detalhe-descricao">${esc(p.descricao)}</div>

            ${tags ? `<div class="detalhe-habilidades"><h4>Habilidades desejadas</h4><div class="projeto-tags">${tags}</div></div>` : ''}

            ${ehContratante ? `<div id="prestadores-recomendados" class="prestadores-recomendados-section"></div>` : ''}

            <div class="contratante-card">
                <h4>Sobre o contratante</h4>
                <div class="contratante-nome">
                    ${p.contratanteId
                        ? `<a href="/pages/contratante/perfil.html?id=${p.contratanteId}" class="link-perfil-contratante">${esc(p.contratanteNome)}</a>`
                        : esc(p.contratanteNome)
                    }${p.contratanteNovo ? ' <span class="badge-novo">Novo</span>' : ''}
                </div>
                ${p.contratanteNovo ? '<p class="hint">Primeira contratação na plataforma</p>' : ''}
            </div>
        </main>

        <aside class="proposta-panel" ${ehContratante ? 'style="display:none"' : ''}>
            <div class="orcamento-range">${moeda(p.orcamentoMin)} – ${moeda(p.orcamentoMax)}</div>
            <div class="prazo-info">Prazo: <strong>${dataFmt(p.prazoEntrega)}</strong></div>
            <div class="propostas-count">${p.totalPropostas} proposta${p.totalPropostas !== 1 ? 's' : ''} recebida${p.totalPropostas !== 1 ? 's' : ''}</div>

            <hr class="divider">

            <div id="area-proposta"></div>
        </aside>
    </div>

    <div id="modal-convite" class="modal-overlay" style="display:none">
        <div class="modal-card">
            <h3>Convidar prestador</h3>
            <div class="modal-projeto-info">
                <div class="label">Projeto</div>
                <div class="valor">${esc(p.titulo)}</div>
            </div>
            <div id="convite-prestador-info"></div>
            <div id="msg-convite"></div>
            <div class="modal-campo">
                <label>Mensagem inicial <span class="obr">*</span></label>
                <textarea id="convite-mensagem" rows="4" placeholder="Apresente o projeto e explique por que este prestador se encaixa (min. 10 caracteres)"></textarea>
            </div>
            <div class="modal-campo-row">
                <div class="modal-campo">
                    <label>Faixa de orçamento (R$)</label>
                    <input id="convite-orcamento" type="number" min="0" step="0.01" placeholder="Opcional">
                </div>
                <div class="modal-campo">
                    <label>Prazo esperado</label>
                    <input id="convite-prazo" type="date" min="${dataISO(new Date())}">
                </div>
            </div>
            <div class="btn-group" style="margin-top:16px">
                <button type="button" id="convite-cancelar" class="btn-secundario">Cancelar</button>
                <button type="button" id="convite-enviar" class="btn-enviar">Enviar convite</button>
            </div>
        </div>
    </div>`;

    iniciarAreaProposta(p);

    if (ehContratante) {
        carregarPrestadoresRecomendados(p.id);
        document.getElementById('convite-cancelar').addEventListener('click', fecharModalConvite);
        document.getElementById('modal-convite').addEventListener('click', (e) => {
            if (e.target.id === 'modal-convite') fecharModalConvite();
        });
    }
}

// ────────────────────────────────────────────────────────────────────────────
// ÁREA DE PROPOSTA (painel lateral)
// ────────────────────────────────────────────────────────────────────────────
async function iniciarAreaProposta(projeto) {
    const area = document.getElementById('area-proposta');

    if (projeto.status !== 'Aberto') {
        area.innerHTML = '<p class="hint center">Este projeto não está aceitando propostas.</p>';
        return;
    }

    let minhasPropostas = [];
    try { minhasPropostas = await api.get('/api/me/propostas'); } catch { }

    const propostaAtiva = minhasPropostas.find(pr =>
        pr.projetoId === projeto.id &&
        !['Recusada', 'Expirada', 'Convertida'].includes(pr.status));

    if (propostaAtiva) {
        propostaAtualId = propostaAtiva.id;
        renderPropostaExistente(area, propostaAtiva);
        return;
    }

    renderFormNovaProposta(area, projeto);
}

function renderPropostaExistente(area, proposta) {
    const badges = {
        Draft: 'Rascunho', Submitted: 'Aguardando análise',
        EmNegociacao: 'Em negociação', Aceita: 'Aceita'
    };
    area.innerHTML = `
    <h3>Sua proposta</h3>
    <div class="status-badge status-${proposta.status.toLowerCase()}">${badges[proposta.status] || proposta.status}</div>
    <div class="prazo-info" style="margin-top:8px">Valor: <strong>${moeda(proposta.valorTotal)}</strong></div>
    <div class="prazo-info">Prazo: <strong>${dataFmt(proposta.prazoTotal)}</strong></div>
    <a href="../../pages/proposta/detalhe.html?id=${proposta.id}" class="btn-enviar" style="display:block;text-align:center;margin-top:16px;text-decoration:none">
        Ver proposta completa
    </a>`;
}

function renderFormNovaProposta(area, projeto) {
    const amanha = new Date();
    amanha.setDate(amanha.getDate() + 1);
    const amanhaSt = dataISO(amanha);

    const em30Dias = new Date();
    em30Dias.setDate(em30Dias.getDate() + 30);
    const em30St = dataISO(em30Dias);

    area.innerHTML = `
    <h3>Criar proposta</h3>
    <div id="msg-proposta"></div>

    <form id="form-proposta" class="form-proposta">
        <div class="campo">
            <label>Objetivo <span class="obr">*</span></label>
            <input id="prop-objetivo" type="text" placeholder="Resumo do que você vai entregar (mín. 20 caracteres)" required>
            <span id="cnt-objetivo" class="char-counter">0/20</span>
        </div>
        <div class="campo">
            <label>Escopo detalhado <span class="obr">*</span></label>
            <textarea id="prop-escopo" rows="4" placeholder="Descreva o que está incluso no trabalho (mín. 50 caracteres)" required></textarea>
            <span id="cnt-escopo" class="char-counter">0/50</span>
        </div>
        <div class="campo">
            <label>O que NÃO está incluso</label>
            <input id="prop-exclusoes" type="text" placeholder="Opcional">
        </div>
        <div class="campo">
            <label>Revisões inclusas <span class="obr">*</span></label>
            <input id="prop-revisoes" type="number" min="1" value="2" required>
        </div>
        <div class="campo-row">
            <div class="campo">
                <label>Valor total (R$) <span class="obr">*</span></label>
                <input id="prop-valor" type="number" min="1" step="0.01" placeholder="Ex: 2500.00" required>
            </div>
            <div class="campo">
                <label>Entrada (R$)</label>
                <input id="prop-entrada" type="number" min="0" step="0.01" placeholder="Opcional">
            </div>
        </div>
        <div id="resumo-financeiro" class="resumo-financeiro" style="display:none"></div>
        <div class="campo">
            <label>Forma de pagamento</label>
            <select id="prop-pagamento">
                <option value="PIX">PIX</option>
                <option value="Transferência">Transferência</option>
                <option value="Boleto">Boleto</option>
            </select>
        </div>
        <div class="campo-row">
            <div class="campo">
                <label>Prazo de entrega <span class="obr">*</span></label>
                <input id="prop-prazo" type="date" min="${amanhaSt}" required>
            </div>
            <div class="campo">
                <label>Proposta válida até <span class="obr">*</span></label>
                <input id="prop-validade" type="date" min="${amanhaSt}" value="${em30St}" required>
            </div>
        </div>
        <div class="campo">
            <label>Observações</label>
            <textarea id="prop-obs" rows="2" placeholder="Opcional"></textarea>
        </div>

        <div class="btn-group">
            <button type="button" id="btn-salvar-rascunho" class="btn-secundario">Salvar rascunho</button>
            <button type="button" id="btn-preview" class="btn-enviar">Revisar e enviar</button>
        </div>
    </form>

    <div id="modal-preview" class="modal-overlay" style="display:none">
        <div class="modal-card" style="max-width:520px">
            <h3>Confirmar envio da proposta</h3>
            <div id="preview-conteudo"></div>
            <div class="btn-group" style="margin-top:16px">
                <button type="button" id="preview-cancelar" class="btn-secundario">Voltar e editar</button>
                <button type="button" id="preview-confirmar" class="btn-enviar">Confirmar envio</button>
            </div>
        </div>
    </div>`;

    const objInput = document.getElementById('prop-objetivo');
    const escInput = document.getElementById('prop-escopo');
    const cntObj = document.getElementById('cnt-objetivo');
    const cntEsc = document.getElementById('cnt-escopo');

    function atualizarContador(input, counter, min) {
        const len = input.value.trim().length;
        counter.textContent = `${len}/${min}`;
        counter.classList.toggle('valido', len >= min);
    }

    objInput.addEventListener('input', () => atualizarContador(objInput, cntObj, 20));
    escInput.addEventListener('input', () => atualizarContador(escInput, cntEsc, 50));

    const valInput = document.getElementById('prop-valor');
    const entInput = document.getElementById('prop-entrada');
    const resumoEl = document.getElementById('resumo-financeiro');

    function atualizarResumo() {
        const valor = parseFloat(valInput.value) || 0;
        const entrada = parseFloat(entInput.value) || 0;
        if (valor <= 0) { resumoEl.style.display = 'none'; return; }
        const restante = Math.max(0, valor - entrada);
        resumoEl.style.display = 'block';
        resumoEl.innerHTML = `
            <span>Valor: <strong>${moeda(valor)}</strong></span>
            ${entrada > 0 ? `<span>Entrada: <strong>${moeda(entrada)}</strong></span>
            <span>Restante: <strong>${moeda(restante)}</strong></span>` : ''}`;
    }

    valInput.addEventListener('input', atualizarResumo);
    entInput.addEventListener('input', atualizarResumo);

    document.getElementById('btn-preview').addEventListener('click', () => abrirPreview(projeto.id));
    document.getElementById('preview-cancelar').addEventListener('click', () => {
        document.getElementById('modal-preview').style.display = 'none';
    });
    document.getElementById('btn-salvar-rascunho').addEventListener('click', () => submitProposta(projeto.id, false));
}

function abrirPreview(projetoId) {
    const objetivo = document.getElementById('prop-objetivo').value.trim();
    const escopo = document.getElementById('prop-escopo').value.trim();
    const valor = parseFloat(document.getElementById('prop-valor').value) || 0;
    const entrada = parseFloat(document.getElementById('prop-entrada').value) || 0;
    const prazo = document.getElementById('prop-prazo').value;
    const revisoes = document.getElementById('prop-revisoes').value;
    const pagamento = document.getElementById('prop-pagamento').value;

    if (objetivo.length < 20 || escopo.length < 50 || valor <= 0 || !prazo) {
        const msg = document.getElementById('msg-proposta');
        msg.innerHTML = '<div class="msg-erro">Preencha todos os campos obrigatórios antes de revisar.</div>';
        return;
    }

    const restante = Math.max(0, valor - entrada);
    document.getElementById('preview-conteudo').innerHTML = `
        <div class="preview-item"><strong>Objetivo:</strong> ${esc(objetivo)}</div>
        <div class="preview-item"><strong>Escopo:</strong> ${esc(escopo.substring(0, 200))}${escopo.length > 200 ? '...' : ''}</div>
        <div class="preview-item"><strong>Valor:</strong> ${moeda(valor)}</div>
        ${entrada > 0 ? `<div class="preview-item"><strong>Entrada:</strong> ${moeda(entrada)} — Restante: ${moeda(restante)}</div>` : ''}
        <div class="preview-item"><strong>Prazo:</strong> ${dataFmt(prazo)}</div>
        <div class="preview-item"><strong>Revisões:</strong> ${revisoes}</div>
        <div class="preview-item"><strong>Pagamento:</strong> ${esc(pagamento)}</div>`;

    document.getElementById('modal-preview').style.display = 'flex';

    document.getElementById('preview-confirmar').onclick = () => {
        document.getElementById('modal-preview').style.display = 'none';
        submitProposta(projetoId, true);
    };
}

async function submitProposta(projetoId, enviarImediatamente) {
    const msg = document.getElementById('msg-proposta');
    const btnEnviar = document.getElementById('btn-preview');
    const btnRascunho = document.getElementById('btn-salvar-rascunho');

    btnEnviar.disabled = true;
    btnRascunho.disabled = true;
    msg.innerHTML = '';

    const payload = {
        objetivo: document.getElementById('prop-objetivo').value.trim(),
        escopo: document.getElementById('prop-escopo').value.trim(),
        exclusoes: document.getElementById('prop-exclusoes').value.trim() || null,
        revisoesInclusas: parseInt(document.getElementById('prop-revisoes').value),
        valorTotal: parseFloat(document.getElementById('prop-valor').value),
        entrada: parseFloat(document.getElementById('prop-entrada').value) || null,
        formaPagamento: document.getElementById('prop-pagamento').value,
        prazoTotal: new Date(document.getElementById('prop-prazo').value).toISOString(),
        validoAte: new Date(document.getElementById('prop-validade').value).toISOString(),
        observacoes: document.getElementById('prop-obs').value.trim() || null,
    };

    try {
        const proposta = await api.post(`/api/propostas?projetoId=${projetoId}`, payload);
        propostaAtualId = proposta.id;

        if (enviarImediatamente) {
            await api.post(`/api/propostas/${proposta.id}/enviar`, {});
            msg.innerHTML = '<div class="msg-sucesso">Proposta enviada! O contratante foi notificado.</div>';
        } else {
            msg.innerHTML = '<div class="msg-sucesso">Rascunho salvo com sucesso.</div>';
        }

        setTimeout(() => {
            window.location.href = `../../pages/proposta/detalhe.html?id=${propostaAtualId}`;
        }, 1500);

    } catch (err) {
        const texto = err?.data?.mensagem || 'Erro ao processar proposta. Tente novamente.';
        msg.innerHTML = `<div class="msg-erro">${esc(texto)}</div>`;
        btnEnviar.disabled = false;
        btnRascunho.disabled = false;
    }
}

// ────────────────────────────────────────────────────────────────────────────
// PRESTADORES RECOMENDADOS (RANKING para o contratante)
// ────────────────────────────────────────────────────────────────────────────

function obterNivelRecomendacaoPorScore(score) {
    if (score >= 0.75) return { texto: 'Altamente recomendado', classe: 'alta' };
    if (score >= 0.60) return { texto: 'Boa recomendação', classe: 'boa' };
    if (score >= 0.45) return { texto: 'Recomendação moderada', classe: 'moderada' };
    return { texto: 'Baixa recomendação', classe: 'baixa' };
}

function encontrarCompentenciasEmComum(prestadorComp, projetoHabilidades) {
    if (!prestadorComp?.length || !projetoHabilidades?.length) return [];
    const projetoHabilidadesLower = projetoHabilidades.map(h => String(h).toLowerCase());
    return prestadorComp.filter(c =>
        projetoHabilidadesLower.includes(String(c).toLowerCase())
    );
}

function obterConvitePrestador(prestadorId) {
    return _convitesProjeto.find(c => c.prestadorId === prestadorId) || null;
}

function renderConviteStatusBadge(convite) {
    if (!convite) return '';
    const label = STATUS_CONVITE_LABELS[convite.status] || convite.status;
    const classe = convite.status.toLowerCase();
    return `<span class="convite-status-modern ${classe}">${label}</span>`;
}

function renderBotaoConvite(prestadorId, projetoAberto) {
    const convite = obterConvitePrestador(prestadorId);

    if (convite) {
        return renderConviteStatusBadge(convite);
    }

    if (!projetoAberto) return '';

    return `<button class="btn-convidar-modern" data-prestador-id="${prestadorId}">Convidar para projeto</button>`;
}

function agendarCarregamentoPrestadores(projectId) {
    clearTimeout(_matchTimer);
    const el = document.getElementById('prestadores-recomendados');
    if (!el) return;
    _matchTimer = setTimeout(() => atualizarPrestadoresRecomendados(projectId), 900);
}

async function carregarPrestadoresRecomendados(projectId) {
    agendarCarregamentoPrestadores(projectId);
}

function renderEstrelas(reputacao) {
    const estrelaCheia = '<i class="fa-solid fa-star"></i>';
    const estrelaVazia = '<i class="fa-regular fa-star"></i>';
    const estrelasInteiras = Math.floor(reputacao);
    const temMeiaEstrela = reputacao % 1 >= 0.5;

    let html = '';
    for (let i = 0; i < estrelasInteiras; i++) {
        html += `<span class="star star-full">${estrelaCheia}</span>`;
    }
    if (temMeiaEstrela) {
        html += `<span class="star star-half">½</span>`;
    }
    const estrelasRestantes = 5 - Math.ceil(reputacao);
    for (let i = 0; i < estrelasRestantes; i++) {
        html += `<span class="star star-empty">${estrelaVazia}</span>`;
    }
    return html;
}

// Função renderPrestadorCard - RANKING POR SCORE COMPOSTO (competências + reputação + confiabilidade)
function renderPrestadorCard(p, index, habilidadesProjeto, projetoAberto) {
    const posicao = index + 1;
    const nomePrestador = esc(p.nome || 'Sem nome');
    const tituloProfissional = esc(p.tituloProfissional || '');

    // Score composto que considera competências, experiência, reputação e confiabilidade
    const scoreParaRecomendacao = p.scoreComposto || p.similaridade || 0;
    const recomendacao = obterNivelRecomendacaoPorScore(scoreParaRecomendacao);

    // Competências em comum - usar competenciasMatchadas se disponível
    let compEmComum = [];
    if (p.competenciasMatchadas && p.competenciasMatchadas.length > 0) {
        compEmComum = p.competenciasMatchadas;
    } else {
        const compPrestador = Array.isArray(p.competencias) ? p.competencias : [];
        compEmComum = encontrarCompentenciasEmComum(compPrestador, habilidadesProjeto);
    }

    const recomendacaoIcons = {
        alta: '<i class="fa-solid fa-trophy"></i>',
        boa: '<i class="fa-solid fa-arrow-trend-up"></i>',
        moderada: '<i class="fa-solid fa-chart-column"></i>',
        baixa: '<i class="fa-solid fa-arrow-trend-down"></i>'
    };

    const bioPreview = p.bio
        ? esc(p.bio.substring(0, 180)) + (p.bio.length > 180 ? '...' : '')
        : 'Este profissional ainda não adicionou uma descrição.';

    const reputacao = p.mediaAvaliacoes || 0;
    const avaliacao = p.totalAvaliacoes || 0;
    const botaoConvite = renderBotaoConvite(p.id, projetoAberto);
    const estrelas = renderEstrelas(reputacao);
    const percentualMatch = Math.round(scoreParaRecomendacao * 100);

    // Badge de habilidades compatíveis (se tiver competências matchadas)
    const stackBadge = compEmComum.length > 0 ? `
        <div class="habilidade-match-badge">
            <span class="habilidade-icon"><i class="fa-solid fa-circle-check"></i></span>
            Habilidades compatíveis
        </div>
    ` : '';

    return `
        <div class="prestador-card-modern" data-prestador-id="${p.id}">
            <div class="prestador-card-gradient"></div>
            
            <div class="prestador-card-content">
                <div class="prestador-card-header">
                    <div class="prestador-ranking-area">
                        <div class="prestador-posicao-modern">
                            <div class="posicao-trofeu ${recomendacao.classe}">
                                <span class="trofeu-icon">${recomendacaoIcons[recomendacao.classe]}</span>
                                <span class="posicao-numero">#${posicao}</span>
                            </div>
                            <div class="match-badge-modern ${recomendacao.classe}">
                                <span class="match-dot"></span>
                                ${recomendacao.texto}
                            </div>
                            ${stackBadge}
                        </div>
                        <div class="match-percentual-modern">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <circle cx="12" cy="12" r="10"/>
                                <path d="M12 2a15 15 0 0 1 0 20 15 15 0 0 1 0-20z"/>
                                <path d="M12 2a15 15 0 0 0 0 20 15 15 0 0 0 0-20z"/>
                                <line x1="12" y1="2" x2="12" y2="22"/>
                            </svg>
                            <span>${percentualMatch}%</span>
                            <small>match</small>
                        </div>
                    </div>
                </div>

                <div class="prestador-info-modern">
                    <div class="prestador-avatar-modern">
                        <div class="avatar-initials">
                            ${nomePrestador.charAt(0).toUpperCase()}
                        </div>
                        ${reputacao >= 4.5 ? '<div class="avatar-verified"><i class="fa-solid fa-check"></i></div>' : ''}
                    </div>
                    <div class="prestador-details">
                        <h4 class="prestador-name">
                            <a href="/pages/prestador/perfil.html?id=${p.id}" target="_blank" class="prestador-link-modern">
                                ${nomePrestador}
                            </a>
                        </h4>
                        ${tituloProfissional ? `<p class="prestador-title">${tituloProfissional}</p>` : ''}
                        <div class="prestador-stats">
                            <div class="stat-item">
                                <span class="stat-icon"><i class="fa-solid fa-star"></i></span>
                                <span class="stat-value">${reputacao.toFixed(1)}</span>
                                <div class="stat-stars">${estrelas}</div>
                            </div>
                            ${avaliacao > 0 ? `
                            <div class="stat-item">
                                <span class="stat-icon"><i class="fa-solid fa-pen-to-square"></i></span>
                                <span class="stat-value">${avaliacao}</span>
                                <span class="stat-label">avaliação${avaliacao !== 1 ? 'es' : ''}</span>
                            </div>` : ''}
                            <div class="stat-item">
                                <span class="stat-icon"><i class="fa-solid fa-bullseye"></i></span>
                                <span class="stat-value">${p.contratosEncerrados || 0}</span>
                                <span class="stat-label">projetos</span>
                            </div>
                        </div>
                    </div>
                </div>

                ${compEmComum.length > 0 ? `
                <div class="competencias-comum-modern">
                    <div class="competencias-header">
                        <span class="competencias-icon"><i class="fa-solid fa-bullseye"></i></span>
                        <span class="competencias-title">Competências que combinam</span>
                        <span class="competencias-count">${compEmComum.length}</span>
                    </div>
                    <div class="competencias-tags-modern">
                        ${compEmComum.map(c => `
                            <span class="tag-match-modern">
                                <span class="tag-check"><i class="fa-solid fa-check"></i></span>
                                ${esc(c)}
                            </span>
                        `).join('')}
                    </div>
                </div>
                ` : ''}

                <div class="prestador-bio-modern">
                    <div class="bio-icon"><i class="fa-solid fa-comments"></i></div>
                    <p class="bio-text">${bioPreview}</p>
                </div>

                <div class="prestador-acoes-modern">
                    <a href="/pages/prestador/perfil.html?id=${p.id}" target="_blank" class="btn-ver-perfil-modern">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/>
                            <circle cx="12" cy="12" r="3"/>
                        </svg>
                        Ver perfil completo
                    </a>
                    ${botaoConvite}
                </div>
            </div>
        </div>
    `;
}

async function atualizarPrestadoresRecomendados(projectId) {
    const el = document.getElementById('prestadores-recomendados');
    if (!el) return;

    el.innerHTML = '<div class="match-loading-modern"><div class="match-loading-spinner"></div><div class="match-loading-text">Carregando ranking de recomendação...</div></div>';

    try {
        const projeto = await api.get(`/api/projects/${projectId}`);
        const habilidadesProjeto = projeto.habilidades || [];
        const projetoAberto = projeto.status === 'Aberto';

        const resultado = await api.get(`/api/busca/projetos/${projectId}/prestadores-recomendados`);

        const prestadores = Array.isArray(resultado)
            ? resultado.filter(p => p.similaridade && p.similaridade >= 0.45)
            : [];

        if (prestadores.length === 0) {
            el.innerHTML = `
                <div class="match-empty-modern">
                    <div class="match-empty-icon"><i class="fa-solid fa-magnifying-glass"></i></div>
                    <div class="match-empty-title">Nenhum prestador encontrado</div>
                    <div class="match-empty-text">Nenhum prestador com perfil relevante foi encontrado para este projeto.</div>
                    <div class="match-empty-hint">Tente ajustar as habilidades ou a descrição do projeto.</div>
                </div>`;
            return;
        }

        // Ordenar por scoreComposto (decrescente) - considerando reputação e confiabilidade
        prestadores.sort((a, b) => (b.scoreComposto || b.similaridade || 0) - (a.scoreComposto || a.similaridade || 0));

        const listaHtml = prestadores.map((p, index) =>
            renderPrestadorCard(p, index, habilidadesProjeto, projetoAberto)
        ).join('');

        const rankingMsg = prestadores.length === 1
            ? 'Ranking automático com base nas informações do projeto, reputação e histórico do prestador'
            : `Ranking automático - ${prestadores.length} prestador${prestadores.length !== 1 ? 'es' : ''} ordenados por recomendação`;

        el.innerHTML = `
            <div class="prestadores-header">
                <h3>Prestadores Mais Recomendados</h3>
                <p class="prestadores-subtitle">${rankingMsg}</p>
                <p class="prestadores-hint">Recomendação baseada em competências, experiência, reputação (avaliações anteriores), confiabilidade do perfil e compatibilidade com a descrição do projeto</p>
            </div>
            <div class="prestadores-list">
                ${listaHtml}
            </div>
        `;

        el.querySelectorAll('.btn-convidar-modern').forEach(btn => {
            btn.addEventListener('click', () => {
                const prestadorId = parseInt(btn.dataset.prestadorId);
                const prest = prestadores.find(p => p.id === prestadorId);
                if (prest) abrirModalConvite(projectId, prest);
            });
        });

    } catch (err) {
        console.error('Erro ao carregar ranking:', err);
        el.innerHTML = `
            <div class="match-empty-modern">
                <div class="match-empty-icon"><i class="fa-solid fa-triangle-exclamation"></i></div>
                <div class="match-empty-title">Erro ao carregar</div>
                <div class="match-empty-text">Não foi possível carregar o ranking de recomendação.</div>
                <div class="match-empty-hint">Tente novamente mais tarde.</div>
            </div>`;
    }
}

// ────────────────────────────────────────────────────────────────────────────
// CONVITE PARA PROJETO (modal + envio)
// ────────────────────────────────────────────────────────────────────────────

let _convitePrestadorAlvo = null;
let _conviteProjetoId = null;

function abrirModalConvite(projetoId, prestador) {
    _convitePrestadorAlvo = prestador;
    _conviteProjetoId = projetoId;

    const infoEl = document.getElementById('convite-prestador-info');
    infoEl.innerHTML = `
        <div class="modal-projeto-info" style="margin-bottom:16px">
            <div class="label">Prestador</div>
            <div class="valor">${esc(prestador.nome || 'Sem nome')}</div>
            ${prestador.tituloProfissional ? `<div style="font-size:0.85rem;color:var(--text-secondary);margin-top:2px">${esc(prestador.tituloProfissional)}</div>` : ''}
        </div>`;

    document.getElementById('convite-mensagem').value = '';
    document.getElementById('convite-orcamento').value = '';
    document.getElementById('convite-prazo').value = '';
    document.getElementById('msg-convite').innerHTML = '';
    document.getElementById('convite-enviar').disabled = false;

    document.getElementById('convite-enviar').onclick = enviarConvite;
    document.getElementById('modal-convite').style.display = 'flex';
}

function fecharModalConvite() {
    document.getElementById('modal-convite').style.display = 'none';
    _convitePrestadorAlvo = null;
    _conviteProjetoId = null;
}

async function enviarConvite() {
    const msg = document.getElementById('msg-convite');
    const btn = document.getElementById('convite-enviar');

    const mensagem = document.getElementById('convite-mensagem').value.trim();
    const orcamento = parseFloat(document.getElementById('convite-orcamento').value) || null;
    const prazo = document.getElementById('convite-prazo').value || null;

    if (mensagem.length < 10) {
        msg.innerHTML = '<div class="msg-erro">A mensagem deve ter pelo menos 10 caracteres.</div>';
        return;
    }

    btn.disabled = true;
    msg.innerHTML = '';

    const payload = {
        prestadorId: _convitePrestadorAlvo.id,
        mensagemInicial: mensagem,
        orcamentoSugerido: orcamento,
        prazoDesejado: prazo ? new Date(prazo).toISOString() : null
    };

    try {
        const convite = await api.post(`/api/projects/${_conviteProjetoId}/convites`, payload);
        _convitesProjeto.push(convite);

        msg.innerHTML = '<div class="msg-sucesso">Convite enviado com sucesso! O prestador foi notificado.</div>';

        const card = document.querySelector(`.prestador-card-modern[data-prestador-id="${_convitePrestadorAlvo.id}"]`);
        if (card) {
            const acoesEl = card.querySelector('.prestador-acoes-modern');
            const btnConvidar = acoesEl.querySelector('.btn-convidar-modern');
            if (btnConvidar) {
                btnConvidar.outerHTML = renderConviteStatusBadge(convite);
            }
        }

        setTimeout(fecharModalConvite, 1500);

    } catch (err) {
        const texto = err?.data?.mensagem || 'Erro ao enviar convite. Tente novamente.';
        msg.innerHTML = `<div class="msg-erro">${esc(texto)}</div>`;
        btn.disabled = false;
    }
}

// Inicia
carregarDetalhe();