// Página: /pages/contrato/detalhe.html?id={guid}
// Design Moderno para Detalhe do Contrato

const params = new URLSearchParams(location.search);
const contratoId = params.get('id');
const root = document.getElementById('contrato-root');

let contratoAtual = null;
let meId = null;

const STATUS_LABEL = {
    Ativo: 'Em vigor',
    Encerrado: 'Encerrado',
    Cancelado: 'Cancelado / Expirado'
};

const STATUS_CLASS = {
    Ativo: 'status-ativo',
    Encerrado: 'status-encerrado',
    Cancelado: 'status-cancelado'
};

// Resolve label e classe do badge de status de forma contextual ao usuário atual.
// Para contratos ainda em fase de assinatura, o texto reflete a perspectiva de quem assina.
function resolverStatusBadge(c) {
    if (c.status === 'Ativo' || c.status === 'Encerrado' || c.status === 'Cancelado') {
        return { label: STATUS_LABEL[c.status] || c.status, cssClass: STATUS_CLASS[c.status] || '' };
    }

    // Contrato pendente de assinaturas (Gerado ou AguardandoAssinatura)
    if (!c.assinadoPorMim) {
        return { label: 'Aguardando sua assinatura', cssClass: 'status-aguardando' };
    }

    return { label: 'Aguardando assinatura da outra parte', cssClass: 'status-pendente' };
}

async function init() {
    if (!contratoId) {
        root.innerHTML = '<div class="error-state"><span class="error-icon">⚠️</span><p class="erro-msg">ID do contrato não informado.</p></div>';
        return;
    }

    try {
        const me = await api.get('/api/me');
        meId = me.id;

        const contrato = await api.get(`/api/contratos/${contratoId}`);
        contratoAtual = contrato;
        renderContrato(contrato);
        configurarModal();
    } catch (err) {
        if (err?.status === 401) {
            location.href = '/pages/auth/login.html';
        } else {
            root.innerHTML = `<div class="error-state"><span class="error-icon">❌</span><p class="erro-msg">${err?.data?.mensagem || 'Erro ao carregar contrato.'}</p></div>`;
        }
    }
}

