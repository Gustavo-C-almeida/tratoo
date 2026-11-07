// ── utils/sessao.js — expiração da sessão do lado do cliente (ES module) ─────
// Carregado pelo auth-guard.js (via import()) depois que /api/me confirmou a sessão.
//
//   • Detecta atividade REAL (pointerdown, keydown, scroll, touchstart, com
//     throttle) e, se houve interação, chama POST /api/auth/atividade a cada 5 min.
//     Polling (chat) manda X-Tratoo-Passivo e não conta.
//   • 2 min antes de expirar, mostra o modal "sua sessão expira em 2 min" com
//     "Continuar conectado". Perto do teto absoluto, só avisa (não dá para estender).
//   • Sincroniza atividade e logout entre abas por BroadcastChannel.
//
// O SERVIDOR é a autoridade: os timers daqui são só UX. Antes de mandar alguém
// para o login, confere /api/me (passivo) — outra aba pode ter renovado a sessão.

import { el } from '/assets/js/utils/dom.js';
import { bootstrapReady } from '/assets/js/core/app.js';
import { api } from '/assets/js/services/api.js';
import { encerrarSessao, irParaLogin } from '/assets/js/services/refresh.js';

const CANAL = 'tratoo-sessao';
const AVISO_ANTES_MS = 2 * 60 * 1000;
const INTERVALO_ATIVIDADE_MS = 5 * 60 * 1000;
const THROTTLE_INTERACAO_MS = 10 * 1000;

let iniciada = false;
let inatividadeEm = 0;      // epoch ms
let absolutaEm = 0;
let janelaMs = 0;
let houveInteracao = false;
let ultimaInteracao = 0;
let timer = null;
let modal = null;           // { raiz, instancia, texto, tipo }
let canal = null;

/** Liga o controle de expiração. `me` é a resposta de /api/me. */
export function iniciarSessao(me) {
    if (iniciada || !me?.sessao) return;
    iniciada = true;

    aplicar(me.sessao);

    for (const evento of ['pointerdown', 'keydown', 'scroll', 'touchstart']) {
        window.addEventListener(evento, registrarInteracao, { passive: true, capture: true });
    }
    window.addEventListener('tratoo:requisicao-ativa', estimarRenovacao);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') conferirNoServidor();
    });
    setInterval(enviarAtividadeSeHouve, INTERVALO_ATIVIDADE_MS);

    try {
        canal = new BroadcastChannel(CANAL);
        canal.addEventListener('message', (e) => {
            const msg = e.data || {};
            if (msg.tipo === 'atividade' && Number(msg.inatividadeEm) > inatividadeEm) {
                inatividadeEm = Math.min(Number(msg.inatividadeEm), absolutaEm);
                fecharModalSeInatividade();
                agendar();
            }
            if (msg.tipo === 'logout') window.location.replace('/pages/auth/login.html');
            if (msg.tipo === 'sessao-expirada') irParaLogin('expirada');
        });
    } catch (_) {
        canal = null;
    }

    agendar();
}

function aplicar(sessao) {
    inatividadeEm = Date.parse(sessao.expiraPorInatividadeEm) || 0;
    absolutaEm = Date.parse(sessao.expiraAbsolutaEm) || 0;
    janelaMs = (Number(sessao.janelaInatividadeSegundos) || 0) * 1000;
}

function avisar(msg) {
    try { canal?.postMessage(msg); } catch (_) {}
}

function registrarInteracao() {
    const agora = Date.now();
    if (agora - ultimaInteracao < THROTTLE_INTERACAO_MS) return;
    ultimaInteracao = agora;
    houveInteracao = true;
}

/** Uma requisição não passiva acabou de ser aceita: o servidor renovou (ou vai renovar) a janela. */
function estimarRenovacao() {
    if (!janelaMs) return;
    const estimado = Math.min(Date.now() + janelaMs, absolutaEm);
    if (estimado <= inatividadeEm) return;
    inatividadeEm = estimado;
    avisar({ tipo: 'atividade', inatividadeEm });
    fecharModalSeInatividade();
    agendar();
}

async function enviarAtividadeSeHouve() {
    if (!houveInteracao) return;
    houveInteracao = false;
    try {
        await api.post('/api/auth/atividade', undefined, { silencioso: true });
        // estimarRenovacao roda pelo evento tratoo:requisicao-ativa.
    } catch (_) { /* 401/503 já tratados pelo api.js */ }
}

