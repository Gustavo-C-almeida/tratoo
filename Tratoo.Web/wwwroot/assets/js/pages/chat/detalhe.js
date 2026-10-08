import { api } from '/assets/js/services/api.js';
import { el, svg } from '/assets/js/utils/dom.js';
// ── Conversa de projeto — Design Moderno Aprimorado ─────────────────────────

const root = () => document.getElementById('chat-detalhe-root');

// ── Parâmetros da URL ─────────────────────────────────────────────────────────
// Controlados por quem monta o link: só aceitamos IDs inteiros (como na API).
// Qualquer outro valor vira null e cai em "Parâmetros de conversa inválidos.".
const params = new URLSearchParams(window.location.search);
const projetoId = idInteiro(params.get('projetoId'));
const prestadorId = idInteiro(params.get('prestadorId'));

// ── Estado ────────────────────────────────────────────────────────────────────
let usuarioId = null;
let projeto = null;
let proposta = null;
let conviteId = null;      // convite aceito deste prestador (habilita proposta proativa)
let mensagens = [];
let pollingTimer = null;
let noScrollLock = false;
let typingTimeout = null;
let isTyping = false;

// ── Utilitários ───────────────────────────────────────────────────────────────
function idInteiro(valor) {
    return /^\d+$/.test(valor ?? '') ? valor : null;
}

function icone(classes) {
    return el('i', { class: classes });
}

function dataFmt(d) {
    if (!d) return '—';
    const dt = new Date(d);
    const hoje = new Date();
    const ontem = new Date(Date.now() - 86400000);
    const diaMsg = dt.toLocaleDateString('pt-BR');
    const diaHoje = hoje.toLocaleDateString('pt-BR');
    const diaOntem = ontem.toLocaleDateString('pt-BR');
    if (diaMsg === diaHoje) return 'Hoje';
    if (diaMsg === diaOntem) return 'Ontem';
    return diaMsg;
}

function horaFmt(d) {
    return new Date(d).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
}

function diaMensagem(d) {
    return new Date(d).toLocaleDateString('pt-BR');
}

// Efeito Ripple para botões
function addRippleEffect(element) {
    element.addEventListener('click', (e) => {
        const ripple = document.createElement('span');
        ripple.classList.add('ripple-effect');
        const rect = element.getBoundingClientRect();
        const size = Math.max(rect.width, rect.height);
        ripple.style.width = ripple.style.height = `${size}px`;
        ripple.style.left = `${e.clientX - rect.left - size / 2}px`;
        ripple.style.top = `${e.clientY - rect.top - size / 2}px`;
        element.appendChild(ripple);
        setTimeout(() => ripple.remove(), 600);
    });
}

// ── Ponto de entrada ──────────────────────────────────────────────────────────
async function iniciar() {
    if (!projetoId || !prestadorId) {
        root().innerHTML = '<p class="chat-estado">Parâmetros de conversa inválidos.</p>';
        return;
    }

    root().innerHTML = `
        <div class="chat-detalhe-wrap">
            <div class="chat-estado">
                <p>Carregando conversa...</p>
                <div class="skeleton-message">
                    <div class="skeleton-bubble"></div>
                </div>
            </div>
        </div>
    `;

    const [meRes, projetoRes, propostasRes] = await Promise.allSettled([
        api.get('/api/me'),
        api.get(`/api/projects/${projetoId}`),
        api.get(`/api/projects/${projetoId}/proposals`).catch(() => []),
    ]);

    if (meRes.status === 'rejected') {
        root().innerHTML = '<p class="chat-estado">Sessão expirada. Faça login novamente.</p>';
        return;
    }

    usuarioId = meRes.value.id;

    if (projetoRes.status === 'rejected') {
        root().innerHTML = '<p class="chat-estado">Projeto não encontrado.</p>';
        return;
    }

    projeto = projetoRes.value;
    document.title = `Conversa — ${projeto.titulo} — Tratoo`;

    if (propostasRes.status === 'fulfilled' && Array.isArray(propostasRes.value)) {
        proposta = propostasRes.value.find(
            p => String(p.prestadorId) === String(prestadorId)
        ) ?? null;
    }

    // Fluxo reverso: se eu sou o contratante, busco o convite ACEITO deste prestador
    // para habilitar o envio de proposta proativa (POST /api/convites/{id}/proposta).
    if (usuarioId === projeto.contratanteId) {
        try {
            const convites = await api.get(`/api/projects/${projetoId}/convites`);
            const aceito = (convites || []).find(
                c => String(c.prestadorId) === String(prestadorId) && c.status === 'Aceito'
            );
            conviteId = aceito ? aceito.id : null;
        } catch {
            conviteId = null;
        }
    }

    renderLayout();
    await carregarMensagens(true);
    iniciarPolling();
    setupTypingIndicator();
}

