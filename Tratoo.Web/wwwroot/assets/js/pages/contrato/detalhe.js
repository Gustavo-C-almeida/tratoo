// Página: /pages/contrato/detalhe.html?id={guid}
// Design Moderno para Detalhe do Contrato

const params = new URLSearchParams(location.search);
const contratoId = params.get('id');
const root = document.getElementById('contrato-root');

let contratoAtual = null;
let meId = null;

// Resolve label e classe do badge de status de forma contextual ao usuário atual.
function resolverStatusBadge(c) {
    const ehPrestador   = meId === c.prestadorId;
    const ehContratante = meId === c.contratanteId;

    // ── Pré-assinatura ────────────────────────────────────────────────────────
    if (c.status === 'Gerado' || c.status === 'AguardandoAssinatura') {
        if (!c.assinadoPorMim)
            return { label: 'Aguardando sua assinatura', cssClass: 'status-aguardando' };
        return { label: 'Aguardando assinatura da outra parte', cssClass: 'status-pendente' };
    }

    // ── Ativo: sub-estados baseados no pagamento ──────────────────────────────
    if (c.status === 'Ativo') {
        if (!c.pagamentoConfirmado) {
            if (ehContratante)
                return { label: 'Aguardando seu pagamento', cssClass: 'status-aguardando' };
            return { label: 'Aguardando pagamento', cssClass: 'status-aguardando' };
        }
        return { label: 'Em execução', cssClass: 'status-ativo' };
    }

    // ── Entrega registrada, aguardando aprovação do contratante ──────────────
    if (c.status === 'AguardandoAprovacaoEntrega') {
        if (ehContratante)
            return { label: 'Entrega aguardando sua aprovação', cssClass: 'status-aguardando' };
        return { label: 'Aguardando aprovação da entrega', cssClass: 'status-pendente' };
    }

    // ── Estados finais ────────────────────────────────────────────────────────
    if (c.status === 'Encerrado')
        return { label: 'Concluído', cssClass: 'status-encerrado' };

    if (c.status === 'Cancelado')
        return { label: 'Cancelado', cssClass: 'status-cancelado' };

    return { label: c.status, cssClass: '' };
}

async function init() {
    if (!contratoId) {
        root.innerHTML = '<div class="error-state"><span class="error-icon"><i class="fa-solid fa-triangle-exclamation"></i></span><p class="erro-msg">ID do contrato não informado.</p></div>';
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
            root.innerHTML = `<div class="error-state"><span class="error-icon"><i class="fa-solid fa-circle-xmark"></i></span><p class="erro-msg">${err?.data?.mensagem || 'Erro ao carregar contrato.'}</p></div>`;
        }
    }
}

// ── Gating do fluxo: prestador só entrega após pagamento confirmado (escrow) ───
function podeRegistrarEntrega(c) {
    return c.status === 'Ativo'
        && meId === c.prestadorId
        && c.pagamentoConfirmado === true
        && !c.temEntregaRegistrada;
}