function fim() {
    return Math.min(inatividadeEm || Infinity, absolutaEm || Infinity);
}

function agendar() {
    clearTimeout(timer);
    const restante = fim() - Date.now();
    if (!Number.isFinite(restante)) return;

    if (restante <= 0) {
        timer = setTimeout(conferirNoServidor, 0);
    } else if (restante <= AVISO_ANTES_MS) {
        mostrarModal();
        timer = setTimeout(conferirNoServidor, restante + 1000);
    } else {
        timer = setTimeout(agendar, Math.min(restante - AVISO_ANTES_MS, 2 ** 31 - 1));
    }
}

/** Pergunta ao servidor (passivo: não renova nada). 401 → o api.js leva ao login. */
async function conferirNoServidor() {
    try {
        const me = await api.get('/api/me', { passivo: true, silencioso: true });
        if (me?.sessao) {
            aplicar(me.sessao);
            fecharModalSeInatividade();
            agendar();
        }
    } catch (_) {
        // 503/rede: tenta de novo daqui a pouco, sem deslogar.
        clearTimeout(timer);
        timer = setTimeout(conferirNoServidor, 15000);
    }
}

// ── Modal "sua sessão expira em 2 min" ────────────────────────────────────────

function tipoDoFim() {
    return absolutaEm && absolutaEm <= inatividadeEm ? 'absoluta' : 'inatividade';
}

function fecharModalSeInatividade() {
    if (modal && modal.tipo === 'inatividade' && fim() - Date.now() > AVISO_ANTES_MS) {
        modal.instancia.hide();
    }
}

async function mostrarModal() {
    if (modal) return;
    await bootstrapReady;
    const Modal = window.bootstrap?.Modal;
    if (!Modal || modal) return;

    const tipo = tipoDoFim();
    const idTitulo = 'sessao-expira-titulo';
    const texto = el('p', { class: 'mb-0' });
    const continuar = el('button', { type: 'button', class: 'btn btn-primary' },
        tipo === 'inatividade' ? 'Continuar conectado' : 'Entendi');
    const sair = el('button', { type: 'button', class: 'btn btn-light' }, 'Sair agora');

    const raiz = el('div', { class: 'modal fade', tabindex: '-1', role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': idTitulo },
        el('div', { class: 'modal-dialog modal-dialog-centered' },
            el('div', { class: 'modal-content' },
                el('div', { class: 'modal-header' },
                    el('h2', { class: 'modal-title h5', id: idTitulo },
                        tipo === 'inatividade' ? 'Sua sessão está quase expirando' : 'Sua sessão vai terminar')),
                el('div', { class: 'modal-body' }, texto),
                el('div', { class: 'modal-footer' }, sair, continuar))));
    document.body.append(raiz);

    const instancia = new Modal(raiz, { backdrop: 'static', keyboard: false });
    modal = { raiz, instancia, tipo };

    const atualizarTexto = () => {
        const seg = Math.max(0, Math.round((fim() - Date.now()) / 1000));
        const quando = seg >= 60 ? `${Math.ceil(seg / 60)} min` : `${seg} s`;
        texto.textContent = tipo === 'inatividade'
            ? `Por inatividade, sua sessão expira em ${quando}. Quer continuar conectado?`
            : `Por segurança, sua sessão termina em ${quando}. Salve o que estiver fazendo; depois será preciso entrar de novo.`;
    };
    atualizarTexto();
    const relogio = setInterval(atualizarTexto, 1000);

    continuar.addEventListener('click', async () => {
        if (tipo === 'inatividade') {
            continuar.disabled = true;
            try { await api.post('/api/auth/atividade', undefined, { silencioso: true }); } catch (_) {}
            continuar.disabled = false;
        }
        instancia.hide();
    });
    sair.addEventListener('click', async () => {
        sair.disabled = true;
        await encerrarSessao();
        window.location.replace('/pages/auth/login.html');
    });

    raiz.addEventListener('hidden.bs.modal', () => {
        clearInterval(relogio);
        instancia.dispose();
        raiz.remove();
        modal = null;
    }, { once: true });

    instancia.show();
}
