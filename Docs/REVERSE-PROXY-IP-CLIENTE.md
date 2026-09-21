# IP real do cliente atrás de reverse proxy

Correção do tratamento de `X-Forwarded-For` / `X-Forwarded-Proto` no Tratoo.API.
Data: 2026-09-20.

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

O `ForwardedHeadersMiddleware` (agora o primeiro do pipeline):

1. confere se o peer da conexão é um proxy confiável (`KnownProxies`/`KnownNetworks`);
2. consome a **última** entrada de `X-Forwarded-For` e a grava em `Connection.RemoteIpAddress`;
3. consome a última entrada de `X-Forwarded-Proto` e a grava em `Request.Scheme`
   (o que faz `Request.IsHttps` passar a responder corretamente);
4. move o que sobrou para `X-Original-For` / `X-Original-Proto`.

Repete o passo 1–3 no máximo `ForwardLimit` vezes (configurado como 1 = só o proxy de borda).

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
| `Tratoo.API/Infrastructure/ClientRequestInfo.cs` | Fonte única do IP: `ObterIp()`, `ObterIpOuNulo()`, `Normalizar()`. |
| `Tratoo.API/Infrastructure/RateLimiterSetup.cs` | As cinco políticas, agora particionadas por IP, com os limites originais preservados. |
| `Tratoo.API/EndPoints/DiagnosticoRedeExtensions.cs` | `GET /api/diagnostico/rede` — anônimo em Development, role `Admin` fora dela. |
| `Tratoo.Tests/` | Projeto de testes novo (xunit + `Microsoft.AspNetCore.TestHost`), adicionado à solution. |

### Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `Tratoo.API/Program.cs` | `AddTratooForwardedHeaders(...)` no builder; `app.UseForwardedHeaders()` como **primeiro** middleware (antes de `UseHsts`, security headers, static files, auth e rate limiter); bloco inline de 57 linhas do rate limiter trocado por `AddTratooRateLimiter()`; registro do endpoint de diagnóstico. |
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

Exclusivamente via `Connection.RemoteIpAddress` **depois** do `UseForwardedHeaders()`.
Nenhum ponto do código lê `X-Forwarded-For` diretamente — leitura manual ignoraria a
checagem de proxy confiável e aceitaria qualquer valor enviado pelo cliente.

### Quais proxies são confiáveis

Configurável pela seção `ForwardedHeaders`:

```jsonc
"ForwardedHeaders": {
  "Habilitado": true,
  "ForwardLimit": 1,
  "ConfiarNoProxyImediato": null,  // null => true fora de Development
  "KnownProxies": [],
  "KnownNetworks": []
}
```

Variáveis de ambiente planas no Railway:
`ForwardedHeaders__ConfiarNoProxyImediato`, `ForwardedHeaders__ForwardLimit`,
`ForwardedHeaders__KnownProxies__0`, `ForwardedHeaders__KnownNetworks__0`.

**Padrão adotado: `ConfiarNoProxyImediato = true` fora de Development.** O IP interno do
proxy da Railway é dinâmico e não documentado; travar em uma lista exigiria inventar
endereços, o que não foi feito. Com as listas vazias, o middleware aceita o peer imediato
como proxy — seguro **desde que o Kestrel não seja alcançável diretamente da internet**
(ver seção 6).

Cuidado registrado no código: se `ConfiarNoProxyImediato = false` for definido **sem**
preencher `KnownProxies`/`KnownNetworks`, listas vazias significariam "confiar em
qualquer peer" — exatamente o oposto do pedido. Nesse caso o setup restaura o default do
framework (apenas loopback). Coberto pelo teste
`SemProxyConfigurado_ENaoConfiandoNoPeer_IgnoraHeader`.

### Como o spoofing de `X-Forwarded-For` é evitado

Duas camadas:

1. **`ForwardLimit = 1`.** O middleware lê a entrada **mais à direita** de
   `X-Forwarded-For`. Um proxy que *anexa* o IP observado empurra qualquer valor forjado
   pelo cliente para a esquerda, onde ele é descartado:

   ```
   Cliente envia:  X-Forwarded-For: 8.8.8.8
   Proxy anexa:    X-Forwarded-For: 8.8.8.8, 203.0.113.45   ← IP real, é este que vale
   ```

   Coberto por `ComForwardLimit1_UsaUltimaEntradaDaCadeia`.