// ── Layout fixo com design moderno ─────────────────────────────────────────
function renderLayout() {
    // IDs entram na URL codificados (projetoId já foi validado como inteiro;
    // proposta.id vem da API) — nunca como markup.
    const linkProjeto = `/pages/projetos/detalhe.html?id=${encodeURIComponent(projetoId)}`;
    const linkProposta = proposta ? `/pages/proposta/detalhe.html?id=${encodeURIComponent(proposta.id)}` : null;
    const ehContratante = usuarioId === projeto.contratanteId;
    // Contratante vê o nome do prestador (vindo da proposta deste chat);
    // prestador vê o nome do contratante do projeto.
    const outroNome = ehContratante
        ? (proposta?.prestadorNome || 'Prestador')
        : (projeto.contratanteNome || 'Contratante');

    // Pode enviar proposta proativa: contratante, convite aceito e sem proposta ativa.
    const propostaAtiva = proposta && !['Recusada', 'Expirada', 'Cancelada'].includes(proposta.status);
    const podeEnviarProposta = ehContratante && conviteId && !propostaAtiva;

    // Montado via DOM: título, nomes e links vêm da URL/API e entram como
    // texto/atributo, nunca interpretados como HTML.
    root().replaceChildren(el('div', { class: 'chat-detalhe-wrap' },
        el('header', { class: 'chat-detalhe-header' },
            el('a', { class: 'chat-detalhe-back', href: '/pages/chat/index.html', 'aria-label': 'Voltar para conversas' },
                'Voltar para conversas'),
            el('div', { class: 'chat-detalhe-info' },
                el('h1', { class: 'chat-detalhe-titulo' }, projeto.titulo),
                el('p', { class: 'chat-detalhe-participantes' }, `Conversa com ${outroNome || 'participante'}`)),
            el('div', { class: 'chat-detalhe-acoes' },
                el('a', { class: 'chat-acao-link', href: linkProjeto },
                    icone('fa-solid fa-clipboard-list'), ' Ver projeto'),
                linkProposta && el('a', { class: 'chat-acao-link', href: linkProposta },
                    icone('fa-solid fa-file-lines'), ' Ver proposta'),
                podeEnviarProposta && el('button', { type: 'button', class: 'chat-acao-link chat-acao-btn', id: 'btn-enviar-proposta' },
                    icone('fa-solid fa-envelope'), ' Enviar proposta'))),

        el('div', { class: 'chat-mensagens', id: 'chat-mensagens', role: 'log', 'aria-live': 'polite', 'aria-label': 'Mensagens da conversa' },
            el('div', { class: 'chat-estado' },
                el('p', {}, 'Carregando mensagens...'))),

        el('div', { class: 'chat-form-wrap' },
            el('div', { id: 'typing-indicator-container' }),
            el('form', { class: 'chat-form', id: 'chat-form' },
                el('div', { class: 'chat-input-wrapper' },
                    el('textarea', {
                        id: 'chat-input',
                        class: 'chat-input',
                        rows: 1,
                        placeholder: 'Escreva uma mensagem... (Enter para enviar)',
                        maxlength: 2000,
                        'aria-label': 'Mensagem'
                    }),
                    el('div', { class: 'char-counter', id: 'char-counter' }, '0/2000')),
                el('button', { type: 'submit', class: 'chat-btn-enviar ripple', 'aria-label': 'Enviar mensagem' },
                    svg('svg', {
                        viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', 'stroke-width': 2,
                        'stroke-linecap': 'round', 'stroke-linejoin': 'round'
                    },
                        svg('line', { x1: 22, y1: 2, x2: 11, y2: 13 }),
                        svg('polygon', { points: '22 2 15 22 11 13 2 9 22 2' })))))));

    // Setup listeners
    const form = document.getElementById('chat-form');
    const input = document.getElementById('chat-input');
    const charCounter = document.getElementById('char-counter');
    const btnEnviar = document.querySelector('.chat-btn-enviar');

    form.addEventListener('submit', enviarMensagem);

    // Character counter com feedback visual
    input.addEventListener('input', () => {
        const length = input.value.length;
        charCounter.textContent = `${length}/2000`;

        if (length > 1800) {
            charCounter.classList.add('warning');
            if (length > 1950) charCounter.classList.add('danger');
        } else {
            charCounter.classList.remove('warning', 'danger');
        }

        // Auto-resize
        input.style.height = 'auto';
        input.style.height = Math.min(input.scrollHeight, 140) + 'px';
    });

    input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            form.dispatchEvent(new Event('submit'));
        }
    });

    // Adicionar efeito ripple
    if (btnEnviar) addRippleEffect(btnEnviar);

    // Scroll listener
    const lista = document.getElementById('chat-mensagens');
    lista.addEventListener('scroll', () => {
        const distanciaFundo = lista.scrollHeight - lista.scrollTop - lista.clientHeight;
        noScrollLock = distanciaFundo > 80;
        if (!noScrollLock) esconderBadge();
    });

    // Botão de proposta proativa (contratante)
    document.getElementById('btn-enviar-proposta')?.addEventListener('click', abrirModalProposta);

    // Focus no input
    input.focus();
}

