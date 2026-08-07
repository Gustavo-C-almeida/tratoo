import { api } from '/assets/js/services/api.js';
import { onReady } from '/assets/js/core/app.js';
import { escapeHtml, setButtonLoading, showError, getErrorMessage } from '/assets/js/utils/form.js';

// e-mail guardado em memória para os fluxos de MFA e reset de senha
let emailMFA = null;
let dadosLoginPendente = null; // Guarda dados temporários do login
let emailReset = null;         // E-mail usado no fluxo de redefinição de senha

// ── Renderizações ─────────────────────────────────────────────────────────────

function renderizarFormLogin() {
    const container = document.getElementById('login');
    if (!container) return;

    // NOTA: o <p> de erro NÃO pode receber classe de display (d-flex, d-block):
    // utils/form.js alterna a visibilidade por `el.hidden`, que seria anulado.
    // NOTA: o botão de submit não pode conter ícone — setButtonLoading troca o
    // textContent e o ícone seria perdido ao restaurar.
    container.innerHTML = `
        <div class="text-center mb-4">
            <h1 class="page-title h3">Entrar na Tratoo</h1>
            <p class="page-subtitle">Acesse sua conta para continuar</p>
        </div>

        <form id="loginForm" novalidate>
            <div class="mb-3">
                <label class="form-label" for="email">E-mail</label>
                <div class="input-group">
                    <span class="input-group-text"><i class="fa-regular fa-envelope" aria-hidden="true"></i></span>
                    <input type="email" class="form-control" id="email" name="email"
                           autocomplete="username" placeholder="seu@email.com" required>
                </div>
            </div>

            <div class="mb-2">
                <label class="form-label" for="senha">Senha</label>
                <div class="input-group">
                    <span class="input-group-text"><i class="fa-solid fa-lock" aria-hidden="true"></i></span>
                    <input type="password" class="form-control" id="senha" name="senha"
                           autocomplete="current-password" placeholder="••••••••" required>
                </div>
            </div>

            <div class="text-end mb-3">
                <a href="#" id="esqueceuSenha" class="small fw-semibold text-decoration-none">Esqueci minha senha</a>
            </div>

            <p id="login-erro" class="alert alert-danger py-2 px-3 small" role="alert" hidden></p>

            <button type="submit" class="btn btn-primary btn-lg w-100">Entrar</button>
        </form>

        <hr class="my-4">

        <p class="text-center small text-secondary mb-0">
            Não tem conta?
            <a href="/pages/auth/start.html" class="fw-semibold text-decoration-none">Cadastre-se</a>
        </p>
    `;

    document.getElementById('email')?.focus();
}

function renderizarFormMFA(email) {
    emailMFA = email;
    const container = document.getElementById('login');
    if (!container) return;

    container.innerHTML = `
        <div class="text-center mb-4">
            <span class="auth-icon" aria-hidden="true"><i class="fa-solid fa-shield-halved"></i></span>
            <h1 class="page-title h3">Verificação em dois fatores</h1>
            <p class="page-subtitle">Enviamos um código de 6 dígitos para</p>
            <p class="fw-semibold text-body mb-0">${escapeHtml(email)}</p>
        </div>

        <div class="alert alert-info py-2 px-3 small text-center" role="status">
            Verifique sua caixa de entrada e a pasta de spam.
        </div>

        <form id="mfaForm" novalidate>
            <div class="mb-3">
                <label class="form-label" for="codigoMFA">Código de verificação</label>
                <input type="text" class="form-control form-control-lg auth-otp"
                       id="codigoMFA" name="codigoMFA" maxlength="6" inputmode="numeric"
                       autocomplete="one-time-code" placeholder="000000" required>
            </div>

            <p id="mfa-erro" class="alert alert-danger py-2 px-3 small" role="alert" hidden></p>

            <button type="submit" class="btn btn-primary btn-lg w-100">Verificar código</button>
        </form>

        <hr class="my-4">

        <p class="text-center small mb-0">
            <a href="#" id="voltarLogin" class="fw-semibold text-decoration-none">
                <i class="fa-solid fa-arrow-left me-1" aria-hidden="true"></i>Voltar ao login
            </a>
        </p>
    `;

    document.getElementById('codigoMFA')?.focus();
}

