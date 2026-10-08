import { api } from '/assets/js/services/api.js';
import { el } from '/assets/js/utils/dom.js';
// ── Estado do onboarding ──────────────────────────────────────────────────────
const estado = {
    tipoPessoa: null,           // 'PessoaFisica' | 'PessoaJuridica'
    // PF
    nomeLegal: '',
    cpfCnpj: '',
    dataNascimento: '',
    exibirIdade: false,
    // PJ — empresa
    razaoSocial: '',
    cnpj: '',
    nomeEmpresa: '',
    segmento: '',
    inscricaoEstadual: '',
    inscricaoMunicipal: '',
    dataAbertura: '',
    // PJ — representante
    nomeRepresentanteLegal: '',
    cpfRepresentanteLegal: '',
    cargoRepresentante: '',
    emailRepresentante: '',
    telefoneRepresentante: '',
    // Endereço
    cep: '',
    logradouro: '',
    numero: '',
    complemento: '',
    bairro: '',
    cidade: '',
    estado: ''
};

const container = () => document.getElementById('onboarding');

// ── Utilitários ───────────────────────────────────────────────────────────────

function mostrarErro(id, mensagem) {
    const el = document.getElementById(id);
    if (el) { el.textContent = mensagem; el.hidden = false; }
}

function ocultarErro(id) {
    const el = document.getElementById(id);
    if (el) el.hidden = true;
}

function formatarCpf(v) {
    return v.replace(/\D/g, '')
        .replace(/(\d{3})(\d)/, '$1.$2')
        .replace(/(\d{3})(\d)/, '$1.$2')
        .replace(/(\d{3})(\d{1,2})$/, '$1-$2')
        .slice(0, 14);
}

function formatarCnpj(v) {
    return v.replace(/\D/g, '')
        .replace(/(\d{2})(\d)/, '$1.$2')
        .replace(/(\d{3})(\d)/, '$1.$2')
        .replace(/(\d{3})(\d)/, '$1/$2')
        .replace(/(\d{4})(\d{1,2})$/, '$1-$2')
        .slice(0, 18);
}

function formatarCep(v) {
    return v.replace(/\D/g, '')
        .replace(/(\d{5})(\d{1,3})$/, '$1-$2')
        .slice(0, 9);
}

function formatarTelefone(v) {
    const d = v.replace(/\D/g, '').slice(0, 11);
    if (d.length <= 10)
        return d.replace(/(\d{2})(\d{4})(\d{0,4})/, '($1) $2-$3').trim().replace(/-$/, '');
    return d.replace(/(\d{2})(\d{5})(\d{0,4})/, '($1) $2-$3').trim().replace(/-$/, '');
}

// ── Barra de progresso ────────────────────────────────────────────────────────

function progresso(etapaAtual, total) {
    return Array.from({ length: total }, (_, i) => {
        const n = i + 1;
        const cls = n < etapaAtual
            ? 'onboarding__passo--concluido'
            : n === etapaAtual
                ? 'onboarding__passo--ativo'
                : '';
        const label = n < etapaAtual ? '✓' : n;
        const linha = n < total
            ? el('span', { class: `onboarding__passo-linha ${n < etapaAtual ? 'onboarding__passo-linha--ativa' : ''}` })
            : null;
        return [el('span', { class: `onboarding__passo ${cls}` }, label), linha];
    });
}

// ── Montagem das etapas ───────────────────────────────────────────────────────
// Os formulários são montados via DOM (utils/dom.js): o que o usuário já
// digitou (estado) volta aos campos como atributo `value`, sem nunca passar
// pelo parser de HTML.

function exibirEtapa(etapaAtual, totalEtapas, titulo, subtitulo, form) {
    container().replaceChildren(el('div', { class: 'onboarding' },
        el('div', { class: 'onboarding__progresso' }, progresso(etapaAtual, totalEtapas)),
        el('h2', { class: 'onboarding__title' }, titulo),
        el('p', { class: 'onboarding__subtitle' }, subtitulo),
        form));
}