2. **Lista de proxies confiáveis**, quando informada. Um cliente que fale direto com o
   Kestrel tem seus headers `X-Forwarded-*` integralmente ignorados. Coberto por
   `ClienteNaoConfiavel_NaoConsegueForjarXForwardedFor`.

O limite da camada 1 é honesto: ela depende de o proxy **anexar** (ou substituir) o
header em vez de repassá-lo intacto. É o comportamento padrão de Nginx com
`proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for`, da Cloudflare e, pelo que
se espera, do proxy da Railway — mas isso **precisa ser confirmado em produção**
(seção 6).

### Comportamento com outros proxies no futuro

| Cenário | Configuração |
|---|---|
| **Railway hoje** | Nada a fazer — o padrão já vale. |
| **Nginx próprio, IP fixo** | `ConfiarNoProxyImediato: false` + `KnownProxies: ["<ip do nginx>"]` (ou `KnownNetworks` em CIDR). Nginx precisa de `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;` e `X-Forwarded-Proto $scheme;`. |
| **Cloudflare → Nginx → app** | `ForwardLimit: 2` e confiar no Nginx. Alternativa mais robusta: configurar o Nginx para usar `CF-Connecting-IP` e reescrever o `X-Forwarded-For`, mantendo `ForwardLimit: 1`. |
| **Sem proxy (rodando exposto)** | `Habilitado: false`. |

`X-Forwarded-Host` **não** é processado, de propósito: nada no projeto gera URL absoluta
a partir de `Request.Host` e `AllowedHosts` está em `"*"`, então confiar no host
encaminhado abriria host-header injection sem nenhum benefício. Se o backend passar a
montar links absolutos, adicione `XForwardedHost` **e** restrinja `AllowedHosts` ao
domínio real, na mesma alteração.

---

## 5. Validação

`dotnet test Tratoo.Tests` — **30 testes, 30 aprovados**, ~0,6 s.
Solution compila sem erros (mesmos 29 avisos `CS8618` pré-existentes).

| # | Cenário | Teste | Resultado |
|---|---|---|---|
| 1 | Requisição sem forwarded headers | `SemForwardedHeaders_UsaIpDaConexao` | IP = conexão; `http`; `IsHttps=false` |
| 2 | Com `X-Forwarded-For` | `ComXForwardedFor_AdotaIpDoClienteEmVezDoProxy` | IP do cliente, não do proxy |
| 3 | Com `X-Forwarded-Proto: https` | `ComXForwardedProto_CorrigeSchemeEIsHttps` | `Scheme=https` |
| 4 | `RemoteIpAddress` após o middleware | idem #2 (via `ClientRequestInfo`) | correto |
| 5 | `Request.IsHttps` | idem #3 | `true` |
| 6 | `Request.Scheme` | idem #3 | `https` |
| 7 | IP na captura da assinatura | `CapturaIpAuditoriaTests` (`/assinatura`) | IP do cliente |
| 8 | IP na captura dos logs de auditoria | `CapturaIpAuditoriaTests` (`/pagamento/liberar`, `/admin/disputa`) | IP do cliente; fallbacks `null` e `"admin"` preservados |
| 9 | Rate limiting por IP | `Login_ContaPorIpDoCliente_NaoPorProxy` | 10 ok, 11ª = 429, outro IP passa |
| 9b | Políticas não compartilham balde | `PoliticasDiferentes_NaoCompartilhamBalde` | `senha` esgotada não afeta `login` |
| 10 | Cliente não confiável não forja XFF | `ClienteNaoConfiavel_NaoConsegueForjarXForwardedFor` | header ignorado (IP e proto) |
| 10b | Proxy conhecido é aceito | `ProxyConhecido_TemSeuXForwardedForAceito` | IP do cliente |
| 10c | Rede confiável em CIDR | `RedeConhecidaEmCidr_ReconheceProxyDaFaixa` | IP do cliente |
| 10d | `false` sem proxy configurado não vira "confia em todos" | `SemProxyConfigurado_ENaoConfiandoNoPeer_IgnoraHeader` | header ignorado |
| 10e | `ForwardLimit=1` usa a última entrada | `ComForwardLimit1_UsaUltimaEntradaDaCadeia` | descarta o valor forjado |
| — | HSTS só com proto https | `Hsts_SoEEmitidoQuandoOProtoEncaminhadoEHttps` | header ausente sem proto, presente com |
| — | Normalização de IP | `ClientRequestInfoTests` (6 casos) | IPv4-mapped, scope id, tamanho, fallbacks |
| — | **Regressão do Defeito A** | `ComMiddlewareDesabilitado_VoltaAEnxergarOProxy` e `SemForwardedHeaders_TodosOsRegistrosRecebemOIpDoProxy` | reproduz o bug antigo |
| — | **Regressão do Defeito B** | `AddFixedWindowLimiter_DoFramework_UsaUmUnicoBaldeGlobal` e `SemForwardedHeaders_ClientesDistintosCompartilhamOLimite` | reproduz o bug antigo |

