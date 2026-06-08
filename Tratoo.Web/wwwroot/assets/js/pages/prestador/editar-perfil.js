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
            <a href="perfil.html" style="font-size:14px;color:#64748b;text-decoration:none"><i class="fa-solid fa-arrow-left"></i> Voltar ao perfil</a>
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
                        placeholder="Ex: Designer UI/UX | Figma & Illustrator, Redator, Consultor...">
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
                            placeholder="Ex: Design Gráfico, Marketing Digital, Tradução...">
                    </div>
                    <div class="form-group">
                        <label>Função executada</label>
                        <input type="text" id="p-funcao" value="${escHtml(d.funcaoExecutada ?? '')}"
                            placeholder="Ex: Designer, Social Media, Editor de Vídeo...">
                    </div>
                </div>
                <div class="form-group">
                    <label>Telefone / WhatsApp</label>
                    <input type="tel" id="p-telefone" value="${escHtml(d.telefone ?? '')}"
                        placeholder="(11) 9 0000-0000">
                </div>
                <div class="form-group">
                    <label>E-mail de contato público</label>
                    <input type="email" id="p-email-contato" value="${escHtml(d.emailContato ?? '')}"
                        placeholder="contato@exemplo.com">
                </div>
                <div class="form-group">
                    <label>LinkedIn</label>
                    <input type="url" id="p-linkedin" value="${escHtml(d.linkedinUrl ?? '')}"
                        placeholder="https://linkedin.com/in/...">
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

        <!-- DADOS BANCÁRIOS -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Dados Bancários (recebimento via PIX)</h2>
            <p style="font-size:13px;color:#64748b;margin-bottom:12px">
                Necessários para receber os pagamentos liberados. Por segurança, qualquer alteração
                exige confirmação por um código enviado ao seu e-mail.
            </p>
            <div id="dados-bancarios-container">
                <p style="font-size:13px;color:#94a3b8">Carregando...</p>
            </div>
        </div>

        <!-- ZONA DE PERIGO — EXCLUSÃO DE CONTA (LGPD) -->
        <div class="perfil-secao" style="border:1px solid #fecaca;background:#fef2f2">
            <h2 class="perfil-secao__titulo" style="color:#b91c1c">Excluir conta</h2>
            <p style="font-size:13px;color:#7f1d1d;line-height:1.6;margin-bottom:14px">
                Esta ação é <strong>permanente</strong>. Sua conta será desativada e seus dados pessoais
                (nome, e-mail, foto, CPF/CNPJ) serão anonimizados, e seu perfil deixará de aparecer nas
                buscas. Propostas, contratos, pagamentos e avaliações são mantidos por obrigação legal,
                exibindo "Usuário indisponível" no seu lugar. Você não conseguirá mais acessar esta conta.
            </p>
            <p id="excluir-conta-err" class="form-erro" hidden></p>
            <button type="button" class="btn-salvar" id="btn-excluir-conta"
                    style="background:#dc2626;border-color:#dc2626">
                <i class="fa-solid fa-trash-can" aria-hidden="true"></i> Excluir minha conta
            </button>
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

    document.getElementById('btn-excluir-conta').addEventListener('click', excluirConta);

    carregarDadosBancarios();
}

async function excluirConta() {
    const erro = document.getElementById('excluir-conta-err');
    erro.hidden = true;

    const confirmacao = prompt('Esta ação é permanente e não pode ser desfeita.\n\nPara confirmar a exclusão da sua conta, digite EXCLUIR:');
    if (confirmacao === null) return;
    if (confirmacao.trim().toUpperCase() !== 'EXCLUIR') {
        erro.textContent = 'Confirmação incorreta. Digite EXCLUIR para confirmar.';
        erro.hidden = false;
        return;
    }

    const btn = document.getElementById('btn-excluir-conta');
    btn.disabled = true;
    btn.textContent = 'Excluindo...';

    try {
        await api.delete('/usuarios/conta');
        alert('Sua conta foi excluída. Você será redirecionado.');
        window.location.href = '/pages/auth/login.html';
    } catch (err) {
        erro.textContent = err?.data?.mensagem ?? 'Erro ao excluir a conta.';
        erro.hidden = false;
        btn.disabled = false;
        btn.innerHTML = '<i class="fa-solid fa-trash-can" aria-hidden="true"></i> Excluir minha conta';
    }
}

