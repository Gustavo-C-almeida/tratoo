# Relatório da auditoria de segurança da API

**Data:** 2026-11-10 · **Commit auditado:** `0c253ae` (main) · **Escopo:** autenticação, autorização por papel
(contratante × prestador × admin), isolamento de recursos e rate limiting do `Tratoo.API`.

Inventário e matriz de intenção: [`endpoint-matrix.md`](endpoint-matrix.md).
Testes: [`Tratoo.Tests/Seguranca/`](../../Tratoo.Tests/Seguranca/).

## Status das correções

| # | Status | Commit | Teste de regressão (vermelho → verde) |
|---|---|---|---|
| D2 | ✅ Corrigido | `62986b1` | `ProjetoRascunhoOuPrivado_NaoApareceParaTerceiros` + `…_ContinuaVisivelParaQuemParticipa` |
| D3 | ✅ Corrigido | `db24107` | `Entrega_DeQuemNaoEOPrestador_NaoGravaArquivoNoBucketPrivado` + `Entrega_DoPrestadorDoContrato_ContinuaFuncionando_…` + matriz (rota com policy Prestador) |
| D4 | ✅ Corrigido | `af9b3bb` | `BuscaPublica_ComIA_TemRateLimit` + `BuscaSemTexto_ContinuaSemLimite_…` + matriz (política `busca`) |
| D1 | Decidido, a implementar | — | `/health/*` anônimo só com o status; `/api/admin/health` com os detalhes (Admin + reautenticação). |
| D5, D6, D7, D9, D8, D10, D11, D12 | Em aberto | — | Testes de achado continuam ignorados por padrão. |

Com `TRATOO_REPRODUZIR_ACHADOS=1`, a suíte de segurança agora dá 467 testes: 448 aprovados e 19 reprovados. Os
19 são os achados ainda abertos: D1, D5 (8 rotas), D6 (7 rotas), D7, D11 e D12.

O restante deste relatório descreve o estado **auditado** (`0c253ae`), antes das correções.

## Resumo executivo

**Veredito: aprovado com ressalvas.** Os quatro requisitos centrais se sustentam nos testes contra a API real:

| Requisito | Resultado |
|---|---|
| Todo endpoint privado exige autenticação | ✅ **90/90** rotas protegidas recusam com 401: sem token e com 9 tipos de token inválido, tanto por cookie quanto por header. Todas aceitam um token válido do papel certo. |
| Rota de contratante não atende prestador, e vice-versa | ✅ 44/44 rotas com policy de papel (19 de contratante, 25 de prestador) respondem 403 ao papel errado. ⚠️ Em 8 rotas o papel só é checado no serviço. Ninguém ganha acesso, mas a recusa sai como 400 em vez de 403 (D6). |
| Endpoints de admin só para admin autenticado | ✅ 5/5 rotas admin dão 403 para contratante e prestador, mesmo com headers, query ou body forjados. Token forjado é recusado, e as partes da disputa não conseguem resolvê-la. ❌ **Health check é anônimo (D1)**, por exigência da Railway. |
| Rate limit funciona de fato | ✅ As 12 rotas limitadas dão 429 + `Retry-After: 60` no limite exato, com contador compartilhado entre réplicas. A janela reabre, e não há contorno por XFF, X-Real-IP, Forwarded, peer não confiável ou variação de caminho. ❌ **102 rotas não têm limite (D4, D5)**, e não há contenção por conta (D7). |

São **3 achados de severidade Alta** (D2, D3, D4), que pedem correção antes de considerar a API segura:

- **D2:** projetos em rascunho e privados ficam legíveis sem login.
- **D3:** qualquer usuário autenticado grava arquivos no bucket privado.
- **D4:** a busca anônima gera chamadas pagas à OpenAI sem limite.

## Como executar

```bash
dotnet test Tratoo.sln --filter "FullyQualifiedName~Tratoo.Tests.Seguranca"
```

