// ── Criar / publicar projeto (US-04) ─────────────────────────────────────────

const root = () => document.getElementById('criar-projeto-root');
const habilidades = [];

// Normalização de tags: aliases comuns → nome canônico
const TAG_ALIASES = {
    'reactjs': 'React', 'react.js': 'React',
    'vuejs': 'Vue.js', 'vue': 'Vue.js',
    'nodejs': 'Node.js', 'node': 'Node.js',
    'expressjs': 'Express.js', 'express': 'Express.js',
    'nextjs': 'Next.js', 'nuxtjs': 'Nuxt.js',
    'typescript': 'TypeScript', 'javascript': 'JavaScript',
    'angularjs': 'Angular', 'reactnative': 'React Native',
    'react native': 'React Native',
    'tailwindcss': 'Tailwind CSS', 'tailwind': 'Tailwind CSS',
    'nestjs': 'NestJS', 'k8s': 'Kubernetes',
    'postgres': 'PostgreSQL', 'postgresql': 'PostgreSQL',
    'golang': 'Go', 'csharp': 'C#',
    'dotnet': '.NET', '.net': '.NET',
};

function normalizarTag(tag) {
    return TAG_ALIASES[tag.toLowerCase()] || tag;
}

function escHtml(str) {
    if (!str) return '';
    return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

function renderPagina() {
    // Prazo mínimo = amanhã
    const amanha = new Date();
    amanha.setDate(amanha.getDate() + 1);
    const prazoMin = amanha.toISOString().split('T')[0];

    root().innerHTML = `
    <div class="cp-header">
        <h1>Publicar Projeto</h1>
        <p>Descreva o projeto com detalhes para atrair os melhores profissionais.</p>
    </div>

    <div class="cp-container">
        <div id="msg-global"></div>

        <!-- Seção 1: Informações principais -->
        <div class="form-card">
            <h3>Informações do projeto</h3>

            <div class="campo">
                <label for="proj-titulo">Título <span class="obrigatorio">*</span></label>
                <input id="proj-titulo" type="text" maxlength="200"
                    placeholder='Ex: "Desenvolver API REST em .NET 8 com EF Core"'>
                <small>Mínimo 10, máximo 200 caracteres.</small>
                <div id="titulo-count" class="char-count">0 / 10 mínimo</div>
            </div>

            <div class="campo">
                <label for="proj-descricao">Descrição <span class="obrigatorio">*</span></label>
                <textarea id="proj-descricao" rows="7"
                    placeholder="Descreva o escopo, contexto, requisitos e o que será entregue. Quanto mais detalhe, melhor a qualidade das propostas."></textarea>
                <small>Mínimo 100, máximo 5.000 caracteres.</small>
                <div id="desc-count" class="char-count">0 / 100 mínimo</div>
            </div>

            <div class="campo-row">
                <div class="campo">
                    <label for="proj-categoria">Categoria <span class="obrigatorio">*</span></label>
                    <select id="proj-categoria">
                        <option value="">Selecione</option>
                        <option value="TI">TI & Tecnologia</option>
                        <option value="Design">Design</option>
                        <option value="Marketing">Marketing</option>
                        <option value="Redacao">Redação</option>
                        <option value="Juridico">Jurídico</option>
                        <option value="Outros">Outros</option>
                    </select>
                </div>
                <div class="campo">
                    <label for="proj-idioma">Idioma do projeto</label>
                    <select id="proj-idioma">
                        <option value="Portugues">Português (padrão)</option>
                        <option value="Ingles">Inglês</option>
                        <option value="Espanhol">Espanhol</option>
                    </select>
                </div>
            </div>
        </div>

        <!-- Seção 2: Orçamento e prazo -->
        <div class="form-card">
            <h3>Orçamento & Prazo</h3>

            <div class="campo-row">
                <div class="campo">
                    <label for="proj-orc-min">Orçamento mínimo (R$) <span class="obrigatorio">*</span></label>
                    <input id="proj-orc-min" type="number" min="0.01" step="0.01" placeholder="Ex: 1000">
                </div>
                <div class="campo">
                    <label for="proj-orc-max">Orçamento máximo (R$) <span class="obrigatorio">*</span></label>
                    <input id="proj-orc-max" type="number" min="0.01" step="0.01" placeholder="Ex: 5000">
                </div>
            </div>

            <div class="campo">
                <label for="proj-prazo">Prazo de entrega <span class="obrigatorio">*</span></label>
                <input id="proj-prazo" type="date" min="${prazoMin}">
                <small>Data limite para conclusão do projeto.</small>
            </div>
        </div>

        <!-- Seção 3: Detalhes adicionais (opcionais) -->
        <div class="form-card">
            <h3>Detalhes adicionais</h3>

            <div class="campo">
                <label>Habilidades desejadas</label>
                <div class="habilidades-input-wrapper">
                    <input id="inp-habilidade" type="text" maxlength="50" placeholder="Ex: React, Node.js, Python...">
                    <button type="button" id="btn-add-habilidade">+ Adicionar</button>
                </div>
                <div id="habilidades-tags" class="habilidades-tags"></div>
                <small>Até 10 habilidades. Cada tag: máx. 50 caracteres.</small>
                <div id="match-preview" class="match-preview" style="display:none"></div>
            </div>

            <div class="campo-row">
                <div class="campo">
                    <label for="proj-nivel">Nível do freelancer</label>
                    <select id="proj-nivel">
                        <option value="">Qualquer nível</option>
                        <option value="Junior">Júnior</option>
                        <option value="Pleno">Pleno</option>
                        <option value="Senior">Sênior</option>
                    </select>
                </div>
                <div class="campo">
                    <label for="proj-num-freelancers">Nº de freelancers</label>
                    <input id="proj-num-freelancers" type="number" min="1" value="1">
                </div>
            </div>

            <div class="campo">
                <label for="proj-visibilidade">Visibilidade</label>
                <select id="proj-visibilidade">
                    <option value="Publico">Público (aparece na listagem)</option>
                    <option value="Privado">Privado (somente via link)</option>
                </select>
            </div>
        </div>

        <!-- Ações -->
        <div class="acoes-bar">
            <button id="btn-rascunho" class="btn-rascunho">Salvar como rascunho</button>
            <button id="btn-publicar" class="btn-publicar">Publicar projeto</button>
        </div>
    </div>`;

    // Contador título
    const tituloEl = document.getElementById('proj-titulo');
    const tituloCount = document.getElementById('titulo-count');
    tituloEl.addEventListener('input', () => {
        const len = tituloEl.value.length;
        tituloCount.textContent = `${len} / ${len < 10 ? '10 mínimo' : '200 máximo'}`;
        tituloCount.className = 'char-count ' + (len >= 10 && len <= 200 ? 'ok' : 'danger');
    });

    // Contador de descrição (apenas contador, sem checklist)
    const descEl = document.getElementById('proj-descricao');
    const descCount = document.getElementById('desc-count');
    descEl.addEventListener('input', () => {
        const len = descEl.value.length;
        descCount.textContent = `${len} caracter${len !== 1 ? 'es' : ''}`;
        descCount.className = 'char-count ' + (len >= 100 ? 'ok' : (len > 0 ? 'danger' : ''));
    });

    // Habilidades
    document.getElementById('btn-add-habilidade').addEventListener('click', adicionarHabilidade);
    document.getElementById('inp-habilidade').addEventListener('keydown', e => {
        if (e.key === 'Enter') { e.preventDefault(); adicionarHabilidade(); }
    });

    // Botões de ação
    document.getElementById('btn-rascunho').addEventListener('click', () => submeter(false));
    document.getElementById('btn-publicar').addEventListener('click', () => submeter(true));
}

function adicionarHabilidade() {
    const inp = document.getElementById('inp-habilidade');
    const tagRaw = inp.value.trim();
    if (!tagRaw) return;
    if (habilidades.length >= 10) { mostrarMsg('erro', 'Máximo de 10 habilidades atingido.'); return; }

    const tag = normalizarTag(tagRaw);

    // Duplicata (case-insensitive)
    if (habilidades.some(h => h.toLowerCase() === tag.toLowerCase())) { inp.value = ''; return; }

    // Feedback visual de normalização
    if (tag !== tagRaw) {
        const originalPlaceholder = inp.placeholder;
        inp.placeholder = `"${tagRaw}" → "${tag}" ✓`;
        setTimeout(() => { inp.placeholder = originalPlaceholder; }, 2000);
    }

    habilidades.push(tag);
    inp.value = '';
    renderHabilidades();
    agendarMatchPreview();
}

function renderHabilidades() {
    const container = document.getElementById('habilidades-tags');
    if (!container) return;
    container.innerHTML = habilidades.map((h, i) => `
        <span class="habilidade-tag">
            ${escHtml(h)}
            <button type="button" onclick="removerHabilidade(${i})" aria-label="Remover ${escHtml(h)}">&times;</button>
        </span>`).join('');
}

function removerHabilidade(idx) {
    habilidades.splice(idx, 1);
    renderHabilidades();
    agendarMatchPreview();
}

// ── Preview de match (prestadores compatíveis) ────────────────────────────────

let _matchTimer = null;

function agendarMatchPreview() {
    clearTimeout(_matchTimer);
    const el = document.getElementById('match-preview');
    if (!el) return;
    if (habilidades.length === 0) { el.style.display = 'none'; return; }
    _matchTimer = setTimeout(atualizarMatchPreview, 900);
}

async function atualizarMatchPreview() {
    const el = document.getElementById('match-preview');
    if (!el || habilidades.length === 0) return;
    el.style.display = 'block';
    el.innerHTML = '<span class="match-loading">Verificando prestadores compatíveis...</span>';
    try {
        const q = encodeURIComponent(habilidades.join(' '));
        const resultado = await api.get(`/api/busca/prestadores?q=${q}&pageSize=50`);

        // Filtra apenas prestadores com similarity >= 0.6 (boa correspondência)
        const prestadores = Array.isArray(resultado)
            ? resultado.filter(p => p.similaridade && p.similaridade >= 0.2)
            : [];

        if (prestadores.length === 0) {
            el.innerHTML = '<span class="match-zero">Nenhum prestador com boa compatibilidade encontrado com essas competências.</span>';
            return;
        }

        // Ordena por similaridade (decrescente)
        prestadores.sort((a, b) => (b.similaridade || 0) - (a.similaridade || 0));

        // Limita a 5 prestadores na preview
        const top5 = prestadores.slice(0, 5);

        const listaHtml = top5.map(p => {
            const nomePrestador = escHtml(p.nome || 'Sem nome');
            const tituloProfissional = escHtml(p.tituloProfissional || '');
            // Pega competências do endpoint (array de strings)
            const competenciasStr = Array.isArray(p.competencias) && p.competencias.length > 0
                ? p.competencias.map(c => escHtml(String(c))).join(', ')
                : 'Sem competências';
            // Primeiras 100 caracteres da bio
            const bioPreview = p.bio
                ? escHtml(p.bio.substring(0, 100)) + (p.bio.length > 100 ? '...' : '')
                : 'Sem bio';

            return `
                <div class="match-card">
                    <div class="match-card-header">
                        <div>
                            <h4><a href="/pages/prestador/perfil.html?id=${p.id}" target="_blank" class="match-link">${nomePrestador}</a></h4>
                            <p class="match-titulo">${tituloProfissional}</p>
                        </div>
                        <span class="match-similarity">${(p.similaridade * 100).toFixed(0)}%</span>
                    </div>
                    <div class="match-card-body">
                        <p class="match-bio">${bioPreview}</p>
                        <p class="match-skills"><strong>Competências:</strong> ${competenciasStr}</p>
                    </div>
                </div>
            `;
        }).join('');

        const countMsg = prestadores.length > 5
            ? `Mostrando top 5 de ${prestadores.length} prestadores compatíveis`
            : `${prestadores.length} prestador${prestadores.length !== 1 ? 'es' : ''} compatível${prestadores.length !== 1 ? 's' : ''}`;

        el.innerHTML = `
            <div class="match-summary">
                <span class="match-hit">✓ ${countMsg}</span>
            </div>
            <div class="match-list">
                ${listaHtml}
            </div>
        `;
    } catch {
        el.style.display = 'none';
    }
}

// ── Feedback e submissão ──────────────────────────────────────────────────────

function mostrarMsg(tipo, texto) {
    const el = document.getElementById('msg-global');
    if (!el) return;
    el.innerHTML = `<div class="msg-feedback ${tipo}">${escHtml(texto)}</div>`;
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

async function submeter(publicar) {
    const msgEl = document.getElementById('msg-global');
    msgEl.innerHTML = '';

    const titulo = document.getElementById('proj-titulo').value.trim();
    const descricao = document.getElementById('proj-descricao').value.trim();
    const categoria = document.getElementById('proj-categoria').value;
    const orcMin = parseFloat(document.getElementById('proj-orc-min').value);
    const orcMax = parseFloat(document.getElementById('proj-orc-max').value);
    const prazo = document.getElementById('proj-prazo').value;
    const nivel = document.getElementById('proj-nivel').value || null;
    const numFreel = parseInt(document.getElementById('proj-num-freelancers').value) || 1;
    const visib = document.getElementById('proj-visibilidade').value;
    const idioma = document.getElementById('proj-idioma').value;

    // Validações no cliente (o backend valida novamente)
    if (titulo.length < 10) { mostrarMsg('erro', 'O título precisa ter pelo menos 10 caracteres.'); return; }
    if (descricao.length < 100) { mostrarMsg('erro', 'A descrição precisa ter pelo menos 100 caracteres.'); return; }
    if (!categoria) { mostrarMsg('erro', 'Selecione uma categoria.'); return; }
    if (!prazo) { mostrarMsg('erro', 'Informe o prazo de entrega.'); return; }
    if (!orcMin || !orcMax || orcMax < orcMin) { mostrarMsg('erro', 'Verifique os valores de orçamento.'); return; }

    const btnR = document.getElementById('btn-rascunho');
    const btnP = document.getElementById('btn-publicar');
    btnR.disabled = btnP.disabled = true;
    btnR.textContent = btnP.textContent = 'Aguarde...';

    try {
        await api.post('/api/projects', {
            titulo,
            descricao,
            categoria,
            orcamentoMin: orcMin,
            orcamentoMax: orcMax,
            prazoEntrega: prazo,
            habilidades: habilidades.length > 0 ? [...habilidades] : null,
            nivelFreelancer: nivel,
            visibilidade: visib,
            idioma,
            numFreelancersDesejados: numFreel,
            publicarImediatamente: publicar
        });

        const status = publicar ? 'publicado' : 'salvo como rascunho';
        mostrarMsg('sucesso', `Projeto ${status} com sucesso! Redirecionando...`);

        setTimeout(() => {
            window.location.href = 'meus-projetos.html';
        }, 1800);

    } catch (err) {
        const texto = err?.data?.mensagem || 'Erro ao salvar o projeto. Tente novamente.';
        mostrarMsg('erro', texto);
        btnR.disabled = btnP.disabled = false;
        btnR.textContent = 'Salvar como rascunho';
        btnP.textContent = 'Publicar projeto';
    }
}

// Expõe para onclick inline do HTML
window.removerHabilidade = removerHabilidade;

// Inicia
renderPagina();