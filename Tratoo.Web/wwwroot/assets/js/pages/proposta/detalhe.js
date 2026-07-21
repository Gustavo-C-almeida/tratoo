import { api } from '/assets/js/services/api.js';
// ── Detalhe e negociação de proposta (v3 - Melhorada) ───────────────────────

const root = () => document.getElementById('proposta-root');

// ────────────────────────────────────────────────────────────────────────────
// TOAST NOTIFICATION SYSTEM
// ────────────────────────────────────────────────────────────────────────────
class Toast {
    constructor() {
        this.container = this.createContainer();
    }

    createContainer() {
        let container = document.querySelector('.toast-container');
        if (!container) {
            container = document.createElement('div');
            container.className = 'toast-container';
            document.body.appendChild(container);
        }
        return container;
    }

    show(message, type = 'info', duration = 4000) {
        const toast = document.createElement('div');
        toast.className = `toast toast-${type}`;

        const icon = type === 'success' ? '<i class="fa-solid fa-circle-check"></i>' : type === 'error' ? '<i class="fa-solid fa-circle-xmark"></i>' : '<i class="fa-solid fa-circle-info"></i>';
        toast.innerHTML = `<span style="font-size: 18px; font-weight: bold;">${icon}</span> ${message}`;

        toast.onclick = () => this.remove(toast);

        this.container.appendChild(toast);

        setTimeout(() => this.remove(toast), duration);

        return toast;
    }

    remove(toast) {
        toast.style.opacity = '0';
        toast.style.transform = 'translateX(400px)';
        setTimeout(() => toast.remove(), 300);
    }
}

const toast = new Toast();

// ────────────────────────────────────────────────────────────────────────────
// UTILITÁRIOS
// ────────────────────────────────────────────────────────────────────────────
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

function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

const STATUS_LABEL = {
    Draft: 'Rascunho', Submitted: 'Aguardando análise',
    EmNegociacao: 'Em negociação', Aceita: 'Aceita',
    Recusada: 'Recusada', Expirada: 'Expirada', Convertida: 'Convertida'
};

let proposta = null;
let usuarioId = null;
let ehContratante = false;
let pixConfigurado = true; // assume true; atualizado no carregar (apenas para prestadores)
let configFinanceiro = null; // taxa operacional estimada do gateway
let currentStep = 1;
const totalSteps = 3;

function tempoRestante(validoAte) {
    if (!validoAte) return '';
    const diff = new Date(validoAte).getTime() - Date.now();
    if (diff <= 0) return '<span class="validade-expirada">Expirada</span>';
    const dias = Math.floor(diff / 86400000);
    if (dias > 7) return `<span class="validade-ok">Expira em ${dias} dias</span>`;
    if (dias >= 1) return `<span class="validade-alerta">Expira em ${dias} dia${dias > 1 ? 's' : ''}</span>`;
    const horas = Math.floor(diff / 3600000);
    return `<span class="validade-alerta">Expira em ${horas}h</span>`;
}

function tempoAtras(data) {
    if (!data) return '';
    const diff = Date.now() - new Date(data).getTime();
    const min = Math.floor(diff / 60000);
    if (min < 1) return 'agora mesmo';
    if (min < 60) return `há ${min} min`;
    const h = Math.floor(min / 60);
    if (h < 24) return `há ${h}h`;
    const d = Math.floor(h / 24);
    return `há ${d} dia${d > 1 ? 's' : ''}`;
}

// Loading state helper
function setLoading(button, isLoading) {
    if (isLoading) {
        button.classList.add('loading');
        button.disabled = true;
    } else {
        button.classList.remove('loading');
        button.disabled = false;
    }
}

// ────────────────────────────────────────────────────────────────────────────
// CARREGAMENTO INICIAL
// ────────────────────────────────────────────────────────────────────────────
async function carregar() {
    const id = new URLSearchParams(window.location.search).get('id');
    if (!id) {
        root().innerHTML = '<p class="msg-centro erro">ID da proposta não informado.</p>';
        return;
    }

    root().innerHTML = '<div class="msg-centro">Carregando...</div>';

    try {
        proposta = await api.get(`/api/propostas/${id}`);
    } catch {
        root().innerHTML = '<p class="msg-centro erro">Proposta não encontrada ou sem permissão de acesso.</p>';
        return;
    }

    try {
        const me = await api.get('/api/me');
        usuarioId = me.id;
        ehContratante = usuarioId === proposta.contratanteId;
    } catch { /* não autenticado */ }

    // Verifica PIX apenas para prestadores (contratante não precisa)
    if (!ehContratante && usuarioId) {
        try {
            const db = await api.get('/prestadores/me/dados-bancarios');
            pixConfigurado = !!(db && db.configurado);
        } catch { pixConfigurado = false; }
    }

    // Carrega configuração financeira para exibir transparência de taxa operacional
    try {
        configFinanceiro = await api.get('/api/config/financeiro');
    } catch { configFinanceiro = null; }

    document.title = `Proposta — ${esc(proposta.projetoTitulo)} — Tratoo`;
    render();
}

