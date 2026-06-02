// ── Lista centralizada de chats ───────────────────────────────────────────────

const root = () => document.getElementById('chat-root');

function esc(str) {
    if (!str) return '';
    return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

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
    try {
        chats = await api.get('/api/me/chats');
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
                <span>💬</span>
                <p>Nenhuma conversa ainda</p>
                <small>Suas conversas com prestadores ou contratantes aparecerão aqui.</small>
            </div>
        </div>`;
        return;
    }

    const itensHtml = chats.map(c => `
    <a class="chat-card"
       href="/pages/chat/detalhe.html?projetoId=${c.projetoId}&prestadorId=${c.prestadorId}"
       aria-label="Chat sobre ${esc(c.projetoTitulo)} com ${esc(c.prestadorNome)}">
        <div class="chat-card-avatar" aria-hidden="true">
            ${esc(c.prestadorNome.charAt(0).toUpperCase())}
        </div>
        <div class="chat-card-body">
            <div class="chat-card-header">
                <span class="chat-card-nome">${esc(c.prestadorNome)}</span>
                <span class="chat-card-tempo">${tempoAtras(c.ultimaMensagemEm)}</span>
            </div>
            <div class="chat-card-projeto">${esc(c.projetoTitulo)}</div>
            <div class="chat-card-preview">${esc(c.ultimaMensagem)}</div>
        </div>
        <div class="chat-card-badge" aria-label="${c.totalMensagens} mensagens">
            ${c.totalMensagens}
        </div>
    </a>`).join('');

    root().innerHTML = `
    <div class="chat-lista-wrap">
        <div class="chat-lista-header">
            <h1>Conversas</h1>
            <span class="chat-lista-count">${chats.length} conversa${chats.length !== 1 ? 's' : ''}</span>
        </div>
        <div class="chat-lista-items">
            ${itensHtml}
        </div>
    </div>`;
}

carregarChats();
