# Matriz de endpoints — auditoria de segurança (Fase 1)

**Data:** 2026-11-10 · **Commit auditado:** `0c253ae` (main) · **Escopo:** `Tratoo.API` (ASP.NET Core 10, Minimal APIs)

## Como o inventário foi levantado

1. **Estático:** varredura de todo `Map*` / `MapGroup` / `MapHealthChecks` na solução (exceto testes). Não há
   controllers MVC, SignalR, `MapFallback`, websockets nem `MapWhen`. São **116 rotas**: 114 em Production e
   +2 `/api/dev/*` só em Development.
2. **Runtime:** a API real (`dotnet Tratoo.API.dll`) rodou num Postgres 16 + pgvector e num Redis locais e
   descartáveis, com segredos aleatórios de teste:
   - Em Development, o `swagger.json` tem **114 operações**, o que bate com o estático (116 − 2 health checks, que
     não entram no ApiExplorer).
   - Em Production, **todas as 116 URLs** foram chamadas **sem token**. O resultado está na coluna "Anon (prod)".

## Mecanismos encontrados

| Item | Onde | Como funciona |
|---|---|---|
| Autenticação | `SessaoAutenticacaoSetup` | JWT Bearer (HS256, `kid`, `ClockSkew=0`) lido do cookie httpOnly `tratoo_auth` (SameSite=Strict), com fallback para o header `Authorization`. Em toda requisição a sessão é conferida no `ISessaoStore` (Redis). |
| Papéis | `JwtService` + `AddAuthorization` | Claim `role` = `Prestador` ou `Contratante` (do `TipoUsuario`); `Admin` é acrescentado se `Usuario.IsAdmin`, flag que só muda via banco/seed. Policies: `Prestador`, `Contratante`, `Admin`. |
| **Fallback policy** | — | **Não existe.** Uma rota sem `RequireAuthorization` é pública por padrão. |
| Sessão verificada (SV) | `RequireSessaoVerificada` | Com o Redis fora do ar, responde 503 (fail-closed). Sem SV, a rota segue só com o JWT. |
| Reautenticação (RA) | `ExigirReautenticacaoRecente` | Exige senha/MFA recente (`reauth:{sid}`). Sem ela: 403 `REAUTH_REQUIRED`. Implica SV. |
| Guarda de onboarding | `Program.cs` + `RotasIsentasOnboarding` | Usuário autenticado com perfil incompleto recebe 403, exceto nas rotas isentas. |
| Rate limiting | `RateLimiterSetup` | Janela fixa de 1 min, **por IP**, só nas rotas com `RequireRateLimiting`. Contador no Redis, com fallback para memória se o Redis cair. 429 + `Retry-After: 60`. **Sem GlobalLimiter.** |
| IP do cliente | `PeerConfiavelMiddleware` + `ForwardedHeaders` | O gate só aceita `X-Forwarded-*` vindo de peers em `100.64.0.0/10`, `fc00::/7` e loopback. `ForwardLimit=2`, `KnownProxies` vazio. |
| Ordem do pipeline | `Program.cs` | Gate → ForwardedHeaders → headers de segurança → erros → static files → **Authentication → SessaoVerificada → Authorization → RateLimiter** → onboarding → endpoints. |

### Políticas de rate limit

| Política | Limite/min por IP | Rotas |
|---|---|---|
| `login` | 10 | `POST /usuarios/login`, `POST /usuarios/login/mfa` |
| `cadastro` | 5 | `POST /usuarios/cadastro`, `POST /usuarios/cadastro/reenviar-codigo` |
| `senha` | 3 | `POST /usuarios/senha/resetar/solicitar`, `POST /usuarios/senha/resetar` |
| `otp-assinatura` | 3 | `POST /api/contratos/{id}/assinar/solicitar-otp` |
| `dados-bancarios` | 5 | `POST …/dados-bancarios/solicitar-alteracao`, `POST …/confirmar`, `PUT /prestadores/me/dados-bancarios` |
| `refresh` | 30 | `POST /api/auth/refresh` |
| `reautenticar` | 5 | `POST /api/auth/reautenticar` |