// ── Proposta proativa (Contratante -> Prestador, pós-convite) ────────────────
function abrirModalProposta() {
    if (!conviteId) return;
    garantirModalProposta();

    // Datas padrão: prazo +14 dias, validade +7 dias
    const hoje = new Date();
    const prazoDefault = new Date(hoje.getTime() + 14 * 86400000).toISOString().slice(0, 10);
    const validadeDefault = new Date(hoje.getTime() + 7 * 86400000).toISOString().slice(0, 10);

    document.getElementById('pp-valor').value = '';
    document.getElementById('pp-entrada').value = '';
    document.getElementById('pp-revisoes').value = '2';
    document.getElementById('pp-pagamento').value = 'PIX';
    document.getElementById('pp-prazo').value = prazoDefault;
    document.getElementById('pp-validade').value = validadeDefault;
    document.getElementById('pp-objetivo').value = '';
    document.getElementById('pp-escopo').value = '';
    document.getElementById('pp-exclusoes').value = '';
    document.getElementById('pp-obs').value = '';
    document.getElementById('pp-erro').textContent = '';

    document.getElementById('modal-proposta').style.display = 'flex';
}

function garantirModalProposta() {
    if (document.getElementById('modal-proposta')) return;

    const modal = document.createElement('div');
    modal.id = 'modal-proposta';
    modal.className = 'modal-overlay';
    modal.style.display = 'none';
    modal.innerHTML = `
        <div class="modal-box modal-box-proposta">
            <h3>Enviar proposta ao prestador</h3>
            <p class="modal-sub">O prestador poderá aceitar, recusar ou enviar uma contraproposta.</p>

            <div class="pp-grid">
                <div class="modal-campo">
                    <label for="pp-valor">Valor total (R$) <span class="obrigatorio">*</span></label>
                    <input id="pp-valor" type="number" min="1" step="0.01" placeholder="0,00">
                </div>
                <div class="modal-campo">
                    <label for="pp-entrada">Entrada (R$) <span class="opcional">(opcional)</span></label>
                    <input id="pp-entrada" type="number" min="0" step="0.01" placeholder="0,00">
                </div>
                <div class="modal-campo">
                    <label for="pp-prazo">Prazo de entrega <span class="obrigatorio">*</span></label>
                    <input id="pp-prazo" type="date">
                </div>
                <div class="modal-campo">
                    <label for="pp-validade">Validade da proposta <span class="obrigatorio">*</span></label>
                    <input id="pp-validade" type="date">
                </div>
                <div class="modal-campo">
                    <label for="pp-revisoes">Revisões inclusas <span class="obrigatorio">*</span></label>
                    <input id="pp-revisoes" type="number" min="1" value="2">
                </div>
                <div class="modal-campo">
                    <label for="pp-pagamento">Forma de pagamento</label>
                    <select id="pp-pagamento">
                        <option value="PIX">PIX</option>
                        <option value="Boleto">Boleto</option>
                        <option value="Cartão">Cartão</option>
                    </select>
                </div>
            </div>

            <div class="modal-campo">
                <label for="pp-objetivo">Objetivo da proposta <span class="obrigatorio">*</span></label>
                <textarea id="pp-objetivo" rows="2" placeholder="Resumo do que será entregue (mín. 20 caracteres)..."></textarea>
            </div>
            <div class="modal-campo">
                <label for="pp-escopo">Escopo detalhado <span class="obrigatorio">*</span></label>
                <textarea id="pp-escopo" rows="3" placeholder="Detalhe o escopo do trabalho (mín. 50 caracteres)..."></textarea>
            </div>
            <div class="modal-campo">
                <label for="pp-exclusoes">Exclusões <span class="opcional">(opcional)</span></label>
                <textarea id="pp-exclusoes" rows="2" placeholder="O que não está incluso..."></textarea>
            </div>
            <div class="modal-campo">
                <label for="pp-obs">Observações <span class="opcional">(opcional)</span></label>
                <textarea id="pp-obs" rows="2"></textarea>
            </div>

            <p id="pp-erro" class="pp-erro" role="alert"></p>

            <div class="modal-actions">
                <button type="button" id="pp-cancelar" class="btn-secundario">Cancelar</button>
                <button type="button" id="pp-enviar" class="btn-enviar-proposta">Enviar proposta</button>
            </div>
        </div>`;
    document.body.appendChild(modal);

    modal.addEventListener('click', (e) => { if (e.target === modal) modal.style.display = 'none'; });
    document.getElementById('pp-cancelar').addEventListener('click', () => { modal.style.display = 'none'; });
    document.getElementById('pp-enviar').addEventListener('click', enviarPropostaProativa);
}

