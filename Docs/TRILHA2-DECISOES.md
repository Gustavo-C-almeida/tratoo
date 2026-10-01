# Trilha 2 — Docker + Redis: diagnóstico e decisões

**2026-09-29.** Diagnóstico feito sobre o código e a infraestrutura reais (Railway CLI +
`railway ssh`, somente leitura). Nenhum código foi alterado nesta etapa. Este documento é a
entrada dos prompts 2 (compose), 3 (migrations) e 4 (Redis + réplicas).

Legenda de evidência usada em todo o documento:

- **[C] comprovado** — observado no código, na infra ou em execução.
- **[I] inferido** — dedução técnica a partir de evidência, sem execução direta.
- **[D] a decidir** — escolha do responsável pelo projeto; há recomendação.

---

## 0. Resumo executivo

1. **O estado que quebra com 2 réplicas é pequeno e bem delimitado:** 8 chaves de
   `IMemoryCache` ligadas a OTP/tokens/cadastro pendente + o rate limiter. Tudo o que é
   financeiro, contratual ou de auditoria **já está no Postgres** — nada disso vai para o Redis.
2. **O `PagamentoLiberacaoService` já é seguro com N réplicas** contra transferência PIX
   duplicada: a liberação faz uma reivindicação atômica no banco
   (`UPDATE ... WHERE Status = 'Retido'`) antes de chamar o Asaas. Os três jobs de expiração
   **não têm trava**, mas têm efeito praticamente idempotente. A trava de "um job por vez"
   deve ser **`pg_try_advisory_xact_lock` no Postgres**, não no Redis.
3. **Redis em produção hoje (1 réplica) é custo, não benefício.** Recomendação: Redis só no
   compose, atrás de uma abstração com fallback em memória; Redis na Railway só quando
   `numReplicas > 1`. **[D]**
4. **Com Nginx no compose, o gate de proxy confiável descarta o `X-Forwarded-For`** (o peer
   `172.x` não está na lista padrão) e todos os usuários colapsam no IP do Nginx. É preciso
   configurar `ForwardLimit=1` + `PeersConfiaveis` da subnet do compose.
5. **Migrations:** o Npgsql 9.0.4 trava a migração com `LOCK TABLE ... IN ACCESS EXCLUSIVE MODE`
   (trava transacional, sem advisory lock de sessão). Mesmo assim o Neon recomenda conexão
   **direta** para migrations. Recomendação: `--migrate-only` como `preDeployCommand` com uma
   connection string direta dedicada. **[D]**
6. **Dois bancos no compose, um só servidor** — é exatamente a topologia do Neon.

Achados graves **fora do escopo** desta trilha estão na seção 7. O principal: a chave AES que
cifra conta bancária, chave PIX e CPF/CNPJ está fixa no código-fonte.

---

## 1. Estado local do processo

### 1.1 Inventário

| # | Onde | Chave / o que guarda | TTL | Com 2 réplicas | Num redeploy | Classificação |
|---|------|----------------------|-----|----------------|--------------|---------------|
| 1 | `VerificacaoMFAService` | `mfa:{tipo}:{email}` → hash SHA-256 do código de 6 dígitos. Tipos: `login`, `reset_senha`, `cadastro` | 5 min | Código gerado na réplica A e validado na B → "Código expirado ou inválido" | Perdido: usuário precisa pedir outro código | **[Redis]** |
| 2 | `CadastroService` via `CacheTempService` | `cadastro:{email}` → `DadosCadastroPendente` (nome, e-mail, tipo, **hash BCrypt da senha**, IP, aceite de termos) | **24 h** (o código dura 5 min; o reenvio gera outro a partir destes dados) | Confirmação cai na outra réplica → "Cadastro expirado" | **Todo cadastro iniciado nas últimas 24 h e ainda não confirmado é perdido** e precisa ser refeito | **[Redis]** — ver 1.3 (contém hash de senha) |
| 3 | `CadastroService` via `CacheTempService` | `cadastro:cooldown-reenvio:{email}` → `true` | 1 min | Cooldown vale só na réplica que respondeu (reenvio duplicado possível) | Perdido (inofensivo) | **[Redis]** |
| 4 | `DadosBancariosService` | Token de alteração (hash) | 10 min | Token gerado em A, confirmado em B → "Código expirado" | Perdido | **[Redis]** |
| 5 | `DadosBancariosService` | Contador de tentativas do token (máx. `MaxTentativas`) | 10 min | **Proteção anti-brute-force dividida por N** — cada réplica conta separado | Zerado | **[Redis]** |
| 6 | `DadosBancariosService` | Autorização de edição (janela após confirmar token) | 10 min | Confirmou em A, salvou em B → "Confirme o código..." | Perdido | **[Redis]** |
| 7 | `ContratoServicoService` | OTP de assinatura (hash) por `(contratoId, usuarioId)` | 10 min | OTP gerado em A, assinatura em B → "Código expirado" | Perdido | **[Redis]** |
| 8 | `ContratoServicoService` | Tentativas do OTP de assinatura | 10 min | Anti-brute-force dividido por N | Zerado | **[Redis]** |
| 9 | `PerfilExtensions` | `PerfilPublicoDTO` do prestador (leitura pública) | 2 min | Cada réplica com cópia própria; até 2 min desatualizado. **Não há invalidação nenhuma** (já hoje) | Some; recarrega do banco | **[fica em memória]** |
| 10 | `ContratanteExtensions` | `perfil_contratante_{id}` | 2 min | Invalidação (`Remove`) só atinge a réplica que tratou a edição; a outra serve até 2 min desatualizado | Some; recarrega | **[fica em memória]** (aceitável; ver 1.2) |
| 11 | `RateLimiterSetup` | Janela fixa por `{política}\|{ip}`: cadastro 5, login 10, senha 3, dados-bancários 5, otp-assinatura 3 por minuto | 1 min | **Limite efetivo multiplicado por N** (cada réplica tem seus baldes) | Zerado | **[Redis]** |
| 12 | Data Protection do ASP.NET | Chaves em `$HOME/.aspnet/DataProtection-Keys` (`/home/app/...` em produção **[C]**) | — | Cada réplica gera suas chaves | Chaves novas | **Nenhuma ação** — ver 1.4 |
| 13 | Serilog `WriteTo.File("logs/openai-.txt")` | **Todos** os logs (o nome engana), rolagem diária, 30 arquivos | — | Com volume compartilhado, duas réplicas no mesmo arquivo → conflito | Perdido em produção (sem volume) **[C]** | **stdout** — ver 1.5 |

