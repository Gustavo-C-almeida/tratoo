using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Contratos
{
    public class ContratoServicoService : IContratoServicoService
    {
        private readonly IContratoServicoRepository _repo;
        private readonly IIdentidadeRepository _identidadeRepo;
        private readonly IContratanteRepository _contratanteRepo;
        private readonly IPrestadorRepository _prestadorRepo;
        private readonly IEmailService _emailService;
        private readonly IContratosPdfService _pdfService;
        private readonly IR2PrivateStorageService _privateStorage;
        private readonly IProjetoRepository _projetoRepo;
        private readonly IPropostaProjetoRepository _propostaRepo;
        private readonly IPagamentoService _pagamentoService;
        private readonly ILogger<ContratoServicoService> _logger;

        private const string TemplateVersaoAtual = "v1.0-2026-05";

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private static readonly JsonSerializerOptions _jsonOptsInsensitive = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public ContratoServicoService(
            IContratoServicoRepository repo,
            IIdentidadeRepository identidadeRepo,
            IContratanteRepository contratanteRepo,
            IPrestadorRepository prestadorRepo,
            IEmailService emailService,
            IContratosPdfService pdfService,
            IR2PrivateStorageService privateStorage,
            IProjetoRepository projetoRepo,
            IPropostaProjetoRepository propostaRepo,
            IPagamentoService pagamentoService,
            ILogger<ContratoServicoService> logger)
        {
            _repo = repo;
            _identidadeRepo = identidadeRepo;
            _contratanteRepo = contratanteRepo;
            _prestadorRepo = prestadorRepo;
            _emailService = emailService;
            _pdfService = pdfService;
            _privateStorage = privateStorage;
            _projetoRepo = projetoRepo;
            _propostaRepo = propostaRepo;
            _pagamentoService = pagamentoService;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // GERAR (chamado automaticamente ao aceitar proposta)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<ContratoServico> GerarAsync(PropostaProjeto proposta, PropostaVersao versao)
        {
            // Valida NivelVerificacao das duas partes
            var identidadeContratante = await _identidadeRepo.ObterPorUserIdAsync(proposta.Projeto.ContratanteId)
                ?? throw new NegocioException("Contratante precisa ter identidade verificada para gerar contrato.");

            if (identidadeContratante.NivelVerificacao < NivelVerificacao.Identidade)
                throw new NegocioException("Contratante precisa ter identidade verificada (CPF/CNPJ) para gerar contrato.");

            var identidadePrestador = await _identidadeRepo.ObterPorUserIdAsync(proposta.PrestadorId)
                ?? throw new NegocioException("Prestador precisa ter identidade verificada para gerar contrato.");

            if (identidadePrestador.NivelVerificacao < NivelVerificacao.Identidade)
                throw new NegocioException("Prestador precisa ter identidade verificada (CPF/CNPJ) para gerar contrato.");

            var contratante = await _contratanteRepo.GetCompletoAsync(proposta.Projeto.ContratanteId)
                ?? throw new NegocioException("Contratante não encontrado.");

            var prestador = await _prestadorRepo.GetCompletoAsync(proposta.PrestadorId)
                ?? throw new NegocioException("Prestador não encontrado.");

            ValidarDadosParaContrato(contratante, prestador, identidadeContratante, identidadePrestador);

            var conteudo = ConstruirConteudo(proposta, versao, contratante, prestador, identidadeContratante, identidadePrestador);
            var conteudoJson = JsonSerializer.Serialize(conteudo, _jsonOpts);

            var agora = DateTime.UtcNow;
            var contrato = new ContratoServico
            {
                ProjetoId = proposta.ProjetoId,
                PropostaId = proposta.Id,
                ContratanteId = proposta.Projeto.ContratanteId,
                PrestadorId = proposta.PrestadorId,
                ConteudoJson = conteudoJson,
                Status = ContratoServicoStatus.Gerado,
                TemplateVersao = TemplateVersaoAtual,
                CriadoEm = agora,
                ExpiraEm = agora.AddDays(7)
            };

            await _repo.AddAsync(contrato);
            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Contrato {ContratoId} gerado para projeto {ProjetoId}, proposta {PropostaId}. Expira em {ExpiraEm:u}.",
                contrato.Id, contrato.ProjetoId, contrato.PropostaId, contrato.ExpiraEm);

            // Notifica ambas as partes
            try
            {
                await _emailService.EnviarContratoGeradoAsync(
                    contratante.Email, contratante.Nome, proposta.Projeto.Titulo);
                await _emailService.EnviarContratoGeradoAsync(
                    prestador.Email, prestador.Nome, proposta.Projeto.Titulo);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enviar e-mail de contrato gerado {ContratoId}.", contrato.Id);
            }

            return contrato;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ASSINAR
        // ─────────────────────────────────────────────────────────────────────────
        public async Task AssinarAsync(Guid contratoId, int usuarioId, string ip)
        {
            var contrato = await _repo.GetByIdAsync(contratoId)
                ?? throw new NegocioException("Contrato não encontrado.");

            var ehContratante = contrato.ContratanteId == usuarioId;
            var ehPrestador = contrato.PrestadorId == usuarioId;

            if (!ehContratante && !ehPrestador)
                throw new NegocioException("Você não é parte deste contrato.");

            if (contrato.Status == ContratoServicoStatus.Ativo)
                throw new NegocioException("Este contrato já está ativo — ambas as partes já assinaram.");

            if (contrato.Status == ContratoServicoStatus.Cancelado || contrato.Status == ContratoServicoStatus.Encerrado)
                throw new NegocioException("Este contrato não está mais disponível para assinatura.");

            if (contrato.ExpiraEm < DateTime.UtcNow)
                throw new NegocioException("O prazo para assinar este contrato expirou.");

            // Verifica se já assinou
            if (ehContratante && contrato.AssinadoContratanteEm.HasValue)
                throw new NegocioException("Você já assinou este contrato.");
            if (ehPrestador && contrato.AssinadoPrestadorEm.HasValue)
                throw new NegocioException("Você já assinou este contrato.");

            var agora = DateTime.UtcNow;
            var primeiraAssinatura = contrato.Status == ContratoServicoStatus.Gerado;

            if (ehContratante)
            {
                contrato.AssinadoContratanteEm = agora;
                contrato.IpContratante = ip;
            }
            else
            {
                contrato.AssinadoPrestadorEm = agora;
                contrato.IpPrestador = ip;
            }

            if (primeiraAssinatura)
            {
                // Congela hash do conteúdo na primeira assinatura — prova integridade (MP 2.200-2/2001)
                contrato.ConteudoHash = ComputarHash(contrato.ConteudoJson);
                contrato.Status = ContratoServicoStatus.AguardandoAssinatura;
            }

            var ambosSinaram = contrato.AssinadoContratanteEm.HasValue && contrato.AssinadoPrestadorEm.HasValue;
            if (ambosSinaram)
            {
                contrato.Status = ContratoServicoStatus.Ativo;

                // Cria snapshot imutável dos dados das partes
                var contratante = await _contratanteRepo.GetCompletoAsync(contrato.ContratanteId);
                var prestador = await _prestadorRepo.GetCompletoAsync(contrato.PrestadorId);
                var identContratante = await _identidadeRepo.ObterPorUserIdAsync(contrato.ContratanteId);
                var identPrestador = await _identidadeRepo.ObterPorUserIdAsync(contrato.PrestadorId);

                var snapshot = new ContratoSnapshot
                {
                    ContratoId = contrato.Id,
                    DadosContratante = JsonSerializer.Serialize(ConstruirParteSnapshot(contratante!, identContratante!), _jsonOpts),
                    DadosPrestador = JsonSerializer.Serialize(ConstruirParteSnapshot(prestador!, identPrestador!), _jsonOpts),
                    ConteudoFinal = contrato.ConteudoJson,
                    CongeladoEm = agora
                };

                await _repo.AddSnapshotAsync(snapshot);

                // Gera PDF e armazena no bucket privado
                try
                {
                    using var pdfStream = await _pdfService.GerarAsync(contrato, snapshot);
                    var chave = $"contratos/{contrato.Id}/{contrato.Id}.pdf";
                    await _privateStorage.UploadAsync(pdfStream, chave, "application/pdf");
                    contrato.PdfKey = chave;
                    _logger.LogInformation("PDF gerado e armazenado para contrato {ContratoId}.", contrato.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha ao gerar/armazenar PDF do contrato {ContratoId}.", contrato.Id);
                }

                // Notifica ambas as partes
                try
                {
                    if (contratante != null)
                        await _emailService.EnviarContratoAtivoAsync(contratante.Email, contratante.Nome);
                    if (prestador != null)
                        await _emailService.EnviarContratoAtivoAsync(prestador.Email, prestador.Nome);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao notificar partes sobre contrato ativo {ContratoId}.", contrato.Id);
                }

                _logger.LogInformation(
                    "Contrato {ContratoId} ativado. Ambas as partes assinaram.", contrato.Id);
            }
            else
            {
                _logger.LogInformation(
                    "Primeira assinatura registrada no contrato {ContratoId} por usuário {UsuarioId}.",
                    contrato.Id, usuarioId);

                // Notifica a outra parte que precisa assinar
                try
                {
                    if (ehContratante)
                    {
                        var prestador = await _prestadorRepo.GetCompletoAsync(contrato.PrestadorId);
                        if (prestador != null)
                            await _emailService.EnviarSolicitacaoAssinaturaAsync(prestador.Email, prestador.Nome);
                    }
                    else
                    {
                        var contratante = await _contratanteRepo.GetCompletoAsync(contrato.ContratanteId);
                        if (contratante != null)
                            await _emailService.EnviarSolicitacaoAssinaturaAsync(contratante.Email, contratante.Nome);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao notificar solicitação de assinatura para contrato {ContratoId}.", contrato.Id);
                }
            }

            await _repo.SaveChangesAsync();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // OBTER DETALHE
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<ContratoDetalheDto?> ObterDetalheAsync(Guid contratoId, int usuarioId)
        {
            var contrato = await _repo.GetByIdAsync(contratoId);
            if (contrato == null) return null;

            var ehParte = contrato.ContratanteId == usuarioId || contrato.PrestadorId == usuarioId;
            if (!ehParte)
                throw new NegocioException("Você não tem permissão para ver este contrato.");

            return MapDetalhe(contrato, usuarioId);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LISTAR (por usuário)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<List<ContratoResumoDto>> ListarDoContratanteAsync(int contratanteId)
        {
            var contratos = await _repo.GetDoContratanteAsync(contratanteId);
            return contratos.Select(c => MapResumo(c, contratanteId)).ToList();
        }

        public async Task<List<ContratoResumoDto>> ListarDoPrestadorAsync(int prestadorId)
        {
            var contratos = await _repo.GetDoPrestadorAsync(prestadorId);
            return contratos.Select(c => MapResumo(c, prestadorId)).ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // URL DO PDF (URL pré-assinada, válida por 15 minutos)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<string> ObterUrlPdfAsync(Guid contratoId, int usuarioId)
        {
            var contrato = await _repo.GetByIdAsync(contratoId)
                ?? throw new NegocioException("Contrato não encontrado.");

            var ehParte = contrato.ContratanteId == usuarioId || contrato.PrestadorId == usuarioId;
            if (!ehParte)
                throw new NegocioException("Você não tem permissão para acessar este documento.");

            if (string.IsNullOrWhiteSpace(contrato.PdfKey))
                throw new NegocioException("O PDF deste contrato ainda não está disponível.");

            return await _privateStorage.GerarUrlAssinadaAsync(contrato.PdfKey, TimeSpan.FromMinutes(15));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // REGISTRAR ENTREGA (apenas prestador, contrato Ativo)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task RegistrarEntregaAsync(Guid contratoId, int prestadorId)
        {
            var contrato = await _repo.GetByIdAsync(contratoId)
                ?? throw new NegocioException("Contrato não encontrado.");

            if (contrato.PrestadorId != prestadorId)
                throw new NegocioException("Apenas o prestador pode registrar a entrega.");

            if (contrato.Status != ContratoServicoStatus.Ativo)
                throw new NegocioException("A entrega só pode ser registrada em contratos ativos.");

            if (contrato.EntregaRegistradaEm.HasValue)
                throw new NegocioException("A entrega já foi registrada para este contrato.");

            contrato.EntregaRegistradaEm = DateTime.UtcNow;
            await _repo.SaveChangesAsync();

            _logger.LogInformation("Entrega registrada no contrato {ContratoId} pelo prestador {PrestadorId}.",
                contratoId, prestadorId);

            // Notifica o contratante
            try
            {
                var contratante = await _contratanteRepo.GetCompletoAsync(contrato.ContratanteId);
                if (contratante != null)
                    await _emailService.EnviarSolicitacaoAssinaturaAsync(
                        contratante.Email, contratante.Nome);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao notificar contratante sobre entrega do contrato {ContratoId}.", contratoId);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CANCELAR CONTRATO
        // ─────────────────────────────────────────────────────────────────────────
        public async Task CancelarAsync(Guid contratoId, int usuarioId, string? motivo)
        {
            var contrato = await _repo.GetByIdAsync(contratoId)
                ?? throw new NegocioException("Contrato não encontrado.");

            var ehContratante = contrato.ContratanteId == usuarioId;
            var ehPrestador   = contrato.PrestadorId   == usuarioId;

            if (!ehContratante && !ehPrestador)
                throw new NegocioException("Você não tem permissão para cancelar este contrato.");

            if (contrato.Status == ContratoServicoStatus.Cancelado ||
                contrato.Status == ContratoServicoStatus.Encerrado)
                throw new NegocioException($"Contrato já está {contrato.Status.ToString().ToLower()} e não pode ser cancelado.");

            // ── Situação 3: entrega registrada → disputa obrigatória ────────────
            if (contrato.EntregaRegistradaEm.HasValue)
                throw new NegocioException(
                    "Há uma entrega registrada neste contrato. Para solicitar cancelamento, abra uma disputa — " +
                    "nossa equipe irá analisar e decidir sobre o reembolso.");

            // ── Situação 2: ativo sem entrega → 5% taxa + 95% reembolso ────────
            if (contrato.Status == ContratoServicoStatus.Ativo)
            {
                await _pagamentoService.EstornarCancelamentoContratoAsync(contratoId, usuarioId);
            }
            // ── Situação 1: gerado/aguardando assinatura → cancelamento gratuito
            else if (contrato.Status == ContratoServicoStatus.Gerado ||
                     contrato.Status == ContratoServicoStatus.AguardandoAssinatura)
            {
                // Proposta volta a estar disponível e projeto reabre
                var proposta = await _propostaRepo.GetByIdAsync(contrato.PropostaId);
                if (proposta != null)
                {
                    proposta.Status = StatusPropostaProjeto.Recusada;
                    proposta.MotivoCancelamento = "Contrato cancelado antes da assinatura.";
                    proposta.CanceladoPorId = usuarioId;
                    proposta.CanceladoEm = DateTime.UtcNow;
                    proposta.AtualizadoEm = DateTime.UtcNow;
                }

                var projeto = await _projetoRepo.GetByIdAsync(contrato.ProjetoId);
                if (projeto != null)
                {
                    projeto.Status = StatusProjeto.Aberto;
                    projeto.FreelancerSelecionadoId = null;
                    projeto.AtualizadoEm = DateTime.UtcNow;
                }
            }
            else
            {
                throw new NegocioException($"Contrato no status '{contrato.Status}' não pode ser cancelado.");
            }

            contrato.Status = ContratoServicoStatus.Cancelado;
            contrato.MotivoCancelamento = motivo?.Trim();
            contrato.CanceladoPorId = usuarioId;
            contrato.CanceladoEm = DateTime.UtcNow;

            await _repo.SaveChangesAsync();

            _logger.LogInformation(
                "Contrato {ContratoId} cancelado por usuário {UsuarioId}. Motivo: {Motivo}",
                contratoId, usuarioId, motivo ?? "(sem motivo)");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // EXPIRAR (chamado pelo BackgroundService)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task ExpirarContratosAsync()
        {
            var expirados = await _repo.GetExpiradosAsync(DateTime.UtcNow);
            if (expirados.Count == 0) return;

            foreach (var contrato in expirados)
            {
                contrato.Status = ContratoServicoStatus.Cancelado;
                contrato.MotivoCancelamento = "Contrato expirado — prazo de 7 dias para assinatura não foi cumprido.";
                contrato.CanceladoEm = DateTime.UtcNow;

                // Reverte proposta e reabre projeto (mesmo comportamento do cancelamento gratuito)
                try
                {
                    var proposta = await _propostaRepo.GetByIdAsync(contrato.PropostaId);
                    if (proposta != null)
                    {
                        proposta.Status = StatusPropostaProjeto.Recusada;
                        proposta.MotivoCancelamento = "Contrato expirado sem assinatura.";
                        proposta.CanceladoEm = DateTime.UtcNow;
                        proposta.AtualizadoEm = DateTime.UtcNow;
                    }

                    var projeto = await _projetoRepo.GetByIdAsync(contrato.ProjetoId);
                    if (projeto != null)
                    {
                        projeto.Status = StatusProjeto.Aberto;
                        projeto.FreelancerSelecionadoId = null;
                        projeto.AtualizadoEm = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao reverter proposta/projeto ao expirar contrato {ContratoId}.", contrato.Id);
                }
            }

            await _repo.SaveChangesAsync();

            _logger.LogInformation("{Total} contrato(s) expirado(s) e cancelado(s).", expirados.Count);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers privados
        // ─────────────────────────────────────────────────────────────────────────
        private static void ValidarDadosParaContrato(
            Contratante contratante,
            Domain.Models.Prestador.Prestador prestador,
            UserIdentity identContratante,
            UserIdentity identPrestador)
        {
            if (contratante.Endereco == null ||
                string.IsNullOrWhiteSpace(contratante.Endereco.Cidade) ||
                string.IsNullOrWhiteSpace(contratante.Endereco.Estado))
                throw new NegocioException("Contratante precisa ter endereço completo (cidade e estado) cadastrado antes de assinar contrato.");

            if (prestador.Endereco == null ||
                string.IsNullOrWhiteSpace(prestador.Endereco.Cidade) ||
                string.IsNullOrWhiteSpace(prestador.Endereco.Estado))
                throw new NegocioException("Prestador precisa ter endereço completo (cidade e estado) cadastrado antes de assinar contrato.");
        }

        private static ContratoConteudoDto ConstruirConteudo(
            PropostaProjeto proposta,
            PropostaVersao versao,
            Contratante contratante,
            Domain.Models.Prestador.Prestador prestador,
            UserIdentity identContratante,
            UserIdentity identPrestador)
        {
            var cpfCnpjContratante = MascararDocumento(DataProtector.Decrypt(identContratante.CpfCnpjCriptografado));
            var cpfCnpjPrestador = MascararDocumento(DataProtector.Decrypt(identPrestador.CpfCnpjCriptografado));

            var enderecoContratante = FormatarEndereco(contratante.Endereco);
            var enderecoPrestador = FormatarEndereco(prestador.Endereco);

            // Parseia marcos, se existirem
            var marcos = new List<ContratoMarcoDto>();
            if (!string.IsNullOrWhiteSpace(versao.MarcosJson))
            {
                try
                {
                    var marcosRaw = JsonSerializer.Deserialize<List<JsonElement>>(versao.MarcosJson);
                    if (marcosRaw != null)
                    {
                        foreach (var m in marcosRaw)
                        {
                            marcos.Add(new ContratoMarcoDto
                            {
                                Descricao = m.TryGetProperty("descricao", out var d) ? d.GetString() ?? "" : "",
                                Prazo = m.TryGetProperty("prazo", out var p) ? p.GetDateTime() : proposta.ValidoAte,
                                Valor = m.TryGetProperty("valor", out var v) ? v.GetDecimal() : 0
                            });
                        }
                    }
                }
                catch { }
            }

            // Foro dinâmico baseado no estado do contratante (fallback para SP)
            var estadoContratante = contratante.Endereco?.Estado?.Trim().ToUpperInvariant();
            var foro = !string.IsNullOrWhiteSpace(estadoContratante)
                ? $"Comarca de {contratante.Endereco!.Cidade} - {estadoContratante}"
                : "Comarca de São Paulo - SP";

            return new ContratoConteudoDto
            {
                Contratante = new ContratoParteDto
                {
                    Nome = identContratante.NomeLegal,
                    CpfCnpjMascarado = cpfCnpjContratante,
                    Email = contratante.Email,
                    Endereco = enderecoContratante
                },
                Prestador = new ContratoParteDto
                {
                    Nome = identPrestador.NomeLegal,
                    CpfCnpjMascarado = cpfCnpjPrestador,
                    Email = prestador.Email,
                    Endereco = enderecoPrestador
                },
                Objeto = versao.Objetivo,
                Escopo = new ContratoEscopoDto
                {
                    Entregaveis = versao.Escopo,
                    Revisoes = versao.RevisoesInclusas,
                    FormatoEntrega = versao.Observacoes ?? "A definir pelas partes.",
                    OQueNaoEstaIncluso = versao.Exclusoes ?? "Nenhuma exclusão específica informada."
                },
                Prazo = new ContratoPrazoDto
                {
                    DataInicio = DateTime.UtcNow.Date,
                    DataTermino = versao.PrazoTotal,
                    Marcos = marcos
                },
                Pagamento = new ContratoPagamentoDto
                {
                    ValorTotal = versao.ValorTotal,
                    Entrada = versao.Entrada ?? 0,
                    FormaPagamento = versao.FormaPagamento
                },
                Foro = foro
            };
        }

        private static object ConstruirParteSnapshot(Usuario usuario, UserIdentity identidade)
        {
            return new
            {
                nomeLegal = identidade.NomeLegal,
                cpfCnpjMascarado = MascararDocumento(DataProtector.Decrypt(identidade.CpfCnpjCriptografado)),
                email = usuario.Email,
                endereco = FormatarEndereco(usuario.Endereco)
            };
        }

        private static string MascararDocumento(string doc)
        {
            // Remove formatação, mantém apenas dígitos
            var nums = new string(doc.Where(char.IsDigit).ToArray());

            // CPF: ***.***. XX-** (mostra apenas os 2 dígitos antes do traço, posições 7-8)
            // CNPJ: **.***.***/****-XX (mostra apenas os 2 dígitos verificadores)
            return nums.Length == 11
                ? $"***.***. {nums[7..9]}-**"       // CPF: posições 7-8 (antes do traço)
                : $"**.***.***/****-{nums[^2..]}";  // CNPJ: últimos 2 dígitos
        }

        private static string FormatarEndereco(Endereco? e)
        {
            if (e == null) return string.Empty;
            var partes = new[] { e.Logradouro, e.Numero, e.Bairro, e.Cidade, e.Estado, e.Cep }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            return string.Join(", ", partes);
        }

        private static string ComputarHash(string json)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static ContratoDetalheDto MapDetalhe(ContratoServico c, int usuarioId)
        {
            var conteudo = JsonSerializer.Deserialize<ContratoConteudoDto>(c.ConteudoJson, _jsonOptsInsensitive)
                ?? new ContratoConteudoDto();

            var ehContratante = c.ContratanteId == usuarioId;
            var assinadoPorMim = ehContratante ? c.AssinadoContratanteEm.HasValue : c.AssinadoPrestadorEm.HasValue;
            var assinadoPelaOutraParte = ehContratante ? c.AssinadoPrestadorEm.HasValue : c.AssinadoContratanteEm.HasValue;

            var cancelavel = c.Status is ContratoServicoStatus.Gerado
                                           or ContratoServicoStatus.AguardandoAssinatura
                                           or ContratoServicoStatus.Ativo
                          && !c.EntregaRegistradaEm.HasValue;

            return new ContratoDetalheDto
            {
                Id = c.Id,
                ProjetoId = c.ProjetoId,
                ProjetoTitulo = c.Projeto?.Titulo ?? string.Empty,
                PropostaId = c.PropostaId,
                ContratanteId = c.ContratanteId,
                ContratanteNome = c.Contratante?.Nome ?? string.Empty,
                PrestadorId = c.PrestadorId,
                PrestadorNome = c.Prestador?.Nome ?? string.Empty,
                Status = c.Status,
                Conteudo = conteudo,
                AssinadoContratanteEm = c.AssinadoContratanteEm,
                AssinadoPrestadorEm = c.AssinadoPrestadorEm,
                ConteudoHash = c.ConteudoHash,
                CriadoEm = c.CriadoEm,
                ExpiraEm = c.ExpiraEm,
                AssinadoPorMim = assinadoPorMim,
                AssinadoPelaOutraParte = assinadoPelaOutraParte,
                TemPdf = !string.IsNullOrWhiteSpace(c.PdfKey),
                EntregaRegistradaEm = c.EntregaRegistradaEm,
                TemEntregaRegistrada = c.EntregaRegistradaEm.HasValue,
                PodeCancelar = cancelavel,
                MotivoCancelamento = c.MotivoCancelamento,
                CanceladoEm = c.CanceladoEm,
                TemplateVersao = c.TemplateVersao
            };
        }

        private static ContratoResumoDto MapResumo(ContratoServico c, int usuarioId)
        {
            decimal valorTotal = 0;
            DateTime dataTermino = DateTime.MinValue;

            try
            {
                var conteudo = JsonSerializer.Deserialize<ContratoConteudoDto>(c.ConteudoJson, _jsonOptsInsensitive);
                valorTotal = conteudo?.Pagamento?.ValorTotal ?? 0;
                dataTermino = conteudo?.Prazo?.DataTermino ?? DateTime.MinValue;
            }
            catch { /* ConteudoJson malformado — usa defaults */ }

            var ehContratante = c.ContratanteId == usuarioId;
            var assinadoPorMim = ehContratante ? c.AssinadoContratanteEm.HasValue : c.AssinadoPrestadorEm.HasValue;
            var assinadoPelaOutraParte = ehContratante ? c.AssinadoPrestadorEm.HasValue : c.AssinadoContratanteEm.HasValue;

            return new ContratoResumoDto
            {
                Id = c.Id,
                ProjetoId = c.ProjetoId,
                ProjetoTitulo = c.Projeto?.Titulo ?? string.Empty,
                ContratanteNome = c.Contratante?.Nome ?? string.Empty,
                PrestadorNome = c.Prestador?.Nome ?? string.Empty,
                Status = c.Status,
                ValorTotal = valorTotal,
                DataTermino = dataTermino,
                CriadoEm = c.CriadoEm,
                ExpiraEm = c.ExpiraEm,
                AssinadoPorMim = assinadoPorMim,
                AssinadoPelaOutraParte = assinadoPelaOutraParte
            };
        }
    }
}
