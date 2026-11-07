// ── pages/conta/seguranca.js — Segurança e dispositivos (ES module) ──────────
// Lista as sessões (GET /api/auth/sessoes), encerra uma (DELETE /api/auth/sessoes/{id})
// ou todas (POST /api/auth/logout-todas), troca a senha (POST /api/conta/senha) e
// liga/desliga o MFA (PUT /api/conta/mfa). As ações sensíveis podem responder 403
// REAUTH_REQUIRED: o api.js abre o modal de senha/MFA e repete a chamada sozinho.
//
// Dados da API (user-agent, IP) entram só como texto, via el().

import { api } from '/assets/js/services/api.js';
import { onReady } from '/assets/js/core/app.js';
import { el } from '/assets/js/utils/dom.js';
import { setButtonLoading, showError, hideError, getErrorMessage } from '/assets/js/utils/form.js';
import { encerrarSessao } from '/assets/js/services/refresh.js';

const formatoDataHora = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' });

function quando(iso) {
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? '—' : formatoDataHora.format(d);
}

/** "Chrome no Windows" a partir do user-agent (aproximado, só para leitura humana). */
function descreverDispositivo(ua) {
    if (!ua) return 'Dispositivo desconhecido';
    const navegador =
        /Edg\//.test(ua) ? 'Edge' :
        /OPR\//.test(ua) ? 'Opera' :
        /Firefox\//.test(ua) ? 'Firefox' :
        /Chrome\//.test(ua) ? 'Chrome' :
        /Safari\//.test(ua) ? 'Safari' : 'Navegador';
    const sistema =
        /Windows/.test(ua) ? 'Windows' :
        /Android/.test(ua) ? 'Android' :
        /iPhone|iPad|iPod/.test(ua) ? 'iOS' :
        /Mac OS X|Macintosh/.test(ua) ? 'macOS' :
        /Linux/.test(ua) ? 'Linux' : null;
    return sistema ? `${navegador} no ${sistema}` : navegador;
}

function mostrarOk(texto) {
    hideError('seguranca-erro');
    const ok = document.getElementById('seguranca-ok');
    ok.textContent = texto;
    ok.hidden = false;
}

function mostrarErro(err, padrao) {
    document.getElementById('seguranca-ok').hidden = true;
    showError('seguranca-erro', getErrorMessage(err, padrao));
}

// ── Dispositivos ──────────────────────────────────────────────────────────────

async function carregarSessoes() {
    const lista = document.getElementById('lista-sessoes');
    let sessoes;
    try {
        sessoes = await api.get('/api/auth/sessoes');
    } catch (err) {
        lista.replaceChildren(el('li', { class: 'list-group-item text-secondary' }, 'Não foi possível carregar os dispositivos.'));
        return;
    }

    if (!sessoes.length) {
        lista.replaceChildren(el('li', { class: 'list-group-item text-secondary' }, 'Nenhuma sessão ativa.'));
        return;
    }

    lista.replaceChildren(...sessoes.map(s => {
        const encerrar = el('button', { type: 'button', class: 'btn btn-outline-secondary btn-sm' },
            s.atual ? 'Sair deste' : 'Encerrar');
        encerrar.addEventListener('click', () => encerrarUma(s, encerrar));

        return el('li', { class: 'list-group-item d-flex flex-wrap align-items-center justify-content-between gap-2 px-0' },
            el('div', {},
                el('div', { class: 'fw-semibold' },
                    descreverDispositivo(s.dispositivo),
                    s.atual && el('span', { class: 'badge text-bg-success ms-2' }, 'Este dispositivo'),
                    s.manterConectado && el('span', { class: 'badge text-bg-light ms-2' }, 'Manter conectado')),
                el('div', { class: 'seguranca__sessao-detalhe', title: s.dispositivo || '' },
                    `Última atividade: ${quando(s.ultimaAtividadeEm)} · Entrou em ${quando(s.criadaEm)} · IP ${s.ip || '—'}`)),
            encerrar);
    }));
}