São **12 de 114** rotas com rate limit.

## Legenda

- **Intenção:** `público` · `autenticado` (qualquer papel) · `contratante` · `prestador` · `admin`. Vem dos comentários e nomes no código e de inferência pela regra de negócio.
- **Auth:** `—` anônima · `Auth` = `RequireAuthorization()` sem papel · `P` / `C` / `A` = policy Prestador / Contratante / Admin · `Anon` = `AllowAnonymous()` explícito.
- **Extra:** `SV` sessão verificada · `RA` reautenticação recente.
- **Anon (prod):** status HTTP real sem token, com body `{}` e IDs fictícios.
- ⚠️ = divergência (ver seção final).

## Matriz

### Autenticação, sessão e conta

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| POST | `/usuarios/login` | público | — | | login | 400 | |
| POST | `/usuarios/login/mfa` | público | — | | login | 400 | MFA: 5 tentativas/código |
| POST | `/usuarios/logout` | público | Anon | | — | 200 | legado |
| POST | `/usuarios/cadastro` | público | — | | cadastro | **500** | ⚠️ D8 |
| POST | `/usuarios/cadastro/reenviar-codigo` | público | — | | cadastro | **500** | ⚠️ D8 |
| POST | `/usuarios/cadastro/confirmar` | público | — | | **—** | **500** | ⚠️ D5, D8 (mitigado: 5 tentativas/código) |
| POST | `/usuarios/senha/resetar/solicitar` | público | — | | senha | 400 | |
| POST | `/usuarios/senha/resetar` | público | — | | senha | 400 | |
| POST | `/usuarios/onboarding` | autenticado | Auth | | — | 401 | |
| DELETE | `/usuarios/conta` | autenticado | Auth | RA | — | 401 | |
| GET | `/api/me` | autenticado | Auth | | — | 401 | |
| POST | `/api/auth/refresh` | público | Anon | | refresh | 401 | 401 = sem cookie de refresh |
| POST | `/api/auth/logout` | público | Anon | | — | 200 | |
| POST | `/api/auth/logout-todas` | autenticado | Auth | SV | — | 401 | |
| GET | `/api/auth/sessoes` | autenticado | Auth | SV | — | 401 | |
| DELETE | `/api/auth/sessoes/{id}` | autenticado | Auth | SV | — | 401 | dono checado (404 se não for dele) |
| POST | `/api/auth/atividade` | autenticado | Auth | | — | 401 | |
| POST | `/api/auth/reautenticar` | autenticado | Auth | SV | reautenticar | 401 | |
| GET | `/api/conta/seguranca` | autenticado | Auth | | — | 401 | |
| POST | `/api/conta/senha` | autenticado | Auth | RA | — | 401 | |
| PUT | `/api/conta/mfa` | autenticado | Auth | RA | — | 401 | |

### Administração (foco)

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| GET | `/api/admin/disputas` | admin | A | SV | — | 401 | |
| GET | `/api/admin/disputas/{disputaId}` | admin | A | SV | — | 401 | |
| POST | `/api/admin/disputas/{disputaId}/resolver` | admin | A | RA | — | 401 | grava IP + adminId |
| POST | `/api/admin/pagamentos/{id}/reprocessar-transferencia` | admin | A | SV | — | 401 | ⚠️ D9: move dinheiro sem RA |
| GET | `/api/diagnostico/rede` | admin | A (só fora de Dev) | SV | — | 401 | **anônimo em Development** |
| GET | `/health/live` | **admin (requisito)** | Anon | | — | **200** | ⚠️ D1: sonda do Docker |
| GET | `/health/ready` | **admin (requisito)** | Anon | | — | **200** | ⚠️ D1: sonda da Railway; expõe hostname e estado das chaves |
| POST | `/api/dev/seed-admin` | dev-only | — | | — | 404 | ⚠️ D10: em Development, promove qualquer e-mail a Admin **sem auth** |
| POST | `/api/dev/seed-pagamentos` | dev-only | — | | — | 404 | só em Development, sem auth |

