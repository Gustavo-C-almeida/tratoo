// ── Editar Perfil Prestador ───────────────────────────────────────────────────
// Carrega os dados do próprio perfil e permite edição em seções.

const root = () => document.getElementById('editar-root');

function escHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

async function carregarEEditar() {
    root().innerHTML = '<p style="text-align:center;padding:60px;color:#94a3b8">Carregando...</p>';

    let dados;
    try {
        dados = await api.get('/prestadores/me/perfil');
    } catch {
        root().innerHTML = '<p style="text-align:center;padding:60px;color:#dc2626">Você precisa estar autenticado para editar o perfil.</p>';
        return;
    }

    renderizarFormulario(dados);
}

function renderizarFormulario(d) {
    root().innerHTML = `
    <div class="perfil-page">
        <div style="display:flex;align-items:center;gap:12px;margin-bottom:24px">
            <a href="perfil.html" style="font-size:14px;color:#64748b;text-decoration:none">← Voltar ao perfil</a>
            <h1 style="font-size:22px;font-weight:700;color:#0f172a">Editar Perfil</h1>
        </div>

        <!-- FOTO DE PERFIL -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Foto de Perfil</h2>
            <div style="display:flex;align-items:center;gap:20px">
                ${d.fotoUrl
                    ? `<img src="${escHtml(d.fotoUrl)}" style="width:80px;height:80px;border-radius:50%;object-fit:cover;border:2px solid #e2e8f0" alt="Foto atual">`
                    : `<div style="width:80px;height:80px;border-radius:50%;background:#004584;display:flex;align-items:center;justify-content:center;font-size:28px;font-weight:700;color:#fff">
                        ${(d.nome ?? '?').split(' ').slice(0,2).map(p => p[0]).join('').toUpperCase()}
                       </div>`
                }
                <div>
                    <input type="file" id="input-foto" accept=".jpg,.jpeg,.png,.webp" style="margin-bottom:6px">
                    <p style="font-size:12px;color:#64748b">JPG, PNG ou WebP. Máximo 5 MB.</p>
                    <p id="foto-erro" class="form-erro" hidden></p>
                    <button class="btn-salvar" id="btn-foto" onclick="salvarFoto()" style="margin-top:8px;padding:8px 16px;font-size:13px">Salvar foto</button>
                </div>
            </div>
        </div>

        <!-- INFORMAÇÕES BÁSICAS -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Informações do Perfil</h2>
            <form id="form-perfil" novalidate>
                <div class="form-group">
                    <label>Título profissional</label>
                    <input type="text" id="p-titulo" value="${escHtml(d.tituloProfissional ?? '')}" maxlength="80"
                        placeholder="Ex: Desenvolvedor Full-Stack | React & .NET">
                </div>
                <div class="form-group">
                    <label>Bio (mínimo 100 caracteres para pontuar)</label>
                    <textarea id="p-bio" maxlength="1000" rows="5"
                        placeholder="Fale sobre você, sua trajetória e o que você oferece...">${escHtml(d.descricao ?? '')}</textarea>
                    <span id="bio-count" style="font-size:11px;color:#94a3b8">${(d.descricao ?? '').length}/1000</span>
                </div>
                <div class="form-row">
                    <div class="form-group">
                        <label>Área de especialização</label>
                        <input type="text" id="p-area" value="${escHtml(d.areaEspecializacao ?? '')}"
                            placeholder="Ex: Desenvolvimento Web">
                    </div>
                    <div class="form-group">
                        <label>Função executada</label>
                        <input type="text" id="p-funcao" value="${escHtml(d.funcaoExecutada ?? '')}"
                            placeholder="Ex: Desenvolvedor Backend">
                    </div>
                </div>
                <div class="form-row">
                    <div class="form-group">
                        <label>Valor por hora (R$)</label>
                        <input type="number" id="p-valor" value="${d.valorHora ?? ''}" min="0" step="0.01"
                            placeholder="0,00">
                    </div>
                    <div class="form-group">
                        <label>Telefone / WhatsApp</label>
                        <input type="tel" id="p-telefone" value="${escHtml(d.telefone ?? '')}"
                            placeholder="(11) 9 0000-0000">
                    </div>
                </div>
                <div class="form-group">
                    <label>E-mail de contato público</label>
                    <input type="email" id="p-email-contato" value="${escHtml(d.emailContato ?? '')}"
                        placeholder="contato@exemplo.com">
                </div>
                <div class="form-row">
                    <div class="form-group">
                        <label>LinkedIn</label>
                        <input type="url" id="p-linkedin" value="${escHtml(d.linkedinUrl ?? '')}"
                            placeholder="https://linkedin.com/in/...">
                    </div>
                    <div class="form-group">
                        <label>GitHub</label>
                        <input type="url" id="p-github" value="${escHtml(d.gitHubUrl ?? '')}"
                            placeholder="https://github.com/...">
                    </div>
                </div>
                <div class="form-group">
                    <label>URL do portfólio externo</label>
                    <input type="url" id="p-portfolio-url" value="${escHtml(d.portfolioUrl ?? '')}"
                        placeholder="https://meuportfolio.com">
                </div>

                <!-- Links extras (até 3) -->
                <div class="form-group">
                    <label>Links extras (até 3)</label>
                    <div id="links-extras-wrap">
                        ${renderizarLinksExtrasForm(d.outrosLinks)}
                    </div>
                    <button type="button" class="btn-cancelar" onclick="adicionarLinkExtra()" style="margin-top:6px;font-size:12px">
                        + Adicionar link
                    </button>
                </div>

                <p id="perfil-erro" class="form-erro" hidden></p>
                <div class="form-acoes">
                    <a href="perfil.html" class="btn-cancelar" style="text-decoration:none">Cancelar</a>
                    <button type="submit" class="btn-salvar" id="btn-salvar-perfil">Salvar alterações</button>
                </div>
            </form>
        </div>

        <!-- PRIVACIDADE DAS AVALIAÇÕES -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Privacidade das Avaliações</h2>
            <label class="form-check" style="align-items:flex-start;gap:10px;cursor:pointer">
                <input type="checkbox" id="p-avaliacoes-privado" ${d.avaliacoesPrivado ? 'checked' : ''}>
                <span>
                    <strong>Ocultar minhas avaliações publicamente</strong><br>
                    <span style="font-size:12px;color:#64748b">Quando ativado, sua nota, comentários e média ficam invisíveis para terceiros e na busca de prestadores.</span>
                </span>
            </label>
            <p id="privacidade-err" class="form-erro" hidden></p>
            <p id="privacidade-ok" class="form-sucesso" hidden></p>
            <button type="button" class="btn-salvar" id="btn-privacidade" style="margin-top:14px">Salvar preferência</button>
        </div>
    </div>`;

    // Contador de caracteres da bio
    document.getElementById('p-bio').addEventListener('input', function() {
        document.getElementById('bio-count').textContent = `${this.value.length}/1000`;
    });

    document.getElementById('form-perfil').addEventListener('submit', async e => {
        e.preventDefault();
        await salvarPerfil();
    });

    document.getElementById('btn-privacidade').addEventListener('click', salvarPrivacidade);
}

