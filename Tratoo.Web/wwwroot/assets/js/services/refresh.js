// ── services/refresh.js — renovação da sessão (ES module) ────────────────────
// Um único refresh por vez, entre TODAS as abas:
//   • navigator.locks (Web Locks): as abas fazem fila; quem entra depois de outra
//     aba já ter renovado não renova de novo — o cookie novo já está no navegador;
//   • sem Web Locks: BroadcastChannel. Uma aba anuncia "refresh-iniciado" e as
//     outras esperam o "refresh-ok" (até 3 s) antes de tentar por conta própria.
// Mesmo assim duas renovações podem se cruzar (abas sem nenhum dos dois, rede
// lenta): o servidor responde 409 REFRESH_CONCORRENTE sem revogar nada, e aqui
// isso vira "espera 200 ms e repete a requisição".
//
// Resultado de renovarSessao(): 'ok' | 'expirada' | 'ausente' | 'indisponivel'.
// 'ausente' = o navegador nem tinha sessão (visitante); 'expirada' = tinha e acabou.
// 'indisponivel' (503, 429, rede) é transitório: NUNCA desloga.

const CANAL = 'tratoo-sessao';
const LOCK = 'tratoo-refresh';
const ESPERA_OUTRA_ABA_MS = 3000;
const MAX_TENTATIVAS_TRANSITORIAS = 4;

let emAndamento = null;
let ultimoRefreshOk = 0;
let outraAbaRenovandoDesde = 0;
const ouvintesFim = new Set();

const canal = (() => {
    try {
        const c = new BroadcastChannel(CANAL);
        c.addEventListener('message', (e) => {
            const msg = e.data || {};
            if (msg.tipo === 'refresh-iniciado') outraAbaRenovandoDesde = Date.now();
            if (msg.tipo === 'refresh-ok') {
                ultimoRefreshOk = Math.max(ultimoRefreshOk, Number(msg.em) || Date.now());
                outraAbaRenovandoDesde = 0;
                ouvintesFim.forEach(f => f());
            }
            if (msg.tipo === 'refresh-falhou') {
                outraAbaRenovandoDesde = 0;
                ouvintesFim.forEach(f => f());
            }
        });
        return c;
    } catch (_) {
        return null;   // navegador sem BroadcastChannel: cada aba por si (o 409 cobre)
    }
})();

function avisar(msg) {
    try { canal?.postMessage(msg); } catch (_) {}
}

const esperar = (ms) => new Promise(r => setTimeout(r, ms));

function atrasoDeNovaTentativa(tentativa, resposta) {
    const retryAfter = Number(resposta?.headers?.get('Retry-After'));
    const base = Number.isFinite(retryAfter) && retryAfter > 0
        ? retryAfter * 1000
        : Math.min(8000, 500 * 2 ** tentativa);
    return base + Math.floor(Math.random() * 300);
}

/**
 * Renova a sessão. `desde` = quando a requisição que levou 401 terminou: se outra
 * aba renovou depois disso, o cookie já é novo e basta repetir a requisição.
 */
export function renovarSessao(desde = Date.now()) {
    if (!emAndamento) {
        emAndamento = executar(desde).finally(() => { emAndamento = null; });
    }
    return emAndamento;
}

async function executar(desde) {
    if (navigator.locks?.request) {
        return navigator.locks.request(LOCK, async () => {
            if (ultimoRefreshOk > desde) return 'ok';
            return chamarRefresh();
        });
    }

    // Fallback: espera a outra aba terminar, se ela acabou de anunciar um refresh.
    if (Date.now() - outraAbaRenovandoDesde < ESPERA_OUTRA_ABA_MS) {
        await new Promise(resolve => {
            const fim = () => { ouvintesFim.delete(fim); resolve(); };
            ouvintesFim.add(fim);
            setTimeout(fim, ESPERA_OUTRA_ABA_MS);
        });
        if (ultimoRefreshOk > desde) return 'ok';
    }
    return chamarRefresh();
}

async function chamarRefresh() {
    avisar({ tipo: 'refresh-iniciado' });

    for (let tentativa = 1; ; tentativa++) {
        let resposta = null;
        try {
            resposta = await fetch('/api/auth/refresh', {
                method: 'POST',
                credentials: 'same-origin',
                // O servidor já não conta o refresh como atividade; o header deixa explícito.
                headers: { 'X-Tratoo-Passivo': '1' }
            });
        } catch (_) {
            resposta = null;   // rede: transitório
        }

        if (resposta?.ok) {
            ultimoRefreshOk = Date.now();
            avisar({ tipo: 'refresh-ok', em: ultimoRefreshOk });
            return 'ok';
        }

        if (resposta?.status === 409) {
            // Outra requisição venceu a rotação há instantes; o cookie novo chega com a
            // resposta dela. Espera e deixa o chamador repetir.
            await esperar(200);
            avisar({ tipo: 'refresh-ok', em: Date.now() });
            return 'ok';
        }

        if (resposta && (resposta.status === 401 || resposta.status === 400)) {
            let codigo = null;
            try { codigo = (await resposta.json())?.codigoErro ?? null; } catch (_) {}
            if (codigo === 'SESSAO_AUSENTE') return 'ausente';
            avisar({ tipo: 'refresh-falhou' });
            return 'expirada';
        }

        if (tentativa >= MAX_TENTATIVAS_TRANSITORIAS) {
            avisar({ tipo: 'refresh-falhou' });
            return 'indisponivel';
        }
        await esperar(atrasoDeNovaTentativa(tentativa, resposta));
    }
}

/** Caminho de retorno seguro: só caminhos do próprio site, fora das telas de login. */
export function retornoSeguro(valor) {
    if (typeof valor !== 'string' || !valor.startsWith('/') || valor.startsWith('//') || valor.includes('\\')) return null;
    if (valor.startsWith('/pages/auth/')) return null;
    return valor;
}

/** Vai para o login guardando a página atual. motivo: 'expirada' mostra o aviso lá. */
export function irParaLogin(motivo = null) {
    const params = new URLSearchParams();
    if (motivo) params.set('motivo', motivo);
    const retorno = retornoSeguro(window.location.pathname + window.location.search);
    if (retorno) params.set('retorno', retorno);
    const qs = params.toString();
    if (motivo === 'expirada') avisar({ tipo: 'sessao-expirada' });
    window.location.replace('/pages/auth/login.html' + (qs ? `?${qs}` : ''));
}

/** Logout desta sessão, avisando as outras abas. Nunca lança. */
export async function encerrarSessao() {
    try {
        await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' });
    } catch (_) {}
    avisar({ tipo: 'logout' });
}
