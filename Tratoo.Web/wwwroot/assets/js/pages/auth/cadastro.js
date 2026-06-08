let emailPendente = null;
let reenvioTimer = null;

function getTipo() {
    return document.getElementById('cadastro')?.dataset.tipo ?? '';
}

function mostrarErro(mensagem, tipo = 'cadastro') {
    let erroEl;

    if (tipo === 'cadastro') {
        erroEl = document.getElementById('cadastro-erro');
        if (!erroEl) {
            erroEl = document.createElement('p');
            erroEl.id = 'cadastro-erro';
            erroEl.className = 'cadastro__erro';
            const form = document.getElementById('cadastroForm');
            form?.appendChild(erroEl);
        }
    } else if (tipo === 'confirmar') {
        erroEl = document.getElementById('confirmar-erro');
        if (!erroEl) {
            erroEl = document.createElement('p');
            erroEl.id = 'confirmar-erro';
            erroEl.className = 'cadastro__erro';
            const form = document.getElementById('confirmarForm');
            form?.appendChild(erroEl);
        }
    }

    if (erroEl) {
        erroEl.innerHTML = mensagem;
        erroEl.hidden = false;

        // Scroll suave até o erro
        erroEl.scrollIntoView({ behavior: 'smooth', block: 'center' });

        // Remove erro após 5 segundos
        setTimeout(() => {
            if (erroEl && !erroEl.hidden) {
                erroEl.style.opacity = '0';
                setTimeout(() => {
                    erroEl.hidden = true;
                    erroEl.style.opacity = '1';
                }, 300);
            }
        }, 5000);
    }
}

function ocultarErro(tipo = 'cadastro') {
    const erroEl = document.getElementById(tipo === 'cadastro' ? 'cadastro-erro' : 'confirmar-erro');
    if (erroEl) erroEl.hidden = true;
}

function validarCamposCadastro() {
    const nome = document.getElementById('nome').value.trim();
    const email = document.getElementById('email').value.trim();
    const senha = document.getElementById('senha').value;
    const confirmarSenha = document.getElementById('confirmarSenha').value;
    const tipo = getTipo();
    const aceitouTermos = document.getElementById('aceitouTermos')?.checked ?? false;

    const erros = [];

    // Validação de nome
    if (!nome) {
        erros.push('<i class="fa-solid fa-circle-xmark"></i> Nome é obrigatório.');
    } else if (nome.length < 3) {
        erros.push('<i class="fa-solid fa-circle-xmark"></i> Nome inválido (mínimo 3 caracteres).');
    }

    // Validação de email
    const emailRegex = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
    if (!email) {
        erros.push('<i class="fa-solid fa-circle-xmark"></i> E-mail é obrigatório.');
    } else if (!emailRegex.test(email)) {
        erros.push('<i class="fa-solid fa-circle-xmark"></i> E-mail inválido. Exemplo: nome@email.com');
    }

    // Validação de senha
    if (!senha) {
        erros.push('<i class="fa-solid fa-circle-xmark"></i> Senha é obrigatória.');
    } else {
        const temNumero = /\d/.test(senha);
        const temMaiuscula = /[A-Z]/.test(senha);
        const temEspecial = /[^a-zA-Z0-9]/.test(senha);

        if (senha.length < 8) {
            erros.push('<i class="fa-solid fa-circle-xmark"></i> Senha deve ter no mínimo 8 caracteres.');
        }
        if (!temNumero) {
            erros.push('<i class="fa-solid fa-circle-xmark"></i> Senha deve conter pelo menos 1 número.');
        }
        if (!temMaiuscula) {
            erros.push('<i class="fa-solid fa-circle-xmark"></i> Senha deve conter pelo menos 1 letra maiúscula.');
        }
        if (!temEspecial) {
            erros.push('<i class="fa-solid fa-circle-xmark"></i> Senha deve conter pelo menos 1 caractere especial (@, #, $, etc).');
        }
    }

    // Confirmação de senha
    if (senha !== confirmarSenha) {
        erros.push('<i class="fa-solid fa-circle-xmark"></i> As senhas não conferem.');
    }

    // Tipo de usuário
    if (!tipo) {
        erros.push('<i class="fa-solid fa-circle-xmark"></i> Tipo de usuário não identificado.');
    }

    // Termos de uso
    if (!aceitouTermos) {
        erros.push('<i class="fa-solid fa-circle-xmark"></i> Você precisa aceitar os Termos de Uso e a Política de Privacidade.');
    }

    return erros;
}