### Webhook e configuração pública

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| POST | `/api/webhooks/asaas` | público (token) | — | | — | 401 | header `asaas-access-token` obrigatório fora de Dev |
| GET | `/api/config/financeiro` | público | — | | — | 200 | |
| GET | `/api/cep/{cep}` | público | Anon | | **—** | 400 | ⚠️ D5: proxy anônimo para ViaCEP |

### Busca

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| GET | `/api/busca/prestadores` | público | — | | **—** | 200 | ⚠️ D4: chama OpenAI por requisição |
| GET | `/api/busca/prestadores/{id}/similares` | público | — | | **—** | 200 | |
| GET | `/api/busca/projetos` | público | — | | **—** | 200 | ⚠️ D4: chama OpenAI por requisição |
| GET | `/api/busca/projetos/{id}/prestadores-recomendados` | contratante | C | | — | 401 | dono checado no serviço |
| GET | `/api/busca/projetos/recomendados` | prestador | P | | — | 401 | |

### Projetos, chat e convites

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| GET | `/api/projects` | público | — | | — | 200 | filtra `Aberto` + `Publico` |
| GET | `/api/projects/{id}` | público | — | | — | 404 | ⚠️ D2: **não** filtra rascunho/privado |
| POST | `/api/projects` | contratante | C | | — | 401 | |
| POST | `/api/projects/{id}/publish` | contratante | C | | — | 401 | dono no serviço |
| DELETE | `/api/projects/{id}` | contratante | C | | — | 401 | dono no serviço |
| GET | `/api/me/projects` | contratante | C | | — | 401 | |
| GET | `/api/projects/{id}/proposals` | contratante | C | | — | 401 | dono no serviço |
| GET | `/api/projects/{id}/mensagens` | autenticado (partes) | Auth | | — | 401 | partes checadas no handler |
| POST | `/api/projects/{id}/mensagens` | autenticado (partes) | Auth | | **—** | 401 | ⚠️ D5: sem limite de envio |
| GET | `/api/me/chats` | autenticado | Auth | | — | 401 | papel lido do banco |
| POST | `/api/projects/{projetoId}/convites` | contratante | C | | — | 401 | |
| GET | `/api/projects/{projetoId}/convites` | contratante | C | | — | 401 | |
| PUT | `/api/convites/{id}/aceitar` | prestador | P | | — | 401 | |
| PUT | `/api/convites/{id}/recusar` | prestador | P | | — | 401 | |
| GET | `/api/me/convites` | prestador | P | | — | 401 | |
| POST | `/api/convites/{conviteId}/proposta` | contratante | C | | — | 401 | |

### Propostas

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| POST | `/api/propostas` | prestador | P | | — | 401 | |
| POST | `/api/propostas/{id}/enviar` | prestador | P | | — | 401 | |
| PUT | `/api/propostas/{id}/aceitar-negociacao` | prestador | P | | — | 401 | |
| POST | `/api/propostas/{id}/contraproposta` | autenticado (partes) | Auth | | — | 401 | partes no serviço |
| PUT | `/api/propostas/{id}/aceitar` | contratante | C | | — | 401 | |
| PUT | `/api/propostas/{id}/recusar` | contratante | C | | — | 401 | |
| DELETE | `/api/propostas/{id}` | prestador | P | | — | 401 | |
| GET | `/api/propostas/{id}` | autenticado (partes) | Auth | | — | 401 | partes no serviço |
| GET | `/api/me/propostas` | prestador | P | | — | 401 | |