function renderizarFormEsqueceuSenha() {
    const container = document.getElementById('login');
    if (!container) return;

    container.innerHTML = `
        <div class="text-center mb-4">
            <span class="auth-icon" aria-hidden="true"><i class="fa-solid fa-key"></i></span>
            <h1 class="page-title h3">Redefinir senha</h1>
            <p class="page-subtitle">Digite seu e-mail e enviaremos um código de redefinição.</p>
        </div>

        <form id="esqueciSenhaForm" novalidate>
            <div class="mb-3">
                <label class="form-label" for="emailReset">E-mail da conta</label>
                <div class="input-group">
                    <span class="input-group-text"><i class="fa-regular fa-envelope" aria-hidden="true"></i></span>
                    <input type="email" class="form-control" id="emailReset" name="emailReset"
                           autocomplete="username" placeholder="seu@email.com" required>
                </div>
            </div>

            <p id="esqueceu-erro" class="alert alert-danger py-2 px-3 small" role="alert" hidden></p>

            <button type="submit" class="btn btn-primary btn-lg w-100">Enviar código</button>
        </form>

        <hr class="my-4">

        <p class="text-center small mb-0">
            <a href="#" id="voltarLogin" class="fw-semibold text-decoration-none">
                <i class="fa-solid fa-arrow-left me-1" aria-hidden="true"></i>Voltar ao login
            </a>
        </p>
    `;

    document.getElementById('emailReset')?.focus();
}

function renderizarFormResetarSenha(email) {
    emailReset = email;
    const container = document.getElementById('login');
    if (!container) return;

    container.innerHTML = `
        <div class="text-center mb-4">
            <span class="auth-icon" aria-hidden="true"><i class="fa-solid fa-lock-open"></i></span>
            <h1 class="page-title h3">Nova senha</h1>
            <p class="page-subtitle">Se o endereço abaixo estiver cadastrado, você receberá um código em breve.</p>
            <p class="fw-semibold text-body mb-0">${escapeHtml(email)}</p>
        </div>

        <form id="resetarSenhaForm" novalidate>
            <div class="mb-3">
                <label class="form-label" for="codigoReset">Código recebido</label>
                <input type="text" class="form-control form-control-lg auth-otp"
                       id="codigoReset" name="codigoReset" maxlength="6" inputmode="numeric"
                       autocomplete="one-time-code" placeholder="000000" required>
            </div>

            <div class="mb-3">
                <label class="form-label" for="novaSenha">Nova senha</label>
                <input type="password" class="form-control" id="novaSenha" name="novaSenha"
                       autocomplete="new-password" placeholder="Mínimo 8 caracteres, com 1 número" required>
                <div class="form-text">Use ao menos 8 caracteres, incluindo um número.</div>
            </div>

            <div class="mb-3">
                <label class="form-label" for="confirmarSenha">Confirmar nova senha</label>
                <input type="password" class="form-control" id="confirmarSenha" name="confirmarSenha"
                       autocomplete="new-password" placeholder="Repita a nova senha" required>
            </div>

            <p id="resetar-erro" class="alert alert-danger py-2 px-3 small" role="alert" hidden></p>

            <button type="submit" class="btn btn-primary btn-lg w-100">Redefinir senha</button>
        </form>

        <hr class="my-4">

        <p class="text-center small mb-0">
            <a href="#" id="voltarEsqueceu" class="fw-semibold text-decoration-none">
                <i class="fa-solid fa-arrow-left me-1" aria-hidden="true"></i>Tentar outro e-mail
            </a>
        </p>
    `;

    document.getElementById('codigoReset')?.focus();
}

function renderizarSucessoReset() {
    emailReset = null;
    const container = document.getElementById('login');
    if (!container) return;

    container.innerHTML = `
        <div class="text-center">
            <span class="auth-icon auth-icon--success" aria-hidden="true">
                <i class="fa-solid fa-check"></i>
            </span>
            <h1 class="page-title h3">Senha redefinida!</h1>
            <p class="page-subtitle mb-4">Sua nova senha foi salva com sucesso. Faça login para continuar.</p>
            <a href="#" id="irParaLogin" class="btn btn-primary btn-lg w-100">Fazer login</a>
        </div>
    `;
}