// ────────────────────────────────────────────────────────────────────────────
// RENDER PRINCIPAL
// ────────────────────────────────────────────────────────────────────────────
function render() {
    const v = proposta.versaoAtiva;
    const podeNegociar = ['Submitted', 'EmNegociacao'].includes(proposta.status);
    const podeRecusarContratante = ehContratante && podeNegociar;
    const podeAceitarContratante = ehContratante && podeNegociar && !podeAceitarComoPresrador();

    // Calcular métricas para o sidebar
    const tempoMedioResposta = calcularTempoMedioResposta();
    const totalContrapropostas = proposta.versoes?.length - 1 || 0;

    root().innerHTML = `
    <a href="#main-content" class="skip-to-main">Pular para conteúdo principal</a>
    
    <div class="prop-header">
        <a href="javascript:history.back()" class="back-link"><i class="fa-solid fa-arrow-left"></i> Voltar</a>
        <div class="prop-titulo-bloco">
            <h1>${esc(proposta.projetoTitulo)}</h1>
            <div class="prop-sub">Proposta de <strong>${esc(proposta.prestadorNome)}</strong></div>
            <div class="prop-badges">
                <span class="status-badge status-${proposta.status.toLowerCase()}">${STATUS_LABEL[proposta.status] || proposta.status}</span>
                ${tempoRestante(proposta.validoAte)}
                ${proposta.atualizadoEm ? `<span class="hint">Última atualização ${tempoAtras(proposta.atualizadoEm)}</span>` : ''}
            </div>
        </div>
    </div>

    <div class="prop-layout" id="main-content">
        <main class="prop-main">
            ${v ? renderVersao(v, true) : '<p class="hint">Nenhum conteúdo de proposta ainda.</p>'}

            ${proposta.motivoCancelamento ? `
            <div class="msg-erro" style="margin-top:16px" role="alert">
                <strong>Motivo de cancelamento:</strong> ${esc(proposta.motivoCancelamento)}
            </div>` : ''}

            <div class="versoes-section">
                <button id="btn-versoes" class="btn-texto" onclick="toggleVersoes()">
                    Ver histórico de versões (${proposta.versoes?.length || 0})
                </button>
                <div id="lista-versoes" style="display:none"></div>
            </div>
        </main>

        <aside class="prop-sidebar">
            <!-- Métricas Visuais -->
            <div class="proposta-metrics">
                <div class="metric-item">
                    <span class="metric-label"><i class="fa-solid fa-money-bill-wave"></i> Valor</span>
                    <span class="metric-value">${v ? moeda(v.valorTotal) : '—'}</span>
                </div>
                <div class="metric-item">
                    <span class="metric-label"><i class="fa-solid fa-arrows-rotate"></i> Versões</span>
                    <span class="metric-value">${proposta.versoes?.length || 1}</span>
                </div>
                <div class="metric-item">
                    <span class="metric-label"><i class="fa-solid fa-bolt"></i> Contrapropostas</span>
                    <span class="metric-value">${totalContrapropostas}</span>
                </div>
            </div>

            <div class="prop-valores">
                <div class="prazo-info"><i class="fa-solid fa-calendar-days"></i> Prazo: <strong>${v ? dataFmt(v.prazoTotal) : '—'}</strong></div>
                <div class="prazo-info"><i class="fa-solid fa-credit-card"></i> Pagamento: <strong>${esc(v?.formaPagamento || 'PIX')}</strong></div>
                <div class="prazo-info"><i class="fa-solid fa-arrows-rotate"></i> Revisões: <strong>${v?.revisoesInclusas ?? '—'}</strong></div>
            </div>

            ${tempoMedioResposta ? `
            <div class="response-time">
                <div class="metric-label"><i class="fa-solid fa-stopwatch"></i> Tempo médio de resposta</div>
                <div class="time-value">${tempoMedioResposta}</div>
            </div>` : ''}

            <!-- Ações do contratante -->
            ${podeRecusarContratante ? `
            <div id="acoes-contratante" class="acoes-bloco">
                ${podeAceitarContratante ? `<button id="btn-aceitar" class="btn-aceitar" onclick="abrirModalAceite()" data-tooltip="Aceitar esta proposta"><i class="fa-solid fa-check"></i> Aceitar proposta</button>` : `<div class="aviso-turno"><i class="fa-solid fa-clock" aria-hidden="true"></i> Aguardando resposta da outra parte</div>`}
                <button id="btn-recusar" class="btn-recusar" onclick="mostrarFormRecusar()" data-tooltip="Recusar esta proposta"><i class="fa-solid fa-xmark"></i> Recusar</button>
                <!-- Formulário de Recusa Moderno -->
                <div id="form-recusar" class="recusar-form-modern" style="display:none">
                    <div class="recusar-form-container">
                        <div class="recusar-form-header">
                            <div class="recusar-header-icon">
                                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <circle cx="12" cy="12" r="10"/>
                                    <line x1="18" y1="6" x2="6" y2="18"/>
                                    <line x1="6" y1="6" x2="18" y2="18"/>
                                </svg>
                            </div>
                            <div class="recusar-header-text">
                                <h4>Recusar Proposta</h4>
                                <p>Conte-nos o motivo para melhorarmos</p>
                            </div>
                        </div>

                        <!-- Motivos Rápidos -->
                        <div class="motivos-rapidos-modern">
                            <button type="button" class="motivo-chip-modern" onclick="selecionarMotivo('Fora do orçamento')">
                                <span class="chip-icon"><i class="fa-solid fa-money-bill-wave"></i></span>
                                Fora do orçamento
                            </button>
                            <button type="button" class="motivo-chip-modern" onclick="selecionarMotivo('Prazo muito longo')">
                                <span class="chip-icon"><i class="fa-solid fa-clock"></i></span>
                                Prazo muito longo
                            </button>
                            <button type="button" class="motivo-chip-modern" onclick="selecionarMotivo('Escopo insuficiente')">
                                <span class="chip-icon"><i class="fa-solid fa-clipboard-list"></i></span>
                                Escopo insuficiente
                            </button>
                            <button type="button" class="motivo-chip-modern" onclick="selecionarMotivo('Valor acima do esperado')">
                                <span class="chip-icon"><i class="fa-solid fa-money-bill-transfer"></i></span>
                                Valor acima do esperado
                            </button>
                            <button type="button" class="motivo-chip-modern" onclick="selecionarMotivo('Já contratei outro profissional')">
                                <span class="chip-icon"><i class="fa-solid fa-check"></i></span>
                                Já contratei outro
                            </button>
                        </div>

                        <!-- Ou escreva seu motivo -->
                        <div class="recusar-divider">
                            <span>ou descreva seu motivo</span>
                        </div>

                        <!-- Textarea Moderno -->
                        <div class="recusar-textarea-wrapper">
                            <textarea
                                id="motivo-recusar"
                                class="recusar-textarea-modern"
                                rows="4"
                                placeholder="Descreva detalhadamente o motivo da recusa..."
                                aria-label="Motivo da recusa"
                                maxlength="500"
                            ></textarea>
                            <div class="recusar-textarea-footer">
                                <span class="recusar-char-counter" id="recusar-char-counter">0/500</span>
                                <span class="recusar-hint">Opcional, mas ajuda a melhorar</span>
                            </div>
                        </div>

                        <!-- Botões de Ação -->
                        <div class="recusar-form-actions">
                            <button type="button" class="recusar-btn-cancelar" onclick="toggleRecusarForm()">
                                Cancelar
                            </button>
                            <button type="button" class="recusar-btn-confirmar" onclick="confirmarRecusa()">
                                <span class="btn-icon"><i class="fa-solid fa-xmark"></i></span>
                                Confirmar recusa
                            </button>
                        </div>
                    </div>
                </div>
            </div>` : ''}

            <!-- Ações do prestador -->
            ${!ehContratante && !pixConfigurado && ['Draft','Submitted','EmNegociacao'].includes(proposta.status) ? `
            <div class="aviso-pix-banner" style="background:#fef9c3;border:1px solid #fde047;border-radius:10px;padding:12px 14px;margin-bottom:12px;font-size:13px;color:#78350f;line-height:1.5">
                <strong>Chave PIX não configurada</strong><br>
                Para enviar ou aceitar propostas você precisa cadastrar sua chave PIX.
                <a href="/pages/prestador/editar-perfil.html" style="color:#92400e;font-weight:700;display:block;margin-top:6px">
                    <i class="fa-solid fa-arrow-right"></i> Cadastrar dados bancários
                </a>
            </div>` : ''}

            ${!ehContratante && proposta.status === 'Draft' ? `
            <div class="acoes-bloco">
                <button class="btn-enviar" onclick="enviarProposta()" ${!pixConfigurado ? 'disabled title="Cadastre sua chave PIX antes de enviar propostas"' : ''} data-tooltip="Enviar proposta para análise"><i class="fa-solid fa-paper-plane"></i> Enviar proposta</button>
                <button class="btn-secundario" style="margin-top:8px;width:100%" onclick="mostrarFormContraproposta()"><i class="fa-solid fa-pen"></i> Editar rascunho</button>
            </div>` : ''}

            ${!ehContratante && ['Submitted', 'EmNegociacao'].includes(proposta.status) ? `
            <div class="acoes-bloco">
                ${podeAceitarComoPresrador() ? `<button class="btn-aceitar" style="width:100%;margin-bottom:8px" onclick="abrirModalAceitePrestador()" ${!pixConfigurado ? 'disabled title="Cadastre sua chave PIX antes de aceitar"' : ''}><i class="fa-solid fa-check"></i> Aceitar proposta do contratante</button>` : ''}
                ${podeAceitarComoPresrador() ? `<button class="btn-secundario" style="width:100%;margin-bottom:8px" onclick="mostrarFormContraproposta()"><i class="fa-solid fa-arrows-rotate"></i> Fazer contraproposta</button>` : ''}
                <button class="btn-recusar" style="margin-top:8px;width:100%" onclick="cancelarProposta()"><i class="fa-solid fa-trash-can"></i> Cancelar proposta</button>
            </div>` : ''}

            ${ehContratante && podeNegociar && !podeAceitarComoPresrador() ? `
            <div class="acoes-bloco" style="margin-top:16px">
                <button class="btn-secundario" style="width:100%" onclick="mostrarFormContraproposta()"><i class="fa-solid fa-arrows-rotate"></i> Fazer contraproposta</button>
            </div>` : ''}

            ${proposta.status === 'Convertida' ? `
            <div class="acoes-bloco" style="margin-top:12px;text-align:center">
                <a href="/pages/me/contratos.html" class="btn-ver-contrato" style="display:inline-block;padding:0.45rem 1rem;background:#16a34a;color:#fff;border-radius:7px;font-size:0.85rem;font-weight:600;text-decoration:none">
                    <i class="fa-solid fa-file-lines"></i> Ver contrato gerado <i class="fa-solid fa-arrow-right"></i>
                </a>
            </div>` : ''}

            ${!['Draft','Recusada','Expirada'].includes(proposta.status) ? `
            <div class="acoes-bloco" style="margin-top:16px;text-align:center">
                <a href="/pages/chat/detalhe.html?projetoId=${proposta.projetoId}&prestadorId=${proposta.prestadorId}"
                   style="display:inline-block;width:100%;padding:0.5rem 1rem;background:#1E293B;color:#fff;border-radius:7px;font-size:0.85rem;font-weight:600;text-decoration:none;box-sizing:border-box">
                    <i class="fa-solid fa-comments"></i> Abrir conversa
                </a>
            </div>` : ''}

            ${!ehContratante ? `<a href="../prestador/minhas-propostas.html" class="btn-texto" style="display:block;margin-top:16px;text-align:center"><i class="fa-solid fa-clipboard-list"></i> Ver minhas propostas</a>` : ''}
        </aside>
    </div>

    <!-- Modal de confirmação -->
    <div id="modal-confirmar" class="modal-overlay" style="display:none" role="dialog" aria-modal="true">
        <div class="modal-box">
            <h3>Confirmar aceite da proposta</h3>
            <p>Revise os termos antes de confirmar:</p>
            <div id="modal-escopo-resumo" class="modal-escopo"></div>
            <div class="modal-actions">
                <button id="modal-cancelar" class="btn-secundario">Cancelar</button>
                <button id="modal-confirmar-btn" class="btn-aceitar">Confirmar aceite</button>
            </div>
        </div>
    </div>

    <!-- Formulário de contraproposta moderno -->
    <div id="form-contraproposta-wrap" class="form-contraproposta-modern" style="display:none">
        <div class="form-container">
            <div class="form-header">
                <h3><i class="fa-solid fa-arrows-rotate"></i> Nova Contraproposta</h3>
                <button class="close-form" onclick="fecharFormContraproposta()" aria-label="Fechar"><i class="fa-solid fa-xmark"></i></button>
            </div>
            
            <div class="form-stepper">
                <div class="step active" data-step="1">
                    <div class="step-number">1</div>
                    <div class="step-label">Objetivo & Escopo</div>
                </div>
                <div class="step" data-step="2">
                    <div class="step-number">2</div>
                    <div class="step-label">Valores & Prazos</div>
                </div>
                <div class="step" data-step="3">
                    <div class="step-number">3</div>
                    <div class="step-label">Revisão</div>
                </div>
            </div>

            <div id="msg-contra"></div>
            
            <form id="form-contra" class="form-proposta">
                <!-- Passo 1 -->
                <div class="form-step" data-step="1">
                    <div class="campo">
                        <label>Objetivo da proposta <span class="obr">*</span></label>
                        <input id="c-objetivo" type="text" value="${esc(v?.objetivo || '')}" required>
                        <div class="validation-message"></div>
                    </div>
                    <div class="campo">
                        <label>Escopo detalhado <span class="obr">*</span></label>
                        <textarea id="c-escopo" rows="4" required>${esc(v?.escopo || '')}</textarea>
                        <div class="validation-message"></div>
                    </div>
                    <div class="campo">
                        <label>Exclusões (opcional)</label>
                        <input id="c-exclusoes" type="text" value="${esc(v?.exclusoes || '')}">
                    </div>
                </div>

                <!-- Passo 2 -->
                <div class="form-step" data-step="2" style="display:none">
                    <div class="campo">
                        <label>Revisões inclusas <span class="obr">*</span></label>
                        <input id="c-revisoes" type="number" min="1" max="10" value="${v?.revisoesInclusas ?? 2}" required>
                    </div>
                    <div class="campo-row">
                        <div class="campo">
                            <label>Valor total (R$) <span class="obr">*</span></label>
                            <input id="c-valor" type="number" min="1" step="0.01" value="${v?.valorTotal ?? ''}" required>
                        </div>
                    </div>
                    <div class="campo">
                        <label>Forma de pagamento</label>
                        <select id="c-pagamento">
                            <option value="PIX" ${v?.formaPagamento === 'PIX' ? 'selected' : ''}>PIX</option>
                            <option value="Transferência" ${v?.formaPagamento === 'Transferência' ? 'selected' : ''}>Transferência bancária</option>
                            <option value="Boleto" ${v?.formaPagamento === 'Boleto' ? 'selected' : ''}>Boleto</option>
                        </select>
                    </div>
                    <div class="campo">
                        <label>Prazo de entrega <span class="obr">*</span></label>
                        <input id="c-prazo" type="date" value="${v ? dataISO(v.prazoTotal) : ''}" required>
                    </div>
                    <div class="campo">
                        <label>Observações adicionais</label>
                        <textarea id="c-obs" rows="2">${esc(v?.observacoes || '')}</textarea>
                    </div>
                </div>

                <!-- Passo 3 -->
                <div class="form-step" data-step="3" style="display:none">
                    <div class="live-preview">
                        <h4><i class="fa-solid fa-clipboard-list"></i> Pré-visualização da proposta</h4>
                        <div class="preview-values">
                            <div class="preview-item">
                                <div class="preview-label">Valor total</div>
                                <div class="preview-value" id="preview-valor">${moeda(v?.valorTotal || 0)}</div>
                            </div>
                            <div class="preview-item">
                                <div class="preview-label">Prazo</div>
                                <div class="preview-value" id="preview-prazo">${v ? dataFmt(v.prazoTotal) : '—'}</div>
                            </div>
                            <div class="preview-item">
                                <div class="preview-label">Revisões</div>
                                <div class="preview-value" id="preview-revisoes">${v?.revisoesInclusas ?? '—'}</div>
                            </div>
                        </div>
                    </div>
                    <p style="margin-top: 16px; color: var(--text-secondary); font-size: 13px;">
                        Ao enviar esta contraproposta, a outra parte será notificada e poderá aceitar, recusar ou fazer uma nova contraproposta.
                    </p>
                </div>

                <div class="btn-group" style="margin-top: 24px">
                    <button type="button" id="btn-prev" class="btn-secundario" style="display:none"><i class="fa-solid fa-arrow-left"></i> Anterior</button>
                    <button type="button" id="btn-next" class="btn-enviar btn-com-icone">Próximo <i class="fa-solid fa-arrow-right"></i></button>
                    <button type="submit" id="btn-submit" class="btn-enviar btn-com-icone" style="display:none">Enviar contraproposta</button>
                    <button type="button" class="btn-secundario" onclick="fecharFormContraproposta()">Cancelar</button>
                </div>
            </form>
        </div>
    </div>`;

    // Eventos
    document.getElementById('form-contra').addEventListener('submit', enviarContraproposta);

    // Setup do stepper
    setupStepper();
}

