# Rotação de chaves e segredos

Roteiro único de troca de todos os segredos do Tratoo: o que cada um protege, como trocar sem
indisponibilidade, como conferir e como voltar atrás. Substitui o antigo
`ROTACAO-CHAVE-DADOS.md`.

**Regra geral:** nenhum valor de chave aparece em log, health check, mensagem de erro ou neste
documento. Os comandos abaixo nunca imprimem uma chave: só tamanhos e comparações IGUAL/DIFERENTE.

## Resumo

| Segredo | Variável | Troca sem indisponibilidade? | Seção |
|---|---|---|---|
| Chave de dados (conta, PIX, CPF/CNPJ) | `DataProtector__Chave` | Sim, com `DataProtector__ChaveAnterior` + recifragem | [1](#1-chave-de-dados-dataprotector) |
| Chave do JWT de acesso | `Jwt__SecretKey` | Sim, com `Jwt__SecretKeyAnterior` | [2](#2-chave-do-jwt) |
| Token do webhook do Asaas | `Asaas__WebhookToken` | Sim, com `Asaas__WebhookTokenAnterior` | [3](#3-token-do-webhook-do-asaas) |
| API do Asaas | `Asaas__ApiKey` | Depende do painel do Asaas | [4](#4-credenciais-de-terceiros-asaas-resend-openai) |
| API do Resend | `Resend__ApiKey` | Sim (o Resend aceita várias chaves) | [4](#4-credenciais-de-terceiros-asaas-resend-openai) |
| API da OpenAI | `OpenAI__ApiKey` | Sim (a OpenAI aceita várias chaves) | [4](#4-credenciais-de-terceiros-asaas-resend-openai) |
| Proteção do Redis | `Redis__ChaveProtecao` | **Não**: desloga todo mundo | [5](#5-chave-de-proteção-do-redis) |

Faça sempre **primeiro na homologação** (`staging`) e só depois em produção.

## 0. Preparação (uma vez por máquina)

1. Instalar e autenticar a CLI do Railway:
   ```powershell
   npm install -g @railway/cli
   railway login
   ```
2. Na pasta do repositório, vincular ao serviço da API do ambiente desejado (pergunta projeto,
   ambiente e serviço):
   ```powershell
   railway link
   ```
   Para trocar de ambiente depois, rode `railway link` de novo. **Confira o ambiente** antes de
   cada passo: a primeira linha da recifragem mostra o banco (`tratoo_staging` ou o de produção).

3. Função para gravar uma variável com os bytes exatos. O stdin do PowerShell 5.1 acrescenta BOM e
   quebra de linha, que mudariam a chave:
   ```powershell
   function Set-RailwayVar([string]$nome, [string]$valor, [switch]$SemDeploy) {
     $exe = Join-Path $env:APPDATA 'npm\node_modules\@railway\cli\bin\railway.exe'
     $anterior = [Console]::InputEncoding
     try {
       [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)   # sem BOM
       $psi = New-Object System.Diagnostics.ProcessStartInfo $exe
       $psi.Arguments = "variable set $nome --stdin" + $(if ($SemDeploy) { ' --skip-deploys' } else { '' })
       $psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true
       $p = [Diagnostics.Process]::Start($psi)
       $p.StandardInput.Write($valor); $p.StandardInput.Close(); $p.WaitForExit()
       if ($p.ExitCode -ne 0) { throw "railway variable set $nome falhou ($($p.ExitCode))" }
     } finally { [Console]::InputEncoding = $anterior }
   }
   ```

4. Função para gerar uma chave aleatória (base64, sem imprimir):
   ```powershell
   function New-Chave([int]$bytes = 32) {
     $b = New-Object byte[] $bytes
     [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
     [Convert]::ToBase64String($b)
   }
   ```
   Guarde cada chave nova num cofre de senhas: é a única cópia fora do Railway.

## 1. Chave de dados (`DataProtector`)

Cifra conta bancária, chave PIX, CPF/CNPJ e CPF do representante legal (colunas
`ContasBancarias.PixChave`, `ContasBancarias.ContaCriptografada`,
`UserIdentities.CpfCnpjCriptografado`, `UserIdentities.ChavePixCriptografada` e
`UserIdentities.CpfRepresentanteLegalCriptografado`). Trocar a variável sozinha torna esses dados
ilegíveis: a troca é feita com duas chaves e uma recifragem.

### Como funciona

| Variável | Papel |
|---|---|
| `DataProtector__Chave` | Chave **atual**: tudo é gravado com ela. Numa rotação, é a **nova** e precisa ter **32+ bytes** |
| `DataProtector__ChaveAnterior` | Só durante uma rotação: a antiga, usada só para **ler** o que ainda não foi recifrado |
| `DataProtector__RecifrarAutomaticamente` | `true` liga a recifragem automática (padrão: desligada) |
| `DataProtector__RecifragemIntervaloMinutos` | Intervalo entre rodadas automáticas (padrão 60, mínimo 5) |

- **Formato:** os valores são gravados em `v2:` (AES-256-GCM, autenticado). Valores antigos em
  AES-CBC continuam sendo lidos até a recifragem convertê-los.
- **Leitura:** tenta a chave atual; numa rotação, também a anterior.
- **Subida:** a API não sobe sem a chave, com o placeholder, com a anterior igual à atual ou,
  numa rotação, com a chave nova abaixo de 32 bytes.
- **`/health/ready`:**
  - `dados-cifrados`: `Unhealthy` (barra o deploy) se a chave não decifra os dados existentes;
    `Degraded` enquanto houver amostras na chave anterior;
  - `recifragem`: estado da recifragem automática (ver [6](#6-recifragem-automática-e-monitoramento)).

### Recifragem: o comando

Roda dentro do contêiner, com as variáveis do serviço:

```powershell
railway ssh "cd /app && dotnet Tratoo.API.dll --recifrar-dados"            # só relatório
railway ssh "cd /app && dotnet Tratoo.API.dll --recifrar-dados --aplicar"  # grava
```

- Converte para `v2`, na chave atual, os valores na chave anterior **e** os valores em AES-CBC.
  Sem `ChaveAnterior`, faz só a migração de formato.
- Uma única transação; se um valor mudar no banco durante a execução, desfaz tudo (é só rodar
  de novo). Confere cada valor novo antes de gravar e relê o banco no fim. Idempotente.
- Saída: uma linha por coluna com `atual`, `formato antigo`, `anterior`, `recifrados`,
  `ilegíveis`, `ambíguos` e `não-cifrados`. Só contagens.
- Códigos de saída:

| Código | Significado |
|---|---|
| 0 | Concluído (ou relatório sem erro) |
| 1 | Falhou e foi desfeito; nada mudou |
| 2 | Configuração ausente (conexão ou chave) |
| 3 | Concluído, mas sobraram valores ilegíveis ou ambíguos: **não** remova a chave anterior |

### Roteiro de rotação

**Antes:** crie um snapshot (ou branch) do banco no Neon.

1. **Ler a chave atual e gerar a nova** (na mesma sessão do PowerShell):
   ```powershell
   $antiga = [string](railway variables --json | ConvertFrom-Json).DataProtector__Chave
   $nova = New-Chave 32
   "antiga: $($antiga.Length) caracteres; nova: $($nova.Length) caracteres"
   ```

2. **Configurar as duas chaves.** A anterior primeiro, sem deploy; a nova em seguida dispara
   **um** deploy com as duas:
   ```powershell
   Set-RailwayVar DataProtector__ChaveAnterior $antiga -SemDeploy
   Set-RailwayVar DataProtector__Chave $nova
   ```
   Se o deploy subisse só com a nova, o `dados-cifrados` ficaria `Unhealthy` e a versão não seria
   promovida: a ordem acima evita isso.

3. **Conferir as variáveis** (imprime só IGUAL/DIFERENTE):
   ```powershell
   $v = railway variables --json | ConvertFrom-Json
   "Chave = nova: "           + [string]::Equals([string]$v.DataProtector__Chave, $nova, 'Ordinal')
   "ChaveAnterior = antiga: " + [string]::Equals([string]$v.DataProtector__ChaveAnterior, $antiga, 'Ordinal')
   ```
   Use sempre comparação `Ordinal`: `-eq`/`-ceq` ignoram BOM e dariam falso "igual".

4. **Esperar o deploy** e abrir `/health/ready`: `dados-cifrados` `Degraded` com "ainda na chave
   anterior" (ou `Healthy`, se não houver dado cifrado).

5. **Recifrar**, de um destes jeitos:
   - **Manual:** o relatório e depois `--aplicar` (comandos acima). Esperado no segundo:
     *"Recifragem concluída: todos os valores cifrados estão na chave atual e no formato v2.
     DataProtector__ChaveAnterior já pode sair da configuração."* e código 0.
   - **Automática:** `Set-RailwayVar DataProtector__RecifrarAutomaticamente true` (dispara um
     deploy). A primeira rodada começa cerca de 2 min depois da subida. Esperado no
     `/health/ready`, check `recifragem`: *"Concluída em …: tudo em v2 na chave atual.
     DataProtector__ChaveAnterior já pode ser removida."*

   Código 3, ou `recifragem` `Degraded` com valores ilegíveis: **pare** e investigue antes do
   passo 6.

6. **Retirar a chave anterior** (dispara um deploy):
   ```powershell
   railway variable delete DataProtector__ChaveAnterior
   ```
   Conferir `/health/ready`: `dados-cifrados` `Healthy`, "N de N amostras decifradas".

7. **Descartar a chave antiga:** apagar cópias locais e backups que a contenham.

### Voltar atrás

- **Antes do passo 5:** restaurar `DataProtector__Chave` com a antiga e apagar
  `DataProtector__ChaveAnterior`. Nada foi regravado, mas o que a API cifrou com a nova nesse
  intervalo fica ilegível. Por isso, prefira a opção seguinte.
- **Depois do passo 5:** a rotação é simétrica. Inverter as variáveis (`Chave` = antiga,
  `ChaveAnterior` = nova) e recifrar de novo.
- **Versão da aplicação:** **nunca** volte a produção para uma versão anterior ao PR #42 (leitura
  de `v2`): ela não lê o que é gravado hoje. Qualquer versão a partir dele é segura.
- **Último recurso:** restaurar o snapshot do Neon tirado antes da rotação.

## 2. Chave do JWT

O access token (cookie `tratoo_auth`, 15 min; 10 para Admin) é assinado com HMAC-SHA256. A sessão
vive no refresh token, que é opaco, fica no Redis e **não depende** desta chave.

| Variável | Papel |
|---|---|
| `Jwt__SecretKey` | Assina e valida. 32+ bytes; a API não sobe sem ela ou com ela curta |
| `Jwt__SecretKeyAnterior` | Só durante uma troca: só **valida**. 32+ bytes, diferente da atual |

Cada token leva no cabeçalho o `kid` da chave que o assinou (um HMAC da chave, que não a revela).

**Trocar a chave não desloga ninguém.** Sem a anterior, cada cliente ativo leva um 401, o
frontend faz refresh e segue na mesma sessão. A anterior evita essa rajada de refreshes e o vai e
volta entre réplicas durante o deploy.

### Roteiro

1. Gerar e configurar (a anterior primeiro, sem deploy):
   ```powershell
   $antiga = [string](railway variables --json | ConvertFrom-Json).Jwt__SecretKey
   $nova = New-Chave 48
   Set-RailwayVar Jwt__SecretKeyAnterior $antiga -SemDeploy
   Set-RailwayVar Jwt__SecretKey $nova
   ```
2. Conferir com `[string]::Equals(..., 'Ordinal')`, como no passo 3 da seção 1. O log de subida
   mostra *"Troca da chave do JWT em curso"*.
3. Esperar **15+ minutos depois do deploy** (validade do access) e retirar a anterior:
   ```powershell
   railway variable delete Jwt__SecretKeyAnterior
   ```

**Voltar atrás:** inverter as variáveis (`SecretKey` = antiga, `SecretKeyAnterior` = nova).
Mesmo sem isso, ninguém perde a sessão.

## 3. Token do webhook do Asaas

O Asaas envia o token no header `asaas-access-token`; o endpoint compara em tempo constante.

| Variável | Papel |
|---|---|
| `Asaas__WebhookToken` | Token atual. **Obrigatório fora de Development** (16+ caracteres): sem ele a API não sobe |
| `Asaas__WebhookTokenAnterior` | Só durante uma troca: também aceito |

### Roteiro

1. Gerar e configurar os dois tokens (anterior primeiro, sem deploy):
   ```powershell
   $antigo = [string](railway variables --json | ConvertFrom-Json).Asaas__WebhookToken
   $novo = New-Chave 32
   Set-RailwayVar Asaas__WebhookTokenAnterior $antigo -SemDeploy
   Set-RailwayVar Asaas__WebhookToken $novo
   ```
2. Depois do deploy, colar o token **novo** no painel do Asaas (Webhooks → Token de autenticação) e
   salvar. Nesse intervalo os dois tokens valem, então nenhum evento é recusado.
3. Conferir no painel do Asaas que os eventos seguintes foram entregues (sem 401) e retirar o
   anterior:
   ```powershell
   railway variable delete Asaas__WebhookTokenAnterior
   ```

**Voltar atrás:** voltar o token antigo no painel; enquanto o anterior estiver configurado, os
dois valem.

## 4. Credenciais de terceiros (Asaas, Resend, OpenAI)

A aplicação lê uma única chave de cada provedor, na subida. A troca segue o procedimento do
provedor, na ordem **criar a nova → configurar → conferir → revogar a antiga**:

| Provedor | Variável | O que acontece no intervalo | Conferência |
|---|---|---|---|
| Asaas (API) | `Asaas__ApiKey` | Se o painel invalidar a antiga ao gerar a nova, cobranças e transferências falham até o deploy terminar: faça em horário de pouco movimento | Uma cobrança de teste na homologação; logs sem 401 do Asaas |
| Resend | `Resend__ApiKey` (ou `RESEND_API_KEY`, que tem precedência) | Nada: a antiga continua válida até ser revogada | Um e-mail de teste (ex.: "esqueci a senha") |
| OpenAI | `OpenAI__ApiKey` | Nada: a antiga continua válida até ser revogada. Sem chave, a busca usa só palavras-chave | Uma busca semântica; logs sem "AI Service indisponível" |

```powershell
# Cola a chave sem exibi-la (funciona no PowerShell 5.1 e 7).
$s = Read-Host -AsSecureString "Nova chave"
$k = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
Set-RailwayVar Resend__ApiKey $k
Remove-Variable k, s
```

(Para Asaas e OpenAI, troque o nome da variável.) Depois de conferir, revogue a chave antiga no
painel do provedor.

## 5. Chave de proteção do Redis

`Redis__ChaveProtecao` (32+ bytes em base64, igual em todas as réplicas) cifra e dá nome a tudo o
que o Tratoo grava no Redis: códigos MFA/OTP, cadastros pendentes, contadores de tentativas e de rate limit e as
**sessões**. Não há chave anterior: o que foi gravado com a antiga passa a ser tratado como
ausente.

**Impacto da troca:** todos os usuários são deslogados, códigos enviados e cadastros pendentes
deixam de valer (é preciso pedir outro), e os limites de tentativas recomeçam.

**Quando trocar:** só se houver suspeita de vazamento, em horário de pouco movimento:
```powershell
Set-RailwayVar Redis__ChaveProtecao (New-Chave 32)
```
Não há como voltar atrás sem novo logout geral.

## 6. Recifragem automática e monitoramento

### Na API

Com `DataProtector__RecifrarAutomaticamente=true`, o job `RecifragemAutomaticaService` roda a
cada `DataProtector__RecifragemIntervaloMinutos` (padrão 60 min; primeira rodada ~2 min depois da
subida):

- só uma réplica executa (advisory lock do Postgres);
- relatório → recifragem numa transação → releitura completa;
- se falhar ou for interrompida, a transação é desfeita e a rodada seguinte tenta de novo;
- **nunca** remove a chave anterior.

Check `recifragem` do `/health/ready` (nunca derruba o deploy):

| Estado | Significado |
|---|---|
| *Recifragem automática desligada.* | Variável ausente ou `false` |
| *… nenhuma rodada nesta réplica ainda.* | Ligada; a rodada roda em outra réplica ou ainda não começou |
| *Concluída em …: tudo em v2 na chave atual.* | Nada pendente. Com chave anterior: *"… já pode ser removida."* |
| `Degraded` *Última rodada falhou …* | Desfeita; nova tentativa na próxima rodada |
| `Degraded` *N valor(es) não decifram …* | Valor ilegível: investigue antes de remover a chave anterior |

Pode ficar ligada: depois de concluir, cada rodada só confere e não grava nada.

### No GitHub Actions

O workflow **Monitor da recifragem** (`.github/workflows/monitor-recifragem.yml`) roda a cada 2 h
e pode ser disparado em **Actions → Monitor da recifragem → Run workflow**. Só lê o
`/health/ready` público; não tem nenhum segredo.

- URLs nas variáveis do repositório (Settings → Secrets and variables → Actions → **Variables**):
  `URL_PRODUCAO` e `URL_STAGING`. Sem a variável, o ambiente é pulado.
- **Falha** se a recifragem falhou, sobrou pendência ou valor ilegível, ou se a chave de dados não
  decifra os dados.
- **Aviso** enquanto houver rotação em curso.
- **Nota** quando a chave anterior pode sair. A remoção é sempre manual.

## 7. Ambiente local (compose)

| `.env` | Variável |
|---|---|
| `DATAPROTECTOR_CHAVE` / `DATAPROTECTOR_CHAVE_ANTERIOR` | `DataProtector__Chave` / `DataProtector__ChaveAnterior` |
| `JWT_SECRET_KEY` / `JWT_SECRET_KEY_ANTERIOR` | `Jwt__SecretKey` / `Jwt__SecretKeyAnterior` |
| `ASAAS_WEBHOOK_TOKEN` | `Asaas__WebhookToken` (16+ caracteres) |
| `REDIS_CHAVE_PROTECAO` | `Redis__ChaveProtecao` |

Vazias, as variáveis `*_ANTERIOR` significam "sem troca". A recifragem roda com
`docker compose run --rm api --recifrar-dados` (e `--recifrar-dados --aplicar`), com a stack no
ar; o `ENTRYPOINT` da imagem já é `dotnet Tratoo.API.dll`.