function renderContrato(c) {
    const conteudo = c.conteudo || {};
    const pagamento = conteudo.pagamento || {};
    const prazo = conteudo.prazo || {};
    const escopo = conteudo.escopo || {};
    const contratanteParte = conteudo.contratante || {};
    const prestadorParte = conteudo.prestador || {};

    const expiraEm = new Date(c.expiraEm).toLocaleDateString('pt-BR');
    const dataTermino = prazo.dataTermino ? new Date(prazo.dataTermino).toLocaleDateString('pt-BR') : '—';
    const valorFormatado = pagamento.valorTotal
        ? pagamento.valorTotal.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })
        : '—';
    const entradaFormatada = pagamento.entrada
        ? pagamento.entrada.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })
        : '—';

    const assineiContratante = c.assinadoContratanteEm
        ? `<span class="assinado-icon">✓</span> ${new Date(c.assinadoContratanteEm).toLocaleDateString('pt-BR')}`
        : '<span class="pendente-icon">○</span> Pendente';
    const assineiPrestador = c.assinadoPrestadorEm
        ? `<span class="assinado-icon">✓</span> ${new Date(c.assinadoPrestadorEm).toLocaleDateString('pt-BR')}`
        : '<span class="pendente-icon">○</span> Pendente';

    const podAssinar = !c.assinadoPorMim &&
        (c.status === 'Gerado' || c.status === 'AguardandoAssinatura') &&
        new Date(c.expiraEm) > new Date();

    const statusBadge = resolverStatusBadge(c);

    root.innerHTML = `
        <div class="contrato-container">
            <!-- Header Premium -->
            <div class="contrato-header-premium">
                <div class="contrato-header-content">
                    <div class="contrato-breadcrumb">
                        <a href="/pages/me/contratos.html" class="breadcrumb-link">← Meus Contratos</a>
                    </div>
                    <div class="contrato-title-section">
                        <h1 class="contrato-title">Contrato de Prestação de Serviços</h1>
                        <p class="contrato-subtitle">${escapeHtml(c.projetoTitulo)}</p>
                    </div>
                    <div class="contrato-status-area">
                        <span class="status-badge-modern ${statusBadge.cssClass}">
                            <span class="status-dot"></span>
                            ${statusBadge.label}
                        </span>
                    </div>
                </div>
                <div class="contrato-meta-bar">
                    <div class="meta-item">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>
                            <line x1="16" y1="2" x2="16" y2="6"/>
                            <line x1="8" y1="2" x2="8" y2="6"/>
                            <line x1="3" y1="10" x2="21" y2="10"/>
                        </svg>
                        Criado em ${new Date(c.criadoEm).toLocaleDateString('pt-BR')}
                    </div>
                    <div class="meta-item">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <circle cx="12" cy="12" r="10"/>
                            <polyline points="12 6 12 12 16 14"/>
                        </svg>
                        Assinar até ${expiraEm}
                    </div>
                    ${c.conteudoHash ? `
                    <div class="meta-item hash-item" title="${c.conteudoHash}">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M4 22h16a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2H8.5L4 8.5V20a2 2 0 0 0 2 2z"/>
                            <polyline points="14 2 14 8 20 8"/>
                        </svg>
                        Hash Verificado
                    </div>` : ''}
                </div>
            </div>

            <!-- Grid de Partes -->
            <div class="partes-grid-modern">
                <div class="parte-card-modern">
                    <div class="parte-header">
                        <div class="parte-avatar contratante-avatar">👤</div>
                        <div class="parte-info">
                            <h3>Contratante</h3>
                            <p class="parte-nome">
                                ${c.contratanteId
                                    ? `<a href="/pages/contratante/perfil.html?id=${c.contratanteId}" class="link-perfil-contratante">${escapeHtml(contratanteParte.nome || c.contratanteNome)}</a>`
                                    : escapeHtml(contratanteParte.nome || c.contratanteNome)
                                }
                            </p>
                        </div>
                    </div>
                    <div class="parte-details">
                        ${contratanteParte.cpfCnpjMascarado ? `<div class="detail-row"><span class="detail-label">CPF/CNPJ:</span> ${escapeHtml(contratanteParte.cpfCnpjMascarado)}</div>` : ''}
                        ${contratanteParte.email ? `<div class="detail-row"><span class="detail-label">E-mail:</span> ${escapeHtml(contratanteParte.email)}</div>` : ''}
                        ${contratanteParte.endereco ? `<div class="detail-row"><span class="detail-label">Endereço:</span> ${escapeHtml(contratanteParte.endereco)}</div>` : ''}
                        <div class="detail-row assinatura-row ${c.assinadoContratanteEm ? 'assinado' : 'pendente'}">
                            <span class="detail-label">Assinatura:</span>
                            ${assineiContratante}
                        </div>
                    </div>
                </div>
                <div class="parte-card-modern">
                    <div class="parte-header">
                        <div class="parte-avatar prestador-avatar">🛠️</div>
                        <div class="parte-info">
                            <h3>Prestador</h3>
                            <p class="parte-nome">${escapeHtml(prestadorParte.nome || c.prestadorNome)}</p>
                        </div>
                    </div>
                    <div class="parte-details">
                        ${prestadorParte.cpfCnpjMascarado ? `<div class="detail-row"><span class="detail-label">CPF/CNPJ:</span> ${escapeHtml(prestadorParte.cpfCnpjMascarado)}</div>` : ''}
                        ${prestadorParte.email ? `<div class="detail-row"><span class="detail-label">E-mail:</span> ${escapeHtml(prestadorParte.email)}</div>` : ''}
                        ${prestadorParte.endereco ? `<div class="detail-row"><span class="detail-label">Endereço:</span> ${escapeHtml(prestadorParte.endereco)}</div>` : ''}
                        <div class="detail-row assinatura-row ${c.assinadoPrestadorEm ? 'assinado' : 'pendente'}">
                            <span class="detail-label">Assinatura:</span>
                            ${assineiPrestador}
                        </div>
                    </div>
                </div>
            </div>

            <!-- Objeto do Contrato -->
            <div class="contrato-card-modern">
                <div class="card-header">
                    <div class="card-icon">🎯</div>
                    <h2>Objeto do Contrato</h2>
                </div>
                <div class="card-content">
                    <p class="objeto-text">${escapeHtml(conteudo.objeto || '')}</p>
                </div>
            </div>

            <!-- Escopo -->
            <div class="contrato-card-modern">
                <div class="card-header">
                    <div class="card-icon">📋</div>
                    <h2>Escopo do Trabalho</h2>
                </div>
                <div class="card-content">
                    <div class="escopo-grid">
                        <div class="escopo-item">
                            <label>Entregáveis</label>
                            <p class="escopo-value">${escapeHtml(escopo.entregaveis || '')}</p>
                        </div>
                        <div class="escopo-item">
                            <label>Revisões inclusas</label>
                            <p class="escopo-value"><strong>${escopo.revisoes ?? 0}</strong> revisões inclusas</p>
                        </div>
                        <div class="escopo-item">
                            <label>Formato de entrega</label>
                            <p class="escopo-value">${escapeHtml(escopo.formatoEntrega || '')}</p>
                        </div>
                        <div class="escopo-item">
                            <label>O que NÃO está incluso</label>
                            <p class="escopo-value exclusao">${escapeHtml(escopo.oQueNaoEstaIncluso || 'Nenhuma exclusão especificada')}</p>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Prazo e Pagamento Grid -->
            <div class="two-columns-grid">
                <!-- Prazo -->
                <div class="contrato-card-modern">
                    <div class="card-header">
                        <div class="card-icon">📅</div>
                        <h2>Prazo</h2>
                    </div>
                    <div class="card-content">
                        <div class="prazo-timeline">
                            <div class="timeline-item">
                                <div class="timeline-marker start"></div>
                                <div class="timeline-content">
                                    <span class="timeline-label">Início</span>
                                    <span class="timeline-value">${prazo.dataInicio ? new Date(prazo.dataInicio).toLocaleDateString('pt-BR') : '—'}</span>
                                </div>
                            </div>
                            <div class="timeline-connector"></div>
                            <div class="timeline-item">
                                <div class="timeline-marker end"></div>
                                <div class="timeline-content">
                                    <span class="timeline-label">Término</span>
                                    <span class="timeline-value">${dataTermino}</span>
                                </div>
                            </div>
                        </div>
                        ${prazo.marcos && prazo.marcos.length > 0 ? `
                        <div class="marcos-section">
                            <label>Marcos do Projeto</label>
                            <div class="marcos-list">
                                ${prazo.marcos.map((m, idx) => `
                                    <div class="marco-item">
                                        <div class="marco-number">${idx + 1}</div>
                                        <div class="marco-details">
                                            <div class="marco-desc">${escapeHtml(m.descricao || '')}</div>
                                            <div class="marco-meta">
                                                <span>📅 ${m.prazo ? new Date(m.prazo).toLocaleDateString('pt-BR') : '—'}</span>
                                                <span>💰 ${m.valor?.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) || '—'}</span>
                                            </div>
                                        </div>
                                    </div>
                                `).join('')}
                            </div>
                        </div>` : ''}
                    </div>
                </div>

                <!-- Pagamento -->
                <div class="contrato-card-modern">
                    <div class="card-header">
                        <div class="card-icon">💰</div>
                        <h2>Pagamento</h2>
                    </div>
                    <div class="card-content">
                        <div class="valor-total-card">
                            <span class="valor-label">Valor Total</span>
                            <span class="valor-number">${valorFormatado}</span>
                        </div>
                        ${pagamento.entrada ? `
                        <div class="valor-detail-row">
                            <span>Entrada</span>
                            <strong>${entradaFormatada}</strong>
                        </div>` : ''}
                        <div class="valor-detail-row">
                            <span>Forma de Pagamento</span>
                            <strong>${escapeHtml(pagamento.formaPagamento || 'PIX')}</strong>
                        </div>
                        <div class="valor-detail-row">
                            <span>Multa por Atraso</span>
                            <strong>${escapeHtml(pagamento.multaAtraso || '2% ao mês + IPCA')}</strong>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Cláusulas Legais -->
            <div class="contrato-card-modern">
                <div class="card-header">
                    <div class="card-icon">⚖️</div>
                    <h2>Cláusulas Contratuais</h2>
                </div>
                <div class="card-content">
                    <div class="clausulas-grid">
                        <div class="clausula-item">
                            <label>Direitos Autorais</label>
                            <p>${escapeHtml(conteudo.direitosAutorais || '')}</p>
                        </div>
                        <div class="clausula-item">
                            <label>Confidencialidade</label>
                            <p>${escapeHtml(conteudo.confidencialidade || '')}</p>
                        </div>
                        <div class="clausula-item">
                            <label>Cancelamento</label>
                            <p>${escapeHtml(conteudo.cancelamento || '')}</p>
                        </div>
                        <div class="clausula-item">
                            <label>Foro</label>
                            <p>${escapeHtml(conteudo.foro || '')}</p>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Cancelamento (se aplicável) -->
            ${c.status === 'Cancelado' ? `
            <div class="contrato-card-modern cancelado-card">
                <div class="card-header">
                    <div class="card-icon">🚫</div>
                    <h2>Contrato Cancelado</h2>
                </div>
                <div class="card-content">
                    <div class="cancelamento-info-modern">
                        ${c.canceladoEm ? `<div><strong>Cancelado em:</strong> ${new Date(c.canceladoEm).toLocaleDateString('pt-BR')}</div>` : ''}
                        ${c.motivoCancelamento ? `<div><strong>Motivo:</strong> ${escapeHtml(c.motivoCancelamento)}</div>` : ''}
                    </div>
                </div>
            </div>` : ''}

            <!-- Entrega Registrada -->
            ${c.temEntregaRegistrada ? `
            <div class="alert-card success-alert">
                <span class="alert-icon">✅</span>
                <div class="alert-content">
                    <strong>Entrega registrada!</strong>
                    <p>O prestador registrou a entrega em <strong>${new Date(c.entregaRegistradaEm).toLocaleDateString('pt-BR')}</strong>. Confirme o recebimento para liberar o pagamento.</p>
                </div>
            </div>` : ''}

            <!-- Avaliação -->
            <div id="secao-avaliacao"></div>

            <!-- Botões de Ação -->
            <div class="contrato-actions-modern">
                ${podAssinar ? `<button id="btn-assinar" class="btn-modern btn-primary"><span>✍️</span> Assinar digitalmente</button>` : ''}
                ${c.assinadoPorMim ? `<div class="assinado-badge"><span>✓</span> Você já assinou este contrato</div>` : ''}
                ${c.temPdf ? `<button id="btn-pdf" class="btn-modern btn-outline"><span>📄</span> Baixar PDF</button>` : ''}
                ${c.status === 'Ativo' && meId === c.contratanteId ? `<button id="btn-iniciar-pag" class="btn-modern btn-success"><span>💳</span> Pagar via PIX</button>` : ''}
                ${c.status === 'Ativo' && meId === c.prestadorId && !c.temEntregaRegistrada ? `<button id="btn-registrar-entrega" class="btn-modern btn-info"><span>📦</span> Registrar entrega</button>` : ''}
                ${c.podeCancelar ? `<button id="btn-cancelar-contrato" class="btn-modern btn-danger"><span>🗑️</span> Cancelar contrato</button>` : ''}
            </div>
        </div>
    `;

    // Event listeners
    if (podAssinar) document.getElementById('btn-assinar')?.addEventListener('click', () => {
        document.getElementById('modal-assinar').style.display = 'flex';
    });
    if (c.temPdf) document.getElementById('btn-pdf')?.addEventListener('click', baixarPdf);
    if (c.status === 'Ativo' && meId === c.contratanteId) {
        document.getElementById('btn-iniciar-pag')?.addEventListener('click', iniciarPagamento);
    }
    if (c.podeCancelar) {
        document.getElementById('btn-cancelar-contrato')?.addEventListener('click', () => abrirModalCancelamento(c));
    }
    if (c.status === 'Ativo' && meId === c.prestadorId && !c.temEntregaRegistrada) {
        document.getElementById('btn-registrar-entrega')?.addEventListener('click', () => {
            document.getElementById('modal-entrega').style.display = 'flex';
        });
    }

    if (c.status === 'Encerrado') carregarSecaoAvaliacao(c);
}

