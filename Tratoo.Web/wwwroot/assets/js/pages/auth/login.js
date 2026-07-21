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

    container.innerHTML = `
        <h2 class="login__title">Entrar na Tratoo</h2>
        <p class="login__subtitle">Acesse sua conta para continuar</p>
        <form id="loginForm" class="login__form" novalidate>
            <div class="login__group">
                <label for="email">E-mail</label>
                <input
                    type="email"
                    id="email"
                    name="email"
                    autocomplete="username"
                    placeholder="seu@email.com"
                    required
                >
            </div>
            <div class="login__group">
                <label for="senha">Senha</label>
                <input
                    type="password"
                    id="senha"
                    name="senha"
                    autocomplete="current-password"
                    placeholder="••••••••"
                    required
                >
            </div>
            <p id="login-erro" class="login__erro" hidden></p>
            <a href="#" id="esqueceuSenha" class="login__esqueceu">Esqueci minha senha</a>
            <button type="submit" class="login__button">Entrar</button>
        </form>
        <div class="login__rodape">
            <p>Não tem conta? <a href="/pages/auth/start.html">Cadastre-se</a></p>
        </div>
    `;

    document.getElementById('email')?.focus();
}

function renderizarFormMFA(email) {
    emailMFA = email;
    const container = document.getElementById('login');
    if (!container) return;

    container.innerHTML = `
        <h2 class="login__title">Verificação em dois fatores</h2>
        <div class="login__mfa-info">
            <p>Enviamos um código de 6 dígitos para</p>
            <strong>${escapeHtml(email)}</strong>
            <p>Verifique sua caixa de entrada e spam.</p>
        </div>
        <form id="mfaForm" class="login__form" novalidate>
            <div class="login__group">
                <label for="codigoMFA">Código de verificação</label>
                <input
                    type="text"
                    id="codigoMFA"
                    name="codigoMFA"
                    class="login__codigo-input"
                    maxlength="6"
                    inputmode="numeric"
                    autocomplete="one-time-code"
                    placeholder="000000"
                    required
                >
            </div>
            <p id="mfa-erro" class="login__erro" hidden></p>
            <button type="submit" class="login__button">Verificar código</button>
        </form>
        <div class="login__rodape">
            <p><a href="#" id="voltarLogin"><i class="fa-solid fa-arrow-left"></i> Voltar ao login</a></p>
        </div>
    `;

    document.getElementById('codigoMFA')?.focus();
}

function renderizarFormEsqueceuSenha() {
    const container = document.getElementById('login');
    if (!container) return;

    container.innerHTML = `
        <h2 class="login__title">Redefinir senha</h2>
        <p class="login__subtitle">Digite seu e-mail e enviaremos um código de redefinição.</p>
        <form id="esqueciSenhaForm" class="login__form" novalidate>
            <div class="login__group">
                <label for="emailReset">E-mail da conta</label>
                <input
                    type="email"
                    id="emailReset"
                    name="emailReset"
                    autocomplete="username"
                    placeholder="seu@email.com"
                    required
                >
            </div>
            <p id="esqueceu-erro" class="login__erro" hidden></p>
            <button type="submit" class="login__button">Enviar código</button>
        </form>
        <div class="login__rodape">
            <p><a href="#" id="voltarLogin"><i class="fa-solid fa-arrow-left"></i> Voltar ao login</a></p>
        </div>
    `;

    document.getElementById('emailReset')?.focus();
}

function renderizarFormResetarSenha(email) {
    emailReset = email;
    const container = document.getElementById('login');
    if (!container) return;

    container.innerHTML = `
        <h2 class="login__title">Nova senha</h2>
        <div class="login__mfa-info">
            <p>Se o endereço abaixo estiver cadastrado, você receberá um código em breve.</p>
            <strong>${escapeHtml(email)}</strong>
            <p>Verifique sua caixa de entrada e spam.</p>
        </div>
        <form id="resetarSenhaForm" class="login__form" novalidate>
            <div class="login__group">
                <label for="codigoReset">Código recebido</label>
                <input
                    type="text"
                    id="codigoReset"
                    name="codigoReset"
                    class="login__codigo-input"
                    maxlength="6"
                    inputmode="numeric"
                    autocomplete="one-time-code"
                    placeholder="000000"
                    required
                >
            </div>
            <div class="login__group">
                <label for="novaSenha">Nova senha</label>
                <input
                    type="password"
                    id="novaSenha"
                    name="novaSenha"
                    autocomplete="new-password"
                    placeholder="Mín. 8 chars, maiúscula, número, especial"
                    required
                >
            </div>
            <div class="login__group">
                <label for="confirmarSenha">Confirmar nova senha</label>
                <input
                    type="password"
                    id="confirmarSenha"
                    name="confirmarSenha"
                    autocomplete="new-password"
                    placeholder="Repita a nova senha"
                    required
                >
            </div>
            <p id="resetar-erro" class="login__erro" hidden></p>
            <button type="submit" class="login__button">Redefinir senha</button>
        </form>
        <div class="login__rodape">
            <p><a href="#" id="voltarEsqueceu"><i class="fa-solid fa-arrow-left"></i> Tentar outro e-mail</a></p>
        </div>
    `;

    document.getElementById('codigoReset')?.focus();
}

function renderizarSucessoReset() {
    emailReset = null;
    const container = document.getElementById('login');
    if (!container) return;

    container.innerHTML = `
        <div class="login__sucesso">
            <span class="login__sucesso-icone">&#10003;</span>
            <h2 class="login__title">Senha redefinida!</h2>
            <p class="login__subtitle">Sua nova senha foi salva com sucesso. Faça login para continuar.</p>
            <a href="#" id="irParaLogin" class="login__button" style="text-decoration:none;text-align:center;display:block">Fazer login</a>
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

document.addEventListener('click', function (e) {
    if (e.target.id === 'voltarLogin') {
        e.preventDefault();
        limparDadosSessao();
        renderizarFormLogin();
    }

    if (e.target.id === 'voltarEsqueceu') {
        e.preventDefault();
        emailReset = null;
        renderizarFormEsqueceuSenha();
    }

    if (e.target.id === 'esqueceuSenha') {
        e.preventDefault();
        renderizarFormEsqueceuSenha();
    }

    if (e.target.id === 'irParaLogin') {
        e.preventDefault();
        limparDadosSessao();
        renderizarFormLogin();
    }
});

// ── Inicialização ─────────────────────────────────────────────────────────────
onReady(() => {
    limparDadosSessao();
    renderizarFormLogin();
});
