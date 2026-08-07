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

        // O menu mobile (offcanvas) e o dropdown do usuário são componentes
        // nativos do Bootstrap, acionados por data-bs-toggle no próprio markup.
        // O data-api do Bootstrap é delegado no document, então funciona mesmo
        // com este header sendo injetado dinamicamente — não há nada a ligar aqui.
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
                    adminLink.className = 'nav-link';   // classe do Bootstrap
                    adminLink.href = '/pages/admin/disputas.html';
                    adminLink.textContent = 'Disputas (Admin)';
                    nav.insertBefore(adminLink, nav.firstChild);
                }
            }
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