function renderizarLinksExtrasForm(json) {
    let links = [];
    try { links = json ? JSON.parse(json) : []; } catch { }

    return links.map((l, i) => `
        <div class="form-row" id="link-extra-${i}" style="margin-bottom:6px">
            <div class="form-group" style="flex:0 0 160px">
                <input type="text" placeholder="Título" value="${escHtml(l.titulo ?? '')}" class="link-titulo">
            </div>
            <div class="form-group">
                <input type="url" placeholder="URL" value="${escHtml(l.url ?? '')}" class="link-url">
            </div>
            <button type="button" class="btn-icon btn-icon--danger" onclick="removerLinkExtra(${i})">✕</button>
        </div>
    `).join('');
}

function adicionarLinkExtra() {
    const wrap = document.getElementById('links-extras-wrap');
    const atual = wrap.querySelectorAll('[id^="link-extra-"]').length;
    if (atual >= 3) { alert('Máximo de 3 links extras.'); return; }
    const i = atual;
    const div = document.createElement('div');
    div.className = 'form-row';
    div.id = `link-extra-${i}`;
    div.style.marginBottom = '6px';
    div.innerHTML = `
        <div class="form-group" style="flex:0 0 160px">
            <input type="text" placeholder="Título" class="link-titulo">
        </div>
        <div class="form-group">
            <input type="url" placeholder="URL" class="link-url">
        </div>
        <button type="button" class="btn-icon btn-icon--danger" onclick="removerLinkExtra(${i})">✕</button>
    `;
    wrap.appendChild(div);
}