- **Com Docker** (como no CI, `TRATOO_EXIGIR_DOCKER=1`), a suíte sobe `pgvector/pgvector:pg16` e `redis:7-alpine` sozinha.
- **Sem Docker**, aponte para serviços já em execução:
  ```bash
  TRATOO_SEGURANCA_PG="Host=127.0.0.1;Port=5432;Username=<usuário que cria bancos>;Password=<senha>" \
  TRATOO_SEGURANCA_REDIS="127.0.0.1:6379" \
  dotnet test Tratoo.sln --filter "FullyQualifiedName~Tratoo.Tests.Seguranca"
  ```
- **Para reproduzir os achados em aberto**, que ficam ignorados por padrão para não travar o CI, acrescente
  `TRATOO_REPRODUZIR_ACHADOS=1`. Cada teste de achado afirma o comportamento **seguro**, então falha enquanto a
  vulnerabilidade existir. A correção remove o `[FactAchado]`/`[TheoryAchado]` e o teste passa a ser obrigatório.

A suíte já entra no `dotnet test Tratoo.sln` do CI (job *Build e testes*) sem mudança no workflow. Ela adiciona
cerca de 1 min 40 s ao job. A suíte inteira do repositório rodou duas vezes seguidas, com **869 aprovados e 0 falhas**.

### Como o teste funciona

- A **API real** (`dotnet Tratoo.API.dll`, Production) roda em processo separado, sobre Postgres + pgvector e Redis
  reais. São duas réplicas compartilhando o Redis, mais uma instância Development usada só para ler o Swagger.
  O que se testa é o `Program.cs` de produção, não um pipeline montado à mão.
- **Resend, OpenAI, Asaas e R2** apontam para um servidor HTTP local falso (`ServidorExternoFalso`). Os códigos de
  MFA, cadastro e reautenticação são lidos dos e-mails capturados, então o login de admin passa pelo MFA de
  verdade. As chamadas à OpenAI e os objetos gravados no R2 são contados.
- **A massa de teste é semeada direto no banco:** 2 contratantes e 2 prestadores (pares A e B), um par com
  disputa aberta, 1 admin (MFA obrigatório), usuários de varredura e projetos em rascunho e privados.
- **As requisições chegam como na Railway:** o peer é o proxy, e o `X-Forwarded-For` tem a forma
  `cliente, borda`. Cada teste usa IPs de cliente inéditos, então os baldes de rate limit não interferem entre si.
- **Resultado com os achados ligados:** 461 testes, 438 aprovados e 23 reprovados. Os 23 reprovados são
  exatamente os achados D1, D2, D3, D4, D5 (8 rotas), D6 (8 rotas), D7, D11 e D12.

## Achados