### Contratos e entregas

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| GET | `/api/contratos/{id}` | autenticado (partes) | Auth | | — | 401 | partes no serviço |
| GET | `/api/me/contratos` | autenticado | Auth | | — | 401 | |
| POST | `/api/contratos/{id}/assinar/solicitar-otp` | autenticado (partes) | Auth | | otp-assinatura | 401 | |
| POST | `/api/contratos/{id}/assinar` | autenticado (partes) | Auth | | **—** | 401 | ⚠️ D5 (mitigado: 5 tentativas/OTP) |
| GET | `/api/contratos/{id}/pdf` | autenticado (partes) | Auth | | — | 401 | URL pré-assinada de 15 min |
| POST | `/api/contratos/{id}/entrega` | **prestador** | **Auth** | | **—** | 401 | ⚠️ D3: upload **antes** de checar o dono |
| GET | `/api/contratos/{id}/entrega` | autenticado (partes) | Auth | | — | 401 | |
| POST | `/api/contratos/{id}/entrega/aprovar` | **contratante** | **Auth** | RA | — | 401 | ⚠️ D6: papel só no serviço |
| POST | `/api/contratos/{id}/entrega/rejeitar` | **contratante** | **Auth** | | — | 401 | ⚠️ D6: papel só no serviço |
| DELETE | `/api/contratos/{id}` | autenticado (partes) | Auth | | — | 401 | |
| GET | `/api/contratos/{contratoId}/avaliacoes/pendente` | autenticado (partes) | Auth | | — | 401 | |

### Pagamentos

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| POST | `/api/pagamentos/iniciar` | contratante | C | | **—** | 401 | ⚠️ D5: chama o Asaas |
| GET | `/api/pagamentos/{id}/pix` | contratante | C | | — | 401 | |
| GET | `/api/pagamentos/{id}` | autenticado (partes) | Auth | | — | 401 | |
| POST | `/api/pagamentos/{id}/liberar` | contratante | C | RA | — | 401 | |
| POST | `/api/pagamentos/{id}/disputar` | autenticado (partes) | Auth | SV | — | 401 | |
| POST | `/api/pagamentos/{id}/estornar` | contratante | C | | — | 401 | |
| GET | `/api/pagamentos/{id}/sincronizar` | autenticado (partes) | Auth | | **—** | 401 | ⚠️ D5: chama o Asaas (e é GET com efeito colateral) |

### Avaliações

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| GET | `/api/avaliacoes/{id}` | autenticado | Auth | | — | 401 | filtra pelo avaliador |
| POST | `/api/avaliacoes/{id}/enviar` | autenticado | Auth | | — | 401 | |
| PUT | `/api/me/avaliacoes/privacidade` | autenticado | Auth | | — | 401 | |
| GET | `/api/usuarios/{id}/reputacao` | público | — | | — | 200 | |
| GET | `/api/usuarios/{id}/avaliacoes` | público | — | | — | 400 | 400 = sem `pagina`/`tamanho` |

### Perfil do contratante

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| GET | `/contratantes/{id}/perfil` | público | — | | — | 400 | |
| GET | `/contratantes/me/perfil` | contratante | C | | — | 401 | |
| PUT | `/contratantes/me/perfil` | contratante | C | | — | 401 | |
| POST | `/contratantes/me/foto` | contratante | C | | **—** | 401 | upload de 2 MB |
| DELETE | `/contratantes/me/foto` | contratante | C | | — | 401 | |

### Perfil do prestador