// Renderizações
function mostrarFormConfirmacao(email) {
    emailPendente = email;
    const container = document.getElementById('cadastro');
    if (!container) return;

    container.innerHTML = `
<div class="cadastro">
    <h2 class="cadastro__title">Confirme seu e-mail</h2>

    <div class="cadastro__confirmacao-info cadastro__confirmacao-info--clean">
        <p>Enviamos um código de 6 dígitos para</p>
        <strong id="email-pendente-display">usuario@email.com</strong>
        <p>Verifique sua caixa de entrada e spam.</p>
    </div>

    <form id="confirmarForm" class="cadastro__form">
        <div class="cadastro__group">
            <label for="codigo"><i class="fa-solid fa-lock"></i> Código de verificação</label>
            <input type="text" id="codigo" name="codigo" class="cadastro__codigo-input" maxlength="6" inputmode="numeric" autocomplete="one-time-code" placeholder="000000">
        </div>

        <p id="confirmar-erro" class="cadastro__erro" hidden></p>

        <button type="submit" class="cadastro__button">Confirmar cadastro</button>
    </form>

    <div class="cadastro__reenvio">
        <p>Não recebeu o código?</p>
        <button id="reenviar-btn" class="cadastro__reenvio-btn" type="button">Reenviar código</button>
        <p id="reenviar-feedback" class="cadastro__reenvio-feedback" hidden></p>
    </div>
</div>
    `;

    // textContent em vez de innerHTML para evitar XSS com e-mails maliciosos
    document.getElementById('email-pendente-display').textContent = email;
    document.getElementById('codigo')?.focus();
    iniciarContadorReenvio();
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

function mostrarSucessoFinal(mensagem) {
    const container = document.getElementById('cadastro');
    if (!container) return;

    container.innerHTML = `
        <div class="cadastro__sucesso">
            <div class="cadastro__sucesso-icone"><i class="fa-solid fa-check"></i></div>
            <p id="sucesso-texto" class="cadastro__sucesso-texto"></p>
            <p class="cadastro__sucesso-sub">Você já pode fazer login.</p>
            <a href="/pages/auth/login.html" class="cadastro__button" style="display: inline-block; margin-top: 1rem; text-decoration: none;">Ir para login <i class="fa-solid fa-arrow-right"></i></a>
        </div>
    `;

    // textContent em vez de innerHTML para evitar XSS com conteúdo do servidor
    document.getElementById('sucesso-texto').textContent = mensagem;
}

// ── Handler de reenvio de código ──────────────────────────────────────────────
document.addEventListener('click', async function (e) {
    if (e.target.id !== 'reenviar-btn') return;

    const feedbackEl = document.getElementById('reenviar-feedback');
    const btn = e.target;

    btn.disabled = true;

    try {
        await api.post('/usuarios/cadastro/reenviar-codigo', { email: emailPendente });
        if (feedbackEl) {
            feedbackEl.textContent = 'Novo código enviado! Verifique sua caixa de entrada.';
            feedbackEl.className = 'cadastro__reenvio-feedback cadastro__reenvio-feedback--sucesso';
            feedbackEl.hidden = false;

            setTimeout(() => {
                feedbackEl.hidden = true;
            }, 5000);
        }
        iniciarContadorReenvio();
    } catch (err) {
        const msg = err?.data?.mensagem
            ?? err?.data?.message
            ?? 'Erro ao reenviar código. Tente novamente.';
        if (feedbackEl) {
            feedbackEl.textContent = `${msg}`;
            feedbackEl.className = 'cadastro__reenvio-feedback cadastro__reenvio-feedback--erro';
            feedbackEl.hidden = false;
        }
        btn.disabled = false;
    }
});

// ── Handlers de submit ────────────────────────────────────────────────────────
document.addEventListener('submit', async function (e) {
    const form = e.target;
    e.preventDefault();

    // Etapa 1: cadastro
    if (form.id === 'cadastroForm') {
        // Validar campos
        const erros = validarCamposCadastro();

        if (erros.length > 0) {
            mostrarErro(erros.join(' '), 'cadastro');
            return;
        }

        ocultarErro('cadastro');

        const nome = document.getElementById('nome').value.trim();
        const email = document.getElementById('email').value.trim();
        const senha = document.getElementById('senha').value;
        const confirmarSenha = document.getElementById('confirmarSenha').value;
        const tipo = getTipo();
        const aceitouTermos = document.getElementById('aceitouTermos')?.checked ?? false;
        const mfa = document.getElementById('mfaHabilitado')?.checked ?? false;

        const btn = form.querySelector('button[type="submit"]');
        btn.disabled = true;
        btn.textContent = 'Aguarde...';

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
            mostrarFormConfirmacao(email);
        } catch (err) {
            const msg = err?.data?.mensagem
                ?? err?.data?.message
                ?? 'Erro ao realizar cadastro. Tente novamente.';
            mostrarErro(`<i class="fa-solid fa-circle-xmark"></i> ${msg}`, 'cadastro');
            btn.disabled = false;
            btn.textContent = 'Cadastrar';
        }
    }

    // Etapa 2: confirmação do código
    if (form.id === 'confirmarForm') {
        const codigo = document.getElementById('codigo').value.trim();

        if (!codigo) {
            mostrarErro('<i class="fa-solid fa-circle-xmark"></i> Digite o código de verificação.', 'confirmar');
            return;
        }

        if (!/^\d{6}$/.test(codigo)) {
            mostrarErro('<i class="fa-solid fa-circle-xmark"></i> Código inválido. Digite os 6 dígitos recebidos por e-mail.', 'confirmar');
            return;
        }

        ocultarErro('confirmar');

        const btn = form.querySelector('button[type="submit"]');
        btn.disabled = true;
        btn.textContent = 'Verificando...';

        try {
            const data = await api.post('/usuarios/cadastro/confirmar', {
                email: emailPendente,
                codigo
            });
            mostrarSucessoFinal(data.mensagem ?? '<i class="fa-solid fa-check"></i> Cadastro confirmado com sucesso!');
        } catch (err) {
            const msg = err?.data?.mensagem
                ?? err?.data?.message
                ?? 'Código inválido ou expirado. Tente novamente.';
            mostrarErro(`<i class="fa-solid fa-circle-xmark"></i> ${msg}`, 'confirmar');
            btn.disabled = false;
            btn.textContent = 'Confirmar';
        }
    }
});

// Adicionar validação em tempo real (opcional)
document.addEventListener('DOMContentLoaded', () => {
    // Remover validação nativa do HTML5
    const forms = document.querySelectorAll('form');
    forms.forEach(form => {
        form.setAttribute('novalidate', true);
    });

    // Adicionar máscara para o campo de código (opcional)
    const codigoInput = document.getElementById('codigo');
    if (codigoInput) {
        codigoInput.addEventListener('input', function (e) {
            this.value = this.value.replace(/[^0-9]/g, '').slice(0, 6);
        });
    }
});