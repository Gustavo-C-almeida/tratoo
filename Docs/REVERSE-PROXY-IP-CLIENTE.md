# IP real do cliente atrás de reverse proxy

Correção do tratamento de `X-Forwarded-For` / `X-Forwarded-Proto` no Tratoo.API.

**2026-09-20** — implementação inicial (`ForwardLimit=1`, hipótese não confirmada em produção).
**2026-09-22 (manhã)** — validado em produção via `railway logs`; topologia real do
Railway tem 2 saltos, não 1. `ForwardLimit` corrigido para `2`. Ver seção 6.1.
**2026-09-22 (tarde)** — trust boundary explícito: gate de peer confiável
(`PeerConfiavelMiddleware`) substitui o antigo `ConfiarNoProxyImediato`, que confiava em
qualquer peer. Ver seção 4.

---

## 1. Diagnóstico

### O problema existia?

**Sim, e era maior do que o relatado.** Foram confirmados **dois** defeitos independentes.

#### Defeito A — `RemoteIpAddress` é o IP do proxy

`Program.cs` **não tinha nenhuma chamada a `UseForwardedHeaders()`** nem configuração de
`ForwardedHeadersOptions`. A varredura no repositório inteiro não encontrou uma única
ocorrência de `ForwardedHeaders`, `X-Forwarded-For`, `X-Forwarded-Proto`, `KnownProxies`
ou `KnownNetworks` — nem em código, nem em `appsettings*.json`, nem no `Dockerfile`.
Também não há `ASPNETCORE_FORWARDEDHEADERS_ENABLED` em lugar nenhum.

Sem esse middleware, `HttpContext.Connection.RemoteIpAddress` é literalmente o endereço
da conexão TCP que chegou ao Kestrel. O `Dockerfile` publica
`ASPNETCORE_URLS=http://+:8080` — HTTP puro — e o TLS termina no proxy da Railway.
Logo, em produção:

```
Navegador ──HTTPS──► Proxy Railway ──HTTP──► Kestrel :8080
   189.x.x.x                                 RemoteIpAddress = IP interno do proxy
   (perdido, só sobrevive em X-Forwarded-For)
```

Os 15 pontos que liam o IP recebiam sempre o mesmo endereço interno.

#### Defeito B — o rate limiting nunca foi por IP

Este não estava no relato original e é mais grave em termos de disponibilidade.

As cinco políticas usavam `RateLimiterOptions.AddFixedWindowLimiter(nome, cfg)`. Apesar
dos comentários no código dizerem "por IP", **essa sobrecarga não particiona por nada**:
ela cria a partição com uma chave constante (`PolicyNameKey`), ou seja, **um único balde
global compartilhado por todos os clientes do planeta**.

Isso foi verificado empiricamente, não por leitura: o teste
`AddFixedWindowLimiter_DoFramework_UsaUmUnicoBaldeGlobal` roda contra a API do próprio
ASP.NET Core e mostra quatro IPs distintos consumindo o mesmo balde de 3.

Consequência prática antes da correção:

| Política | Limite | Efeito real |
|---|---:|---|
| `cadastro` | 5/min | 5 cadastros por minuto **na plataforma inteira** |
| `login` | 10/min | 10 logins/min no total; qualquer visitante derruba o login de todos |
| `senha` | 3/min | 3 resets/min no total |
| `dados-bancarios` | 5/min | 5 operações/min no total |
| `otp-assinatura` | 3/min | 3 OTPs/min no total |

Ou seja, o cenário do enunciado era ainda pior do que o desenhado:

```
Usuário A ─┐
Usuário B ─┼─► Proxy ─► Kestrel ─► um único balde, independente de qualquer IP
Usuário C ─┘
```

Corrigir só o `RemoteIpAddress` **não** teria consertado o rate limiting — o IP nem
entrava na conta.

### Como `X-Forwarded-For` e `X-Forwarded-Proto` entram no fluxo

O `ForwardedHeadersMiddleware` (logo após o gate de peer confiável, que é o primeiro do
pipeline — ver seção 4):

