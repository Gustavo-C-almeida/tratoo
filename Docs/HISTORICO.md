# Tratoo — Histórico técnico

> Registro de como a arquitetura chegou ao estado atual: linha do tempo, contexto anterior de
> cada decisão, correções e o retrato da auditoria de 29–30/10/2026 (divergências, limitações e
> roadmap daquele momento). O estado atual está em [`ARQUITETURA.md`](ARQUITETURA.md); este
> documento não é atualizado quando a arquitetura muda, só recebe novas entradas.
>
> Os itens das seções 4 a 6 refletem o que foi registrado em 30/10/2026 e podem já ter sido
> resolvidos. Por exemplo, a limpeza do commit `9a9ec34` (30/10) removeu `Azure.Storage.Blobs`,
> `node_modules/` e `package.json` e corrigiu os comentários desatualizados; em 31/10 o CodeQL
> não tinha alertas abertos.

---

## 1. Linha do tempo

| Data | Commit / PR | Mudança | Status em 30/10/2026 |
|---|---|---|---|
| 21/07 | `0239830` | Primeiro Dockerfile para a Railway | Evoluído (ver 28–29/09) |
| 06/08 | `bf6761d` | `TratooContext` migrado de SQL Server para PostgreSQL (Neon). Migrations antigas removidas; nasce `InitPostgres` | Confirmado |
| 06/08 | `045bb4b` | `ContratoExpiracaoService` resolvia um tipo concreto em vez da interface | Confirmado |
| 07–10/08 | `394b2ac`, `2bc073c` | Bootstrap 5.3 vendorizado; login e cadastro migrados | Confirmado (migração parcial) |
| 10/08 | `fa43e83` | E-mail: SMTP/Gmail → API HTTPS do Resend | Confirmado (configurado) |
| 20/09 | `49a075b` | `ResendEmailService` passa a depender só do SDK | Confirmado |
| 20/09 | `5aed9aa` | IP real atrás do proxy + rate limiting particionado. Criação do `Tratoo.Tests` | Confirmado |
| 21/09 | `3be901d`/`ccf7d9e` | Diagnóstico temporário do proxy (já removido). Os mesmos commits versionaram `package.json` e `node_modules/` do SDK npm `railway` | Diagnóstico removido; `node_modules` permanecia como resíduo (removido em `9a9ec34`, 30/10) |
| 22/09 | `92340eb`, PR #3 | Gate de peer confiável; `ForwardLimit = 2` validado em produção | Confirmado |
| 27–28/09 | `470dc64`, PR #4 | `Retry-After` nas respostas 429 | Confirmado |
| 28/09 | `fe22680`, PR #5 | Health checks live/ready, sonda `--healthcheck` e `railway.toml` | Código confirmado. O `railway.toml` nunca foi aplicado (ver §3.1) |
| 29/09 | `11d0206`, PR #6 | Container não-root, `PORT`, encerramento gracioso | Confirmado |
| 01/10 | `fcef8f9`, PR #7 | Compose completo (Nginx, 2 réplicas, Redis, Postgres, `migrate`), `IEstadoEfemero`, `ExecucaoExclusiva`, `--migrate-only` | Confirmado (local e testes) |
| 07/10 | `3796758`, PR #8 | 6 alertas de XSS do CodeQL corrigidos com `utils/dom.js` | Confirmado |
| 18/10 | `ae0877c`, PR #9 | Chave AES fora do código; check `dados-cifrados` | Confirmado |
| 18/10 | `4dee3a7`, PR #10 | Perfis de publicação do Azure removidos | Removido (sobrava o pacote `Azure.Storage.Blobs`, retirado em `9a9ec34`) |
| 19/10 | — | Histórico da `main` reescrito para retirar a chave | Confirmado: os commits dos deploys anteriores a essa data (por exemplo, `a853ee5`) não existem no histórico atual |
| 21/10 | `08e90c3`, PR #11 | Rotação de chave (`ChaveAnterior` + `--recifrar-dados`) | Confirmado; executada em 24/10 |
| 29/10 | — (configuração) | Redis ativado em produção | Confirmado |
| 29/10 | `471be1c`, `6903729`, PR #12 | OTP de uso único atômico; logs sem dados do usuário | Confirmado (testes; em produção desde o deploy de 29/10) |
| 29/10 | `80d1d85`, PR #13 | CI com build e testes e Docker obrigatório | Confirmado |
| 29/10 | `0dfdad4`, PR #14 | Teste instável de OTP corrigido; actions em Node 24 | Confirmado |
| 29/10 | `32872b2`, PR #15 | Fundo branco nos perfis; suporte a tema escuro removido nas páginas de perfil | Confirmado (em produção) |
| 30/10 | — (configurações da Railway) | Health check `/health/ready` e `drainingSeconds = 30` passam a valer pelas configurações do serviço; deploy `b40f6558` | Confirmado |
| 30/10 | `9a9ec34` | Limpeza pós-auditoria: `node_modules/`, `package.json` e `Azure.Storage.Blobs` removidos; comentários de pesos da busca, de "produção sem Redis" e da taxa de 5% corrigidos | — |
| 30–31/10 | PRs #16 a #21 | Reconstrução das docs, higiene do repositório, ajustes do `railway.toml`, alertas do CodeQL e remoção de handlers inline no frontend | Em 31/10: 0 alertas abertos do CodeQL; deploy `ebf57cde` (commit `b134922`) |

