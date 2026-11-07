// ── utils/reautenticacao.js — modal "confirme sua identidade" (ES module) ────
// Aberto por services/api.js quando uma ação sensível responde 403 REAUTH_REQUIRED
// (liberar pagamento, aprovar entrega, decidir disputa, trocar senha, MFA, excluir
// conta). Senha primeiro; com MFA ativo (ou Admin), o código enviado por e-mail.
// Resolve true quando o servidor confirmou — aí o api.js repete a ação.
//
// DOM montado com el() (nada de innerHTML com dado); modal do Bootstrap vendorizado.

import { el } from '/assets/js/utils/dom.js';
import { bootstrapReady } from '/assets/js/core/app.js';
import { irParaLogin } from '/assets/js/services/refresh.js';

let pendente = null;

/** Abre o modal (um por vez). Resolve true se reautenticou, false se cancelou. */
export function pedirReautenticacao() {
    if (!pendente) pendente = abrir().finally(() => { pendente = null; });
    return pendente;
}

async function postar(corpo) {
    const r = await fetch('/api/auth/reautenticar', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(corpo)
    });
    let data = {};
    try { data = await r.json(); } catch (_) {}
    return { status: r.status, ok: r.ok, data };
}

function mensagemDeErro({ status, data }) {
    if (status === 429) return 'Muitas tentativas. Aguarde um minuto e tente de novo.';
    if (status === 503) return 'Não foi possível confirmar agora. Tente novamente em instantes.';
    return data?.mensagem || 'Não foi possível confirmar. Tente novamente.';
}

async function abrir() {
    await bootstrapReady;
    const Modal = window.bootstrap?.Modal;
    if (!Modal) return false;

    const idTitulo = 'reauth-titulo';
    const erro = el('p', { class: 'alert alert-danger py-2 px-3 small mt-3 mb-0', role: 'alert', hidden: true });
    const descricao = el('p', { class: 'mb-3' }, 'Por segurança, confirme sua senha para continuar.');
    const senha = el('input', { type: 'password', class: 'form-control', id: 'reauth-senha', autocomplete: 'current-password', required: true });
    const campoSenha = el('div', {}, el('label', { class: 'form-label', for: 'reauth-senha' }, 'Senha'), senha);
    const codigo = el('input', {
        type: 'text', class: 'form-control auth-otp', id: 'reauth-codigo', inputmode: 'numeric',
        maxlength: '6', autocomplete: 'one-time-code', placeholder: '000000'
    });
    const campoCodigo = el('div', { hidden: true },
        el('label', { class: 'form-label', for: 'reauth-codigo' }, 'Código enviado para o seu e-mail'), codigo);
    const confirmar = el('button', { type: 'submit', class: 'btn btn-primary' }, 'Confirmar');

    const form = el('form', { novalidate: true },
        el('div', { class: 'modal-header' },
            el('h2', { class: 'modal-title h5', id: idTitulo }, 'Confirme sua identidade'),
            el('button', { type: 'button', class: 'btn-close', 'data-bs-dismiss': 'modal', 'aria-label': 'Fechar' })),
        el('div', { class: 'modal-body' }, descricao, campoSenha, campoCodigo, erro),
        el('div', { class: 'modal-footer' },
            el('button', { type: 'button', class: 'btn btn-light', 'data-bs-dismiss': 'modal' }, 'Cancelar'),
            confirmar));

    const raiz = el('div', { class: 'modal fade', tabindex: '-1', 'aria-labelledby': idTitulo, 'aria-modal': 'true', role: 'dialog' },
        el('div', { class: 'modal-dialog modal-dialog-centered' }, el('div', { class: 'modal-content' }, form)));
    document.body.append(raiz);

    const modal = new Modal(raiz, { backdrop: 'static' });
    let confirmado = false;
    let etapaCodigo = false;

    const mostrarErro = (texto) => { erro.textContent = texto; erro.hidden = false; };

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        erro.hidden = true;
        confirmar.disabled = true;
        try {
            const corpo = etapaCodigo ? { codigo: codigo.value.trim() } : { senha: senha.value };
            if (etapaCodigo && !/^\d{6}$/.test(corpo.codigo)) { mostrarErro('Digite os 6 dígitos do código.'); return; }
            if (!etapaCodigo && !corpo.senha) { mostrarErro('Digite sua senha.'); return; }

            const r = await postar(corpo);
            if (r.status === 401) { modal.hide(); irParaLogin('expirada'); return; }
            if (!r.ok) { mostrarErro(mensagemDeErro(r)); return; }

            if (r.data?.requerMFA) {
                etapaCodigo = true;
                campoSenha.hidden = true;
                campoCodigo.hidden = false;
                descricao.textContent = r.data.mensagem || 'Enviamos um código de verificação para o seu e-mail.';
                codigo.focus();
                return;
            }

            confirmado = true;
            modal.hide();
        } catch (_) {
            mostrarErro('Falha de conexão. Tente novamente.');
        } finally {
            confirmar.disabled = false;
        }
    });

    return new Promise(resolve => {
        raiz.addEventListener('shown.bs.modal', () => senha.focus(), { once: true });
        raiz.addEventListener('hidden.bs.modal', () => {
            modal.dispose();
            raiz.remove();
            resolve(confirmado);
        }, { once: true });
        modal.show();
    });
}