1. confere se o peer da conexão é um proxy confiável (`KnownProxies`/`KnownNetworks`);
2. consome a **última** entrada de `X-Forwarded-For` e a grava em `Connection.RemoteIpAddress`;
3. consome a última entrada de `X-Forwarded-Proto` e a grava em `Request.Scheme`
   (o que faz `Request.IsHttps` passar a responder corretamente);
4. move o que sobrou para `X-Original-For` / `X-Original-Proto`.

Repete o passo 1–3 no máximo `ForwardLimit` vezes (configurado como 2 — confirmado em
produção, ver seção 6: a borda do Railway encadeia dois saltos antes do Kestrel).

---

## 2. Impacto

### Confirmado como afetado

| Área | Onde | Efeito antes da correção |
|---|---|---|
| **Assinatura de contrato** | `ContratoExtensions.cs:59, :85` → `ContratoServicoService` | `HistoricoAssinatura.Ip` (ações `OtpSolicitado`, `OtpValidado`, `OtpFalhaValidacao`, `OtpBloqueadoBruteForce`, `Assinado`) e `ContratoServico.IpContratante`/`IpPrestador` gravavam o IP do proxy. A prova documental não identificava o signatário. |
| **`ConsentLog`** | `CadastroService.cs:110-111` | Aceite de Termos e Privacidade (LGPD Art. 7) registrado com IP do proxy. |
| **`IAuditLogRepository`** | `AuditLogRepository.cs:21` | Todas as ações do Marco Civil Art. 15 com o mesmo IP. |
| **Autenticação** | `LoginService.cs:67, :92` | `login`, `login_mfa`. |
| **Reset de senha** | `LoginService.cs:153` | `reset_senha_concluido`. |
| **Exclusão de conta** | `ExclusaoContaService.cs:47` | `conta_excluida`. |
| **Dados bancários** | `DadosBancariosService.cs:84, :111, :125, :176` | `dados_bancarios_alteracao_solicitada`, `_token_bloqueado`, `_confirmado`, `_criado`/`_atualizado`. Exatamente a trilha desenhada para detectar sequestro de conta — inútil sem o IP real. |
| **Pagamentos** | `PagamentoService.cs:772` (auditoria de liberação) e `:1129` (`disputa_resolvida`) | Idem, incluindo o ledger financeiro. |
| **Disputas (admin)** | `AdminDisputaExtensions.cs:67` | `disputa_resolvida`. |
| **Entrega** | `ContratoExtensions.cs:220` | Aprovação de entrega. |
| **Rate Limiting** | `Program.cs` (5 políticas) | Ver Defeito B — nem chegava a usar IP. |
| **HSTS** | `Program.cs` `app.UseHsts()` | `HstsMiddleware` só emite `Strict-Transport-Security` quando `Request.IsHttps` é `true`. Atrás do proxy o scheme era sempre `http`, então **o header nunca era emitido em produção**. Confirmado por teste. |

### Verificado e **não** afetado — correções à hipótese original

| Item da hipótese | Situação real |
|---|---|
| **Cookies `Secure`** | **Não era afetado.** `UserExtensions.cs:13-20` define `Secure = !isDev`, ou seja, deriva do ambiente e não de `Request.IsHttps`. O cookie `tratoo_auth` já ia com `Secure` em produção. |
| **URLs absolutas** | **Não era afetado hoje.** A busca por `LinkGenerator`, `Url.Action`, `Url.Link` e composição a partir de `Request.Host`/`Request.Scheme` não retornou nada — nenhum ponto do backend monta URL absoluta (os links de e-mail não usam o host da requisição). Passa a estar correto por construção caso isso mude. |
| **Redirecionamentos HTTPS** | Não há `UseHttpsRedirection()` no pipeline; o redirecionamento é responsabilidade do proxy. |
| **Coluna `Ip` no banco** | Não precisa de migration. `HistoricoAssinatura.Ip` já é `varchar(45)` (IPv6 completo) e `AuditLog.Ip`/`ConsentLog.Ip` são `text`. O middleware devolve sempre **um** endereço, não a lista. |

---

## 3. Alterações realizadas

### Arquivos novos

