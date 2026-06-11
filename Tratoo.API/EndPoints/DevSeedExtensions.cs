using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.API.EndPoints
{
    /// <summary>
    /// Endpoint de seed para desenvolvimento — cria dados de teste para os endpoints de pagamento.
    /// Disponível APENAS em ambiente Development.
    /// </summary>
    public static class DevSeedExtensions
    {
        private const string SeedEmailContratante = "contratante.seed@tratoo.dev";
        private const string SeedEmailPrestador   = "prestador.seed@tratoo.dev";

        public static void AddEndPointsDevSeed(this WebApplication app)
        {
            if (!app.Environment.IsDevelopment()) return;

            // ──────────────────────────────────────────────────────────────────────
            // POST /api/dev/seed-admin?email=...
            // Promove um usuário existente a administrador (role Admin).
            // Admins só podem ser criados por seed/banco — nunca por fluxo de aplicação.
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/dev/seed-admin", async (string email, TratooContext db, IConfiguration config) =>
            {
                if (string.IsNullOrWhiteSpace(email))
                    return Results.BadRequest(new { mensagem = "Informe o e-mail do usuário (?email=...)." });

                var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email);
                if (usuario == null)
                    return Results.NotFound(new { mensagem = "Usuário não encontrado." });

                usuario.IsAdmin = true;
                await db.SaveChangesAsync();

                return Results.Ok(new { mensagem = $"Usuário {email} promovido a administrador. Faça login novamente para obter a role Admin." });
            });

            // ──────────────────────────────────────────────────────────────────────
            // POST /api/dev/seed-pagamentos
            // Cria usuários, projetos, contratos e pagamentos em vários estados.
            // Retorna um JSON com todos os IDs e credenciais para testar os endpoints.
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/dev/seed-pagamentos", async (TratooContext db, IConfiguration config) =>
            {
                var seedSenha = config["Seed:SenhaUsuario"] ?? "Tratoo@123";

                // Idempotência — evita seed duplo
                if (await db.Usuarios.AnyAsync(u => u.Email == SeedEmailContratante))
                {
                    var existente = await db.Contratantes
                        .FirstAsync(u => u.Email == SeedEmailContratante);
                    var existentePrestador = await db.Prestadores
                        .FirstAsync(u => u.Email == SeedEmailPrestador);

                    return Results.Ok(new
                    {
                        mensagem = "Seed já foi executado anteriormente. Re-execute DELETE /api/dev/seed-pagamentos para limpar e recriar.",
                        credenciais = new
                        {
                            contratante = new { email = SeedEmailContratante, senha = seedSenha, id = existente.Id },
                            prestador   = new { email = SeedEmailPrestador,   senha = seedSenha, id = existentePrestador.Id }
                        }
                    });
                }

                // ── 1. Usuários ───────────────────────────────────────────────────
                var senhaHash = BCrypt.Net.BCrypt.HashPassword(seedSenha);

                var contratante = new Contratante
                {
                    Nome          = "Contratante Seed",
                    Email         = SeedEmailContratante,
                    SenhaHash     = senhaHash,
                    TipoUsuario   = TipoUsuario.Contratante,
                    Status        = StatusUsuario.Active,
                    IdentidadeVerificada = true,
                    TipoPessoa    = TipoPessoa.PessoaFisica,
                    Endereco      = new Endereco
                    {
                        Cep         = "01310-100",
                        Logradouro  = "Av. Paulista",
                        Numero      = "1000",
                        Bairro      = "Bela Vista",
                        Cidade      = "São Paulo",
                        Estado      = "SP"
                    }
                };
                contratante.VerificarPerfilMinimo();

                var prestador = new Prestador
                {
                    Nome               = "Prestador Seed",
                    Email              = SeedEmailPrestador,
                    SenhaHash          = senhaHash,
                    TipoUsuario        = TipoUsuario.Prestador,
                    Status             = StatusUsuario.Active,
                    IdentidadeVerificada = true,
                    TipoPessoa         = TipoPessoa.PessoaFisica,
                    AreaEspecializacao = "Design e Conteúdo Digital",
                    FuncaoExecutada    = "Designer e Social Media",
                    Disponivel         = true,
                    Endereco           = new Endereco
                    {
                        Cep        = "04538-133",
                        Logradouro = "Av. Brigadeiro Faria Lima",
                        Numero     = "3900",
                        Bairro     = "Itaim Bibi",
                        Cidade     = "São Paulo",
                        Estado     = "SP"
                    }
                };
                prestador.VerificarPerfilMinimo();

                db.Contratantes.Add(contratante);
                db.Prestadores.Add(prestador);
                await db.SaveChangesAsync();

                // ── 2. UserIdentity ───────────────────────────────────────────────
                db.UserIdentities.Add(new UserIdentity
                {
                    UserId                 = contratante.Id,
                    CpfCnpjCriptografado   = "SEED_ENCRYPTED_CPF_CONTRATANTE",
                    NomeLegal              = "Contratante Seed Silva",
                    NivelVerificacao       = NivelVerificacao.Identidade,
                    DataNascimento         = new DateOnly(1990, 1, 15)
                });
                db.UserIdentities.Add(new UserIdentity
                {
                    UserId                 = prestador.Id,
                    CpfCnpjCriptografado   = "SEED_ENCRYPTED_CPF_PRESTADOR",
                    NomeLegal              = "Prestador Seed Santos",
                    NivelVerificacao       = NivelVerificacao.Financeiro,
                    DataNascimento         = new DateOnly(1992, 6, 20)
                });
                await db.SaveChangesAsync();

                // ── Helper: cria Projeto → PropostaProjeto → ContratoServico Ativo ─
                async Task<ContratoServico> CriarContratoAtivoAsync(string titulo, decimal valor, CategoriaProjet categoria)
                {
                    var agora = DateTime.UtcNow;

                    var projeto = new Projeto
                    {
                        ContratanteId          = contratante.Id,
                        Titulo                 = titulo,
                        Descricao              = "Projeto de seed criado para testes de pagamento.",
                        Categoria              = categoria,
                        OrcamentoMin           = valor,
                        OrcamentoMax           = valor,
                        PrazoEntrega           = agora.AddDays(30),
                        Status                 = StatusProjeto.EmAndamento,
                        Visibilidade           = VisibilidadeProjeto.Publico,
                        Publicado              = true,
                        PublicadoEm            = agora.AddDays(-10),
                        FreelancerSelecionadoId= prestador.Id
                    };
                    db.Projetos.Add(projeto);
                    await db.SaveChangesAsync();

                    var proposta = new PropostaProjeto
                    {
                        ProjetoId   = projeto.Id,
                        PrestadorId = prestador.Id,
                        Status      = StatusPropostaProjeto.Convertida,
                        VersaoAtual = 1,
                        ValidoAte   = agora.AddDays(30)
                    };
                    db.PropostasProjeto.Add(proposta);
                    await db.SaveChangesAsync();

                    var contrato = new ContratoServico
                    {
                        ProjetoId              = projeto.Id,
                        PropostaId             = proposta.Id,
                        ContratanteId          = contratante.Id,
                        PrestadorId            = prestador.Id,
                        Status                 = ContratoServicoStatus.Ativo,
                        ConteudoJson           = $"{{\"titulo\":\"{titulo}\",\"valor\":{valor},\"prazo\":\"30 dias\"}}",
                        ConteudoHash           = "SEED_SHA256_" + Guid.NewGuid().ToString("N")[..8],
                        AssinadoContratanteEm  = agora.AddDays(-5),
                        IpContratante          = "127.0.0.1",
                        AssinadoPrestadorEm    = agora.AddDays(-5),
                        IpPrestador            = "127.0.0.1",
                        CriadoEm              = agora.AddDays(-6),
                        ExpiraEm              = agora.AddDays(90)
                    };
                    db.ContratosServico.Add(contrato);
                    await db.SaveChangesAsync();

                    return contrato;
                }

                // ═══════════════════════════════════════════════════════════════════
                // Cenário A — Contrato Ativo SEM pagamento
                // Endpoint: POST /api/pagamentos/iniciar
                // ═══════════════════════════════════════════════════════════════════
                var contratoA = await CriarContratoAtivoAsync("[SEED-A] Identidade visual para cafeteria", 500.00m, CategoriaProjet.Design);

                // ═══════════════════════════════════════════════════════════════════
                // Cenário B — Pagamento com status Aguardando (QR Code PIX gerado)
                // Endpoints: GET /api/pagamentos/{id}
                //            GET /api/pagamentos/{id}/pix
                // ═══════════════════════════════════════════════════════════════════
                var contratoB       = await CriarContratoAtivoAsync("[SEED-B] Gestão de Instagram (Social Media)", 750.00m, CategoriaProjet.Marketing);
                var pagtoAguardando = new Pagamento
                {
                    ContratoServicoId   = contratoB.Id,
                    ValorBruto          = 750.00m,
                    Status              = StatusPagamento.Aguardando,
                    Metodo              = MetodoPagamento.Pix,
                    Gateway             = "Asaas",
                    GatewayPagamentoId  = "pay_seed_b_aguardando",
                    AsaasClienteId      = "cus_seed_001",
                    StatusGateway       = "PENDING",
                    PixQrCodePayload    = "00020126580014BR.GOV.BCB.PIX0136seed-pix-uuid-fake-b52040000530398654067.505802BR5924Tratoo Seed6009SAO PAULO62140510seedb0000163041C4A",
                    PixQrCodeImagem     = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
                    PixQrCodeExpiracao  = DateTime.UtcNow.AddHours(23),
                    LiberacaoAutomaticaEm = DateTime.UtcNow.AddDays(37)
                };
                db.Pagamentos.Add(pagtoAguardando);
                await db.SaveChangesAsync();

                // ═══════════════════════════════════════════════════════════════════
                // Cenário C — Pagamento com status Retido (escrow ativo)
                // Endpoints: POST /api/pagamentos/{id}/liberar
                //            POST /api/pagamentos/{id}/disputar
                //            POST /api/pagamentos/{id}/estornar
                // ═══════════════════════════════════════════════════════════════════
                var contratoC    = await CriarContratoAtivoAsync("[SEED-C] Edição de 10 vídeos para YouTube", 1200.00m, CategoriaProjet.Video);
                var pagtoRetido  = new Pagamento
                {
                    ContratoServicoId   = contratoC.Id,
                    ValorBruto          = 1200.00m,
                    Status              = StatusPagamento.Retido,
                    Metodo              = MetodoPagamento.Pix,
                    Gateway             = "Asaas",
                    GatewayPagamentoId  = "pay_seed_c_retido",
                    AsaasClienteId      = "cus_seed_001",
                    StatusGateway       = "RECEIVED",
                    PagoEm              = DateTime.UtcNow.AddDays(-2),
                    LiberacaoAutomaticaEm = DateTime.UtcNow.AddDays(5)
                };
                db.Pagamentos.Add(pagtoRetido);
                await db.SaveChangesAsync();

                // Ledger do pagamento retido
                db.LedgerFinanceiro.AddRange(
                    new LedgerFinanceiro
                    {
                        PagamentoId       = pagtoRetido.Id,
                        Tipo              = TipoEntradaLedger.CobrancaPaga,
                        Valor             = 1200.00m,
                        Descricao         = "PIX recebido pelo contratante",
                        ReferenciaExterna = "pay_seed_c_retido",
                        CriadoPorId       = 0
                    },
                    new LedgerFinanceiro
                    {
                        PagamentoId       = pagtoRetido.Id,
                        Tipo              = TipoEntradaLedger.EscrowRetido,
                        Valor             = 1200.00m,
                        Descricao         = "Valor retido em escrow aguardando liberação",
                        ReferenciaExterna = "pay_seed_c_retido",
                        CriadoPorId       = 0
                    }
                );
                await db.SaveChangesAsync();

                // ═══════════════════════════════════════════════════════════════════
                // Cenário D — Pagamento EmDisputa com DisputaPagamento aberta
                // Endpoint: POST /api/pagamentos/{id}/disputas/{disputaId}/resolver
                // ═══════════════════════════════════════════════════════════════════
                var contratoD     = await CriarContratoAtivoAsync("[SEED-D] Dashboard de vendas em Power BI", 2000.00m, CategoriaProjet.Dados);
                var pagtoDisputa  = new Pagamento
                {
                    ContratoServicoId   = contratoD.Id,
                    ValorBruto          = 2000.00m,
                    Status              = StatusPagamento.EmDisputa,
                    Metodo              = MetodoPagamento.Pix,
                    Gateway             = "Asaas",
                    GatewayPagamentoId  = "pay_seed_d_disputa",
                    AsaasClienteId      = "cus_seed_001",
                    StatusGateway       = "RECEIVED",
                    PagoEm              = DateTime.UtcNow.AddDays(-4),
                    LiberacaoAutomaticaEm = DateTime.UtcNow.AddDays(3)
                };
                db.Pagamentos.Add(pagtoDisputa);
                await db.SaveChangesAsync();

                var disputa = new DisputaPagamento
                {
                    PagamentoId   = pagtoDisputa.Id,
                    AbertoPorId   = contratante.Id,
                    Status        = StatusDisputa.Aberta,
                    Motivo        = "Material entregue com qualidade inferior ao combinado. As artes finais não seguiram o briefing aprovado.",
                    AbertoEm      = DateTime.UtcNow.AddDays(-1),
                    EvidenciasJson = "[\"Print das artes entregues divergindo do briefing\",\"E-mail enviado ao prestador sem resposta\"]"
                };
                db.DisputasPagamento.Add(disputa);
                await db.SaveChangesAsync();

                // ── Retorna mapa completo para uso nos testes ─────────────────────
                return Results.Ok(new
                {
                    mensagem = "Seed de pagamentos criado com sucesso!",
                    instrucoes = "Faça login com as credenciais abaixo e use os IDs nos endpoints de pagamento.",
                    credenciais = new
                    {
                        contratante = new { email = SeedEmailContratante, senha = seedSenha, id = contratante.Id },
                        prestador   = new { email = SeedEmailPrestador,   senha = seedSenha, id = prestador.Id }
                    },
                    cenarios = new
                    {
                        A = new
                        {
                            descricao     = "Iniciar pagamento (chama Asaas Sandbox — retorna QR Code real)",
                            loginComo     = "contratante",
                            endpoint      = "POST /api/pagamentos/iniciar",
                            body          = new { contratoServicoId = contratoA.Id }
                        },
                        B = new
                        {
                            descricao     = "Pagamento AGUARDANDO — QR Code PIX fake já salvo",
                            loginComo     = "contratante ou prestador",
                            endpoints     = new[]
                            {
                                $"GET /api/pagamentos/{pagtoAguardando.Id}",
                                $"GET /api/pagamentos/{pagtoAguardando.Id}/pix"
                            },
                            pagamentoId   = pagtoAguardando.Id
                        },
                        C = new
                        {
                            descricao     = "Pagamento RETIDO — testar liberar / disputar / estornar",
                            loginComo     = "contratante",
                            endpoints     = new[]
                            {
                                $"POST /api/pagamentos/{pagtoRetido.Id}/liberar",
                                $"POST /api/pagamentos/{pagtoRetido.Id}/disputar",
                                $"POST /api/pagamentos/{pagtoRetido.Id}/estornar"
                            },
                            pagamentoId   = pagtoRetido.Id,
                            bodiesExemplo = new
                            {
                                liberar  = new { observacaoContratante = "Serviço entregue conforme acordado." },
                                disputar = new { motivo = "Qualidade abaixo do esperado.", evidencias = new[] { "Descrição da evidência" } }
                            }
                        },
                        D = new
                        {
                            descricao     = "Pagamento EM DISPUTA — testar resolver (admin)",
                            loginComo     = "qualquer usuário (admin no futuro)",
                            endpoint      = $"POST /api/pagamentos/{pagtoDisputa.Id}/disputas/{disputa.Id}/resolver",
                            pagamentoId   = pagtoDisputa.Id,
                            disputaId     = disputa.Id,
                            bodyExemplo   = new { decisao = "AguardandoResolucao", notaResolucao = "Análise concluída a favor do contratante." }
                        }
                    }
                });
            });
        }
    }
}
