import { api } from '/assets/js/services/api.js';
import { onReady } from '/assets/js/core/app.js';
import { isValidEmail, setButtonLoading, showError, hideError, getErrorMessage } from '/assets/js/utils/form.js';

// e-mail pendente de confirmação, guardado em memória entre as etapas
let emailPendente = null;
let reenvioTimer = null;

function getTipo() {
    return document.getElementById('cadastro')?.dataset.tipo ?? '';
}

function textosPorTipo(tipo) {
    return tipo === 'contratante'
        ? { titulo: 'Criar conta de contratante', subtitulo: 'Cadastre-se para contratar profissionais na Tratoo' }
        : { titulo: 'Criar conta de prestador', subtitulo: 'Cadastre-se para oferecer seus serviços na Tratoo' };
}

// ── Renderizações ─────────────────────────────────────────────────────────────

function renderizarFormCadastro() {
    const container = document.getElementById('cadastro');
    if (!container) return;

    const { titulo, subtitulo } = textosPorTipo(getTipo());

    // NOTA: o <p> de erro NÃO pode receber classe de display (d-flex, d-block):
    // utils/form.js alterna a visibilidade por `el.hidden`, que seria anulado.
    // NOTA: o botão de submit não pode conter ícone — setButtonLoading troca o
    // textContent e o ícone seria perdido ao restaurar.
    container.innerHTML = `
        <div class="text-center mb-4">
            <h1 class="page-title h3">${titulo}</h1>
            <p class="page-subtitle">${subtitulo}</p>
        </div>

        <form id="cadastroForm" novalidate>
            <div class="mb-3">
                <label class="form-label" for="nome">Nome completo</label>
                <div class="input-group">
                    <span class="input-group-text"><i class="fa-regular fa-user" aria-hidden="true"></i></span>
                    <input type="text" class="form-control" id="nome" name="nome"
                           autocomplete="name" placeholder="Seu nome completo" required>
                </div>
            </div>

            <div class="mb-3">
                <label class="form-label" for="email">E-mail</label>
                <div class="input-group">
                    <span class="input-group-text"><i class="fa-regular fa-envelope" aria-hidden="true"></i></span>
                    <input type="email" class="form-control" id="email" name="email"
                           autocomplete="username" placeholder="seu@email.com" required>
                </div>
            </div>

            <div class="mb-3">
                <label class="form-label" for="senha">Senha</label>
                <div class="input-group">
                    <span class="input-group-text"><i class="fa-solid fa-lock" aria-hidden="true"></i></span>
                    <input type="password" class="form-control" id="senha" name="senha"
                           autocomplete="new-password" placeholder="••••••••" required>
                </div>
                <div class="form-text">Mínimo 8 caracteres, com 1 número, 1 maiúscula e 1 caractere especial.</div>
            </div>

            <div class="mb-3">
                <label class="form-label" for="confirmarSenha">Confirmar senha</label>
                <div class="input-group">
                    <span class="input-group-text"><i class="fa-solid fa-lock" aria-hidden="true"></i></span>
                    <input type="password" class="form-control" id="confirmarSenha" name="confirmarSenha"
                           autocomplete="new-password" placeholder="Repita a senha" required>
                </div>
            </div>

            <div class="border rounded-3 p-3 mb-3">
                <div class="form-check">
                    <input class="form-check-input" type="checkbox" id="mfaHabilitado" name="mfaHabilitado">
                    <label class="form-check-label" for="mfaHabilitado">
                        <span class="fw-semibold d-block">Autenticação em dois fatores (MFA)</span>
                        <span class="small text-secondary">Ao fazer login, você receberá um código por e-mail para confirmar sua identidade.</span>
                    </label>
                </div>
            </div>

            <div class="form-check mb-3">
                <input class="form-check-input" type="checkbox" id="aceitouTermos" name="aceitouTermos" required>
                <label class="form-check-label small" for="aceitouTermos">
                    Li e aceito os
                    <a href="/pages/termos/termos-de-uso.html" target="_blank" class="fw-semibold text-decoration-none">Termos de Uso</a>
                    e a
                    <a href="/pages/termos/politica-de-privacidade.html" target="_blank" class="fw-semibold text-decoration-none">Política de Privacidade</a>
                    da Tratoo.
                </label>
            </div>

            <p id="cadastro-erro" class="alert alert-danger py-2 px-3 small" role="alert" hidden></p>

            <button type="submit" class="btn btn-primary btn-lg w-100">Cadastrar</button>
        </form>

        <hr class="my-4">

        <p class="text-center small text-secondary mb-0">
            Já tem conta?
            <a href="/pages/auth/login.html" class="fw-semibold text-decoration-none">Entrar</a>
        </p>
    `;

    document.getElementById('nome')?.focus();
}