| Arquivo | Conteúdo |
|---|---|
| `Tratoo.API/Infrastructure/ForwardedHeadersSetup.cs` | `ForwardedHeadersSettings` + `AddTratooForwardedHeaders()` + `Aplicar()` (tradução settings → `ForwardedHeadersOptions`, exposta para teste). |
| `Tratoo.API/Infrastructure/PeerConfiavelMiddleware.cs` | Gate do trust boundary: descarta `X-Forwarded-*` de peers fora das faixas confiáveis, com log de aviso. |
| `Tratoo.API/Infrastructure/RedeConfiavel.cs` | Conjunto de faixas CIDR pré-parseado; casa IPv4 mapeado em IPv6 e aceita IP solto como /32 ou /128. |
| `Tratoo.API/Infrastructure/ClientRequestInfo.cs` | Fonte única do IP: `ObterIp()`, `ObterIpOuNulo()`, `Normalizar()`. |
| `Tratoo.API/Infrastructure/RateLimiterSetup.cs` | As cinco políticas, agora particionadas por IP, com os limites originais preservados. |
| `Tratoo.API/EndPoints/DiagnosticoRedeExtensions.cs` | `GET /api/diagnostico/rede` — anônimo em Development, role `Admin` fora dela. |
| `Tratoo.Tests/` | Projeto de testes novo (xunit + `Microsoft.AspNetCore.TestHost`), adicionado à solution. |

### Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `Tratoo.API/Program.cs` | `AddTratooForwardedHeaders(builder.Configuration)` no builder; `app.UseGateDePeerConfiavel()` + `app.UseForwardedHeaders()` como os **dois primeiros** middlewares (antes de `UseHsts`, security headers, static files, auth e rate limiter); bloco inline de 57 linhas do rate limiter trocado por `AddTratooRateLimiter()`; registro do endpoint de diagnóstico. |
| `Tratoo.API/EndPoints/UserExtensions.cs` | 7 chamadas migradas para `ClientRequestInfo`. |
| `Tratoo.API/EndPoints/ContratoExtensions.cs` | 3 chamadas. |
| `Tratoo.API/EndPoints/DadosBancariosExtensions.cs` | 3 chamadas. |
| `Tratoo.API/EndPoints/PagamentoExtensions.cs` | 1 chamada. |
| `Tratoo.API/EndPoints/AdminDisputaExtensions.cs` | 1 chamada (fallback `"admin"` preservado). |
| `Tratoo.API/appsettings.example.json` | Seção `ForwardedHeaders` documentada. |

Os 15 `http.Connection.RemoteIpAddress?.ToString()` foram substituídos preservando o
fallback de cada ponto: `?? "desconhecido"` → `ObterIp(http)`; `?? "admin"` →
`ObterIp(http, "admin")`; sem fallback → `ObterIpOuNulo(http)`. Nenhuma assinatura de
método de domínio mudou, nenhum DTO mudou, nenhuma regra de negócio mudou.

### Políticas de rate limiting

Limites **inalterados** (5/10/3/5/3 por minuto). Mudou só a chave de partição:

```csharp
RateLimitPartition.GetFixedWindowLimiter($"{nomePolitica}|{ip}", ...)
```

O nome da política entra na chave porque o `RateLimitingMiddleware` mantém **um único**
`PartitionedRateLimiter` compartilhado entre todas as políticas — chaves iguais em
políticas diferentes cairiam no mesmo balde.

### Sobre a centralização

Havia a opção de não criar helper nenhum, já que o `ForwardedHeaders` sozinho conserta
`RemoteIpAddress`. O `ClientRequestInfo` foi mantido por três motivos concretos, não por
estilo:

1. **Normalização.** `::ffff:203.0.113.45` e `203.0.113.45` são o mesmo cliente, mas
   strings diferentes — o que quebraria a chave de rate limiting e a comparação de
   registros de auditoria. Scope id de IPv6 link-local (`fe80::1%12`) idem.
2. **Truncamento** no limite de 45 caracteres da coluna.
3. **Ponto único de leitura**, que é o que impede alguém de voltar a ler
   `X-Forwarded-For` na mão em algum endpoint e furar a validação de proxy confiável.

---

## 4. Segurança

### Como o IP real é obtido

