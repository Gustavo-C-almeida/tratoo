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
        const label = n < etapaAtual ? '&#10003;' : n;
        const linha = n < total
            ? `<span class="onboarding__passo-linha ${n < etapaAtual ? 'onboarding__passo-linha--ativa' : ''}"></span>`
            : '';
        return `<span class="onboarding__passo ${cls}">${label}</span>${linha}`;
    }).join('');
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
    container().innerHTML = `
        <div class="onboarding">
            <div class="onboarding__progresso">${progresso(2, 3)}</div>
            <h2 class="onboarding__title">Seus dados</h2>
            <p class="onboarding__subtitle">Dados do titular da conta.</p>
            <form id="pf-form2" novalidate>
                <div class="onboarding__group">
                    <label for="nomeLegal">Nome completo</label>
                    <input type="text" id="nomeLegal" placeholder="Ex: João da Silva"
                        value="${estado.nomeLegal}" autocomplete="name" required>
                </div>
                <div class="onboarding__group">
                    <label for="cpfCnpj">CPF</label>
                    <input type="text" id="cpfCnpj" placeholder="000.000.000-00"
                        maxlength="14" inputmode="numeric" value="${estado.cpfCnpj}" required>
                </div>
                <div class="onboarding__group">
                    <label for="dataNascimento">Data de nascimento</label>
                    <input type="date" id="dataNascimento"
                        value="${estado.dataNascimento}" required>
                </div>
                <div class="onboarding__group">
                    <label style="flex-direction:row;align-items:center;gap:8px;cursor:pointer">
                        <input type="checkbox" id="exibirIdade" ${estado.exibirIdade ? 'checked' : ''}
                            style="width:auto;margin:0">
                        Exibir minha idade publicamente no perfil
                    </label>
                </div>
                <p id="pf-erro2" class="onboarding__erro" hidden></p>
                <div class="onboarding__acoes">
                    <button type="button" id="btn-voltar2" class="onboarding__btn-secundario">Voltar</button>
                    <button type="submit" class="onboarding__btn-principal">Continuar</button>
                </div>
            </form>
        </div>
    `;

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
    container().innerHTML = `
        <div class="onboarding">
            <div class="onboarding__progresso">${progresso(2, 4)}</div>
            <h2 class="onboarding__title">Identificação da empresa</h2>
            <p class="onboarding__subtitle">Dados cadastrais da sua empresa.</p>
            <form id="pj-form2" novalidate>
                <div class="onboarding__group">
                    <label for="razaoSocial">Razão Social</label>
                    <input type="text" id="razaoSocial" placeholder="Ex: Empresa Comércio Ltda"
                        value="${estado.razaoSocial}" required>
                </div>
                <div class="onboarding__group">
                    <label for="nomeEmpresa">Nome fantasia <span class="onboarding__opcional">(opcional)</span></label>
                    <input type="text" id="nomeEmpresa" placeholder="Ex: Minha Empresa"
                        value="${estado.nomeEmpresa}">
                </div>
                <div class="onboarding__group">
                    <label for="cnpj">CNPJ</label>
                    <input type="text" id="cnpj" placeholder="00.000.000/0001-00"
                        maxlength="18" inputmode="numeric" value="${estado.cnpj}" required>
                </div>
                <div class="onboarding__group">
                    <label for="segmento">Segmento de atuação</label>
                    <input type="text" id="segmento" placeholder="Ex: Tecnologia, Construção civil..."
                        value="${estado.segmento}" required>
                </div>
                <div class="onboarding__row">
                    <div class="onboarding__group" style="flex:1">
                        <label for="inscricaoEstadual">Inscrição Estadual <span class="onboarding__opcional">(opcional)</span></label>
                        <input type="text" id="inscricaoEstadual" placeholder="Ex: 123.456.789.000"
                            value="${estado.inscricaoEstadual}">
                    </div>
                    <div class="onboarding__group" style="flex:1">
                        <label for="inscricaoMunicipal">Inscrição Municipal <span class="onboarding__opcional">(opcional)</span></label>
                        <input type="text" id="inscricaoMunicipal" placeholder="Ex: 00123456"
                            value="${estado.inscricaoMunicipal}">
                    </div>
                </div>
                <div class="onboarding__group">
                    <label for="dataAbertura">Data de abertura <span class="onboarding__opcional">(opcional)</span></label>
                    <input type="date" id="dataAbertura" value="${estado.dataAbertura}">
                </div>
                <p id="pj-erro2" class="onboarding__erro" hidden></p>
                <div class="onboarding__acoes">
                    <button type="button" id="btn-voltar-pj2" class="onboarding__btn-secundario">Voltar</button>
                    <button type="submit" class="onboarding__btn-principal">Continuar</button>
                </div>
            </form>
        </div>
    `;

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
    container().innerHTML = `
        <div class="onboarding">
            <div class="onboarding__progresso">${progresso(3, 4)}</div>
            <h2 class="onboarding__title">Representante legal</h2>
            <p class="onboarding__subtitle">Dados de quem representa a empresa legalmente.</p>
            <form id="pj-form3" novalidate>
                <div class="onboarding__group">
                    <label for="nomeRepresentante">Nome completo</label>
                    <input type="text" id="nomeRepresentante" placeholder="Ex: Maria Souza"
                        value="${estado.nomeRepresentanteLegal}" required>
                </div>
                <div class="onboarding__group">
                    <label for="cpfRepresentante">CPF</label>
                    <input type="text" id="cpfRepresentante" placeholder="000.000.000-00"
                        maxlength="14" inputmode="numeric"
                        value="${estado.cpfRepresentanteLegal ? formatarCpf(estado.cpfRepresentanteLegal) : ''}" required>
                </div>
                <div class="onboarding__group">
                    <label for="cargoRepresentante">Cargo <span class="onboarding__opcional">(opcional)</span></label>
                    <input type="text" id="cargoRepresentante" placeholder="Ex: Sócio-Diretor, CEO..."
                        value="${estado.cargoRepresentante}">
                </div>
                <div class="onboarding__group">
                    <label for="emailRepresentante">E-mail <span class="onboarding__opcional">(opcional)</span></label>
                    <input type="email" id="emailRepresentante" placeholder="contato@empresa.com.br"
                        value="${estado.emailRepresentante}">
                </div>
                <div class="onboarding__group">
                    <label for="telefoneRepresentante">Telefone / WhatsApp <span class="onboarding__opcional">(opcional)</span></label>
                    <input type="text" id="telefoneRepresentante" placeholder="(11) 99999-0000"
                        maxlength="15" inputmode="numeric"
                        value="${estado.telefoneRepresentante}">
                </div>
                <p id="pj-erro3" class="onboarding__erro" hidden></p>
                <div class="onboarding__acoes">
                    <button type="button" id="btn-voltar-pj3" class="onboarding__btn-secundario">Voltar</button>
                    <button type="submit" class="onboarding__btn-principal">Continuar</button>
                </div>
            </form>
        </div>
    `;

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
        `<option value="${uf}" ${estado.estado === uf ? 'selected' : ''}>${uf}</option>`
    ).join('');

    container().innerHTML = `
        <div class="onboarding">
            <div class="onboarding__progresso">${progresso(etapaAtual, totalEtapas)}</div>
            <h2 class="onboarding__title">${labelTitulo}</h2>
            <p class="onboarding__subtitle">${labelSub}</p>
            <form id="enderecoForm" novalidate>
                <div class="onboarding__group">
                    <label for="cep">CEP</label>
                    <div class="onboarding__cep-wrap">
                        <input type="text" id="cep" placeholder="00000-000"
                            maxlength="9" inputmode="numeric"
                            value="${estado.cep ? formatarCep(estado.cep) : ''}" required>
                        <span id="cep-loading" class="onboarding__cep-loading" hidden>Buscando...</span>
                    </div>
                </div>
                <div class="onboarding__group">
                    <label for="logradouro">Logradouro</label>
                    <input type="text" id="logradouro" placeholder="Ex: Rua das Flores"
                        value="${estado.logradouro}" required>
                </div>
                <div class="onboarding__row">
                    <div class="onboarding__group onboarding__group--numero">
                        <label for="numero">Número</label>
                        <input type="text" id="numero" placeholder="Ex: 123"
                            value="${estado.numero}" required>
                    </div>
                    <div class="onboarding__group onboarding__group--complemento">
                        <label for="complemento">Complemento <span class="onboarding__opcional">(opcional)</span></label>
                        <input type="text" id="complemento" placeholder="Ex: Apto 4"
                            value="${estado.complemento}">
                    </div>
                </div>
                <div class="onboarding__group">
                    <label for="bairro">Bairro</label>
                    <input type="text" id="bairro" placeholder="Ex: Centro"
                        value="${estado.bairro}" required>
                </div>
                <div class="onboarding__row">
                    <div class="onboarding__group onboarding__group--cidade">
                        <label for="cidade">Cidade</label>
                        <input type="text" id="cidade" placeholder="Ex: São Paulo"
                            value="${estado.cidade}" required>
                    </div>
                    <div class="onboarding__group onboarding__group--estado">
                        <label for="estadoUF">Estado</label>
                        <select id="estadoUF" required>
                            <option value="">UF</option>
                            ${opcoesEstado}
                        </select>
                    </div>
                </div>
                <p id="endereco-erro" class="onboarding__erro" hidden></p>
                <div class="onboarding__acoes">
                    <button type="button" id="btn-voltar-end" class="onboarding__btn-secundario">Voltar</button>
                    <button type="submit" id="btn-finalizar" class="onboarding__btn-principal">Finalizar cadastro</button>
                </div>
            </form>
        </div>
    `;

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
