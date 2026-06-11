# Tratoo — Marketplace de Serviços com Escrow e Garantia

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF%20Core-9.0-512BD4)](https://learn.microsoft.com/ef/core/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-transacional-CC2927)](https://www.microsoft.com/sql-server)
[![pgvector](https://img.shields.io/badge/PostgreSQL-pgvector%20(HNSW)-336791)](https://github.com/pgvector/pgvector)
[![Storage](https://img.shields.io/badge/Storage-Cloudflare%20R2-F38020)](https://developers.cloudflare.com/r2/)

O Tratoo conecta quem precisa contratar um serviço a profissionais freelancers, com contrato digital assinado, pagamento retido em garantia (escrow) e mediação de disputas — reduzindo o risco para os dois lados de uma negociação entre desconhecidos.

> Projeto de estudo/portfólio. Estudo de arquitetura .NET com escrow, contratos digitais, busca semântica e fluxo administrativo. Veja [Configuração e Segredos](#configuração-e-segredos).

---

## O Problema que o Tratoo Resolve

| Problema | Solução Tratoo |
|----------|----------------|
| Prestador teme entregar e não receber | Valor retido em escrow antes de o trabalho começar |
| Contratante teme pagar e não receber | Pagamento só é liberado após aprovação da entrega |
| Falta de confiança entre desconhecidos | Identidade verificada (CPF/CNPJ), reputação e avaliações blind review |
| Disputas sem mediação | Fluxo formal de disputa com resolução por administrador |
| Sem prova documental do acordo | Contrato digital assinado com OTP + hash SHA-256 + IP, arquivado em PDF |

Sobre valores: no MVP a plataforma não cobra taxa própria — o valor é repassado integralmente ao prestador. Existe apenas a taxa operacional do gateway (Asaas), exibida de forma meramente informativa (campo `Pagamento.TaxaGateway`, preenchido a partir do gateway; não entra em nenhum cálculo nem é deduzido do repasse).

---

## Índice

- [Como Funciona (Fluxo End-to-End)](#como-funciona-fluxo-end-to-end)
- [Arquitetura](#arquitetura)
- [Stack Tecnológica](#stack-tecnológica)
- [Estrutura do Projeto](#estrutura-do-projeto)
- [Modelagem de Domínio](#modelagem-de-domínio)
- [Integrações Externas](#integrações-externas)
- [Segurança e Compliance](#segurança-e-compliance)
- [Background Services](#background-services)
- [Área Administrativa](#área-administrativa)
- [Configuração e Segredos](#configuração-e-segredos)
- [Rodando o Projeto](#rodando-o-projeto)
- [Roadmap Técnico](#roadmap-técnico)
- [Compliance Legal](#compliance-legal)

---

## Como Funciona (Fluxo End-to-End)

Cada etapa descreve o quê acontece, como a tecnologia viabiliza e por quê a regra existe.

### 1. Cadastro e Onboarding

- O usuário cria a conta, confirma o e-mail e escolhe seu papel — Contratante (contrata serviços) ou Prestador (oferece serviços). Depois completa o perfil mínimo.
- `POST /usuarios/cadastro` envia um OTP por e-mail (`EmailService`) e guarda dados em cache; `POST /usuarios/cadastro/confirmar` valida o código. Em `POST /usuarios/onboarding`, CPF/CNPJ é validado e criptografado em repouso (AES, `DataProtector`) em `UserIdentity`.
- Confirmar e-mail evita contas falsas; criptografia atende LGPD; o onboarding guard impede uso com perfil incompleto.

### 2. Publicação do Projeto

- O contratante descreve a necessidade — escopo, prazo, orçamento e competências.
- `POST /projetos` cria o `Projeto`; é transformado em embedding (`ProjetoIndexadorService` → OpenAI) e indexado no pgvector.
- Representar como vetor permite encontrar profissionais por significado, não apenas palavras-chave.

### 3. Descoberta de Profissionais

- O contratante recebe sugestões de prestadores compatíveis, ou convida alguém diretamente.
- `BuscaSemanticaService` busca em duas camadas: pgvector retorna top-100 por distância de cosseno (HNSW), C# aplica filtros + score composto (ver [Busca Semântica](#busca-semântica-openai--pgvector)).
- Competências, experiências, certificações e portfólio entram no ranqueamento — aproximando à intenção real.

### 4. Negociação Versionada

- O prestador envia uma proposta (valor, prazo, escopo). O contratante pode aceitar, recusar ou fazer contraproposta.
- Cada rodada vira uma `PropostaVersao` (até 10 versões). Regra de turno obrigatório: quem enviou a última versão não pode aceitá-la. Partes trocam mensagens por projeto (`MensagemProjeto`, REST com polling), liberadas por convite.
- Histórico versionado preserva a evolução; turno obrigatório impede que um lado "aceite a si mesmo".

### 5. Geração e Assinatura do Contrato

- Ao aceitar, o sistema gera o contrato automaticamente; ambas as partes assinam.
- `ContratoServico` nasce com status `Gerado`. Assinatura exige OTP por e-mail (6 dígitos, 10 min, máx. 5 tentativas). Na 1ª assinatura: calcula `ConteudoHash` (SHA-256) e registra IP → `AguardandoAssinatura`. Na 2ª: valida hash, grava `ContratoSnapshot` imutável e gera PDF (QuestPDF) no bucket privado R2.
- OTP vincula signatário, IP rastreia, hash prova integridade, snapshot/PDF preservam conteúdo — base legal MP 2.200-2/2001 (sem certificado ICP-Brasil).

### 6. Pagamento em Garantia (Escrow)

- O contratante paga via PIX, mas o valor não vai imediatamente ao prestador — fica retido.
- `POST /api/pagamentos/iniciar` cria cobrança PIX no Asaas (QR Code). Ao confirmar, Asaas chama webhook `PAYMENT_RECEIVED` e `Pagamento` passa a `Retido`. Cada movimento registrado em `LedgerFinanceiro` (imutável).
- Contratante só libera ao receber; prestador tem certeza que o dinheiro está reservado.

### 7. Entrega Formal

- O prestador executa e registra oficialmente a entrega, com descrição, anexos e links.
- `EntregaService` cria `Entrega` (com `EntregaAnexo` no R2 privado e `EntregaLink`), move contrato para `AguardandoAprovacaoEntrega` e registra em `HistoricoContrato`.
- Entrega vira parte do histórico auditável do contrato e dispara a etapa de aprovação.

### 8. Aprovação e Liberação do Pagamento

- O contratante aprova a entrega ou solicita ajustes.
- Ao aprovar: `EntregaService.AprovarEntregaAsync` encerra contrato, cria slots de avaliação (blind review) e libera pagamento via `IPagamentoService` — dispara transferência PIX ao prestador no Asaas. Liberação é definitiva com webhook `TRANSFER_DONE`. Ao solicitar ajustes: contrato volta a `Ativo` e prestador reenvia.
- Repasse integral condicionado à aprovação. Se contratante não agir no prazo, background service libera automaticamente — protegendo prestador.

### 9. Avaliações (*Blind Review*)

- As duas partes se avaliam após encerramento.
- Ao liberar pagamento, `AvaliacaoService` cria 2 slots. Notas só ficam públicas quando ambos avaliam; após 7 dias, publica-se a preenchida e oculta-se vazia (`AvaliacaoExpiracaoService`). Reputação e embeddings recalculados.
- Blind review impede avaliação retaliatória — ninguém vê a nota do outro antes de enviar a sua.

### 10. Disputa e Resolução Administrativa

- Havendo desacordo, contratante abre disputa e valor permanece retido.
- `POST /api/pagamentos/{id}/disputar` cria `DisputaPagamento` e pagamento vai para `EmDisputa`. Administrador resolve pela área restrita (ver [Área Administrativa](#área-administrativa)): favor contratante (estorno + `Cancelado`) ou prestador (liberação + `Encerrado`). Tudo registrado em `HistoricoContrato`, `AuditLog` e `LedgerFinanceiro`.
- Mediação imparcial com trilha completa — decisão é definitiva e não reabre.

### Gestão da Conta

A qualquer momento o usuário pode atualizar perfil, experiências, certificações e portfólio, configurar chave PIX, ativar MFA, alterar senha ou solicitar exclusão. Na exclusão, dados pessoais são anonimizados (nome → "Usuário indisponível", e-mail removido, login bloqueado, perfil retirado das buscas), mas contratos, pagamentos e avaliações são preservados para fins legais.

---

## Arquitetura

Solução em camadas (Clean Architecture + Feature Folders) com persistência poliglota.

```
┌──────────────────────────────────────────────────────────────────────┐
│                  CLIENTE — Web (HTML/CSS/JS vanilla)                  │
│                  servido como arquivos estáticos pela API            │
└───────────────────────────────┬──────────────────────────────────────┘
                                │ cookie httpOnly "tratoo_auth" (JWT)
                                ▼
┌──────────────────────────────────────────────────────────────────────┐
│                   APRESENTAÇÃO — Tratoo.API (.NET 8)                  │
│  Minimal APIs · Security Headers · Exception Handler · Auth (JWT)     │
│  Authorization · Rate Limiting · Onboarding Guard · Background Svcs   │
└───────────────────────────────┬──────────────────────────────────────┘
                                ▼
┌──────────────────────────────────────────────────────────────────────┐
│                      DOMÍNIO — Tratoo.Domain                          │
│  Services (regra de negócio) · Repositories · Domain Models · DTOs    │
│  Interfaces de Gateway/Storage/Email · Enums · Migrations             │
└───────────────┬──────────────────────────────────┬───────────────────┘
                ▼                                   ▼
┌───────────────────────────┐      ┌───────────────────────────────────┐
│       PERSISTÊNCIA        │      │           INFRAESTRUTURA          │
│ ┌───────────────────────┐ │      │ ┌──────────┐  ┌────────────────┐  │
│ │ SQL Server            │ │      │ │ Cloudflare│  │ Asaas (PIX/    │  │
│ │ TratooContext         │ │      │ │ R2 (blob) │  │ escrow)        │  │
│ └───────────────────────┘ │      │ └──────────┘  └────────────────┘  │
│ ┌───────────────────────┐ │      │ ┌──────────┐  ┌────────────────┐  │
│ │ PostgreSQL + pgvector │ │      │ │ OpenAI    │  │ SMTP (e-mail)  │  │
│ │ VectorContext (HNSW)  │ │      │ │ Embeddings│  │                │  │
│ └───────────────────────┘ │      │ └──────────┘  └────────────────┘  │
└───────────────────────────┘      └───────────────────────────────────┘
```

### Persistência Poliglota

| Banco | Uso | Por quê este banco |
|-------|-----|--------------------|
| SQL Server (`TratooContext`) | Usuários, projetos, propostas, contratos, pagamentos, disputas, ledger | Transações ACID e consistência forte para núcleo financeiro/contratual |
| PostgreSQL + pgvector (`VectorContext`) | Embeddings de prestadores e projetos (busca semântica) | Índice HNSW para similaridade de cosseno eficiente em vetores 1536-dim |

### Fluxo de uma Requisição (ex.: aprovar entrega)

A ordem reflete o pipeline real em `Program.cs`:

```
Browser (cookie httpOnly "tratoo_auth")
  │
  ▼ 1. Security Headers        → CSP, X-Frame-Options, X-Content-Type-Options, Referrer/Permissions-Policy, HSTS
  ▼ 2. UseExceptionHandler     → NegocioException = 400 amigável | demais = 500 + log (Serilog)
  ▼ 3. Static Files            → wwwroot short-circuitado
  ▼ 4. UseAuthentication       → lê/valida JWT do cookie (issuer, audience, lifetime, ClockSkew = 0)
  ▼ 5. UseAuthorization        → roles/policies (Prestador / Contratante / Admin)
  ▼ 6. UseRateLimiter          → janelas fixas por IP
  ▼ 7. Onboarding Guard        → bloqueia perfis incompletos (403 ONBOARDING_PENDENTE)
  ▼ 8. Endpoint                → extrai userId das claims e chama Service
  ▼ 9. EntregaService.AprovarEntregaAsync
        - valida ownership e status
        - aprova entrega → encerra contrato → cria avaliações (blind review)
        - libera pagamento via IPagamentoService → logado e auditado
  ▼ 10. SaveChanges            → transação no SQL Server
  ▼ 11. Resposta JSON          → DTO (entidades nunca serializadas diretamente)
```

---

## Stack Tecnológica

### Backend

| Componente | Tecnologia | Para que serve |
|------------|------------|-----------------|
| Runtime / API | .NET 8 · ASP.NET Core Minimal APIs | Endpoints organizados por recurso (Extension Methods) |
| ORM transacional | EF Core 9 + SQL Server | Migrations, transações e consistência do núcleo |
| Busca vetorial | Npgsql + Pgvector.EntityFrameworkCore | Embeddings no PostgreSQL com índice HNSW |
| Autenticação | JWT (HMAC-SHA256) em cookie httpOnly `tratoo_auth` | Sessão sem estado, protegida contra JS |
| Hash de senha | BCrypt.Net-Next | Hash + salt das senhas |
| Criptografia de PII | AES (`DataProtector`) | CPF/CNPJ criptografado em repouso (LGPD) |
| Geração de PDF | QuestPDF | PDF imutável do contrato assinado (R2 privado) |
| Storage de blobs | AWS SDK S3 (compatível R2) | Fotos/portfólio (público) e contratos/anexos (privado, pré-assinado) |
| Logging | Serilog | Logs estruturados em console + arquivo rotativo |
| Validação | Manual no domínio via `NegocioException` | Regra inválida vira 400 amigável |
| DI / Mapeamento | `Microsoft.Extensions.DependencyInjection` | Composição explícita; DTOs na fronteira HTTP |

### Frontend

HTML/CSS/JavaScript vanilla (sem framework), servido como arquivos estáticos pela API (`UseStaticFiles`). Mensagens por projeto usam polling (sem WebSocket).

### Infraestrutura

| Serviço | Finalidade |
|---------|-----------|
| Cloudflare R2 (bucket público) | Fotos de perfil e portfólio (`R2StorageService`) |
| Cloudflare R2 (bucket privado) | PDFs e anexos — URL pré-assinada (`R2PrivateStorageService`) |
| Asaas | Cobrança PIX, escrow, transferências, estornos e webhooks |
| OpenAI Embeddings (`text-embedding-3-small`, 1536 dims) | Vetores para busca semântica |
| SMTP | OTP, assinatura, MFA + notificações |

---

## Estrutura do Projeto

```
Tratoo/
├── Tratoo.API/                     # Apresentação
│   ├── EndPoints/                  # Minimal APIs por recurso (Extension Methods)
│   │   ├── ProjetoExtensions.cs        PropostaExtensions.cs
│   │   ├── ContratoExtensions.cs       PagamentoExtensions.cs
│   │   ├── AvaliacaoExtensions.cs      BuscaExtensions.cs
│   │   ├── AdminDisputaExtensions.cs   ChatConviteExtensions.cs   ...
│   ├── BackgroundServices/         # Serviços em segundo plano (IHostedService)
│   ├── Requests/                   # DTOs de entrada HTTP
│   └── Program.cs                  # Composição, DI, pipeline, segurança
│
├── Tratoo.Domain/                  # Núcleo
│   ├── Domain/
│   │   ├── Models/                 # Entidades EF Core (Usuario, Projeto, Pagamento, ...)
│   │   │   ├── Financeiro/         # Pagamento, DisputaPagamento, LedgerFinanceiro, WebhookLog, ContaBancaria
│   │   │   └── Prestador/          # Competencia, Experiencia, Certificacao, Portfolio, ...
│   │   └── Enums/                  # Enumerações de domínio
│   ├── Features/                   # Feature Folders (coesão por funcionalidade)
│   │   ├── Auth/  Projetos/  Propostas/  Contratos/  Pagamentos/
│   │   ├── Avaliacoes/  Perfis/  Mensagens/  IA/  Storage/  Infrastructure/
│   │   │     └── cada uma com DTOs/ · Repositories/ · Services/
│   ├── Config/                     # EmailSettings (bind de configuração, sem segredos)
│   ├── Data/                       # TratooContext (SQL Server) e VectorContext (pgvector)
│   └── Migrations/                 # Migrations EF Core
│
└── Tratoo.Web/wwwroot/             # Frontend estático
    ├── pages/                      # Páginas por área (contratante, prestador, admin, ...)
    ├── assets/css · assets/js      # Estilos e scripts (vanilla)
    └── components/                 # Header/footer reutilizáveis
```

---

## Modelagem de Domínio

### Usuário — herança TPT (Table Per Type)

`Usuario` é a base abstrata; `Prestador` e `Contratante` são mapeados em **tabelas próprias** (`ToTable("Prestadores")` / `ToTable("Contratantes")`).

```
Usuario (abstrata)
  - Id (int) · Nome · Email · SenhaHash
  - TipoUsuario (Prestador | Contratante)
  - Status (Pending | Active | Blocked) · IsAdmin (definido só via seed/banco)
  - MFA · IdentidadeVerificada · TipoPessoa · Endereco · Telefone
  - ExcluidoEm (soft delete / LGPD) · DataCadastro
        ├── Prestador     (TituloProfissional, AreaEspecializacao, FotoUrl,
        │                  competências, experiências, certificações, portfólio,
        │                  conta bancária / chave PIX, PorcentagemCompleto)
        └── Contratante   (Segmento, NomeEmpresa, LogoUrl, SiteUrl, Disponibilidade...)

  Identidade / consentimento:
   - UserIdentity (CPF/CNPJ criptografado em AES, NivelVerificacao)
   - ConsentLog   (termos/privacidade, IP, versão) · AuditLog (ações críticas)
```

### Projeto → Proposta → Contrato → Pagamento

```
Projeto (1) ───< PropostaProjeto (1) ───< PropostaVersao (negociação versionada, até 10)
   │                                            │  turno obrigatório:
   │  Status: Rascunho/Aberto/                  │  autor da última versão NÃO pode aceitá-la
   │          EmAndamento/Cancelado             ▼  (no aceite)
   │                                     ContratoServico (1) ───── Pagamento (1)
   │   Status do contrato:                          │  ValorBruto + TaxaGateway (informativa)
   │   Gerado → AguardandoAssinatura → Ativo →       │
   │   AguardandoAprovacaoEntrega → Encerrado|Cancelado
   │   ConteudoJson + ConteudoHash (SHA-256)         ├──< Entrega (1) ──< EntregaAnexo / EntregaLink
   │   + ContratoSnapshot (imutável) + PdfKey        │
   │   + HistoricoAssinatura (IP por parte)          ├──< DisputaPagamento (0..N)
   │                                                 └──< LedgerFinanceiro (imutável)
   │
   └──< Avaliacao (2 slots por contrato, blind review) · HistoricoContrato (trilha de eventos)
```

**`LedgerFinanceiro`** é um livro-razão **imutável** (nunca alterado/excluído) — base de rastreabilidade financeira. **`ContratoSnapshot`** congela os dados das partes no instante da assinatura.

### Entidades de auditoria / compliance

| Entidade | Finalidade |
|----------|-----------|
| **AuditLog** | Ações críticas (login, assinatura, exclusão de conta, resolução de disputa) |
| **ConsentLog** | Consentimento LGPD (termos/privacidade) com IP e versão |
| **HistoricoContrato** | Trilha por contrato (entrega, aprovação, liberação, disputa resolvida) |
| **HistoricoAssinatura** | Registro de cada assinatura com IP |
| **WebhookLog** | Idempotência dos webhooks do gateway |

---

## Integrações Externas

### Asaas (pagamentos) — `AsaasGatewayService`

Operações reais expostas pelo serviço: criar/reutilizar cliente, criar cobrança PIX, obter QR Code (com *retry*), criar transferência PIX ao prestador, estornar cobrança, consultar status e simular pagamento (sandbox).

Ciclo de vida:

```
1. POST /api/pagamentos/iniciar
   → cria cliente (cus_...) e cobrança PIX (pay_...) + QR Code
2. Contratante paga PIX
   → webhook PAYMENT_RECEIVED → Pagamento = Retido (escrow)
3. Entrega aprovada
   → cria transferência PIX ao prestador (tra_...)
   → webhook TRANSFER_DONE → Pagamento = Liberado
```

Webhooks tratados (idempotentes via `WebhookLog`):

| Evento | Ação |
|--------|------|
| `PAYMENT_RECEIVED` | Pagamento → `Retido` (escrow) |
| `PAYMENT_REFUNDED` | Pagamento → `Estornado` |
| `TRANSFER_DONE` | Transferência ao prestador confirmada → `Liberado` |
| `TRANSFER_FAILED` | Marca falha para reprocessamento |

O endpoint `POST /api/webhooks/asaas` é público, protegido por token compartilhado (`Asaas:WebhookToken`) em vez de JWT. Em ambientes sem webhook (localhost), há fallback (`ConfirmarTransferenciaImediatamente`) e endpoints de sincronizar/simular.

### Cloudflare R2 (storage)

| Bucket | Conteúdo | Acesso | Classe |
|--------|----------|--------|--------|
| Público | Fotos de perfil, portfólio | URL direta | `R2StorageService` |
| Privado | PDFs de contrato, anexos de entrega | **URL pré-assinada** temporária (ex.: 15 min) | `R2PrivateStorageService` |

Usa o **AWS SDK S3** apontando para o endpoint S3-compatível do R2.

### Busca Semântica (OpenAI + pgvector)

Prestadores e projetos são convertidos em embeddings (`text-embedding-3-small`, 1536 dims) e armazenados no PostgreSQL/pgvector (índice HNSW). A busca (`BuscaSemanticaService`) tem duas camadas:

1. pgvector — top-100 por distância de cosseno (HNSW)
2. C# — aplica filtros de negócio e score composto (soma = 1.00)

| Fator | Peso |
|-------|------|
| Similaridade semântica | 35% |
| Habilidades (match exato de stack) | 15% |
| Reputação | 15% |
| Completude do perfil | 10% |
| Contratos concluídos | 10% |
| Verificação de identidade | 10% |
| Disponibilidade | 5% |

Quando a IA está indisponível, há *fallback* para busca textual. A reindexação roda periodicamente (background service).

### SMTP (e-mail) — `EmailService`

Envia OTPs (confirmação de cadastro, assinatura de contrato, MFA), redefinição de senha e notificações (ex.: lembrete de avaliação pendente). As credenciais vêm da seção `Email` (bind em `EmailSettings`).

---

## Segurança e Compliance

### Pipeline de Segurança (ordem real dos middlewares)

```
Security Headers → Exception Handler → Static Files →
Authentication → Authorization → Rate Limiter → Onboarding Guard → Endpoint
```

### Autenticação e Autorização

| Mecanismo | Detalhe |
|-----------|---------|
| JWT | HMAC-SHA256, cookie httpOnly `tratoo_auth`, issuer/audience/lifetime, `ClockSkew = 0` |
| Roles | `Prestador`, `Contratante`, `Admin` (Admin apenas via seed/banco) |
| Policies | `RequireAuthorization("Prestador" | "Contratante" | "Admin")` |
| Ownership | Validado em cada serviço (usuário edita apenas o que lhe pertence) |
| MFA | Opcional, OTP por e-mail |
| Senhas | BCrypt (hash + salt) |
| Onboarding Guard | Bloqueia rotas (403) para perfis incompletos |

### Hardening HTTP

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: geolocation=(), camera=(), microphone=()
Content-Security-Policy: default-src 'self'; frame-ancestors 'none'
Strict-Transport-Security: (produção)
```

### Rate Limiting (janela fixa por IP)

| Política | Limite |
|----------|--------|
| `cadastro` | 5 / minuto |
| `login` | 10 / minuto |
| `senha` (reset) | 3 / minuto |
| `dados-bancarios` (token/confirmar/salvar) | 5 / minuto |
| `otp-assinatura` (OTP de contrato) | 3 / minuto |

Rejeições retornam **429** com mensagem amigável em JSON.

### Proteção de Dados (LGPD)

| Medida | Implementação |
|--------|---------------|
| CPF/CNPJ | Criptografado em repouso (AES, `DataProtector`); mascarado nos contratos |
| Consentimento | `ConsentLog` (termos/privacidade) com IP e versão |
| Auditoria | `AuditLog` de ações críticas |
| Direito ao esquecimento | Soft delete: anonimiza nome para "Usuário indisponível", remove e-mail/foto, bloqueia login, retira perfil das buscas; preserva contratos, pagamentos e avaliações |

---

## Background Services

| Serviço | Intervalo | Delay inicial | Função |
|---------|-----------|---------------|--------|
| `PropostaExpiracaoService` | 1 h | 15 s | Expira propostas vencidas (`ValidoAte`) |
| `ContratoExpiracaoService` | 1 h | 30 s | Cancela contratos não assinados no prazo |
| `PagamentoLiberacaoService` | 4 h | 2 min | Libera pagamentos `Retido` cujo prazo de liberação automática venceu |
| `AvaliacaoExpiracaoService` | 24 h | 3 min | Publica/oculta avaliações pendentes após 7 dias |
| `ReindexacaoBackgroundService` | semanal | 2 min | Reindexa embeddings desatualizados |

Características comuns: tratamento de erro **por item** (falha isolada não interrompe o lote), logging estruturado e *scope* próprio (`IServiceScopeFactory`) — um `DbContext` por iteração.

---

## Área Administrativa

Módulo protegido pela role Admin (concedida apenas via seed/banco).

```
/pages/admin/disputas   → lista filtrável (status, datas, contratante, prestador, projeto)
/pages/admin/disputa    → detalhe (projeto, partes, valor, evidências, histórico, status)
                        → A favor do contratante: estorno + Cancelado
                        → A favor do prestador: liberação + Encerrado
```

Resolução é imutável (não reabre) e gera trilha (estado anterior → posterior) em `HistoricoContrato`, `AuditLog` e `LedgerFinanceiro`. Liberação automática fica suspensa enquanto há disputa ativa.

---

## Configuração e Segredos

> **Nenhum segredo é versionado.** Os `appsettings.json` reais são ignorados pelo `.gitignore`. O repositório versiona apenas **`appsettings.example.json`** (template com placeholders). Configure localmente via **User Secrets** (`dotnet user-secrets`) ou **variáveis de ambiente**.

Chaves esperadas (valores fornecidos pelo ambiente, **não** comitados):

```
ConnectionStrings:DefaultConnection   # SQL Server   (TratooContext)
ConnectionStrings:VectorConnection    # PostgreSQL + pgvector (VectorContext)
Jwt:SecretKey / Jwt:Issuer / Jwt:Audience / Jwt:ExpirationHours
Asaas:ApiKey / Asaas:BaseUrl / Asaas:WebhookToken / ...
OpenAI:ApiKey / OpenAI:BaseUrl
CloudflareR2:*          # bucket público
CloudflareR2Private:*   # bucket privado
Email:Remetente / Email:Senha / Email:ServidorSmtp / Email:PortaSmtp
Seed:SenhaUsuario       # senha dos usuários de seed (somente desenvolvimento)
```

---

## Rodando o Projeto

Pré-requisitos: .NET 8 SDK, SQL Server e PostgreSQL com extensão pgvector.

```bash
# 1. Clone o repositório
git clone https://github.com/Gustavo-C-almeida/tratoo.git
cd tratoo

# 2. Configure os segredos
#    Opção A — copie o template e preencha:
cp Tratoo.API/appsettings.example.json Tratoo.API/appsettings.Development.json
#    Opção B (recomendada) — use User Secrets no projeto Tratoo.API:
cd Tratoo.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Database=TRATOO;..."
dotnet user-secrets set "ConnectionStrings:VectorConnection"  "Host=...;Database=...;Username=...;Password=..."
dotnet user-secrets set "Jwt:SecretKey" "uma-chave-secreta-forte-com-no-minimo-32-caracteres"
#    (repita para Asaas:*, OpenAI:*, CloudflareR2:*, CloudflareR2Private:*, Email:*)
cd ..

# 3. Aplique as migrations do SQL Server
#    (o PostgreSQL/pgvector é inicializado automaticamente na subida da API)
dotnet ef database update \
  --project Tratoo.Domain \
  --startup-project Tratoo.API \
  --context TratooContext

# 4. Rode a API (serve também o frontend estático)
dotnet run --project Tratoo.API
```

Acesse a aplicação na URL exibida no console (porta definida em `Tratoo.API/Properties/launchSettings.json`). Em `ASPNETCORE_ENVIRONMENT=Development`, o **Swagger** fica disponível em `/swagger` e os endpoints de **seed** (incluindo promoção de um usuário a administrador) são habilitados.

---

## Roadmap Técnico

### Prioridade 1 — Fundamentais
| Item | Descrição |
|------|-----------|
| Testes automatizados | xUnit + Moq + FluentAssertions (unitários de regra de negócio + integração) |
| FluentValidation | Separar validação de entrada da regra de negócio |
| `PagedResult<T>` | Padronizar paginação nos GETs de coleção |

### Prioridade 2 — Maturidade
| Item | Descrição |
|------|-----------|
| Auditoria automática | `SaveChangesInterceptor` para `CriadoPor/AtualizadoPor` |
| Soft delete padronizado | `ISoftDeletable` + Global Query Filter |
| Exception middleware | Classe dedicada + `ProblemDetails` (RFC 7807) |
| Filtros estruturados | Objeto de filtro por recurso |
| Refatorações SOLID | Interfaces para services de perfil; quebrar `PagamentoService` |

### Prioridade 3 — Escala e Resiliência
Health Checks · Cache distribuído (Redis) · Polly (retry/circuit breaker/timeout) · Idempotência explícita nos endpoints financeiros · OpenTelemetry · Mensageria (RabbitMQ) · CQRS onde agregar valor.

---

## Compliance Legal

- **Marco Civil da Internet (Lei 12.965/2014)** — retenção de logs de ações relevantes (`AuditLog`).
- **LGPD (Lei 13.709/2018)** — consentimento explícito (`ConsentLog`), criptografia de PII e exclusão de conta com anonimização e preservação de registros legais.
- **MP 2.200-2/2001** — assinatura eletrônica simples com garantias (OTP + hash SHA-256 + IP); **não** utiliza certificado ICP-Brasil.

---

<sub>README descritivo do projeto Tratoo — estudo de arquitetura .NET com escrow, contratos digitais, busca semântica e fluxo administrativo de disputas.</sub>
