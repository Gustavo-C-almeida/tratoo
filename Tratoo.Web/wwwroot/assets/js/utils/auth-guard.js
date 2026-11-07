// Guard de acesso para páginas protegidas.
// Deve ser carregado ANTES do script específico da página.
//
// Fluxo:
//  1. Oculta o body para evitar flash de conteúdo protegido.
//  2. Chama GET /api/me (leitura dos claims do JWT no cookie httpOnly).
//  3. 401 -> tenta renovar a sessão (refresh em single-flight entre abas, em
//     services/refresh.js) e repete /api/me uma vez. Sem sessão -> login, com
//     ?motivo=expirada&retorno=<página atual> quando ela expirou.
//     Servidor indisponível (503/rede no refresh) NÃO desloga: avisa e recarrega.
//  4. perfilCompleto === false -> redireciona para onboarding.
//  5. Verifica se o perfil (role) do usuário tem permissão para a página atual.
//  6. OK -> expõe window.__tratooUser, exibe o body e carrega utils/sessao.js
//     (aviso de expiração, atividade, sincronia entre abas).
//
// Continua um script clássico (bloqueante); os módulos entram por import().
(async function guardOnboarding() {
    document.body.style.visibility = 'hidden';

    function avisoTransitorio() {
        // Mensagem mínima sem dado dinâmico; a página continua oculta.
        const aviso = document.createElement('p');
        aviso.setAttribute('role', 'status');
        aviso.textContent = 'Não foi possível confirmar sua sessão agora. Tentando novamente…';
        aviso.style.cssText = 'visibility:visible;position:fixed;top:40%;left:0;right:0;text-align:center;font-family:sans-serif;';
        document.body.append(aviso);
        setTimeout(() => window.location.reload(), 5000);
    }

    try {
        let resp = await fetch('/api/me', { credentials: 'same-origin' });

        if (resp.status === 401) {
            const sessao = await import('/assets/js/services/refresh.js');
            const resultado = await sessao.renovarSessao(Date.now());

            if (resultado === 'indisponivel') {
                avisoTransitorio();
                return;
            }
            if (resultado === 'ok') {
                resp = await fetch('/api/me', { credentials: 'same-origin' });
            }
            if (resp.status === 401) {
                // Havia sessão e ela acabou ('expirada') ou nunca houve ('ausente'): login com retorno.
                sessao.irParaLogin(resultado === 'expirada' ? 'expirada' : null);
                return;
            }
        }

        if (resp.status === 503) {
            avisoTransitorio();
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

        // Expiração da sessão no cliente (só UX: o servidor é a autoridade).
        import('/assets/js/utils/sessao.js')
            .then(m => m.iniciarSessao(me))
            .catch(() => {});
    } catch {
        // Falha de rede ou outro erro inesperado -> login por segurança.
        window.location.replace('/pages/auth/login.html');
    }
})();
