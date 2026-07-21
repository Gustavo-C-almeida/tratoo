# Front-end do Tratoo — Guia de Contribuição

Front-end **HTML/CSS/JavaScript puro** (sem framework, sem bundler, sem npm),
servido como arquivos estáticos por `Tratoo.Web`. Este guia descreve as
convenções para manter o código consistente, organizado e escalável.

> Regra de ouro: **nada de dependência de build**. Tudo roda direto no browser
> via ES Modules nativos e Custom Elements nativos.

---

## 1. Estrutura por feature (espelhada)

Toda feature nova cria a mesma tripla, com o **mesmo nome** nos três lugares:

```
wwwroot/
├── pages/<feature>/<page>.html          # markup da página
├── assets/css/pages/<feature>/<page>.css # estilos da página
└── assets/js/pages/<feature>/<page>.js   # lógica da página (ES module)
```

Exemplo: `pages/prestador/buscar.html` ↔ `assets/css/pages/prestador/buscar.css`
↔ `assets/js/pages/prestador/buscar.js`.

Assets compartilhados:

```
assets/
├── css/
│   ├── base/         # reset, variables (design tokens), global
│   └── components/   # header, footer, e futuros componentes (botão, card…)
└── js/
    ├── core/         # app.js (bootstrap único)
    ├── components/   # Custom Elements (<tratoo-header>, <tratoo-footer>)
    ├── services/     # api.js (wrapper de fetch)
    └── utils/        # form.js, auth-guard.js
```

---

## 2. Esqueleto de uma página

```html
<!DOCTYPE html>
<html lang="pt-BR">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Título — Tratoo</title>
    <link rel="stylesheet" href="/assets/css/main.css">
    <link rel="stylesheet" href="/assets/css/pages/<feature>/<page>.css">
    <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.2/css/all.min.css">
</head>
<body>
    <tratoo-header></tratoo-header>

    <!-- conteúdo da página -->
    <div id="root"></div>

    <tratoo-footer></tratoo-footer>

    <!-- bootstrap: registra os web components (obrigatório em toda página) -->
    <script type="module" src="/assets/js/core/app.js"></script>

    <!-- SOMENTE em páginas protegidas: guard clássico (bloqueante) -->
    <script src="/assets/js/utils/auth-guard.js"></script>

    <!-- script da página (ES module) -->
    <script type="module" src="/assets/js/pages/<feature>/<page>.js"></script>
</body>
</html>
```

Regras:

- **`core/app.js` entra em toda página** — é ele que registra `<tratoo-header>`
  e `<tratoo-footer>`.
- **`auth-guard.js` continua sendo script clássico** (não módulo) e vem **antes**
  do módulo da página. Ele roda de forma síncrona durante o parse para esconder
  o `body` e checar `/api/me` o quanto antes (evita flash de conteúdo protegido).
  Módulos são *deferred* — se o guard virasse módulo, o flash voltaria.
- Não há mais `header.js`, `loadComponents.js` nem tags soltas de `api.js`:
  a ordem de carregamento é resolvida pelos `import`.

---

## 3. Web Components (header / footer)

Header e footer são Custom Elements nativos (light DOM — usam o CSS global de
`components/header.css` / `components/footer.css`):

- `assets/js/components/tratoo-header.js` — detecta a variante (público, auth,
  contratante, prestador) pela URL / usuário autenticado e liga dropdown, menu
  mobile, link ativo e logout no `connectedCallback`.
- `assets/js/components/tratoo-footer.js` — injeta `/components/footer.html`.

Para criar um **novo componente reutilizável**, siga o mesmo padrão
(`class XExtends extends HTMLElement { connectedCallback() {...} }`) e registre-o
em `core/app.js`. Componentes menores ainda baseados em fetch (ex.: o formulário
de cadastro em `#cadastro`) devem migrar para Custom Element de forma incremental.

---

## 4. JavaScript — ES Modules

- **Todo page-script é um ES module** (`<script type="module">`).
- **Chamadas à API sempre pelo serviço central**:
  ```js
  import { api } from '/assets/js/services/api.js';
  const dados = await api.get('/api/projects');   // get/post/put/patch/delete/uploadPost/uploadPut
  ```
  O `api` centraliza método, headers, `credentials`, tratamento de erro
  (`throw { status, data }`) e o overlay de loading global.
- **Inicialização padronizada** via `onReady` (o "contrato" de página) em vez de
  registrar seu próprio `DOMContentLoaded`:
  ```js
  import { onReady } from '/assets/js/core/app.js';
  function initPage() { /* ... */ }
  onReady(initPage);
  ```
- **Helpers de formulário reutilizáveis** em `utils/form.js` — não reimplemente
  por página:
  ```js
  import { escapeHtml, setButtonLoading, showError, getErrorMessage, isValidEmail }
      from '/assets/js/utils/form.js';

  const restore = setButtonLoading(btn, 'Enviando...');
  try { await api.post('/x', body); }
  catch (err) { showError('meu-erro', getErrorMessage(err)); restore(); }
  ```
- Evite handlers inline no HTML (`onclick="..."`). Use `addEventListener` /
  event delegation — módulos não expõem funções no escopo global.
- `window.__tratooUser` (populado pelo `auth-guard.js`) é o global de usuário
  compartilhado entre o guard, o header e as páginas.

`login.js` e `cadastro.js` são exemplos de referência já migrados para este padrão.

---

## 5. CSS — tokens, BEM e componentes

- **Design tokens são a fonte única de verdade** (`base/variables.css`). Use
  `var(--token)`; não escreva valores hex/px/rem crus no CSS de feature.
- **Nomenclatura BEM**: `bloco`, `bloco__elemento`, `bloco--modificador`
  (ex.: `.login__form`, `.cadastro__button`).
- **Componentes reutilizáveis** (botão, card, badge) devem morar em
  `assets/css/components/` e ser importados por `main.css` — não reimplementados
  por página.
- `main.css` compõe `reset + variables + global + components` via `@import`; cada
  página linka ainda o seu `pages/<feature>/<page>.css`.

### Dívida técnica conhecida (consolidação incremental)

O CSS de feature ainda reimplementa botões/badges por conta própria
(ex.: `.btn-aceitar`, `.btn-recusar`, `.badge-aberta`). O maior ofensor é
`pages/proposta/detalhe.css` (>3.000 linhas, com regras duplicadas). O caminho
recomendado — a ser feito com **revisão visual** — é extrair um sistema único de
`.btn` / `.card` / `.badge` para `components/` e migrar as páginas uma a uma,
validando cada tela antes de remover as classes antigas.
