// ── utils/form.js — helpers reutilizáveis de formulário (ES module) ──────────
// Centraliza padrões que estavam duplicados por página: escape de HTML,
// estado de loading em botão, exibição/ocultação de erro e extração da
// mensagem de erro vinda da API.
//
// Importe apenas o que usar:
//   import { setButtonLoading, showError, getErrorMessage } from '/assets/js/utils/form.js';

// Escapa texto para inserção segura em HTML (previne XSS ao interpolar dados
// do usuário/servidor em template strings).
export function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text ?? '';
    return div.innerHTML;
}

// Regex de e-mail usada de forma consistente na validação de formulários.
export function isValidEmail(email) {
    return /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(String(email ?? '').trim());
}

// Coloca um botão em estado de carregamento e retorna uma função que restaura
// o texto/disabled originais. Uso:
//   const restore = setButtonLoading(btn, 'Aguarde...');
//   try { ... } catch { restore(); }
export function setButtonLoading(btn, loadingText = 'Aguarde...') {
    if (!btn) return () => {};
    const originalText = btn.textContent;
    const originalDisabled = btn.disabled;
    btn.disabled = true;
    btn.textContent = loadingText;
    return function restore() {
        btn.disabled = originalDisabled;
        btn.textContent = originalText;
    };
}

// Exibe uma mensagem em um elemento de erro. Aceita id (string) ou elemento.
// Opções: { html } insere via innerHTML (senão textContent, mais seguro);
//          { scroll } rola suavemente até o erro; { autoHideMs } auto-oculta.
export function showError(target, message, { html = false, scroll = true, autoHideMs = 0 } = {}) {
    const el = typeof target === 'string' ? document.getElementById(target) : target;
    if (!el) return;

    if (html) el.innerHTML = message;
    else el.textContent = message;
    el.hidden = false;

    if (scroll) el.scrollIntoView({ behavior: 'smooth', block: 'center' });

    if (autoHideMs > 0) {
        setTimeout(() => { if (el && !el.hidden) el.hidden = true; }, autoHideMs);
    }
}

// Oculta um elemento de erro (id ou elemento).
export function hideError(target) {
    const el = typeof target === 'string' ? document.getElementById(target) : target;
    if (el) el.hidden = true;
}

// Extrai a mensagem de erro de uma rejeição do `api` ({ status, data }) de forma
// consistente, com fallback amigável.
export function getErrorMessage(err, fallback = 'Ocorreu um erro. Tente novamente.') {
    return err?.data?.mensagem
        ?? err?.data?.message
        ?? err?.message
        ?? fallback;
}