### O que os testes **não** cobrem

A persistência em si. `ContratoServicoService`, `AuditLogRepository` e `CadastroService`
recebem a string do IP e a atribuem diretamente a `HistoricoAssinatura.Ip`,
`AuditLog.Ip` e `ConsentLog.Ip`, sem transformação — isso foi verificado por leitura, não
por teste, porque exercitar esses caminhos exigiria banco, cache de OTP, serviço de
e-mail, geração de PDF e R2. Os testes cobrem a **captura**, que é exatamente onde o
defeito estava.

---

## 6. Pontos que dependem de infraestrutura

Nada abaixo pode ser determinado a partir do código. Não foram assumidos valores.

1. **O proxy da Railway anexa ou repassa `X-Forwarded-For`?**
   É a premissa da defesa anti-spoofing da camada 1. Se ele repassar intacto o header
   enviado pelo cliente, um atacante consegue forjar o IP registrado em auditoria.
   **Como verificar:** chamar `GET /api/diagnostico/rede` em produção (autenticado como
   `Admin`) **enviando** `X-Forwarded-For: 8.8.8.8`. Se `ipDoCliente` voltar `8.8.8.8`,
   o proxy repassa e a configuração precisa ser endurecida.

2. **O Kestrel é alcançável diretamente, sem passar pelo proxy?**
   `ConfiarNoProxyImediato = true` só é seguro se não for. Confirmar que a porta 8080 do
   container não tem rota pública fora do edge da Railway.

3. **Qual o IP/faixa interna do proxy da Railway?**
   Se for estável e obtível, trocar para `ConfiarNoProxyImediato: false` +
   `KnownProxies`/`KnownNetworks`. É mais restritivo que o padrão atual.

4. **Quantos saltos de proxy existem de fato?**
   `ForwardLimit` está em 1. Se houver CDN antes da Railway, ajustar — e conferir no
   diagnóstico se `xOriginalFor` mostra entradas sobrando.

5. **HSTS agora passa a ser emitido de verdade.**
   Antes não era (Defeito A). Confirmar que todo o tráfego do domínio é HTTPS antes de
   deixar rodando — `max-age` é sticky no navegador.

### Checklist de validação pós-deploy

```
GET /api/diagnostico/rede            (Admin)
  → ipDoCliente   deve ser o SEU IP público, não 10.x/172.x
  → scheme        deve ser "https"
  → isHttps       deve ser true
  → confiaEmQualquerPeer  true no padrão Railway

GET /api/diagnostico/rede  com header X-Forwarded-For: 8.8.8.8
  → ipDoCliente   deve continuar sendo o SEU IP.
                  Se voltar 8.8.8.8 → o proxy repassa o header → ver item 1.

Resposta de qualquer rota → deve conter Strict-Transport-Security

Auditoria: fazer um login e conferir a última linha de AuditLog.Ip
```

---

## 7. Observação lateral (fora do escopo, não alterada)

O projeto de testes emite `MSB3277`: `Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4` traz
`Microsoft.EntityFrameworkCore.Relational 9.0.1` enquanto `Tratoo.Domain` referencia
`Microsoft.EntityFrameworkCore 9.0.10`. É um conflito de versões transitivas
pré-existente, apenas revelado pelo novo projeto. Não foi mexido por não ter relação com
esta correção.