function removerLinkExtra(i) {
    document.getElementById(`link-extra-${i}`)?.remove();
    // Reindexar
    document.querySelectorAll('#links-extras-wrap > div').forEach((el, idx) => {
        el.id = `link-extra-${idx}`;
    });
}

function coletarLinksExtras() {
    const items = document.querySelectorAll('#links-extras-wrap > div');
    const links = [];
    items.forEach(el => {
        const titulo = el.querySelector('.link-titulo')?.value.trim();
        const url    = el.querySelector('.link-url')?.value.trim();
        if (titulo && url) links.push({ titulo, url });
    });
    return links.length ? JSON.stringify(links) : null;
}

async function salvarFoto() {
    const input = document.getElementById('input-foto');
    const btn   = document.getElementById('btn-foto');
    const erro  = document.getElementById('foto-erro');
    erro.hidden = true;

    if (!input.files.length) { erro.textContent = 'Selecione uma foto.'; erro.hidden = false; return; }

    const arquivo = input.files[0];

    // Validação de extensão client-side (espelha o backend)
    const ext = arquivo.name.split('.').pop().toLowerCase();
    if (!['jpg', 'jpeg', 'png', 'webp'].includes(ext)) {
        erro.textContent = 'Formato inválido. Use JPG, PNG ou WebP.';
        erro.hidden = false;
        return;
    }

    // Validação de tamanho client-side (5 MB)
    if (arquivo.size > 5 * 1024 * 1024) {
        erro.textContent = 'A foto deve ter no máximo 5 MB.';
        erro.hidden = false;
        return;
    }

    btn.disabled = true; btn.textContent = 'Enviando...';

    const form = new FormData();
    form.append('foto', arquivo);

    try {
        await api.uploadPost('/prestadores/me/foto', form);
        window.location.href = 'perfil.html';
    } catch (e) {
        erro.textContent = e?.data?.mensagem ?? 'Erro ao enviar foto.';
        erro.hidden = false;
        btn.disabled = false; btn.textContent = 'Salvar foto';
    }
}

async function salvarPerfil() {
    const btn  = document.getElementById('btn-salvar-perfil');
    const erro = document.getElementById('perfil-erro');
    erro.hidden = true;

    const body = {
        tituloProfissional: document.getElementById('p-titulo').value.trim() || null,
        descricao:          document.getElementById('p-bio').value.trim()    || null,
        areaEspecializacao: document.getElementById('p-area').value.trim()   || null,
        funcaoExecutada:    document.getElementById('p-funcao').value.trim() || null,
        valorHora:          parseFloat(document.getElementById('p-valor').value) || null,
        telefone:           document.getElementById('p-telefone').value.trim() || null,
        emailContato:       document.getElementById('p-email-contato').value.trim() || null,
        linkedinUrl:        document.getElementById('p-linkedin').value.trim() || null,
        gitHubUrl:          document.getElementById('p-github').value.trim()   || null,
        portfolioUrl:       document.getElementById('p-portfolio-url').value.trim() || null,
        outrosLinks:        coletarLinksExtras()
    };

    btn.disabled = true; btn.textContent = 'Salvando...';

    try {
        await api.put('/prestadores/me/perfil', body);
        window.location.href = 'perfil.html';
    } catch (e) {
        erro.textContent = e?.data?.mensagem ?? 'Erro ao salvar perfil.';
        erro.hidden = false;
        btn.disabled = false; btn.textContent = 'Salvar alterações';
    }
}

async function salvarPrivacidade() {
    const btn = document.getElementById('btn-privacidade');
    const err = document.getElementById('privacidade-err');
    const ok  = document.getElementById('privacidade-ok');
    err.hidden = true; ok.hidden = true;

    const privado = document.getElementById('p-avaliacoes-privado').checked;
    btn.disabled = true; btn.textContent = 'Salvando...';

    try {
        await api.put('/api/me/avaliacoes/privacidade', { privado });
        ok.textContent = 'Preferência de privacidade salva!';
        ok.hidden = false;
    } catch (e) {
        err.textContent = e?.data?.mensagem ?? 'Erro ao salvar preferência.';
        err.hidden = false;
    } finally {
        btn.disabled = false; btn.textContent = 'Salvar preferência';
    }
}

// ── Init ──────────────────────────────────────────────────────────────────────
carregarEEditar();