Antes de agosto, o histórico tem a base funcional do produto (cadastro com LGPD, perfis, projetos,
propostas, contratos, escrow, avaliações, cancelamentos e busca semântica). Ela começa com um
commit inicial em 01/06/2026 e passa por uma refatoração grande em 08/06. Os pesos atuais da busca
semântica estão no código desde `b2aa430` (08/06/2026).

---

## 2. Contexto anterior das decisões arquiteturais

As decisões estão descritas no estado atual em [`ARQUITETURA.md` §9](ARQUITETURA.md#9-decisões-arquiteturais).
Aqui fica o que existia antes de cada uma.

### D1 — PostgreSQL no Neon

- **Antes:** o negócio rodava em SQL Server (`TratooContext`), e os vetores em PostgreSQL com
  pgvector (`VectorContext`). Eram dois motores, dois custos e dois jeitos de operar. O SQL Server
  exige mais memória e não tem um plano gratuito competitivo.
- **Mudança (06/08, `bf6761d`):** o `TratooContext` foi para o PostgreSQL no Neon, no mesmo servidor
  do banco vetorial, mantendo dois bancos. Todas as migrations do SQL Server foram substituídas por
  uma única `InitPostgres`, e os defaults de data foram reescritos (`GETUTCDATE()` →
  `timezone('utc', now())`).
- **Alternativa descartada:** manter os dois motores.
- **Custo:** o histórico de migrations recomeçou do zero.
- **Origem dos dois bancos:** a separação existe desde a época em que o vetorial era o único Postgres.

### D2 — E-mail pelo Resend

- **Antes:** envio por SMTP (Gmail). Não funcionava na Railway, que bloqueia as portas de saída 25,
  465 e 587 no plano usado.
- **Mudança:** SDK oficial do Resend sobre HTTPS/443 (10/08, `fa43e83`; ajustada em 20/09,
  `49a075b`). Desde 29/10 (`6903729`), os logs registram só o Id da mensagem, sem destinatário nem
  assunto.

### D3 — IP real atrás do proxy

Até 20/09 o Tratoo não processava `X-Forwarded-For`, e três coisas quebravam juntas:

- toda a auditoria gravava o mesmo IP interno: assinatura de contrato, `ConsentLog`, `AuditLog`,
  dados bancários e disputas;
- `Request.IsHttps` era sempre falso, e o `UseHsts()` nunca emitia o header;
- o rate limiting era global (ver D4).

A correção "óbvia" (`KnownNetworks` com a faixa do peer) teria reintroduzido o bug de forma
silenciosa. A topologia foi medida em produção em 22/09 em vez de suposta. Um diagnóstico temporário
(`[DIAG-PROXY]`, `3be901d`/`ccf7d9e`) foi usado e depois removido. Evidência: `5aed9aa`, `92340eb`.

### D4 — Rate limiting

**Defeito corrigido em 20/09.** O `AddFixedWindowLimiter` não particiona nada: usa uma chave
constante, ou seja, um balde para o planeta inteiro. Na prática, 10 logins por minuto valiam para a
plataforma toda, e qualquer visitante conseguia bloquear o login de todos. Com várias réplicas, cada
uma teria o seu balde. A correção passou a usar `AddPolicy` + `RateLimitPartition` com a chave
`"{política}|{ip}"`. Evidência: `5aed9aa`, `470dc64`, `fcef8f9`.

### D5 — Health checks

O gate de deploy ficou inativo de 28/09 a 30/10, porque a Railway não aplicava o `railway.toml`
(§3.1). A auditoria detectou o problema e ele foi corrigido nas configurações do serviço. É um caso
típico de configuração declarada que nunca entrou em vigor e só aparece quando se confere o estado
efetivo da plataforma. Evidência: `fe22680`.

### D6 — Container de produção

- **Antes de 29/09:** o processo rodava como root; o `ASPNETCORE_URLS` fixo tinha precedência e
  fazia a app ignorar o `PORT` da Railway; e a Railway mandava SIGKILL sem dar tempo de encerrar
  (default de `drainingSeconds` = 0).
- **Mudança (`11d0206`):** `USER $APP_UID`, `PORT` → `http_ports`, `ENTRYPOINT` em forma exec,
  `ShutdownTimeout` de 25 s e log do ciclo de encerramento.
- O `drainingSeconds = 30` só passou a valer em 30/10, pelas configurações do serviço. Na
  auditoria de 30/10, o processo rodava com UID 1654 e a troca de versão registrou "Encerramento
  concluído em 8 ms".

### D7 — Estado efêmero e Redis

- **Contexto (01/10):** subir uma segunda réplica quebraria OTP, cadastro pendente e contadores
  (validar na réplica B um código gerado na A) e multiplicaria os limites por N.
- **Decisão inicial:** `IEstadoEfemero` com memória como padrão e Redis por configuração. A
  recomendação era ligar o Redis em produção só quando houvesse mais de uma réplica.
- **29/10:** o Redis foi ativado mesmo com 1 réplica, sem mudar código: o serviço Redis foi criado a
  partir do template da Railway e as variáveis `Redis__ConnectionString` e `Redis__ChaveProtecao`
  foram adicionadas. Desde então o estado sobrevive a redeploys.

### D9 — Migrations fora do boot

Em 30/10 o `--migrate-only` estava preparado como `preDeployCommand`, comentado no `railway.toml`
até dois pré-requisitos: confirmar o `__EFMigrationsHistory` de produção e criar a variável
`ConnectionStrings__MigrationConnection` com a conexão direta do Neon (sem pooler), recomendada para
mudar schema. Como a Railway não lê o arquivo, ativá-lo exigiria configurar o comando nas
configurações do serviço.

### D10 — Chave de dados: estudo de caso

Até outubro, a chave AES era um literal no código-fonte de um repositório **público**. Remover o
literal do HEAD não basta, porque a chave continua no histórico e em clones antigos. E trocar a
chave sem coordenação tornaria os dados de produção ilegíveis. A sequência executada foi:

1. A chave passou a vir da configuração, com o mesmo valor (PR #9, 18/10).
2. O check `dados-cifrados` passou a validar a chave contra os dados reais.
3. Os artefatos de Azure foram removidos (PR #10) e o histórico da `main` foi reescrito (19/10).
4. A rotação foi suportada (PR #11, 21/10) e executada em produção (24/10).

Estado verificado em 30/10: só `DataProtector__Chave` configurada, sem chave anterior, e o check
`dados-cifrados` Healthy. Lição: um segredo que vazou continua vazado para sempre; o que resolve é
fazer os dados deixarem de depender dele. Inverter a ordem teria deixado os dados de produção
ilegíveis.

### D11 — OTP de uso único

- **Antes de 29/10:** o código era comparado e só depois removido, e a tentativa só era contada
  depois do erro. N requisições simultâneas com o mesmo código podiam todas ser aceitas, e um
  atacante conseguia comparar mais vezes do que o limite.
- **Correção (29/10, `471be1c`, PR #12):** contar antes de comparar e aceitar só a requisição que
  apagou o código. O teste instável corrigido em `0dfdad4` documenta um caso-limite: quando os
  palpites excedentes invalidam o código antes de qualquer comparação legítima, zero comparações é
  um resultado seguro.

### D12 — XSS

- **Correção de 07/10 (PR #8, `3796758`):** 6 alertas de XSS em `chat/detalhe.js`,
  `auth/onboarding.js` e `projetos/detalhe.js` foram fechados com `utils/dom.js`. O `esc()`
  artesanal anterior não escapava aspas e era inseguro dentro de atributo.
- **Correção de 29/10 (PR #12):** os logs deixaram de incluir destinatário e assunto de e-mail (o
  assunto carrega o título do projeto, digitado pelo usuário) e fragmentos de nomes de chave
  derivados de e-mail ou IP. Isso fechou alertas de *log injection* e de exposição de informação
  privada.
- **Estado em 30/10:** 14 alertas corrigidos e 10 abertos, todos de severidade média (4 de *log
  forging* no backend e 6 de sanitização no frontend). Cerca de 200 usos de `innerHTML` em 30
  arquivos JS; `utils/dom.js` usado em 3 páginas. Em 31/10, depois dos PRs #19 a #21, eram 0
  alertas abertos e 24 corrigidos.

### D13 — CI

O CI foi criado em 29/10 (PR #13) e as actions foram atualizadas para Node 24 no PR #14. A suíte de
testes nasceu em 20/09, junto com a correção de proxy, e cresceu acompanhando a infraestrutura.

---

## 3. Correções de configuração registradas

### 3.1 `railway.toml` × configuração efetiva (auditoria de 30/10)

O arquivo existe desde 28/09 (PR #5) e declara `healthcheckPath = "/health/ready"` e
`drainingSeconds = 30`; o `preDeployCommand` está comentado. Nos 12 deploys de 28/09 a 30/10
consultados pela API da Railway:

- o manifesto efetivo de cada deploy trazia `healthcheckPath: null` e `drainingSeconds: null`;
- o `resolvedFileConfig` do serviço informava que nenhum arquivo de configuração tinha sido
  resolvido (`fileManifest` vazio), embora o `propertyFileMapping` do deploy listasse exatamente
  `deploy.healthcheckPath` e `deploy.drainingSeconds`;
- o log de build do deploy `d0e04f31` terminava em `image push`, sem a etapa de health check;
- nesse deploy, a versão nova começou a escutar às 02:26:02 UTC e a anterior recebeu SIGTERM às
  02:26:05. A conexão com o Redis só foi aberta às 02:26:17. Qualquer chamada ao `/health/ready`
  teria aberto essa conexão antes, porque o check `redis` depende dela. Ou seja, nenhum
  `/health/ready` rodou antes da troca de versão.

Na prática, durante esse período a promoção não esperava a readiness, o check `dados-cifrados` não
barrava uma chave errada e o tempo entre SIGTERM e SIGKILL era o padrão da plataforma.

**Causa.** O Config as Code foi depreciado pela Railway e só continua valendo para serviços que já
usavam o arquivo. O `tratoo` passou a ter um `railway.toml` só em 28/09 (o primeiro deploy, de
06/08, não tinha arquivo). A plataforma ainda faz o parse (por isso o `propertyFileMapping`), mas
descarta os valores. A CLI informa que os arquivos existentes deixam de funcionar de vez em
01/12/2026.

**Correção (30/10, com autorização).** `healthcheckPath = /health/ready` e `drainingSeconds = 30`
foram definidos nas configurações do serviço, seguidos de um redeploy. Evidência do deploy
`b40f6558` (04:38 UTC, commit `3389b87`): o manifesto efetivo traz os dois valores, e o log de
build mostra `Starting Healthcheck`, `Path: /health/ready`, `Retry window: 5m0s` e
`Healthcheck succeeded!`.

### 3.2 Linha do tempo do deploy `d0e04f31` (30/10, anterior à correção do health check)

- **02:22:45 UTC:** push na `main`. O deploy, o CI e o CodeQL são criados no mesmo segundo.
- **02:23:53:** o CI termina (1 min 08 s).
- **02:25:31:** o CodeQL termina (2 min 46 s).
- **02:25:48:** a imagem é gerada (anotação OCI `created`).
- **02:26:02:** a versão nova começa a escutar.
- **02:26:05:** a anterior recebe SIGTERM.

O build começou só depois de todos os checks do commit passarem, coerente com `checkSuites: true`.

---

## 4. Divergências encontradas na auditoria de 29–30/10/2026

| # | Onde | O que dizia | O que era | Ação registrada |
|---|---|---|---|---|
| 1 | `railway.toml` × Railway | Health check `/health/ready` e `drainingSeconds = 30` | Não aplicados de 28/09 a 30/10 | Corrigido em 30/10 nas configurações do serviço (§3.1) |
| 2 | Docs antigas e comentário do `BuscaSemanticaService` | Pesos 35/15/15/10/10/10/5 com "Disponibilidade" | 42/22/12/9/8/4/3 com "Comprovação" | Comentário corrigido em `9a9ec34` |
| 3 | Docs antigas | "Taxa da plataforma de 10%" | Não há taxa da plataforma. A `TaxaGateway` do Asaas é só informativa | Corrigido nas docs e no README |
| 4 | Docs antigas e comentário em `ContratoServicoService` | Cancelamento de contrato ativo sem entrega cobra 5% de taxa | O estorno é integral (`EstornarCancelamentoContratoAsync`) | Comentários do backend corrigidos em `9a9ec34` |
| 5 | README e docs antigas | Banco transacional em SQL Server | Só PostgreSQL desde 06/08 | Corrigido |
| 6 | Comentários de `EstadoEfemeroSetup`, `IEstadoEfemero` e `RateLimiterSetup` | "Produção sem Redis, 1 réplica" | Redis ativo em produção desde 29/10 (1 réplica) | Corrigido em `9a9ec34` |
| 7 | `TRILHA2-DECISOES.md` §7.1 | "Rotação pendente" | Rotação concluída em 24/10 | Corrigido em `9a9ec34` |
| 8 | Comportamento declarado do `dados-cifrados` | "Barra o deploy com a chave errada" | Não barrava enquanto o health check de deploy estava inativo (ver #1) | Resolvido com #1 |
| 9 | Redis de produção × compose | Compose sem persistência, por decisão (o cadastro pendente carrega o hash BCrypt da senha) | Produção nasceu do template da Railway com RDB (`save 60 1`) em volume | Registrado; o conteúdo em disco são valores cifrados (AES-256-GCM), nomes de chave em HMAC e contadores |
| 10 | Comentário legado no `TratooContext` | "TPH" | A configuração (`ToTable` por tipo) é TPT | Registrado |

---

## 5. Limitações registradas em 30/10/2026

- Uma réplica e nenhum ambiente de staging. A escala horizontal foi validada só no compose.
- Rollback manual, e configuração de deploy fora do repositório: o health check e o draining
  estão nas configurações do serviço, e o `railway.toml` não é lido.
- Migrations manuais. Não há verificação automática de que o schema de produção bate com o código.
- Latência entre regiões: a aplicação está em `sfo` e o banco em `us-east-2`. Na auditoria, os
  checks de banco levaram 690 ms e 417 ms, contra 2 ms do Redis; um diagnóstico anterior mediu de 59
  a 84 ms por handshake TCP entre o container e o Neon ([`TRILHA2-DECISOES.md` §7.3](TRILHA2-DECISOES.md)).
- Pagamentos em sandbox, sem reconciliação automática com o gateway. Existe só
  `SincronizarStatusAsync` manual e reprocessamento de transferência pelo admin. Também não há
  relatório "entrou/saiu/retido".
- Criptografia de dados em AES-CBC sem autenticação (sem MAC) e com derivação de chave simples (sem
  KDF).
- Frontend: CSP com `'unsafe-inline'`, cerca de 200 usos de `innerHTML` ainda não migrados e 10
  alertas de CodeQL de severidade média em aberto.
- Testes: sem cobertura de ponta a ponta das regras de negócio (proposta → contrato → pagamento →
  avaliação) e sem testes de frontend.
- Dependências: Dependabot desativado. `Azure.Storage.Blobs` referenciado sem uso. `node_modules/`
  e `package.json` do SDK npm `railway` versionados por engano (471 arquivos).
- Logs em arquivo dentro do container se perdem a cada deploy. Os logs que valem são os do stdout,
  coletados pela Railway.
- Webhook do Asaas: a validação depende de o token estar configurado.
- Configuração da Railway: o Config as Code foi depreciado, e o `railway.toml` precisa ser mantido
  em sincronia à mão com as configurações reais.
- `Logging__LogLevel__*` configuradas na Railway provavelmente não alteram o nível de log, porque o
  Serilog é configurado no código e não lê a seção `Logging` (inferido, não testado).
- Não verificados na auditoria: o console do Neon (plano, backups e PITR); o conteúdo de
  `__EFMigrationsHistory` em produção; os painéis do Asaas, do Resend, do R2 e da OpenAI; e a
  execução de ponta a ponta dos fluxos de negócio em produção.

---

## 6. Roadmap registrado em 30/10/2026

Nada abaixo estava implementado naquela data.

| Prioridade | Item | Por quê |
|---|---|---|
| Média | Ativar o `preDeployCommand` (`--migrate-only`) nas configurações do serviço, depois de confirmar o `__EFMigrationsHistory` e criar `ConnectionStrings__MigrationConnection` | Migrations automáticas, uma vez por deploy, com falha bloqueando a promoção |
| Baixa | Levar a configuração de deploy de volta para o repositório via Infrastructure as Code (`.railway/railway.ts`), ou remover o `railway.toml` e documentar as configurações | A configuração efetiva fica fora do Git |
| Média | Reconciliação periódica de pagamentos e retentativa automática de transferência | Itens de compliance deferidos (reconciliação, retentativa, relatório) |
| Média | Formato de cifra autenticado e versionado (AES-GCM) para o `DataProtector`, migrando com o mesmo mecanismo de rotação | Integridade dos dados sensíveis |
| Média | Fechar os alertas abertos do CodeQL e migrar os `innerHTML` restantes para `utils/dom.js`; depois remover `'unsafe-inline'` (nonces) | Reduz a superfície de XSS |
| Média | Tornar `Asaas__WebhookToken` obrigatório na subida | Elimina o caminho em que a validação é pulada |
| Baixa | Ativar o Dependabot; remover `Azure.Storage.Blobs`, `node_modules/` e `package.json` | Higiene de dependências e do repositório |
| Baixa | Alinhar as regiões da aplicação e do banco | Maior ganho de latência disponível |
| Baixa | Decidir explicitamente sobre a persistência do Redis de produção (RDB ligado) | Era padrão do template, não uma decisão |
| Baixa | Testes de ponta a ponta dos fluxos de negócio | Cobrir o que a suíte de infraestrutura não cobre |
| Baixa | Continuar a migração visual das páginas restantes | Consistência e manutenção do frontend |

---

## 7. Itens que não fazem parte da arquitetura atual

| Item | Situação | Evidência |
|---|---|---|
| SQL Server | Removido em 06/08 | `bf6761d` |
| Serviço Python de IA | Substituído pela chamada direta à OpenAI no `EmbeddingService` | Ausente do repositório |
| Azure App Service | Perfis de publicação removidos | `4dee3a7`; pacote `Azure.Storage.Blobs` retirado em `9a9ec34` |
| Diagnóstico temporário `[DIAG-PROXY]` | Removido | Ausente do código |
| `railway.toml` como fonte da configuração de deploy | A Railway não lê o arquivo | `resolvedFileConfig` vazio em 30/10 |
| Suporte a tema escuro nas páginas de perfil | Removido | `32872b2` (PR #15) |
| Tratoo.Shared | Nunca existiu; o código compartilhado sempre ficou em `Tratoo.Domain/Features/Shared/` | Histórico Git |

---

## 8. Método da auditoria de 29–30/10/2026

O `ARQUITETURA.md` foi reconstruído (PR #16) a partir do código, do histórico Git (agosto a outubro
de 2026), da suíte de testes e da configuração de produção na Railway. Estado de referência naquela
data: commit `3389b87` (merge do PR #15), nos deploys `d0e04f31` e `b40f6558`. Fontes, da mais
forte para a mais fraca: comportamento observado (health checks públicos, logs de deploy,
`railway ssh` e `redis-cli` só para leitura, suíte de testes 238/238 local com Docker em 5 min 09 s
e no CI); configuração da Railway lida pela CLI 5.58 e pela API GraphQL só com *queries*; código da
`main`; histórico Git e PRs; documentos anteriores, usados apenas como pista. A legenda daquela
versão também tinha os rótulos Planejado, Removido e Divergência.

Observações da época que não foram reverificadas depois: runtime de produção `8.0.33`; PID 1 com UID
1654 e um único socket em LISTEN na 8080; `/app` do root com permissão 755 e `/app/logs` do usuário
`app`; nenhum `appsettings.json` na imagem; `INFO commandstats` do Redis com `SETEX`, `GET`,
`EVAL`/`EVALSHA`, `INCR`, `INCRBY`, `PEXPIRE` e `TIME`, e `DBSIZE` 0 no momento da inspeção;
`Redis__ChaveProtecao` com 32 bytes; `PORT` 8080 numa inspeção de 29/09; Dependabot desativado e
secret scanning sem alertas.