// ────────────────────────────────────────────────────────────────────────────
// HELPER: o prestador pode aceitar a última versão?
// Só é verdade quando a última versão foi criada pelo contratante
// ────────────────────────────────────────────────────────────────────────────
function podeAceitarComoPresrador() {
    if (!proposta.versoes || !proposta.versoes.length) return false;
    const ultima = proposta.versoes[proposta.versoes.length - 1];
    return ultima.criadoPor === proposta.contratanteId;
}

// ────────────────────────────────────────────────────────────────────────────
// MÉTRICAS
// ────────────────────────────────────────────────────────────────────────────
function calcularTempoMedioResposta() {
    if (!proposta.versoes || proposta.versoes.length < 2) return null;

    let totalDiferenca = 0;
    let contagem = 0;

    for (let i = 1; i < proposta.versoes.length; i++) {
        const atual = new Date(proposta.versoes[i].criadoEm);
        const anterior = new Date(proposta.versoes[i - 1].criadoEm);
        const diffHoras = Math.abs(atual - anterior) / (1000 * 60 * 60);

        if (diffHoras < 168) { // menos de 7 dias
            totalDiferenca += diffHoras;
            contagem++;
        }
    }

    if (contagem === 0) return null;

    const mediaHoras = totalDiferenca / contagem;

    if (mediaHoras < 1) return `${Math.round(mediaHoras * 60)} minutos`;
    if (mediaHoras < 24) return `${Math.round(mediaHoras)} horas`;
    return `${Math.round(mediaHoras / 24)} dias`;
}