async function enviarPropostaProativa() {
    const erroEl = document.getElementById('pp-erro');
    erroEl.textContent = '';

    const valor = parseFloat(document.getElementById('pp-valor').value);
    const entrada = parseFloat(document.getElementById('pp-entrada').value);
    const revisoes = parseInt(document.getElementById('pp-revisoes').value, 10);
    const prazo = document.getElementById('pp-prazo').value;
    const validade = document.getElementById('pp-validade').value;
    const objetivo = document.getElementById('pp-objetivo').value.trim();
    const escopo = document.getElementById('pp-escopo').value.trim();

    // Validação client-side espelhando as regras do backend
    if (!valor || valor <= 0) return (erroEl.textContent = 'Informe o valor total da proposta.');
    if (!revisoes || revisoes <= 0) return (erroEl.textContent = 'Informe quantas revisões estão inclusas.');
    if (!prazo) return (erroEl.textContent = 'Informe o prazo de entrega.');
    if (!validade) return (erroEl.textContent = 'Informe a validade da proposta.');
    if (objetivo.length < 20) return (erroEl.textContent = 'Descreva o objetivo com pelo menos 20 caracteres.');
    if (escopo.length < 50) return (erroEl.textContent = 'Detalhe o escopo com pelo menos 50 caracteres.');
    if (!isNaN(entrada) && entrada > valor) return (erroEl.textContent = 'A entrada não pode ser maior que o valor total.');

    const payload = {
        valorTotal: valor,
        entrada: isNaN(entrada) ? null : entrada,
        revisoesInclusas: revisoes,
        formaPagamento: document.getElementById('pp-pagamento').value,
        prazoTotal: new Date(prazo).toISOString(),
        validoAte: new Date(validade).toISOString(),
        objetivo,
        escopo,
        exclusoes: document.getElementById('pp-exclusoes').value.trim() || null,
        observacoes: document.getElementById('pp-obs').value.trim() || null,
    };

    const btn = document.getElementById('pp-enviar');
    btn.disabled = true;
    btn.textContent = 'Enviando...';
    try {
        await api.post(`/api/convites/${conviteId}/proposta`, payload);
        document.getElementById('modal-proposta').style.display = 'none';
        // Recarrega para refletir a nova proposta (link "Ver proposta")
        await iniciar();
    } catch (err) {
        erroEl.textContent = err?.data?.mensagem || 'Erro ao enviar proposta.';
        btn.disabled = false;
        btn.textContent = 'Enviar proposta';
    }
}

