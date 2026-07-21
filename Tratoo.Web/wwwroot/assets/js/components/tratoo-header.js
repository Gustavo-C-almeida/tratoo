// ── <tratoo-header> ──────────────────────────────────────────────────────────
// Header inteligente como Custom Element (light DOM — usa o CSS global de
// /assets/css/components/header.css). Detecta a variante pela URL / usuário
// autenticado, injeta o markup correspondente e liga os comportamentos
// (dropdown do usuário, menu mobile, link ativo e logout).
//
// Substitui o antigo par `core/header.js` (IIFE global) + `<div id="app-header">`,
// dando um ciclo de vida real (connectedCallback) em vez de um script solto que
// dependia da ordem de carregamento no <body>.

export class TratooHeader extends HTMLElement {
    async connectedCallback() {
        // connectedCallback pode disparar mais de uma vez se o elemento for movido;
        // inicializamos apenas uma vez.
        if (this._initialized) return;
        this._initialized = true;

        this._user = null;
        const type = await this._detectType();
        await this._render(type);
    }

    // ── Detecção da variante baseada na URL ───────────────────────────────────
    async _detectType() {
        const path = window.location.pathname;

        // Páginas de autenticação (header mínimo: só logo)
        if (/\/pages\/auth\/(login|cadastro-cliente|cadastro-prestador|onboarding)\.html/.test(path)) {
            return 'auth';
        }

        // Páginas do contratante
        if (path.startsWith('/pages/contratante/')) {
            return 'contratante';
        }

        // Perfil público do prestador — mostra o header de acordo com o usuário
        // autenticado (contratantes também devem ver o header deles).
        if (path.includes('/pages/prestador/perfil.html')) {
            return await this._getUserHeaderType('publico');
        }

        // Páginas do prestador (exceto perfil público)
        if (path.startsWith('/pages/prestador/')) {
            return 'prestador';
        }

        // Páginas compartilhadas autenticadas — detecta pelo /api/me
        if (/\/pages\/(me|contrato|proposta|pagamento|avaliacao|chat|admin)\//.test(path)) {
            return await this._getUserHeaderType('publico');
        }

        // Página de projetos — se logado, mostra nav do usuário; senão, público
        if (path.startsWith('/pages/projetos/')) {
            return await this._getUserHeaderType('publico');
        }

        // Raiz, start, termos e qualquer outra coisa -> header público
        return 'publico';
    }

    // ── Obtém o tipo do usuário (usa cache do auth-guard se disponível) ────────
    async _getUserHeaderType(fallback) {
        if (window.__tratooUser) {
            this._user = window.__tratooUser;
            return this._user.tipo === 'Contratante' ? 'contratante' : 'prestador';
        }
        try {
            const res = await fetch('/api/me', { credentials: 'same-origin' });
            if (!res.ok) return fallback;
            const user = await res.json();
            this._user = user;
            return user.tipo === 'Contratante' ? 'contratante' : 'prestador';
        } catch (_) {
            return fallback;
        }
    }

    // ── Injeta o HTML da variante e inicializa comportamentos ─────────────────
    async _render(type) {
        try {
            const res = await fetch('/components/header-' + type + '.html');
            if (!res.ok) return;
            this.innerHTML = await res.text();
        } catch (_) {
            return;
        }

        if (type === 'contratante' || type === 'prestador') {
            await this._initAuthHeader();
        }

        this._initMobileMenu();
        this._markActiveLink();
    }

    // ── Header autenticado (nome, dropdown, logout) ───────────────────────────
    async _initAuthHeader() {
        if (!this._user && window.__tratooUser) {
            this._user = window.__tratooUser;
        }
        if (!this._user) {
            try {
                const res = await fetch('/api/me', { credentials: 'same-origin' });
                if (res.ok) this._user = await res.json();
            } catch (_) {}
        }

        if (this._user) {
            const nome = this._user.nome || 'Usuário';
            const inicial = nome.trim().charAt(0).toUpperCase();

            const nameEl = this.querySelector('#header-user-name');
            const avatarEl = this.querySelector('#header-user-avatar');
            if (nameEl) nameEl.textContent = nome;
            if (avatarEl) avatarEl.textContent = inicial;

            // Link da área administrativa — visível apenas para administradores.
            if (this._user.isAdmin === true) {
                const nav = this.querySelector('#header-nav');
                if (nav && !this.querySelector('#header-admin-link')) {
                    const adminLink = document.createElement('a');
                    adminLink.id = 'header-admin-link';
                    adminLink.href = '/pages/admin/disputas.html';
                    adminLink.textContent = 'Disputas (Admin)';
                    nav.insertBefore(adminLink, nav.firstChild);
                }
            }
        }

        // Toggle do dropdown
        const userBtn = this.querySelector('#header-user-btn');
        const dropdown = this.querySelector('#header-user-dropdown');

        if (userBtn && dropdown) {
            userBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                const isOpen = dropdown.classList.toggle('open');
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
        const logoutBtn = this.querySelector('#header-logout-btn');
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
    _initMobileMenu() {
        const toggle = this.querySelector('#header-menu-toggle');
        const nav = this.querySelector('#header-nav');
        if (!toggle || !nav) return;

        toggle.addEventListener('click', function () {
            const isOpen = nav.classList.toggle('open');
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
    _markActiveLink() {
        const path = window.location.pathname;
        const links = this.querySelectorAll('#header-nav a[href]');
        links.forEach(function (a) {
            if (a.getAttribute('href') === path) {
                a.classList.add('active');
                a.setAttribute('aria-current', 'page');
            }
        });
    }
}