// ────────────────────────────────────────────────────────────────────────────
// STEPPER DO FORMULÁRIO
// ────────────────────────────────────────────────────────────────────────────
function setupStepper() {
    const btnNext = document.getElementById('btn-next');
    const btnPrev = document.getElementById('btn-prev');
    const btnSubmit = document.getElementById('btn-submit');

    if (!btnNext) return;

    currentStep = 1;
    updateStepperDisplay();

    btnNext.onclick = () => {
        if (validateCurrentStep()) {
            if (currentStep < totalSteps) {
                currentStep++;
                updateStepperDisplay();
                updateLivePreview();
            }
        }
    };

    if (btnPrev) {
        btnPrev.onclick = () => {
            if (currentStep > 1) {
                currentStep--;
                updateStepperDisplay();
            }
        };
    }

    // Adicionar listeners para preview ao vivo
    const valorInput = document.getElementById('c-valor');
    const prazoInput = document.getElementById('c-prazo');
    const revisoesInput = document.getElementById('c-revisoes');

    if (valorInput) valorInput.addEventListener('input', () => updateLivePreview());
    if (prazoInput) prazoInput.addEventListener('change', () => updateLivePreview());
    if (revisoesInput) revisoesInput.addEventListener('input', () => updateLivePreview());
}

function updateStepperDisplay() {
    // Atualizar steps
    document.querySelectorAll('.step').forEach((step, index) => {
        const stepNum = index + 1;
        step.classList.remove('active', 'completed');
        if (stepNum < currentStep) {
            step.classList.add('completed');
        } else if (stepNum === currentStep) {
            step.classList.add('active');
        }
    });

    // Mostrar/esconder seções
    document.querySelectorAll('.form-step').forEach((step, index) => {
        step.style.display = index + 1 === currentStep ? 'block' : 'none';
    });

    // Mostrar/esconder botões
    const btnPrev = document.getElementById('btn-prev');
    const btnNext = document.getElementById('btn-next');
    const btnSubmit = document.getElementById('btn-submit');

    if (btnPrev) {
        btnPrev.style.display = currentStep > 1 ? 'inline-flex' : 'none';
    }

    if (btnNext) {
        btnNext.style.display = currentStep < totalSteps ? 'inline-flex' : 'none';
    }

    if (btnSubmit) {
        btnSubmit.style.display = currentStep === totalSteps ? 'inline-flex' : 'none';
    }
}