function renderizarFormConfirmacao(email) {
    emailPendente = email;
    const container = document.getElementById('cadastro');
    if (!container) return;

    container.innerHTML = `
        <div class="text-center mb-4">
            <span class="auth-icon" aria-hidden="true"><i class="fa-solid fa-envelope-circle-check"></i></span>
            <h1 class="page-title h3">Confirme seu e-mail</h1>
            <p class="page-subtitle">Enviamos um código de 6 dígitos para</p>
            <p class="fw-semibold text-body mb-0" id="email-pendente-display"></p>
        </div>

        <div class="alert alert-info py-2 px-3 small text-center" role="status">
            Verifique sua caixa de entrada e a pasta de spam.
        </div>

        <form id="confirmarForm" novalidate>
            <div class="mb-3">
                <label class="form-label" for="codigo">Código de verificação</label>
                <input type="text" class="form-control form-control-lg auth-otp"
                       id="codigo" name="codigo" maxlength="6" inputmode="numeric"
                       autocomplete="one-time-code" placeholder="000000" required>
            </div>

            <p id="confirmar-erro" class="alert alert-danger py-2 px-3 small" role="alert" hidden></p>

            <button type="submit" class="btn btn-primary btn-lg w-100">Confirmar cadastro</button>
        </form>

        <div class="text-center mt-4">
            <p class="small text-secondary mb-2">Não recebeu o código?</p>
            <button id="reenviar-btn" class="btn btn-outline-secondary btn-sm" type="button">Reenviar código</button>
            <p id="reenviar-feedback" class="small mt-2 mb-0" hidden></p>
        </div>
    `;

    // textContent em vez de innerHTML para evitar XSS com e-mails maliciosos
    document.getElementById('email-pendente-display').textContent = email;
    document.getElementById('codigo')?.focus();
    iniciarContadorReenvio();
}

function renderizarSucessoFinal(mensagem) {
    const container = document.getElementById('cadastro');
    if (!container) return;

    container.innerHTML = `
        <div class="text-center">
            <span class="auth-icon auth-icon--success" aria-hidden="true">
                <i class="fa-solid fa-check"></i>
            </span>
            <h1 class="page-title h3" id="sucesso-texto"></h1>
            <p class="page-subtitle mb-4">Você já pode fazer login.</p>
            <a href="/pages/auth/login.html" class="btn btn-primary btn-lg w-100">Ir para login</a>
        </div>
    `;

    // textContent em vez de innerHTML para evitar XSS com conteúdo do servidor
    document.getElementById('sucesso-texto').textContent = mensagem;
}

function iniciarContadorReenvio() {
    if (reenvioTimer) clearInterval(reenvioTimer);

    const btn = document.getElementById('reenviar-btn');
    if (!btn) return;

    let segundos = 60;
    btn.disabled = true;
    btn.innerHTML = `<i class="fa-solid fa-clock"></i> Reenviar em ${segundos}s`;

    reenvioTimer = setInterval(() => {
        segundos--;
        if (segundos <= 0) {
            clearInterval(reenvioTimer);
            reenvioTimer = null;
            btn.disabled = false;
            btn.textContent = 'Reenviar código';
        } else {
            btn.innerHTML = `<i class="fa-solid fa-clock"></i> Reenviar em ${segundos}s`;
        }
    }, 1000);
}

// ── Erros ─────────────────────────────────────────────────────────────────────
// Usam o helper compartilhado utils/form.js (showError), mesmo padrão do login.js.
// A tela de cadastro pode listar mais de um problema por vez, por isso o
// conteúdo é HTML com quebras de linha em vez de texto simples.

function mostrarErroCadastro(msgs)  { showError('cadastro-erro', [].concat(msgs).join('<br>'), { html: true, autoHideMs: 6000 }); }
function mostrarErroConfirmar(msg)  { showError('confirmar-erro', msg, { scroll: false }); }
function ocultarErroCadastro()      { hideError('cadastro-erro'); }
function ocultarErroConfirmar()     { hideError('confirmar-erro'); }