| Método | Rota | Intenção | Auth | Extra | Rate limit | Anon (prod) | Obs. |
|---|---|---|---|---|---|---|---|
| GET | `/prestadores/{id}/perfil` | público | — | | — | 404 | |
| GET | `/prestadores/me/perfil` | prestador | P | | — | 401 | |
| PUT | `/prestadores/me/perfil` | prestador | P | | — | 401 | |
| POST | `/prestadores/me/foto` | prestador | P | | **—** | 401 | upload de 5 MB |
| POST | `/prestadores/me/portfolio` | prestador | P | | **—** | 401 | upload |
| PUT | `/prestadores/me/portfolio/{id}` | prestador | P | | — | 401 | upload |
| DELETE | `/prestadores/me/portfolio/{id}` | prestador | P | | — | 401 | |
| POST | `/prestadores/me/experiencias` | prestador | P | | — | 401 | |
| PUT | `/prestadores/me/experiencias/{id}` | prestador | P | | — | 401 | |
| DELETE | `/prestadores/me/experiencias/{id}` | prestador | P | | — | 401 | |
| POST | `/prestadores/me/certificacoes` | prestador | P | | **—** | 401 | upload |
| PUT | `/prestadores/me/certificacoes/{id}` | prestador | P | | — | 401 | upload |
| DELETE | `/prestadores/me/certificacoes/{id}` | prestador | P | | — | 401 | |
| POST | `/prestadores/{prestadorId}/competencias` | **prestador** | **Auth** | | — | 401 | ⚠️ D6: `userId == prestadorId` no handler |
| DELETE | `/prestadores/{prestadorId}/competencias/{id}` | **prestador** | **Auth** | | — | 401 | ⚠️ D6 |
| POST | `/prestadores/me/experiencias/{expId}/competencias/{compId}` | **prestador** | **Auth** | | — | 401 | ⚠️ D6 |
| DELETE | `/prestadores/me/experiencias/{expId}/competencias/{compId}` | **prestador** | **Auth** | | — | 401 | ⚠️ D6 |
| POST | `/prestadores/me/certificacoes/{certId}/competencias/{compId}` | **prestador** | **Auth** | | — | 401 | ⚠️ D6 |
| DELETE | `/prestadores/me/certificacoes/{certId}/competencias/{compId}` | **prestador** | **Auth** | | — | 401 | ⚠️ D6 |
| POST | `/prestadores/me/portfolio/{portId}/competencias/{compId}` | **prestador** | **Auth** | | — | 401 | ⚠️ D6 |
| DELETE | `/prestadores/me/portfolio/{portId}/competencias/{compId}` | **prestador** | **Auth** | | — | 401 | ⚠️ D6 |
| GET | `/prestadores/me/dados-bancarios` | prestador | P | | — | 401 | |
| POST | `/prestadores/me/dados-bancarios/solicitar-alteracao` | prestador | P | | dados-bancarios | 401 | |
| POST | `/prestadores/me/dados-bancarios/confirmar` | prestador | P | | dados-bancarios | 401 | |
| PUT | `/prestadores/me/dados-bancarios` | prestador | P | | dados-bancarios | 401 | |

## Resumo

- **Rotas privadas:** as 90 que exigem autenticação devolvem 401 sem token em Production. Nenhuma rota privada ficou exposta por engano. (A varredura deu 92 respostas 401: as outras 2 vêm de `refresh` e do webhook, que são públicas, mas recusam por falta de cookie ou de token.)
- **Rotas públicas (24 em Production; 90 + 24 = 114):**
  - login, MFA, cadastro (3), reset de senha (2), refresh, logout (2), webhook (token próprio)
  - CEP, config financeira, busca (3), listagem e detalhe de projetos, reputação e avaliações públicas, perfis públicos (2)
  - health checks (2)

  Todas são públicas de propósito, exceto os **health checks** pelo seu requisito (D1) e o **detalhe de projeto**, que vaza mais do que deveria (D2).
- **Admin:** as 4 rotas `/api/admin/*` e `/api/diagnostico/rede` (em Production) exigem a policy `Admin`. O papel vem só da flag `IsAdmin` no banco, entra no JWT assinado e não pode ser afetado por header, query ou body. Admin tem MFA obrigatório no login e JWT de 10 min.

## Divergências encontradas (intenção × código)

Severidades **preliminares**. Serão confirmadas com teste nas Fases 2 a 4.