function validateCurrentStep() {
    let isValid = true;

    if (currentStep === 1) {
        const objetivo = document.getElementById('c-objetivo');
        const escopo = document.getElementById('c-escopo');

        if (!objetivo.value.trim()) {
            showValidationError(objetivo, 'Objetivo é obrigatório');
            isValid = false;
        } else {
            clearValidationError(objetivo);
        }

        if (!escopo.value.trim()) {
            showValidationError(escopo, 'Escopo é obrigatório');
            isValid = false;
        } else if (escopo.value.trim().length < 20) {
            showValidationError(escopo, 'Escopo deve ter pelo menos 20 caracteres');
            isValid = false;
        } else {
            clearValidationError(escopo);
        }
    }

    if (currentStep === 2) {
        const valor = document.getElementById('c-valor');
        const prazo = document.getElementById('c-prazo');
        const revisoes = document.getElementById('c-revisoes');

        if (!valor.value || parseFloat(valor.value) <= 0) {
            showValidationError(valor, 'Valor total é obrigatório e deve ser maior que zero');
            isValid = false;
        } else {
            clearValidationError(valor);
        }

        if (!prazo.value) {
            showValidationError(prazo, 'Prazo de entrega é obrigatório');
            isValid = false;
        } else if (new Date(prazo.value) < new Date()) {
            showValidationError(prazo, 'Prazo deve ser uma data futura');
            isValid = false;
        } else {
            clearValidationError(prazo);
        }

        if (revisoes.value && (parseInt(revisoes.value) < 1 || parseInt(revisoes.value) > 10)) {
            showValidationError(revisoes, 'Revisões devem ser entre 1 e 10');
            isValid = false;
        } else {
            clearValidationError(revisoes);
        }
    }

    return isValid;
}

function showValidationError(input, message) {
    input.classList.add('invalid');
    input.classList.remove('valid');
    const validationDiv = input.parentElement.querySelector('.validation-message');
    if (validationDiv) {
        validationDiv.textContent = message;
        validationDiv.classList.add('show', 'error');
    }
}

function clearValidationError(input) {
    input.classList.remove('invalid');
    input.classList.add('valid');
    const validationDiv = input.parentElement.querySelector('.validation-message');
    if (validationDiv) {
        validationDiv.classList.remove('show', 'error');
    }
}

function updateLivePreview() {
    const valor = document.getElementById('c-valor')?.value;
    const prazo = document.getElementById('c-prazo')?.value;
    const revisoes = document.getElementById('c-revisoes')?.value;

    const previewValor = document.getElementById('preview-valor');
    const previewPrazo = document.getElementById('preview-prazo');
    const previewRevisoes = document.getElementById('preview-revisoes');

    if (previewValor) {
        previewValor.textContent = moeda(parseFloat(valor) || 0);
    }
    if (previewPrazo) {
        previewPrazo.textContent = prazo ? dataFmt(prazo) : '—';
    }
    if (previewRevisoes) {
        previewRevisoes.textContent = revisoes || '—';
    }
}