async function encerrarUma(sessao, botao) {
    const restaurar = setButtonLoading(botao, 'Encerrando...');
    try {
        const r = await api.delete(`/api/auth/sessoes/${encodeURIComponent(sessao.id)}`);
        if (r?.eraAtual) {
            await encerrarSessao();   // avisa as outras abas
            window.location.replace('/pages/auth/login.html');
            return;
        }
        mostrarOk('Sessão encerrada. Aquele dispositivo precisará entrar de novo.');
        await carregarSessoes();
    } catch (err) {
        restaurar();
        mostrarErro(err, 'Não foi possível encerrar a sessão.');
    }
}

async function sairDeTodas() {
    const botao = document.getElementById('btn-sair-todas');
    if (!window.confirm('Sair de todos os dispositivos, inclusive este?')) return;
    const restaurar = setButtonLoading(botao, 'Saindo...');
    try {
        await api.post('/api/auth/logout-todas');
        await encerrarSessao();
        window.location.replace('/pages/auth/login.html');
    } catch (err) {
        restaurar();
        mostrarErro(err, 'Não foi possível sair de todos os dispositivos.');
    }
}

// ── Senha ─────────────────────────────────────────────────────────────────────

async function trocarSenha(e) {
    e.preventDefault();
    const form = e.target;
    const novaSenha = document.getElementById('nova-senha').value;
    const confirmar = document.getElementById('confirmar-senha').value;
    if (!novaSenha) return mostrarErro(null, 'Digite a nova senha.');
    if (novaSenha !== confirmar) return mostrarErro(null, 'As senhas não conferem.');

    const restaurar = setButtonLoading(form.querySelector('button[type="submit"]'), 'Salvando...');
    try {
        const r = await api.post('/api/conta/senha', { novaSenha, confirmarNovaSenha: confirmar });
        form.reset();
        mostrarOk(r?.mensagem || 'Senha alterada.');
        await carregarSessoes();
    } catch (err) {
        mostrarErro(err, 'Não foi possível alterar a senha.');
    } finally {
        restaurar();
    }
}

// ── MFA ───────────────────────────────────────────────────────────────────────

async function carregarMfa() {
    const status = document.getElementById('mfa-status');
    const botao = document.getElementById('btn-mfa');
    try {
        const s = await api.get('/api/conta/seguranca');
        status.textContent = s.mfaAtivo
            ? (s.mfaObrigatorio ? 'Ativa (obrigatória para administradores).' : 'Ativa.')
            : 'Desativada.';
        botao.textContent = s.mfaAtivo ? 'Desativar verificação em duas etapas' : 'Ativar verificação em duas etapas';
        botao.dataset.ativar = s.mfaAtivo ? 'false' : 'true';
        botao.hidden = s.mfaAtivo && s.mfaObrigatorio;
    } catch (err) {
        status.textContent = 'Não foi possível carregar.';
    }
}

async function alternarMfa() {
    const botao = document.getElementById('btn-mfa');
    const ativar = botao.dataset.ativar === 'true';
    const pergunta = ativar
        ? 'Ativar a verificação em duas etapas? Você será desconectado e precisará entrar de novo.'
        : 'Desativar a verificação em duas etapas? Você será desconectado e precisará entrar de novo.';
    if (!window.confirm(pergunta)) return;

    const restaurar = setButtonLoading(botao, 'Salvando...');
    try {
        await api.put('/api/conta/mfa', { ativo: ativar });
        // O servidor encerrou todas as sessões, inclusive esta.
        await encerrarSessao();
        window.location.replace('/pages/auth/login.html');
    } catch (err) {
        restaurar();
        mostrarErro(err, 'Não foi possível alterar a verificação em duas etapas.');
    }
}

onReady(() => {
    document.getElementById('btn-sair-todas').addEventListener('click', sairDeTodas);
    document.getElementById('form-senha').addEventListener('submit', trocarSenha);
    document.getElementById('btn-mfa').addEventListener('click', alternarMfa);
    carregarSessoes();
    carregarMfa();
});