function configurarModal() {
    document.getElementById('modal-cancelar-btn')?.addEventListener('click', () => {
        document.getElementById('modal-assinar').style.display = 'none';
    });

    document.getElementById('modal-assinar-btn')?.addEventListener('click', assinar);

    document.getElementById('modal-cancelar-contrato-fechar')?.addEventListener('click', () => {
        document.getElementById('modal-cancelar-contrato').style.display = 'none';
    });

    document.getElementById('modal-cancelar-contrato-btn')?.addEventListener('click', cancelarContrato);

    document.getElementById('modal-entrega-fechar')?.addEventListener('click', () => {
        document.getElementById('modal-entrega').style.display = 'none';
    });

    document.getElementById('modal-entrega-btn')?.addEventListener('click', registrarEntrega);
}

async function assinar() {
    document.getElementById('modal-assinar').style.display = 'none';
    try {
        await api.post(`/api/contratos/${contratoId}/assinar`, { confirmo: true });
        const atualizado = await api.get(`/api/contratos/${contratoId}`);
        contratoAtual = atualizado;
        renderContrato(atualizado);
        configurarModal();
        showToast('Assinatura registrada com sucesso!');
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao assinar.', true);
    }
}

async function baixarPdf() {
    const btn = document.getElementById('btn-pdf');
    if (!btn) return;

    btn.disabled = true;
    btn.textContent = 'Gerando link...';

    try {
        const { url } = await api.get(`/api/contratos/${contratoId}/pdf`);
        window.open(url, '_blank', 'noopener');
    } catch (err) {
        showToast(err?.data?.mensagem || 'Não foi possível gerar o link do PDF.', true);
    } finally {
        btn.disabled = false;
        btn.textContent = 'Baixar PDF do contrato';
    }
}

