// ── <tratoo-footer> ──────────────────────────────────────────────────────────
// Rodapé como Custom Element (light DOM — usa /assets/css/components/footer.css,
// já carregado via main.css). Injeta o markup de /components/footer.html no
// próprio elemento. Substitui o par `loadComponent('site-footer', ...)` +
// `<div id="site-footer">`.
//
// O CSS mantém o rodapé "sticky" (grudado ao fim da viewport) via
// `tratoo-footer { margin-top: auto }` — ver base/global.css.

export class TratooFooter extends HTMLElement {
    async connectedCallback() {
        if (this._initialized) return;
        this._initialized = true;

        try {
            const res = await fetch('/components/footer.html');
            if (!res.ok) return;
            this.innerHTML = await res.text();
        } catch (_) {
            /* silencioso — o rodapé é não-crítico */
        }
    }
}