// Grupo rótulo + input. `opcional` acrescenta o selo "(opcional)" ao rótulo;
// `classe` (modificador) e `style` vão para o wrapper do grupo.
function campo(id, rotulo, attrsInput, { opcional = false, classe, style } = {}) {
    return el('div', { class: classe ? `onboarding__group ${classe}` : 'onboarding__group', style },
        el('label', { for: id },
            opcional ? `${rotulo} ` : rotulo,
            opcional && el('span', { class: 'onboarding__opcional' }, '(opcional)')),
        el('input', { id, ...attrsInput }));
}

function areaErro(id) {
    return el('p', { id, class: 'onboarding__erro', hidden: true });
}

function acoes(idVoltar, textoAvancar = 'Continuar', idAvancar) {
    return el('div', { class: 'onboarding__acoes' },
        el('button', { type: 'button', id: idVoltar, class: 'onboarding__btn-secundario' }, 'Voltar'),
        el('button', { type: 'submit', id: idAvancar, class: 'onboarding__btn-principal' }, textoAvancar));
}

// ── Etapa 1: escolha PF / PJ ──────────────────────────────────────────────────

function renderizarEtapa1() {
    container().innerHTML = `
        <div class="onboarding">
            <h2 class="onboarding__title">Complete seu perfil</h2>
            <p class="onboarding__subtitle">Você é pessoa física ou jurídica?</p>
            <div class="onboarding__opcoes">
                <button id="btn-pf" class="onboarding__opcao" type="button">
                    <span class="onboarding__opcao-icone"><i class="fa-solid fa-user"></i></span>
                    <strong>Pessoa Física</strong>
                    <small>Cadastro com CPF</small>
                </button>
                <button id="btn-pj" class="onboarding__opcao" type="button">
                    <span class="onboarding__opcao-icone"><i class="fa-solid fa-building"></i></span>
                    <strong>Pessoa Jurídica</strong>
                    <small>Cadastro com CNPJ</small>
                </button>
            </div>
        </div>
    `;
    document.getElementById('btn-pf').addEventListener('click', () => {
        estado.tipoPessoa = 'PessoaFisica';
        renderizarPF_Etapa2();
    });
    document.getElementById('btn-pj').addEventListener('click', () => {
        estado.tipoPessoa = 'PessoaJuridica';
        renderizarPJ_Etapa2();
    });
}

// ══════════════════════════════════════════════════════════════════════════════
// FLUXO PESSOA FÍSICA (3 etapas: escolha -> dados -> endereço)
// ══════════════════════════════════════════════════════════════════════════════

function renderizarPF_Etapa2() {
    exibirEtapa(2, 3, 'Seus dados', 'Dados do titular da conta.',
        el('form', { id: 'pf-form2', novalidate: true },
            campo('nomeLegal', 'Nome completo', {
                type: 'text', placeholder: 'Ex: João da Silva',
                value: estado.nomeLegal, autocomplete: 'name', required: true
            }),
            campo('cpfCnpj', 'CPF', {
                type: 'text', placeholder: '000.000.000-00',
                maxlength: 14, inputmode: 'numeric', value: estado.cpfCnpj, required: true
            }),
            campo('dataNascimento', 'Data de nascimento', {
                type: 'date', value: estado.dataNascimento, required: true
            }),
            el('div', { class: 'onboarding__group' },
                el('label', { style: 'flex-direction:row;align-items:center;gap:8px;cursor:pointer' },
                    el('input', { type: 'checkbox', id: 'exibirIdade', checked: estado.exibirIdade, style: 'width:auto;margin:0' }),
                    ' Exibir minha idade publicamente no perfil')),
            areaErro('pf-erro2'),
            acoes('btn-voltar2')));

    const inputCpf = document.getElementById('cpfCnpj');
    inputCpf.addEventListener('input', () => { inputCpf.value = formatarCpf(inputCpf.value); });

    document.getElementById('btn-voltar2').addEventListener('click', renderizarEtapa1);

    document.getElementById('pf-form2').addEventListener('submit', (e) => {
        e.preventDefault();
        ocultarErro('pf-erro2');

        const nomeLegal = document.getElementById('nomeLegal').value.trim();
        const cpf = document.getElementById('cpfCnpj').value.replace(/\D/g, '');
        const dataNascimento = document.getElementById('dataNascimento').value;
        const exibirIdade = document.getElementById('exibirIdade').checked;

        const erros = [];
        if (!nomeLegal || nomeLegal.length < 5) erros.push('Informe o nome completo (mínimo 5 caracteres).');
        if (cpf.length !== 11) erros.push('CPF inválido — informe os 11 dígitos.');
        if (!dataNascimento) erros.push('Data de nascimento é obrigatória.');

        if (erros.length > 0) { mostrarErro('pf-erro2', erros.join(' ')); return; }

        estado.nomeLegal = nomeLegal;
        estado.cpfCnpj = cpf;
        estado.dataNascimento = dataNascimento;
        estado.exibirIdade = exibirIdade;

        renderizarEndereco(renderizarPF_Etapa2, 3, 3);
    });
}

