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

### 1. Página inicial e escolha de papel

A página inicial apresenta o que a plataforma faz e para quem ela serve. Ao clicar em "Começar", o usuário escolhe em qual contexto se encaixa: "Quero contratar serviços" ou "Sou um prestador de serviços". O design das duas opções é igual; o que muda é que o cadastro já nasce apontado para o tipo certo de usuário.

### 2. Cadastro e confirmação de e-mail

O formulário pede nome, e-mail, senha, confirmação de senha e a opção de ativar MFA (segunda camada de segurança). Independentemente do MFA, o sistema sempre envia um código de confirmação para o e-mail fornecido — sem isso o cadastro não é efetivado. Isso impede que alguém registre um e-mail que não é dele.

Internamente: `POST /usuarios/cadastro` envia um OTP via `EmailService` e guarda os dados em cache. `POST /usuarios/cadastro/confirmar` valida o código e persiste o usuário. CPF/CNPJ informado no onboarding é criptografado em repouso via AES (`DataProtector`) e armazenado em `UserIdentity`.

### 3. Onboarding (perfil mínimo obrigatório)

No primeiro acesso, o sistema direciona para uma página de onboarding onde o usuário completa o perfil mínimo antes de poder usar a plataforma. Isso é obrigatório: se alguém tentar pular pela URL, o Onboarding Guard intercepta e devolve ao onboarding até que o perfil mínimo esteja completo (403 `ONBOARDING_PENDENTE`).

O formulário se adapta ao tipo de pessoa: pessoa jurídica (caso típico de contratante empresa) exibe campos de CNPJ, razão social e segmento; pessoa física exibe CPF e dados pessoais.

### 4. Perfil do contratante

Após o onboarding, o contratante pode editar seu perfil com: foto, biografia ou apresentação da empresa, site, perfil do LinkedIn, telefone (campo privado — só a plataforma vê) e uma configuração de privacidade que controla se as avaliações recebidas ficam visíveis publicamente.

### 5. Perfil do prestador

O perfil do prestador é a vitrine de trabalho dele. Cada elemento preenchido impacta diretamente no ranqueamento da busca semântica.

- Completude: barra de progresso mostrando quantos % do perfil estão preenchidos, com dicas do que falta (foto, bio, competências, portfólio etc.). O campo `PorcentagemCompleto` é calculado pelo sistema.
- Portfólio: cards dos melhores trabalhos, cada um podendo ter imagens ou PDF (upload para R2 privado), link externo (GitHub, Behance, site...), descrição e competências utilizadas naquele trabalho.
- Competências: habilidades com nível definido (Básico, Intermediário, Avançado, Especialista) e vinculadas ao portfólio e às experiências.
- Experiência profissional: cargos anteriores, empresa, datas, responsabilidades, opção de marcar como emprego atual e competências utilizadas.
- Certificações: certificados com data de validade, anexo (PDF ou imagem no R2) e link de verificação.
- Avaliações: nota média, distribuição de estrelas, comentários recebidos e opção de resposta pública.
- Profissionais similares: exibidos no perfil público para aumentar a chance de descoberta.

### 6. Criação de projeto e recomendação inteligente

O contratante cria um projeto informando título, descrição, categoria, orçamento, prazo e as habilidades que procura. O projeto pode ser salvo como rascunho (`Rascunho`) para revisão antes de publicar, ou publicado imediatamente (`Aberto`). Projetos rascunho não aparecem na busca pública — a publicação é um passo separado. Ao publicar, `ProjetoIndexadorService` transforma a descrição em embedding (OpenAI `text-embedding-3-small`, 1536 dims) e indexa no PostgreSQL/pgvector.

Ao visualizar o projeto, o sistema apresenta o prestador mais recomendado. O `BuscaSemanticaService` opera em duas camadas: pgvector retorna os top-100 por distância de cosseno (índice HNSW), e em seguida C# aplica filtros e um score composto:

| Fator | Peso |
|-------|------|
| Similaridade semântica | 35% |
| Habilidades (match exato) | 15% |
| Reputação | 15% |
| Completude do perfil | 10% |
| Contratos concluídos | 10% |
| Verificação de identidade | 10% |
| Disponibilidade | 5% |

O capricho no perfil — competências, experiências, portfólio — faz diferença real no ranqueamento. A partir da sugestão, o contratante pode enviar um convite diretamente ao prestador.

### 7. Convites e busca de projetos