function showToast(msg, isError = false) {
    const t = document.createElement('div');
    t.className = `toast ${isError ? 'toast-erro' : 'toast-ok'}`;
    t.textContent = msg;
    document.body.appendChild(t);
    setTimeout(() => t.remove(), 4000);
}

function abrirModalCancelamento(c) {
    const aviso = document.getElementById('modal-cancelar-aviso');
    if (aviso) {
        if (c.status === 'Ativo') {
            const valor = c.conteudo?.pagamento?.valorTotal;
            const taxa = valor ? (valor * 0.05).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) : '5% do valor';
            const reembolso = valor ? (valor * 0.95).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) : '95% do valor';
            aviso.innerHTML = `
                <div class="modal-aviso-taxa">
                    <strong>Taxa de cancelamento:</strong> ${taxa} (5%)<br>
                    <strong>Reembolso ao contratante:</strong> ${reembolso} (95%)
                </div>`;
        } else {
            aviso.innerHTML = `<p style="font-size:0.9rem;color:#374151;margin-bottom:0.75rem">
                O cancelamento é gratuito. O projeto voltará ao status <strong>Aberto</strong>.
            </p>`;
        }
    }
    document.getElementById('modal-cancelar-contrato').style.display = 'flex';
}

async function cancelarContrato() {
    const motivo = document.getElementById('motivo-cancelamento')?.value?.trim() || null;
    const btn = document.getElementById('modal-cancelar-contrato-btn');
    if (btn) { btn.disabled = true; btn.textContent = 'Cancelando...'; }

    document.getElementById('modal-cancelar-contrato').style.display = 'none';

    try {
        const qs = motivo ? `?motivo=${encodeURIComponent(motivo)}` : '';
        await api.delete(`/api/contratos/${contratoId}${qs}`);
        const atualizado = await api.get(`/api/contratos/${contratoId}`);
        contratoAtual = atualizado;
        renderContrato(atualizado);
        configurarModal();
        showToast('Contrato cancelado.');
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao cancelar contrato.', true);
        if (btn) { btn.disabled = false; btn.textContent = 'Confirmar cancelamento'; }
    }
}