// ══════════════════════════════════════════════════════════════════════════════
// FLUXO PESSOA JURÍDICA (4 etapas: escolha -> empresa -> representante -> endereço)
// ══════════════════════════════════════════════════════════════════════════════

function renderizarPJ_Etapa2() {
    exibirEtapa(2, 4, 'Identificação da empresa', 'Dados cadastrais da sua empresa.',
        el('form', { id: 'pj-form2', novalidate: true },
            campo('razaoSocial', 'Razão Social', {
                type: 'text', placeholder: 'Ex: Empresa Comércio Ltda',
                value: estado.razaoSocial, required: true
            }),
            campo('nomeEmpresa', 'Nome fantasia', {
                type: 'text', placeholder: 'Ex: Minha Empresa', value: estado.nomeEmpresa
            }, { opcional: true }),
            campo('cnpj', 'CNPJ', {
                type: 'text', placeholder: '00.000.000/0001-00',
                maxlength: 18, inputmode: 'numeric', value: estado.cnpj, required: true
            }),
            campo('segmento', 'Segmento de atuação', {
                type: 'text', placeholder: 'Ex: Tecnologia, Construção civil...',
                value: estado.segmento, required: true
            }),
            el('div', { class: 'onboarding__row' },
                campo('inscricaoEstadual', 'Inscrição Estadual', {
                    type: 'text', placeholder: 'Ex: 123.456.789.000', value: estado.inscricaoEstadual
                }, { opcional: true, style: 'flex:1' }),
                campo('inscricaoMunicipal', 'Inscrição Municipal', {
                    type: 'text', placeholder: 'Ex: 00123456', value: estado.inscricaoMunicipal
                }, { opcional: true, style: 'flex:1' })),
            campo('dataAbertura', 'Data de abertura', {
                type: 'date', value: estado.dataAbertura
            }, { opcional: true }),
            areaErro('pj-erro2'),
            acoes('btn-voltar-pj2')));

    const inputCnpj = document.getElementById('cnpj');
    inputCnpj.addEventListener('input', () => { inputCnpj.value = formatarCnpj(inputCnpj.value); });

    document.getElementById('btn-voltar-pj2').addEventListener('click', renderizarEtapa1);

    document.getElementById('pj-form2').addEventListener('submit', (e) => {
        e.preventDefault();
        ocultarErro('pj-erro2');

        const razaoSocial = document.getElementById('razaoSocial').value.trim();
        const cnpj = document.getElementById('cnpj').value.replace(/\D/g, '');
        const nomeEmpresa = document.getElementById('nomeEmpresa').value.trim();
        const segmento = document.getElementById('segmento').value.trim();
        const inscricaoEstadual = document.getElementById('inscricaoEstadual').value.trim();
        const inscricaoMunicipal = document.getElementById('inscricaoMunicipal').value.trim();
        const dataAbertura = document.getElementById('dataAbertura').value;

        const erros = [];
        if (!razaoSocial || razaoSocial.length < 3) erros.push('Informe a razão social.');
        if (cnpj.length !== 14) erros.push('CNPJ inválido — informe os 14 dígitos.');
        if (!segmento) erros.push('Segmento de atuação é obrigatório.');

        if (erros.length > 0) { mostrarErro('pj-erro2', erros.join(' ')); return; }

        estado.razaoSocial = razaoSocial;
        estado.cpfCnpj = cnpj;
        estado.nomeEmpresa = nomeEmpresa;
        estado.segmento = segmento;
        estado.inscricaoEstadual = inscricaoEstadual;
        estado.inscricaoMunicipal = inscricaoMunicipal;
        estado.dataAbertura = dataAbertura;

        renderizarPJ_Etapa3();
    });
}