// ────────────────────────────────────────────────────────────────────────────
// VERSÕES
// ────────────────────────────────────────────────────────────────────────────
function renderVersao(v, destaque = false) {
    return `
    <div class="versao-card ${destaque ? 'versao-ativa' : ''}">
        <div class="versao-header">
            <span class="versao-num"><i class="fa-solid fa-file-lines"></i> Versão ${v.versao}</span>
            <span class="hint">por ${esc(v.criadoPorNome)} — ${dataFmt(v.criadoEm)}</span>
        </div>
        <div class="versao-campo"><strong><i class="fa-solid fa-bullseye"></i> Objetivo:</strong> ${esc(v.objetivo)}</div>
        <div class="versao-campo"><strong><i class="fa-solid fa-clipboard-list"></i> Escopo:</strong><br><pre class="pre-wrap">${esc(v.escopo)}</pre></div>
        ${v.exclusoes ? `<div class="versao-campo"><strong><i class="fa-solid fa-ban"></i> Exclusões:</strong> ${esc(v.exclusoes)}</div>` : ''}
        <div class="versao-campo-row">
            <span><strong><i class="fa-solid fa-money-bill-wave"></i> Valor:</strong> ${moeda(v.valorTotal)}</span>
            <span><strong><i class="fa-solid fa-credit-card"></i> Pagamento:</strong> ${esc(v.formaPagamento)}</span>
        </div>
        <div class="versao-campo-row">
            <span><strong><i class="fa-solid fa-calendar-days"></i> Prazo:</strong> ${dataFmt(v.prazoTotal)}</span>
            <span><strong><i class="fa-solid fa-arrows-rotate"></i> Revisões:</strong> ${v.revisoesInclusas}</span>
        </div>
        ${v.observacoes ? `<div class="versao-campo"><strong><i class="fa-solid fa-pen-to-square"></i> Obs:</strong> ${esc(v.observacoes)}</div>` : ''}
    </div>`;
}

async function toggleVersoes() {
    const lista = document.getElementById('lista-versoes');
    if (lista.style.display === 'none') {
        if (!proposta.versoes?.length) {
            lista.innerHTML = '<p class="hint">Nenhuma versão registrada.</p>';
        } else if (!lista.hasChildNodes() || lista.children.length === 1) {
            lista.innerHTML = await renderVersoesLazy();
        }
        lista.style.display = 'block';
        const btn = document.getElementById('btn-versoes');
        if (btn) btn.textContent = 'Ocultar histórico';
    } else {
        lista.style.display = 'none';
        const btn = document.getElementById('btn-versoes');
        if (btn) btn.textContent = `Ver histórico de versões (${proposta.versoes?.length || 0})`;
    }
}

async function renderVersoesLazy() {
    const versoesRecentes = proposta.versoes.slice(-5).reverse();
    let html = versoesRecentes.map(v => renderVersao(v)).join('');

    if (proposta.versoes.length > 5) {
        html += `<button class="btn-texto" onclick="carregarMaisVersoes()" style="margin-top: 12px;">
            <i class="fa-solid fa-book"></i> Carregar mais ${proposta.versoes.length - 5} versões...
        </button>`;
    }

    return html;
}

function carregarMaisVersoes() {
    const lista = document.getElementById('lista-versoes');
    const todasVersoes = [...proposta.versoes].reverse();
    lista.innerHTML = todasVersoes.map(v => renderVersao(v)).join('');
}

