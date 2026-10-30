import { api } from '/assets/js/services/api.js';
import { el } from '/assets/js/utils/dom.js';
// ── Lista centralizada de chats ───────────────────────────────────────────────

const root = () => document.getElementById('chat-root');

function tempoAtras(data) {
    if (!data) return '';
    // data vem com "Z" do backend — new Date() converte para timezone local
    const diff = Date.now() - new Date(data).getTime();
    const min = Math.floor(diff / 60000);
    if (min < 1) return 'agora mesmo';
    if (min < 60) return `há ${min} min`;
    const h = Math.floor(min / 60);
    if (h < 24) return `há ${h}h`;
    const d = Math.floor(h / 24);
    return `há ${d} dia${d > 1 ? 's' : ''}`;
}

async function carregarChats() {
    root().innerHTML = '<div class="chat-lista-loading">Carregando conversas...</div>';

    let chats = [];
    let usuarioId = null;
    try {
        chats = await api.get('/api/me/chats');
        const me = await api.get('/api/me');
        usuarioId = me.id;
    } catch {
        root().innerHTML = '<p class="chat-lista-vazia">Erro ao carregar conversas. Tente novamente.</p>';
        return;
    }

    if (!chats || chats.length === 0) {
        root().innerHTML = `
        <div class="chat-lista-wrap">
            <div class="chat-lista-header">
                <h1>Conversas</h1>
            </div>
            <div class="chat-lista-vazia">
                <span><i class="fa-solid fa-comments"></i></span>
                <p>Nenhuma conversa ainda</p>
                <small>Suas conversas com prestadores ou contratantes aparecerão aqui.</small>
            </div>
        </div>`;
        return;
    }

    const itens = chats.map(c => {
        const outro = c.outroParticipanteNome || c.prestadorNome || 'Participante';
        const mostrarBadge = c.ultimaMensagemPorId !== usuarioId;
        const href = `/pages/chat/detalhe.html?projetoId=${encodeURIComponent(c.projetoId)}`
            + `&prestadorId=${encodeURIComponent(c.prestadorId)}`;
        return el('a', {
            class: 'chat-card',
            href,
            'aria-label': `Chat sobre ${c.projetoTitulo || ''} com ${outro}`,
        },
            el('div', { class: 'chat-card-avatar', 'aria-hidden': 'true' }, (outro.charAt(0) || '?').toUpperCase()),
            el('div', { class: 'chat-card-body' },
                el('div', { class: 'chat-card-header' },
                    el('span', { class: 'chat-card-nome' }, outro),
                    el('span', { class: 'chat-card-tempo' }, tempoAtras(c.ultimaMensagemEm))),
                el('div', { class: 'chat-card-projeto' },
                    el('i', { class: 'fa-solid fa-folder-open', 'aria-hidden': 'true' }), ' ', c.projetoTitulo),
                el('div', { class: 'chat-card-preview' }, c.ultimaMensagem)),
            mostrarBadge && el('div', { class: 'chat-card-badge', 'aria-label': '1 nova mensagem' }, '1'));
    });

    root().replaceChildren(el('div', { class: 'chat-lista-wrap' },
        el('div', { class: 'chat-lista-header' },
            el('h1', {}, 'Conversas'),
            el('span', { class: 'chat-lista-count' }, `${chats.length} conversa${chats.length !== 1 ? 's' : ''}`)),
        el('div', { class: 'chat-lista-items' }, itens)));
}

carregarChats();