// ── Dados bancários (fluxo seguro com token por e-mail) ─────────────────────────
// Nunca persistir dados bancários em localStorage/sessionStorage — apenas em memória.

function bancEsc(s) { return escHtml(s == null ? '' : String(s)); }

async function carregarDadosBancarios() {
    const cont = document.getElementById('dados-bancarios-container');
    if (!cont) return;
    try {
        const d = await api.get('/prestadores/me/dados-bancarios');
        renderDadosBancariosView(d);
    } catch (e) {
        cont.innerHTML = `<p class="form-erro">Não foi possível carregar os dados bancários.</p>`;
    }
}

function renderDadosBancariosView(d) {
    const cont = document.getElementById('dados-bancarios-container');
    const tipoLabel = {
        CPF: 'CPF', CNPJ: 'CNPJ', Email: 'E-mail', Telefone: 'Telefone', Aleatoria: 'Chave aleatória'
    };

    if (d && d.configurado) {
        cont.innerHTML = `
            <div class="banc-view">
                <div class="banc-linha"><span>Banco</span><strong>${bancEsc(d.banco)}</strong></div>
                <div class="banc-linha"><span>Agência</span><strong>${bancEsc(d.agenciaMascarada)}</strong></div>
                <div class="banc-linha"><span>Conta</span><strong>${bancEsc(d.contaMascarada)}</strong></div>
                <div class="banc-linha"><span>Tipo de chave</span><strong>${bancEsc(tipoLabel[d.tipoPix] || d.tipoPix)}</strong></div>
                <div class="banc-linha"><span>Chave PIX</span><strong>${bancEsc(d.chavePixMascarada)}</strong></div>
            </div>
            <p style="font-size:12px;color:#94a3b8;margin-top:8px">
                Atualizado em ${d.atualizadoEm ? new Date(d.atualizadoEm).toLocaleDateString('pt-BR') : '—'}
            </p>
            <button type="button" class="btn-salvar" id="btn-banc-alterar" style="margin-top:12px">Alterar dados bancários</button>`;
    } else {
        cont.innerHTML = `
            <p style="font-size:13px;color:#64748b">Você ainda não cadastrou seus dados bancários.</p>
            <button type="button" class="btn-salvar" id="btn-banc-alterar" style="margin-top:8px">Cadastrar dados bancários</button>`;
    }

    document.getElementById('btn-banc-alterar').addEventListener('click', solicitarTokenBancario);
}

async function solicitarTokenBancario() {
    const btn = document.getElementById('btn-banc-alterar');
    if (btn) { btn.disabled = true; btn.textContent = 'Enviando código...'; }
    try {
        await api.post('/prestadores/me/dados-bancarios/solicitar-alteracao', {});
        renderTokenBancario();
    } catch (e) {
        if (btn) { btn.disabled = false; btn.textContent = 'Alterar dados bancários'; }
        alert(e?.data?.mensagem || 'Erro ao solicitar o código.');
    }
}

function renderTokenBancario() {
    const cont = document.getElementById('dados-bancarios-container');
    cont.innerHTML = `
        <div class="banc-token">
            <p style="font-size:13px;color:#334155">
                Enviamos um código de confirmação para o seu e-mail. Ele expira em <strong>10 minutos</strong>.
            </p>
            <div class="form-group" style="max-width:220px">
                <label for="banc-token-input">Código de confirmação</label>
                <input type="text" id="banc-token-input" inputmode="numeric" autocomplete="one-time-code"
                       maxlength="6" placeholder="000000">
            </div>
            <p id="banc-token-err" class="form-erro" hidden></p>
            <div style="display:flex;gap:8px;margin-top:8px">
                <button type="button" class="btn-salvar" id="btn-banc-confirmar">Confirmar código</button>
                <button type="button" class="btn-secundario" id="btn-banc-cancelar">Cancelar</button>
            </div>
        </div>`;

    document.getElementById('btn-banc-confirmar').addEventListener('click', confirmarTokenBancario);
    document.getElementById('btn-banc-cancelar').addEventListener('click', carregarDadosBancarios);
    document.getElementById('banc-token-input').focus();
}