// ── Indicador de digitação ─────────────────────────────────────────────────
function setupTypingIndicator() {
    // Aqui você pode implementar WebSocket ou polling para indicador de digitação
    // Por enquanto é apenas visual local
}

// ── Carregamento de mensagens com animação ─────────────────────────────────
async function carregarMensagens(primeiraCarga = false) {
    const lista = document.getElementById('chat-mensagens');
    if (!lista) return;

    let resp;
    try {
        const url = `/api/projects/${projetoId}/mensagens?page=1&prestadorId=${prestadorId}`;
        resp = await api.get(url);
    } catch {
        if (primeiraCarga) {
            lista.innerHTML = '<p class="chat-estado">Não foi possível carregar as mensagens.</p>';
        }
        return;
    }

    const novas = resp.itens || [];
    const totalAntes = mensagens.length;
    mensagens = novas;

    renderMensagens(lista);

    if (primeiraCarga) {
        scrollParaFundo(lista, false);
    } else if (novas.length > totalAntes) {
        if (!noScrollLock) {
            scrollParaFundo(lista, true);
        } else {
            mostrarBadge(novas.length - totalAntes);
        }
    }
}

// ── Renderização com animações e status de leitura ─────────────────────────
function renderMensagens(lista) {
    if (!mensagens.length) {
        lista.replaceChildren(el('div', { class: 'chat-estado' },
            el('p', {}, icone('fa-solid fa-comments'), ' Nenhuma mensagem ainda'),
            el('small', {}, 'Seja o primeiro a escrever!')));
        return;
    }

    const itens = document.createDocumentFragment();
    let ultimoDia = null;

    for (const m of mensagens) {
        const dia = diaMensagem(m.enviadoEm);
        if (dia !== ultimoDia) {
            itens.append(criarSeparadorData(m.enviadoEm));
            ultimoDia = dia;
        }
        itens.append(criarBolha(m));
    }

    lista.replaceChildren(itens);

    // Adicionar efeito de fade-in nas mensagens
    const mensagensElements = lista.querySelectorAll('.msg-wrap');
    mensagensElements.forEach((msg, index) => {
        msg.style.animationDelay = `${index * 0.02}s`;
    });
}

function criarSeparadorData(d) {
    return el('div', { class: 'chat-sep-data' }, el('span', {}, dataFmt(d)));
}

// Nome e texto vêm de outros usuários: entram como texto puro, nunca como HTML.
function criarBolha(m) {
    const minha = m.remetenteId === usuarioId;
    const lido = m.lidoEm ? 'true' : 'false';

    return el('div', { class: `msg-wrap ${minha ? 'msg-minha' : 'msg-outra'}` },
        el('div', { class: 'msg-balao' },
            el('div', { class: 'msg-autor' }, m.remetenteNome),
            el('div', { class: 'msg-texto' }, m.texto),
            el('div', { class: 'msg-hora', 'data-read': lido }, horaFmt(m.enviadoEm))));
}