function validarCamposCadastro() {
    const nome = document.getElementById('nome').value.trim();
    const email = document.getElementById('email').value.trim();
    const senha = document.getElementById('senha').value;
    const confirmarSenha = document.getElementById('confirmarSenha').value;
    const tipo = getTipo();
    const aceitouTermos = document.getElementById('aceitouTermos')?.checked ?? false;

    const erros = [];

    if (!nome) {
        erros.push('Nome é obrigatório.');
    } else if (nome.length < 3) {
        erros.push('Nome inválido (mínimo 3 caracteres).');
    }

    if (!email) {
        erros.push('E-mail é obrigatório.');
    } else if (!isValidEmail(email)) {
        erros.push('E-mail inválido. Exemplo: nome@email.com');
    }

    if (!senha) {
        erros.push('Senha é obrigatória.');
    } else {
        if (senha.length < 8) erros.push('Senha deve ter no mínimo 8 caracteres.');
        if (!/\d/.test(senha)) erros.push('Senha deve conter pelo menos 1 número.');
        if (!/[A-Z]/.test(senha)) erros.push('Senha deve conter pelo menos 1 letra maiúscula.');
        if (!/[^a-zA-Z0-9]/.test(senha)) erros.push('Senha deve conter pelo menos 1 caractere especial (@, #, $, etc).');
    }

    if (senha !== confirmarSenha) {
        erros.push('As senhas não conferem.');
    }

    if (!tipo) {
        erros.push('Tipo de usuário não identificado.');
    }

    if (!aceitouTermos) {
        erros.push('Você precisa aceitar os Termos de Uso e a Política de Privacidade.');
    }

    return erros;
}

// ── Handlers ──────────────────────────────────────────────────────────────────

document.addEventListener('submit', async function (e) {
    const form = e.target;
    e.preventDefault();

    // Etapa 1: cadastro
    if (form.id === 'cadastroForm') {
        const erros = validarCamposCadastro();
        if (erros.length > 0) {
            mostrarErroCadastro(erros);
            return;
        }

        ocultarErroCadastro();

        const nome = document.getElementById('nome').value.trim();
        const email = document.getElementById('email').value.trim();
        const senha = document.getElementById('senha').value;
        const confirmarSenha = document.getElementById('confirmarSenha').value;
        const tipo = getTipo();
        const aceitouTermos = document.getElementById('aceitouTermos')?.checked ?? false;
        const mfa = document.getElementById('mfaHabilitado')?.checked ?? false;

        const btn = form.querySelector('button[type="submit"]');
        const restaurarBtn = setButtonLoading(btn, 'Aguarde...');

        try {
            await api.post('/usuarios/cadastro', {
                nome,
                email,
                senha,
                confirmarSenha,
                tipo,
                aceitouTermos,
                mfa
            });
            renderizarFormConfirmacao(email);
        } catch (err) {
            mostrarErroCadastro(getErrorMessage(err, 'Erro ao realizar cadastro. Tente novamente.'));
            restaurarBtn();
        }
    }

    // Etapa 2: confirmação do código
    if (form.id === 'confirmarForm') {
        const codigo = document.getElementById('codigo').value.trim();

        if (!codigo) {
            mostrarErroConfirmar('Digite o código de verificação.');
            return;
        }

        if (!/^\d{6}$/.test(codigo)) {
            mostrarErroConfirmar('Código inválido. Digite os 6 dígitos recebidos por e-mail.');
            return;
        }

        ocultarErroConfirmar();

        const btn = form.querySelector('button[type="submit"]');
        const restaurarBtn = setButtonLoading(btn, 'Verificando...');

        try {
            const data = await api.post('/usuarios/cadastro/confirmar', {
                email: emailPendente,
                codigo
            });
            renderizarSucessoFinal(data.mensagem ?? 'Cadastro confirmado com sucesso!');
        } catch (err) {
            mostrarErroConfirmar(getErrorMessage(err, 'Código inválido ou expirado. Tente novamente.'));
            restaurarBtn();
        }
    }
});

// ── Reenvio de código ────────────────────────────────────────────────────────

document.addEventListener('click', async function (e) {
    if (e.target.id !== 'reenviar-btn') return;

    const feedbackEl = document.getElementById('reenviar-feedback');
    const btn = e.target;

    btn.disabled = true;

    try {
        await api.post('/usuarios/cadastro/reenviar-codigo', { email: emailPendente });
        if (feedbackEl) {
            feedbackEl.textContent = 'Novo código enviado! Verifique sua caixa de entrada.';
            feedbackEl.className = 'small mt-2 mb-0 text-success';
            feedbackEl.hidden = false;

            setTimeout(() => { feedbackEl.hidden = true; }, 5000);
        }
        iniciarContadorReenvio();
    } catch (err) {
        const msg = getErrorMessage(err, 'Erro ao reenviar código. Tente novamente.');
        if (feedbackEl) {
            feedbackEl.textContent = msg;
            feedbackEl.className = 'small mt-2 mb-0 text-danger';
            feedbackEl.hidden = false;
        }
        btn.disabled = false;
    }
});

// Máscara do campo de código (delegada: o input só existe a partir da etapa 2)
document.addEventListener('input', function (e) {
    if (e.target?.id !== 'codigo') return;
    e.target.value = e.target.value.replace(/[^0-9]/g, '').slice(0, 6);
});

// ── Inicialização ─────────────────────────────────────────────────────────────
onReady(() => {
    renderizarFormCadastro();
});