Exclusivamente via `Connection.RemoteIpAddress` **depois** do gate e do
`UseForwardedHeaders()`. Nenhum ponto do código lê `X-Forwarded-For` diretamente —
leitura manual puraria a validação de origem e aceitaria qualquer valor do cliente.

### O trust boundary

```
Cliente ──HTTPS──► borda Railway ──► roteamento interno ──► Kestrel :8080
                   escreve XFF[0]     acrescenta XFF[1]      peer = 100.64.0.x
                   = IP do cliente    = IP do pool de borda
                                                              │
                        ┌─────────────────────────────────────┘
                        ▼
   1. PeerConfiavelMiddleware  → o peer está numa faixa confiável?
        não ──► descarta X-Forwarded-*  (audita o IP real de quem conectou)
        sim ──► segue
   2. UseForwardedHeaders (ForwardLimit=2) → percorre 2 saltos → XFF[0] = cliente
```

**A decisão de confiança é tomada sobre o peer TCP, não sobre a cadeia.** Isso é
deliberado e a razão está na seção 6.1: as listas `KnownProxies`/`KnownNetworks` do
ASP.NET Core validam **cada salto**, e cobrir só a faixa do peer faz o middleware parar
no primeiro salto e gravar o IP do pool de borda como se fosse o cliente — o mesmo bug
que o `ForwardLimit=2` corrigiu. Cobrir os dois saltos exigiria a faixa do pool de borda
do Railway, que **não é documentada** (a lista de CIDRs deles responde 404). Separar as
responsabilidades resolve: o gate decide *quem pode apresentar* headers, o middleware
oficial decide *quantos saltos percorrer*.

Comprovado pelos testes `PinarSoAFaixaDoPeer_QuebraAIdentificacaoDoCliente` e
`PinarTodosOsSaltos_IdentificaOClienteCorretamente`.

### Faixas confiáveis padrão — e a evidência de cada uma

| Faixa | Por que está aqui |
|---|---|
| `100.64.0.0/10` | RFC 6598 (CGNAT). É o que o Kestrel enxerga como peer no Railway — `100.64.0.1/.2/.3/.12/.13/.14` observados entre o middleware de diagnóstico e os network flow logs. Não é roteável na internet pública. |
| `fc00::/7` | RFC 4193 (ULA). Os network flow logs do Railway mostram o ingresso na porta 8080 vindo de `fd12:0:8::/48` — 21 endereços distintos em 2 h. Faixa ampla porque o Railway não documenta o prefixo exato. |
| `127.0.0.0/8`, `::1/128` | Loopback: desenvolvimento local e health checks. |

Nenhuma faixa foi inventada: cada uma foi observada em produção ou é não-roteável por
definição de RFC. A faixa do **segundo** salto (o pool de borda) continua desconhecida e
por isso **não** é usada como critério de confiança.

Configuração (seção `ForwardedHeaders`; no Railway, variáveis planas
`ForwardedHeaders__ForwardLimit`, `ForwardedHeaders__PeersConfiaveis__0`, …):

```jsonc
"ForwardedHeaders": {
  "Habilitado": true,
  "ForwardLimit": 2,
  "PeersConfiaveis": [ "100.64.0.0/10", "fc00::/7", "127.0.0.0/8", "::1/128" ],
  "KnownProxies": [],   // ver armadilha acima
  "KnownNetworks": []
}
```

Definir `PeersConfiaveis` **substitui** a lista padrão inteira — a chave ausente é que
aciona o padrão. Isso exigiu deixar a propriedade como `null` no C#: o binder de
configuração do .NET *anexa* a arrays já inicializados em vez de substituí-los, o que
tornaria impossível **restringir** o trust boundary por configuração (só ampliar). O
defeito foi encontrado justamente pelos testes de binding e está coberto por
`PeersConfiaveisVindoDeConfiguracao_SubstituiOPadrao`.

### Como o spoofing de `X-Forwarded-For` é evitado

Três camadas, da mais forte para a mais fraca:

