// Página: /pages/pagamento/detalhe.html?id={guid}
// Exibe detalhes do pagamento PIX com escrow, QR Code, ledger e disputas.

const params = new URLSearchParams(location.search);
const pagamentoId = params.get('id');
const root = document.getElementById('pag-root');

let pagamentoAtual = null;
let meId = null;
let isContratante = null; // null = indeterminado (mostra tudo, backend valida)
let pollingTimer = null;

const STATUS_LABEL = {
    Criado:     'Criado',
    Aguardando: 'Aguardando pagamento PIX',
    Processando:'Processando',
    Retido:     'Retido em escrow',
    TransferenciaEmProgresso: 'Transferência em progresso',
    Liberado:   'Liberado ao prestador',
    FalhaTransferencia: 'Falha na transferência',
    Cancelado:  'Cancelado',
    Falhou:     'Falhou',
    Estornado:  'Estornado',
    EmDisputa:  'Em disputa'
};

const STATUS_CLASS = {
    Criado:     'badge-gerado',
    Aguardando: 'badge-aguardando',
    Processando:'badge-aguardando',
    Retido:     'badge-retido',
    TransferenciaEmProgresso: 'badge-aguardando',
    Liberado:   'badge-liberado',
    FalhaTransferencia: 'badge-cancelado',
    Cancelado:  'badge-cancelado',
    Falhou:     'badge-cancelado',
    Estornado:  'badge-encerrado',
    EmDisputa:  'badge-disputa'
};

const LEDGER_LABEL = {
    Entrada:           'Entrada',
    Estorno:           'Estorno',
    Transferencia:     'Transferência'
};

const DISPUTA_STATUS_LABEL = {
    Aberta:    'Aberta',
    EmAnalise: 'Em análise',
    Resolvida: 'Resolvida',
    Encerrada: 'Encerrada'
};

// ──────────────────────────────────────────────────────────────────────────────
// INIT
// ──────────────────────────────────────────────────────────────────────────────