function renderizarPJ_Etapa3() {
    exibirEtapa(3, 4, 'Representante legal', 'Dados de quem representa a empresa legalmente.',
        el('form', { id: 'pj-form3', novalidate: true },
            campo('nomeRepresentante', 'Nome completo', {
                type: 'text', placeholder: 'Ex: Maria Souza',
                value: estado.nomeRepresentanteLegal, required: true
            }),
            campo('cpfRepresentante', 'CPF', {
                type: 'text', placeholder: '000.000.000-00', maxlength: 14, inputmode: 'numeric',
                value: estado.cpfRepresentanteLegal ? formatarCpf(estado.cpfRepresentanteLegal) : '',
                required: true
            }),
            campo('cargoRepresentante', 'Cargo', {
                type: 'text', placeholder: 'Ex: Sócio-Diretor, CEO...', value: estado.cargoRepresentante
            }, { opcional: true }),
            campo('emailRepresentante', 'E-mail', {
                type: 'email', placeholder: 'contato@empresa.com.br', value: estado.emailRepresentante
            }, { opcional: true }),
            campo('telefoneRepresentante', 'Telefone / WhatsApp', {
                type: 'text', placeholder: '(11) 99999-0000', maxlength: 15, inputmode: 'numeric',
                value: estado.telefoneRepresentante
            }, { opcional: true }),
            areaErro('pj-erro3'),
            acoes('btn-voltar-pj3')));

    const inputCpfRep = document.getElementById('cpfRepresentante');
    inputCpfRep.addEventListener('input', () => { inputCpfRep.value = formatarCpf(inputCpfRep.value); });

    const inputTel = document.getElementById('telefoneRepresentante');
    inputTel.addEventListener('input', () => { inputTel.value = formatarTelefone(inputTel.value); });

    document.getElementById('btn-voltar-pj3').addEventListener('click', renderizarPJ_Etapa2);

    document.getElementById('pj-form3').addEventListener('submit', (e) => {
        e.preventDefault();
        ocultarErro('pj-erro3');

        const nomeRep = document.getElementById('nomeRepresentante').value.trim();
        const cpfRep = document.getElementById('cpfRepresentante').value.replace(/\D/g, '');
        const cargo = document.getElementById('cargoRepresentante').value.trim();
        const email = document.getElementById('emailRepresentante').value.trim();
        const telefone = document.getElementById('telefoneRepresentante').value.trim();

        const erros = [];
        if (!nomeRep || nomeRep.length < 5) erros.push('Informe o nome completo do representante (mínimo 5 caracteres).');
        if (cpfRep.length !== 11) erros.push('CPF do representante inválido — informe os 11 dígitos.');

        if (erros.length > 0) { mostrarErro('pj-erro3', erros.join(' ')); return; }

        estado.nomeRepresentanteLegal = nomeRep;
        estado.cpfRepresentanteLegal = cpfRep;
        estado.cargoRepresentante = cargo;
        estado.emailRepresentante = email;
        estado.telefoneRepresentante = telefone;

        renderizarEndereco(renderizarPJ_Etapa3, 4, 4);
    });
}

// ── Etapa de endereço (compartilhada entre PF e PJ) ───────────────────────────

const ESTADOS_BR = [
    'AC','AL','AP','AM','BA','CE','DF','ES','GO','MA','MT','MS','MG',
    'PA','PB','PR','PE','PI','RJ','RN','RS','RO','RR','SC','SP','SE','TO'
];

async function buscarCep(cep) {
    const cepLimpo = cep.replace(/\D/g, '');
    if (cepLimpo.length !== 8) return null;
    try {
        const resp = await fetch(`/api/cep/${cepLimpo}`);
        if (!resp.ok) return null;
        const data = await resp.json();
        return data;
    } catch {
        return null;
    }
}

