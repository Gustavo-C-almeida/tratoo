// Página: /pages/avaliacao/enviar.html?id={guid}
// Exibe o formulário de avaliação (nota + comentário + privacidade) para um slot pendente.

const params   = new URLSearchParams(location.search);
const avaliacaoId = params.get('id');
const root     = document.getElementById('av-root');

let notaSelecionada = 0;

// ──────────────────────────────────────────────────────────────────────────────
// INIT
// ──────────────────────────────────────────────────────────────────────────────

async function init() {
    if (!avaliacaoId) {
        root.innerHTML = '<p class="erro-msg">ID da avaliação não informado.</p>';
        return;
    }

    root.innerHTML = '<p class="loading-msg">Carregando avaliação...</p>';

    try {
        const pendente = await api.get(`/api/avaliacoes/${avaliacaoId}`);

        if (!pendente) {
            root.innerHTML = `
                <div class="av-container">
                    <p class="av-encerrada">Esta avaliação não está disponível ou já foi enviada.</p>
                    <a href="javascript:history.back()" class="btn-secundario">Voltar</a>
                </div>`;
            return;
        }

        if (pendente.jaEnviou) {
            renderAguardandoPar(pendente);
        } else {
            renderFormulario(pendente);
        }
    } catch (err) {
        if (err?.status === 401) {
            location.href = '/pages/auth/login.html';
        } else {
            root.innerHTML = `<p class="erro-msg">${err?.data?.mensagem || 'Erro ao carregar avaliação.'}</p>`;
        }
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// RENDER: aguardando o outro lado
// ──────────────────────────────────────────────────────────────────────────────

function renderAguardandoPar(av) {
    root.innerHTML = `
        <div class="av-container">
            <header class="av-header">
                <a href="javascript:history.back()" class="back-link-pag">← Voltar</a>
                <h1>Avaliação enviada</h1>
            </header>
            <div class="av-card av-aguardando">
                <div class="av-aguardando-icon">⏳</div>
                <h2>Aguardando avaliação da outra parte</h2>
                <p>Você já enviou sua avaliação para <strong>${escapeHtml(av.avaliadoNome)}</strong>.</p>
                <p>As avaliações serão publicadas simultaneamente quando a outra parte também responder,
                   ou após 7 dias automaticamente.</p>
                <p class="av-projeto">${escapeHtml(av.projetoTitulo)}</p>
            </div>
        </div>`;
}

// ──────────────────────────────────────────────────────────────────────────────
// RENDER: formulário de avaliação
// ──────────────────────────────────────────────────────────────────────────────

function renderFormulario(av) {
    const fotoHtml = av.avaliadoFotoUrl
        ? `<img src="${escapeHtml(av.avaliadoFotoUrl)}" alt="${escapeHtml(av.avaliadoNome)}" class="av-foto">`
        : `<div class="av-foto-placeholder">${av.avaliadoNome?.[0]?.toUpperCase() || '?'}</div>`;

    root.innerHTML = `
        <div class="av-container">
            <header class="av-header">
                <a href="javascript:history.back()" class="back-link-pag">← Voltar</a>
                <h1>Avaliar prestação de serviço</h1>
            </header>

            <div class="av-card">
                <div class="av-avaliado">
                    ${fotoHtml}
                    <div>
                        <p class="av-avaliado-nome">${escapeHtml(av.avaliadoNome)}</p>
                        <p class="av-projeto">${escapeHtml(av.projetoTitulo)}</p>
                    </div>
                </div>

                <div class="av-nota-section">
                    <label class="av-label">Sua nota <span class="obrigatorio">*</span></label>
                    <div class="estrelas" id="estrelas">
                        ${[1,2,3,4,5].map(n => `
                            <button type="button" class="estrela" data-nota="${n}" aria-label="${n} estrelas">★</button>
                        `).join('')}
                    </div>
                    <p class="av-nota-texto" id="nota-texto"></p>
                </div>

                <div class="av-campo">
                    <label class="av-label" for="comentario">Comentário <span class="av-opcional">(opcional)</span></label>
                    <textarea id="comentario" rows="4" maxlength="1000"
                        placeholder="Descreva sua experiência com o serviço prestado..."></textarea>
                    <p class="av-char-count"><span id="char-count">0</span>/1000</p>
                </div>

                <div class="av-privacidade">
                    <label class="av-toggle-label">
                        <input type="checkbox" id="chk-publica" checked>
                        <span class="av-toggle-text">
                            <strong>Avaliação pública</strong>
                            <small>Seu comentário pode aparecer no perfil de ${escapeHtml(av.avaliadoNome)},
                            caso ele permita avaliações públicas.</small>
                        </span>
                    </label>
                </div>

                <div class="av-aviso-blind">
                    🔒 <strong>Avaliação cega (blind review):</strong> sua avaliação só será publicada
                    quando a outra parte também responder, evitando influência mútua.
                </div>

                <div class="av-acoes">
                    <button id="btn-enviar" class="btn-enviar" disabled>Enviar avaliação</button>
                </div>
            </div>
        </div>`;

    bindFormulario();
}

// ──────────────────────────────────────────────────────────────────────────────
// BIND EVENTOS
// ──────────────────────────────────────────────────────────────────────────────

function bindFormulario() {
    const estrelas = document.querySelectorAll('.estrela');
    const notaTexto = document.getElementById('nota-texto');
    const btnEnviar = document.getElementById('btn-enviar');
    const textarea = document.getElementById('comentario');
    const charCount = document.getElementById('char-count');

    const notas = ['', 'Muito ruim', 'Ruim', 'Regular', 'Bom', 'Excelente'];

    // Hover nas estrelas
    estrelas.forEach(btn => {
        btn.addEventListener('mouseenter', () => destacarEstrelas(btn.dataset.nota));
        btn.addEventListener('mouseleave', () => destacarEstrelas(notaSelecionada));
        btn.addEventListener('click', () => {
            notaSelecionada = parseInt(btn.dataset.nota);
            destacarEstrelas(notaSelecionada);
            notaTexto.textContent = notas[notaSelecionada] || '';
            btnEnviar.disabled = false;
        });
    });

    // Contador de caracteres
    textarea.addEventListener('input', () => {
        charCount.textContent = textarea.value.length;
    });

    // Enviar
    btnEnviar.addEventListener('click', enviarAvaliacao);
}

function destacarEstrelas(nota) {
    const n = parseInt(nota) || 0;
    document.querySelectorAll('.estrela').forEach((btn, i) => {
        btn.classList.toggle('ativa', i < n);
    });
}

// ──────────────────────────────────────────────────────────────────────────────
// ENVIAR
// ──────────────────────────────────────────────────────────────────────────────

async function enviarAvaliacao() {
    if (!notaSelecionada) {
        showToast('Selecione uma nota antes de enviar.', true);
        return;
    }

    const comentario = document.getElementById('comentario')?.value?.trim() || null;
    const publica = document.getElementById('chk-publica')?.checked ?? true;

    const btn = document.getElementById('btn-enviar');
    btn.disabled = true;
    btn.textContent = 'Enviando...';

    try {
        await api.post(`/api/avaliacoes/${avaliacaoId}/enviar`, {
            nota: notaSelecionada,
            comentario: comentario || null,
            publica
        });

        root.innerHTML = `
            <div class="av-container">
                <div class="av-card av-sucesso">
                    <div class="av-sucesso-icon">✓</div>
                    <h2>Avaliação enviada!</h2>
                    <p>Obrigado pelo seu feedback. Ele será publicado quando a outra parte também responder.</p>
                    <a href="/pages/me/contratos.html" class="btn-secundario">Ver meus contratos</a>
                </div>
            </div>`;
    } catch (err) {
        showToast(err?.data?.mensagem || 'Erro ao enviar avaliação.', true);
        btn.disabled = false;
        btn.textContent = 'Enviar avaliação';
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
        .replace(/&/g, '&amp;').replace(/</g, '&lt;')
        .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

init();
