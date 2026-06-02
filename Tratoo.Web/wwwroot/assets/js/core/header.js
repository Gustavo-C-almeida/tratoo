// ── Header inteligente — detecta o tipo e carrega o componente certo ──────────

(function () {
    var _cachedUser = null;

    // ── Ponto de entrada ──────────────────────────────────────────────────────
    async function initHeader() {
        var el = document.getElementById('app-header');
        if (!el) return;

        var type = await _detectType();
        await _loadHeader(el, type);
    }

    // ── Detecção do tipo de header baseada na URL ─────────────────────────────
    async function _detectType() {
        var path = window.location.pathname;

        // Páginas de autenticação (header mínimo: só logo)
        if (/\/pages\/auth\/(login|cadastro-cliente|cadastro-prestador|onboarding)\.html/.test(path)) {
            return 'auth';
        }

        // Páginas do contratante
        if (path.startsWith('/pages/contratante/')) {
            return 'contratante';
        }

        // Perfil público do prestador — mostra header de acordo com o usuário autenticado
        // (não apenas "prestador", pois contratantes também devem ver o header deles)
        if (path.includes('/pages/prestador/perfil.html')) {
            return await _getUserHeaderType('publico');
        }

        // Páginas do prestador (exceto perfil público)
        if (path.startsWith('/pages/prestador/')) {
            return 'prestador';
        }

        // Páginas compartilhadas autenticadas — detecta pelo /api/me
        if (/\/pages\/(me|contrato|proposta|pagamento|avaliacao|chat)\//.test(path)) {
            return await _getUserHeaderType('publico');
        }

        // Página de projetos — se logado, mostra nav do usuário; senão, header público
        if (path.startsWith('/pages/projetos/')) {
            return await _getUserHeaderType('publico');
        }

        // Raiz, start, termos e qualquer outra coisa → header público
        return 'publico';
    }

    // ── Obtém o tipo do usuário (usa cache do auth-guard se disponível) ────────
    async function _getUserHeaderType(fallback) {
        // Aproveita o cache já preenchido pelo auth-guard.js
        if (window.__tratooUser) {
            _cachedUser = window.__tratooUser;
            return _cachedUser.tipo === 'Contratante' ? 'contratante' : 'prestador';
        }
        try {
            var res = await fetch('/api/me', { credentials: 'same-origin' });
            if (!res.ok) return fallback;
            var user = await res.json();
            _cachedUser = user;
            return user.tipo === 'Contratante' ? 'contratante' : 'prestador';
        } catch (_) {
            return fallback;
        }
    }

    // ── Injeta o HTML do componente e inicializa comportamentos ───────────────
    async function _loadHeader(el, type) {
        try {
            var res = await fetch('/components/header-' + type + '.html');
            if (!res.ok) return;
            el.innerHTML = await res.text();
        } catch (_) {
            return;
        }

        if (type === 'contratante' || type === 'prestador') {
            await _initAuthHeader();
        }

        _initMobileMenu();
        _markActiveLink();
    }

    // ── Inicializa o header autenticado (nome, dropdown, logout) ──────────────
    async function _initAuthHeader() {
        // Busca dados do usuário (usa cache do auth-guard ou chamada anterior)
        if (!_cachedUser && window.__tratooUser) {
            _cachedUser = window.__tratooUser;
        }
        if (!_cachedUser) {
            try {
                var res = await fetch('/api/me', { credentials: 'same-origin' });
                if (res.ok) _cachedUser = await res.json();
            } catch (_) {}
        }

        if (_cachedUser) {
            var nome = _cachedUser.nome || 'Usuário';
            var inicial = nome.trim().charAt(0).toUpperCase();

            var nameEl = document.getElementById('header-user-name');
            var avatarEl = document.getElementById('header-user-avatar');
            if (nameEl) nameEl.textContent = nome;
            if (avatarEl) avatarEl.textContent = inicial;
        }

        // Toggle do dropdown
        var userBtn = document.getElementById('header-user-btn');
        var dropdown = document.getElementById('header-user-dropdown');

        if (userBtn && dropdown) {
            userBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                var isOpen = dropdown.classList.toggle('open');
                userBtn.setAttribute('aria-expanded', String(isOpen));
            });

            document.addEventListener('click', function () {
                dropdown.classList.remove('open');
                if (userBtn) userBtn.setAttribute('aria-expanded', 'false');
            });

            document.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') {
                    dropdown.classList.remove('open');
                    userBtn.setAttribute('aria-expanded', 'false');
                    userBtn.focus();
                }
            });
        }

        // Logout
        var logoutBtn = document.getElementById('header-logout-btn');
        if (logoutBtn) {
            logoutBtn.addEventListener('click', async function () {
                logoutBtn.textContent = 'Saindo...';
                logoutBtn.disabled = true;
                try {
                    await fetch('/usuarios/logout', { method: 'POST', credentials: 'same-origin' });
                } catch (_) {}
                window.location.href = '/pages/auth/login.html';
            });
        }
    }

    // ── Hamburger (menu mobile) ───────────────────────────────────────────────
    function _initMobileMenu() {
        var toggle = document.getElementById('header-menu-toggle');
        var nav = document.getElementById('header-nav');
        if (!toggle || !nav) return;

        toggle.addEventListener('click', function () {
            var isOpen = nav.classList.toggle('open');
            toggle.classList.toggle('active', isOpen);
            toggle.setAttribute('aria-expanded', String(isOpen));
        });

        // Fecha ao clicar em um link do nav
        nav.addEventListener('click', function (e) {
            if (e.target.tagName === 'A') {
                nav.classList.remove('open');
                toggle.classList.remove('active');
                toggle.setAttribute('aria-expanded', 'false');
            }
        });
    }

    // ── Marca o link ativo baseado na URL atual ───────────────────────────────
    function _markActiveLink() {
        var path = window.location.pathname;
        var links = document.querySelectorAll('#header-nav a[href]');
        links.forEach(function (a) {
            if (a.getAttribute('href') === path) {
                a.classList.add('active');
                a.setAttribute('aria-current', 'page');
            }
        });
    }

    // ── Executa ao DOM estar pronto ───────────────────────────────────────────
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initHeader);
    } else {
        initHeader();
    }
})();