function renderizarEndereco(fnVoltar, etapaAtual, totalEtapas) {
    const isPF = estado.tipoPessoa === 'PessoaFisica';
    const labelTitulo = isPF ? 'Seu endereço' : 'Endereço da empresa';
    const labelSub = isPF ? 'Informe seu endereço.' : 'Informe o endereço da sede da empresa.';

    const opcoesEstado = ESTADOS_BR.map(uf =>
        el('option', { value: uf, selected: estado.estado === uf }, uf)
    );

    exibirEtapa(etapaAtual, totalEtapas, labelTitulo, labelSub,
        el('form', { id: 'enderecoForm', novalidate: true },
            el('div', { class: 'onboarding__group' },
                el('label', { for: 'cep' }, 'CEP'),
                el('div', { class: 'onboarding__cep-wrap' },
                    el('input', {
                        type: 'text', id: 'cep', placeholder: '00000-000', maxlength: 9, inputmode: 'numeric',
                        value: estado.cep ? formatarCep(estado.cep) : '', required: true
                    }),
                    el('span', { id: 'cep-loading', class: 'onboarding__cep-loading', hidden: true }, 'Buscando...'))),
            campo('logradouro', 'Logradouro', {
                type: 'text', placeholder: 'Ex: Rua das Flores', value: estado.logradouro, required: true
            }),
            el('div', { class: 'onboarding__row' },
                campo('numero', 'Número', {
                    type: 'text', placeholder: 'Ex: 123', value: estado.numero, required: true
                }, { classe: 'onboarding__group--numero' }),
                campo('complemento', 'Complemento', {
                    type: 'text', placeholder: 'Ex: Apto 4', value: estado.complemento
                }, { opcional: true, classe: 'onboarding__group--complemento' })),
            campo('bairro', 'Bairro', {
                type: 'text', placeholder: 'Ex: Centro', value: estado.bairro, required: true
            }),
            el('div', { class: 'onboarding__row' },
                campo('cidade', 'Cidade', {
                    type: 'text', placeholder: 'Ex: São Paulo', value: estado.cidade, required: true
                }, { classe: 'onboarding__group--cidade' }),
                el('div', { class: 'onboarding__group onboarding__group--estado' },
                    el('label', { for: 'estadoUF' }, 'Estado'),
                    el('select', { id: 'estadoUF', required: true },
                        el('option', { value: '' }, 'UF'),
                        opcoesEstado))),
            areaErro('endereco-erro'),
            acoes('btn-voltar-end', 'Finalizar cadastro', 'btn-finalizar')));

    const inputCep = document.getElementById('cep');

    const buscarCepAutomaticamente = async () => {
        const cepLimpo = inputCep.value.replace(/\D/g, '');
        if (cepLimpo.length !== 8) return;
        const loading = document.getElementById('cep-loading');
        loading.hidden = false;
        const dados = await buscarCep(cepLimpo);
        loading.hidden = true;
        if (dados) {
            document.getElementById('logradouro').value = dados.logradouro ?? '';
            document.getElementById('bairro').value = dados.bairro ?? '';
            document.getElementById('cidade').value = dados.localidade ?? '';
            const sel = document.getElementById('estadoUF');
            for (const opt of sel.options) {
                if (opt.value === (dados.uf ?? '')) { opt.selected = true; break; }
            }
            document.getElementById('numero').focus();
        }
    };

    inputCep.addEventListener('input', () => {
        inputCep.value = formatarCep(inputCep.value);
        buscarCepAutomaticamente();
    });

    inputCep.addEventListener('blur', buscarCepAutomaticamente);

    document.getElementById('btn-voltar-end').addEventListener('click', fnVoltar);

    document.getElementById('enderecoForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        ocultarErro('endereco-erro');

        const cep = document.getElementById('cep').value.replace(/\D/g, '');
        const logradouro = document.getElementById('logradouro').value.trim();
        const numero = document.getElementById('numero').value.trim();
        const complemento = document.getElementById('complemento').value.trim();
        const bairro = document.getElementById('bairro').value.trim();
        const cidade = document.getElementById('cidade').value.trim();
        const uf = document.getElementById('estadoUF').value;

        const erros = [];
        if (cep.length !== 8) erros.push('CEP inválido.');
        if (!logradouro) erros.push('Informe o logradouro.');
        if (!numero) erros.push('Informe o número.');
        if (!bairro) erros.push('Informe o bairro.');
        if (!cidade) erros.push('Informe a cidade.');
        if (!uf) erros.push('Selecione o estado.');

        if (erros.length > 0) { mostrarErro('endereco-erro', erros.join(' ')); return; }

        estado.cep = cep;
        estado.logradouro = logradouro;
        estado.numero = numero;
        estado.complemento = complemento;
        estado.bairro = bairro;
        estado.cidade = cidade;
        estado.estado = uf;

        const btn = document.getElementById('btn-finalizar');
        btn.disabled = true;
        btn.textContent = 'Salvando...';

        const isPF = estado.tipoPessoa === 'PessoaFisica';

        try {
            await api.post('/usuarios/onboarding', {
                tipoPessoa: estado.tipoPessoa,
                // PF
                cpfCnpj: estado.cpfCnpj,
                nomeLegal: isPF ? estado.nomeLegal : estado.razaoSocial,
                dataNascimento: isPF ? estado.dataNascimento : null,
                exibirIdade: isPF ? estado.exibirIdade : false,
                // PJ
                nomeEmpresa: estado.nomeEmpresa || null,
                segmento: estado.segmento || null,
                inscricaoEstadual: estado.inscricaoEstadual || null,
                inscricaoMunicipal: estado.inscricaoMunicipal || null,
                dataAbertura: estado.dataAbertura || null,
                nomeRepresentanteLegal: estado.nomeRepresentanteLegal || null,
                cpfRepresentanteLegal: estado.cpfRepresentanteLegal || null,
                cargoRepresentante: estado.cargoRepresentante || null,
                emailRepresentante: estado.emailRepresentante || null,
                telefoneRepresentante: estado.telefoneRepresentante || null,
                // Endereço
                cep: estado.cep,
                logradouro: estado.logradouro,
                numero: estado.numero,
                complemento: estado.complemento || null,
                bairro: estado.bairro,
                cidade: estado.cidade,
                estado: estado.estado
            });
            renderizarSucesso();
        } catch (err) {
            const msg = err?.data?.mensagem
                ?? err?.data?.message
                ?? 'Erro ao salvar. Verifique os dados e tente novamente.';
            mostrarErro('endereco-erro', msg);
            btn.disabled = false;
            btn.textContent = 'Finalizar cadastro';
        }
    });
}