| # | Sev. | Endpoint(s) | Evidência (obtido × esperado) | Impacto | Correção sugerida |
|---|---|---|---|---|---|
| **D2** | **Alta** | `GET /api/projects/{id}` | `ProjetoRascunhoOuPrivado_NaoApareceParaTerceiros`: anônimo recebe **200** com o título e a descrição do projeto **rascunho** e do **privado** de outro contratante. Esperado: 404. | Vazamento de dados de negócio não publicados. Como o ID é inteiro sequencial, dá para enumerar todos os projetos. | No detalhe, devolver 404 se `Status == Rascunho` ou `Visibilidade == Privado`, a menos que o usuário seja o dono (ou o prestador convidado/selecionado). |
| **D3** | **Alta** | `POST /api/contratos/{id}/entrega` | `Entrega_DeQuemNaoEOPrestador_…`: prestador B **e** contratante B gravam `privado/entregas/{contrato de A}/{guid}.pdf` no bucket e só depois recebem **400**. Esperado: 403 e nada gravado. | Qualquer conta grava até 10 × 20 MB por requisição, sem rate limit, no bucket privado de contratos de terceiros: custo e armazenamento de conteúdo arbitrário. | Rota com `RequireAuthorization("Prestador")`. Validar dono, status do contrato e pagamento **antes** do upload (mover a checagem para o início, ou validar no handler). Mais rate limit no upload. |
| **D4** | **Alta** | `GET /api/busca/prestadores`, `GET /api/busca/projetos` | `BuscaPublica_ComIA_TemRateLimit`: **40 buscas anônimas do mesmo IP, 40 × 200 e 40 chamadas à OpenAI**. Esperado: 429 antes disso. | Abuso de custo e de cota da OpenAI. Esgotar a cota derruba a busca para todos. | Política por IP (ex.: 30/min), cache de embedding por `q` normalizado (`IMemoryCache`/Redis) e limite de tamanho de `q`. |
| D5 | Média | 102 rotas sem limite. Testadas: `cadastro/confirmar`, `contratos/{id}/assinar`, `projects/{id}/mensagens`, `pagamentos/iniciar`, `prestadores/me/foto`, `contratos/{id}/entrega`, `cep/{cep}`, `admin/disputas` | `RotaSensivel_TemRateLimit`: **40 requisições seguidas, nenhuma 429** em todas as 8. `confirmar` e `assinar` ficam mitigadas pelo limite de 5 tentativas por código (provado em `ConfirmacaoDeCadastro_…`). | Spam de chat, abuso de upload, chamadas ao Asaas sem limite (`iniciar`, `sincronizar`) e proxy anônimo do ViaCEP. | `GlobalLimiter` por usuário/IP como piso (ex.: 120/min), mais políticas específicas para upload, chat, pagamentos e CEP. |
| D1 | Média | `/health/live`, `/health/ready` | `HealthCheckDetalhado_SoParaAdmin`: anônimo recebe **200** com o nome da instância, as dependências (redis, postgres, pgvector), a duração e o estado da chave de dados e da recifragem. `HealthChecks_NaoVazamSegredos…` passa: sem connection string, chave, stack trace ou mensagem de exceção. | Reconhecimento da infraestrutura. O requisito "somente admin" não é atendido. | A Railway (`healthcheckPath=/health/ready`) e o Docker não mandam credencial, então **a decisão é sua**. Recomendo: `/health/live` e `/health/ready` anônimos devolvendo só o `status`, e um `/api/admin/health` (policy Admin + SV) com os detalhes. |
| D7 | Média | `POST /usuarios/login` | `LoginDistribuido_ContraUmaConta_EContido`: **30 senhas erradas de 30 IPs** e a senha certa segue entrando (200). | Credential stuffing e força bruta distribuída contra uma conta não são contidos. Admin fica mitigado pelo MFA obrigatório. | Contador de falhas por conta no estado efêmero (Redis), com atraso progressivo ou bloqueio temporário, mais aviso por e-mail. |
| D9 | Média | `POST /api/admin/pagamentos/{id}/reprocessar-transferencia` | Verificado no código (`PagamentoExtensions.cs:167`): admin **sem** `ExigirReautenticacaoRecente`, diferente de `resolver`. | Uma sessão de admin sequestrada move dinheiro sem nova confirmação. | `.ExigirReautenticacaoRecente()`. |
| D6 | Baixa | 8 rotas: 6 de vínculo de competência, `entrega` e `entrega/rejeitar` | `RotaDeUmPapel_ComTokenDoOutroPapel_Responde403`: o papel errado recebe **400** (`NegocioException` do serviço). Esperado: 403 na rota. `entrega/aprovar` dá 403, mas por causa do filtro de reautenticação. | Nenhum acesso indevido (o serviço barra), mas sem defesa em profundidade. Uma refatoração do serviço abriria a rota. | `RequireAuthorization("Prestador"/"Contratante")` nas rotas. |
| D11 | Baixa | todas (sessão aberta) | `UsuarioBloqueadoComSessaoAberta_PerdeOAcesso`: com `Status = Blocked` no banco, a sessão existente segue com **200**. Por desenho, o refresh relê a conta e derruba a sessão, então a janela é **≤ 15 min** (um access token). | Um usuário bloqueado age por até 15 min. | Ao bloquear (hoje só via banco), chamar `SessaoService.RevogarTodasAsync`. Se o bloqueio virar endpoint admin, fazer isso nele. |
| D12 | Baixa | `/api/admin/*` | `AdminRebaixadoNoBanco_PerdeOAcessoAdministrativo`: com `IsAdmin = false` no banco, o token atual segue com **200** em `/api/admin/disputas`. Janela **≤ 10 min**, e o refresh revoga (`MotivoPapelAlterado`). | Um admin removido opera por até 10 min. | Revogar as sessões ao remover o admin, ou conferir `EhAdmin` na sessão a cada requisição admin. |
| D8 | Baixa | `POST /usuarios/cadastro*` | Body `{}` dá **500** (`NullReferenceException`), e `tipo: "admin"` dá **500** (`ArgumentException` do `UsuarioFactory`). Nenhum usuário é criado (`Cadastro_NaoCriaAdmin_…` passa). | Ruído em log/alerta e mensagens de erro inconsistentes. | Validar o request (campos obrigatórios e `Tipo ∈ {prestador, contratante}`) e devolver 400. |
| D10 | Baixa | `/api/dev/seed-admin` | Em Development, promove qualquer e-mail a Admin **sem autenticação**. Em Production a rota não existe (`RotasDeSeed_NaoExistemEmProducao` passa). | Escalada total se Development for exposto por engano. | Exigir também um segredo (`Seed:Token`), ou recusar quando o host não for loopback. |
| I1 | Info | rate limit | Com o Redis fora do ar, o limite degrada para memória **por réplica** (documentado; coberto por `EstadoEfemeroTests`). | Limite efetivo × réplicas durante a queda. | — |
| I2 | Info | rate limit | O teste prova que a cadeia `forjado, cliente, borda` não escapa e que um peer fora da faixa não escolhe o próprio IP. **Não verificado em produção:** se a borda da Railway sobrescreve ou acrescenta o XFF. | — | Rodar `GET /api/diagnostico/rede` (admin) em produção com um `X-Forwarded-For` forjado e conferir `ipDoCliente`. |
| I3 | Info | rate limit | A janela no Redis é **alinhada ao minuto cheio**: 10 logins em `:59.9` + 10 em `:00.1` passam todos, ou seja, 2 × limite em cerca de 1 s. | Inerente à janela fixa. | Aceitar, ou migrar para janela deslizante (dois contadores ponderados no mesmo script Lua). |

