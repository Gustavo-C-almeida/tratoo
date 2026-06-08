# Tratoo — Marketplace de Serviços com Escrow e Garantia

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF%20Core-9.0-512BD4)](https://learn.microsoft.com/ef/core/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-transacional-CC2927)](https://www.microsoft.com/sql-server)
[![pgvector](https://img.shields.io/badge/PostgreSQL-pgvector%20(HNSW)-336791)](https://github.com/pgvector/pgvector)
[![Storage](https://img.shields.io/badge/Storage-Cloudflare%20R2-F38020)](https://developers.cloudflare.com/r2/)

O Tratoo é uma plataforma que conecta empresas e pessoas que precisam contratar um serviço a profissionais freelancers, oferecendo contrato digital, pagamento protegido e mecanismos de confiança para ambas as partes.

1. Cadastro e criação do perfil

Tudo começa quando o usuário cria sua conta.

Ao se cadastrar, ele confirma seu e-mail e escolhe como deseja atuar na plataforma:

Contratante → quem precisa contratar um serviço.
Prestador → quem deseja oferecer seus serviços.

Após o cadastro, o usuário passa por um onboarding inicial para preencher as informações mínimas do perfil.

Além disso, pode realizar a validação de identidade utilizando CPF ou CNPJ, aumentando sua credibilidade dentro da plataforma.

Fluxo do contratante
2. Publicação do projeto

O contratante descreve sua necessidade:

O que precisa ser feito;
Prazo desejado;
Orçamento disponível;
Competências necessárias.

Exemplo:

"Preciso desenvolver uma API REST em .NET 8 integrada com PostgreSQL e autenticação JWT."

O projeto é publicado e passa a ficar visível para prestadores compatíveis.

3. Descoberta de profissionais

A plataforma utiliza busca inteligente baseada em IA.

Em vez de procurar apenas por palavras-chave, o sistema analisa:

Competências;
Experiências;
Certificações;
Portfólio;
Histórico profissional.

Assim, o contratante recebe sugestões de profissionais realmente compatíveis com sua demanda.

Também é possível convidar diretamente um prestador específico.

4. Negociação

Após demonstrar interesse, o prestador envia uma proposta contendo:

Valor;
Prazo;
Escopo do serviço.

O contratante pode:

Aceitar;
Recusar;
Fazer uma contraproposta.

As negociações ficam registradas em versões sucessivas para que exista histórico completo do acordo.

Durante essa etapa, ambos podem conversar pelo chat integrado da plataforma.

5. Aceitação da proposta

Quando contratante e prestador chegam a um consenso:

A proposta é aceita;
O sistema bloqueia novas alterações;
O processo segue para formalização contratual.
Formalização do acordo
6. Geração automática do contrato

Com a proposta aceita, o Tratoo gera automaticamente um contrato digital contendo:

Escopo acordado;
Valor;
Prazo;
Obrigações das partes.

Não é necessário redigir documentos manualmente.

7. Assinatura digital

Ambas as partes recebem um código de confirmação por e-mail.

Ao informar esse código:

A assinatura é registrada;
O IP é armazenado;
Um hash de integridade é gerado;
O PDF assinado é arquivado.

Isso cria evidências digitais do aceite do contrato.

Pagamento protegido
8. Depósito em garantia (Escrow)

Após a assinatura do contrato, o contratante realiza o pagamento via PIX.

Mas o dinheiro não vai imediatamente para o prestador.

O valor fica retido em uma conta de garantia (escrow).

Isso gera proteção para ambos:

Para o contratante

Só libera o dinheiro quando receber o serviço.

Para o prestador

Tem a certeza de que o valor já foi pago e reservado.
Execução do trabalho
9. Desenvolvimento e entrega

O prestador executa o serviço normalmente.

Quando concluir:

Registra oficialmente a entrega;
Pode anexar arquivos;
Pode adicionar links;
Pode incluir observações.

A entrega passa a fazer parte do histórico do contrato.

10. Aprovação da entrega

O contratante recebe uma notificação e pode:

Aprovar

Se tudo estiver correto:

O serviço é considerado concluído;
O pagamento é liberado.
Solicitar ajustes

Caso algo precise ser corrigido:

O contratante informa os pontos necessários;
O prestador realiza os ajustes.
Liberação do pagamento
11. Recebimento pelo prestador

Quando a entrega é aprovada:

O dinheiro sai da garantia;
O pagamento é transferido ao prestador;
A operação é registrada no livro-razão financeiro da plataforma.

Em alguns cenários, se o contratante não se manifestar dentro do prazo definido, a liberação pode ocorrer automaticamente.

Construção de reputação
12. Avaliações

Após o encerramento do trabalho:

O contratante avalia o prestador;
O prestador avalia o contratante.

O sistema utiliza blind review.

Ou seja:

Uma parte não vê a avaliação da outra imediatamente;
As avaliações só são publicadas quando ambos avaliam ou quando o prazo expira.

Isso reduz avaliações retaliatórias e aumenta a confiabilidade da reputação.

Caso ocorra algum problema
13. Abertura de disputa

Se houver desacordo sobre a entrega:

O contratante pode abrir uma disputa;
O pagamento continua retido;
Nenhuma das partes recebe ou perde o valor até a análise.

Ambos podem anexar:

Evidências;
Conversas;
Arquivos;
Histórico da negociação.
14. Análise administrativa

Administradores da plataforma acessam uma área restrita para avaliar o caso.

Eles podem decidir:

Favor do contratante
Pagamento estornado;
Contrato encerrado;
Disputa resolvida.
Favor do prestador
Pagamento liberado;
Contrato concluído;
Disputa resolvida.

Toda a análise fica registrada para auditoria.

Gestão da conta

Em qualquer momento, o usuário pode:

Atualizar seu perfil;
Adicionar experiências;
Inserir certificações;
Publicar portfólio;
Configurar chave PIX;
Ativar MFA;
Alterar senha;
Solicitar exclusão da conta.

Quando a exclusão é solicitada, os dados pessoais são anonimizados conforme LGPD, mas o histórico contratual é preservado para fins legais e financeiros.

> ⚠️ Projeto de estudo/portfólio. Veja [Configuração e Segredos](#configuração-e-segredos) — **nenhuma credencial é versionada** neste repositório.

---

## Índice

- [Visão Geral](#visão-geral)
- [Arquitetura](#arquitetura)
- [Stack Tecnológica](#stack-tecnológica)
- [Estrutura do Projeto](#estrutura-do-projeto)
- [Modelagem de Domínio](#modelagem-de-domínio)
- [Fluxos Detalhados](#fluxos-detalhados)
- [Segurança e Compliance](#segurança-e-compliance)
- [Integrações Externas](#integrações-externas)
- [Background Services](#background-services)
- [Área Administrativa](#área-administrativa)
- [Configuração e Segredos](#configuração-e-segredos)
- [Roadmap Técnico](#roadmap-técnico)

---

## Visão Geral

| Problema | Solução Tratoo |
|----------|----------------|
| Prestador teme não receber | Valor retido em escrow **antes** do início do trabalho |
| Contratante teme pagar e não receber | Pagamento só liberado **após aprovação** da entrega |
| Falta de confiança entre desconhecidos | Identidade verificada (CPF/CNPJ), reputação e avaliações *blind review* |
| Disputas sem mediação | Fluxo formal de disputa com **resolução por administrador** |
| Sem garantia documental | Contrato digital assinado com **OTP + hash SHA-256 + IP** |

**Importante sobre valores:** a plataforma **não cobra taxa própria** no MVP — o valor é repassado **integralmente** ao prestador. Existe apenas a *taxa operacional do gateway* (Asaas), exibida de forma **meramente informativa** (não entra em nenhum cálculo do sistema).

---

## Arquitetura

Solução em camadas (Clean Architecture + Feature Folders), com **persistência poliglota**.

```
┌──────────────────────────────────────────────────────────────────────┐
│                         CLIENTE — Web (HTML/CSS/JS vanilla)           │
│                     servido como arquivos estáticos pela API          │
└───────────────────────────────┬──────────────────────────────────────┘
                                │ cookie httpOnly (JWT)
                                ▼
┌──────────────────────────────────────────────────────────────────────┐
│                   APRESENTAÇÃO — Tratoo.API (.NET 8)                  │
│  Minimal APIs · Middlewares · Security Headers · Rate Limiting        │
│  Auth (JWT) · Onboarding Guard · Static Files · Background Services    │
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
│ │ (transacional)        │ │      │ │ R2 (blob) │  │ escrow)        │  │
│ └───────────────────────┘ │      │ └──────────┘  └────────────────┘  │
│ ┌───────────────────────┐ │      │ ┌──────────┐  ┌────────────────┐  │
│ │ PostgreSQL + pgvector │ │      │ │ OpenAI    │  │ SMTP (e-mail)  │  │
│ │ (busca semântica HNSW)│ │      │ │ Embeddings│  │                │  │
│ └───────────────────────┘ │      │ └──────────┘  └────────────────┘  │
└───────────────────────────┘      └───────────────────────────────────┘
```

### Persistência Poliglota

| Banco | Uso | Tecnologia |
|-------|-----|------------|
| **SQL Server** | Dados transacionais (usuários, projetos, contratos, pagamentos, disputas) | EF Core 9 (`TratooContext`) |
| **PostgreSQL + pgvector** | Embeddings para busca semântica (prestadores, projetos) | Índice HNSW (`VectorContext`) |

### Fluxo de uma Requisição (ex.: aprovar entrega)

```
Browser (cookie httpOnly)
  │
  ▼ 1. Security Headers (CSP, HSTS, X-Frame-Options, X-Content-Type-Options)
  ▼ 2. UseExceptionHandler  → NegocioException = 400 amigável | demais = 500 + log
  ▼ 3. UseAuthentication    → lê/valida JWT do cookie (issuer, audience, lifetime, ClockSkew=0)
  ▼ 4. UseAuthorization     → roles/policies (Prestador / Contratante / Admin)
  ▼ 5. UseRateLimiter       → janelas por IP em rotas sensíveis
  ▼ 6. Onboarding Guard     → bloqueia perfis incompletos (403), com rotas isentas
  ▼ 7. Endpoint            → extrai userId das claims, chama o Service
  ▼ 8. EntregaService.AprovarEntregaAsync
        - valida ownership (é parte do contrato) e status
        - aprova entrega → encerra contrato → cria avaliações (blind review)
        - libera pagamento (reuso de IPagamentoService) → tudo logado e auditado
  ▼ 9. SaveChanges (transação no SQL Server)
  ▼ 10. Resposta JSON (DTO — entidades nunca são serializadas diretamente)
```

---

## Stack Tecnológica

### Backend

| Componente | Tecnologia |
|------------|------------|
| Runtime | .NET 8 |
| API | ASP.NET Core Minimal APIs |
| ORM | Entity Framework Core 9 (SQL Server + PostgreSQL/Npgsql) |
| Busca vetorial | Pgvector.EntityFrameworkCore (índice HNSW) |
| Logging | Serilog (console + arquivo rotativo) + `ILogger` |
| Autenticação | JWT (HMAC-SHA256) em cookie httpOnly |
| Hash de senha | BCrypt (BCrypt.Net-Next) |
| Geração de PDF | QuestPDF |
| Storage | AWS SDK S3 (compatível com Cloudflare R2) |
| Validação | Manual no domínio (*Always-Valid Domain*) via `NegocioException` |
| DI / Mapeamento | `Microsoft.Extensions.DependencyInjection` · mapeamento manual |

### Frontend

HTML/CSS/JavaScript **vanilla** (sem framework), servido como arquivos estáticos pela própria API (`UseStaticFiles`).

### Infraestrutura

| Serviço | Finalidade |
|---------|-----------|
| Cloudflare R2 (bucket público) | Fotos de perfil, portfólio |
| Cloudflare R2 (bucket privado) | PDFs de contrato e anexos de entrega (URL pré-assinada) |
| Asaas | Cobrança PIX, escrow lógico, transferências, estornos, webhooks |
| OpenAI Embeddings (`text-embedding-3-small`, 1536 dims) | Vetores para busca semântica |
| SMTP | Códigos de verificação (OTP) e notificações |

---

## Estrutura do Projeto

```
Tratoo/
├── Tratoo.API/                     # Apresentação
│   ├── EndPoints/                  # Minimal APIs por recurso (Extension Methods)
│   │   ├── ProjetoExtensions.cs        PropostaExtensions.cs
│   │   ├── ContratoExtensions.cs       PagamentoExtensions.cs
│   │   ├── AvaliacaoExtensions.cs      BuscaExtensions.cs
│   │   ├── AdminDisputaExtensions.cs   ...
│   ├── BackgroundServices/         # Serviços em segundo plano (IHostedService)
│   ├── Requests/                   # DTOs de entrada HTTP
│   └── Program.cs                  # Composição, DI, pipeline, segurança
│
├── Tratoo.Domain/                  # Núcleo
│   ├── Domain/
│   │   ├── Models/                 # Entidades EF Core (Usuario, Projeto, ...)
│   │   └── Enums/                  # Enumerações
│   ├── Features/                   # Feature Folders (coesão por funcionalidade)
│   │   ├── Auth/  Projetos/  Propostas/  Contratos/  Pagamentos/
│   │   ├── Avaliacoes/  Perfis/  Mensagens/  IA/  Infrastructure/
│   │   │     └── cada uma com DTOs/ · Repositories/ · Services/
│   ├── Data/                       # TratooContext (SQL Server) e VectorContext (pgvector)
│   └── Migrations/                 # Migrations EF Core
│
└── Tratoo.Web/wwwroot/             # Frontend estático
    ├── pages/                      # Páginas por área (contratante, prestador, admin, ...)
    ├── assets/css · assets/js      # Estilos e scripts (vanilla)
    └── components/                 # Componentes de header/footer
```

---

## Modelagem de Domínio

### Usuário (TPT: base `Usuario` + subtipos)

```
Usuario (abstrata)
  - Id (int) · Nome · Email · SenhaHash
  - TipoUsuario (Prestador | Contratante)
  - Status (Pending | Active | Blocked)
  - IsAdmin (bool)  → administrador, definido apenas via seed/banco
  - MFA · IdentidadeVerificada · TipoPessoa · Endereco · Telefone
  - AvaliacoesPrivado · ExcluidoEm (soft delete / LGPD) · DataCadastro
        ├── Prestador     (TituloProfissional, AreaEspecializacao, FotoUrl,
        │                  competências, experiências, certificações, portfólio,
        │                  conta bancária / chave PIX, PorcentagemCompleto)
        └── Contratante   (Segmento, NomeEmpresa, LogoUrl, SiteUrl, Disponibilidade...)

  Identidade/consentimento:
   - UserIdentity (CPF/CNPJ criptografado, NivelVerificacao)
   - ConsentLog   (termos/privacidade, IP, versão) · AuditLog (ações críticas)
```

### Projeto → Proposta → Contrato → Pagamento

```
Projeto (1) ───< Proposta (1) ───< PropostaVersao (negociação versionada)
   │                                     │
   │  Status: Rascunho/Aberto/           │  CriadoPor = quem enviou a versão
   │          EmAndamento/Cancelado      │  (autor da última versão NÃO pode aceitá-la)
   │                                     ▼  (no aceite)
   │                              ContratoServico (1) ──── Pagamento (1)
   │   Status: Gerado → AguardandoAssinatura → Ativo →           │
   │           AguardandoAprovacaoEntrega → Encerrado | Cancelado │
   │   ConteudoJson + ConteudoHash (SHA-256) · assinaturas + IP   │
   │                                                              ▼
   │                                                   LedgerFinanceiro (imutável)
   │                                                   DisputaPagamento (0..N)
```

**Pagamento** guarda `ValorBruto` e `TaxaGateway` (informativa do Asaas — **não** é taxa da plataforma). O **LedgerFinanceiro** é um livro-razão **imutável** (nunca alterado/excluído) — base de rastreabilidade financeira.

### Entidades de auditoria/compliance

| Entidade | Finalidade |
|----------|-----------|
| **AuditLog** | Ações críticas (login, assinatura, exclusão de conta, resolução de disputa) |
| **ConsentLog** | Consentimento LGPD (termos/privacidade) com IP e versão |
| **HistoricoContrato** | Trilha por contrato (entrega, aprovação, liberação, disputa resolvida) |
| **WebhookLog** | Idempotência dos webhooks do gateway |

---

## Fluxos Detalhados

### 1) Cadastro e Onboarding

| Fase | Endpoint | Validações | Resultado |
|------|----------|-----------|-----------|
| Cadastro | `POST /usuarios/cadastro` | senha forte, e-mail único | OTP por e-mail; dados em cache temporário |
| Confirmação | `POST /usuarios/cadastro/confirmar` | código válido (expira) | Usuário `Active` |
| Onboarding | `POST /usuarios/onboarding` | CPF/CNPJ válido e único | `UserIdentity` com documento criptografado; perfil mínimo completo |

### 2) Projeto e Propostas

- Contratante publica o projeto → embedding gerado para busca semântica.
- Prestador cria proposta (rascunho) e envia.
- Negociação versionada (até 10 versões); **a parte que enviou a última versão não pode aceitá-la** (turno obrigatório, validado no back-end e no front-end).
- Aceite → geração automática do contrato.
- Regras: uma proposta ativa por prestador/projeto; prestador não propõe no próprio projeto; proposta expira por prazo (background service).

### 3) Contrato e Assinatura Digital

1. **Gerado** ao aceitar a proposta (expira para assinatura em 7 dias).
2. **1ª assinatura** (OTP por e-mail) → calcula `ConteudoHash` (SHA-256) e registra IP → `AguardandoAssinatura`.
3. **2ª assinatura** → valida que o hash não mudou → `Ativo`, snapshot imutável e **PDF** (QuestPDF) no bucket privado.

Garantias: OTP vincula o signatário, IP dá rastreabilidade, hash prova não-adulteração, snapshot/PDF preservam o conteúdo no momento da assinatura (base legal: MP 2.200-2/2001).

### 4) Pagamento com Escrow

```
Contratante paga via PIX (QR Code Asaas)
        │  webhook PAYMENT_RECEIVED (idempotente)
        ▼
   Pagamento = Retido (escrow)   ──────────────┐
        │                                       │ disputa aberta?
   Prestador registra a ENTREGA formal          ▼
        │                                  Pagamento = EmDisputa
   Contratante analisa                     (liberação suspensa)
        ├── Aprova  → libera (PIX ao prestador) + contrato Encerrado + avaliações
        └── Solicita ajustes  → prestador reenvia
```

- **Repasse integral** ao prestador (sem taxa da plataforma).
- **Liberação automática** por prazo (background service) protege o prestador se o contratante não agir.
- Idempotência de webhooks (`WebhookLog`) e reivindicação atômica de status evitam transferências duplicadas.

### 5) Avaliação *Blind Review*

Ao liberar o pagamento, são criados 2 slots (contratante↔prestador). As notas só se tornam **públicas quando ambas as partes avaliam** — ou, após 7 dias, publica-se a preenchida e oculta-se a vazia. Reputação recalculada e embeddings reindexados ao publicar. Evita retaliação.

---

## Segurança e Compliance

### Autenticação e Autorização

| Mecanismo | Detalhe |
|-----------|---------|
| JWT | HMAC-SHA256, cookie **httpOnly**, validação de issuer/audience/lifetime, `ClockSkew = 0` |
| Roles | `Prestador`, `Contratante`, `Admin` (Admin só via seed/banco) |
| Policies | `RequireAuthorization("Prestador" | "Contratante" | "Admin")` |
| Ownership | Validado em cada serviço (cada um edita/lê apenas o que lhe pertence) |
| MFA | Opcional, OTP por e-mail |
| Senhas | BCrypt (hash + salt) |
| Onboarding Guard | Bloqueia rotas para perfis incompletos (com rotas públicas isentas) |

### Proteção de Dados (LGPD)

| Medida | Implementação |
|--------|---------------|
| CPF/CNPJ | Criptografado em repouso |
| Consentimento | `ConsentLog` (termos/privacidade) com IP e versão |
| Auditoria | `AuditLog` de ações críticas |
| Direito ao esquecimento | Exclusão de conta via **soft delete**: anonimiza nome para **"Usuário indisponível"**, remove documento, bloqueia login, retira o perfil das buscas e **preserva** registros históricos (contratos, pagamentos, avaliações) |
| Mascaramento de PII | CPF/CNPJ exibido mascarado nos contratos |

### Hardening HTTP

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: geolocation=(), camera=(), microphone=()
Content-Security-Policy: default-src 'self'; ...
Strict-Transport-Security: (apenas em produção)
```

### Rate Limiting (janela fixa por IP)

| Política | Limite |
|----------|--------|
| Cadastro | 5 / minuto |
| Login | 10 / minuto |
| Reset de senha | 3 / minuto |
| Dados bancários (token/confirmar/salvar) | 5 / minuto |
| OTP de assinatura de contrato | 3 / minuto |

---

## Integrações Externas

### Asaas (pagamentos)

Operações: criar/recuperar cliente, criar cobrança PIX (QR Code), consultar cobrança (sincronização), criar transferência PIX ao prestador, estornar.

**Webhooks tratados** (idempotentes):

| Evento | Ação |
|--------|------|
| `PAYMENT_RECEIVED` | Pagamento → `Retido` (escrow) |
| `PAYMENT_REFUNDED` | Pagamento → `Estornado` |
| `TRANSFER_DONE` | Transferência ao prestador confirmada → `Liberado` |
| `TRANSFER_FAILED` | Marca falha para reprocessamento |

### Cloudflare R2

| Bucket | Uso | Acesso |
|--------|-----|--------|
| Público | Fotos de perfil, portfólio | URL direta |
| Privado | PDFs de contrato, anexos de entrega | **URL pré-assinada** temporária |

### Busca Semântica (IA)

Prestadores e projetos são transformados em **embeddings** (OpenAI `text-embedding-3-small`, 1536 dims) e armazenados no **PostgreSQL/pgvector** (índice HNSW). A busca combina similaridade vetorial com filtros (score composto). Reindexação automática periódica (background service).

---

## Background Services

| Serviço | Intervalo | Delay inicial | Função |
|---------|-----------|---------------|--------|
| `PropostaExpiracaoService` | 1 h | 15 s | Expira propostas vencidas (`ValidoAte`) |
| `ContratoExpiracaoService` | 1 h | 30 s | Cancela contratos não assinados no prazo |
| `PagamentoLiberacaoService` | 4 h | 2 min | Libera pagamentos `Retido` cujo prazo de liberação automática venceu |
| `AvaliacaoExpiracaoService` | 24 h | 3 min | Publica/oculta avaliações pendentes após 7 dias |
| `ReindexacaoBackgroundService` | semanal | 2 min | Reindexa embeddings desatualizados |

Características comuns: tratamento de erro por item (falha isolada não interrompe o lote), logging estruturado e uso de *scopes* (`IServiceScopeFactory`) para um `DbContext` por iteração.

---

## Área Administrativa

Módulo protegido por role **Admin** (concedida apenas via seed/banco — nunca por fluxo da aplicação).

- **`/pages/admin/disputas`** — lista de disputas com filtros (status, datas, contratante, prestador, projeto).
- **`/pages/admin/disputa`** — detalhe completo (projeto, partes, valor, evidências, histórico do contrato, status atuais) e resolução:
  - **A favor do contratante** → estorno do pagamento + contrato `Cancelado`.
  - **A favor do prestador** → liberação do pagamento + contrato `Encerrado`.
- Resolução é **imutável** (disputa resolvida não reabre) e gera trilha de auditoria com **estado anterior → posterior** (`HistoricoContrato` + `AuditLog` + `LedgerFinanceiro`). Enquanto há disputa ativa, a liberação automática e a conclusão ficam **suspensas**.

---

## Configuração e Segredos

> **Nenhum segredo é versionado.** Os `appsettings.json` do repositório **não** contêm credenciais reais. Configure localmente via **User Secrets** (`dotnet user-secrets`) ou **variáveis de ambiente**.

Chaves esperadas (valores devem ser fornecidos pelo ambiente, **não** comitados):

```
ConnectionStrings:DefaultConnection   # SQL Server
ConnectionStrings:VectorConnection    # PostgreSQL + pgvector
Jwt:SecretKey / Jwt:Issuer / Jwt:Audience / Jwt:ExpirationHours
Asaas:*            # chave de API e configurações do gateway
OpenAI:ApiKey / OpenAI:BaseUrl
CloudflareR2:*     # bucket público
CloudflareR2Private:*  # bucket privado
SMTP / e-mail (provedor de envio)
```

### Executando localmente (visão geral)

1. Provisione **SQL Server** e **PostgreSQL com a extensão pgvector**.
2. Configure as chaves acima via `dotnet user-secrets` (no projeto `Tratoo.API`) ou variáveis de ambiente.
3. Aplique as migrations: `dotnet ef database update --project Tratoo.Domain --startup-project Tratoo.API --context TratooContext`.
4. Rode a API (`dotnet run --project Tratoo.API`); o frontend estático é servido pela própria API.
5. Em ambiente de desenvolvimento há endpoints de *seed* (incluindo promoção de um usuário a administrador) habilitados **apenas** quando `ASPNETCORE_ENVIRONMENT=Development`.

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

## Compliance

- **Marco Civil da Internet (Lei 12.965/2014)** — retenção de logs de ações relevantes (`AuditLog`).
- **LGPD (Lei 13.709/2018)** — consentimento explícito (`ConsentLog`), exclusão de conta com anonimização e preservação de registros legais.
- **MP 2.200-2/2001** — assinatura digital simples com garantias (OTP + hash SHA-256 + IP).

---

<sub>README descritivo do projeto Tratoo. Estudo de arquitetura .NET com escrow, contratos digitais, busca semântica e fluxo administrativo de disputas.</sub>