Não há outro estado em processo: a busca por `ConcurrentDictionary`, `SemaphoreSlim`,
coleções estáticas mutáveis e `Interlocked` não encontrou nada além de dicionários estáticos
**somente leitura** (sinônimos da busca semântica). **[C]**

### 1.2 O que continua em memória

Os caches de perfil público (#9, #10) são **cache de leitura com TTL curto**. Levá-los para o
Redis custaria uma ida à rede para economizar outra (o Postgres) e ainda exigiria
invalidação distribuída. A inconsistência de até 2 min entre réplicas é aceitável para dado
de vitrine. Se isso incomodar no futuro, a correção barata é reduzir o TTL, não distribuir.

### 1.3 Critério aplicado e cuidados ao mover para o Redis

Nada que seja **fonte da verdade** vai para o Redis. Confirmado que já estão no Postgres,
com garantia no próprio banco:

- ledger financeiro (`LedgerFinanceiro`, imutável);
- idempotência de webhook — **índice único** em `WebhookLogs.ChaveIdempotencia` **[C]**;
- idempotência de pagamento — **índice único** em `Pagamentos.IdempotencyKey` **[C]**;
- trilhas de auditoria (`AuditLog`, `ConsentLog`, `HistoricoAssinatura`, `HistoricoContrato`).

O que vai para o Redis é **estado efêmero de fluxo** (a perda obriga o usuário a repetir um
passo, sem corromper nada). Dois cuidados passam a valer quando esse estado sai da memória do
processo e vai para um serviço de rede:

1. **O hash de OTP deixa de ser seguro.** Hoje é `SHA-256(código)` sem segredo (`SecureHasher`).
   Um código de 6 dígitos tem 900 mil valores possíveis: quem ler o Redis reverte o hash em
   milissegundos. No Redis, o armazenamento precisa ser **HMAC-SHA256 com uma chave do servidor**
   (pepper vindo de configuração), mantendo a comparação em tempo constante. **[I]**
2. **`DadosCadastroPendente` contém o hash BCrypt da senha.** Não é texto puro, mas deixa de
   estar só na RAM do processo, e vive até **24 h**. Exige Redis **sem porta publicada** e com
   senha. Persistência em disco é uma escolha explícita: sem ela (`--save ""`,
   `--appendonly no`) um restart do Redis perde os cadastros pendentes, exatamente como um
   redeploy faz hoje; com ela, o hash de senha passa a existir também em disco. **[D]** —
   recomendação: sem persistência (paridade com o comportamento atual). **[I]**

### 1.4 Data Protection: nada o consome

As chaves são geradas (o log de subida mostra `/home/app/.aspnet/DataProtection-Keys`) **[C]**,
mas nenhum componente as usa:

- a autenticação é JWT Bearer; o cookie `tratoo_auth` carrega o próprio JWT, assinado com
  `Jwt:SecretKey`, não com Data Protection **[C]**;
- não há `AddAntiforgery`/`UseAntiforgery`, e os endpoints com formulário usam
  `.DisableAntiforgery()` **[C]**;
- não há sessão, TempData nem autenticação por cookie do ASP.NET **[C]**.

**Decisão:** não compartilhar chaves (seria complexidade sem consumidor). Registrar no código
que, se um dia entrar antiforgery ou cookie auth, as chaves precisarão ser compartilhadas
entre réplicas. O `DataProtector` do domínio **não** é o Data Protection do ASP.NET — é AES
com chave estática (seção 7.1).

### 1.5 Logs em arquivo

Em produção o arquivo é gravado dentro do container, que não tem volume **[C]**, e se perde
a cada deploy. Os logs que valem estão no stdout, coletados pela Railway. No compose, um
volume compartilhado entre 2 réplicas faria dois processos escreverem no mesmo arquivo
rolante.

**Decisão:** tornar o sink de arquivo configurável, com **padrão = ligado** (produção não muda
nada) e **desligado no compose**. Container loga em stdout (`docker compose logs` já agrega
por réplica). Sem volume `apilogs`.

---

## 2. BackgroundServices com 2+ réplicas

Nenhum job tem trava hoje: não existe advisory lock, `FOR UPDATE`, token de concorrência
(`IsConcurrencyToken`/`RowVersion`) nem nível de isolamento explícito no código **[C]**.

| Job | Intervalo | O que faz | Idempotente? | Dano se rodar em dobro |
|-----|-----------|-----------|--------------|------------------------|
| `PagamentoLiberacaoService` | 4 h (espera 2 min no boot) | Lista `Retido` com `LiberacaoAutomaticaEm <= agora` e chama `LiberarAutomaticamenteAsync` | **Sim para o dinheiro [C]**: `TryMarcarTransferenciaEmProgressoAsync` faz `ExecuteUpdate ... WHERE Id = @id AND Status = 'Retido'` antes de chamar o Asaas; só uma réplica vence, a outra registra "Liberação concorrente detectada" e sai | **Sem PIX duplicado.** Efeitos colaterais menores: as validações que rodam **antes** da reivindicação (prestador inapto, chave PIX inválida) podem registrar a falha em duplicidade na auditoria **[I]** |
| `ContratoExpiracaoService` | 1 h (30 s no boot) | Carrega contratos expirados, marca `Cancelado`, recusa a proposta e reabre o projeto; um `SaveChanges` no fim | Na prática sim: as duas réplicas gravam os mesmos valores (só `CanceladoEm`/`AtualizadoEm` diferem por ms) | Log duplicado. Existe uma janela de "lost update" com ação de usuário concorrente (ex.: cancelamento manual com outro motivo sobrescrito) — **já existe hoje com 1 réplica** **[I]** |
| `PropostaExpiracaoService` | 1 h (15 s no boot) | Marca propostas vencidas como `Expirada` | Sim | Nenhum relevante |
| `AvaliacaoExpiracaoService` | 1×/dia (3 min no boot) | Publica ou oculta avaliações pendentes há 7 dias, recalcula reputação e reindexa embedding | Sim: `RecalcularReputacaoAsync` recalcula do zero a partir das avaliações públicas **[C]** | Upsert concorrente de `ReputacaoResumo` pode gerar conflito de chave (erro logado; corrigido na próxima execução) **[I]**; reindexação duplicada |
| `ReindexacaoBackgroundService` | Semanal (segunda 02:00 UTC) | Recalcula embeddings de todos os prestadores e projetos via OpenAI | Sim (upsert) | **Custo dobrado na OpenAI** e duas cargas simultâneas no banco vetorial |

### 2.1 Mecanismo: `pg_try_advisory_xact_lock` no Postgres

| Critério | `pg_advisory_lock` (sessão) | **`pg_try_advisory_xact_lock` (transação)** | Lock no Redis |
|----------|------------------------------|---------------------------------------------|---------------|
| Funciona pelo pooler do Neon (PgBouncer `transaction`) | **Não** — "Session-level advisory locks" são explicitamente não suportados | **Sim** — a trava vive e morre na transação, que é a unidade do pooler | Independe do Neon |
| Nova dependência | Nenhuma | Nenhuma | **Redis passa a ser obrigatório para a correção dos jobs** em produção |
| Liberação se o processo morrer | Ao cair a sessão (instável sob pooler) | Automática no fim/abort da transação | Depende de TTL; semântica fraca sob falha de rede |
| Mesma fonte da verdade dos dados | Sim | **Sim** | Não |

**Decisão:**

1. **Primeira linha: tornar cada escrita condicional** (padrão já usado na liberação). Os jobs
   de expiração passam a usar `ExecuteUpdate ... WHERE Status = <pendente> AND ExpiraEm < agora`,
   ou verificam o número de linhas afetadas. Isso corrige a corrida com usuário que **já existe
   com 1 réplica** e torna a réplica extra irrelevante.
2. **Segunda linha: eleição de "quem roda" com `pg_try_advisory_xact_lock(<chave do job>)`**,
   dentro de uma transação curta, para os jobs em que rodar duas vezes custa algo:
   `ReindexacaoBackgroundService` (OpenAI) e `PagamentoLiberacaoService` (evita chamadas
   duplicadas ao gateway e auditoria duplicada). Quem não obtém a trava pula a rodada.
   - Observação: a liberação chama o Asaas por HTTP. **Não** segurar uma transação aberta
     durante as chamadas externas; a trava protege a varredura, e a reivindicação atômica por
     pagamento continua sendo a garantia real.
3. **Não usar Redis para trava.** O Redis continua opcional em produção.

A correção dos jobs é **pré-requisito** para subir 2 réplicas no prompt 4 — mesmo sabendo que
o dinheiro já está protegido, é o único ponto em que um erro de concorrência tem efeito externo.

---

## 3. Redis em produção

### 3.1 Estado atual **[C]**

- Projeto `truthful-mindfulness`, ambiente `production`, **um único serviço** (`tratoo`),
  `numReplicas: 1`, região `sfo` (us-west2), sem volumes, sem pre-deploy.
- Nenhuma variável `REDIS*`.
- Rede privada disponível: o container tem `railnet0` **dual-stack** (IPv4 `10.229.x` e IPv6
  `fd12:...`), DNS `fd12::10` e `search railway.internal`. O nome interno segue
  `SERVICE_NAME.railway.internal`.
- **Neon em `us-east-2` (Ohio); app em `sfo`.** Handshake TCP container → Neon medido em
  **59–84 ms** (5 amostras). Um Redis na Railway ficaria na mesma região da app.

### 3.2 Benefício real com 1 réplica

| Benefício | Tamanho real |
|-----------|--------------|
| OTP / token bancário sobrevivem ao redeploy | Janela de 5–10 min por fluxo × poucos deploys por semana. Hoje o usuário afetado pede outro código. **Pequeno.** |
| Cadastro pendente sobrevive ao redeploy | Janela de **24 h**: cada deploy descarta todos os cadastros iniciados e não confirmados. É o maior benefício real, e ainda assim só vale se o Redis tiver persistência — o que traz o hash de senha para o disco (seção 1.3). **Moderado.** |
| Rate limit sobrevive ao redeploy | Um atacante ganha uma janela nova de 1 min a cada deploy. **Desprezível.** |
| Latência | O estado já está em memória (0 ms). O Redis **acrescenta** um round-trip. **Negativo.** |

**Custo:** mais um serviço (imagem `redis` oficial, que a Railway chama de "unmanaged"), mais
um ponto de falha no login, cadastro, assinatura de contrato e dados bancários, e a decisão de
comportamento em falha para cada um.

### 3.3 Comportamento se o Redis cair

| Uso | Comportamento | Por quê |
|-----|---------------|---------|
| OTP, token bancário, cadastro pendente, janela de edição | **Fail-closed** com erro claro ("serviço temporariamente indisponível, tente em instantes") | Validar "sem store" seria aceitar qualquer código; cair silenciosamente para a memória reintroduz o bug de réplica sem ninguém perceber |
| Contadores de tentativa (anti-brute-force) | **Fail-closed** junto com o OTP a que pertencem | São inseparáveis: OTP sem contador é convite a brute force |
| Rate limiting | **Degradar para o limiter em memória**, por réplica, com log de aviso | Bloquear todo login porque o Redis caiu é negação de serviço autoinfligida; o limite local ainda protege (fica N× mais frouxo, que é o estado atual) |
| Cache de perfil | Não usa Redis (seção 1.2) | — |
| Trava de jobs | Não usa Redis (seção 2.1) | — |

### 3.4 Recomendação **[D]**

**Redis só no compose (local/CI), atrás de uma abstração com implementação em memória
selecionada por configuração.** Em produção, com `Redis:ConnectionString` ausente, tudo
continua exatamente como hoje. Adotar Redis na Railway **quando `numReplicas` passar de 1** —
nesse dia o benefício deixa de ser marginal e a mudança é só configuração (serviço Redis +
`Redis__ConnectionString=${{Redis.REDIS_URL}}`).

---

## 4. Proxy local (Nginx no compose)

### 4.1 Topologia

| | Produção **[C]** | Compose com Nginx |
|---|---|---|
| Caminho | Cliente → borda Railway → proxy Railway → Kestrel | Cliente → Nginx → Kestrel |
| Hops L7 antes da app | **2** | **1** |
| Peer TCP visto pelo Kestrel | `::ffff:100.64.0.x` | `::ffff:172.x.y.z` (IP do Nginx na bridge) **[I]** |
| Quem gera o `X-Forwarded-For` | A Railway regenera do zero | O Nginx, conforme `proxy_set_header` |

### 4.2 Configuração atual do código **[C]**

- `ForwardLimit = 2`.
- `PeersConfiaveis` padrão: `100.64.0.0/10`, `fc00::/7`, `127.0.0.0/8`, `::1/128`. Definir a
  lista **substitui** o padrão inteiro.
- `KnownProxies`/`KnownNetworks` limpas de propósito (a validação é feita pelo gate).
- Só `XForwardedFor | XForwardedProto` são processados. **`X-Forwarded-Host` não é.**

### 4.3 O que quebra sem configurar

O peer `172.x` não está em `PeersConfiaveis`, então o `PeerConfiavelMiddleware` **descarta** os
`X-Forwarded-*`. Consequências:

- `ClientRequestInfo.ObterIp` devolve o IP do Nginx para **todos** os usuários;
- **o rate limiting colapsa numa partição só:** login passa a ser 10/min para a aplicação
  inteira, cadastro 5/min etc. — o 11º login do minuto, de qualquer pessoa, recebe 429;
- `AuditLog`, `ConsentLog` e `HistoricoAssinatura` gravam o IP do Nginx (prova documental
  errada na assinatura de contratos);
- `Request.IsHttps` fica `false`: o HSTS não é emitido e o scheme aparece como `http`.

É o mesmo defeito corrigido em produção em 2026-09-20, reproduzido localmente. Existe inclusive
um teste que falha exatamente assim (`RateLimitingPorIpTests`).

### 4.4 Valores necessários

- **Subnet fixa** na rede do compose (ex.: `172.28.0.0/24`), para o valor ser previsível.
- `ForwardedHeaders__PeersConfiaveis__0=<subnet do compose>` e
  `ForwardedHeaders__PeersConfiaveis__1=127.0.0.0/8` (mantém o healthcheck local e o
  `docker exec`).
- **`ForwardedHeaders__ForwardLimit=1`.**
- No Nginx, **sobrescrever** o header, espelhando a Railway:
  `proxy_set_header X-Forwarded-For $remote_addr;` (não usar `$proxy_add_x_forwarded_for`),
  `proxy_set_header X-Forwarded-Proto $scheme;` e `proxy_set_header Host $host;` — o Host
  precisa chegar direto, porque `X-Forwarded-Host` não é processado.

**Por que `ForwardLimit=1`:** se o Nginx **anexar** ao header, um cliente que envie
`X-Forwarded-For: 1.2.3.4` produz `1.2.3.4, <ip real>`. Com `ForwardLimit=2` o middleware
aceitaria `1.2.3.4` — **spoofing de IP e fuga do rate limit**. Com limite 1 e sobrescrita, o
valor forjado nunca é considerado. **[I]** (lógica do middleware; validar no prompt 2)

---

## 5. Migrations

### 5.1 Fatos

- Existe **uma** migration: `20260806232531_InitPostgres`. `dotnet ef migrations
  has-pending-model-changes` → "No changes have been made to the model since the last
  migration" (executado com connection string inválida, sem acesso a banco) **[C]**.
- **O EF Core 9 tem trava de migração:** a interface `IMigrationsDatabaseLock` /
  `AcquireDatabaseLock` existe no `Microsoft.EntityFrameworkCore.Relational 9.0.10` **[C]**.
- **A implementação do Npgsql 9.0.4 é `NpgsqlMigrationDatabaseLock`**, e a única trava SQL no
  assembly é **`LOCK TABLE <tabela de histórico> IN ACCESS EXCLUSIVE MODE`**. Não há
  `pg_advisory` no assembly **[C]** (inspeção das strings da DLL no cache do NuGet).
- `LOCK TABLE` só é válido dentro de transação e é liberado no fim dela, portanto é
  **compatível com o PgBouncer em modo transaction** **[I]**.
- **O EF Core 9 lança exceção no `Migrate()` se o modelo tiver mudanças sem migration**
  (`PendingModelChangesWarning` virou erro) **[I]**; hoje não há pendência.
- Neon: PgBouncer com `pool_mode=transaction`; `SET`, `PREPARE` em SQL e advisory lock de
  sessão não são suportados; `query_wait_timeout=120`. A tabela oficial de "pooled vs direct"
  diz: **schema migrations → conexão direta** ("tools may not support transaction pooling") **[C]**.
- Railway pre-deploy: roda **em container separado**, na rede privada, com as variáveis do
  serviço; **se falhar, o deploy não prossegue** e não há retry; sem limite de tempo por padrão
  (1–3600 s configurável). Chaves: `deploy.preDeployCommand` (string ou array) e
  `deploy.preDeployTimeoutSeconds` **[C]**.
- As duas connection strings de produção apontam para o **endpoint `-pooler`**; **não existe
  variável de conexão direta** **[C]**.
- O `TratooContextFactory` (design-time) lê `appsettings.json`, que aponta para o **Neon de
  produção** **[C]** — é por aí que as migrations são aplicadas "à mão" hoje **[I]**.

### 5.2 Comparação para produção

| Opção | Prós | Contras |
|-------|------|---------|
| **Manual** (`dotnet ef database update` da máquina do dev) | Nada muda | Depende de alguém lembrar; roda do Windows local contra produção com as credenciais do `appsettings.json`; a ordem código/schema fica a critério humano |
| **`--migrate-only` como `preDeployCommand`** | Automático; roda **uma vez por deploy** (não por réplica); falha **aborta o deploy antes** de o código novo servir; mesma imagem do app | Exige migrations **compatíveis com a versão anterior** (a versão antiga continua servindo durante o pre-deploy e o overlap); precisa de conexão direta |
| `MigrateAsync()` no startup do app | Simples | Roda em cada réplica/restart (a trava serializa, mas a semântica é ruim); falha derruba o app em vez de bloquear o deploy; atrasa o boot |

### 5.3 Recomendação **[D]**

1. **Produção:** `preDeployCommand = ["dotnet", "Tratoo.API.dll", "--migrate-only"]` no
   `railway.toml`, usando uma **connection string direta** (host sem `-pooler`) numa variável
   dedicada, por exemplo `ConnectionStrings__MigrationConnection`. O modo `--migrate-only` usa
   essa conexão se existir e cai para a `DefaultConnection` se não existir. O responsável cria
   a variável na Railway (a IA não altera variáveis).
2. **Antes de ligar o pre-deploy — verificação obrigatória:** confirmar (consulta somente
   leitura) que `__EFMigrationsHistory` de produção contém `20260806232531_InitPostgres`. Se o
   banco tiver sido criado sem histórico, o primeiro `Migrate()` tentaria recriar todas as
   tabelas e o deploy falharia (falha segura, mas bloqueante).
3. **`VectorDbInitializer`:** incluir no `--migrate-only` e **mantê-lo também no startup** até o
   pre-deploy estar provado em produção (é idempotente: `IF NOT EXISTS`). Remover do boot só
   depois, num passo separado.
4. **Regra para as próximas migrations:** apenas mudanças retrocompatíveis por deploy
   (expand/contract), porque o pre-deploy roda com a versão antiga ainda no ar.

---

## 6. Bancos: dois no compose

**Fatos [C]:**

- `TratooContext` → banco `tratoo` (nenhuma referência a `vector` na migration).
- `VectorContext` → banco `tratoo_vector` (`PrestadorEmbeddings`, `ProjetoEmbeddings`), sem
  migrations do EF: o schema é criado por SQL cru no `VectorDbInitializer`.
- Em produção são **dois bancos no mesmo servidor Neon** (mesmo host `-pooler`, `Database=`
  diferente).
- O health check `pgvector` consulta a `VectorConnection` e verifica a extensão.

**Decisão:** o compose replica exatamente isso — **um container Postgres (pgvector/pgvector:pg16)
com dois bancos**, `tratoo` criado por `POSTGRES_DB` e `tratoo_vector` criado por script em
`/docker-entrypoint-initdb.d/`. A extensão `vector` continua sendo criada pelo
`VectorDbInitializer`.

**Por que não um banco só:** as duas connection strings apontarem para o mesmo banco
funcionaria, mas esconderia justamente os defeitos que só aparecem com a separação (uma
consulta que use o contexto errado, uma extensão criada no banco errado, o health check de
pgvector passando pelo motivo errado). Paridade com produção custa uma linha de SQL.

Observações:

- scripts de `initdb.d` só rodam com o **volume vazio**; mudar o script exige `down -v`;
- a versão do Postgres local deve ser a mesma do Neon (o `TratooContextFactory` fixa
  `SetPostgresVersion(16, 0)`); confirmar com `SELECT version()` antes de fixar a imagem.

---

## 7. Achados fora do escopo (registrados para não se perderem)

### 7.1 Chave AES fixa no código-fonte — **grave**

`Tratoo.Domain/Features/Shared/DataProtector.cs` usa
`BuildKey("CHAVE_SECRETA_32_BYTES_PLACEHOLDER")` como chave AES-256. Essa classe cifra **conta
bancária, chave PIX** (`DadosBancariosService`) e **CPF/CNPJ** (`IdentidadeService`), e é usada
na descriptografia do PIX na liberação de pagamento **[C]**. Quem tiver acesso ao repositório
decifra tudo o que estiver no banco. Além disso, o modo é AES-CBC sem autenticação (sem MAC).
Corrigir exige chave vinda de secret e **re-cifragem dos dados existentes** — trabalho próprio,
fora da Trilha 2.

### 7.2 Configuração local aponta para o Neon de produção

`Tratoo.API/appsettings.json` e `appsettings.Development.json` usam **o mesmo host Neon da
produção** **[C]** (os arquivos estão no `.gitignore`, então não vazam). Um `dotnet run` local
liga os BackgroundServices contra dados reais: expiração de contratos e liberação de
pagamentos. O compose da Trilha 2 é a oportunidade de fazer o desenvolvimento local apontar
para o Postgres do compose por padrão.

### 7.3 App e banco em regiões diferentes

App em `sfo` (us-west2), Neon em `us-east-2`: **~60 ms por round-trip** medidos do container.
Toda requisição que faz N consultas paga N × 60 ms. Mover um dos dois para a mesma região é
provavelmente a maior melhoria de latência disponível hoje.

### 7.4 OTP de login/reset/cadastro sem contador de tentativas

`VerificacaoMFAService.Validar` não conta tentativas erradas (o OTP de assinatura e o token
bancário contam). A proteção é só o rate limit por IP. Ao mover para o Redis (prompt 4), aplicar
o mesmo contador dos outros dois fluxos.

---

## 8. Decisões que os prompts 2, 3 e 4 seguem

### Prompt 2 — compose base

1. Um serviço `postgres` (`pgvector/pgvector:pg16`, versão conferida com o Neon) com **dois
   bancos**: `tratoo` + `tratoo_vector` via `docker-entrypoint-initdb.d`. Volume nomeado `pgdata`.
2. Serviço `redis` **sem porta publicada**, com senha, `--save ""` e `--appendonly no`.
3. Rede bridge com **subnet fixa**; **só o Nginx publica portas** (443/80, parametrizáveis).
4. API atrás do Nginx com `ForwardedHeaders__ForwardLimit=1`,
   `ForwardedHeaders__PeersConfiaveis__0=<subnet>`, `__1=127.0.0.0/8`. O Nginx **sobrescreve**
   `X-Forwarded-For` com `$remote_addr` e envia `X-Forwarded-Proto` e `Host`.
5. Sink de arquivo do Serilog configurável: **padrão ligado** (produção inalterada),
   **desligado no compose**; sem volume `apilogs`.
6. Segredos só via `.env` (fora do git) com `${VAR:?}`; `.env.example` só com placeholders;
   Asaas sandbox; `Resend:FromEmail` obrigatório.
7. Nenhuma mudança no comportamento de produção.

### Prompt 3 — migrations

1. Modo `--migrate-only` no `Program.cs`, no padrão do `--healthcheck`: **antes** de subir o
   Kestrel e os hosted services; aplica `TratooContext` + `VectorDbInitializer`; `return 0` em
   sucesso e código ≠ 0 em falha.
2. Usa `ConnectionStrings:MigrationConnection` se existir; senão `DefaultConnection`.
3. Compose: serviço `migrate` com as **duas** connection strings;
   `api` depende de `migrate: service_completed_successfully`.
4. `VectorDbInitializer` **permanece no startup** por enquanto.
5. Produção: `preDeployCommand` no `railway.toml` **só depois** de o responsável (a) confirmar
   que `__EFMigrationsHistory` de produção contém `InitPostgres` e (b) criar a variável com a
   conexão **direta** do Neon. O prompt entrega as instruções; não altera a Railway.

### Prompt 4 — Redis + réplicas

1. **Primeiro os jobs** (seção 2.1): escritas condicionais nos três jobs de expiração +
   `pg_try_advisory_xact_lock` em `ReindexacaoBackgroundService` e `PagamentoLiberacaoService`.
   Teste de concorrência provando que duas instâncias não liberam o mesmo pagamento duas vezes.
2. **Uma abstração** para os 8 estados efêmeros da tabela 1.1 (#1–#8), com implementação em
   memória (padrão, produção) e em Redis (quando `Redis:ConnectionString` existir). OTP em
   **HMAC-SHA256 com pepper** no Redis. Adicionar contador de tentativas ao
   `VerificacaoMFAService`.
3. Rate limiting distribuído no Redis com **degradação para memória** em falha; OTP e
   contadores com **fail-closed**.
4. Data Protection: **não** compartilhar; apenas documentar no código.
5. Cache de perfil: **continua em memória**.
6. `api` com 2 réplicas; Nginx balanceando pelo DNS do Docker (`resolver 127.0.0.11`); sem
   `container_name` nem IP fixo na api.
7. Produção continua com 1 réplica e **sem Redis**. Redis na Railway fica para quando
   `numReplicas > 1` **[D]**.

### Decisões pendentes do responsável

| # | Decisão | Recomendação |
|---|---------|--------------|
| D1 | Redis também na Railway agora? | **Não** — só quando `numReplicas > 1` (seção 3.4). O código já está pronto: basta configurar `Redis__ConnectionString` + `Redis__ChaveProtecao` |
| D2 | Migrations em produção via `preDeployCommand`? | **Sim**, com conexão direta e após verificar o histórico (seção 5.3) |
| D3 | Criar `ConnectionStrings__MigrationConnection` (Neon direto) na Railway | Necessário para D2 |
| D4 | Sink de arquivo do Serilog em produção | Manter ligado por ora (sem mudança); reavaliar depois |
| D5 | Achado 7.1 (chave AES no código) | Tratar **antes** de qualquer outra coisa desta trilha, em tarefa separada |
| D6 | Achado 7.3 (regiões diferentes) | Avaliar mover o serviço para a região do Neon (ou o contrário) |

---

## 8.1 Status — etapa 06 (compose base) implementada em 2026-09-29

Arquivos: `compose.yaml`, `.env.example`, `.gitattributes`, `infra/postgres/initdb/`,
`infra/nginx/templates/default.conf.template`, `infra/nginx/gerar-certificado.sh`,
`Tratoo.API/Infrastructure/LogArquivoSetup.cs` (+ teste). Único código compartilhado alterado:
o sink de arquivo do Serilog virou opcional, com **padrão ligado** (produção inalterada).

Validado com a stack real: subida em ordem de dependência com healthchecks; 5432/6379/8080
fechadas no host e abertas dentro da rede; `/health/ready` 200 via `https://localhost`
(HTTP/2, TLS 1.3); 301 http→https; IP do cliente = `$remote_addr` do Nginx com
`X-Forwarded-For`/`X-Forwarded-Proto` forjados descartados; controle negativo com a config de
produção atrás do Nginx → todo cliente vira o IP do Nginx e `isHttps=false`; dados
preservados após `down` + `up`; SIGTERM → encerramento gracioso com exit 0.

Ajustes e observações em relação às decisões acima:

- **`X-Real-IP` também é sobrescrito pelo Nginx.** A API não o lê, mas o Nginx é peer
  confiável: sem sobrescrever, um valor forjado chegava intacto à aplicação (observado).
- **`Asaas__WebhookToken` é obrigatório no compose.** Com token vazio, o endpoint de
  webhook **pula a validação** e aceita eventos forjados (`PagamentoExtensions`). Em produção
  a variável existe; a validação fail-open é um achado para tratar à parte.
- **HSTS não aparece em `localhost`/`127.0.0.1`**: o `HstsOptions.ExcludedHosts` padrão do
  ASP.NET exclui esses hosts. É o desejado em dev (o navegador não fixa HSTS no localhost).
- **Todo acesso vindo do host aparece como `172.28.0.1`** (gateway da rede no Docker
  Desktop). Localmente, todos os clientes do host dividem a mesma partição de rate limit —
  limitação do NAT do Docker, não do código.
- **Postgres local usa um único usuário superusuário**, enquanto o Neon usa o dono do banco
  (não superusuário). Divergência conhecida: um `CREATE EXTENSION` que exija superusuário
  passaria aqui e falharia no Neon.
- Postgres local é 16.15; a versão exata do Neon não foi conferida.

## 8.2 Status — etapa 07 (migrations) implementada em 2026-09-30

- `Tratoo.API/Infrastructure/ModoMigracao.cs` + desvio no topo do `Program.cs`:
  `--migrate-only` monta `TratooContext`/`VectorContext` à mão, **sem host** (sem Kestrel,
  sem BackgroundServices), aplica as migrations, roda o `VectorDbInitializer` e sai com
  `0` (sucesso), `1` (falha) ou `2` (connection string ausente). Usa
  `ConnectionStrings:MigrationConnection` se existir, senão a `DefaultConnection`, e avisa
  quando o alvo é um endpoint `-pooler`.
- Compose: serviço `migrate` (mesma imagem; `command: ["--migrate-only"]`), connection
  strings numa âncora YAML compartilhada com a `api`; a `api` depende de
  `migrate: service_completed_successfully`.
- **`VectorDbInitializer` continua no boot da API.** Hoje a Railway depende dele para o
  schema vetorial; ele só sai do boot depois que o `preDeployCommand` estiver ativo e provado.
- **`preDeployCommand` preparado, mas comentado no `railway.toml`**, com o checklist de
  ativação. Ativo agora, rodaria no próximo deploy antes das duas pré-condições da seção 5.3.
- **Correção colateral:** `Microsoft.EntityFrameworkCore.Relational 9.0.10` declarado no
  `Tratoo.Domain`. O `Tratoo.Tests` resolvia a 9.0.1 (conflito MSB3277 antigo) e rodava com
  um EF diferente do de produção; o teste de processo do `--migrate-only` quebrava por isso.
  O conjunto de pacotes da API ficou idêntico (86 bibliotecas, mesmas versões).

Validado no compose: banco vazio → `migrate` aplica `InitPostgres` (34 tabelas) e o schema
vetorial → exit 0 → API sobe; segunda execução → "No migrations were applied", exit 0;
**duas instâncias simultâneas** (`--scale migrate=2`, bancos vazios) → ambas viram 1 pendente,
uma aplicou e a outra esperou a trava (3 s) e encontrou tudo aplicado, histórico com 1 linha;
Postgres parado → exit 1; migração falhando no `up` → `compose up` sai com 1 e `api`/`nginx`
ficam em `Created`, nunca iniciam. Testes: modo de migração rodando o **processo real** com
banco inalcançável (exit 1, sem "Now listening", sem BackgroundService) e sem connection
string (exit 2); mutação que desliga o desvio no `Program.cs` derruba os dois testes.

## 8.3 Status — etapa 08 (Redis + 2 réplicas) implementada em 2026-10-01

**Ordem seguida: jobs primeiro, depois o estado compartilhado, por último as réplicas.**

### Jobs com N réplicas (seção 2.1)
- Escritas condicionais: propostas (um `UPDATE … WHERE` único), contratos (cancelamento
  condicional + proposta/projeto numa transação; falha desfaz tudo e volta na próxima
  rodada) e avaliações (finaliza só se ainda pendente e no mesmo estado de nota lido).
- `ExecucaoExclusiva` (`pg_try_advisory_xact_lock`) nas rodadas de liberação de pagamento e
  de reindexação. A trava fica numa transação aberta em conexão dedicada enquanto a rodada
  roda em outro escopo — compatível com o PgBouncer em modo transaction.
- **Achado novo:** o lembrete D+3 de avaliação era reenviado por réplica (e também a cada
  redeploy, já com 1 réplica): a "janela de 1 dia" depende de as 24 h de cada processo
  baterem. Agora há marca atômica "lembrete enviado" (`DefinirSeAusente`, `SET NX` no Redis;
  em memória com 1 réplica, igual a antes).

### Estado efêmero (seções 1 e 3)
- Abstração única `IEstadoEfemero` (memória por padrão; Redis com
  `Redis:ConnectionString`). `ICacheTempService` removido. MFA, cadastro, dados bancários e
  OTP de assinatura migrados; contadores com incremento atômico; **MFA ganhou limite de 5
  tentativas** (achado 7.4).
- **Desvio da decisão 1.3 (HMAC com pepper):** a proteção ficou no próprio armazenamento
  Redis — valores com AES-256-GCM (nome da chave como dado associado) e nomes de chave
  como HMAC-SHA256, ambos derivados (HKDF) de `Redis:ChaveProtecao`. Cobre o hash de OTP
  **e** o cadastro pendente (nome, e-mail, IP, hash BCrypt) **e** os e-mails/IPs que iam nos
  nomes das chaves; o `SecureHasher` continua igual. Sem `Redis:ChaveProtecao` a API não sobe
  (quando há Redis).
- Fail-closed: indisponibilidade vira `ServicoIndisponivelException` → 503 + `Retry-After: 5`
  (handler extraído para `TratamentoErrosSetup`). Exceção deliberada: **pedido de reset de
  senha** engole a falha e responde igual — 503 só para contas existentes revelaria quais
  e-mails estão cadastrados.
- Rate limiting com contador no Redis (janela pelo relógio do Redis, alinhada ao minuto) e
  **degradação para memória** em qualquer falha.
- `/health/ready` ganha o check `redis` só quando o Redis está configurado.
- Data Protection: não compartilhado (nada o consome); registrado no `Program.cs`.
- Cliente: StackExchange.Redis **2.13.17** (linha 2.x; a 3.x puxa dependências da geração
  .NET 10 para dentro de uma app .NET 8). `BacklogPolicy.FailFast` + `AbortOnConnectFail=false`.

### Compose
- `api` com `deploy.replicas: ${API_REPLICAS:-2}`; Nginx balanceando pelo DNS do Docker
  (resolver + `proxy_pass` com variável), `proxy_next_upstream error timeout`.
- Redis sem persistência, coerente com o conteúdo (efêmero, e o cadastro pendente nunca
  chega a disco).

### Defeitos que só a validação real mostrou (corrigidos, com teste de regressão)
1. **500 numa rota com rate limit:** com `FailFast`, o StackExchange.Redis *cancela*
   (`TaskCanceledException`) comandos pendentes quando a conexão oscila — fora do `catch` de
   `RedisException`. Agora o limitador degrada em qualquer falha e o estado efêmero mapeia o
   cancelamento para 503. O multiplexer passou a logar eventos de conexão.
2. **~5 s de latência após uma réplica sair:** o IP dela continuava no cache de DNS do
   Nginx e a conexão esperava o `proxy_connect_timeout` (5 s) antes do failover. Agora 1 s
   (e `valid=5s`): pior caso medido 1,03 s, mediana 25 ms.

## 9. Evidências e método

- **Código:** varredura de `IMemoryCache`, `ICacheTempService`, estado estático, Data
  Protection/antiforgery/cookies, BackgroundServices e repositórios envolvidos.
- **EF/Npgsql:** inspeção das strings UTF-16 de `Npgsql.EntityFrameworkCore.PostgreSQL.dll`
  9.0.4 e de `Microsoft.EntityFrameworkCore.Relational.dll` 9.0.10 no cache local do NuGet;
  `dotnet ef migrations has-pending-model-changes` com connection string deliberadamente
  inválida (nenhum acesso a banco).
- **Railway (somente leitura):** `railway status --json` (serviços, réplicas, região,
  pre-deploy, volumes); `railway variables --json` processado localmente para extrair **só**
  propriedades (tem `-pooler`? qual `Database`? qual região AWS?), sem exibir valores;
  `railway ssh` para interfaces, DNS e handshake TCP com o Neon (host não exibido).
- **Documentação:** Neon — Connection pooling; Railway — Pre-deploy command, Redis, Private
  networking e o schema `railway.schema.json`.
- **Não verificado nesta etapa:** o conteúdo de `__EFMigrationsHistory` em produção (exigiria
  consulta ao banco de produção) e a versão exata do Postgres no Neon.