async function registrarEntrega() {
    const btn = document.getElementById('modal-entrega-btn');
    if (btn) { btn.disabled = true; btn.textContent = 'Registrando...'; }

    document.getElementById('modal-entrega').style.display = 'none';

    try {
        await api.post(`/api/contratos/${contratoId}/entrega`, {});
        const atualizado = await api.get(`/api/contratos/${contratoId}`);
        contratoAtual = atualizado;
        renderContrato(atualizado);
        configurarModal();
        showToast('Entrega registrada! O contratante foi notificado.');
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao registrar entrega.', true);
        if (btn) { btn.disabled = false; btn.textContent = 'Confirmar entrega'; }
    }
}

async function iniciarPagamento() {
    const btn = document.getElementById('btn-iniciar-pag');
    if (btn) { btn.disabled = true; btn.textContent = 'Iniciando...'; }

    try {
        const resultado = await api.post('/api/pagamentos/iniciar', { contratoServicoId: contratoId });
        location.href = `/pages/pagamento/detalhe.html?id=${resultado.pagamentoId}`;
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao iniciar pagamento.', true);
        if (btn) { btn.disabled = false; btn.textContent = 'Pagar via PIX'; }
    }
}

function escapeHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// ──────────────────────────────────────────────────────────────────────────────
// AVALIAÇÃO — seção no detalhe de contrato encerrado
// ──────────────────────────────────────────────────────────────────────────────

