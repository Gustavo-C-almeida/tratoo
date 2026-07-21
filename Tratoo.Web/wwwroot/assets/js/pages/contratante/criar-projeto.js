import { api } from '/assets/js/services/api.js';
// ── Criar / publicar projeto (US-04) ─────────────────────────────────────────

const root = () => document.getElementById('criar-projeto-root');
const habilidades = [];

// Normalização de tags: aliases comuns -> nome canônico.
// Cobre as diversas áreas do marketplace (design, marketing, dados, vídeo, etc.),
// não apenas desenvolvimento de software.
const TAG_ALIASES = {
    // Desenvolvimento de software
    'reactjs': 'React', 'react.js': 'React',
    'nodejs': 'Node.js', 'node': 'Node.js',
    'typescript': 'TypeScript', 'javascript': 'JavaScript',
    'golang': 'Go', 'csharp': 'C#', 'dotnet': '.NET', '.net': '.NET',
    // Design e edição de imagem
    'ps': 'Photoshop', 'photoshop': 'Photoshop',
    'ai': 'Illustrator', 'illustrator': 'Illustrator',
    'figma': 'Figma', 'canva': 'Canva',
    'uxui': 'UX/UI', 'ux/ui': 'UX/UI', 'ui/ux': 'UX/UI',
    // Marketing, social media e tráfego
    'socialmedia': 'Social Media', 'social media': 'Social Media',
    'trafego': 'Gestão de Tráfego', 'tráfego': 'Gestão de Tráfego',
    'trafego pago': 'Gestão de Tráfego', 'ads': 'Tráfego Pago',
    'metaads': 'Meta Ads', 'meta ads': 'Meta Ads',
    'googleads': 'Google Ads', 'google ads': 'Google Ads',
    'seo': 'SEO', 'copy': 'Copywriting', 'copywriting': 'Copywriting',
    // Dados, BI e planilhas
    'excel': 'Excel', 'planilhas': 'Planilhas', 'sheets': 'Google Sheets',
    'powerbi': 'Power BI', 'power bi': 'Power BI', 'bi': 'Power BI',
    'sql': 'SQL', 'dados': 'Análise de Dados',
    // Vídeo e conteúdo
    'premiere': 'Premiere', 'aftereffects': 'After Effects',
    'after effects': 'After Effects', 'davinci': 'DaVinci Resolve',
    'edicao de video': 'Edição de Vídeo',
    // Outros serviços
    'traducao': 'Tradução', 'tradução': 'Tradução',
    'va': 'Assistência Virtual', 'assistente virtual': 'Assistência Virtual',
    'automacao': 'Automação', 'automação': 'Automação',
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
            <h3><i class="fa-solid fa-file-pen"></i> Informações do projeto</h3>

            <div class="campo">
                <label for="proj-titulo">Título <span class="obrigatorio">*</span></label>
                <input id="proj-titulo" type="text" maxlength="200"
                    placeholder='Ex: "Criar identidade visual para minha loja" ou "Editar 10 vídeos para o Instagram"'>
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
                        <option value="TI">Desenvolvimento de Software</option>
                        <option value="Design">Design & UX/UI</option>
                        <option value="Marketing">Marketing Digital</option>
                        <option value="Redacao">Redação & Conteúdo</option>
                        <option value="Video">Edição de Vídeo</option>
                        <option value="Dados">Dados & BI</option>
                        <option value="Traducao">Tradução</option>
                        <option value="Suporte">Suporte & Assistência Virtual</option>
                        <option value="Consultoria">Consultoria</option>
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
            <h3><i class="fa-solid fa-dollar-sign"></i> Orçamento & Prazo</h3>

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
            <h3><i class="fa-solid fa-sliders"></i> Detalhes adicionais</h3>

            <div class="campo">
                <label>Habilidades desejadas</label>
                <div class="habilidades-input-wrapper">
                    <input id="inp-habilidade" type="text" maxlength="50" placeholder="Ex: Photoshop, Social Media, Power BI...">
                    <button type="button" id="btn-add-habilidade">+ Adicionar</button>
                </div>
                <div id="habilidades-tags" class="habilidades-tags"></div>
                <small>Até 10 habilidades. Cada tag: máx. 50 caracteres.</small>
            </div>

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
                <label for="proj-visibilidade">Visibilidade</label>
                <select id="proj-visibilidade">
                    <option value="Publico">Público (aparece na listagem)</option>
                    <option value="Privado">Privado (somente via link)</option>
                </select>
            </div>
        </div>

        <!-- Ações -->
        <div class="acoes-bar">
            <button id="btn-rascunho" class="btn-rascunho"><i class="fa-solid fa-floppy-disk"></i> Salvar como rascunho</button>
            <button id="btn-publicar" class="btn-publicar"><i class="fa-solid fa-rocket"></i> Publicar projeto</button>
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
        inp.placeholder = `"${tagRaw}" normalizado para "${tag}"`;
        setTimeout(() => { inp.placeholder = originalPlaceholder; }, 2000);
    }

    habilidades.push(tag);
    inp.value = '';
    renderHabilidades();
}

function renderHabilidades() {
    const container = document.getElementById('habilidades-tags');
    if (!container) return;
    container.innerHTML = habilidades.map((h, i) => `
        <span class="habilidade-tag">
            ${escHtml(h)}
            <button type="button" onclick="removerHabilidade(${i})" aria-label="Remover ${escHtml(h)}"><i class="fa-solid fa-x"></i></button>
        </span>`).join('');
}

function removerHabilidade(idx) {
    habilidades.splice(idx, 1);
    renderHabilidades();
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