## O que foi verificado e passou

**Autenticação** (`AutenticacaoTests`)
- **Inventário:** a lista de rotas da API real (Swagger da instância Development) é **igual** à `MatrizEndpoints`. Rota nova sem decisão de guarda quebra o teste.
- **Rotas protegidas (90):** sem token, todas dão 401.
- **Tokens inválidos (9 tipos):** malformado, lixo, assinatura com outra chave, expirado, `nbf` no futuro, sem `sid`, `sid` inexistente, emissor/audiência errados e `alg: none`. Todos dão 401, por cookie e pelo header `Authorization`.
- **Token válido:** um token válido do papel certo nunca recebe 401.
- **Rotas públicas (22, mais os 2 health checks do D1):** não exigem token. As exceções são `refresh` e o webhook, que recusam pela credencial própria.
- **Webhook:** token errado dá 401, token certo dá 200.
- **Ciclo da sessão:**
  - Logout invalida o token na hora (`SESSAO_ENCERRADA`).
  - Uma sessão criada numa réplica vale na outra, e a revogação também.
  - Conta excluída derruba o token e o login.
  - Usuário bloqueado ou pendente não loga e não recebe cookie.
- **Seed:** as rotas `/api/dev/*` não existem em Production.

**Papéis e admin** (`AutorizacaoPapelTests`)
- **Papel cruzado:** com o papel errado, 19 rotas de contratante, 25 de prestador e 5 de admin dão 403.
- **Escalada pelo cliente não funciona:**
  - Headers `X-Admin`, `X-Role`, `X-User-Role` e `X-Is-Admin`.
  - Query `?admin=true&role=Admin`.
  - Body com `isAdmin`/`role`.
  - Token com papel `Admin` assinado por outra chave (dá 401).