async function confirmarTokenBancario() {
    const erro = document.getElementById('banc-token-err');
    erro.hidden = true;
    const token = document.getElementById('banc-token-input').value.trim();
    if (!token) { erro.hidden = false; erro.textContent = 'Informe o código.'; return; }

    const btn = document.getElementById('btn-banc-confirmar');
    btn.disabled = true; btn.textContent = 'Confirmando...';
    try {
        await api.post('/prestadores/me/dados-bancarios/confirmar', { token });
        renderEdicaoBancaria();
    } catch (e) {
        btn.disabled = false; btn.textContent = 'Confirmar código';
        erro.hidden = false;
        erro.textContent = e?.data?.mensagem || 'Código inválido.';
    }
}

function renderEdicaoBancaria() {
    const cont = document.getElementById('dados-bancarios-container');
    cont.innerHTML = `
        <div class="banc-edit">
            <p class="form-sucesso" style="margin-bottom:10px"><i class="fa-solid fa-check"></i> Identidade confirmada. Preencha e salve seus dados (você tem 10 minutos).</p>
            <div class="form-row">
                <div class="form-group" style="flex:0 0 200px">
                    <label for="banc-banco">Banco</label>
                    <input type="text" id="banc-banco" placeholder="Ex: Nubank" maxlength="60">
                </div>
                <div class="form-group" style="flex:0 0 120px">
                    <label for="banc-agencia">Agência</label>
                    <input type="text" id="banc-agencia" inputmode="numeric" placeholder="0001" maxlength="10">
                </div>
                <div class="form-group">
                    <label for="banc-conta">Conta (com dígito)</label>
                    <input type="text" id="banc-conta" inputmode="numeric" placeholder="123456-7" maxlength="20">
                </div>
            </div>
            <div class="form-row">
                <div class="form-group" style="flex:0 0 200px">
                    <label for="banc-tipo">Tipo de chave PIX</label>
                    <select id="banc-tipo">
                        <option value="CPF">CPF</option>
                        <option value="CNPJ">CNPJ</option>
                        <option value="Email">E-mail</option>
                        <option value="Telefone">Telefone</option>
                        <option value="Aleatoria">Chave aleatória</option>
                    </select>
                </div>
                <div class="form-group">
                    <label for="banc-chave">Chave PIX</label>
                    <input type="text" id="banc-chave" placeholder="Informe a chave conforme o tipo" maxlength="120">
                </div>
            </div>
            <p id="banc-edit-err" class="form-erro" hidden></p>
            <div style="display:flex;gap:8px;margin-top:8px">
                <button type="button" class="btn-salvar" id="btn-banc-salvar">Salvar dados bancários</button>
                <button type="button" class="btn-secundario" id="btn-banc-cancelar2">Cancelar</button>
            </div>
        </div>`;

    document.getElementById('btn-banc-salvar').addEventListener('click', salvarDadosBancarios);
    document.getElementById('btn-banc-cancelar2').addEventListener('click', carregarDadosBancarios);
}

async function salvarDadosBancarios() {
    const erro = document.getElementById('banc-edit-err');
    erro.hidden = true;

    const payload = {
        banco: document.getElementById('banc-banco').value.trim(),
        agencia: document.getElementById('banc-agencia').value.trim(),
        conta: document.getElementById('banc-conta').value.trim(),
        tipoPix: document.getElementById('banc-tipo').value,
        chavePix: document.getElementById('banc-chave').value.trim(),
    };

    if (!payload.banco || !payload.agencia || !payload.conta || !payload.chavePix) {
        erro.hidden = false; erro.textContent = 'Preencha todos os campos.'; return;
    }

    const btn = document.getElementById('btn-banc-salvar');
    btn.disabled = true; btn.textContent = 'Salvando...';
    try {
        const atualizado = await api.put('/prestadores/me/dados-bancarios', payload);
        renderDadosBancariosView(atualizado);
    } catch (e) {
        btn.disabled = false; btn.textContent = 'Salvar dados bancários';
        erro.hidden = false;
        erro.textContent = e?.data?.mensagem || 'Erro ao salvar. Talvez seja necessário confirmar o código novamente.';
    }
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
            <button type="button" class="btn-icon btn-icon--danger" onclick="removerLinkExtra(${i})"><i class="fa-solid fa-xmark"></i></button>
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
        <button type="button" class="btn-icon btn-icon--danger" onclick="removerLinkExtra(${i})"><i class="fa-solid fa-xmark"></i></button>
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
        telefone:           document.getElementById('p-telefone').value.trim() || null,
        emailContato:       document.getElementById('p-email-contato').value.trim() || null,
        linkedinUrl:        document.getElementById('p-linkedin').value.trim() || null,
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