// Banner que comunica o estado do escrow e bloqueia ações fora de ordem.
function renderEscrowEstado(c) {
    // Só relevante enquanto o contrato está em execução (Ativo) ou aguardando análise.
    const ehPrestador = meId === c.prestadorId;
    const ehContratante = meId === c.contratanteId;

    // 1) Contrato ativo, pagamento ainda NÃO confirmado
    if (c.status === 'Ativo' && !c.pagamentoConfirmado) {
        if (ehPrestador) {
            return `
            <div class="alert-card warning-alert">
                <span class="alert-icon"><i class="fa-solid fa-hourglass-half"></i></span>
                <div class="alert-content">
                    <strong>Aguardando confirmação do pagamento</strong>
                    <p>Você poderá iniciar a execução e registrar a entrega assim que o contratante
                    realizar o pagamento e o valor ficar protegido em garantia pela plataforma.</p>
                </div>
            </div>`;
        }
        if (ehContratante) {
            return `
            <div class="alert-card warning-alert">
                <span class="alert-icon"><i class="fa-solid fa-credit-card"></i></span>
                <div class="alert-content">
                    <strong>Pagamento pendente</strong>
                    <p>Realize o pagamento para liberar o início da execução. O valor fica
                    <strong>protegido pela plataforma</strong> e só é repassado ao prestador após você aprovar a entrega.</p>
                </div>
            </div>`;
        }
    }

    // 2) Pagamento confirmado e em escrow (execução liberada, valor protegido)
    if (c.pagamentoConfirmado && ['Ativo', 'AguardandoAprovacaoEntrega'].includes(c.status)) {
        return `
        <div class="alert-card success-alert">
            <span class="alert-icon"><i class="fa-solid fa-lock"></i></span>
            <div class="alert-content">
                <strong>Pagamento protegido em garantia (escrow)</strong>
                <p>O valor está retido com segurança na plataforma e <strong>ainda não foi liberado</strong> ao prestador.
                Será repassado somente após a aprovação da entrega pelo contratante.</p>
            </div>
        </div>`;
    }

    return '';
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
        ? `<span class="assinado-icon"><i class="fa-solid fa-check"></i></span> ${new Date(c.assinadoContratanteEm).toLocaleDateString('pt-BR')}`
        : '<span class="pendente-icon">○</span> Pendente';
    const assineiPrestador = c.assinadoPrestadorEm
        ? `<span class="assinado-icon"><i class="fa-solid fa-check"></i></span> ${new Date(c.assinadoPrestadorEm).toLocaleDateString('pt-BR')}`
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
                        <a href="/pages/me/contratos.html" class="breadcrumb-link"><i class="fa-solid fa-arrow-left"></i> Meus Contratos</a>
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
                        <div class="parte-avatar contratante-avatar"><i class="fa-solid fa-user"></i></div>
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
                        <div class="parte-avatar prestador-avatar"><i class="fa-solid fa-screwdriver-wrench"></i></div>
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
                    <div class="card-icon"><i class="fa-solid fa-bullseye"></i></div>
                    <h2>Objeto do Contrato</h2>
                </div>
                <div class="card-content">
                    <p class="objeto-text">${escapeHtml(conteudo.objeto || '')}</p>
                </div>
            </div>

            <!-- Escopo -->
            <div class="contrato-card-modern">
                <div class="card-header">
                    <div class="card-icon"><i class="fa-solid fa-clipboard-list"></i></div>
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
                        <div class="card-icon"><i class="fa-solid fa-calendar-days"></i></div>
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
                                                <span><i class="fa-solid fa-calendar-days"></i> ${m.prazo ? new Date(m.prazo).toLocaleDateString('pt-BR') : '—'}</span>
                                                <span><i class="fa-solid fa-money-bill-wave"></i> ${m.valor?.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) || '—'}</span>
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
                        <div class="card-icon"><i class="fa-solid fa-money-bill-wave"></i></div>
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
                    <div class="card-icon"><i class="fa-solid fa-scale-balanced"></i></div>
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
                    <div class="card-icon"><i class="fa-solid fa-ban"></i></div>
                    <h2>Contrato Cancelado</h2>
                </div>
                <div class="card-content">
                    <div class="cancelamento-info-modern">
                        ${c.canceladoEm ? `<div><strong>Cancelado em:</strong> ${new Date(c.canceladoEm).toLocaleDateString('pt-BR')}</div>` : ''}
                        ${c.motivoCancelamento ? `<div><strong>Motivo:</strong> ${escapeHtml(c.motivoCancelamento)}</div>` : ''}
                    </div>
                </div>
            </div>` : ''}

            <!-- Estado do pagamento em garantia (escrow) -->
            ${renderEscrowEstado(c)}

            <!-- Entrega formal (carregada de forma assíncrona) -->
            <div id="secao-entrega"></div>

            <!-- Avaliação -->
            <div id="secao-avaliacao"></div>

            <!-- Botões de Ação -->
            <div class="contrato-actions-modern">
                ${podAssinar ? `<button id="btn-assinar" class="btn-modern btn-primary"><span><i class="fa-solid fa-signature"></i></span> Assinar digitalmente</button>` : ''}
                ${c.assinadoPorMim ? `<div class="assinado-badge"><span><i class="fa-solid fa-check"></i></span> Você já assinou este contrato</div>` : ''}
                ${c.temPdf ? `<button id="btn-pdf" class="btn-modern btn-outline"><span><i class="fa-solid fa-file-lines"></i></span> Baixar PDF</button>` : ''}
                ${c.status === 'Ativo' && meId === c.contratanteId && !c.pagamentoConfirmado ? `<button id="btn-iniciar-pag" class="btn-modern btn-success"><span><i class="fa-solid fa-credit-card"></i></span> Pagar via PIX</button>` : ''}
                ${podeRegistrarEntrega(c) ? `<button id="btn-registrar-entrega" class="btn-modern btn-info"><span><i class="fa-solid fa-box-open"></i></span> Registrar entrega</button>` : ''}
                ${c.podeCancelar ? `<button id="btn-cancelar-contrato" class="btn-modern btn-danger"><span><i class="fa-solid fa-trash-can"></i></span> Cancelar contrato</button>` : ''}
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
    if (podeRegistrarEntrega(c)) {
        document.getElementById('btn-registrar-entrega')?.addEventListener('click', abrirModalEntrega);
    }

    // Carrega a seção de entrega formal sempre que houver (aguardando aprovação ou encerrado)
    if (['AguardandoAprovacaoEntrega', 'Encerrado'].includes(c.status)) {
        carregarSecaoEntrega(c);
    }

    if (c.status === 'Encerrado') carregarSecaoAvaliacao(c);
}

function configurarModal() {
    // ── Etapa 1: fechar modal de confirmação ──────────────────────────────────
    document.getElementById('modal-cancelar-btn')?.addEventListener('click', () => {
        document.getElementById('modal-assinar').style.display = 'none';
    });

    // ── Etapa 1 -> Etapa 2: solicitar OTP e abrir modal de código ─────────────
    document.getElementById('modal-solicitar-otp-btn')?.addEventListener('click', solicitarOtp);

    // ── Etapa 2: fechar modal OTP e voltar para etapa 1 ───────────────────────
    document.getElementById('modal-otp-cancelar-btn')?.addEventListener('click', () => {
        document.getElementById('modal-otp').style.display = 'none';
        document.getElementById('modal-assinar').style.display = 'flex';
    });

    // ── Etapa 2: confirmar OTP e assinar ──────────────────────────────────────
    document.getElementById('modal-otp-confirmar-btn')?.addEventListener('click', assinarComOtp);

    // ── Reenviar OTP ──────────────────────────────────────────────────────────
    document.getElementById('btn-reenviar-otp')?.addEventListener('click', async () => {
        const btn = document.getElementById('btn-reenviar-otp');
        const originalText = btn.innerHTML;

        btn.disabled = true;
        btn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Reenviando...';

        try {
            await api.post(`/api/contratos/${contratoId}/assinar/solicitar-otp`, {});
            showToast('Novo código enviado ao seu e-mail.', false);
            document.getElementById('otp-input').value = '';
            document.getElementById('otp-erro').style.display = 'none';

            // Desabilitar por 60 segundos
            let countdown = 60;
            const interval = setInterval(() => {
                countdown--;
                btn.innerHTML = `<i class="fa-solid fa-clock"></i> Aguarde ${countdown}s`;
                if (countdown <= 0) {
                    clearInterval(interval);
                    btn.innerHTML = originalText;
                    btn.disabled = false;
                }
            }, 1000);
        } catch (err) {
            btn.innerHTML = originalText;
            btn.disabled = false;
            showToast(err?.data?.mensagem || 'Erro ao reenviar código.', true);
        }
    });

    // ── Enter no campo OTP ────────────────────────────────────────────────────
    document.getElementById('otp-input')?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') assinarComOtp();
    });

    document.getElementById('modal-cancelar-contrato-fechar')?.addEventListener('click', () => {
        document.getElementById('modal-cancelar-contrato').style.display = 'none';
    });

    document.getElementById('modal-cancelar-contrato-btn')?.addEventListener('click', cancelarContrato);

    document.getElementById('modal-entrega-fechar')?.addEventListener('click', () => {
        document.getElementById('modal-entrega').style.display = 'none';
    });

    document.getElementById('modal-entrega-btn')?.addEventListener('click', registrarEntrega);

    // Links dinâmicos e preview de arquivos do modal de entrega
    document.getElementById('entrega-add-link')?.addEventListener('click', adicionarLinhaLink);
    document.getElementById('entrega-arquivos')?.addEventListener('change', previewArquivosEntrega);

    // Modal de solicitar ajustes (rejeitar)
    document.getElementById('modal-rejeitar-fechar')?.addEventListener('click', () => {
        document.getElementById('modal-rejeitar').style.display = 'none';
    });
    document.getElementById('modal-rejeitar-btn')?.addEventListener('click', rejeitarEntrega);
}

// Etapa 1 -> 2: envia OTP e abre modal de código
async function solicitarOtp() {
    const btn = document.getElementById('modal-solicitar-otp-btn');
    btn.disabled = true;
    btn.textContent = 'Enviando código...';

    try {
        await api.post(`/api/contratos/${contratoId}/assinar/solicitar-otp`, {});
        document.getElementById('modal-assinar').style.display = 'none';
        document.getElementById('otp-input').value = '';
        document.getElementById('otp-erro').style.display = 'none';
        document.getElementById('modal-otp').style.display = 'flex';
        setTimeout(() => document.getElementById('otp-input')?.focus(), 100);
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao enviar código. Tente novamente.', true);
    } finally {
        btn.disabled = false;
        btn.textContent = 'Continuar';
    }
}

// Etapa 2: valida OTP e assina
async function assinarComOtp() {
    const otp = document.getElementById('otp-input')?.value?.trim();
    const erroEl = document.getElementById('otp-erro');

    if (!otp || otp.length !== 6) {
        erroEl.textContent = 'Informe o código de 6 dígitos.';
        erroEl.style.display = 'block';
        return;
    }

    const btn = document.getElementById('modal-otp-confirmar-btn');
    btn.disabled = true;
    btn.textContent = 'Assinando...';
    erroEl.style.display = 'none';

    try {
        await api.post(`/api/contratos/${contratoId}/assinar`, { confirmo: true, otp });
        document.getElementById('modal-otp').style.display = 'none';
        const atualizado = await api.get(`/api/contratos/${contratoId}`);
        contratoAtual = atualizado;
        renderContrato(atualizado);
        configurarModal();
        showToast('Assinatura registrada com sucesso!');
    } catch (err) {
        erroEl.textContent = err?.data?.mensagem || 'Código inválido. Verifique e tente novamente.';
        erroEl.style.display = 'block';
        btn.disabled = false;
        btn.textContent = 'Assinar digitalmente';
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

// ── Entrega formal: abrir modal, links dinâmicos e preview de arquivos ─────────

const ENTREGA_EXT_OK = ['.pdf', '.doc', '.docx', '.xls', '.xlsx', '.jpg', '.jpeg', '.png', '.zip'];
const ENTREGA_MAX_ARQUIVOS = 10;
const ENTREGA_MAX_BYTES = 20 * 1024 * 1024;

function abrirModalEntrega() {
    // Reseta o formulário
    document.getElementById('entrega-descricao').value = '';
    document.getElementById('entrega-observacoes').value = '';
    document.getElementById('entrega-data').valueAsDate = new Date();
    document.getElementById('entrega-links-lista').innerHTML = '';
    document.getElementById('entrega-arquivos').value = '';
    document.getElementById('entrega-arquivos-lista').innerHTML = '';
    document.getElementById('modal-entrega').style.display = 'flex';
}

function adicionarLinhaLink() {
    const lista = document.getElementById('entrega-links-lista');
    const row = document.createElement('div');
    row.className = 'entrega-link-row';
    row.innerHTML = `
        <input type="url" class="entrega-link-url" placeholder="https://...">
        <input type="text" class="entrega-link-desc" placeholder="Descrição (opcional)">
        <button type="button" class="entrega-link-remover" aria-label="Remover link"><i class="fa-solid fa-xmark"></i></button>`;
    row.querySelector('.entrega-link-remover').addEventListener('click', () => row.remove());
    lista.appendChild(row);
}

function previewArquivosEntrega() {
    const input = document.getElementById('entrega-arquivos');
    const lista = document.getElementById('entrega-arquivos-lista');
    const arquivos = Array.from(input.files);
    lista.innerHTML = arquivos.map(f => {
        const ext = '.' + (f.name.split('.').pop() || '').toLowerCase();
        const mb = (f.size / (1024 * 1024)).toFixed(1);
        const invalido = !ENTREGA_EXT_OK.includes(ext) || f.size > ENTREGA_MAX_BYTES;
        return `<div class="entrega-arquivo-item ${invalido ? 'invalido' : ''}">
            <span>${escapeHtml(f.name)}</span><span class="entrega-arquivo-tam">${mb} MB</span>
        </div>`;
    }).join('');
}

async function registrarEntrega() {
    const descricao = document.getElementById('entrega-descricao')?.value?.trim();
    if (!descricao) { showToast('Informe a descrição da entrega.', true); return; }

    const arquivos = Array.from(document.getElementById('entrega-arquivos').files);
    if (arquivos.length > ENTREGA_MAX_ARQUIVOS) {
        showToast(`Máximo de ${ENTREGA_MAX_ARQUIVOS} arquivos.`, true); return;
    }
    for (const f of arquivos) {
        const ext = '.' + (f.name.split('.').pop() || '').toLowerCase();
        if (!ENTREGA_EXT_OK.includes(ext)) { showToast(`Formato não permitido: ${f.name}`, true); return; }
        if (f.size > ENTREGA_MAX_BYTES) { showToast(`Arquivo acima de 20 MB: ${f.name}`, true); return; }
    }

    // Monta links
    const links = Array.from(document.querySelectorAll('.entrega-link-row'))
        .map(r => ({
            url: r.querySelector('.entrega-link-url').value.trim(),
            descricao: r.querySelector('.entrega-link-desc').value.trim() || null
        }))
        .filter(l => l.url);

    const form = new FormData();
    form.append('descricaoEntrega', descricao);
    form.append('observacoes', document.getElementById('entrega-observacoes').value.trim());
    form.append('dataEntrega', document.getElementById('entrega-data').value || new Date().toISOString());
    form.append('links', JSON.stringify(links));
    arquivos.forEach(f => form.append('files', f));

    const btn = document.getElementById('modal-entrega-btn');
    if (btn) { btn.disabled = true; btn.textContent = 'Enviando...'; }

    try {
        await api.uploadPost(`/api/contratos/${contratoId}/entrega`, form);
        document.getElementById('modal-entrega').style.display = 'none';
        const atualizado = await api.get(`/api/contratos/${contratoId}`);
        contratoAtual = atualizado;
        renderContrato(atualizado);
        configurarModal();
        showToast('Entrega enviada! O contratante foi notificado.');
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao registrar entrega.', true);
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Enviar entrega'; }
    }
}

// ── Exibição da entrega + aprovar/rejeitar ────────────────────────────────────

const ENTREGA_STATUS = {
    PendenteAprovacao: { label: 'Aguardando aprovação', cls: 'badge-aguardando' },
    Aprovada:          { label: 'Aprovada',             cls: 'badge-liberado' },
    Rejeitada:         { label: 'Ajustes solicitados',  cls: 'badge-cancelado' }
};

async function carregarSecaoEntrega(c) {
    const container = document.getElementById('secao-entrega');
    if (!container) return;

    let entrega;
    try {
        entrega = await api.get(`/api/contratos/${contratoId}/entrega`);
    } catch {
        return; // sem entrega ou sem acesso
    }
    if (!entrega || !entrega.id) return;

    const st = ENTREGA_STATUS[entrega.status] || { label: entrega.status, cls: '' };
    const ehContratante = meId === c.contratanteId;
    const podeAprovar = ehContratante && entrega.status === 'PendenteAprovacao';

    const anexos = (entrega.anexos || []).map(a => `
        <li>
            <a href="${a.urlDownload}" target="_blank" rel="noopener">${escapeHtml(a.nomeArquivo)}</a>
            <span class="entrega-anexo-tam">${(a.tamanhoArquivo / 1024).toFixed(0)} KB</span>
        </li>`).join('');

    const links = (entrega.links || []).map(l => `
        <li><a href="${escapeHtml(l.url)}" target="_blank" rel="noopener">${escapeHtml(l.descricao || l.url)}</a></li>`).join('');

    container.innerHTML = `
        <div class="card-modern entrega-card">
            <div class="card-header">
                <h2><i class="fa-solid fa-box-open"></i> Entrega do serviço</h2>
                <span class="status-badge ${st.cls}">${st.label}</span>
            </div>
            <div class="card-content">
                <p class="entrega-descricao-txt">${escapeHtml(entrega.descricaoEntrega)}</p>
                ${entrega.observacoes ? `<p class="entrega-obs"><strong>Observações:</strong> ${escapeHtml(entrega.observacoes)}</p>` : ''}
                <p class="entrega-meta">Entregue em ${new Date(entrega.dataEntrega).toLocaleDateString('pt-BR')}</p>

                ${anexos ? `<div class="entrega-bloco"><h4>Anexos</h4><ul class="entrega-anexos">${anexos}</ul></div>` : ''}
                ${links ? `<div class="entrega-bloco"><h4>Links</h4><ul class="entrega-links">${links}</ul></div>` : ''}

                ${entrega.status === 'Rejeitada' && entrega.motivoRejeicao
                    ? `<div class="alert-card warning-alert"><span class="alert-icon"><i class="fa-solid fa-triangle-exclamation"></i></span><div class="alert-content"><strong>Ajustes solicitados:</strong><p>${escapeHtml(entrega.motivoRejeicao)}</p></div></div>` : ''}

                ${podeAprovar ? `
                <div class="entrega-analise-destaque">
                    <p class="entrega-analise-titulo"><i class="fa-solid fa-clipboard-list"></i> Entrega recebida — analise e escolha uma ação:</p>
                    <div class="entrega-acoes">
                        <button id="btn-aprovar-entrega" class="btn-modern btn-success"><span><i class="fa-solid fa-circle-check"></i></span> Aprovar entrega</button>
                        <button id="btn-rejeitar-entrega" class="btn-modern btn-warning"><span><i class="fa-solid fa-arrow-left"></i></span> Solicitar ajustes</button>
                        ${c.pagamentoId ? `<button id="btn-disputar-entrega" class="btn-modern btn-danger"><span><i class="fa-solid fa-scale-balanced"></i></span> Abrir disputa</button>` : ''}
                    </div>
                    <p class="entrega-aviso-aprovar">Ao aprovar, o pagamento protegido em garantia será liberado ao prestador e o projeto será concluído.</p>
                </div>` : ''}

                ${!ehContratante && entrega.status === 'PendenteAprovacao'
                    ? `<div class="alert-card warning-alert"><span class="alert-icon"><i class="fa-solid fa-clock"></i></span><div class="alert-content"><strong>Aguardando análise do contratante</strong><p>Sua entrega foi enviada. O contratante irá aprovar, solicitar ajustes ou abrir uma disputa.</p></div></div>` : ''}
            </div>
        </div>`;

    if (podeAprovar) {
        document.getElementById('btn-aprovar-entrega')?.addEventListener('click', aprovarEntrega);
        document.getElementById('btn-rejeitar-entrega')?.addEventListener('click', () => {
            document.getElementById('rejeitar-motivo').value = '';
            document.getElementById('modal-rejeitar').style.display = 'flex';
        });
        // Disputa é tratada na tela de pagamento (fonte única do fluxo de disputa/escrow).
        document.getElementById('btn-disputar-entrega')?.addEventListener('click', () => {
            if (c.pagamentoId) location.href = `/pages/pagamento/detalhe.html?id=${c.pagamentoId}`;
        });
    }
}

async function aprovarEntrega() {
    const btn = document.getElementById('btn-aprovar-entrega');
    if (btn) { btn.disabled = true; btn.textContent = 'Aprovando...'; }
    try {
        await api.post(`/api/contratos/${contratoId}/entrega/aprovar`, {});
        const atualizado = await api.get(`/api/contratos/${contratoId}`);
        contratoAtual = atualizado;
        renderContrato(atualizado);
        configurarModal();
        showToast('Entrega aprovada! Pagamento liberado ao prestador.');
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao aprovar entrega.', true);
        if (btn) { btn.disabled = false; btn.textContent = 'Aprovar entrega'; }
    }
}

async function rejeitarEntrega() {
    const motivo = document.getElementById('rejeitar-motivo')?.value?.trim();
    if (!motivo) { showToast('Informe o motivo dos ajustes.', true); return; }

    const btn = document.getElementById('modal-rejeitar-btn');
    if (btn) btn.disabled = true;
    try {
        await api.post(`/api/contratos/${contratoId}/entrega/rejeitar`, { motivo });
        document.getElementById('modal-rejeitar').style.display = 'none';
        const atualizado = await api.get(`/api/contratos/${contratoId}`);
        contratoAtual = atualizado;
        renderContrato(atualizado);
        configurarModal();
        showToast('Ajustes solicitados ao prestador.');
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao solicitar ajustes.', true);
    } finally {
        if (btn) btn.disabled = false;
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
        let pendente = null;
        let erroPendente = null;
        try {
            pendente = await api.get(`/api/contratos/${contrato.id}/avaliacoes/pendente`);
        } catch (err) {
            erroPendente = err?.status ?? 0;
        }

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
                                <div class="benefit-icon"><i class="fa-solid fa-star"></i></div>
                                <div class="benefit-text">
                                    <strong>Sua opinião importa</strong>
                                    <span>Ajuda outros contratantes a escolherem melhores profissionais</span>
                                </div>
                            </div>
                            <div class="benefit-item">
                                <div class="benefit-icon"><i class="fa-solid fa-lock"></i></div>
                                <div class="benefit-text">
                                    <strong>Avaliação anônima</strong>
                                    <span>Seu comentário fica privado até a outra parte responder</span>
                                </div>
                            </div>
                            <div class="benefit-item">
                                <div class="benefit-icon"><i class="fa-solid fa-trophy"></i></div>
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
                                <div class="step-marker"><i class="fa-solid fa-check"></i></div>
                                <div class="step-content">
                                    <strong>Você avaliou</strong>
                                    <span>Sua avaliação foi registrada com sucesso</span>
                                </div>
                            </div>
                            <div class="timeline-line"></div>
                            <div class="timeline-step active">
                                <div class="step-marker"><i class="fa-solid fa-hourglass-half"></i></div>
                                <div class="step-content">
                                    <strong>Aguardando contraparte</strong>
                                    <span>A outra parte ainda não avaliou este serviço</span>
                                </div>
                            </div>
                            <div class="timeline-line"></div>
                            <div class="timeline-step pending">
                                <div class="step-marker"><i class="fa-solid fa-lock"></i></div>
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
        } else if (erroPendente === 404) {
            // 404 = avaliação ainda não foi gerada para este contrato — não exibir nada
            secao.innerHTML = '';
        } else {
            // pendente.jaEnviou === true: já enviou, buscar detalhes das avaliações concluídas
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
                            <span class="status-dot-check"><i class="fa-solid fa-check"></i></span>
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