// ── Função de redirecionamento ───────────────────────────────────────────────

async function redirecionarAposLogin(perfilCompleto, tipo) {
    // Se os dados não foram fornecidos, busca do backend
    let perfilStatus = perfilCompleto;
    let tipoUsuario = tipo;

    if (perfilStatus === undefined || perfilStatus === null || !tipoUsuario) {
        try {
            const response = await api.get('/api/me');
            perfilStatus = perfilStatus ?? response.perfilCompleto;
            tipoUsuario = tipoUsuario || response.tipo;
        } catch (err) {
            console.error('Erro ao verificar status do perfil:', err);
            perfilStatus = true;
            tipoUsuario = tipoUsuario || 'Prestador';
        }
    }

    if (perfilStatus === false) {
        window.location.href = '/pages/auth/onboarding.html';
    } else if (tipoUsuario === 'Contratante') {
        window.location.href = '/pages/contratante/meus-projetos.html';
    } else {
        window.location.href = '/pages/projetos/index.html';
    }
}

// ── Erros ─────────────────────────────────────────────────────────────────────
// Usam o helper compartilhado utils/form.js (showError): login/MFA rolam até o
// erro e auto-ocultam após 5s; as telas de reset apenas exibem inline.

function mostrarErroLogin(msg)    { showError('login-erro', msg, { autoHideMs: 5000 }); }
function mostrarErroMFA(msg)      { showError('mfa-erro', msg, { autoHideMs: 5000 }); }
function mostrarErroEsqueceu(msg) { showError('esqueceu-erro', msg, { scroll: false }); }
function mostrarErroResetar(msg)  { showError('resetar-erro', msg, { scroll: false }); }

// ── Handlers ──────────────────────────────────────────────────────────────────

