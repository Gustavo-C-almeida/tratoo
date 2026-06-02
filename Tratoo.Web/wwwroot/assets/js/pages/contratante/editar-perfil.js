// ── Editar Perfil Contratante ──────────────────────────────────────────────────

const root = () => document.getElementById('editar-root');

function escHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

function iniciais(nome) {
    return (nome ?? '?').split(' ').slice(0, 2).map(p => p[0]).join('').toUpperCase();
}

async function carregarEEditar() {
    root().innerHTML = '<p style="text-align:center;padding:60px;color:#94a3b8">Carregando...</p>';

    let dados;
    try {
        dados = await api.get('/contratantes/me/perfil');
    } catch {
        root().innerHTML = '<p style="text-align:center;padding:60px;color:#dc2626">Você precisa estar autenticado para editar o perfil.</p>';
        return;
    }

    renderizarFormulario(dados);
}

function renderizarFormulario(d) {
    const isPF = d.tipoPessoa === 'PessoaFisica';

    // ── Barra de completude (só exibida se vier do endpoint /me/perfil) ──────
    const completude = typeof d.porcentagemCompletude === 'number' ? d.porcentagemCompletude : null;

    let completudeHtml = '';
    if (completude !== null) {
        const cor = completude >= 80 ? '#22c55e' : completude >= 50 ? '#f59e0b' : '#ef4444';
        const proximoPasso = d.proximoPassoCompletude
            ? `<p style="font-size:13px;color:#64748b;margin-top:6px">💡 ${escHtml(d.proximoPassoCompletude)}</p>`
            : '';
        completudeHtml = `
        <div class="perfil-secao" style="padding:16px 20px">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:6px">
                <span style="font-size:14px;font-weight:600;color:#0f172a">Completude do perfil</span>
                <span style="font-size:14px;font-weight:700;color:${cor}">${completude}%</span>
            </div>
            <div style="background:#e2e8f0;border-radius:999px;height:8px;overflow:hidden">
                <div style="background:${cor};width:${completude}%;height:100%;border-radius:999px;transition:width .4s"></div>
            </div>
            ${proximoPasso}
        </div>`;
    }

    root().innerHTML = `
    <div class="perfil-page">
        <div style="display:flex;align-items:center;gap:12px;margin-bottom:24px">
            <a href="perfil.html" style="font-size:14px;color:#64748b;text-decoration:none">← Voltar ao perfil</a>
            <h1 style="font-size:22px;font-weight:700;color:#0f172a">Editar Perfil</h1>
        </div>
        ${completudeHtml}

        <!-- FOTO DE PERFIL -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Foto de Perfil</h2>
            <div style="display:flex;align-items:center;gap:20px">
                ${d.logoUrl
                    ? `<img src="${escHtml(d.logoUrl)}" class="perfil-avatar" alt="Foto atual">`
                    : `<div class="perfil-avatar--placeholder">${iniciais(d.nome)}</div>`
                }
                <div>
                    <input type="file" id="input-foto" accept=".jpg,.jpeg,.png" style="margin-bottom:6px">
                    <p style="font-size:12px;color:#64748b">JPG ou PNG. Máximo 2 MB.</p>
                    <p id="foto-erro" class="form-erro" hidden></p>
                    <p id="foto-sucesso" class="form-sucesso" hidden></p>
                    <div style="display:flex;gap:8px;margin-top:8px">
                        <button class="btn-salvar" id="btn-foto" style="padding:8px 16px;font-size:13px">Salvar foto</button>
                        ${d.logoUrl ? `<button class="btn-remover" id="btn-remover-foto" style="padding:8px 14px;font-size:13px">Remover</button>` : ''}
                    </div>
                </div>
            </div>
        </div>

        <!-- INFORMAÇÕES DO PERFIL -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Informações do Perfil</h2>
            <form id="form-perfil" novalidate>
                <div class="form-group">
                    <label>Sobre / Bio</label>
                    <textarea id="p-bio" maxlength="1000" rows="5"
                        placeholder="Apresentação livre. Fale sobre você ou sua empresa...">${escHtml(d.descricao ?? '')}</textarea>
                    <span id="bio-count" style="font-size:11px;color:#94a3b8">${(d.descricao ?? '').length}/1000</span>
                </div>
                <div class="form-group">
                    <label>Site</label>
                    <input type="url" id="p-site" value="${escHtml(d.siteUrl ?? '')}"
                        placeholder="https://meusite.com.br">
                </div>
                <div class="form-group">
                    <label>LinkedIn</label>
                    <input type="url" id="p-linkedin" value="${escHtml(d.linkedinUrl ?? '')}"
                        placeholder="https://linkedin.com/in/seuperfil">
                </div>
                <div class="form-group">
                    <label>Telefone <span style="font-weight:normal;color:#9ca3af;font-size:12px">(privado — visível apenas para a plataforma)</span></label>
                    <input type="text" id="p-telefone" value="${escHtml(d.telefone ?? '')}"
                        placeholder="(11) 99999-0000" maxlength="15" inputmode="numeric">
                </div>
                ${isPF ? `
                <label class="form-check">
                    <input type="checkbox" id="p-exibir-idade" ${d.exibirIdade ? 'checked' : ''}>
                    Exibir minha idade publicamente no perfil
                </label>
                ` : ''}
                <p id="perfil-erro" class="form-erro" hidden></p>
                <p id="perfil-sucesso" class="form-sucesso" hidden></p>
                <button type="submit" class="btn-salvar" id="btn-salvar-perfil">Salvar alterações</button>
            </form>
        </div>

        <!-- PRIVACIDADE DAS AVALIAÇÕES -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Privacidade das Avaliações</h2>
            <label class="form-check" style="align-items:flex-start;gap:10px;cursor:pointer">
                <input type="checkbox" id="p-avaliacoes-privado" ${d.avaliacoesPrivado ? 'checked' : ''}>
                <span>
                    <strong>Ocultar minhas avaliações publicamente</strong><br>
                    <span style="font-size:12px;color:#64748b">Quando ativado, sua nota, comentários e média ficam invisíveis para terceiros e nas listagens.</span>
                </span>
            </label>
            <p id="privacidade-err" class="form-erro" hidden></p>
            <p id="privacidade-ok" class="form-sucesso" hidden></p>
            <button type="button" class="btn-salvar" id="btn-privacidade" style="margin-top:14px">Salvar preferência</button>
        </div>
    </div>
    `;

    // Contador bio
    const bio = document.getElementById('p-bio');
    const bioCount = document.getElementById('bio-count');
    bio.addEventListener('input', () => { bioCount.textContent = `${bio.value.length}/1000`; });

    // Máscara telefone
    const inputTel = document.getElementById('p-telefone');
    if (inputTel) {
        inputTel.addEventListener('input', () => {
            const d = inputTel.value.replace(/\D/g, '').slice(0, 11);
            inputTel.value = d.length <= 10
                ? d.replace(/(\d{2})(\d{4})(\d{0,4})/, '($1) $2-$3').trim().replace(/-$/, '')
                : d.replace(/(\d{2})(\d{5})(\d{0,4})/, '($1) $2-$3').trim().replace(/-$/, '');
        });
    }

    // Upload foto
    document.getElementById('btn-foto').addEventListener('click', salvarFoto);

    // Remover foto
    const btnRemover = document.getElementById('btn-remover-foto');
    if (btnRemover) btnRemover.addEventListener('click', removerFoto);

    // Salvar perfil
    document.getElementById('form-perfil').addEventListener('submit', salvarPerfil);

    document.getElementById('btn-privacidade').addEventListener('click', salvarPrivacidade);
}