// ── Scroll suave ───────────────────────────────────────────────────────────
function scrollParaFundo(lista, suave = true) {
    lista.scrollTo({
        top: lista.scrollHeight,
        behavior: suave ? 'smooth' : 'instant'
    });
}

// ── Badge de novas mensagens ───────────────────────────────────────────────
function mostrarBadge(qtd) {
    let badge = document.getElementById('chat-novas-badge');
    if (!badge) {
        badge = document.createElement('div');
        badge.id = 'chat-novas-badge';
        badge.className = 'chat-novas-badge';
        badge.innerHTML = `<button id="chat-badge-btn"></button>`;
        document.getElementById('chat-mensagens')?.after(badge);
        badge.querySelector('button').addEventListener('click', () => {
            const lista = document.getElementById('chat-mensagens');
            scrollParaFundo(lista, true);
            esconderBadge();
            noScrollLock = false;
        });
    }
    badge.querySelector('button').textContent =
        `${qtd} nova${qtd > 1 ? 's' : ''} mensagem${qtd > 1 ? 'ns' : ''} <i class="fa-solid fa-arrow-down"></i>`;
    badge.style.display = 'flex';
}

function esconderBadge() {
    const badge = document.getElementById('chat-novas-badge');
    if (badge) badge.style.display = 'none';
}

// ── Envio de mensagem com feedback ─────────────────────────────────────────
async function enviarMensagem(e) {
    e.preventDefault();
    const input = document.getElementById('chat-input');
    const btn = document.querySelector('.chat-btn-enviar');
    const texto = input.value.trim();
    if (!texto) return;

    // Desabilitar botão e mostrar loading
    btn.disabled = true;
    btn.style.opacity = '0.7';

    // Mostrar mensagem otimista
    const mensagemTemp = {
        id: Date.now(),
        texto: texto,
        remetenteId: usuarioId,
        remetenteNome: 'Você',
        enviadoEm: new Date().toISOString(),
        lidoEm: null
    };

    const lista = document.getElementById('chat-mensagens');
    const ultimoDia = mensagens.length > 0 ? diaMensagem(mensagens[mensagens.length - 1].enviadoEm) : null;
    const diaAtual = diaMensagem(mensagemTemp.enviadoEm);

    if (ultimoDia !== diaAtual) {
        lista.appendChild(criarSeparadorData(mensagemTemp.enviadoEm));
    }

    const tempMsgDiv = document.createElement('div');
    tempMsgDiv.className = 'msg-wrap msg-minha';
    tempMsgDiv.appendChild(criarBolha(mensagemTemp));
    tempMsgDiv.style.opacity = '0.6';
    lista.appendChild(tempMsgDiv);
    scrollParaFundo(lista, true);

    const ehContratante = projeto && (usuarioId === projeto.contratanteId);
    const payload = ehContratante
        ? { texto, prestadorId: Number(prestadorId) }
        : { texto };

    try {
        await api.post(`/api/projects/${projetoId}/mensagens`, payload);
        input.value = '';
        input.style.height = 'auto';
        document.getElementById('char-counter').textContent = '0/2000';
        noScrollLock = false;
        esconderBadge();
        await carregarMensagens();
    } catch (err) {
        // Remover mensagem otimista em caso de erro
        tempMsgDiv.remove();
        alert(err?.data?.mensagem || 'Erro ao enviar mensagem.');
    } finally {
        btn.disabled = false;
        btn.style.opacity = '1';
        input.focus();
    }
}

// ── Polling otimizado ──────────────────────────────────────────────────────
function iniciarPolling() {
    const tick = () => {
        if (document.visibilityState === 'visible') carregarMensagens();
    };

    pollingTimer = setInterval(tick, 3000);

    document.addEventListener('visibilitychange', () => {
        clearInterval(pollingTimer);
        pollingTimer = setInterval(tick, document.visibilityState === 'visible' ? 3000 : 30000);
    });
}

// Inicializar
iniciar();