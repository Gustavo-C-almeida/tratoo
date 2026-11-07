// ── services/api.js — wrapper central de fetch (ES module) ───────────────────
// Importe com: `import { api } from '/assets/js/services/api.js';`
// Centraliza método/headers/credenciais, tratamento de erro `{ status, data }`
// e o overlay de loading global (com debounce de 200ms).
//
// Sessão (o servidor é a autoridade; aqui só se reage às respostas dele):
//   • 401 → refresh em single-flight entre abas (services/refresh.js) e repete a
//     requisição UMA vez. Refresh com 401 → login com ?motivo=expirada&retorno=…
//   • 403 REAUTH_REQUIRED → modal de senha/MFA (utils/reautenticacao.js) e repete.
//   • 503 transitório (GET, ou SESSAO_INDISPONIVEL em qualquer método — a ação não
//     rodou) → aviso discreto e novas tentativas com backoff. Nunca desloga.
//
// Opções (último argumento): { passivo, silencioso, anonimoPermitido }
//   passivo          → header X-Tratoo-Passivo: 1 (polling: não conta como atividade)
//   silencioso       → sem overlay de loading
//   anonimoPermitido → sessão ausente não redireciona ao login (sondagem em página pública)

import { renovarSessao, irParaLogin } from './refresh.js';
import { mostrarAviso, esconderAviso } from '../utils/aviso.js';

const API_BASE = '';

// Rotas em que um 401 é resposta de negócio, não "access expirado".
const SEM_RENOVACAO = [/^\/api\/auth\//i, /^\/usuarios\/(login|cadastro|senha)/i];
const MAX_TENTATIVAS_503 = 3;

// ── Loading overlay global ────────────────────────────────────────────────────
// Contador de requisições concorrentes: só esconde quando todas terminam.
let _reqCount   = 0;
let _reqTimer   = null;

function _getOverlay() {
    let el = document.getElementById('loading-overlay');
    if (!el) {
        el = document.createElement('div');
        el.id = 'loading-overlay';
        el.setAttribute('role', 'status');
        el.setAttribute('aria-live', 'polite');
        el.innerHTML = `
            <div class="loading-card">
                <div class="loading-spinner" aria-hidden="true"></div>
                <span>Carregando...</span>
            </div>`;
        document.body.appendChild(el);
    }
    return el;
}

function _showLoading() {
    _reqCount++;
    // Aguarda 200 ms antes de exibir — evita flash em respostas rápidas
    if (_reqCount === 1 && !_reqTimer) {
        _reqTimer = setTimeout(() => {
            _reqTimer = null;
            if (_reqCount > 0) _getOverlay().classList.add('visible');
        }, 200);
    }
}

function _hideLoading() {
    _reqCount = Math.max(0, _reqCount - 1);
    if (_reqCount === 0) {
        clearTimeout(_reqTimer);
        _reqTimer = null;
        const el = document.getElementById('loading-overlay');
        if (el) el.classList.remove('visible');
    }
}
// ─────────────────────────────────────────────────────────────────────────────

const esperar = (ms) => new Promise(r => setTimeout(r, ms));

function atraso503(tentativa, response) {
    const retryAfter = Number(response.headers.get('Retry-After'));
    const base = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : 1000 * 2 ** (tentativa - 1);
    return Math.min(10000, base) + Math.floor(Math.random() * 400);
}

async function enviar(metodo, endpoint, { corpo, formData, passivo }) {
    const headers = {};
    let body;
    if (formData) {
        body = formData;   // não define Content-Type; o browser injeta o boundary
    } else if (corpo !== undefined) {
        headers['Content-Type'] = 'application/json';
        body = JSON.stringify(corpo);
    }
    if (passivo) headers['X-Tratoo-Passivo'] = '1';

    const response = await fetch(`${API_BASE}${endpoint}`, {
        method: metodo,
        headers,
        credentials: 'same-origin',
        body
    });
    let data = {};
    try { data = await response.json(); } catch (_) {}
    return { response, data };
}

async function requisitar(metodo, endpoint, opcoes = {}) {
    const silencioso = opcoes.silencioso === true;
    if (!silencioso) _showLoading();
    try {
        let { response, data } = await enviar(metodo, endpoint, opcoes);
        const podeRenovar = !SEM_RENOVACAO.some(r => r.test(endpoint));

        // ── 401: access expirado (ou sessão encerrada) ───────────────────────
        if (response.status === 401 && podeRenovar) {
            const resultado = await renovarSessao(Date.now());
            if (resultado === 'ok') {
                ({ response, data } = await enviar(metodo, endpoint, opcoes));
            } else if (resultado === 'indisponivel') {
                throw { status: 503, data: { mensagem: 'Não foi possível confirmar sua sessão agora. Tente novamente em instantes.' } };
            }
            if (response.status === 401 && !opcoes.anonimoPermitido) {
                irParaLogin(resultado === 'ausente' ? null : 'expirada');
            }
        }

        // ── 403 REAUTH_REQUIRED: confirma a identidade e repete a ação ─────────
        if (response.status === 403 && data?.codigoErro === 'REAUTH_REQUIRED') {
            if (!silencioso) _hideLoading();   // o overlay não pode cobrir o modal
            let confirmou = false;
            try {
                const { pedirReautenticacao } = await import('/assets/js/utils/reautenticacao.js');
                confirmou = await pedirReautenticacao();
            } finally {
                if (!silencioso) _showLoading();
            }
            if (confirmou) ({ response, data } = await enviar(metodo, endpoint, opcoes));
        }

        // ── 503 transitório: avisa e tenta de novo, sem deslogar ──────────────
        const repetivel = () => response.status === 503 && (metodo === 'GET' || data?.codigoErro === 'SESSAO_INDISPONIVEL');
        for (let t = 1; t <= MAX_TENTATIVAS_503 && repetivel(); t++) {
            mostrarAviso('Instabilidade momentânea no servidor. Tentando novamente…');
            await esperar(atraso503(t, response));
            ({ response, data } = await enviar(metodo, endpoint, opcoes));
        }
        esconderAviso();

        if (!response.ok) throw { status: response.status, data };

        // Atividade real reconhecida pelo servidor: utils/sessao.js atualiza a estimativa local.
        if (!opcoes.passivo) window.dispatchEvent(new CustomEvent('tratoo:requisicao-ativa'));
        return data;
    } finally {
        if (!silencioso) _hideLoading();
    }
}

export const api = {
    get(endpoint, opcoes)          { return requisitar('GET', endpoint, opcoes); },
    post(endpoint, body, opcoes)   { return requisitar('POST', endpoint, { ...opcoes, corpo: body ?? {} }); },
    put(endpoint, body, opcoes)    { return requisitar('PUT', endpoint, { ...opcoes, corpo: body ?? {} }); },
    patch(endpoint, body, opcoes)  { return requisitar('PATCH', endpoint, { ...opcoes, corpo: body ?? {} }); },
    delete(endpoint, opcoes)       { return requisitar('DELETE', endpoint, opcoes); },

    /** Upload multipart/form-data via POST (foto, portfólio PDF). */
    uploadPost(endpoint, formData, opcoes) { return requisitar('POST', endpoint, { ...opcoes, formData }); },

    /** Upload multipart/form-data via PUT. */
    uploadPut(endpoint, formData, opcoes)  { return requisitar('PUT', endpoint, { ...opcoes, formData }); }
};