1. **Gate de peer confiável** (nosso). Quem fala direto com o Kestrel de fora das faixas
   tem `X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host`, `X-Forwarded-Port` e
   `X-Real-IP` removidos da requisição, e é auditado/limitado pelo IP real da conexão.
   Falha de forma segura e **visível**: emite `LogWarning`, então uma faixa mal
   configurada aparece no log em vez de corromper a auditoria em silêncio.
   Coberto por `PeerNaoConfiavel_TemHeadersForjadosDescartados`,
   `PeerNaoConfiavel_NaoEscapaComCadeiaLonga` e
   `PeerNaoConfiavel_NaoEscapaDoLimiteForjandoIp`.

2. **`ForwardLimit = 2`.** Mesmo vindo de um peer confiável, só os dois saltos mais à
   direita são percorridos. Se algum proxy um dia *anexar* em vez de regenerar, o que o
   cliente injetou fica à esquerda da janela e é descartado. Coberto por
   `ComMaisEntradasQueForwardLimit_DescartaOExcedenteDaEsquerda`.

3. **Comportamento da borda do Railway.** Confirmado por teste ao vivo (seção 6.1) e
   pela própria equipe deles em fórum: *"We do strip X-Forwarded-For at our edge and
   ensure clients cannot overwrite it."* É a camada sobre a qual temos menos controle —
   por isso ela é a última, não a primeira.

### Comportamento com outros proxies no futuro

| Cenário | Configuração |
|---|---|
| **Railway hoje** | Nada a fazer — os padrões já valem. |
| **Nginx próprio, IP fixo, à frente do Railway** | Soma +1 em `ForwardLimit` (fica 3). O peer continua sendo o Railway, então `PeersConfiaveis` não muda. Nginx precisa de `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;` e `X-Forwarded-Proto $scheme;`. |
| **Nginx próprio como único proxy (sem Railway)** | `ForwardLimit: 1` e `PeersConfiaveis: ["<ip ou faixa do nginx>"]`. |
| **Cloudflare → Nginx → Railway → app** | `ForwardLimit: 4`. Alternativa mais robusta: o Nginx reescrever `X-Forwarded-For` a partir de `CF-Connecting-IP`. |
| **Sem proxy (exposto direto)** | `Habilitado: false`. |

`X-Forwarded-Host` **não** é processado, de propósito: nada no projeto gera URL absoluta
a partir de `Request.Host` e `AllowedHosts` está em `"*"`, então confiar no host
encaminhado abriria host-header injection sem nenhum benefício. Se o backend passar a
montar links absolutos, adicione `XForwardedHost` **e** restrinja `AllowedHosts` ao
domínio real, na mesma alteração. (O gate já remove esse header de peers não confiáveis,
para que ele nunca chegue "sujo" caso passe a ser lido.)

---

## 5. Validação

`dotnet test Tratoo.Tests` — **57 testes, 57 aprovados**, ~0,7 s.
Solution compila sem erros.

Diferença importante em relação à versão anterior desta suíte: os testes de pipeline
agora sobem pelo **caminho real** — `AddTratooForwardedHeaders(IConfiguration)` com
binding de verdade — em vez de chamar `ForwardedHeadersSetup.Aplicar()` diretamente.
Foi essa mudança que expôs o defeito do binder descrito na seção 4 (arrays com valor
inicial são *anexados*, não substituídos).

### Trust boundary / anti-spoofing

| Cenário | Teste | Resultado |
|---|---|---|
| Peer não confiável forja `X-Forwarded-For` e `-Proto` | `PeerNaoConfiavel_TemHeadersForjadosDescartados` | IP = conexão real; `scheme=http` |
| Peer não confiável forja cadeia longa | `PeerNaoConfiavel_NaoEscapaComCadeiaLonga` | header descartado |
| Peer não confiável tenta escapar do rate limit | `PeerNaoConfiavel_NaoEscapaDoLimiteForjandoIp` | 4ª requisição = 429 pelo IP real |
| Peer não confiável tenta envenenar auditoria | `PeerNaoConfiavel_NaoEnvenenaOIpDeAuditoria` (3 rotas) | grava o IP real |
| Faixas padrão aceitam CGNAT, ULA, IPv4-mapped, loopback | `PeersDentroDasFaixasPadrao_SaoAceitos` (5 casos) | IP do cliente |
| Fronteiras da faixa rejeitam `100.63.255.255` / `100.128.0.0` | `PeersForaDasFaixasPadrao_SaoRejeitados` (4 casos) | IP da conexão |
| Config substitui (não soma) as faixas padrão | `PeersConfiaveisCustomizado_SubstituiOPadrao` | CGNAT deixa de ser confiável |
| Lista vazia = opt-out explícito | `PeersConfiaveisVazio_DesligaOGate` | aceita qualquer peer |