Lado do prestador ao receber um convite: aparece uma notificação com os detalhes do projeto (escopo, valor, prazo). Ele aceita ou recusa. Se aceitar, entra oficialmente como candidato e imediatamente ganha acesso ao chat do projeto — ele e o contratante podem conversar desde esse momento para alinhar expectativas antes de qualquer proposta.

O prestador também pode buscar projetos ativamente na seção "Buscar Projetos", que exibe todos os projetos públicos dos contratantes. Os filtros disponíveis são: categoria, orçamento, prazo e palavra-chave. Cada card do projeto mostra título, descrição resumida, valor, habilidades exigidas e número de propostas enviadas. Depois de escolher, o prestador lê com calma e, se achar que dá conta, envia a proposta.

Além da busca manual, o sistema também recomenda projetos ao prestador automaticamente (`GET /api/busca/projetos/recomendados`), cruzando o perfil dele — competências, portfólio, experiências — contra os projetos abertos. Quanto mais completo o perfil, mais relevantes as recomendações.

O chat do projeto é liberado quando existe uma proposta entre as partes — seja ela enviada pelo prestador (espontânea ou após aceitar um convite) ou criada pelo contratante via fluxo de convite. Sem proposta, o acesso ao chat é negado (403). Funciona por REST com polling (3 s com aba ativa, 30 s em segundo plano) — não usa WebSocket. As duas partes conversam nele durante toda a negociação, podendo trocar mensagens, tirar dúvidas e alinhar detalhes antes de fechar no contrato.

### 8. Proposta e negociação versionada

A proposta tem dois estados antes de chegar ao contratante: o prestador primeiro cria um rascunho (`POST /api/propostas`) com valor, prazo, escopo e marcos de entrega, revisa e então envia explicitamente (`POST /api/propostas/{id}/enviar`). Só após o envio ela aparece para o contratante.

O contratante pode aceitar, recusar ou fazer uma contraproposta. Cada rodada vira uma `PropostaVersao` (até 10 versões), com regra de turno obrigatório: quem enviou a última versão não pode aceitá-la — o que impede que um lado "aceite a si mesmo". Recusar encerra a proposta sem gerar contrato; o prestador pode cancelar a própria proposta enquanto ela está pendente.

Na tela de propostas recebidas, o contratante vê um selo "Convidado" nas propostas de quem ele chamou diretamente e pode filtrar entre propostas de convidados e espontâneas, o que ajuda a organizar quando o projeto recebe muitos candidatos. Todo o histórico de versões fica registrado, versão por versão.

Ao aceitar, o sistema gera automaticamente um contrato pendente de assinatura dos dois lados.

### 9. Contrato e assinatura digital

Cada parte assina com um OTP enviado por e-mail (6 dígitos, validade de 10 minutos, máximo de 5 tentativas). Na primeira assinatura, o sistema calcula o `ConteudoHash` (SHA-256) do contrato e registra o IP — status muda para `AguardandoAssinatura`. Na segunda, valida o hash, grava um `ContratoSnapshot` imutável com os dados das partes, gera o PDF (QuestPDF) e o armazena no bucket R2 privado.

Se alguém tentar alterar qualquer dado depois, o hash detecta. O snapshot congela o que foi acordado. Base legal: MP 2.200-2/2001 (assinatura eletrônica simples, sem certificado ICP-Brasil).

Após a segunda assinatura, o PDF do contrato fica disponível para ambas as partes via URL pré-assinada temporária (15 minutos, gerada sob demanda no R2 privado).

### 10. Pagamento em garantia (escrow)

Com o contrato assinado, o sistema leva o contratante para o pagamento. O valor não vai direto ao prestador — fica retido na plataforma (escrow). `POST /api/pagamentos/iniciar` cria a cobrança PIX no Asaas e retorna o QR Code. O contratante paga; o Asaas chama o webhook `PAYMENT_RECEIVED` e o `Pagamento` passa para `Retido`. Cada movimentação é registrada em `LedgerFinanceiro` (imutável).

A tela mostra de forma transparente o valor bruto, a taxa operacional do gateway (campo `TaxaGateway`, meramente informativo — não é deduzido do repasse) e o que o prestador recebe.

O prestador tem a garantia de que o dinheiro existe e está reservado. O contratante tem a garantia de que ele só sai quando o serviço for entregue.

### 11. Execução e chat

Com o pagamento feito, o projeto entra em andamento. As partes continuam se comunicando pelo chat do projeto. Quando o prestador termina, ele registra a entrega formalmente.

### 12. Entrega formal

