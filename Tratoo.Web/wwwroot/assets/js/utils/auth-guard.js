// Guard de acesso para páginas protegidas.
// Deve ser carregado ANTES do script específico da página.
//
// Fluxo:
//  1. Oculta o body para evitar flash de conteúdo protegido.
//  2. Chama GET /api/me (leitura dos claims do JWT no cookie httpOnly).
//  3. 401 -> redireciona para login.
//  4. perfilCompleto === false -> redireciona para onboarding.
//  5. Verifica se o perfil (role) do usuário tem permissão para a página atual.
//  6. OK -> expõe window.__tratooUser e exibe o body normalmente.
(async function guardOnboarding() {
    document.body.style.visibility = 'hidden';

    try {
        const resp = await fetch('/api/me', { credentials: 'same-origin' });

        if (resp.status === 401) {
            window.location.replace('/pages/auth/login.html');
            return;
        }

        const me = await resp.json().catch(() => ({}));

        if (me.perfilCompleto === false) {
            window.location.replace('/pages/auth/onboarding.html');
            return;
        }

        // Guarda tipo e role disponíveis para header.js e outros scripts
        window.__tratooUser = me;

        // Controle de acesso por perfil: páginas em /pages/contratante/ são
        // exclusivas para Contratante; páginas em /pages/prestador/ para Prestador.
        const path = window.location.pathname.toLowerCase();
        const tipo = (me.tipo || '').toLowerCase();

        if (path.startsWith('/pages/contratante/') && tipo !== 'contratante') {
            window.location.replace('/pages/projetos/index.html');
            return;
        }
        if (path.startsWith('/pages/prestador/') && tipo !== 'prestador') {
            window.location.replace('/pages/contratante/meus-projetos.html');
            return;
        }

        // Área administrativa: exclusiva para usuários com role Admin (seed/banco).
        if (path.startsWith('/pages/admin/') && me.isAdmin !== true) {
            window.location.replace('/pages/projetos/index.html');
            return;
        }

        // Autenticado, onboarding completo e role válida — exibe a página.
        document.body.style.visibility = '';
    } catch {
        // Falha de rede ou outro erro inesperado -> login por segurança.
        window.location.replace('/pages/auth/login.html');
    }
})();