### ForwardLimit e topologia

| Cenário | Teste | Resultado |
|---|---|---|
| Cadeia de 2 saltos resolve o cliente (2 nós de borda) | `DoisSaltos_ResolveParaOClienteReal` | IP do cliente |
| Mais entradas que `ForwardLimit` | `ComMaisEntradasQueForwardLimit_DescartaOExcedenteDaEsquerda` | descarta o excedente da esquerda |
| `ForwardLimit=1` (valor antigo) | `ForwardLimit1_ResolveriaErroneamenteParaOPoolDeBorda` | reproduz o bug corrigido |
| Padrão do projeto sem config | `ConfiguracaoPadrao_ResolveATopologiaDeDoisSaltos` | IP do cliente |
| **Armadilha**: pinar só a faixa do peer em `KnownNetworks` | `PinarSoAFaixaDoPeer_QuebraAIdentificacaoDoCliente` | resolve o IP da borda — documenta por que o gate existe |
| Pinar todos os saltos funciona | `PinarTodosOsSaltos_IdentificaOClienteCorretamente` | IP do cliente |

### Binding real de configuração

| Cenário | Teste |
|---|---|
| Sem config → padrões (`ForwardLimit=2`, gate ativo, só For+Proto) | `SemConfiguracaoNenhuma_UsaOsPadroesDoProjeto` |
| `ForwardLimit` vindo de config | `ForwardLimitVindoDeConfiguracao_SobrescreveOPadrao` |
| `Habilitado=false` | `HabilitadoFalse_DesligaOProcessamento` |
| `PeersConfiaveis` substitui o padrão | `PeersConfiaveisVindoDeConfiguracao_SubstituiOPadrao` |
| `KnownProxies`/`KnownNetworks` chegam ao middleware | `KnownNetworksVindoDeConfiguracao_ChegaAoMiddleware` |
| CIDR inválido é ignorado e reportado, sem derrubar a app | `CidrInvalido_EIgnoradoEReportado` |
| Entrada em branco zera o gate | `PeersConfiaveisEmBranco_DeixaOGateVazio` |
| **Variável de ambiente `ForwardedHeaders__…`** (formato do Railway) | `VariavelDeAmbienteComDuploUnderscore_EhLidaCorretamente` |

### Captura de IP, rate limiting e HSTS

| Cenário | Teste |
|---|---|
| IP na captura da assinatura / auditoria / disputa | `CapturaIpAuditoriaTests` (fallbacks `desconhecido`, `null`, `admin` preservados) |
| Rate limiting conta por cliente, não por borda | `Login_ContaPorIpDoCliente_NaoPorBorda` |
| Políticas não compartilham balde | `PoliticasDiferentes_NaoCompartilhamBalde` |
| Dois clientes no mesmo nó de borda | `DoisClientesNoMesmoNoDeBorda_NaoCompartilhamCota` |
| HSTS só com proto https | `Hsts_SoEEmitidoQuandoOProtoEncaminhadoEHttps` |
| Normalização de IP | `ClientRequestInfoTests` (6 casos) |
| **Regressão do Defeito A** | `SemForwardedHeaders_TodosOsRegistrosRecebemOIpDoPeer` |
| **Regressão do Defeito B** | `AddFixedWindowLimiter_DoFramework_UsaUmUnicoBaldeGlobal`, `SemForwardedHeaders_ClientesDistintosCompartilhamOLimite` |

### O que os testes **não** cobrem

A persistência em si. `ContratoServicoService`, `AuditLogRepository` e `CadastroService`
recebem a string do IP e a atribuem diretamente a `HistoricoAssinatura.Ip`,
`AuditLog.Ip` e `ConsentLog.Ip`, sem transformação — isso foi verificado por leitura, não
por teste, porque exercitar esses caminhos exigiria banco, cache de OTP, serviço de
e-mail, geração de PDF e R2. Os testes cobrem a **captura**, que é exatamente onde o
defeito estava.