- **Admin lê** `/api/admin/disputas`, o detalhe de uma disputa e `/api/diagnostico/rede`.
- **Resolver disputa:**
  - As **partes** da disputa, mesmo reautenticadas, recebem 403.
  - O admin sem reautenticação recebe 403 `REAUTH_REQUIRED`.
  - O admin com senha + MFA resolve (200), e a segunda tentativa dá 400.
  - O banco registra `ResolvidoPorId = admin` e `ResolvidaEm`, e o `AuditLog` tem `disputa_resolvida` com o **IP real do cliente**.
- **Cadastro não cria admin:**
  - `tipo` admin/Admin/ADMIN é recusado e nenhum usuário é criado.
  - Campos `isAdmin`, `role`, `tipoUsuario` e `status` no cadastro e na confirmação são ignorados, e o login devolve `isAdmin: false`.
- **Health:** não vaza connection string, chaves, token do webhook nem stack trace.
- **Variações de método e caminho** (maiúsculas, barra final, `//`, `./`, `../`, `%2f`, HEAD, OPTIONS, PUT, POST, DELETE) não contornam a guarda de admin.

**Isolamento** (`IsolamentoRecursosTests`)
- **37 tentativas de B sobre os recursos de A** dão 400, 403 ou 404, sem dados de A na resposta.
  - Recursos: contrato, PDF, entrega, OTP, assinatura, cancelamento, aprovação/rejeição de entrega, pagamento (detalhe, PIX, sincronizar, iniciar, liberar, estornar, disputar), proposta (ler, contrapropor, aceitar, recusar, excluir, enviar), projeto (propostas, publicar, cancelar, convites, recomendações), chat (ler e escrever, nos dois papéis) e competência no perfil alheio.
  - B se reautenticou nas rotas que pedem, então a recusa vem da checagem de dono.
- **Os recursos de A ficam intactos no banco:** status do contrato, pagamento, proposta e projetos, e nenhuma mensagem, disputa ou convite criado.
- **Sessões:** B não encerra a sessão de A (404), e a sessão de A segue válida.
- **Mass assignment:** `id`, `isAdmin`, `role`, `tipoUsuario`, `status`, `email`, `senhaHash` e `identidadeVerificada` no PUT de perfil são ignorados, nos dois papéis.

**Rate limit** (`RateLimitTests`)
- **Limite exato:** as 12 rotas limitadas aceitam N e dão 429 na N+1, com `Retry-After: 60` e a mensagem em JSON. Outro cliente não é afetado.
- **Réplicas:** 5 requisições numa réplica e 5 na outra levam a 429 nas duas.
- **Janela:** depois do bloqueio, o acesso volta em até 64 s.
- **Sem contorno por header:** XFF com IPs falsos à esquerda, `X-Real-IP`, `Forwarded`, `CF-Connecting-IP`, `True-Client-IP`, `X-Client-IP` e `X-Originating-IP`.
- **Peer fora da faixa confiável** (conexão direta pelo IP da interface): o XFF forjado é descartado e o limite vale.
- **Variações de caminho** (`/`, maiúsculas, `//`, `./`, query) caem no mesmo balde. Outros métodos na rota de login dão 405.
- **Confirmação de cadastro:** não tem rate limit, mas o código morre na 5ª tentativa errada (a 6ª, com o código certo, falha).

## Matriz final (rota × resultado)