// ────────────────────────────────────────────────────────────────────────────
// AÇÕES COM TOAST
// ────────────────────────────────────────────────────────────────────────────
// ────────────────────────────────────────────────────────────────────────────
// MODAL DE ACEITE MELHORADO - EXIBE MESMAS INFORMAÇÕES DO CARD VERSÃO ATIVA
// ────────────────────────────────────────────────────────────────────────────
function abrirModalAceite() {
    const v = proposta.versaoAtiva;
    const modalEscopo = document.getElementById('modal-escopo-resumo');

    if (modalEscopo && v) {
        // Renderiza o modal com o MESMO layout e informações do versao-card
        modalEscopo.innerHTML = `
            <div class="modal-versao-preview">
                <!-- Cabeçalho da versão -->
                <div class="modal-versao-header">
                    <div class="modal-versao-badge">
                        <span class="modal-versao-icon"><i class="fa-solid fa-star"></i></span>
                        <span class="modal-versao-title">Versão ${v.versao} - Proposta Ativa</span>
                    </div>
                    <div class="modal-versao-meta">
                        <span class="modal-meta-item">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <circle cx="12" cy="12" r="10"/>
                                <polyline points="12 6 12 12 16 14"/>
                            </svg>
                            ${dataFmt(v.criadoEm)}
                        </span>
                        <span class="modal-meta-item">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                                <circle cx="12" cy="7" r="4"/>
                            </svg>
                            ${esc(v.criadoPorNome)}
                        </span>
                    </div>
                </div>

                <!-- Objetivo -->
                <div class="modal-versao-section">
                    <div class="modal-section-icon"><i class="fa-solid fa-bullseye"></i></div>
                    <div class="modal-section-content">
                        <div class="modal-section-label">Objetivo da Proposta</div>
                        <div class="modal-section-value">${esc(v.objetivo)}</div>
                    </div>
                </div>

                <!-- Escopo -->
                <div class="modal-versao-section">
                    <div class="modal-section-icon"><i class="fa-solid fa-clipboard-list"></i></div>
                    <div class="modal-section-content">
                        <div class="modal-section-label">Escopo Detalhado</div>
                        <div class="modal-section-value modal-scope-text">${esc(v.escopo)}</div>
                    </div>
                </div>

                ${v.exclusoes ? `
                <!-- Exclusões -->
                <div class="modal-versao-section">
                    <div class="modal-section-icon"><i class="fa-solid fa-ban"></i></div>
                    <div class="modal-section-content">
                        <div class="modal-section-label">Exclusões</div>
                        <div class="modal-section-value">${esc(v.exclusoes)}</div>
                    </div>
                </div>` : ''}

                <!-- Transparência de taxa operacional -->
                ${configFinanceiro && v.valorTotal ? (() => {
                    const taxa = Math.round(v.valorTotal * configFinanceiro.taxaOperacionalEstimadaPercentual * 100) / 100;
                    const liquido = v.valorTotal - taxa;
                    return `
                <div class="resumo-taxa-op">
                    <div class="rto-linha"><span>Valor da proposta</span><strong>${moeda(v.valorTotal)}</strong></div>
                    <div class="rto-linha rto-taxa"><span>Taxa operacional estimada (${configFinanceiro.taxaOperacionalEstimadaDisplay})</span><span>− ${moeda(taxa)}</span></div>
                    <div class="rto-linha rto-liquido"><span>Estimativa do valor a receber</span><strong>${moeda(liquido)}</strong></div>
                    <p class="rto-obs">${esc(configFinanceiro.observacao)}</p>
                </div>`;
                })() : ''}

                <!-- Grid de Valores -->
                <div class="modal-versao-grid">
                    <div class="modal-grid-item">
                        <div class="modal-grid-icon"><i class="fa-solid fa-money-bill-wave"></i></div>
                        <div class="modal-grid-content">
                            <div class="modal-grid-label">Valor Total</div>
                            <div class="modal-grid-value">${moeda(v.valorTotal)}</div>
                        </div>
                    </div>
                    <div class="modal-grid-item">
                        <div class="modal-grid-icon"><i class="fa-solid fa-credit-card"></i></div>
                        <div class="modal-grid-content">
                            <div class="modal-grid-label">Forma de Pagamento</div>
                            <div class="modal-grid-value">${esc(v.formaPagamento)}</div>
                        </div>
                    </div>
                    <div class="modal-grid-item">
                        <div class="modal-grid-icon"><i class="fa-solid fa-calendar-days"></i></div>
                        <div class="modal-grid-content">
                            <div class="modal-grid-label">Prazo de Entrega</div>
                            <div class="modal-grid-value">${dataFmt(v.prazoTotal)}</div>
                        </div>
                    </div>
                    <div class="modal-grid-item">
                        <div class="modal-grid-icon"><i class="fa-solid fa-arrows-rotate"></i></div>
                        <div class="modal-grid-content">
                            <div class="modal-grid-label">Revisões Inclusas</div>
                            <div class="modal-grid-value">${v.revisoesInclusas} ${v.revisoesInclusas === 1 ? 'revisão' : 'revisões'}</div>
                        </div>
                    </div>
                </div>

                ${v.observacoes ? `
                <!-- Observações -->
                <div class="modal-versao-section">
                    <div class="modal-section-icon"><i class="fa-solid fa-pen-to-square"></i></div>
                    <div class="modal-section-content">
                        <div class="modal-section-label">Observações Adicionais</div>
                        <div class="modal-section-value">${esc(v.observacoes)}</div>
                    </div>
                </div>` : ''}

                <!-- Alerta de Confirmação -->
                <div class="modal-alert">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"/>
                        <line x1="12" y1="8" x2="12" y2="12"/>
                        <line x1="12" y1="16" x2="12.01" y2="16"/>
                    </svg>
                    <span>Ao confirmar, esta proposta será aceita e um contrato será gerado automaticamente.</span>
                </div>
            </div>
        `;
    } else if (modalEscopo) {
        modalEscopo.innerHTML = '<p class="modal-empty">Verifique os detalhes antes de confirmar.</p>';
    }

    const modal = document.getElementById('modal-confirmar');
    if (modal) {
        modal.style.display = 'flex';
        const cancelBtn = document.getElementById('modal-cancelar');
        const confirmBtn = document.getElementById('modal-confirmar-btn');
        if (cancelBtn) cancelBtn.onclick = () => { modal.style.display = 'none'; };
        if (confirmBtn) confirmBtn.onclick = confirmarAceite;
    }
}

async function confirmarAceite() {
    const btn = document.getElementById('modal-confirmar-btn');
    setLoading(btn, true);
    btn.textContent = 'Aceitando...';

    try {
        await api.put(`/api/propostas/${proposta.id}/aceitar`, {});
        const modal = document.getElementById('modal-confirmar');
        if (modal) modal.style.display = 'none';

        toast.show('<i class="fa-solid fa-circle-check"></i> Proposta aceita com sucesso!', 'success');

        try {
            const meContratos = await api.get('/api/me/contratos');
            const contratoGerado = meContratos.find(c =>
                c.status === 'Gerado' || c.status === 'AguardandoAssinatura');
            if (contratoGerado) {
                setTimeout(() => {
                    if (confirm('Um contrato foi gerado automaticamente. Deseja visualizá-lo agora?')) {
                        location.href = `/pages/contrato/detalhe.html?id=${contratoGerado.id}`;
                        return;
                    }
                }, 500);
            }
        } catch (_) { }

        setTimeout(() => window.location.reload(), 1500);
    } catch (err) {
        toast.show(err?.data?.mensagem || 'Erro ao aceitar proposta.', 'error');
        setLoading(btn, false);
        btn.textContent = 'Confirmar aceite';
    }
}

// ────────────────────────────────────────────────────────────────────────────
// ACEITE PELO PRESTADOR (contraproposta do contratante aceita)
// ────────────────────────────────────────────────────────────────────────────
function abrirModalAceitePrestador() {
    const ultima = proposta.versoes?.[proposta.versoes.length - 1];
    const modal = document.getElementById('modal-confirmar');
    const modalEscopo = document.getElementById('modal-escopo-resumo');

    if (modalEscopo && ultima) {
        modalEscopo.innerHTML = `
            <p style="margin-bottom:8px;font-size:0.85rem;color:var(--text-secondary)">Você está aceitando a versão ${ultima.versao} enviada pelo contratante.</p>
            <p><strong><i class="fa-solid fa-bullseye"></i> Objetivo:</strong> ${esc(ultima.objetivo)}</p>
            <p><strong><i class="fa-solid fa-money-bill-wave"></i> Valor:</strong> ${moeda(ultima.valorTotal)}</p>
            <p><strong><i class="fa-solid fa-calendar-days"></i> Prazo:</strong> ${dataFmt(ultima.prazoTotal)}</p>
            <p><strong><i class="fa-solid fa-arrows-rotate"></i> Revisões:</strong> ${ultima.revisoesInclusas}</p>`;
    }

    if (modal) {
        modal.querySelector('h3').textContent = 'Aceitar proposta do contratante';
        modal.style.display = 'flex';
        document.getElementById('modal-cancelar').onclick = () => { modal.style.display = 'none'; };
        document.getElementById('modal-confirmar-btn').onclick = confirmarAceitePrestador;
    }
}