| # | Sev. | Rota(s) | Divergência | Evidência |
|---|---|---|---|---|
| D1 | Média | `/health/live`, `/health/ready` | O requisito diz "somente admin", mas as duas rotas são anônimas por desenho: a Railway (`healthcheckPath=/health/ready`) e o Docker HEALTHCHECK não enviam credencial. `/health/ready` expõe o hostname da instância, os nomes das dependências e o estado da rotação de chaves. | `HealthCheckSetup.cs:83-93`, `railway.toml:28`, resposta 200 sem token |
| D2 | **Alta** | `GET /api/projects/{id}` | Rota anônima que devolve **qualquer** projeto, inclusive `Rascunho` e `Privado`. A listagem filtra por status e visibilidade; o detalhe não. Os IDs são inteiros sequenciais, então dá para enumerar. | `ProjetoRepository.GetByIdAsync` sem filtro × `BuscarAsync:25-26` |
| D3 | **Alta** | `POST /api/contratos/{id}/entrega` | Não tem policy `Prestador`, e os arquivos (até 10 × 20 MB) vão para o **bucket privado do R2 antes** de o serviço checar se o usuário é o prestador do contrato. Qualquer autenticado consegue gravar arquivos órfãos em `entregas/{guid}/`, sem rate limit. | `ContratoExtensions.cs:172-189` × `EntregaService.cs:59-63` |
| D4 | **Alta** | `GET /api/busca/prestadores`, `GET /api/busca/projetos` | Rotas anônimas e sem rate limit. Cada `q` gera uma chamada de embeddings à OpenAI, sem cache e sem limite de tamanho, o que abre espaço para abuso de custo e de cota. | `BuscaSemanticaService.cs:59,353` |
| D5 | Média | ver ⚠️ D5 na matriz + todos os uploads | Não há `GlobalLimiter`: 102 rotas não têm limite. As mais relevantes: `cadastro/confirmar` e `assinar` (mitigadas por 5 tentativas por código), uploads, mensagens de chat, `pagamentos/iniciar` e `sincronizar` (chamam o Asaas), CEP e endpoints admin. | `RateLimiterSetup.cs` |
| D6 | Baixa | 8 rotas de competências, `entrega/aprovar`, `entrega/rejeitar` | O papel não é exigido na rota, só checado no serviço ou no handler. Funciona, mas sem defesa em profundidade, e a recusa vem como **400** (`NegocioException`) em vez de **403**. | `CompetenciaExtensions.cs`, `CompetenciaRelacionamentoExtensions.cs`, `ContratoExtensions.cs:213-243` |
| D7 | Média | `POST /usuarios/login` | O limite é só por IP (10/min) e não há bloqueio por conta após falhas. Um ataque distribuído contra uma conta não é contido. Admin é mitigado pelo MFA obrigatório. | `LoginService.cs:30-57` |
| D8 | Baixa | `POST /usuarios/cadastro*` | Body `{}` dá **500** (`NullReferenceException`) em vez de 400. | Varredura runtime + log da API |
| D9 | Média | `POST /api/admin/pagamentos/{id}/reprocessar-transferencia` | Ação financeira de admin sem `ExigirReautenticacaoRecente`, ao contrário de `resolver` disputa. | `PagamentoExtensions.cs:167` |
| D10 | Baixa | `/api/dev/seed-admin` | Em Development, promove qualquer e-mail a Admin **sem autenticação**. Hoje é seguro: Dockerfile e railway.toml não definem Development, e o padrão é Production. Mas basta um `ASPNETCORE_ENVIRONMENT=Development` exposto para virar escalada total. | `DevSeedExtensions.cs:21-41` |
| I1 | Info | rate limit | Se o Redis cair, o contador degrada para memória **por réplica** (o limite efetivo multiplica pelo número de réplicas). É o comportamento documentado. | `LimitadorJanelaFixaRedis` |
| I2 | Info | rate limit | Spoofing de `X-Forwarded-For`: o gate e o `ForwardLimit=2` estão corretos para a topologia documentada da Railway, mas **não dá para verificar daqui** se a borda sobrescreve ou acrescenta o XFF. Não verificado em produção. | `Docs/REVERSE-PROXY-IP-CLIENTE.md` |

### Sem divergência

- Nenhuma rota privada sem guard, nenhuma ordem errada de middleware e nenhum papel lido de input do cliente.
- O cadastro aceita só `prestador`/`contratante` (`UsuarioFactory`), e nenhum request DTO tem `role`/`isAdmin`.