// ── Sucesso ───────────────────────────────────────────────────────────────────

async function renderizarSucesso() {
    let destino = '/';
    try {
        const resp = await fetch('/api/me', { credentials: 'same-origin' });
        if (resp.ok) {
            const me = await resp.json();
            destino = me.tipo === 'Contratante'
                ? '/pages/contratante/meus-projetos.html'
                : '/pages/projetos/index.html';
        }
    } catch { /* fallback para '/' */ }

    container().innerHTML = `
        <div class="onboarding">
            <div class="onboarding__sucesso">
                <p class="onboarding__sucesso-icone">&#10003;</p>
                <h2 class="onboarding__sucesso-titulo">Tudo pronto!</h2>
                <p class="onboarding__sucesso-texto">
                    Seu perfil está completo. Você já pode usar todas as funcionalidades da Tratoo.
                </p>
                <a href="${destino}" class="onboarding__btn-principal onboarding__btn-home">Ir para a plataforma</a>
            </div>
        </div>
    `;
}

// ── Inicialização ─────────────────────────────────────────────────────────────
// Verifica se o usuário já concluiu o onboarding. Se sim, redireciona para home.
// Se não autenticado, redireciona para login.
(async function inicializar() {
    try {
        const resp = await fetch('/api/me', { credentials: 'same-origin' });
        if (resp.status === 401) {
            window.location.replace('/pages/auth/login.html');
            return;
        }
        const me = await resp.json().catch(() => ({}));
        if (me.perfilCompleto === true) {
            const destino = me.tipo === 'Contratante'
                ? '/pages/contratante/meus-projetos.html'
                : '/pages/projetos/index.html';
            window.location.replace(destino);
            return;
        }
        renderizarEtapa1();
    } catch {
        window.location.replace('/pages/auth/login.html');
    }
})();