async function confirmarAceitePrestador() {
    const btn = document.getElementById('modal-confirmar-btn');
    setLoading(btn, true);
    btn.textContent = 'Aceitando...';

    try {
        await api.put(`/api/propostas/${proposta.id}/aceitar-negociacao`, {});
        document.getElementById('modal-confirmar').style.display = 'none';
        toast.show('<i class="fa-solid fa-circle-check"></i> Proposta aceita! Um contrato foi gerado.', 'success');

        try {
            const meContratos = await api.get('/api/me/contratos');
            const contratoGerado = meContratos.find(c =>
                c.status === 'Gerado' || c.status === 'AguardandoAssinatura');
            if (contratoGerado) {
                setTimeout(() => {
                    if (confirm('Um contrato foi gerado automaticamente. Deseja visualizá-lo agora?')) {
                        location.href = `/pages/contrato/detalhe.html?id=${contratoGerado.id}`;
                        return;
                    }
                }, 500);
            }
        } catch (_) { }

        setTimeout(() => window.location.reload(), 1500);
    } catch (err) {
        toast.show(err?.data?.mensagem || 'Erro ao aceitar proposta.', 'error');
        setLoading(btn, false);
        btn.textContent = 'Confirmar aceite';
    }
}

function mostrarFormRecusar() {
    const form = document.getElementById('form-recusar');
    if (form) {
        form.style.display = form.style.display === 'none' ? 'block' : 'none';
    }
}

function selecionarMotivo(motivo) {
    const motivoTextarea = document.getElementById('motivo-recusar');
    if (motivoTextarea) {
        motivoTextarea.value = motivo;
    }
}

async function confirmarRecusa() {
    const motivo = document.getElementById('motivo-recusar')?.value.trim() || '';
    const btn = event.target;
    setLoading(btn, true);

    try {
        await api.put(`/api/propostas/${proposta.id}/recusar`, { motivo: motivo || null });
        toast.show('Proposta recusada.', 'info');
        setTimeout(() => window.location.reload(), 1000);
    } catch (err) {
        toast.show(err?.data?.mensagem || 'Erro ao recusar proposta.', 'error');
        setLoading(btn, false);
    }
}

async function enviarProposta() {
    if (!confirm('Deseja enviar esta proposta ao contratante?')) return;
    const btn = event.target;
    setLoading(btn, true);

    try {
        await api.post(`/api/propostas/${proposta.id}/enviar`, {});
        toast.show('Proposta enviada com sucesso!', 'success');
        setTimeout(() => window.location.reload(), 1000);
    } catch (err) {
        toast.show(err?.data?.mensagem || 'Erro ao enviar proposta.', 'error');
        setLoading(btn, false);
    }
}

async function cancelarProposta() {
    const motivo = prompt('Motivo do cancelamento (opcional):');
    if (motivo === null) return;

    const btn = event.target;
    setLoading(btn, true);

    try {
        await api.delete(`/api/propostas/${proposta.id}?motivo=${encodeURIComponent(motivo || '')}`);
        toast.show('Proposta cancelada.', 'info');
        setTimeout(() => history.back(), 1000);
    } catch (err) {
        toast.show(err?.data?.mensagem || 'Erro ao cancelar proposta.', 'error');
        setLoading(btn, false);
    }
}

// ────────────────────────────────────────────────────────────────────────────
// CONTRAPROPOSTA
// ────────────────────────────────────────────────────────────────────────────
function mostrarFormContraproposta() {
    const wrap = document.getElementById('form-contraproposta-wrap');
    if (wrap) {
        wrap.style.display = 'flex';
        currentStep = 1;
        updateStepperDisplay();
        document.body.style.overflow = 'hidden';
    }
}

function fecharFormContraproposta() {
    const wrap = document.getElementById('form-contraproposta-wrap');
    if (wrap) {
        wrap.style.display = 'none';
        document.body.style.overflow = '';
    }
}

async function enviarContraproposta(e) {
    e.preventDefault();
    const msg = document.getElementById('msg-contra');
    const btn = e.target.querySelector('[type=submit]');
    setLoading(btn, true);
    if (msg) msg.innerHTML = '';

    const payload = {
        objetivo: document.getElementById('c-objetivo').value.trim(),
        escopo: document.getElementById('c-escopo').value.trim(),
        exclusoes: document.getElementById('c-exclusoes').value.trim() || null,
        revisoesInclusas: parseInt(document.getElementById('c-revisoes').value),
        valorTotal: parseFloat(document.getElementById('c-valor').value),
        formaPagamento: document.getElementById('c-pagamento').value,
        prazoTotal: new Date(document.getElementById('c-prazo').value).toISOString(),
        observacoes: document.getElementById('c-obs').value.trim() || null,
    };

    try {
        await api.post(`/api/propostas/${proposta.id}/contraproposta`, payload);
        toast.show('Contraproposta enviada! A outra parte foi notificada.', 'success');
        fecharFormContraproposta();
        setTimeout(() => window.location.reload(), 1500);
    } catch (err) {
        if (msg) {
            msg.innerHTML = `<div class="msg-erro">${esc(err?.data?.mensagem || 'Erro ao enviar contraproposta.')}</div>`;
        } else {
            toast.show(err?.data?.mensagem || 'Erro ao enviar contraproposta.', 'error');
        }
        setLoading(btn, false);
    }
}

// Iniciar
carregar();