O prestador registra a entrega com descrição, observações, data, links externos e anexos (arquivos enviados ao bucket R2 privado). `EntregaService` cria a entidade `Entrega` com status `PendenteAprovacao`, move o contrato para `AguardandoAprovacaoEntrega`, atualiza `EntregaRegistradaEm` e registra em `HistoricoContrato`. O contratante é notificado.

### 13. Aprovação e liberação do pagamento

O contratante confere a entrega. Se estiver tudo certo, aprova — `AprovarEntregaAsync` encerra o contrato, cria os slots de avaliação (blind review) e dispara `IPagamentoService.LiberarPagamentoAsync`, que inicia a transferência PIX ao prestador no Asaas. O webhook `TRANSFER_DONE` confirma e o pagamento vai para `Liberado`.

Se houver algo a corrigir, o contratante solicita ajustes com um motivo obrigatório — a entrega vai para `Rejeitada`, o contrato volta a `Ativo` e o prestador pode enviar uma nova entrega.

Se o contratante não agir dentro do prazo, `PagamentoLiberacaoService` (background) libera automaticamente — protegendo quem trabalhou.

### 14. Cancelamento e estorno

Cancelamento de contrato: qualquer das partes pode cancelar, mas o sistema trata de forma justa — antes do pagamento é gratuito; com contrato ativo sem entrega, pode haver taxa de 5%; com entrega já registrada, o cancelamento é bloqueado e o caminho passa a ser a disputa. A regra usa `EntregaRegistradaEm` para determinar a situação.

Estorno de pagamento: antes de qualquer liberação ao prestador, o contratante pode solicitar estorno diretamente (`POST /api/pagamentos/{id}/estornar`). Isso aciona a devolução via Asaas sem necessidade de abrir disputa — um caminho mais simples para casos onde houve erro no pagamento ou acordo entre as partes. Diferente da disputa, o estorno não passa por análise administrativa.

### 15. Disputa e resolução administrativa

Se o contratante discordar da entrega em vez de aprovar ou solicitar ajustes, pode abrir uma disputa. `POST /api/pagamentos/{id}/disputar` cria `DisputaPagamento` e o pagamento vai para `EmDisputa` — a liberação automática por prazo fica suspensa. Um administrador resolve pela área restrita: a favor do contratante (estorno + `Cancelado`) ou do prestador (liberação + `Encerrado`). A decisão é imutável e gera trilha em `HistoricoContrato`, `AuditLog` e `LedgerFinanceiro`.

### 16. Avaliação às cegas e reputação

Com o contrato encerrado e o pagamento liberado, os dois se avaliam. É uma avaliação às cegas (blind review): nenhum dos lados vê a nota do outro até que ambos avaliem, ou até o prazo de 7 dias acabar. Após esse ponto, `AvaliacaoExpiracaoService` publica as avaliações preenchidas e oculta as vazias.

Isso existe para ninguém ter medo de ser honesto. Se a nota aparecesse na hora, dava para esperar, ver o que recebeu, e devolver uma nota de vingança. Às cegas, cada um avalia de verdade. As notas entram na reputação pública do prestador — a nota média, distribuição de estrelas e comentários que aparecem no perfil. Com o tempo, os bons profissionais se destacam.

### Gestão da conta

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
│ │ PostgreSQL + pgvector │ │      │ │ OpenAI    │  │ Resend (e-mail)│  │
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
| Resend (API HTTPS) | OTP, assinatura, MFA + notificações |

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
│   ├── Config/                     # ResendSettings (bind de configuração, sem segredos)
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

### E-mail transacional — `ResendEmailService`

Envia OTPs (confirmação de cadastro, assinatura de contrato, MFA), redefinição de senha e notificações (ex.: lembrete de avaliação pendente).

Usa a **API HTTPS do Resend** (`POST https://api.resend.com/emails`) em vez de SMTP: o plano Trial do Railway bloqueia SMTP outbound (25/465/587), enquanto HTTPS/443 funciona normalmente. Registrado como `HttpClient` tipado (`AddHttpClient<IEmailService, ResendEmailService>`), com timeout configurável e erros da API tratados sem expor a credencial.

A API key vem da variável de ambiente `RESEND_API_KEY` — nunca do `appsettings`, que carrega apenas remetente/URL/timeout (bind em `ResendSettings`).

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
RESEND_API_KEY / RESEND_FROM_EMAIL / RESEND_FROM_NAME   # e-mail transacional (Resend)
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



<sub>README descritivo do projeto Tratoo — estudo de arquitetura .NET com escrow, contratos digitais, busca semântica e fluxo administrativo de disputas.</sub>
