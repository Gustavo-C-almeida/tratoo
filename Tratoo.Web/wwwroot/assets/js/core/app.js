function loadComponent(id, url) {
    const el = document.getElementById(id);
    if (!el) return;
    fetch(url)
        .then(r => r.text())
        .then(html => { el.innerHTML = html; });
}