async function init() {
    if (!pagamentoId) {
        root.innerHTML = '<p class="erro-msg">ID do pagamento não informado.</p>';
        return;
    }

    root.innerHTML = '<p class="loading-msg">Carregando pagamento...</p>';

    try {
        const [me, pag] = await Promise.all([
            api.get('/api/me'),
            api.get(`/api/pagamentos/${pagamentoId}`)
        ]);
        meId = me.id;
        pagamentoAtual = pag;

        // Determinar papel do usuário a partir do contrato (melhora UX dos botões)
        if (pag.contratoServicoId) {
            try {
                const contrato = await api.get(`/api/contratos/${pag.contratoServicoId}`);
                isContratante = contrato.contratanteId === meId;
            } catch {
                isContratante = null; // backend vai validar
            }
        }

        renderPagamento(pag);
        configurarModais();
        iniciarPolling(pag.status);
    } catch (err) {
        if (err?.status === 401) {
            location.href = '/pages/auth/login.html';
        } else {
            root.innerHTML = `<p class="erro-msg">${err?.data?.mensagem || 'Erro ao carregar pagamento.'}</p>`;
        }
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// POLLING (quando aguardando confirmação do PIX)
// ──────────────────────────────────────────────────────────────────────────────

function iniciarPolling(status) {
    clearTimeout(pollingTimer);
    if (['Criado', 'Aguardando', 'Processando'].includes(status)) {
        pollingTimer = setTimeout(async () => {
            try {
                const pag = await api.get(`/api/pagamentos/${pagamentoId}`);
                if (pag.status !== pagamentoAtual?.status) {
                    pagamentoAtual = pag;
                    renderPagamento(pag);
                    configurarModais();
                    showToast('Status do pagamento atualizado.');
                }
                iniciarPolling(pag.status);
            } catch {
                iniciarPolling(status); // tenta novamente
            }
        }, 10000);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// RENDER PRINCIPAL
// ──────────────────────────────────────────────────────────────────────────────

function renderPagamento(pag) {
    const statusLabel = STATUS_LABEL[pag.status] || pag.status;
    const statusClass = STATUS_CLASS[pag.status] || '';

    const fmtBRL = v => v != null
        ? Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })
        : '—';

    const fmtData  = s => s ? new Date(s).toLocaleDateString('pt-BR') : '—';
    const fmtDtHr  = s => s ? new Date(s).toLocaleString('pt-BR') : null;

    const liberacaoAuto = fmtData(pag.liberacaoAutomaticaEm);

    root.innerHTML = `
        <div class="pag-container">

            <!-- CABEÇALHO -->
            <header class="contrato-header">
                <div>
                    <a href="javascript:history.back()" class="back-link-pag"><i class="fa-solid fa-arrow-left"></i> Voltar</a>
                    <h1>Pagamento via PIX</h1>
                    <p class="pag-sub">Escrow protegido pela Tratoo</p>
                </div>
                <span class="status-badge ${statusClass}">${statusLabel}</span>
            </header>

            <!-- CONFIRMAÇÃO DE PAGAMENTO -->
            ${renderConfirmacao(pag)}

            <!-- QR CODE PIX -->
            ${renderPixSection(pag)}

            <!-- ESCROW ATIVO -->
            ${pag.status === 'Retido' ? `
            <section class="pag-secao pag-escrow">
                <h2>Escrow ativo</h2>
                <p>
                    O valor está retido na Tratoo e será liberado ao prestador quando você
                    <strong>confirmar a entrega</strong>, ou automaticamente em
                    <strong>${liberacaoAuto}</strong> (7 dias após o prazo de entrega).
                </p>
            </section>` : ''}

            <!-- RESUMO FINANCEIRO -->
            <section class="pag-secao">
                <h2>Resumo financeiro</h2>
                <div class="financeiro-grid">
                    <div class="financeiro-item fi-destaque">
                        <span class="fi-label">Valor do serviço</span>
                        <span class="fi-valor">${fmtBRL(pag.valorBruto)}</span>
                    </div>
                    <div class="financeiro-item fi-destaque">
                        <span class="fi-label">Valor ao prestador</span>
                        <span class="fi-valor fi-liquido">${fmtBRL(pag.valorBruto)}</span>
                    </div>
                </div>
                ${pag.taxaGateway != null ? `
                <p class="fi-nota-taxa">
                    Taxa operacional do gateway (Asaas): ${fmtBRL(pag.taxaGateway)} — cobrada pela operadora de pagamentos, sem impacto no valor recebido pelo prestador.
                </p>` : ''}
                <dl class="dados-lista" style="margin-top:1rem">
                    <dt>Iniciado em</dt><dd>${fmtDtHr(pag.criadoEm) || '—'}</dd>
                    ${pag.pagoEm            ? `<dt>Confirmado em</dt><dd>${fmtDtHr(pag.pagoEm)}</dd>` : ''}
                    ${pag.liberadoEm        ? `<dt>Liberado em</dt><dd>${fmtDtHr(pag.liberadoEm)}</dd>` : ''}
                    <dt>Liberação automática</dt><dd>${liberacaoAuto}</dd>
                    ${pag.statusGateway     ? `<dt>Status no gateway</dt><dd>${escapeHtml(pag.statusGateway)}</dd>` : ''}
                    ${pag.asaasCobrancaId   ? `<dt>ID Asaas</dt><dd class="mono">${escapeHtml(pag.asaasCobrancaId)}</dd>` : ''}
                </dl>
            </section>

            <!-- AÇÕES -->
            ${renderAcoes(pag)}

            <!-- DISPUTAS -->
            ${pag.disputas?.length ? renderDisputas(pag.disputas) : ''}

            <!-- LEDGER -->
            ${pag.ledger?.length ? renderLedger(pag.ledger) : ''}

        </div>
    `;

    bindEventos(pag);

    // Após render, verifica avaliação pendente para status Liberado
    if (pag.status === 'Liberado' && pag.contratoServicoId) {
        carregarBotaoAvaliacao(pag.contratoServicoId);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// AVALIAÇÃO PENDENTE (status Liberado)
// ──────────────────────────────────────────────────────────────────────────────

async function carregarBotaoAvaliacao(contratoServicoId) {
    try {
        const pendente = await api.get(`/api/contratos/${contratoServicoId}/avaliacoes/pendente`);
        if (!pendente?.id || pendente.jaEnviou) return;

        const container = document.querySelector('.pag-container');
        if (!container) return;

        const secao = document.createElement('section');
        secao.className = 'pag-secao pag-avaliar';
        secao.innerHTML = `
            <h2>Avalie o serviço</h2>
            <p>O pagamento foi liberado. Compartilhe sua experiência avaliando o prestador.</p>
            <a href="/pages/avaliacao/enviar.html?id=${encodeURIComponent(pendente.id)}" class="btn-avaliar">
                Avaliar serviço
            </a>`;
        container.appendChild(secao);
    } catch {
        // Sem avaliação pendente ou usuário não é contratante — silencia
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// BANNER DE CONFIRMAÇÃO (PIX recebido — status Retido ou Liberado)
// ──────────────────────────────────────────────────────────────────────────────

function renderConfirmacao(pag) {
    // Só exibe quando o PIX já foi recebido (escrow). 'Retido' e 'Liberado' são
    // os estados em que o pagamento está efetivamente confirmado.
    if (!['Retido', 'Liberado'].includes(pag.status)) return '';

    const dataConfirmacao = pag.pagoEm
        ? new Date(pag.pagoEm).toLocaleString('pt-BR')
        : null;

    // Número amigável do pagamento (primeiros 8 caracteres do GUID).
    const numero = pag.id ? `#${String(pag.id).slice(0, 8).toUpperCase()}` : '—';

    return `
        <section class="pag-confirmado" role="status" aria-live="polite">
            <span class="pag-confirmado-icone" aria-hidden="true"><i class="fa-solid fa-check"></i></span>
            <div class="pag-confirmado-texto">
                <h2>Pagamento confirmado com sucesso.</h2>
                <p class="pag-confirmado-detalhes">
                    ${dataConfirmacao ? `Confirmado em <strong>${escapeHtml(dataConfirmacao)}</strong> · ` : ''}
                    Pagamento <strong class="mono">${escapeHtml(numero)}</strong>
                </p>
            </div>
        </section>
    `;
}

// ──────────────────────────────────────────────────────────────────────────────
// SEÇÃO PIX
// ──────────────────────────────────────────────────────────────────────────────

function renderPixSection(pag) {
    if (!['Criado', 'Aguardando'].includes(pag.status)) return '';
    if (!pag.pixPayload && !pag.pixQrCodeImagem) return '';

    const expiracao = pag.pixExpiracao
        ? `<p class="pix-expiracao">Expira em: <strong>${new Date(pag.pixExpiracao).toLocaleString('pt-BR')}</strong></p>`
        : '';

    return `
        <section class="pag-secao pag-pix">
            <h2>Escaneie ou copie o código PIX</h2>
            <p class="pix-instrucao">Abra o aplicativo do seu banco, escolha a opção PIX e escaneie o QR Code ou cole o código abaixo.</p>

            ${pag.pixQrCodeImagem ? `
            <div class="pix-qr-wrap">
                <img src="${pag.pixQrCodeImagem}" alt="QR Code PIX" class="pix-qr-img">
            </div>` : ''}

            ${pag.pixPayload ? `
            <div class="pix-copia-cola">
                <input id="pix-payload" type="text" value="${escapeHtml(pag.pixPayload)}" readonly
                       class="pix-payload-input" aria-label="Código PIX copia e cola">
                <button id="btn-copiar-pix" class="btn-copiar">Copiar código</button>
            </div>` : ''}

            ${expiracao}

            <div class="pix-aviso">
                <strong>Importante:</strong> após o pagamento, aguarde a confirmação automática.
                O valor ficará <strong>retido em escrow</strong> até você confirmar a entrega do serviço.
            </div>

            <button id="btn-atualizar-pix" class="btn-secundario btn-atualizar">Atualizar status</button>
        </section>
    `;
}

// ──────────────────────────────────────────────────────────────────────────────
// AÇÕES
// ──────────────────────────────────────────────────────────────────────────────

function renderAcoes(pag) {
    const acoes = [];

    // Liberar: apenas contratante, quando Retido
    if (pag.status === 'Retido' && isContratante !== false) {
        acoes.push(`<button id="btn-liberar" class="btn-liberar">Confirmar entrega e liberar</button>`);
    }

    // Abrir disputa: qualquer parte, quando Retido ou EmDisputa (nova disputa)
    if (['Retido', 'EmDisputa'].includes(pag.status)) {
        acoes.push(`<button id="btn-disputar" class="btn-disputar">Abrir disputa</button>`);
    }

    // Solicitar estorno: apenas contratante, enquanto não liberado
    if (['Aguardando', 'Retido'].includes(pag.status) && isContratante !== false) {
        acoes.push(`<button id="btn-estornar" class="btn-estornar">Solicitar estorno</button>`);
    }

    if (!acoes.length) return '';
    return `<div class="pag-acoes">${acoes.join('')}</div>`;
}

// ──────────────────────────────────────────────────────────────────────────────
// DISPUTAS
// ──────────────────────────────────────────────────────────────────────────────

function renderDisputas(disputas) {
    return `
        <section class="pag-secao">
            <h2>Disputas</h2>
            <div class="disputas-lista">
                ${disputas.map(d => `
                <div class="disputa-card">
                    <div class="disputa-header">
                        <span class="disputa-status disputa-status-${d.status?.toLowerCase()}">${DISPUTA_STATUS_LABEL[d.status] || d.status}</span>
                        <span class="disputa-data">${new Date(d.abertoEm).toLocaleDateString('pt-BR')}</span>
                    </div>
                    <p class="disputa-motivo">${escapeHtml(d.motivo)}</p>
                    ${d.notaResolucao ? `<p class="disputa-resolucao"><strong>Resolução:</strong> ${escapeHtml(d.notaResolucao)}</p>` : ''}
                    ${d.resolvidaEm ? `<p class="disputa-data-resolucao">Resolvida em: ${new Date(d.resolvidaEm).toLocaleDateString('pt-BR')}</p>` : ''}
                </div>`).join('')}
            </div>
        </section>`;
}

// ──────────────────────────────────────────────────────────────────────────────
// LEDGER
// ──────────────────────────────────────────────────────────────────────────────

function renderLedger(ledger) {
    const fmtBRL = v => Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });

    return `
        <section class="pag-secao">
            <h2>Histórico financeiro</h2>
            <table class="ledger-tabela">
                <thead>
                    <tr>
                        <th>Tipo</th>
                        <th>Descrição</th>
                        <th>Valor</th>
                        <th>Data</th>
                    </tr>
                </thead>
                <tbody>
                    ${ledger.map(e => `
                    <tr>
                        <td><span class="ledger-tipo">${LEDGER_LABEL[e.tipo] || e.tipo}</span></td>
                        <td class="ledger-desc">${escapeHtml(e.descricao)}</td>
                        <td class="${Number(e.valor) >= 0 ? 'ledger-positivo' : 'ledger-negativo'}">
                            ${fmtBRL(e.valor)}
                        </td>
                        <td>${new Date(e.criadoEm).toLocaleDateString('pt-BR')}</td>
                    </tr>`).join('')}
                </tbody>
            </table>
        </section>`;
}

// ──────────────────────────────────────────────────────────────────────────────
// EVENTOS
// ──────────────────────────────────────────────────────────────────────────────

function bindEventos(pag) {
    // Copiar código PIX
    document.getElementById('btn-copiar-pix')?.addEventListener('click', () => {
        const input = document.getElementById('pix-payload');
        if (!input) return;
        navigator.clipboard.writeText(input.value)
            .then(() => showToast('Código PIX copiado!'))
            .catch(() => {
                input.select();
                document.execCommand('copy');
                showToast('Código PIX copiado!');
            });
    });

    // Atualizar PIX
    document.getElementById('btn-atualizar-pix')?.addEventListener('click', atualizarPix);

    // Abrir modais
    document.getElementById('btn-liberar')?.addEventListener('click', () => {
        document.getElementById('modal-liberar').style.display = 'flex';
    });
    document.getElementById('btn-disputar')?.addEventListener('click', () => {
        document.getElementById('modal-disputar').style.display = 'flex';
    });
    document.getElementById('btn-estornar')?.addEventListener('click', () => {
        document.getElementById('modal-estornar').style.display = 'flex';
    });
}

// ──────────────────────────────────────────────────────────────────────────────
// CONFIGURAR MODAIS (fixos no HTML)
// ──────────────────────────────────────────────────────────────────────────────

function configurarModais() {
    document.getElementById('modal-liberar-cancelar')?.addEventListener('click', () => {
        document.getElementById('modal-liberar').style.display = 'none';
    });
    document.getElementById('modal-liberar-btn')?.addEventListener('click', liberarPagamento);

    document.getElementById('modal-disputar-cancelar')?.addEventListener('click', () => {
        document.getElementById('modal-disputar').style.display = 'none';
    });
    document.getElementById('modal-disputar-btn')?.addEventListener('click', abrirDisputa);

    document.getElementById('modal-estornar-cancelar')?.addEventListener('click', () => {
        document.getElementById('modal-estornar').style.display = 'none';
    });
    document.getElementById('modal-estornar-btn')?.addEventListener('click', solicitarEstorno);
}

// ──────────────────────────────────────────────────────────────────────────────
// AÇÕES ASSÍNCRONAS
// ──────────────────────────────────────────────────────────────────────────────

async function atualizarPix() {
    const btn = document.getElementById('btn-atualizar-pix');
    if (btn) { btn.disabled = true; btn.textContent = 'Atualizando...'; }
    try {
        const pix = await api.get(`/api/pagamentos/${pagamentoId}/pix`);
        // Mescla dados do PIX atualizado com o estado atual
        pagamentoAtual = { ...pagamentoAtual, ...pix, status: pix.status ?? pagamentoAtual.status };
        renderPagamento(pagamentoAtual);
        configurarModais();
        showToast('Status atualizado.');
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao atualizar.', true);
        if (btn) { btn.disabled = false; btn.textContent = 'Atualizar status'; }
    }
}

async function liberarPagamento() {
    const obs = document.getElementById('obs-liberacao')?.value?.trim() || null;
    document.getElementById('modal-liberar').style.display = 'none';

    const btn = document.getElementById('modal-liberar-btn');
    if (btn) btn.disabled = true;

    try {
        await api.post(`/api/pagamentos/${pagamentoId}/liberar`, { observacaoContratante: obs });
        showToast('Pagamento liberado ao prestador!');
        const pag = await api.get(`/api/pagamentos/${pagamentoId}`);
        pagamentoAtual = pag;
        renderPagamento(pag);
        configurarModais();
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao liberar pagamento.', true);
        if (btn) btn.disabled = false;
    }
}

async function abrirDisputa() {
    const motivo = document.getElementById('disputa-motivo')?.value?.trim();
    if (!motivo) {
        showToast('Informe o motivo da disputa.', true);
        return;
    }

    document.getElementById('modal-disputar').style.display = 'none';
    const btn = document.getElementById('modal-disputar-btn');
    if (btn) btn.disabled = true;

    try {
        await api.post(`/api/pagamentos/${pagamentoId}/disputar`, { motivo, evidencias: [] });
        showToast('Disputa aberta com sucesso.');
        const pag = await api.get(`/api/pagamentos/${pagamentoId}`);
        pagamentoAtual = pag;
        renderPagamento(pag);
        configurarModais();
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao abrir disputa.', true);
        if (btn) btn.disabled = false;
    }
}

async function solicitarEstorno() {
    document.getElementById('modal-estornar').style.display = 'none';
    const btn = document.getElementById('modal-estornar-btn');
    if (btn) btn.disabled = true;

    try {
        await api.post(`/api/pagamentos/${pagamentoId}/estornar`, {});
        showToast('Solicitação de estorno enviada.');
        const pag = await api.get(`/api/pagamentos/${pagamentoId}`);
        pagamentoAtual = pag;
        renderPagamento(pag);
        configurarModais();
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao solicitar estorno.', true);
        if (btn) btn.disabled = false;
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// UTILITÁRIOS
// ──────────────────────────────────────────────────────────────────────────────

function showToast(msg, isError = false) {
    const t = document.createElement('div');
    t.className = `toast ${isError ? 'toast-erro' : 'toast-ok'}`;
    t.textContent = msg;
    document.body.appendChild(t);
    setTimeout(() => t.remove(), 4000);
}

function escapeHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

init();
