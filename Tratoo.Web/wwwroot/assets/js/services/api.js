// ── services/api.js — wrapper central de fetch (ES module) ───────────────────
// Importe com: `import { api } from '/assets/js/services/api.js';`
// Centraliza método/headers/credenciais, tratamento de erro `{ status, data }`
// e o overlay de loading global (com debounce de 200ms).

const API_BASE = '';

// ── Loading overlay global ────────────────────────────────────────────────────
// Contador de requisições concorrentes: só esconde quando todas terminam.
let _reqCount   = 0;
let _reqTimer   = null;

function _getOverlay() {
    let el = document.getElementById('loading-overlay');
    if (!el) {
        el = document.createElement('div');
        el.id = 'loading-overlay';
        el.setAttribute('role', 'status');
        el.setAttribute('aria-live', 'polite');
        el.innerHTML = `
            <div class="loading-card">
                <div class="loading-spinner" aria-hidden="true"></div>
                <span>Carregando...</span>
            </div>`;
        document.body.appendChild(el);
    }
    return el;
}

function _showLoading() {
    _reqCount++;
    // Aguarda 200 ms antes de exibir — evita flash em respostas rápidas
    if (_reqCount === 1 && !_reqTimer) {
        _reqTimer = setTimeout(() => {
            _reqTimer = null;
            if (_reqCount > 0) _getOverlay().classList.add('visible');
        }, 200);
    }
}

function _hideLoading() {
    _reqCount = Math.max(0, _reqCount - 1);
    if (_reqCount === 0) {
        clearTimeout(_reqTimer);
        _reqTimer = null;
        const el = document.getElementById('loading-overlay');
        if (el) el.classList.remove('visible');
    }
}
// ─────────────────────────────────────────────────────────────────────────────

export const api = {
    async post(endpoint, body) {
        _showLoading();
        try {
            const response = await fetch(`${API_BASE}${endpoint}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify(body)
            });
            let data = {};
            try { data = await response.json(); } catch (_) {}
            if (!response.ok) throw { status: response.status, data };
            return data;
        } finally {
            _hideLoading();
        }
    },

    async get(endpoint) {
        _showLoading();
        try {
            const response = await fetch(`${API_BASE}${endpoint}`, {
                method: 'GET',
                credentials: 'same-origin'
            });
            let data = {};
            try { data = await response.json(); } catch (_) {}
            if (!response.ok) throw { status: response.status, data };
            return data;
        } finally {
            _hideLoading();
        }
    },

    async put(endpoint, body) {
        _showLoading();
        try {
            const response = await fetch(`${API_BASE}${endpoint}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify(body)
            });
            let data = {};
            try { data = await response.json(); } catch (_) {}
            if (!response.ok) throw { status: response.status, data };
            return data;
        } finally {
            _hideLoading();
        }
    },

    async patch(endpoint, body) {
        _showLoading();
        try {
            const response = await fetch(`${API_BASE}${endpoint}`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify(body)
            });
            let data = {};
            try { data = await response.json(); } catch (_) {}
            if (!response.ok) throw { status: response.status, data };
            return data;
        } finally {
            _hideLoading();
        }
    },

    async delete(endpoint) {
        _showLoading();
        try {
            const response = await fetch(`${API_BASE}${endpoint}`, {
                method: 'DELETE',
                credentials: 'same-origin'
            });
            let data = {};
            try { data = await response.json(); } catch (_) {}
            if (!response.ok) throw { status: response.status, data };
            return data;
        } finally {
            _hideLoading();
        }
    },

    /** Upload multipart/form-data via POST (foto, portfólio PDF). */
    async uploadPost(endpoint, formData) {
        _showLoading();
        try {
            const response = await fetch(`${API_BASE}${endpoint}`, {
                method: 'POST',
                credentials: 'same-origin',
                body: formData   // não define Content-Type; o browser injeta boundary automaticamente
            });
            let data = {};
            try { data = await response.json(); } catch (_) {}
            if (!response.ok) throw { status: response.status, data };
            return data;
        } finally {
            _hideLoading();
        }
    },

    /** Upload multipart/form-data via PUT. */
    async uploadPut(endpoint, formData) {
        _showLoading();
        try {
            const response = await fetch(`${API_BASE}${endpoint}`, {
                method: 'PUT',
                credentials: 'same-origin',
                body: formData
            });
            let data = {};
            try { data = await response.json(); } catch (_) {}
            if (!response.ok) throw { status: response.status, data };
            return data;
        } finally {
            _hideLoading();
        }
    }
};