// ──────────────────────────────────────────────────────────────────────────────
// AVALIAÇÃO — seção no detalhe de contrato encerrado (Design Moderno)
// ──────────────────────────────────────────────────────────────────────────────

async function carregarSecaoAvaliacao(contrato) {
    const secao = document.getElementById('secao-avaliacao');
    if (!secao) return;

    try {
        const pendente = await api.get(`/api/contratos/${contrato.id}/avaliacoes/pendente`).catch(() => null);

        // Determinar quem é o avaliador e quem é o avaliado
        const souContratante = meId === contrato.contratanteId;
        const parteAvaliada = souContratante ? (contrato.prestadorNome || 'Prestador') : (contrato.contratanteNome || 'Contratante');
        const tipoAvaliacao = souContratante ? 'prestador' : 'contratante';

        if (pendente && !pendente.jaEnviou) {
            secao.innerHTML = `
                <div class="avaliacao-modern-card pendente-card">
                    <div class="avaliacao-modern-header">
                        <div class="avaliacao-modern-icon-wrapper pending">
                            <svg class="avaliacao-icon-pending" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <circle cx="12" cy="12" r="10"/>
                                <polyline points="12 6 12 12 16 14"/>
                            </svg>
                        </div>
                        <div class="avaliacao-modern-header-content">
                            <h3 class="avaliacao-modern-title">Sua avaliação está pendente!</h3>
                            <p class="avaliacao-modern-subtitle">Avalie <strong>${escapeHtml(parteAvaliada)}</strong> e ajude a construir uma comunidade mais confiável</p>
                        </div>
                        <a href="/pages/avaliacao/enviar.html?id=${pendente.id}" class="btn-avaliar-modern primary">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
                            </svg>
                            Avaliar agora
                        </a>
                    </div>
                    <div class="avaliacao-modern-body">
                        <div class="benefits-grid">
                            <div class="benefit-item">
                                <div class="benefit-icon">⭐</div>
                                <div class="benefit-text">
                                    <strong>Sua opinião importa</strong>
                                    <span>Ajuda outros contratantes a escolherem melhores profissionais</span>
                                </div>
                            </div>
                            <div class="benefit-item">
                                <div class="benefit-icon">🔒</div>
                                <div class="benefit-text">
                                    <strong>Avaliação anônima</strong>
                                    <span>Seu comentário fica privado até a outra parte responder</span>
                                </div>
                            </div>
                            <div class="benefit-item">
                                <div class="benefit-icon">🏆</div>
                                <div class="benefit-text">
                                    <strong>Impacto real</strong>
                                    <span>Contribui para o ranking de reputação na plataforma</span>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>`;
        } else if (pendente && pendente.jaEnviou) {
            secao.innerHTML = `
                <div class="avaliacao-modern-card aguardando-card">
                    <div class="avaliacao-modern-header">
                        <div class="avaliacao-modern-icon-wrapper waiting">
                            <svg class="avaliacao-icon-waiting" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <circle cx="12" cy="12" r="10"/>
                                <path d="M12 6v6l4 2"/>
                            </svg>
                        </div>
                        <div class="avaliacao-modern-header-content">
                            <h3 class="avaliacao-modern-title">Avaliação enviada com sucesso!</h3>
                            <p class="avaliacao-modern-subtitle">Aguardando a avaliação de <strong>${escapeHtml(parteAvaliada)}</strong></p>
                        </div>
                        <div class="status-chip waiting">
                            <span class="status-dot-pulse"></span>
                            Em andamento
                        </div>
                    </div>
                    <div class="avaliacao-modern-body">
                        <div class="waiting-timeline">
                            <div class="timeline-step completed">
                                <div class="step-marker">✓</div>
                                <div class="step-content">
                                    <strong>Você avaliou</strong>
                                    <span>Sua avaliação foi registrada com sucesso</span>
                                </div>
                            </div>
                            <div class="timeline-line"></div>
                            <div class="timeline-step active">
                                <div class="step-marker">⏳</div>
                                <div class="step-content">
                                    <strong>Aguardando contraparte</strong>
                                    <span>A outra parte ainda não avaliou este serviço</span>
                                </div>
                            </div>
                            <div class="timeline-line"></div>
                            <div class="timeline-step pending">
                                <div class="step-marker">🔒</div>
                                <div class="step-content">
                                    <strong>Publicação automática</strong>
                                    <span>Em até 7 dias sua avaliação será publicada, mesmo sem resposta</span>
                                </div>
                            </div>
                        </div>
                        <div class="info-banner">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <circle cx="12" cy="12" r="10"/>
                                <line x1="12" y1="8" x2="12" y2="12"/>
                                <line x1="12" y1="16" x2="12.01" y2="16"/>
                            </svg>
                            <div class="info-banner-text">
                                <strong>Avaliação bilateral?</strong> 
                                As avaliações são públicas apenas quando ambas as partes avaliam, ou automaticamente após 7 dias.
                            </div>
                        </div>
                    </div>
                </div>`;
        } else {
            // Buscar detalhes das avaliações já concluídas
            let avaliacoes = null;
            try {
                avaliacoes = await api.get(`/api/contratos/${contrato.id}/avaliacoes`).catch(() => null);
            } catch (e) { }

            const minhaAvaliacao = avaliacoes?.find(a => a.avaliadorId === meId);
            const outraAvaliacao = avaliacoes?.find(a => a.avaliadorId !== meId);

            secao.innerHTML = `
                <div class="avaliacao-modern-card concluida-card">
                    <div class="avaliacao-modern-header">
                        <div class="avaliacao-modern-icon-wrapper completed">
                            <svg class="avaliacao-icon-completed" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                                <polyline points="22 4 12 14.01 9 11.01"/>
                            </svg>
                        </div>
                        <div class="avaliacao-modern-header-content">
                            <h3 class="avaliacao-modern-title">Avaliação concluída</h3>
                            <p class="avaliacao-modern-subtitle">Avaliações deste contrato já foram processadas</p>
                        </div>
                        <div class="status-chip completed">
                            <span class="status-dot-check">✓</span>
                            Concluído
                        </div>
                    </div>
                    <div class="avaliacao-modern-body">
                        <div class="avaliacoes-summary">
                            ${minhaAvaliacao ? `
                            <div class="avaliacao-summary-item minha">
                                <div class="summary-header">
                                    <div class="summary-user">
                                        <div class="user-avatar small">${escapeHtml((meId === contrato.contratanteId ? 'C' : 'P'))}</div>
                                        <span>${meId === contrato.contratanteId ? 'Sua avaliação' : 'Sua avaliação'}</span>
                                    </div>
                                    <div class="summary-rating">
                                        ${renderStarRating(minhaAvaliacao.nota || 0)}
                                        <span class="rating-number">${(minhaAvaliacao.nota || 0).toFixed(1)}</span>
                                    </div>
                                </div>
                                ${minhaAvaliacao.comentario ? `<p class="summary-comment">"${escapeHtml(minhaAvaliacao.comentario)}"</p>` : ''}
                            </div>
                            ` : ''}
                            ${outraAvaliacao ? `
                            <div class="avaliacao-summary-item outra">
                                <div class="summary-header">
                                    <div class="summary-user">
                                        <div class="user-avatar small outra">${escapeHtml((outraAvaliacao.avaliadorId === contrato.contratanteId ? 'C' : 'P'))}</div>
                                        <span>Avaliação de ${escapeHtml(outraAvaliacao.avaliadorNome || (outraAvaliacao.avaliadorId === contrato.contratanteId ? 'Contratante' : 'Prestador'))}</span>
                                    </div>
                                    <div class="summary-rating">
                                        ${renderStarRating(outraAvaliacao.nota || 0)}
                                        <span class="rating-number">${(outraAvaliacao.nota || 0).toFixed(1)}</span>
                                    </div>
                                </div>
                                ${outraAvaliacao.comentario ? `<p class="summary-comment">"${escapeHtml(outraAvaliacao.comentario)}"</p>` : ''}
                            </div>
                            ` : ''}
                        </div>
                    </div>
                </div>`;
        }
    } catch (err) {
        console.error('Erro ao carregar avaliação:', err);
        // Falha silenciosa - não mostrar erro para o usuário
    }
}

function renderStarRating(rating) {
    const fullStars = Math.floor(rating);
    const hasHalfStar = rating % 1 >= 0.5;
    let stars = '';

    for (let i = 0; i < fullStars; i++) {
        stars += '<svg class="star-icon filled" viewBox="0 0 24 24"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>';
    }
    if (hasHalfStar) {
        stars += '<svg class="star-icon half" viewBox="0 0 24 24"><defs><linearGradient id="half"><stop offset="50%" stop-color="currentColor"/><stop offset="50%" stop-color="#d1d5db"/></linearGradient></defs><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" fill="url(#half)"/></svg>';
    }
    const emptyStars = 5 - Math.ceil(rating);
    for (let i = 0; i < emptyStars; i++) {
        stars += '<svg class="star-icon empty" viewBox="0 0 24 24"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>';
    }
    return `<div class="star-rating">${stars}</div>`;
}

// Inicializar
init();