Execução com os achados ligados (`TRATOO_REPRODUZIR_ACHADOS=1`). ✅ aprovado · ❌ achado · — não se aplica.
"Papel errado" = token do outro papel (contratante ↔ prestador; ambos para rotas admin).

| Rota | Intenção | Sem token → 401 | Token inválido → 401 | Token válido ≠ 401 | Papel errado → 403 | Rate limit |
|---|---|---|---|---|---|---|
| `POST /usuarios/login` | público | pública ✅ | — | — | — | login ✅ |
| `POST /usuarios/login/mfa` | público | pública ✅ | — | — | — | login ✅ |
| `POST /usuarios/logout` | público | pública ✅ | — | — | — | — |
| `POST /usuarios/cadastro` | público | pública ✅ | — | — | — | cadastro ✅ |
| `POST /usuarios/cadastro/reenviar-codigo` | público | pública ✅ | — | — | — | cadastro ✅ |
| `POST /usuarios/cadastro/confirmar` | público | pública ✅ | — | — | — | ❌ D5 |
| `POST /usuarios/senha/resetar/solicitar` | público | pública ✅ | — | — | — | senha ✅ |
| `POST /usuarios/senha/resetar` | público | pública ✅ | — | — | — | senha ✅ |
| `POST /usuarios/onboarding` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `DELETE /usuarios/conta` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `GET /api/me` | autenticado | ✅ | ✅ | ✅ | ✅ | — |
| `POST /api/auth/refresh` | público | pública ✅ | — | — | — | refresh ✅ |
| `POST /api/auth/logout` | público | pública ✅ | — | — | — | — |
| `POST /api/auth/logout-todas` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `GET /api/auth/sessoes` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `DELETE /api/auth/sessoes/{id}` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/auth/atividade` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/auth/reautenticar` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | reautenticar ✅ |
| `GET /api/conta/seguranca` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/conta/senha` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `PUT /api/conta/mfa` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `GET /api/admin/disputas` | admin | ✅ | ✅ | ✅ | ✅ | ❌ D5 |
| `GET /api/admin/disputas/{disputaId:guid}` | admin | ✅ | ✅ | ✅ | ✅ | — |
| `POST /api/admin/disputas/{disputaId:guid}/resolver` | admin | ✅ | ✅ | ✅ | ✅ | — |
| `POST /api/admin/pagamentos/{id:guid}/reprocessar-transferencia` | admin | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/diagnostico/rede` | admin | ✅ | ✅ | ✅ | ✅ | — |
| `GET /health/live` | admin | ❌ anônima (D1) | — | — | — | — |
| `GET /health/ready` | admin | ❌ anônima (D1) | — | — | — | — |
| `POST /api/webhooks/asaas` | público | pública ✅ | — | — | — | — |
| `GET /api/config/financeiro` | público | pública ✅ | — | — | — | — |
| `GET /api/cep/{cep}` | público | pública ✅ | — | — | — | ❌ D5 |
| `GET /api/busca/prestadores` | público | pública ✅ | — | — | — | ❌ D4 |
| `GET /api/busca/prestadores/{id:int}/similares` | público | pública ✅ | — | — | — | — |
| `GET /api/busca/projetos` | público | pública ✅ | — | — | — | ❌ D4 |
| `GET /api/busca/projetos/{id:int}/prestadores-recomendados` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/busca/projetos/recomendados` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/projects` | público | pública ✅ | — | — | — | — |
| `GET /api/projects/{id:int}` | público | pública ✅ | — | — | — | — |
| `POST /api/projects` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `POST /api/projects/{id:int}/publish` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `DELETE /api/projects/{id:int}` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/me/projects` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/projects/{id:int}/proposals` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/projects/{id:int}/mensagens` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/projects/{id:int}/mensagens` | autenticado | ✅ | ✅ | ✅ | ✅ | ❌ D5 |
| `GET /api/me/chats` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/projects/{projetoId:int}/convites` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/projects/{projetoId:int}/convites` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `PUT /api/convites/{id:guid}/aceitar` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `PUT /api/convites/{id:guid}/recusar` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/me/convites` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `POST /api/convites/{conviteId:guid}/proposta` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `POST /api/propostas` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `POST /api/propostas/{id:guid}/enviar` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `PUT /api/propostas/{id:guid}/aceitar-negociacao` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `POST /api/propostas/{id:guid}/contraproposta` | autenticado | ✅ | ✅ | ✅ | ✅ | — |
| `PUT /api/propostas/{id:guid}/aceitar` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `PUT /api/propostas/{id:guid}/recusar` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `DELETE /api/propostas/{id:guid}` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/propostas/{id:guid}` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `GET /api/me/propostas` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/contratos/{id:guid}` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `GET /api/me/contratos` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/contratos/{id:guid}/assinar/solicitar-otp` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | otp-assinatura ✅ |
| `POST /api/contratos/{id:guid}/assinar` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | ❌ D5 |
| `GET /api/contratos/{id:guid}/pdf` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/contratos/{id:guid}/entrega` | prestador | ✅ | ✅ | ✅ | ❌ D6 (400) | ❌ D5 |
| `GET /api/contratos/{id:guid}/entrega` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/contratos/{id:guid}/entrega/aprovar` | contratante | ✅ | ✅ | ✅ | ✅ 403, mas pelo filtro de reautenticação (D6) | — |
| `POST /api/contratos/{id:guid}/entrega/rejeitar` | contratante | ✅ | ✅ | ✅ | ❌ D6 (400) | — |
| `DELETE /api/contratos/{id:guid}` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `GET /api/contratos/{contratoId:guid}/avaliacoes/pendente` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/pagamentos/iniciar` | contratante | ✅ | ✅ | ✅ | ✅ | ❌ D5 |
| `GET /api/pagamentos/{id:guid}/pix` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/pagamentos/{id:guid}` | autenticado | ✅ | ✅ | ✅ | ✅ | — |
| `POST /api/pagamentos/{id:guid}/liberar` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `POST /api/pagamentos/{id:guid}/disputar` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/pagamentos/{id:guid}/estornar` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `GET /api/pagamentos/{id:guid}/sincronizar` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `GET /api/avaliacoes/{id:guid}` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `POST /api/avaliacoes/{id:guid}/enviar` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `PUT /api/me/avaliacoes/privacidade` | autenticado | ✅ | ✅ | ✅ | n/a (qualquer papel) | — |
| `GET /api/usuarios/{id:int}/reputacao` | público | pública ✅ | — | — | — | — |
| `GET /api/usuarios/{id:int}/avaliacoes` | público | pública ✅ | — | — | — | — |
| `GET /contratantes/{id}/perfil` | público | pública ✅ | — | — | — | — |
| `GET /contratantes/me/perfil` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `PUT /contratantes/me/perfil` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `POST /contratantes/me/foto` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `DELETE /contratantes/me/foto` | contratante | ✅ | ✅ | ✅ | ✅ | — |
| `GET /prestadores/{id}/perfil` | público | pública ✅ | — | — | — | — |
| `GET /prestadores/me/perfil` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `PUT /prestadores/me/perfil` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `POST /prestadores/me/foto` | prestador | ✅ | ✅ | ✅ | ✅ | ❌ D5 |
| `POST /prestadores/me/portfolio` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `PUT /prestadores/me/portfolio/{id}` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `DELETE /prestadores/me/portfolio/{id}` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `POST /prestadores/me/experiencias` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `PUT /prestadores/me/experiencias/{id}` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `DELETE /prestadores/me/experiencias/{id}` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `POST /prestadores/me/certificacoes` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `PUT /prestadores/me/certificacoes/{id}` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `DELETE /prestadores/me/certificacoes/{id}` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `POST /prestadores/{prestadorId}/competencias` | prestador | ✅ | ✅ | ✅ | ✅ (Forbid no handler) | — |
| `DELETE /prestadores/{prestadorId}/competencias/{id}` | prestador | ✅ | ✅ | ✅ | ✅ (Forbid no handler) | — |
| `POST /prestadores/me/experiencias/{expId}/competencias/{compId}` | prestador | ✅ | ✅ | ✅ | ❌ D6 (400) | — |
| `DELETE /prestadores/me/experiencias/{expId}/competencias/{compId}` | prestador | ✅ | ✅ | ✅ | ❌ D6 (400) | — |
| `POST /prestadores/me/certificacoes/{certId}/competencias/{compId}` | prestador | ✅ | ✅ | ✅ | ❌ D6 (400) | — |
| `DELETE /prestadores/me/certificacoes/{certId}/competencias/{compId}` | prestador | ✅ | ✅ | ✅ | ❌ D6 (400) | — |
| `POST /prestadores/me/portfolio/{portId}/competencias/{compId}` | prestador | ✅ | ✅ | ✅ | ❌ D6 (400) | — |
| `DELETE /prestadores/me/portfolio/{portId}/competencias/{compId}` | prestador | ✅ | ✅ | ✅ | ❌ D6 (400) | — |
| `GET /prestadores/me/dados-bancarios` | prestador | ✅ | ✅ | ✅ | ✅ | — |
| `POST /prestadores/me/dados-bancarios/solicitar-alteracao` | prestador | ✅ | ✅ | ✅ | ✅ | dados-bancarios ✅ |
| `POST /prestadores/me/dados-bancarios/confirmar` | prestador | ✅ | ✅ | ✅ | ✅ | dados-bancarios ✅ |
| `PUT /prestadores/me/dados-bancarios` | prestador | ✅ | ✅ | ✅ | ✅ | dados-bancarios ✅ |

## O que não foi verificado

| Item | Por quê |
|---|---|
| Comportamento da borda da Railway com XFF forjado (I2) | Exige uma requisição real em produção. Estes testes não tocam produção. |
| Configuração real de produção (variáveis, `ASPNETCORE_ENVIRONMENT`, segredos) | Fora do repositório. O Dockerfile e o railway.toml não definem Development. |
| Integrações reais (Asaas, R2, OpenAI, Resend) | Substituídas pelo servidor falso. A auditoria é da guarda da API, não dos provedores. |
| Fluxo completo de assinatura com OTP (5 tentativas) | O controle existe no código (`ContratoServicoService`, `MaxTentativasOtp`) e nos testes `OtpConcorrenciaTests`/`RedisReplicasTests`. Aqui só se provou que `assinar` não tem rate limit. |
| Redis fora do ar durante a requisição | Coberto pelos testes existentes (`EstadoEfemeroTests`: rate limit degrada, OTP falha fechado, sessão sensível dá 503). Não foi repetido contra a API real. |
| `reprocessar-transferencia` sem reautenticação (D9) | Verificado só no código. Não há teste de achado, porque exigir reautenticação ali é uma decisão de produto. |
| CSRF | O cookie é `SameSite=Strict` + `HttpOnly` + `Secure`. Não foi testado a fundo porque está fora do escopo pedido. |

## Próximos passos (Fase 5)

Correções propostas, uma por commit, cada uma removendo o `[FactAchado]` do teste que a reproduz:

1. **Alta:** D2 (filtrar o detalhe de projeto), D3 (policy + checagem antes do upload), D4 (rate limit + cache na busca).
2. **Média:** D5 (`GlobalLimiter` + políticas de upload, chat, pagamento e CEP), D7 (contenção por conta), D9 (reautenticação no reprocessamento), D1 (conforme a sua decisão sobre o health check).
3. **Baixa:** D6, D8, D10, D11, D12.

Pelo fluxo de `Docs/CICD.md`, tudo sai de `fix/auditoria-seguranca-api` (criada a partir da `main`), com PR para a
`staging` e, depois da homologação, PR `staging` → `main`.