async function salvarFoto() {
    const input = document.getElementById('input-foto');
    const btn = document.getElementById('btn-foto');
    const erro = document.getElementById('foto-erro');
    const sucesso = document.getElementById('foto-sucesso');

    erro.hidden = true; sucesso.hidden = true;

    if (!input.files || !input.files[0]) {
        erro.textContent = 'Selecione um arquivo JPG ou PNG.';
        erro.hidden = false;
        return;
    }

    const arquivo = input.files[0];
    const ext = arquivo.name.split('.').pop().toLowerCase();
    if (!['jpg', 'jpeg', 'png'].includes(ext)) {
        erro.textContent = 'Formato inválido. Use JPG ou PNG.';
        erro.hidden = false;
        return;
    }
    if (arquivo.size > 2 * 1024 * 1024) {
        erro.textContent = 'Arquivo muito grande. Máximo 2 MB.';
        erro.hidden = false;
        return;
    }

    btn.disabled = true;
    btn.textContent = 'Enviando...';

    const formData = new FormData();
    formData.append('foto', arquivo);

    try {
        await api.uploadPost('/contratantes/me/foto', formData);
        window.location.href = 'perfil.html';
    } catch (err) {
        erro.textContent = err?.data?.mensagem ?? 'Erro ao enviar a foto.';
        erro.hidden = false;
        btn.disabled = false;
        btn.textContent = 'Salvar foto';
    }
}

async function removerFoto() {
    const btn = document.getElementById('btn-remover-foto');
    const erro = document.getElementById('foto-erro');
    const sucesso = document.getElementById('foto-sucesso');

    erro.hidden = true; sucesso.hidden = true;

    if (!confirm('Remover foto de perfil?')) return;

    btn.disabled = true;
    try {
        await api.delete('/contratantes/me/foto');
        window.location.reload();
    } catch (err) {
        erro.textContent = err?.data?.mensagem ?? 'Erro ao remover a foto.';
        erro.hidden = false;
        btn.disabled = false;
    }
}

async function salvarPerfil(e) {
    e.preventDefault();

    const btn = document.getElementById('btn-salvar-perfil');
    const erro = document.getElementById('perfil-erro');
    const sucesso = document.getElementById('perfil-sucesso');
    erro.hidden = true; sucesso.hidden = true;

    const descricao = document.getElementById('p-bio').value.trim() || null;
    const siteUrl = document.getElementById('p-site').value.trim() || null;
    const linkedinUrl = document.getElementById('p-linkedin').value.trim() || null;
    const telefone = document.getElementById('p-telefone').value.trim() || null;
    const exibirIdade = document.getElementById('p-exibir-idade')?.checked ?? false;

    btn.disabled = true;
    btn.textContent = 'Salvando...';

    try {
        await api.put('/contratantes/me/perfil', { descricao, siteUrl, linkedinUrl, telefone, exibirIdade });
        sucesso.textContent = 'Perfil atualizado com sucesso!';
        sucesso.hidden = false;
    } catch (err) {
        erro.textContent = err?.data?.mensagem ?? 'Erro ao salvar o perfil.';
        erro.hidden = false;
    } finally {
        btn.disabled = false;
        btn.textContent = 'Salvar alterações';
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

carregarEEditar();