document.addEventListener('submit', async function (e) {
    e.preventDefault();
    const form = e.target;

    // Etapa 1: credenciais (login)
    if (form.id === 'loginForm') {
        const email = document.getElementById('email').value.trim();
        const senha = document.getElementById('senha').value;

        // Validações
        const emailRegex = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
        if (!emailRegex.test(email)) {
            mostrarErroLogin('Digite um e-mail válido.');
            return;
        }
        if (!senha) {
            mostrarErroLogin('Digite sua senha.');
            return;
        }

        const btn = form.querySelector('button[type="submit"]');
        const restaurarBtn = setButtonLoading(btn, 'Aguarde...');

        try {
            const response = await api.post('/usuarios/login', { email, senha });

            // CASO 1: Usuário tem MFA habilitado
            if (response.requerMFA) {
                // Guarda apenas o email para usar na validação do MFA
                dadosLoginPendente = {
                    email: response.email ?? email
                    // NÃO guarda perfilCompleto aqui porque o login ainda não foi concluído
                };
                renderizarFormMFA(dadosLoginPendente.email);
                return;
            }

            // CASO 2: Usuário NÃO tem MFA - login concluído, agora sim verifica perfil
            await redirecionarAposLogin(response.perfilCompleto, response.tipo);

        } catch (err) {
            mostrarErroLogin(getErrorMessage(err, 'E-mail ou senha incorretos.'));
            restaurarBtn();
        }
    }

    // Etapa 2: MFA (verificação de dois fatores) - SÓ AQUI QUE O LOGIN É CONCLUÍDO
    if (form.id === 'mfaForm') {
        const codigo = document.getElementById('codigoMFA').value.trim();

        // Validação do código
        if (!codigo) {
            mostrarErroMFA('Digite o código de verificação.');
            return;
        }

        if (!/^\d{6}$/.test(codigo)) {
            mostrarErroMFA('Código inválido. Digite os 6 dígitos recebidos por e-mail.');
            return;
        }

        const btn = form.querySelector('button[type="submit"]');
        const restaurarBtn = setButtonLoading(btn, 'Verificando...');

        try {
            // Verifica se temos email para validar
            if (!emailMFA && !dadosLoginPendente?.email) {
                throw new Error('Sessão expirada. Faça login novamente.');
            }

            const emailParaValidar = emailMFA || dadosLoginPendente.email;

            // Valida o código MFA - SÓ AQUI O LOGIN É EFETIVAMENTE CONCLUÍDO
            const response = await api.post('/usuarios/login/mfa', {
                email: emailParaValidar,
                codigo
            });

            // Agora sim, após o MFA ser confirmado, verifica o perfil e redireciona
            await redirecionarAposLogin(response.perfilCompleto, response.tipo);

        } catch (err) {
            const msg = getErrorMessage(err, 'Código inválido ou expirado. Tente novamente.');
            mostrarErroMFA(msg);
            restaurarBtn();

            // Se for erro de sessão expirada, limpa os dados e volta ao login
            if (msg.includes('Sessão expirada') || err?.status === 401) {
                setTimeout(() => {
                    limparDadosSessao();
                    renderizarFormLogin();
                }, 2000);
            }
        }
    }

    // Etapa Esqueci: solicitar código de redefinição
    if (form.id === 'esqueciSenhaForm') {
        const email = document.getElementById('emailReset')?.value.trim() ?? '';

        if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) {
            mostrarErroEsqueceu('Digite um e-mail válido.');
            return;
        }

        const btn = form.querySelector('button[type="submit"]');
        const restaurarBtn = setButtonLoading(btn, 'Enviando...');

        try {
            await api.post('/usuarios/senha/resetar/solicitar', { email });
            // Sempre avança para a tela do código (a API nunca revela se o e-mail existe)
            renderizarFormResetarSenha(email);
        } catch (err) {
            mostrarErroEsqueceu(getErrorMessage(err, 'Não foi possível enviar o código. Tente novamente.'));
            restaurarBtn();
        }
    }

    // Etapa Resetar: confirmar código e definir nova senha
    if (form.id === 'resetarSenhaForm') {
        const codigo       = document.getElementById('codigoReset')?.value.trim() ?? '';
        const novaSenha    = document.getElementById('novaSenha')?.value ?? '';
        const confirmar    = document.getElementById('confirmarSenha')?.value ?? '';

        if (!/^\d{6}$/.test(codigo)) {
            mostrarErroResetar('O código deve ter exatamente 6 dígitos.');
            return;
        }
        if (!novaSenha) {
            mostrarErroResetar('Digite a nova senha.');
            return;
        }
        if (novaSenha !== confirmar) {
            mostrarErroResetar('As senhas não conferem.');
            return;
        }

        const btn = form.querySelector('button[type="submit"]');
        const restaurarBtn = setButtonLoading(btn, 'Redefinindo...');

        try {
            await api.post('/usuarios/senha/resetar', {
                email: emailReset,
                codigo,
                novaSenha,
                confirmarNovaSenha: confirmar
            });
            renderizarSucessoReset();
        } catch (err) {
            mostrarErroResetar(getErrorMessage(err, 'Código inválido ou expirado. Tente novamente.'));
            restaurarBtn();
        }
    }
}); // ── fim do handler de submit ──

// ── Voltar ao login ──────────────────────────────────────────────────────────

function limparDadosSessao() {
    emailMFA = null;
    dadosLoginPendente = null;
    emailReset = null;
}

// Usa closest() em vez de comparar e.target.id: os links contêm um <i> de ícone,
// e um clique exatamente sobre o ícone tem e.target === <i>, não o <a>. Com a
// comparação direta o clique era silenciosamente ignorado nessa região.
document.addEventListener('click', function (e) {
    const alvo = e.target.closest?.('#voltarLogin, #voltarEsqueceu, #esqueceuSenha, #irParaLogin');
    if (!alvo) return;

    e.preventDefault();

    switch (alvo.id) {
        case 'voltarLogin':
        case 'irParaLogin':
            limparDadosSessao();
            renderizarFormLogin();
            break;

        case 'voltarEsqueceu':
            emailReset = null;
            renderizarFormEsqueceuSenha();
            break;

        case 'esqueceuSenha':
            renderizarFormEsqueceuSenha();
            break;
    }
});

// ── Inicialização ─────────────────────────────────────────────────────────────
onReady(() => {
    limparDadosSessao();
    renderizarFormLogin();
});