Todos os endereços usados nos testes são de faixas reservadas para documentação
(RFC 5737) ou não-roteáveis (RFC 6598/4193) — nenhum IP de tráfego real de produção
ficou versionado.

---

## 6. Pontos que dependem de infraestrutura

### 6.1 Confirmado em produção em 2026-09-22

Os itens 1 e 4 da versão anterior desta seção foram validados diretamente em produção,
via um middleware de diagnóstico temporário (`Console.WriteLine` logo antes de
`app.UseForwardedHeaders()`, para capturar os headers **crus**) cruzado com
`railway logs --http --json`, que expõe um campo `srcIp` autoritativo — calculado pela
própria borda do Railway, não algo que passa pelo `X-Forwarded-For`.

**Metodologia:** uma requisição de navegador comum foi capturada nos logs; em seguida,
uma segunda requisição com `X-Forwarded-For: 8.8.8.8, 9.9.9.9` forjado foi enviada de
propósito à mesma URL pública para testar resistência a spoofing.

**Achado 1 — a borda do Railway encadeia DOIS saltos antes do Kestrel, não um.**
Toda requisição chegou ao middleware com dois valores em `X-Forwarded-For`, por exemplo:

```
X-Forwarded-For: 201.81.0.58, 46.151.194.129
RemoteIp (peer cru, pré-middleware): ::ffff:100.64.0.1   ← CGNAT interno do Railway
```

O primeiro valor bate exatamente com o `srcIp` que o próprio `railway logs --http`
registra para a mesma requisição — ou seja, é o Railway quem diz que esse é o cliente
real, de forma independente do header. O segundo valor variou entre pelo menos dois
IPs (`46.151.194.129` e `46.151.194.130`) em requisições consecutivas — consistente com
um **pool** de nós de borda do Railway, não com um proxy externo de IP fixo.

**Consequência prática:** com `ForwardLimit=1` (o valor original desta correção), a
aplicação estava gravando o IP do *pool de edge do Railway* — não o do cliente — em toda
a auditoria, e usando esse mesmo IP como chave de rate limiting. Como o pool é
compartilhado, **dois usuários reais diferentes que caíssem no mesmo nó de borda
colidiam na mesma partição** — uma reencarnação mais sutil do exato bug (Defeito B) que
esta correção existia para resolver. **`ForwardLimit` foi corrigido para `2`** e o
comportamento agora bate com o `srcIp` autoritativo do Railway — coberto por
`TopologiaRealDoRailway_DoisSaltos_ResolveParaOClienteReal` e
`DoisClientesReaisNoMesmoNoDeEdge_NaoCompartilhamCota` em `Tratoo.Tests`.

**Achado 2 — o Railway REGENERA `X-Forwarded-For` do zero a cada requisição.**
O valor forjado (`8.8.8.8, 9.9.9.9`) não apareceu em nenhuma linha de log — em nenhuma
posição, nenhuma vez. Toda requisição, forjada ou não, chegou ao Kestrel com o par real
(`201.81.0.58, 46.151.194.1{29,30}`). Isso indica que a borda do Railway não repassa nem
anexa ao que o cliente envia — ela descarta e escreve o header do zero. Não há caminho
de spoofing por `X-Forwarded-For` contra esta aplicação, independente do `ForwardLimit`
configurado (contanto que ele não exceda o número real de saltos — ver 6.2).

Também foi confirmado no mesmo teste:
- `RemoteIp` (peer cru) sempre na faixa `100.64.0.0/10` (RFC 6598, CGNAT) → o Kestrel
  não é alcançável fora da rede interna do Railway. Item 2 da versão anterior: resolvido.
- `X-Forwarded-Proto: https` e `X-Forwarded-Host` corretos em toda requisição.

**Achado 3 — o container não é alcançável direto da internet (evidência de plataforma).**
`railway logs --network` (network flow logs da própria plataforma) sobre 2 h e 396 flows:

| O que foi medido | Resultado |
|---|---|
| Ingresso na porta 8080 com `peerKind=internet` | **zero ocorrências** |
| Ingresso na porta 8080 com `peerKind=service` | 100% dos flows, de 21 endereços `fd12:0:8::/48` distintos |
| Peer visto pelo Kestrel (middleware de diagnóstico) | `::ffff:100.64.0.x` — `.1 .2 .3 .12 .13 .14` |

É essa medição que sustenta o trust boundary da seção 4: a premissa "só a borda do
Railway alcança o Kestrel" deixou de ser suposição e passou a ter evidência de
plataforma. Também é o motivo de `fc00::/7` estar nas faixas padrão — dependendo da
camada, o peer aparece como CGNAT IPv4 ou como ULA IPv6.

**O que continua sem evidência: a faixa do segundo salto.** O valor que aparece em
`X-Forwarded-For[1]` (pool de borda) foi observado em apenas 2 endereços, o Railway não
publica CIDRs (a lista de utilidades deles responde 404) e a documentação oficial não
menciona faixas nem contagem de saltos. Por isso `KnownProxies`/`KnownNetworks`
permanecem **vazios** — preenchê-los só com a faixa do peer quebraria a identificação do
cliente (seção 4).

### 6.2 Ainda em aberto

1. **A faixa do pool de borda do Railway.** Sem ela, o trust boundary fica no peer TCP e
   não na cadeia inteira. Se o Railway publicar CIDRs algum dia, dá para migrar para
   `KnownNetworks` cobrindo **os dois saltos** e remover o gate próprio.

2. **O número de saltos (2) é constante para toda rota e tipo de conexão?**
   A amostra cobriu requisições HTTP/1.1 e HTTP/2 comuns. Não foi testado com WebSocket
   nem por um período longo o suficiente para pegar uma mudança de topologia sem aviso.
   Sintoma de desalinhamento: IPs de auditoria concentrados numa faixa pequena e
   repetida. O endpoint `/api/diagnostico/rede` mostra o estado atual a qualquer momento.

3. **`X-Real-IP`.** Um funcionário do Railway afirma em fórum que a plataforma envia
   esse header como "single source of truth". Se confirmado em produção, seria mais
   robusto que percorrer a cadeia — mas hoje é declaração de fórum sem documentação, e
   este projeto processa apenas `XForwardedFor`/`XForwardedProto`. O gate já remove
   `X-Real-IP` de peers não confiáveis, então adotá-lo no futuro não exige repensar o
   trust boundary.

4. **HSTS agora é emitido de verdade.** Antes não era (Defeito A). Confirmar que todo o
   tráfego do domínio é HTTPS antes de deixar rodando por muito tempo — `max-age` é
   sticky no navegador.

### Checklist de validação pós-deploy

```
GET /api/diagnostico/rede            (Admin)
  → ipDoCliente              deve ser o SEU IP público, não 100.64.x / 198.51.x
  → scheme / isHttps         "https" / true
  → trustBoundary.gateAtivo  true
  → trustBoundary.faixasInvalidas   deve estar vazio
  → configuracaoAtiva.forwardLimit  2

Comparar com railway logs --http --json (campo srcIp) na mesma requisição:
  → ipDoCliente do endpoint deve ser IGUAL ao srcIp do Railway.
    Diferente = ForwardLimit desalinhado com o número real de saltos (6.2, item 2).

railway logs --filter "X-Forwarded-* descartados"
  → deve estar VAZIO. Qualquer ocorrência para tráfego legítimo significa que
    PeersConfiaveis não cobre a faixa real do peer — o gate está derrubando
    headers válidos e a auditoria está gravando o IP da borda.

Resposta de qualquer rota → deve conter Strict-Transport-Security

Auditoria: fazer um login e conferir a última linha de AuditLog.Ip — deve bater com
o IP público real de quem logou, não com um IP repetido entre usuários diferentes.
```

## 7. Observação lateral (fora do escopo, não alterada)

O projeto de testes emite `MSB3277`: `Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4` traz
`Microsoft.EntityFrameworkCore.Relational 9.0.1` enquanto `Tratoo.Domain` referencia
`Microsoft.EntityFrameworkCore 9.0.10`. É um conflito de versões transitivas
pré-existente, apenas revelado pelo novo projeto. Não foi mexido por não ter relação com
esta correção.
