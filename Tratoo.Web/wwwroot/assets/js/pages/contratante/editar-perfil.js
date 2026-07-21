import { api } from '/assets/js/services/api.js';
// ── Editar Perfil Contratante ──────────────────────────────────────────────────

const root = () => document.getElementById('editar-root');

function escHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

function iniciais(nome) {
    return (nome ?? '?').split(' ').slice(0, 2).map(p => p[0]).join('').toUpperCase();
}

const IDIOMAS_DISPONIVEIS = ['Português', 'Inglês', 'Espanhol', 'Francês', 'Alemão', 'Italiano', 'Mandarim'];

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
    const completude = typeof d.porcentagemCompletude === 'number' ? d.porcentagemCompletude : null;

    let completudeHtml = '';
    if (completude !== null) {
        const cor = completude >= 80 ? '#22c55e' : completude >= 50 ? '#f59e0b' : '#ef4444';
        const proximoPasso = d.proximoPassoCompletude
            ? `<p style="font-size:13px;color:#64748b;margin-top:6px"><i class="fa-solid fa-lightbulb"></i> ${escHtml(d.proximoPassoCompletude)}</p>`
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

    // Checkboxes de idiomas
    const idiomasSelecionados = d.idiomasAceitos ?? [];
    const idiomasHtml = IDIOMAS_DISPONIVEIS.map(idioma => `
        <label class="form-check form-check--inline">
            <input type="checkbox" name="idioma" value="${idioma}" ${idiomasSelecionados.includes(idioma) ? 'checked' : ''}>
            ${idioma}
        </label>`).join('');

    root().innerHTML = `
    <div class="perfil-page">
        <div style="display:flex;align-items:center;gap:12px;margin-bottom:24px">
            <a href="perfil.html" style="font-size:14px;color:#64748b;text-decoration:none"><i class="fa-solid fa-arrow-left"></i> Voltar ao perfil</a>
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

        <!-- INFORMAÇÕES BÁSICAS -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Informações Básicas</h2>
            <form id="form-perfil" novalidate>
                <div class="form-group">
                    <label>Sobre / Bio</label>
                    <textarea id="p-bio" maxlength="1000" rows="5"
                        placeholder="Apresentação livre. Fale sobre você ou sua empresa...">${escHtml(d.descricao ?? '')}</textarea>
                    <span id="bio-count" style="font-size:11px;color:#94a3b8">${(d.descricao ?? '').length}/1000</span>
                </div>
                <div class="form-group">
                    <label>Nome da empresa</label>
                    <input type="text" id="p-nome-empresa" value="${escHtml(d.nomeEmpresa ?? '')}"
                        placeholder="Razão social ou nome fantasia" maxlength="200">
                </div>
                <div class="form-group">
                    <label>Segmento de atuação</label>
                    <input type="text" id="p-segmento" value="${escHtml(d.segmento ?? '')}"
                        placeholder="Ex: Tecnologia, Saúde, Construção..." maxlength="100">
                </div>
                <div class="form-group">
                    <label>Tamanho da equipe</label>
                    <select id="p-tamanho-equipe">
                        <option value="">Não informado</option>
                        <option value="SoloPJ"         ${d.tamanhoEquipe === 'SoloPJ'         ? 'selected' : ''}>Solo / PJ</option>
                        <option value="MicroEmpresa"   ${d.tamanhoEquipe === 'MicroEmpresa'   ? 'selected' : ''}>Microempresa</option>
                        <option value="PequenoEmpresa" ${d.tamanhoEquipe === 'PequenoEmpresa' ? 'selected' : ''}>Pequena empresa</option>
                        <option value="MediaEmpresa"   ${d.tamanhoEquipe === 'MediaEmpresa'   ? 'selected' : ''}>Média empresa</option>
                    </select>
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
                    <label>E-mail de contato <span style="font-weight:normal;color:#9ca3af;font-size:12px">(público — exibido no perfil se preenchido)</span></label>
                    <input type="email" id="p-email-contato" value="${escHtml(d.emailContato ?? '')}"
                        placeholder="contato@empresa.com.br">
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

        <!-- DISPONIBILIDADE E CONTEXTO -->
        <div class="perfil-secao">
            <h2 class="perfil-secao__titulo">Disponibilidade e Contexto</h2>
            <form id="form-contexto" novalidate>
                <div class="form-group">
                    <label>Disponibilidade <span style="font-weight:normal;color:#9ca3af;font-size:12px">(exibida no perfil público)</span></label>
                    <div class="form-radio-group">
                        <label class="form-radio">
                            <input type="radio" name="disponibilidade" value="AceitandoPrestadores"
                                ${d.disponibilidade === 'AceitandoPrestadores' ? 'checked' : ''}> Aceitando prestadores
                        </label>
                        <label class="form-radio">
                            <input type="radio" name="disponibilidade" value="Pausado"
                                ${d.disponibilidade === 'Pausado' ? 'checked' : ''}> Pausado no momento
                        </label>
                        <label class="form-radio">
                            <input type="radio" name="disponibilidade" value=""
                                ${!d.disponibilidade ? 'checked' : ''}> Não informar
                        </label>
                    </div>
                </div>
                <div class="form-group">
                    <label>Idiomas aceitos nos projetos</label>
                    <div class="form-check-group">${idiomasHtml}</div>
                </div>
                <div class="form-group">
                    <label>Por que trabalhar comigo <span style="font-weight:normal;color:#9ca3af;font-size:12px">(máx. 500 caracteres)</span></label>
                    <textarea id="p-pq-trabalhar" maxlength="500" rows="4"
                        placeholder="Descreva seus diferenciais, como você trabalha com prestadores, o que valoriza...">${escHtml(d.porQueTrabalharComigo ?? '')}</textarea>
                    <span id="pq-count" style="font-size:11px;color:#94a3b8">${(d.porQueTrabalharComigo ?? '').length}/500</span>
                </div>
                <p id="contexto-erro" class="form-erro" hidden></p>
                <p id="contexto-sucesso" class="form-sucesso" hidden></p>
                <button type="submit" class="btn-salvar" id="btn-salvar-contexto">Salvar disponibilidade e contexto</button>
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

        <!-- ZONA DE PERIGO — EXCLUSÃO DE CONTA (LGPD) -->
        <div class="perfil-secao" style="border:1px solid #fecaca;background:#fef2f2">
            <h2 class="perfil-secao__titulo" style="color:#b91c1c">Excluir conta</h2>
            <p style="font-size:13px;color:#7f1d1d;line-height:1.6;margin-bottom:14px">
                Esta ação é <strong>permanente</strong>. Sua conta será desativada e seus dados pessoais
                (nome, e-mail, foto, CPF/CNPJ) serão anonimizados. Projetos, contratos, pagamentos e
                avaliações são mantidos por obrigação legal, exibindo "Usuário indisponível" no seu lugar.
                Você não conseguirá mais acessar esta conta.
            </p>
            <p id="excluir-conta-err" class="form-erro" hidden></p>
            <button type="button" class="btn-salvar" id="btn-excluir-conta"
                    style="background:#dc2626;border-color:#dc2626">
                <i class="fa-solid fa-trash-can" aria-hidden="true"></i> Excluir minha conta
            </button>
        </div>
    </div>
    `;

    // Contador bio
    const bio = document.getElementById('p-bio');
    const bioCount = document.getElementById('bio-count');
    bio.addEventListener('input', () => { bioCount.textContent = `${bio.value.length}/1000`; });

    // Contador pq-trabalhar
    const pqTextarea = document.getElementById('p-pq-trabalhar');
    const pqCount = document.getElementById('pq-count');
    pqTextarea.addEventListener('input', () => { pqCount.textContent = `${pqTextarea.value.length}/500`; });

    // Máscara telefone
    const inputTel = document.getElementById('p-telefone');
    if (inputTel) {
        inputTel.addEventListener('input', () => {
            const v = inputTel.value.replace(/\D/g, '').slice(0, 11);
            inputTel.value = v.length <= 10
                ? v.replace(/(\d{2})(\d{4})(\d{0,4})/, '($1) $2-$3').trim().replace(/-$/, '')
                : v.replace(/(\d{2})(\d{5})(\d{0,4})/, '($1) $2-$3').trim().replace(/-$/, '');
        });
    }

    // Upload foto
    document.getElementById('btn-foto').addEventListener('click', salvarFoto);

    const btnRemover = document.getElementById('btn-remover-foto');
    if (btnRemover) btnRemover.addEventListener('click', removerFoto);

    // Salvar perfil básico
    document.getElementById('form-perfil').addEventListener('submit', salvarPerfil);

    // Salvar disponibilidade/contexto
    document.getElementById('form-contexto').addEventListener('submit', salvarContexto);

    document.getElementById('btn-privacidade').addEventListener('click', salvarPrivacidade);

    document.getElementById('btn-excluir-conta').addEventListener('click', excluirConta);
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
    const emailContato = document.getElementById('p-email-contato').value.trim() || null;
    const telefone = document.getElementById('p-telefone').value.trim() || null;
    const exibirIdade = document.getElementById('p-exibir-idade')?.checked ?? false;
    const nomeEmpresa = document.getElementById('p-nome-empresa').value.trim() || null;
    const segmento = document.getElementById('p-segmento').value.trim() || null;
    const tamanhoEquipe = document.getElementById('p-tamanho-equipe').value || null;

    btn.disabled = true;
    btn.textContent = 'Salvando...';

    try {
        await api.put('/contratantes/me/perfil', {
            descricao, siteUrl, linkedinUrl, emailContato, telefone, exibirIdade,
            nomeEmpresa, segmento, tamanhoEquipe
        });
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

async function salvarContexto(e) {
    e.preventDefault();

    const btn = document.getElementById('btn-salvar-contexto');
    const erro = document.getElementById('contexto-erro');
    const sucesso = document.getElementById('contexto-sucesso');
    erro.hidden = true; sucesso.hidden = true;

    const disponibilidadeRadio = document.querySelector('input[name="disponibilidade"]:checked');
    const disponibilidade = disponibilidadeRadio?.value || null;

    const idiomasAceitos = [...document.querySelectorAll('input[name="idioma"]:checked')]
        .map(el => el.value);

    const porQueTrabalharComigo = document.getElementById('p-pq-trabalhar').value.trim() || null;

    btn.disabled = true;
    btn.textContent = 'Salvando...';

    try {
        await api.put('/contratantes/me/perfil', {
            disponibilidade,
            idiomasAceitos,
            porQueTrabalharComigo
        });
        sucesso.textContent = 'Disponibilidade e contexto salvos!';
        sucesso.hidden = false;
    } catch (err) {
        erro.textContent = err?.data?.mensagem ?? 'Erro ao salvar.';
        erro.hidden = false;
    } finally {
        btn.disabled = false;
        btn.textContent = 'Salvar disponibilidade e contexto';
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
